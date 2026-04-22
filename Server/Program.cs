using NetUtils;
using Server.Algorithm;
using Server.API;
using Server.Data;
using Server.Group;
using Server.Group.GroupSessions;
using Server.UserRouting;
using Server.Infrastructure;
using System.Net;
using System.Net.Sockets;
using static Server.Data.AppDbContext;

namespace Server
{
    internal class Program
    {
        public enum MainUser
        {
            Login = 1,
            Register = 2
        }

        public enum MainGroup
        {
            CreateGroup = 1,
            JoinGroup = 2
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

        private static readonly HttpClient otpHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

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
            // Pendiente
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
                            case (int)MainGroup.CreateGroup:
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

                            case (int)MainGroup.JoinGroup:
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

                            default:
                                {
                                    AppLogger.Warn("Lobby", $"[User:{currentUser.username}] Opción de grupo no válida: {groupOption}");
                                    SocketTools.sendBool(socket, false);
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

                SocketTools.sendBool(socket, true);
                SocketTools.sendInt(socket, session.MemberCount);
                SocketTools.sendBool(socket, session.HasStarted);

                int option = SocketTools.receiveInt(socket);

                switch (option)
                {
                    case (int)LobbyOption.Refresh:
                        AppLogger.Debug("Lobby", $"[Group:{groupCode}] [User:{user.username}] Refresh lobby.");
                        break;

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
                                SocketTools.sendDouble(socket, 0);
                                SocketTools.sendDouble(socket, 0);
                                SocketTools.sendInt(socket, -1);
                                break;
                            }

                            ReceiveLocationService receiveLocationService = new ReceiveLocationService();
                            bool allReceived = receiveLocationService.Execute(socket, session, user);

                            if (!allReceived)
                            {
                                AppLogger.Info("Location", $"[Group:{groupCode}] Aún faltan ubicaciones.");
                                SocketTools.sendDouble(socket, 0);
                                SocketTools.sendDouble(socket, 0);
                                SocketTools.sendInt(socket, -1);
                                break;
                            }

                            AppLogger.Info("Location", $"[Group:{groupCode}] Todas las ubicaciones recibidas.");
                            await SendRouteResult(socket, session, user);
                            break;
                        }

                    case (int)LobbyOption.PollResult:
                        {
                            if (!session.AreAllLocationsReceived())
                            {
                                AppLogger.Debug("Lobby", $"[Group:{groupCode}] [User:{user.username}] PollResult: aún faltan ubicaciones.");
                                SocketTools.sendDouble(socket, 0);
                                SocketTools.sendDouble(socket, 0);
                                SocketTools.sendInt(socket, -1);
                                break;
                            }

                            AppLogger.Info("Lobby", $"[Group:{groupCode}] [User:{user.username}] PollResult: todas las ubicaciones listas.");
                            await SendRouteResult(socket, session, user);
                            break;
                        }

                    default:
                        AppLogger.Warn("Lobby", $"[Group:{groupCode}] [User:{user.username}] Opción de lobby no válida: {option}");
                        break;
                }
            }
        }

        static async Task SendRouteResult(Socket socket, GroupSession session, User user)
        {
            IReadOnlyCollection<UserLocation> locations = session.GetAllLocations();

            List<GeometryUtils.GeographicLocation> points = locations
                .Select(l => new GeometryUtils.GeographicLocation(l.Latitude, l.Longitude))
                .ToList();

            GeometryUtils.GeographicLocation centroid = GeometryUtils.CalculateCentroid(points);

            AppLogger.Info("Routing", $"[Group:{session.GroupCode}] [User:{user.username}] Centroide calculado: {centroid.Latitude}, {centroid.Longitude}");

            try
            {
                OTP otp = new OTP(otpHttpClient);

                UserLocation? currentLocation = session.GetLocation(user.id);

                if (currentLocation == null)
                {
                    AppLogger.Warn("Routing", $"[Group:{session.GroupCode}] [User:{user.username}] No se encontró ubicación del usuario actual.");
                    SocketTools.sendDouble(socket, 0);
                    SocketTools.sendDouble(socket, 0);
                    SocketTools.sendInt(socket, -2);
                    return;
                }

                OTP.Coordenada origin = new OTP.Coordenada(currentLocation.Latitude, currentLocation.Longitude);
                OTP.Coordenada destination = new OTP.Coordenada(centroid.Latitude, centroid.Longitude);

                string jsonResponse = await otp.ConsultarAsync(origin, destination);
                int? duration = otp.ExtraerDuracion(jsonResponse);

                if (duration == null)
                {
                    AppLogger.Warn("Routing", $"[Group:{session.GroupCode}] [User:{user.username}] Sin ruta disponible.");
                    SocketTools.sendDouble(socket, centroid.Latitude);
                    SocketTools.sendDouble(socket, centroid.Longitude);
                    SocketTools.sendInt(socket, -3);
                    return;
                }

                AppLogger.Info("Routing", $"[Group:{session.GroupCode}] [User:{user.username}] Duración calculada: {duration.Value}s");

                SocketTools.sendDouble(socket, centroid.Latitude);
                SocketTools.sendDouble(socket, centroid.Longitude);
                SocketTools.sendInt(socket, duration.Value);
            }
            catch (Exception ex)
            {
                AppLogger.Error("OTP", $"[Group:{session.GroupCode}] [User:{user.username}] Fallo al consultar OTP.\n{ex}");

                SocketTools.sendDouble(socket, 0);
                SocketTools.sendDouble(socket, 0);
                SocketTools.sendInt(socket, -2);
            }
        }
    }
}