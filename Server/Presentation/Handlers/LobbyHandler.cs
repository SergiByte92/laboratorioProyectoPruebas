using NetUtils;
using Server.API;
using Server.Application.Services;
using Server.Data;
using Server.Group;
using Server.Group.GroupSessions;
using Server.Infrastructure;
using System.Net.Sockets;
using System.Text.Json;
using static Server.Data.AppDbContext;

namespace Server.Presentation.Handlers;

/// <summary>
/// Gestiona el flujo del lobby de grupos.
///
/// Responsabilidades:
/// - Crear la sesión en memoria tras crear un grupo en BBDD.
/// - Unir usuarios a sesiones existentes.
/// - Mantener el ciclo del lobby mediante un protocolo TCP.
/// - Enviar el estado actual del lobby al cliente.
/// - Procesar acciones del lobby: refresh, exit, start, send location y poll result.
/// - Lanzar el cálculo de ruta cuando todas las ubicaciones están disponibles.
///
/// Nota importante:
/// Este handler no gestiona login ni registro. Recibe un usuario ya autenticado
/// desde Program.cs / ServiceIdentity.
/// </summary>
public sealed class LobbyHandler
{
    #region Constants - Validation

    private const int MinGroupCodeLength = 4;
    private const int MaxGroupCodeLength = 12;

    #endregion

    #region Protocol Enums

    /*
     * Opciones del protocolo interno del lobby.
     * Estos valores deben coincidir con los códigos enviados por el cliente MAUI.
     */
    private enum LobbyOpt
    {
        Refresh = 1,
        Exit = 2,
        Start = 3,
        SendLocation = 4,
        PollResult = 5
    }

    #endregion

    #region Fields

    private readonly GroupSessionManager _sessionManager;
    private readonly IMeetingRouteService _meetingRouteService;
    private readonly string _connectionString;

    #endregion

    #region Constructor

    public LobbyHandler(
        GroupSessionManager sessionManager,
        IMeetingRouteService meetingRouteService,
        string connectionString)
    {
        _sessionManager = sessionManager;
        _meetingRouteService = meetingRouteService;
        _connectionString = connectionString;
    }

    #endregion

    #region Public API - Group Preparation

    /// <summary>
    /// Crea un grupo persistente en BBDD y una sesión equivalente en memoria.
    ///
    /// Flujo:
    /// 1. CreateGroupService recibe los datos desde el socket y crea el grupo.
    /// 2. Si la creación fue correcta, se crea una GroupSession en memoria.
    /// 3. El owner se añade como primer miembro de la sesión.
    /// 4. La sesión se registra en GroupSessionManager.
    ///
    /// Devuelve el código del grupo si todo fue correcto.
    /// Devuelve null si no se pudo preparar el grupo.
    /// </summary>
    public async Task<string?> PrepareCreateGroupAsync(Socket socket, User user)
    {
        AppLogger.Info("LobbyHandler", $"[User:{user.username}] Preparando creación de grupo...");

        using AppDbContext context = new AppDbContext(_connectionString);
        var createService = new CreateGroupService(context);

        var result = await createService.ExecuteAsync(socket, user);

        if (!result.Success)
        {
            AppLogger.Warn("LobbyHandler", $"[User:{user.username}] No se pudo crear el grupo.");
            return null;
        }

        var session = new GroupSession(result.GroupId, result.GroupCode, user.id);
        bool addedOwner = session.AddMember(user.id, user.username);

        if (!addedOwner)
        {
            AppLogger.Error(
                "LobbyHandler",
                $"[Group:{result.GroupCode}] No se pudo añadir al owner a la sesión en memoria.");

            return null;
        }

        _sessionManager.Add(session);

        AppLogger.Info(
            "LobbyHandler",
            $"[Group:{result.GroupCode}] [User:{user.username}] Sesión creada.");

        return result.GroupCode;
    }

