using NetUtils;
using Server.Algorithm;
using Server.API;
using Server.Data;
using Server.Group;
using Server.Group.GroupSessions;
using Server.Infrastructure;
using Server.UserRouting;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using static Server.Data.AppDbContext;

namespace Server.Presentation.Handlers;

/// <summary>
/// Handler del lobby — capa de presentación TCP para grupos.
///
/// POR QUÉ EXISTE ESTA CLASE:
/// Antes, toda la lógica de LobbyGroup(), SendRouteResult(),
/// SendPendingResult() y SendErrorResult() estaba como métodos
/// estáticos en Program.cs, mezclada con la autenticación y el bootstrap.
///
/// LobbyHandler tiene UNA responsabilidad: manejar el protocolo TCP
/// para todo lo relacionado con el ciclo de vida de un grupo:
/// crear, unirse, lobby loop, y cálculo de ruta.
///
/// FLUJO DE VIDA DE UN GRUPO (desde perspectiva TCP):
///
/// PrepareCreateGroupAsync() → crea sesión en memoria + BBDD
///     ↓
/// RunLobbyLoopAsync() → bucle: envía cabecera, espera opción del cliente
///     ├── Refresh → vuelve al bucle
///     ├── Exit    → elimina miembro, sale del bucle
///     ├── Start   → arranca el grupo, sigue en bucle
///     ├── SendLocation → registra ubicación, si todas recibidas → SendRouteResultAsync() → sale
///     └── PollResult   → si todas recibidas → SendRouteResultAsync() → sale
///
/// GANANCIA:
/// → Program.cs ya no tiene 700 líneas. Es solo bootstrap + orquestación.
/// → Todo lo relacionado con TCP de grupos está en un solo archivo.
/// → SendRouteResult era 150 líneas estáticas en Program.cs; ahora es un método
///   de instancia con sus dependencias inyectadas y nombradas.
/// </summary>
public sealed class LobbyHandler
{
    // Dependencias inyectadas — no usamos campos estáticos globales
    private readonly GroupSessionManager _sessionManager;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _otpSemaphore;
    private readonly string _connectionString;

    // Enums del protocolo de lobby — mismos valores que el cliente MAUI
    // Se definen aquí para no depender del enum de Program.cs
    private enum LobbyOpt
    {
        Refresh = 1,
        Exit = 2,
        Start = 3,
        SendLocation = 4,
        PollResult = 5
    }

    public LobbyHandler(
        GroupSessionManager sessionManager,
        HttpClient httpClient,
        SemaphoreSlim otpSemaphore,
        string connectionString)
    {
        _sessionManager = sessionManager;
        _httpClient = httpClient;
        _otpSemaphore = otpSemaphore;
        _connectionString = connectionString;
    }

    // ═══════════════════════════════════════════════════════════════════
    // PREPARACIÓN — antes de entrar al lobby
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Prepara la creación de un grupo:
    /// 1. Lee nombre, label, descripción, método del socket.
    /// 2. Persiste en BBDD via CreateGroupService.
    /// 3. Crea la sesión en memoria.
    ///
    /// Devuelve el groupCode si todo fue bien, null si falló.
    /// Program.cs usa el groupCode devuelto para:
    ///   a) Llamar a RunLobbyAsync()
    ///   b) Gestionar la limpieza en el finally si hay desconexión
    ///
    /// POR QUÉ SEPARAMOS PREPARACIÓN DE LOOP:
    /// Program.cs necesita saber el groupCode ANTES de entrar al loop
    /// para poder limpiar la sesión en el finally si el cliente se
    /// desconecta mientras estamos en el lobby.
    /// Si el groupCode estuviera encapsulado dentro del loop,
    /// el finally de Program.cs no podría hacer la limpieza.
    /// </summary>
    public async Task<string?> PrepareCreateGroupAsync(Socket socket, User user)
    {
        AppLogger.Info("LobbyHandler", $"[User:{user.username}] Preparando creación de grupo...");

        using AppDbContext context = new AppDbContext(_connectionString);
        var createService = new CreateGroupService(context);

        var result = await createService.ExecuteAsync(socket, user);

        if (!result.Success)
        {
            AppLogger.Warn("LobbyHandler", $"[User:{user.username}] No se pudo crear el grupo (CreateGroupService falló).");
            return null;
        }

        // Crear sesión en memoria — independiente de la BBDD
        var session = new GroupSession(result.GroupId, result.GroupCode, user.id);
        session.AddMember(user.id, user.username);
        _sessionManager.Add(session);

        AppLogger.Info("LobbyHandler",
            $"[Group:{result.GroupCode}] [User:{user.username}] Sesión creada. Owner registrado.");

        return result.GroupCode;
    }

