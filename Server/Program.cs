using NetUtils;
using Server.Application.Services;
using Server.Data;
using Server.Group.GroupSessions;
using Server.Infrastructure;
using Server.Presentation.Handlers;
using System.Net;
using System.Net.Sockets;
using static Server.Data.AppDbContext;

namespace Server;

/// <summary>
/// Punto de entrada del servidor JustMeetingPoint.
///
/// RESPONSABILIDAD (ahora acotada):
/// - Configurar e inicializar dependencias compartidas.
/// - Arrancar los listeners TCP.
/// - Orquestar el flujo de alto nivel de cada conexión cliente.
///
/// TODO LO QUE YA NO ESTÁ AQUÍ (y por qué es una mejora):
/// - Lógica de autenticación → AuthService + AuthHandler
/// - Lógica del lobby → LobbyHandler
/// - Cálculo de rutas → LobbyHandler.SendRouteResultAsync
/// - Gestión de sesiones → GroupSession + GroupSessionManager
///
/// ANTES: Program.cs tenía ~700 líneas con métodos estáticos que
///        mezclaban TCP, BBDD, lógica de negocio y gestión de grupos.
/// AHORA: Program.cs tiene ~150 líneas centradas solo en bootstrap y
///        orquestación de handlers.
///
/// ══════════════════════════════════════════════════════════════════════
/// FIX DEL BUG — CREAR GRUPO SOLO UNA VEZ POR SESIÓN
/// ══════════════════════════════════════════════════════════════════════
/// CAUSA RAÍZ:
///   Los cases CreateGroup y JoinGroup usaban `return` después de que el
///   lobby terminaba. Eso salía de ServiceIdentity() completamente,
///   activando el finally → socket.Close(). El socket quedaba destruido.
///   El cliente MAUI seguía creyendo que tenía una sesión válida, pero
///   cualquier operación posterior fallaba con SocketException.
///
/// FIX:
///   `return` → `break` en ambos cases.
///   Con `break`, después de que el lobby termina, el control vuelve
///   al while(true) de ServiceIdentity y el servidor sigue esperando
///   el siguiente opcode del mismo cliente autenticado.
///   El socket permanece abierto toda la sesión.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
internal class Program
{
    // ── Enums del protocolo ─────────────────────────────────────────────
    // Se mantienen aquí porque Program.cs es el punto de entrada
    // del protocolo TCP. Los handlers los usan de forma interna.

    private enum MainUser
    {
        Login = 1,
        Register = 2
    }

    private enum MainMenuOption
    {
        CreateGroup = 1,
        JoinGroup = 2,
        GetHomeData = 3,
        GetProfileData = 4
    }

    // ── Estado compartido del servidor ──────────────────────────────────
    // Son readonly porque se inicializan una vez al arrancar y no cambian.
    // Son static porque viven durante todo el proceso del servidor.

    /// <summary>
    /// Cadena de conexión a PostgreSQL.
    /// TODO: mover a appsettings.json en la siguiente iteración.
    /// </summary>
    private static readonly string ConnectionString =
        "Host=localhost;Port=5432;Database=SGSDatabase;Username=postgres;Password=postgres123";

    /// <summary>
    /// Gestor de sesiones de grupo activas en memoria.
    /// Singleton: un único manager para todas las conexiones simultáneas.
    /// Usa ConcurrentDictionary internamente — thread-safe.
    /// </summary>
    private static readonly GroupSessionManager SessionManager = new();