    /// <summary>
    /// Une al usuario actual a una sesión de grupo ya existente en memoria.
    ///
    /// El cliente envía el código del grupo por socket.
    /// El servidor normaliza y valida el código antes de intentar unir al usuario.
    ///
    /// Importante:
    /// Este método responde al cliente con un bool indicando si el join fue aceptado.
    /// </summary>
    public string? PrepareJoinGroup(Socket socket, User user)
    {
        string groupCode = NormalizeGroupCode(SocketTools.receiveString(socket));

        AppLogger.Info(
            "LobbyHandler",
            $"[Group:{groupCode}] [User:{user.username}] Intentando unirse...");

        if (!IsValidGroupCode(groupCode))
        {
            SocketTools.sendBool(socket, false);

            AppLogger.Warn(
                "LobbyHandler",
                $"[Group:{groupCode}] Join rechazado: código inválido.");

            return null;
        }

        bool success = _sessionManager.TryJoinGroup(groupCode, user.id, user.username);
        SocketTools.sendBool(socket, success);

        if (!success)
        {
            AppLogger.Warn(
                "LobbyHandler",
                $"[Group:{groupCode}] Join fallido — grupo no encontrado, iniciado o usuario duplicado.");

            return null;
        }

        AppLogger.Info(
            "LobbyHandler",
            $"[Group:{groupCode}] [User:{user.username}] Unido correctamente.");

        return groupCode;
    }

    #endregion

    #region Public API - Lobby Loop

    /// <summary>
    /// Ejecuta el bucle principal del lobby para un usuario concreto.
    ///
    /// Patrón de comunicación:
    /// 1. El servidor obtiene la sesión actual.
    /// 2. El servidor envía la cabecera estándar del lobby.
    /// 3. El cliente consume esa cabecera.
    /// 4. El cliente envía una opción del lobby.
    /// 5. El servidor procesa la opción.
    /// 6. El bucle vuelve a empezar.
    ///
    /// Este orden es crítico. Si cliente y servidor leen/escriben en distinto orden,
    /// el socket queda desincronizado.
    /// </summary>
    public async Task RunLobbyAsync(Socket socket, string groupCode, User user)
    {
        AppLogger.Info(
            "LobbyHandler",
            $"[Group:{groupCode}] [User:{user.username}] Entrando al lobby.");

        while (true)
        {
            GroupSession? session = _sessionManager.Get(groupCode);

            if (session is null)
            {
                /*
                 * Si la sesión ya no existe, se notifica al cliente con sessionValid = false.
                 * El cliente debe abandonar la pantalla de lobby o mostrar error.
                 */
                SocketTools.sendBool(socket, false);

                AppLogger.Warn(
                    "LobbyHandler",
                    $"[Group:{groupCode}] Sesión no encontrada. Terminando lobby.");

                return;
            }

            /*
             * El servidor siempre inicia cada iteración enviando el estado actual.
             * Esto permite que el cliente refresque miembros/estado antes de decidir
             * si manda Refresh, Start, SendLocation o PollResult.
             */
            SendLobbyHeader(socket, session);

            int option;

            try
            {
                option = SocketTools.receiveInt(socket);
            }
            catch (SocketException)
            {
                /*
                 * Desconexión normal en entorno móvil:
                 * app cerrada, emulador detenido, pérdida de red, etc.
                 * Program.cs se encargará de cerrar socket y limpiar si corresponde.
                 */
                AppLogger.Warn(
                    "LobbyHandler",
                    $"[Group:{groupCode}] [User:{user.username}] Desconexión detectada.");

                return;
            }

            switch (option)
            {
                case (int)LobbyOpt.Refresh:

                    /*
                     * Refresh no necesita enviar payload extra.
                     * La información útil ya se ha enviado al principio de la iteración
                     * mediante SendLobbyHeader.
                     */
                    AppLogger.Debug(
                        "LobbyHandler",
                        $"[Group:{groupCode}] Refresh. Members={session.MemberCount}, Started={session.HasStarted}");

                    break;

                case (int)LobbyOpt.Exit:

                    /*
                     * Exit termina el lobby para este usuario.
                     * Si era el último miembro, se elimina la sesión completa.
                     */
                    HandleExit(groupCode, session, user);
                    return;

                case (int)LobbyOpt.Start:

                    /*
                     * Start solo marca la sesión como iniciada.
                     * Después del break, el bucle vuelve arriba y enviará una nueva cabecera
                     * con HasStarted actualizado.
                     */
                    HandleStart(socket, groupCode, session, user);
                    break;

                case (int)LobbyOpt.SendLocation:

                    /*
                     * SendLocation puede terminar el lobby de este usuario si ya puede
                     * devolverle su resultado de ruta.
                     */
                    bool locationDone = await HandleSendLocationAsync(
                        socket,
                        groupCode,
                        session,
                        user);

                    if (locationDone)
                        return;

                    break;

                case (int)LobbyOpt.PollResult:

                    /*
                     * PollResult se usa cuando el cliente ya envió ubicación pero el cálculo
                     * todavía estaba pendiente porque faltaban ubicaciones de otros miembros.
                     */
                    bool pollDone = await HandlePollResultAsync(
                        socket,
                        groupCode,
                        session,
                        user);

                    if (pollDone)
                        return;

                    break;

                default:

                    AppLogger.Warn(
                        "LobbyHandler",
                        $"[Group:{groupCode}] Opción desconocida: {option}");

                    break;
            }
        }
    }

