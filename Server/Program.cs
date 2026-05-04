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
/// Responsabilidad:
/// - Configurar dependencias compartidas.
/// - Arrancar los listeners TCP.
/// - Orquestar el flujo principal de cada conexión cliente.
/// 
/// La lógica específica se delega en:
/// - AuthService / AuthHandler
/// - LobbyHandler
/// - GroupSession / GroupSessionManager
/// - MeetingRouteService
/// </summary>
internal class Program
{
    #region Protocol Enums

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

    #endregion

    #region Server State

    /// <summary>
    /// Cadena de conexión a PostgreSQL.
    /// TODO: mover a appsettings.json en una siguiente iteración.
    /// </summary>
    private static readonly string ConnectionString =
        "Host=localhost;Port=5432;Database=SGSDatabase;Username=Alumno;Password=AlumnoIFP";

    /// <summary>
    /// Gestor compartido de sesiones de grupo activas en memoria.
    /// </summary>
    private static readonly GroupSessionManager SessionManager = new();

    /// <summary>
    /// HttpClient compartido para consultas a OpenTripPlanner.
    /// </summary>
    private static readonly HttpClient OtpHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(180)
    };

    /// <summary>
    /// Limita las consultas simultáneas contra OTP.
    /// No comparte resultados: cada usuario mantiene su propia consulta y ruta.
    /// </summary>
    private static readonly SemaphoreSlim OtpSemaphore = new(1, 1);

    #endregion

    #region Application Services

    private static readonly IAuthService AuthService = new AuthService(ConnectionString);

    #endregion

    #region Bootstrap

    static void Main(string[] args)
    {
        // Verificar conectividad con la base de datos al arrancar.
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
        Thread threadApi = new Thread(ServerAPI);
        Thread threadIdentity = new Thread(ServerIdentity);

        threadApi.Start();
        threadIdentity.Start();

        AppLogger.Info("Boot", "Servidor JMP corriendo. Pulsa ENTER para detener.");
        Console.ReadLine();
    }

    #endregion

    #region TCP Listeners

    static void ServerAPI()
    {
        IPAddress address = IPAddress.Parse("172.20.32.1");
        IPEndPoint endPoint = new IPEndPoint(address, 1000);

        Socket server = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        server.Bind(endPoint);
        server.Listen();

        AppLogger.Info("Socket", "ServerAPI escuchando en :1000 (reservado, sin implementación).");

        while (server.IsBound)
        {
            Socket client = server.Accept();

            AppLogger.Info("Socket", "ServerAPI: cliente aceptado (sin implementación).");

            // Por implementar en futuras versiones.
            client.Close();
        }
    }

    static void ServerIdentity()
    {
        try
        {
            IPAddress address = IPAddress.Parse("172.20.32.1");
            IPEndPoint endPoint = new IPEndPoint(address, 1001);

            Socket server = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            server.Bind(endPoint);
            server.Listen();

            AppLogger.Info("Socket", "ServerIdentity escuchando en :1001. Esperando clientes...");

            while (true)
            {
                Socket client = server.Accept();

                AppLogger.Info("Socket", "Nueva conexión aceptada en ServerIdentity.");

                // Thread por conexión: suficiente para demo/académico.
                // Para producción: async I/O, SocketAsyncEventArgs o SignalR.
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

    #endregion

    #region Client Session Orchestration

    /// <summary>
    /// Gestiona el ciclo de vida completo de una conexión TCP de cliente.
    /// 
    /// Flujo:
    /// - Login/Register inicial.
    /// - Menú autenticado.
    /// - Delegación en AuthHandler y LobbyHandler.
    /// - Limpieza garantizada del socket en finally.
    /// </summary>
    static async Task ServiceIdentity(Socket socket)
    {
        // Handlers creados por conexión.
        var authHandler = new AuthHandler(AuthService);

        IMeetingRouteService meetingRouteService =
            new MeetingRouteService(OtpHttpClient, OtpSemaphore);

        var lobbyHandler = new LobbyHandler(
            SessionManager,
            meetingRouteService,
            ConnectionString);

        User? currentUser = null;
        string? activeGroupCode = null;

        try
        {
            int initialOption = SocketTools.receiveInt(socket);

            if (initialOption == (int)MainUser.Login)
            {
                AppLogger.Info("ServiceIdentity", "Cliente solicitando login.");

                currentUser = authHandler.HandleLogin(socket);

                if (currentUser is null)
                {
                    AppLogger.Warn("ServiceIdentity", "Login fallido. Cerrando conexión.");
                    return;
                }

                AppLogger.Info(
                    "ServiceIdentity",
                    $"[User:{currentUser.username}] Sesión autenticada. Entrando en menú principal.");

                while (true)
                {
                    int menuOption = SocketTools.receiveInt(socket);

                    AppLogger.Debug(
                        "ServiceIdentity",
                        $"[User:{currentUser.username}] Opción de menú recibida: {menuOption}");

                    switch (menuOption)
                    {
                        case (int)MainMenuOption.CreateGroup:

                            AppLogger.Info(
                                "ServiceIdentity",
                                $"[User:{currentUser.username}] Iniciando CreateGroup.");

                            string? createdCode = await lobbyHandler.PrepareCreateGroupAsync(
                                socket,
                                currentUser);

                            if (createdCode is not null)
                            {
                                activeGroupCode = createdCode;

                                await lobbyHandler.RunLobbyAsync(
                                    socket,
                                    createdCode,
                                    currentUser);

                                activeGroupCode = null;
                            }

                            /*
                             * Importante:
                             * break mantiene vivo el socket y devuelve el control al menú principal.
                             * return saldría de ServiceIdentity y cerraría el socket en el finally.
                             */
                            break;

                        case (int)MainMenuOption.JoinGroup:

                            AppLogger.Info(
                                "ServiceIdentity",
                                $"[User:{currentUser.username}] Iniciando JoinGroup.");

                            string? joinedCode = lobbyHandler.PrepareJoinGroup(
                                socket,
                                currentUser);

                            if (joinedCode is not null)
                            {
                                activeGroupCode = joinedCode;

                                await lobbyHandler.RunLobbyAsync(
                                    socket,
                                    joinedCode,
                                    currentUser);

                                activeGroupCode = null;
                            }

                            /*
                             * Mismo criterio que CreateGroup:
                             * break permite volver al menú sin cerrar la sesión TCP.
                             */
                            break;

                        case (int)MainMenuOption.GetHomeData:

                            authHandler.HandleGetHomeData(socket, currentUser);
                            break;

                        case (int)MainMenuOption.GetProfileData:

                            authHandler.HandleGetProfileData(socket, currentUser);
                            break;

                        default:

                            AppLogger.Warn(
                                "ServiceIdentity",
                                $"[User:{currentUser.username}] Opción de menú desconocida: {menuOption}");
                            break;
                    }
                }
            }
            else if (initialOption == (int)MainUser.Register)
            {
                AppLogger.Info("ServiceIdentity", "Cliente solicitando registro.");

                authHandler.HandleRegister(socket);

                AppLogger.Info("ServiceIdentity", "Registro completado. Cerrando conexión.");
            }
            else
            {
                AppLogger.Warn(
                    "ServiceIdentity",
                    $"Opción inicial no reconocida: {initialOption}. Cerrando conexión.");

                SocketTools.sendBool(socket, false);
            }
        }
        catch (SocketException ex)
        {
            AppLogger.Warn(
                "ServiceIdentity",
                $"[User:{currentUser?.username ?? "desconocido"}] " +
                $"Cliente desconectado: {ex.Message}");
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                "ServiceIdentity",
                $"[User:{currentUser?.username ?? "desconocido"}] " +
                $"Error inesperado en sesión.\n{ex}");

            try
            {
                SocketTools.sendBool(socket, false);
            }
            catch
            {
                // El socket puede estar cerrado. No hay acción recuperable.
            }
        }
        finally
        {
            if (currentUser != null && activeGroupCode != null)
            {
                var session = SessionManager.Get(activeGroupCode);

                if (session != null)
                {
                    session.RemoveMember(currentUser.id);

                    AppLogger.Warn(
                        "ServiceIdentity",
                        $"[Group:{activeGroupCode}] [User:{currentUser.username}] " +
                        $"Eliminado por desconexión durante lobby.");

                    if (session.MemberCount == 0)
                    {
                        SessionManager.Remove(activeGroupCode);

                        AppLogger.Info(
                            "ServiceIdentity",
                            $"[Group:{activeGroupCode}] Sesión de grupo eliminada (sin miembros).");
                    }
                }
            }

            try
            {
                socket.Close();

                AppLogger.Debug(
                    "ServiceIdentity",
                    $"[User:{currentUser?.username ?? "desconocido"}] Socket cerrado.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("ServiceIdentity", $"Error cerrando socket: {ex.Message}");
            }
        }
    }

    #endregion
}