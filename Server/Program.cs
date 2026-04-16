using NetUtils;
using Server.Algorithm;
using Server.API;
using Server.Data;
using Server.Group;
using Server.Group.GroupSessions;
using Server.UserRouting;
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
            PollResult = 5   // NUEVO: el cliente ya envió su ubicación y pregunta si ya están todos
        }

        public static string connectionString =
            "Host=localhost;Port=5432;Database=SGSDatabase;Username=postgres;Password=postgres123";

        private static readonly GroupSessionManager groupSessionManager = new();

        static void Main(string[] args)
        {
            try
            {
                using AppDbContext context = new AppDbContext(connectionString);
                context.Database.EnsureCreated();

                Console.WriteLine("Base de datos creada/verificada correctamente.");
                Thread.Sleep(1000);
                Console.Clear();
            }
            catch (Exception ex)
            {
                Console.WriteLine("No ha sido posible crear/verificar la base de datos.");
                Console.WriteLine(ex);
                Thread.Sleep(1500);
                Console.Clear();
            }

            Thread threadServerAPI = new Thread(ServerAPI);
            threadServerAPI.Start();

            Thread threadServerIdentity = new Thread(ServerIdentity);
            threadServerIdentity.Start();

            Console.WriteLine("Servidores corriendo. Pulsa ENTER para detenerlos.");
            Console.ReadLine();
        }

        static void ServerAPI()
        {
            IPAddress address = IPAddress.Parse("192.168.1.37");
            IPEndPoint endPoint = new IPEndPoint(address, 1000);

            Socket socketServer = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socketServer.Bind(endPoint);
            socketServer.Listen();

            while (socketServer.IsBound)
            {
                Socket socketAccept = socketServer.Accept();
                Thread threadsServer = new Thread(ServiceAPI);
                threadsServer.Start(socketAccept);
            }
        }

        static void ServiceAPI(object? o)
        {
            if (o is not Socket socket)
                return;

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

                Console.WriteLine("Server Identity escuchando en el puerto 1001");
                Console.WriteLine("Esperando clientes...");

                while (true)
                {
                    Socket socketAccept = socketServer.Accept();
                    Console.WriteLine("Cliente aceptado");

                    Thread threadServer = new Thread(() =>
                    {
                        ServiceIdentity(socketAccept).GetAwaiter().GetResult();
                    });

                    threadServer.Start();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error fatal en ServerIdentity:");
                Console.WriteLine(ex);
            }
        }

        static async Task ServiceIdentity(object? o)
        {
            if (o is not Socket socket)
                return;

            // Guardamos usuario y código de grupo para poder limpiar en el finally
            // si el cliente se desconecta abruptamente sin enviar Exit.
            User? currentUser = null;
            string? activeGroupCode = null;

            try
            {
                int option = SocketTools.receiveInt(socket);

                if (option == (int)MainUser.Login)
                {
                    Console.WriteLine("Cliente logeándose...");

                    using AppDbContext context = new AppDbContext(connectionString);

                    currentUser = CheckLogin(socket, context);

                    if (currentUser is null)
                        return;

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
                                        Console.WriteLine("[WARN] No se pudo crear el grupo.");
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

                                    Console.WriteLine($"[INFO] Grupo creado y sesión activa: {result.GroupCode}");

                                    await LobbyGroup(socket, result.GroupCode, currentUser);
                                    activeGroupCode = null; // salió correctamente por Exit
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

                                        Console.WriteLine($"[INFO] Usuario {currentUser.username} unido al grupo {groupCode}");
                                        await LobbyGroup(socket, groupCode, currentUser);
                                        activeGroupCode = null; // salió correctamente por Exit
                                        return;
                                    }

                                    Console.WriteLine($"[WARN] Join fallido para grupo {groupCode}");
                                    break;
                                }

                            default:
                                {
                                    SocketTools.sendBool(socket, false);
                                    break;
                                }
                        }
                    }
                }
                else if (option == (int)MainUser.Register)
                {
                    Console.WriteLine("Cliente registrándose...");

                    using AppDbContext context = new AppDbContext(connectionString);
                    Register(socket, context);

                    Console.WriteLine("Cliente registrado correctamente");
                }
                else
                {
                    Console.WriteLine("Opción no válida recibida");
                    SocketTools.sendBool(socket, false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error en ServiceIdentity:");
                Console.WriteLine(ex);

                try
                {
                    SocketTools.sendBool(socket, false);
                }
                catch { }
            }
            finally
            {
                // CORRECCIÓN: si el cliente se cayó sin enviar Exit, lo eliminamos
                // de la sesión para que no quede como miembro zombie.
                if (currentUser != null && activeGroupCode != null)
                {
                    GroupSession? session = groupSessionManager.Get(activeGroupCode);

                    if (session != null)
                    {
                        session.RemoveMember(currentUser.id);
                        Console.WriteLine($"[INFO] Usuario {currentUser.username} eliminado de {activeGroupCode} por desconexión.");

                        if (session.MemberCount == 0)
                        {
                            groupSessionManager.Remove(activeGroupCode);
                            Console.WriteLine($"[INFO] Sesión {activeGroupCode} eliminada por quedar vacía.");
                        }
                    }
                }

                socket.Close();
            }
        }

        public static User? CheckLogin(Socket socket, AppDbContext context)
        {
            string receiveUser = SocketTools.receiveString(socket);
            string receivePassword = SocketTools.receiveString(socket);

            User? userInDb = context.Users
                .FirstOrDefault(u => u.username == receiveUser && u.password == receivePassword);

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

            Console.WriteLine($"Usuario {user} registrado correctamente en base de datos");
        }

        static async Task LobbyGroup(Socket socket, string groupCode, User user)
        {
            while (true)
            {
                GroupSession? session = groupSessionManager.Get(groupCode);

                // CORRECCIÓN: antes solo se enviaba un int y el cliente se quedaba
                // esperando el bool de HasStarted → cuelgue. Ahora se envía primero
                // una señal booleana para indicar si la sesión sigue válida.
                if (session == null)
                {
                    SocketTools.sendBool(socket, false); // señal: sesión inválida
                    return;
                }

                SocketTools.sendBool(socket, true); // señal: sesión válida
                SocketTools.sendInt(socket, session.MemberCount);
                SocketTools.sendBool(socket, session.HasStarted);

                int option = SocketTools.receiveInt(socket);

                switch (option)
                {
                    case (int)LobbyOption.Refresh:
                        break;

                    case (int)LobbyOption.Exit:
                        {
                            session.RemoveMember(user.id);

                            Console.WriteLine($"[INFO] Usuario {user.username} salió del grupo {groupCode}");

                            if (session.MemberCount == 0)
                            {
                                groupSessionManager.Remove(groupCode);
                                Console.WriteLine($"[INFO] Sesión {groupCode} eliminada por quedar vacía.");
                            }

                            return;
                        }

                    case (int)LobbyOption.Start:
                        {
                            if (user.id != session.OwnerUserId)
                            {
                                SocketTools.sendBool(socket, false);
                                break;
                            }

                            bool started = session.Start();
                            SocketTools.sendBool(socket, started);

                            if (!started)
                                Console.WriteLine($"[WARN] El grupo {groupCode} ya estaba iniciado.");
                            else
                                Console.WriteLine($"[INFO] Grupo {groupCode} iniciado por owner");

                            break;
                        }

                    case (int)LobbyOption.SendLocation:
                        {
                            if (!session.HasStarted)
                            {
                                Console.WriteLine("[WARN] Se intentó enviar ubicación antes de iniciar el grupo.");
                                SocketTools.sendDouble(socket, 0);
                                SocketTools.sendDouble(socket, 0);
                                SocketTools.sendInt(socket, -1);
                                break;
                            }

                            ReceiveLocationService receiveLocationService = new ReceiveLocationService();
                            bool allReceived = receiveLocationService.Execute(socket, session, user);

                            if (!allReceived)
                            {
                                Console.WriteLine($"[INFO] Aún faltan ubicaciones en el grupo {groupCode}.");
                                SocketTools.sendDouble(socket, 0);
                                SocketTools.sendDouble(socket, 0);
                                SocketTools.sendInt(socket, -1);
                                break;
                            }

                            // Todos han enviado → calculamos y respondemos a este usuario
                            await SendRouteResult(socket, session, user);
                            break;
                        }

                    // NUEVO: el usuario ya envió su ubicación y hace polling
                    // esperando a que el resto del grupo también la envíe.
                    case (int)LobbyOption.PollResult:
                        {
                            if (!session.AreAllLocationsReceived())
                            {
                                Console.WriteLine($"[INFO] PollResult: aún faltan ubicaciones en {groupCode}.");
                                SocketTools.sendDouble(socket, 0);
                                SocketTools.sendDouble(socket, 0);
                                SocketTools.sendInt(socket, -1);
                                break;
                            }

                            Console.WriteLine($"[INFO] PollResult: todas las ubicaciones listas en {groupCode}.");
                            await SendRouteResult(socket, session, user);
                            break;
                        }

                    default:
                        break;
                }
            }
        }

        /// <summary>
        /// Calcula el centroide, consulta OTP y envía el resultado de ruta al cliente.
        /// Extraído como método para no duplicar el bloque en SendLocation y PollResult.
        /// </summary>
        static async Task SendRouteResult(Socket socket, GroupSession session, User user)
        {
            IReadOnlyCollection<UserLocation> locations = session.GetAllLocations();

            List<GeometryUtils.GeographicLocation> points = locations
                .Select(l => new GeometryUtils.GeographicLocation(l.Latitude, l.Longitude))
                .ToList();

            GeometryUtils.GeographicLocation centroid = GeometryUtils.CalculateCentroid(points);

            Console.WriteLine($"[INFO] Centroide calculado: {centroid.Latitude}, {centroid.Longitude}");

            try
            {
                using HttpClient httpClient = new HttpClient();
                OTP otp = new OTP(httpClient);

                UserLocation? currentLocation = session.GetLocation(user.id);

                if (currentLocation == null)
                {
                    Console.WriteLine("[WARN] No se encontró ubicación del usuario actual.");
                    SocketTools.sendDouble(socket, 0);
                    SocketTools.sendDouble(socket, 0);
                    SocketTools.sendInt(socket, -1);
                    return;
                }

                OTP.Coordenada origin = new OTP.Coordenada(currentLocation.Latitude, currentLocation.Longitude);
                OTP.Coordenada destination = new OTP.Coordenada(centroid.Latitude, centroid.Longitude);

                string jsonResponse = await otp.ConsultarAsync(origin, destination, "foot");
                int duration = otp.ExtraerDuracion(jsonResponse);

                Console.WriteLine($"[INFO] Duración calculada para user {user.id}: {duration}s");

                SocketTools.sendDouble(socket, centroid.Latitude);
                SocketTools.sendDouble(socket, centroid.Longitude);
                SocketTools.sendInt(socket, duration);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] Fallo al consultar OTP:");
                Console.WriteLine(ex.Message);

                SocketTools.sendDouble(socket, 0);
                SocketTools.sendDouble(socket, 0);
                SocketTools.sendInt(socket, -2);
            }
        }
    }
}