    #endregion

    #region Lobby Option Handlers

    /// <summary>
    /// Saca al usuario de la sesión actual.
    /// Si no quedan miembros, elimina la sesión del manager.
    /// </summary>
    private void HandleExit(string groupCode, GroupSession session, User user)
    {
        session.RemoveMember(user.id);

        AppLogger.Info(
            "LobbyHandler",
            $"[Group:{groupCode}] [User:{user.username}] Salida voluntaria.");

        if (session.MemberCount == 0)
        {
            _sessionManager.Remove(groupCode);

            AppLogger.Info(
                "LobbyHandler",
                $"[Group:{groupCode}] Sesión eliminada — sin miembros.");
        }
    }

    /// <summary>
    /// Inicia el grupo si el usuario actual es el owner.
    ///
    /// Responde al cliente con:
    /// - true si el grupo se ha iniciado correctamente.
    /// - false si el usuario no es owner o el grupo no puede iniciarse.
    /// </summary>
    private static void HandleStart(Socket socket, string groupCode, GroupSession session, User user)
    {
        if (user.id != session.OwnerUserId)
        {
            AppLogger.Warn(
                "LobbyHandler",
                $"[Group:{groupCode}] [User:{user.username}] Start rechazado: no es owner.");

            SocketTools.sendBool(socket, false);
            return;
        }

        bool started = session.Start();
        SocketTools.sendBool(socket, started);

        AppLogger.Info(
            "LobbyHandler",
            started
                ? $"[Group:{groupCode}] Grupo iniciado por [User:{user.username}]."
                : $"[Group:{groupCode}] El grupo ya estaba iniciado.");
    }

    /// <summary>
    /// Recibe la ubicación del usuario y decide si ya puede devolver resultado.
    ///
    /// Devuelve:
    /// - true: el servidor ya envió el resultado final y este usuario puede salir del lobby.
    /// - false: todavía no hay resultado final y el cliente deberá seguir en lobby/polling.
    ///
    /// Convenciones de payload:
    /// - Error(...)   => error funcional.
    /// - Pending()    => faltan ubicaciones o cálculo aún no disponible.
    /// - Resultado OK => ruta calculada para el usuario actual.
    /// </summary>
    private async Task<bool> HandleSendLocationAsync(
        Socket socket,
        string groupCode,
        GroupSession session,
        User user)
    {
        if (!session.HasStarted)
        {
            AppLogger.Warn(
                "LobbyHandler",
                $"[Group:{groupCode}] [User:{user.username}] Ubicación enviada antes de Start.");

            SendPayload(socket, MeetingResultFactory.Error("El grupo aún no se ha iniciado."));

            return false;
        }

        /*
         * ReceiveLocationService lee latitud/longitud desde el socket,
         * valida la ubicación y la registra en la GroupSession.
         */
        var locationService = new ReceiveLocationService();
        ReceiveLocationResult locationResult = locationService.Execute(socket, session, user);

        if (!locationResult.Success)
        {
            AppLogger.Warn(
                "LobbyHandler",
                $"[Group:{groupCode}] [User:{user.username}] Ubicación rechazada: {locationResult.ErrorMessage}");

            SendPayload(
                socket,
                MeetingResultFactory.Error(
                    locationResult.ErrorMessage ?? "No se pudo registrar la ubicación."));

            return false;
        }

        if (!locationResult.AllLocationsReceived)
        {
            /*
             * La ubicación del usuario queda guardada, pero todavía no se puede calcular
             * una ruta común porque faltan miembros por enviar su origen.
             */
            AppLogger.Info(
                "LobbyHandler",
                $"[Group:{groupCode}] [User:{user.username}] Ubicación registrada. " +
                $"Progreso: {session.GetAllLocations().Count}/{session.MemberCount}");

            SendPayload(socket, MeetingResultFactory.Pending());

            return false;
        }

        /*
         * Todas las ubicaciones están disponibles.
         * A partir de aquí se puede calcular la ruta específica del usuario actual
         * hacia el punto de encuentro común.
         */
        AppLogger.Info(
            "LobbyHandler",
            $"[Group:{groupCode}] Todas las ubicaciones listas. Calculando ruta para [User:{user.username}].");

        await SendRouteResultAsync(socket, session, user);

        return true;
    }

