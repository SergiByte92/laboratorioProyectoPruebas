using NetUtils;
using Server.Algorithm;
using Server.API;
using Server.Data;
using Server.Group;
using Server.Group.GroupSessions;
using Server.Infrastructure;
using Server.UserRouting;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using static Server.Data.AppDbContext;

namespace Server;

internal class Program
{
    public enum MainUser
    {
        Login = 1,
        Register = 2
    }

    public enum MainMenuOption
    {
        CreateGroup = 1,
        JoinGroup = 2,
        GetHomeData = 3,
        GetProfileData = 4
    }

    public enum LobbyOption
    {
        Refresh = 1,
        Exit = 2,
        Start = 3,
        SendLocation = 4,
        PollResult = 5
    }

    public static string connectionString =
        "Host=localhost;Port=5432;Database=SGSDatabase;Username=postgres;Password=postgres123";

    private static readonly GroupSessionManager groupSessionManager = new();

    /// <summary>
    /// HttpClient compartido para consultar OTP.
    /// Subimos timeout a 90s porque OTP en Docker/local puede tardar bastante,
    /// sobre todo con grafo frío o rutas complicadas.
    /// </summary>
    private static readonly HttpClient otpHttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(90)
    };

    /// <summary>
    /// Evita lanzar varias consultas OTP simultáneas.
    /// OTP local en Docker puede saturarse si dos usuarios consultan ruta a la vez.
    /// Con este semáforo procesamos una consulta cada vez.
    /// </summary>
    private static readonly SemaphoreSlim otpSemaphore = new(1, 1);

    static void Main(string[] args)
    {
        try
        {
            using AppDbContext context = new AppDbContext(connectionString);
            context.Database.EnsureCreated();

            AppLogger.Info("Boot", "Base de datos creada/verificada correctamente.");
            Thread.Sleep(1000);
            Console.Clear();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Boot", $"No ha sido posible crear/verificar la base de datos.\n{ex}");
            Thread.Sleep(1500);
            Console.Clear();
        }

        Thread threadServerAPI = new Thread(ServerAPI);
        threadServerAPI.Start();

        Thread threadServerIdentity = new Thread(ServerIdentity);
        threadServerIdentity.Start();

        AppLogger.Info("Boot", "Servidores corriendo. Pulsa ENTER para detenerlos.");
        Console.ReadLine();
    }

    static void ServerAPI()
    {
        IPAddress address = IPAddress.Parse("192.168.1.37");
        IPEndPoint endPoint = new IPEndPoint(address, 1000);

        Socket socketServer = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        socketServer.Bind(endPoint);
        socketServer.Listen();

        AppLogger.Info("Socket", "ServerAPI escuchando en el puerto 1000.");

        while (socketServer.IsBound)
        {
            Socket socketAccept = socketServer.Accept();
            AppLogger.Info("Socket", "Cliente aceptado en ServerAPI.");

            Thread threadsServer = new Thread(ServiceAPI);
            threadsServer.Start(socketAccept);
        }
    }

    static void ServiceAPI(object? o)
    {
        if (o is not Socket socket)
            return;

        AppLogger.Debug("Socket", "ServiceAPI invocado.");
    }

    static void ServerIdentity()
    {
        try
        {
            IPAddress address = IPAddress.Parse("192.168.1.37");
            IPEndPoint endPoint = new IPEndPoint(address, 1001);

            Socket socketServer = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socketServer.Bind(endPoint);
            socketServer.Listen();

            AppLogger.Info("Socket", "ServerIdentity escuchando en el puerto 1001.");
            AppLogger.Info("Socket", "Esperando clientes...");

            while (true)
            {
                Socket socketAccept = socketServer.Accept();
                AppLogger.Info("Socket", "Cliente aceptado en ServerIdentity.");

                Thread threadServer = new Thread(() =>
                {
                    ServiceIdentity(socketAccept).GetAwaiter().GetResult();
                });

                threadServer.Start();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Socket", $"Error fatal en ServerIdentity.\n{ex}");
        }
    }

    static async Task ServiceIdentity(object? o)
    {
        if (o is not Socket socket)
            return;

        User? currentUser = null;
        string? activeGroupCode = null;

        try
        {
            int option = SocketTools.receiveInt(socket);

            if (option == (int)MainUser.Login)
            {
                AppLogger.Info("Auth", "Cliente logeándose...");

                using AppDbContext context = new AppDbContext(connectionString);

                currentUser = CheckLogin(socket, context);

                if (currentUser is null)
                {
                    AppLogger.Warn("Auth", "Login fallido.");
                    return;
                }

                AppLogger.Info("Auth", $"[User:{currentUser.username}] Login correcto.");

                while (true)
                {
                    int groupOption = SocketTools.receiveInt(socket);

                    switch (groupOption)
                    {
                        case (int)MainMenuOption.CreateGroup:
                            {
                                CreateGroupService createGroupService = new CreateGroupService(context);
                                var result = await createGroupService.ExecuteAsync(socket, currentUser);

                                if (!result.Success)
                                {
                                    AppLogger.Warn("Lobby", $"[User:{currentUser.username}] No se pudo crear el grupo.");
                                    break;
                                }

                                GroupSession session = new GroupSession(
                                    result.GroupId,
                                    result.GroupCode,
                                    currentUser.id
                                );

                                session.AddMember(currentUser.id, currentUser.username);
                                groupSessionManager.Add(session);

                                activeGroupCode = result.GroupCode;

                                AppLogger.Info("Lobby", $"[Group:{result.GroupCode}] [User:{currentUser.username}] Grupo creado y sesión activa.");

                                await LobbyGroup(socket, result.GroupCode, currentUser);
                                activeGroupCode = null;
                                return;
                            }

                        case (int)MainMenuOption.JoinGroup:
                            {
                                string groupCode = SocketTools.receiveString(socket);

                                bool success = groupSessionManager.TryJoinGroup(
                                    groupCode,
                                    currentUser.id,
                                    currentUser.username
                                );

                                SocketTools.sendBool(socket, success);

                                if (success)
                                {
                                    activeGroupCode = groupCode;

                                    AppLogger.Info("Lobby", $"[Group:{groupCode}] [User:{currentUser.username}] Usuario unido al grupo.");
                                    await LobbyGroup(socket, groupCode, currentUser);
                                    activeGroupCode = null;
                                    return;
                                }

                                AppLogger.Warn("Lobby", $"[Group:{groupCode}] [User:{currentUser.username}] Join fallido.");
                                break;
                            }

                        case (int)MainMenuOption.GetHomeData:
                            {
                                AppLogger.Debug("Nav", $"[User:{currentUser.username}] El usuario está en el Home.");
                                SocketTools.sendString(currentUser.username, socket);
                                break;
                            }

                        case (int)MainMenuOption.GetProfileData:
                            {
                                AppLogger.Info("User", $"[User:{currentUser.username}] Consultando datos de perfil.");

                                SocketTools.sendString(currentUser.username, socket);
                                SocketTools.sendString(currentUser.email, socket);
                                SocketTools.sendString(currentUser.birth_date.ToString("dd/MM/yyyy"), socket);

                                break;
                            }

                        default:
                            {
                                AppLogger.Warn("Protocol", $"[User:{currentUser.username}] Opción no reconocida recibida: {groupOption}");
                                break;
                            }
                    }
                }
            }
            else if (option == (int)MainUser.Register)
            {
                AppLogger.Info("Auth", "Cliente registrándose...");

                using AppDbContext context = new AppDbContext(connectionString);
                Register(socket, context);

                AppLogger.Info("Auth", "Cliente registrado correctamente.");
            }
            else
            {
                AppLogger.Warn("Auth", $"Opción principal no válida recibida: {option}");
                SocketTools.sendBool(socket, false);
            }
        }
        catch (SocketException ex)
        {
            AppLogger.Warn("Socket", $"Cliente desconectado.\n{ex.Message}");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Auth", $"Error en ServiceIdentity.\n{ex}");

            try
            {
                SocketTools.sendBool(socket, false);
            }
            catch
            {
            }
        }
        finally
        {
            if (currentUser != null && activeGroupCode != null)
            {
                GroupSession? session = groupSessionManager.Get(activeGroupCode);

                if (session != null)
                {
                    session.RemoveMember(currentUser.id);
                    AppLogger.Warn("Lobby", $"[Group:{activeGroupCode}] [User:{currentUser.username}] Usuario eliminado por desconexión.");

                    if (session.MemberCount == 0)
                    {
                        groupSessionManager.Remove(activeGroupCode);
                        AppLogger.Info("Lobby", $"[Group:{activeGroupCode}] Sesión eliminada por quedar vacía.");
                    }
                }
            }

            socket.Close();
        }
    }

    public static User? CheckLogin(Socket socket, AppDbContext context)
    {
        string receiveEmail = SocketTools.receiveString(socket);
        string receivePassword = SocketTools.receiveString(socket);

        User? userInDb = context.Users
            .FirstOrDefault(u => u.email == receiveEmail && u.password == receivePassword);

        bool loginSuccessful = userInDb != null;
        SocketTools.sendBool(socket, loginSuccessful);

        return userInDb;
    }

    public static void Register(Socket socket, AppDbContext context)
    {
        string user = SocketTools.receiveString(socket);
        string email = SocketTools.receiveString(socket);
        string password = SocketTools.receiveString(socket);
        string date = SocketTools.receiveString(socket);

        AddUser(context, user, email, password, date);

        SocketTools.sendBool(socket, true);
    }

    public static void AddUser(AppDbContext context, string user, string email, string password, string date)
    {
        bool exists = context.Users.Any(u => u.username == user || u.email == email);

        if (exists)
            throw new InvalidOperationException("El usuario o email ya existe");

        User userAdd = new User
        {
            username = user,
            email = email,
            password = password,
            birth_date = DateOnly.Parse(date),
            created_at = DateTime.UtcNow
        };

        context.Users.Add(userAdd);
        context.SaveChanges();

        AppLogger.Info("Auth", $"[User:{user}] Usuario registrado correctamente en base de datos.");
    }

    static async Task LobbyGroup(Socket socket, string groupCode, User user)
    {
        while (true)
        {
            GroupSession? session = groupSessionManager.Get(groupCode);

            if (session == null)
            {
                SocketTools.sendBool(socket, false);
                AppLogger.Warn("Lobby", $"[Group:{groupCode}] [User:{user.username}] Sesión inválida.");
                return;
            }

            /*
             * Cabecera estándar del lobby.
             * El cliente debe leer siempre:
             * - bool sessionValid
             * - int memberCount
             * - bool hasStarted
             */
            SocketTools.sendBool(socket, true);
            SocketTools.sendInt(socket, session.MemberCount);
            SocketTools.sendBool(socket, session.HasStarted);

            int option;

            try
            {
                option = SocketTools.receiveInt(socket);
            }
            catch (SocketException)
            {
                AppLogger.Warn("Socket", $"[Group:{groupCode}] [User:{user.username}] Desconexión durante lobby.");
                return;
            }

            switch (option)
            {
                case (int)LobbyOption.Refresh:
                    {
                        AppLogger.Debug("Lobby", $"[Group:{groupCode}] [User:{user.username}] Refresh lobby.");
                        break;
                    }

                case (int)LobbyOption.Exit:
                    {
                        session.RemoveMember(user.id);

                        AppLogger.Info("Lobby", $"[Group:{groupCode}] [User:{user.username}] Usuario salió del grupo.");

                        if (session.MemberCount == 0)
                        {
                            groupSessionManager.Remove(groupCode);
                            AppLogger.Info("Lobby", $"[Group:{groupCode}] Sesión eliminada por quedar vacía.");
                        }

                        return;
                    }

                case (int)LobbyOption.Start:
                    {
                        if (user.id != session.OwnerUserId)
                        {
                            AppLogger.Warn("Lobby", $"[Group:{groupCode}] [User:{user.username}] Intento de start sin ser owner.");
                            SocketTools.sendBool(socket, false);
                            break;
                        }

                        bool started = session.Start();
                        SocketTools.sendBool(socket, started);

                        if (!started)
                            AppLogger.Warn("Lobby", $"[Group:{groupCode}] El grupo ya estaba iniciado.");
                        else
                            AppLogger.Info("Lobby", $"[Group:{groupCode}] [User:{user.username}] Grupo iniciado por owner.");

                        break;
                    }

                case (int)LobbyOption.SendLocation:
                    {
                        if (!session.HasStarted)
                        {
                            AppLogger.Warn("Location", $"[Group:{groupCode}] [User:{user.username}] Se intentó enviar ubicación antes de iniciar el grupo.");
                            SendErrorResult(socket, -2, "El grupo aún no se ha iniciado.");
                            break;
                        }

                        ReceiveLocationService receiveLocationService = new ReceiveLocationService();
                        bool allReceived = receiveLocationService.Execute(socket, session, user);

                        if (!allReceived)
                        {
                            AppLogger.Info("Location", $"[Group:{groupCode}] Aún faltan ubicaciones.");
                            SendPendingResult(socket);
                            break;
                        }

                        AppLogger.Info("Location", $"[Group:{groupCode}] Todas las ubicaciones recibidas.");
                        await SendRouteResult(socket, session, user);

                        /*
                         * Resultado final enviado. No seguimos esperando comandos de lobby.
                         */
                        return;
                    }

                case (int)LobbyOption.PollResult:
                    {
                        if (!session.AreAllLocationsReceived())
                        {
                            AppLogger.Debug("Lobby", $"[Group:{groupCode}] [User:{user.username}] PollResult: aún faltan ubicaciones.");
                            SendPendingResult(socket);
                            break;
                        }

                        AppLogger.Info("Lobby", $"[Group:{groupCode}] [User:{user.username}] PollResult: todas las ubicaciones listas.");
                        await SendRouteResult(socket, session, user);

                        /*
                         * Resultado final enviado. No seguimos esperando comandos de lobby.
                         */
                        return;
                    }

                default:
                    {
                        AppLogger.Warn("Lobby", $"[Group:{groupCode}] [User:{user.username}] Opción de lobby no válida: {option}");
                        break;
                    }
            }
        }
    }

    static void SendPendingResult(Socket socket)
    {
        var payload = new MeetingResultTransportModel
        {
            Latitude = 0,
            Longitude = 0,
            OriginLatitude = 0,
            OriginLongitude = 0,
            DurationSeconds = -1,
            DistanceMeters = 0,
            TransferCount = 0,
            HasValidRoute = false,
            MeetingPointName = "Pendiente",
            AddressText = "Esperando ubicaciones del resto del grupo",
            DistanceText = "Distancia no disponible",
            FairnessText = "Cálculo pendiente",
            Legs = new List<RouteLegDto>()
        };

        SocketTools.sendString(JsonSerializer.Serialize(payload), socket);
    }

    static void SendErrorResult(Socket socket, int statusCode, string message)
    {
        var payload = new MeetingResultTransportModel
        {
            Latitude = 0,
            Longitude = 0,
            OriginLatitude = 0,
            OriginLongitude = 0,
            DurationSeconds = statusCode,
            DistanceMeters = 0,
            TransferCount = 0,
            HasValidRoute = false,
            MeetingPointName = "Error",
            AddressText = message,
            DistanceText = "Distancia no disponible",
            FairnessText = "No se pudo calcular el punto de encuentro",
            Legs = new List<RouteLegDto>()
        };

        SocketTools.sendString(JsonSerializer.Serialize(payload), socket);
    }

    static async Task SendRouteResult(Socket socket, GroupSession session, User user)
    {
        IReadOnlyCollection<UserLocation> locations = session.GetAllLocations();

        List<GeometryUtils.GeographicLocation> points = locations
            .Select(l => new GeometryUtils.GeographicLocation(l.Latitude, l.Longitude))
            .ToList();

        GeometryUtils.GeographicLocation centroid = GeometryUtils.CalculateCentroid(points);

        AppLogger.Info(
            "Routing",
            $"[Group:{session.GroupCode}] [User:{user.username}] Centroide calculado: {centroid.Latitude}, {centroid.Longitude}");

        try
        {
            OTP otp = new OTP(otpHttpClient);

            UserLocation? currentLocation = session.GetLocation(user.id);

            if (currentLocation == null)
            {
                AppLogger.Warn(
                    "Routing",
                    $"[Group:{session.GroupCode}] [User:{user.username}] No se encontró ubicación del usuario actual.");

                SendErrorResult(socket, -2, "No se encontró la ubicación del usuario actual.");
                return;
            }

            OTP.Coordenada origin = new OTP.Coordenada(
                currentLocation.Latitude,
                currentLocation.Longitude);

            OTP.Coordenada destination = new OTP.Coordenada(
                centroid.Latitude,
                centroid.Longitude);

            /*
             * Bloque crítico:
             * solo permitimos una consulta OTP simultánea.
             */
            MeetingRouteResult? route;

            await otpSemaphore.WaitAsync();

            try
            {
                string jsonResponse = await otp.ConsultarAsync(origin, destination);
                route = otp.ExtraerResultadoRuta(jsonResponse);
            }
            finally
            {
                otpSemaphore.Release();
            }

            if (route == null)
            {
                AppLogger.Warn(
                    "Routing",
                    $"[Group:{session.GroupCode}] [User:{user.username}] Sin ruta disponible.");

                var noRoutePayload = new MeetingResultTransportModel
                {
                    Latitude = centroid.Latitude,
                    Longitude = centroid.Longitude,
                    OriginLatitude = currentLocation.Latitude,
                    OriginLongitude = currentLocation.Longitude,
                    DurationSeconds = -3,
                    DistanceMeters = 0,
                    TransferCount = 0,
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

            AppLogger.Info(
                "Routing",
                $"[Group:{session.GroupCode}] [User:{user.username}] Duración calculada: {route.DurationSeconds}s, distancia={route.DistanceMeters:F2}m, transbordos={route.TransferCount}");

            var payload = new MeetingResultTransportModel
            {
                Latitude = centroid.Latitude,
                Longitude = centroid.Longitude,
                OriginLatitude = currentLocation.Latitude,
                OriginLongitude = currentLocation.Longitude,
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
        }
        catch (TaskCanceledException ex)
        {
            AppLogger.Warn(
                "OTP",
                $"[Group:{session.GroupCode}] [User:{user.username}] Timeout consultando OTP.\n{ex.Message}");

            SendErrorResult(socket, -2, "OTP tardó demasiado en calcular la ruta.");
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                "OTP",
                $"[Group:{session.GroupCode}] [User:{user.username}] Fallo al consultar OTP.\n{ex}");

            SendErrorResult(socket, -2, "Error calculando la ruta en el servidor.");
        }
    }
}