    /// <summary>
    /// Prepara la unión a un grupo existente:
    /// 1. Lee el groupCode del socket.
    /// 2. Intenta añadir el usuario a la sesión en memoria.
    /// 3. Responde al cliente si fue exitoso.
    ///
    /// Devuelve el groupCode si el join fue exitoso, null si falló.
    /// Mismo patrón que PrepareCreateGroupAsync — ver comentario ahí.
    /// </summary>
    public async Task<string?> PrepareJoinGroupAsync(Socket socket, User user)
    {
        string groupCode = SocketTools.receiveString(socket);

        AppLogger.Info("LobbyHandler", $"[Group:{groupCode}] [User:{user.username}] Intentando unirse...");

        bool success = _sessionManager.TryJoinGroup(groupCode, user.id, user.username);
        SocketTools.sendBool(socket, success);

        if (!success)
        {
            AppLogger.Warn("LobbyHandler",
                $"[Group:{groupCode}] Join fallido — grupo no encontrado o ya iniciado.");
            return null;
        }

        AppLogger.Info("LobbyHandler", $"[Group:{groupCode}] [User:{user.username}] Unido correctamente.");

        // Necesitamos el await para que la firma sea async Task<string?>
        // y sea consistente con PrepareCreateGroupAsync (ambos usan await Task.Run en sus deps).
        await Task.CompletedTask;
        return groupCode;
    }