    /// <summary>
    /// Permite al cliente preguntar si el resultado ya está disponible.
    ///
    /// Se usa cuando SendLocation devolvió Pending porque todavía faltaban
    /// ubicaciones de otros miembros.
    ///
    /// Devuelve:
    /// - true: resultado final enviado.
    /// - false: todavía pendiente.
    /// </summary>
    private async Task<bool> HandlePollResultAsync(
        Socket socket,
        string groupCode,
        GroupSession session,
        User user)
    {
        if (!session.AreAllLocationsReceived())
        {
            AppLogger.Debug(
                "LobbyHandler",
                $"[Group:{groupCode}] [User:{user.username}] PollResult: faltan ubicaciones. " +
                $"Progreso: {session.GetAllLocations().Count}/{session.MemberCount}");

            SendPayload(socket, MeetingResultFactory.Pending());

            return false;
        }

        AppLogger.Info(
            "LobbyHandler",
            $"[Group:{groupCode}] [User:{user.username}] PollResult: todas listas. Calculando ruta.");

        await SendRouteResultAsync(socket, session, user);

        return true;
    }

    #endregion

    #region Route Result Workflow

    /// <summary>
    /// Calcula y envía el resultado de ruta para el usuario actual.
    ///
    /// Importante:
    /// El cálculo es por usuario. No se reutiliza una única ruta común.
    /// Cada miembro recibe su propia ruta desde su origen hasta el punto de encuentro.
    /// </summary>
    private async Task SendRouteResultAsync(Socket socket, GroupSession session, User user)
    {
        MeetingResultTransportModel result =
            await _meetingRouteService.CalculateForUserAsync(session, user);

        SendPayload(socket, result);

        AppLogger.Debug(
            "LobbyHandler",
            $"[Group:{session.GroupCode}] [User:{user.username}] Resultado enviado al cliente.");
    }

    #endregion

    #region Socket Response Helpers

    /// <summary>
    /// Envía la cabecera estándar del lobby.
    ///
    /// Contrato exacto de escritura:
    /// 1. bool sessionValid
    /// 2. int memberCount
    /// 3. bool hasStarted
    ///
    /// El cliente debe leer exactamente en este mismo orden.
    /// </summary>
    private static void SendLobbyHeader(Socket socket, GroupSession session)
    {
        SocketTools.sendBool(socket, true);
        SocketTools.sendInt(socket, session.MemberCount);
        SocketTools.sendBool(socket, session.HasStarted);
    }

    /// <summary>
    /// Serializa el payload de resultado y lo envía al cliente como string JSON.
    ///
    /// El cliente deserializa este JSON como MeetingResultModel.
    /// </summary>
    private static void SendPayload(Socket socket, MeetingResultTransportModel payload)
    {
        string json = JsonSerializer.Serialize(payload);
        SocketTools.sendString(json, socket);
    }

    #endregion

    #region Normalization Helpers

    private static string NormalizeGroupCode(string? value)
    {
        return (value ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty)
            .ToUpperInvariant();
    }

    #endregion

    #region Validation Helpers

    private static bool IsValidGroupCode(string groupCode)
    {
        return groupCode.Length >= MinGroupCodeLength
            && groupCode.Length <= MaxGroupCodeLength
            && groupCode.All(char.IsLetterOrDigit);
    }

    #endregion
}