    /// <summary>
    /// HttpClient compartido para consultas a OpenTripPlanner.
    /// HttpClient es thread-safe y debe reutilizarse (no crear uno por request).
    /// Timeout generoso porque OTP en Docker puede tardar con el grafo frío.
    /// </summary>
    private static readonly HttpClient OtpHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(180)
    };

    /// <summary>
    /// Semáforo para limitar consultas OTP simultáneas.
    /// SemaphoreSlim(1,1) = máximo 1 consulta a la vez.
    /// Previene saturar OTP/Docker en entorno local.
    /// Nota: NO comparte resultados. Cada usuario tiene su propia consulta y ruta.
    /// </summary>
    private static readonly SemaphoreSlim OtpSemaphore = new(1, 1);

    // ── Servicios de aplicación ─────────────────────────────────────────
    // Singleton porque no tienen estado mutable específico de conexión.
    // Se crean una vez y se comparten entre todas las conexiones TCP.

    private static readonly IAuthService AuthService = new AuthService(ConnectionString);

    // ═══════════════════════════════════════════════════════════════════
    // BOOTSTRAP
    // ═══════════════════════════════════════════════════════════════════

    static void Main(string[] args)
    {
        // Verificar conectividad con la base de datos al arrancar
        try
        {
            using AppDbContext context = new AppDbContext(ConnectionString);
            context.Database.EnsureCreated();
            AppLogger.Info("Boot", "Base de datos verificada correctamente.");
            Thread.Sleep(500);
            Console.Clear();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Boot", $"No se pudo conectar con la base de datos.\n{ex}");
            Thread.Sleep(2000);
            Console.Clear();
        }

        // Arrancar servidores TCP en threads independientes.
        // Puerto 1000: ServerAPI (placeholder, sin implementación actual).
        // Puerto 1001: ServerIdentity (login, registro, grupos, lobby).
        Thread threadApi = new Thread(ServerAPI);
        Thread threadIdentity = new Thread(ServerIdentity);

        threadApi.Start();
        threadIdentity.Start();

        AppLogger.Info("Boot", "Servidor JMP corriendo. Pulsa ENTER para detener.");
        Console.ReadLine();
    }

    // ── Listener puerto 1000 (ServerAPI — reservado para uso futuro) ────
    static void ServerAPI()
    {
        IPAddress address = IPAddress.Parse("192.168.1.39");
        IPEndPoint endPoint = new IPEndPoint(address, 1000);

        Socket server = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        server.Bind(endPoint);
        server.Listen();

        AppLogger.Info("Socket", "ServerAPI escuchando en :1000 (reservado, sin implementación).");

        while (server.IsBound)
        {
            Socket client = server.Accept();
            AppLogger.Info("Socket", "ServerAPI: cliente aceptado (sin implementación).");
            // Por implementar en futuras versiones (REST API, etc.)
            client.Close();
        }
    }

    // ── Listener puerto 1001 (ServerIdentity — el servidor principal) ───
    static void ServerIdentity()
    {
        try
        {
            IPAddress address = IPAddress.Parse("192.168.1.39");
            IPEndPoint endPoint = new IPEndPoint(address, 1001);

            Socket server = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            server.Bind(endPoint);
            server.Listen();

            AppLogger.Info("Socket", "ServerIdentity escuchando en :1001. Esperando clientes...");

            while (true)
            {
                Socket client = server.Accept();
                AppLogger.Info("Socket", "Nueva conexión aceptada en ServerIdentity.");

                // Thread por conexión: una conexión = un thread bloqueante.
                // Funcional para demos/académico. Para producción: async I/O
                // con SocketAsyncEventArgs o migración a SignalR.
                Thread thread = new Thread(() =>
                {
                    ServiceIdentity(client).GetAwaiter().GetResult();
                });
                thread.Start();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Socket", $"Error fatal en ServerIdentity.\n{ex}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // ORQUESTADOR DE SESIÓN — un método por conexión TCP activa
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gestiona el ciclo de vida completo de una conexión TCP de cliente.
    ///
    /// Operaciones soportadas:
    ///   1. Login → mientras esté autenticado, acepta opciones del menú
    ///   2. Register → registra y cierra la conexión
    ///
    /// Dentro del menú autenticado:
    ///   - CreateGroup → prepara grupo + entra al lobby → ← FIX: BREAK
    ///   - JoinGroup   → se une a grupo + entra al lobby → ← FIX: BREAK
    ///   - GetHomeData    → responde username
    ///   - GetProfileData → responde username, email, fecha
    ///
    /// El FIX crítico está en los cases CreateGroup y JoinGroup:
    /// Antes usaban `return` (cerraba el socket), ahora usan `break`
    /// (vuelven al while y el usuario puede crear/unirse a otro grupo).
    /// </summary>
    static async Task ServiceIdentity(Socket socket)
    {
        // Handlers creados por conexión (no singleton).
        // Cada conexión tiene sus propios handlers, pero comparten
        // los singletons inyectados (AuthService, SessionManager, etc.)
        var authHandler = new AuthHandler(AuthService);

        IMeetingRouteService meetingRouteService =
            new MeetingRouteService(OtpHttpClient, OtpSemaphore);

        var lobbyHandler = new LobbyHandler(
            SessionManager,
            meetingRouteService,
            ConnectionString);

        // Variables de sesión:
        // currentUser     → usuario autenticado (null si aún no logueado)
        // activeGroupCode → código del grupo en el lobby actual (null si no en lobby)
        //                   Se usa en el finally para limpiar la sesión
        //                   si el cliente se desconecta mid-lobby.
        User? currentUser = null;
        string? activeGroupCode = null;

        try
        {
            // ── Operación inicial: Login o Register ──────────────────────────
            int initialOption = SocketTools.receiveInt(socket);

            // ── Flujo de Login ───────────────────────────────────────────────
            if (initialOption == (int)MainUser.Login)
            {
                AppLogger.Info("ServiceIdentity", "Cliente solicitando login.");

                currentUser = authHandler.HandleLogin(socket);

                if (currentUser is null)
                {
                    // Login fallido — AuthHandler ya envió `false` al cliente.
                    // Cerramos la conexión (el finally lo hace).
                    AppLogger.Warn("ServiceIdentity", "Login fallido. Cerrando conexión.");
                    return;
                }

                AppLogger.Info("ServiceIdentity",
                    $"[User:{currentUser.username}] Sesión autenticada. Entrando en menú principal.");

                // ── Bucle del menú principal (permanece vivo toda la sesión) ──
                // Este while solo termina si:
                //   a) El cliente cierra la conexión (SocketException)
                //   b) Un error inesperado (Exception)
                //   c) En el futuro: opción de logout explícita
                while (true)
                {
                    int menuOption = SocketTools.receiveInt(socket);

                    AppLogger.Debug("ServiceIdentity",
                        $"[User:{currentUser.username}] Opción de menú recibida: {menuOption}");

                    switch (menuOption)
                    {
                        // ── CreateGroup ─────────────────────────────────────
                        case (int)MainMenuOption.CreateGroup:

                            AppLogger.Info("ServiceIdentity",
                                $"[User:{currentUser.username}] Iniciando CreateGroup.");

                            // Paso 1: preparar el grupo (BBDD + sesión en memoria).
                            // Devuelve el groupCode si fue bien, null si falló.
                            string? createdCode = await lobbyHandler.PrepareCreateGroupAsync(
                                socket, currentUser);

                            if (createdCode is not null)
                            {
                                // Paso 2: registrar el groupCode ANTES de entrar al lobby.
                                // Si el cliente se desconecta dentro del lobby (SocketException),
                                // el finally usa activeGroupCode para limpiar la sesión.
                                activeGroupCode = createdCode;

                                // Paso 3: entrar al bucle del lobby.
                                // RunLobbyAsync termina cuando: el usuario sale, se calcula
                                // la ruta, o hay una desconexión.
                                await lobbyHandler.RunLobbyAsync(socket, createdCode, currentUser);

                                // Paso 4: limpiar el código activo tras salir del lobby.
                                activeGroupCode = null;
                            }

                            // ✅ FIX CRÍTICO: BREAK (no return)
                            // Con return: salíamos de ServiceIdentity → finally → socket.Close()
                            //             El socket moría. El usuario no podía crear otro grupo.
                            // Con break:  volvemos al while(true) del menú principal.
                            //             El socket sigue vivo. El usuario puede hacer
                            //             CreateGroup, JoinGroup, GetHomeData, etc. de nuevo.
                            break;

                        // ── JoinGroup ───────────────────────────────────────
                        case (int)MainMenuOption.JoinGroup:

                            AppLogger.Info("ServiceIdentity",
                                $"[User:{currentUser.username}] Iniciando JoinGroup.");

                            // Mismo patrón que CreateGroup: preparar → registrar code → lobby → limpiar
                            string? joinedCode = lobbyHandler.PrepareJoinGroup(socket, currentUser);

                            if (joinedCode is not null)
                            {
                                activeGroupCode = joinedCode;
                                await lobbyHandler.RunLobbyAsync(socket, joinedCode, currentUser);
                                activeGroupCode = null;
                            }

                            // ✅ FIX CRÍTICO: BREAK (no return) — misma razón que CreateGroup
                            break;

                        // ── GetHomeData ─────────────────────────────────────
                        case (int)MainMenuOption.GetHomeData:
                            authHandler.HandleGetHomeData(socket, currentUser);
                            break;

                        // ── GetProfileData ──────────────────────────────────
                        case (int)MainMenuOption.GetProfileData:
                            authHandler.HandleGetProfileData(socket, currentUser);
                            break;

                        // ── Opción desconocida ──────────────────────────────
                        default:
                            AppLogger.Warn("ServiceIdentity",
                                $"[User:{currentUser.username}] Opción de menú desconocida: {menuOption}");
                            break;
                    }
                }
            }
            // ── Flujo de Register ────────────────────────────────────────────
            else if (initialOption == (int)MainUser.Register)
            {
                AppLogger.Info("ServiceIdentity", "Cliente solicitando registro.");
                authHandler.HandleRegister(socket);
                AppLogger.Info("ServiceIdentity", "Registro completado. Cerrando conexión.");
                // El registro es stateless: se conecta, registra, se cierra.
                // El usuario deberá hacer login en una nueva conexión.
            }
            else
            {
                AppLogger.Warn("ServiceIdentity",
                    $"Opción inicial no reconocida: {initialOption}. Cerrando conexión.");
                SocketTools.sendBool(socket, false);
            }
        }
        catch (SocketException ex)
        {
            // Desconexión del cliente — caso normal en una app móvil
            // (el usuario cierra la app, pierde WiFi, etc.)
            AppLogger.Warn("ServiceIdentity",
                $"[User:{currentUser?.username ?? "desconocido"}] " +
                $"Cliente desconectado: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Error inesperado — logueamos completo para diagnóstico
            AppLogger.Error("ServiceIdentity",
                $"[User:{currentUser?.username ?? "desconocido"}] " +
                $"Error inesperado en sesión.\n{ex}");

            // Intentar notificar al cliente (puede fallar si el socket ya está muerto)
            try { SocketTools.sendBool(socket, false); }
            catch { /* Si falla, ignorar — el finally cerrará el socket */ }
        }
        finally
        {
            // ── Limpieza garantizada ─────────────────────────────────────────
            // Este bloque se ejecuta SIEMPRE: en salida normal, en SocketException
            // y en cualquier otra excepción.

            // Si el cliente se desconectó mientras estaba en un lobby,
            // eliminarlo de la sesión para que los demás miembros no esperen
            // una ubicación que nunca llegará.
            if (currentUser != null && activeGroupCode != null)
            {
                var session = SessionManager.Get(activeGroupCode);

                if (session != null)
                {
                    session.RemoveMember(currentUser.id);

                    AppLogger.Warn("ServiceIdentity",
                        $"[Group:{activeGroupCode}] [User:{currentUser.username}] " +
                        $"Eliminado por desconexión durante lobby.");

                    // Si era el último miembro, eliminar la sesión completa
                    if (session.MemberCount == 0)
                    {
                        SessionManager.Remove(activeGroupCode);
                        AppLogger.Info("ServiceIdentity",
                            $"[Group:{activeGroupCode}] Sesión de grupo eliminada (sin miembros).");
                    }
                }
            }

            // Cerrar el socket TCP — libera el puerto y los recursos del OS
            try
            {
                socket.Close();
                AppLogger.Debug("ServiceIdentity",
                    $"[User:{currentUser?.username ?? "desconocido"}] Socket cerrado.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("ServiceIdentity", $"Error cerrando socket: {ex.Message}");
            }
        }
    }
}