    // ═══════════════════════════════════════════════════════════════════
    // BUCLE DEL LOBBY
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bucle principal del lobby para un usuario en un grupo.
    ///
    /// PROTOCOLO DE CABECERA:
    /// Al inicio de CADA iteración el servidor envía:
    ///   → [bool]   sessionValid  (false si la sesión ya no existe)
    ///   → [int]    memberCount
    ///   → [bool]   hasStarted
    ///
    /// Después el servidor espera la opción del cliente:
    ///   ← [int]    option (Refresh=1, Exit=2, Start=3, SendLocation=4, PollResult=5)
    ///
    /// El método termina (return) cuando:
    ///   - El cliente sale voluntariamente (Exit)
    ///   - Se calcula y envía la ruta (SendLocation/PollResult con todas las ubicaciones)
    ///   - La sesión ya no existe (servidor reiniciado, grupo eliminado)
    ///   - SocketException (cliente desconectado)
    ///
    /// Cuando termina, el control vuelve a Program.cs que limpia activeGroupCode
    /// y hace BREAK para continuar atendiendo al mismo cliente. ← EL FIX DEL BUG
    /// </summary>
    public async Task RunLobbyAsync(Socket socket, string groupCode, User user)
    {
        AppLogger.Info("LobbyHandler", $"[Group:{groupCode}] [User:{user.username}] Entrando al bucle del lobby.");

        while (true)
        {
            GroupSession? session = _sessionManager.Get(groupCode);

            // Si la sesión desapareció (p.ej. todos salieron, servidor reinició),
            // notificamos al cliente y salimos del bucle.
            if (session == null)
            {
                SocketTools.sendBool(socket, false); // sessionValid = false
                AppLogger.Warn("LobbyHandler",
                    $"[Group:{groupCode}] [User:{user.username}] Sesión no encontrada. Terminando lobby.");
                return;
            }

            // ── Cabecera estándar ────────────────────────────────────────────
            // El cliente SIEMPRE lee estos tres valores antes de enviar su opción.
            // Este contrato es lo que mantiene el protocolo sincronizado.
            SocketTools.sendBool(socket, true);                    // sessionValid
            SocketTools.sendInt(socket, session.MemberCount);      // memberCount
            SocketTools.sendBool(socket, session.HasStarted);      // hasStarted

            // ── Leer opción del cliente ──────────────────────────────────────
            int option;
            try
            {
                option = SocketTools.receiveInt(socket);
            }
            catch (SocketException)
            {
                // Desconexión detectada durante la espera de opción.
                // No es un error, es un caso normal (el usuario cerró la app).
                // El finally de Program.cs limpiará la sesión.
                AppLogger.Warn("LobbyHandler",
                    $"[Group:{groupCode}] [User:{user.username}] Desconexión detectada en lobby.");
                return;
            }

            // ── Procesar opción ──────────────────────────────────────────────
            switch (option)
            {
                case (int)LobbyOpt.Refresh:
                    // El cliente solo quiere los datos actualizados (ya enviados en la cabecera).
                    // No hay que hacer nada más, el bucle continúa.
                    AppLogger.Debug("LobbyHandler",
                        $"[Group:{groupCode}] [User:{user.username}] Refresh. " +
                        $"Members={session.MemberCount}, Started={session.HasStarted}");
                    break;

                case (int)LobbyOpt.Exit:
                    // Salida voluntaria — eliminar miembro y terminar lobby.
                    HandleExit(groupCode, session, user);
                    return; // ← Sale del bucle (el socket sigue abierto en Program.cs)

                case (int)LobbyOpt.Start:
                    // Intentar iniciar el grupo (solo el owner puede).
                    HandleStart(socket, groupCode, session, user);
                    break; // ← El bucle continúa — cliente seguirá en lobby hasta enviar ubicación

                case (int)LobbyOpt.SendLocation:
                    // Registrar ubicación. Si todas recibidas, calcula ruta y termina el lobby.
                    bool locationDone = await HandleSendLocationAsync(socket, groupCode, session, user);
                    if (locationDone)
                        return; // ← Lobby terminado — control vuelve a Program.cs
                    break;

                case (int)LobbyOpt.PollResult:
                    // Consultar si ya se tienen todas las ubicaciones.
                    bool pollDone = await HandlePollResultAsync(socket, groupCode, session, user);
                    if (pollDone)
                        return; // ← Lobby terminado — control vuelve a Program.cs
                    break;

                default:
                    AppLogger.Warn("LobbyHandler",
                        $"[Group:{groupCode}] [User:{user.username}] Opción de lobby desconocida: {option}");
                    break;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // HANDLERS INTERNOS DEL LOBBY
    // ═══════════════════════════════════════════════════════════════════

    private void HandleExit(string groupCode, GroupSession session, User user)
    {
        session.RemoveMember(user.id);
        AppLogger.Info("LobbyHandler", $"[Group:{groupCode}] [User:{user.username}] Salida voluntaria.");

        // Si era el último miembro, eliminar la sesión del manager
        if (session.MemberCount == 0)
        {
            _sessionManager.Remove(groupCode);
            AppLogger.Info("LobbyHandler", $"[Group:{groupCode}] Sesión eliminada — sin miembros.");
        }
    }

    private void HandleStart(Socket socket, string groupCode, GroupSession session, User user)
    {
        // Solo el owner puede iniciar el grupo
        if (user.id != session.OwnerUserId)
        {
            AppLogger.Warn("LobbyHandler",
                $"[Group:{groupCode}] [User:{user.username}] Intento de Start sin ser owner. Rechazado.");
            SocketTools.sendBool(socket, false);
            return;
        }

        // session.Start() devuelve false si el grupo ya estaba iniciado
        bool started = session.Start();
        SocketTools.sendBool(socket, started);

        AppLogger.Info("LobbyHandler", started
            ? $"[Group:{groupCode}] Grupo iniciado por [User:{user.username}]."
            : $"[Group:{groupCode}] El grupo ya estaba iniciado.");
    }

    /// <summary>
    /// Procesa la opción SendLocation.
    /// Devuelve true si el lobby debe terminar (ruta calculada y enviada).
    /// Devuelve false si el lobby debe continuar (esperando más ubicaciones).
    /// </summary>
    private async Task<bool> HandleSendLocationAsync(
        Socket socket, string groupCode, GroupSession session, User user)
    {
        // Guardia: el grupo debe haber sido iniciado antes de enviar ubicación
        if (!session.HasStarted)
        {
            AppLogger.Warn("LobbyHandler",
                $"[Group:{groupCode}] [User:{user.username}] Ubicación enviada antes de Start. Ignorado.");
            SendErrorResult(socket, -2, "El grupo aún no se ha iniciado.");
            return false;
        }

        // ReceiveLocationService lee lat/lon del socket y los guarda en la sesión
        var locationService = new ReceiveLocationService();
        bool allReceived = locationService.Execute(socket, session, user);

        if (!allReceived)
        {
            // Faltan ubicaciones de otros miembros — responder "pendiente"
            AppLogger.Info("LobbyHandler",
                $"[Group:{groupCode}] [User:{user.username}] Ubicación registrada. " +
                $"Progreso: {session.GetAllLocations().Count}/{session.MemberCount}");
            SendPendingResult(socket);
            return false; // El lobby continúa con PollResult
        }

        // Todas las ubicaciones recibidas — calcular y enviar ruta
        AppLogger.Info("LobbyHandler",
            $"[Group:{groupCode}] Todas las ubicaciones listas. Calculando ruta individual para [User:{user.username}].");
        await SendRouteResultAsync(socket, session, user);
        return true; // Lobby terminado
    }

    /// <summary>
    /// Procesa la opción PollResult.
    /// Devuelve true si el lobby debe terminar (ruta calculada).
    /// Devuelve false si aún faltan ubicaciones.
    /// </summary>
    private async Task<bool> HandlePollResultAsync(
        Socket socket, string groupCode, GroupSession session, User user)
    {
        if (!session.AreAllLocationsReceived())
        {
            AppLogger.Debug("LobbyHandler",
                $"[Group:{groupCode}] [User:{user.username}] PollResult: faltan ubicaciones. " +
                $"Progreso: {session.GetAllLocations().Count}/{session.MemberCount}");
            SendPendingResult(socket);
            return false;
        }

        AppLogger.Info("LobbyHandler",
            $"[Group:{groupCode}] [User:{user.username}] PollResult: todas listas. Calculando ruta.");
        await SendRouteResultAsync(socket, session, user);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════════
    // CÁLCULO Y ENVÍO DE RUTA (antes en Program.cs como estático)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Calcula la ruta individual de un usuario hasta el centroide del grupo
    /// y envía el resultado serializado como JSON al socket del cliente.
    ///
    /// Flujo:
    /// 1. Calcula centroide geográfico (común a todos los miembros).
    /// 2. Obtiene ubicación propia del usuario.
    /// 3. Consulta OTP (semáforo limita concurrencia a 1 simultánea).
    /// 4. Serializa resultado a JSON y lo envía por el socket.
    ///
    /// GANANCIA DE MOVERLO AQUÍ:
    /// Antes era un método estático que usaba campos estáticos globales
    /// (_otpSemaphore, otpHttpClient). Ahora usa instancias inyectadas.
    /// → Se puede testear con HttpClient mockeado.
    /// → Se puede cambiar el semáforo sin tocar Program.cs.
    /// </summary>
    private async Task SendRouteResultAsync(Socket socket, GroupSession session, User user)
    {
        // ── 1. Calcular centroide ────────────────────────────────────────────
        var locations = session.GetAllLocations();

        var points = locations
            .Select(l => new GeometryUtils.GeographicLocation(l.Latitude, l.Longitude))
            .ToList();

        GeometryUtils.GeographicLocation centroid = GeometryUtils.CalculateCentroid(points);

        AppLogger.Info("LobbyHandler",
            $"[Group:{session.GroupCode}] [User:{user.username}] " +
            $"Centroide: {centroid.Latitude:F6}, {centroid.Longitude:F6}");

        // ── 2. Obtener ubicación del usuario ─────────────────────────────────
        UserLocation? userLocation = session.GetLocation(user.id);

        if (userLocation == null)
        {
            AppLogger.Warn("LobbyHandler",
                $"[Group:{session.GroupCode}] [User:{user.username}] Ubicación del usuario no encontrada.");
            SendErrorResult(socket, -2, "No se encontró la ubicación del usuario actual.");
            return;
        }

        // ── 3. Consulta OTP (con semáforo para limitar concurrencia) ─────────
        MeetingRouteResult? route;

        try
        {
            AppLogger.Info("LobbyHandler",
                $"[Group:{session.GroupCode}] [User:{user.username}] Esperando turno OTP...");

            // El semáforo evita saturar OTP/Docker con consultas paralelas.
            // Con SemaphoreSlim(1,1): máximo 1 consulta simultánea.
            // Aumentar a (2,2) o (3,3) si el hardware lo permite.
            await _otpSemaphore.WaitAsync();

            try
            {
                AppLogger.Info("LobbyHandler",
                    $"[Group:{session.GroupCode}] [User:{user.username}] Turno OTP adquirido.");

                var otp = new OTP(_httpClient);

                var origin = new OTP.Coordenada(userLocation.Latitude, userLocation.Longitude);
                var destination = new OTP.Coordenada(centroid.Latitude, centroid.Longitude);

                AppLogger.Info("LobbyHandler",
                    $"[User:{user.username}] OTP: {userLocation.Latitude:F6},{userLocation.Longitude:F6} → " +
                    $"{centroid.Latitude:F6},{centroid.Longitude:F6}");

                var sw = Stopwatch.StartNew();
                string jsonResponse = await otp.ConsultarAsync(origin, destination);
                sw.Stop();

                AppLogger.Info("LobbyHandler",
                    $"[User:{user.username}] OTP respondió en {sw.ElapsedMilliseconds} ms.");

                route = otp.ExtraerResultadoRuta(jsonResponse);
            }
            finally
            {
                // SIEMPRE liberar el semáforo, incluso si hay excepción.
                // Sin esto, un fallo en OTP dejaría el semáforo bloqueado
                // y ningún otro usuario podría calcular su ruta.
                _otpSemaphore.Release();
                AppLogger.Debug("LobbyHandler",
                    $"[Group:{session.GroupCode}] [User:{user.username}] Semáforo OTP liberado.");
            }
        }
        catch (TaskCanceledException ex)
        {
            // OTP tardó más de Timeout en responder
            AppLogger.Error("LobbyHandler",
                $"[User:{user.username}] Timeout OTP. {ex.Message}");
            SendErrorResult(socket, -2, "OTP tardó demasiado. Inténtalo de nuevo.");
            return;
        }
        catch (Exception ex)
        {
            AppLogger.Error("LobbyHandler",
                $"[User:{user.username}] Error consultando OTP.\n{ex}");
            SendErrorResult(socket, -2, "Error calculando la ruta en el servidor.");
            return;
        }

        // ── 4. Serializar y enviar resultado ─────────────────────────────────
        if (route == null)
        {
            // OTP respondió pero no encontró itinerario (p.ej. fuera de cobertura)
            AppLogger.Warn("LobbyHandler",
                $"[Group:{session.GroupCode}] [User:{user.username}] OTP sin itinerario disponible.");

            var noRoutePayload = new MeetingResultTransportModel
            {
                Latitude = centroid.Latitude,
                Longitude = centroid.Longitude,
                OriginLatitude = userLocation.Latitude,
                OriginLongitude = userLocation.Longitude,
                DurationSeconds = -3,           // -3 = sin ruta (pero centroide calculado)
                HasValidRoute = false,
                MeetingPointName = "Punto de encuentro",
                AddressText = "No se encontró una ruta válida",
                DistanceText = "Distancia no disponible",
                FairnessText = "Centroide calculado, pero sin ruta disponible",
                Legs = new List<RouteLegDto>()
            };

            SocketTools.sendString(JsonSerializer.Serialize(noRoutePayload), socket);
            return;
        }

        // Ruta válida — construir payload completo
        AppLogger.Info("LobbyHandler",
            $"[Group:{session.GroupCode}] [User:{user.username}] Ruta calculada: " +
            $"{route.DurationSeconds}s ({route.DurationSeconds / 60} min), " +
            $"{route.DistanceMeters:F0}m, {route.TransferCount} transbordos, {route.Legs.Count} tramos.");

        var payload = new MeetingResultTransportModel
        {
            Latitude = centroid.Latitude,
            Longitude = centroid.Longitude,
            OriginLatitude = userLocation.Latitude,
            OriginLongitude = userLocation.Longitude,
            DurationSeconds = route.DurationSeconds,
            DistanceMeters = route.DistanceMeters,
            TransferCount = route.TransferCount,
            HasValidRoute = true,
            MeetingPointName = "Punto de encuentro",
            AddressText = "Ruta calculada correctamente",
            DistanceText = $"{route.DistanceMeters / 1000:0.0} km",
            FairnessText = route.TransferCount == 0
                ? "Ruta directa sin transbordos"
                : $"Ruta con {route.TransferCount} transbordo{(route.TransferCount == 1 ? "" : "s")}",
            Legs = route.Legs
        };

        SocketTools.sendString(JsonSerializer.Serialize(payload), socket);

        AppLogger.Debug("LobbyHandler",
            $"[Group:{session.GroupCode}] [User:{user.username}] Resultado enviado al cliente.");
    }

    // ═══════════════════════════════════════════════════════════════════
    // HELPERS DE RESPUESTA
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Envía una respuesta JSON indicando que el resultado aún está pendiente
    /// (no todas las ubicaciones han llegado). DurationSeconds = -1 es la señal.
    /// </summary>
    private static void SendPendingResult(Socket socket)
    {
        var payload = new MeetingResultTransportModel
        {
            DurationSeconds = -1,           // -1 = pendiente (el cliente hace polling)
            HasValidRoute = false,
            MeetingPointName = "Pendiente",
            AddressText = "Esperando ubicaciones del resto del grupo",
            DistanceText = "Distancia no disponible",
            FairnessText = "Cálculo pendiente",
            Legs = new List<RouteLegDto>()
        };

        SocketTools.sendString(JsonSerializer.Serialize(payload), socket);
    }

    /// <summary>
    /// Envía una respuesta JSON de error con un código de estado específico.
    /// statusCode: -2 = error técnico, -3 = sin ruta disponible.
    /// </summary>
    private static void SendErrorResult(Socket socket, int statusCode, string message)
    {
        var payload = new MeetingResultTransportModel
        {
            DurationSeconds = statusCode,
            HasValidRoute = false,
            MeetingPointName = "Error",
            AddressText = message,
            DistanceText = "Distancia no disponible",
            FairnessText = "No se pudo calcular el punto de encuentro",
            Legs = new List<RouteLegDto>()
        };

        SocketTools.sendString(JsonSerializer.Serialize(payload), socket);
    }
}