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

        public static string connectionString =
            "Host=localhost;Port=5432;Database=SGSDatabase;Username=Alumno;Password=AlumnoIFP";

        // Grupos activos en memoria
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
            IPAddress address = IPAddress.Parse("192.168.111.48");
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
                IPAddress address = IPAddress.Parse("192.168.111.48");
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

            try
            {
                int option = SocketTools.receiveInt(socket);

                if (option == (int)MainUser.Login)
                {
                    Console.WriteLine("Cliente logeándose...");

                    using AppDbContext context = new AppDbContext(connectionString);

                    User? currentUser = CheckLogin(socket, context);

                    if (currentUser is null)
                        return;

                    while (true)
                    {
                        int groupOption = SocketTools.receiveInt(socket);

                        switch (groupOption)
                        {
                            case (int)MainGroup.CreateGroup:
                                {
                                    var createGroupService = new CreateGroupService(context);

                                    // IMPORTANTE:
                                    // ExecuteAsync debe devolver:
                                    // (bool Success, int GroupId, string GroupCode)
                                    var result = await createGroupService.ExecuteAsync(socket, currentUser);

                                    if (!result.Success)
                                    {
                                        Console.WriteLine("[WARN] No se pudo crear el grupo.");
                                        break;
                                    }

                                    // Creamos sesión activa en memoria
                                    var session = new GroupSession(
                                        result.GroupId,
                                        result.GroupCode,
                                        currentUser.id
                                    );

                                    // El creador entra automáticamente al grupo
                                    session.AddMember(currentUser.id, currentUser.username);

                                    // Guardamos la sesión en memoria
                                    groupSessionManager.Add(session);

                                    Console.WriteLine($"[INFO] Grupo creado y sesión activa: {result.GroupCode}");

                                    // Entramos al lobby directamente
                                    LobbyGroup(socket, result.GroupCode, currentUser); // Siguiente paso son los calculos

                                    // Al salir del lobby, terminamos este flujo
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
                                        Console.WriteLine($"[INFO] Usuario {currentUser.username} unido al grupo {groupCode}");
                                        LobbyGroup(socket, groupCode, currentUser);
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
                catch
                {
                    // Ignorado a propósito
                }
            }
            finally
            {
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

        static async void LobbyGroup(Socket socket, string groupCode, User user)
        {
            while (true)
            {
                var session = groupSessionManager.Get(groupCode);

                // Si la sesión ya no existe, avisamos y salimos
                if (session == null)
                {
                    SocketTools.sendInt(socket, -1);
                    return;
                }

                // Enviamos cuántos miembros hay ahora mismo
                SocketTools.sendInt(socket, session.MemberCount);

                // Esperamos opción del cliente:
                // 1 = refresh
                // 2 = salir
                int option = SocketTools.receiveInt(socket);

                switch (option)
                {
                    case 1:
                        // refresh -> no hacemos nada, el while vuelve a iterar
                        break;

                    case 2:
                        session.RemoveMember(user.id);

                        Console.WriteLine($"[INFO] Usuario {user.username} salió del grupo {groupCode}");

                        // Si quieres, aquí podrías eliminar el grupo si queda vacío: => cuando quedaria vacio?
                        // if (session.MemberCount == 0)
                        // {
                        //     groupSessionManager.Remove(groupCode);
                        // }

                        return;
                    case 3: // start (solo owner)
                        {
                            if (user.id != session.OwnerUserId)
                            {
                                SocketTools.sendBool(socket, false);
                                break;
                            }

                            bool started = session.Start();

                            SocketTools.sendBool(socket, started);

                            if (!started)
                            {
                                Console.WriteLine($"[WARN] El grupo {groupCode} ya estaba iniciado.");
                                break;
                            }

                            Console.WriteLine($"[INFO] Grupo {groupCode} iniciado por owner");

                            ReceiveLocationService receiveLocationService = new ReceiveLocationService();
                            bool allReceived = receiveLocationService.Execute(socket, session, user);

                            if (allReceived)
                            {
                                Console.WriteLine("[INFO] Ya están todas las ubicaciones. Listo para procesar.");

                                IReadOnlyCollection<UserLocation> locations = session.GetAllLocations();

                                List<GeometryUtils.GeographicLocation> points = locations
                                    .Select(location => new GeometryUtils.GeographicLocation(location.Latitude, location.Longitude))
                                    .ToList();

                                GeometryUtils.GeographicLocation centroid = GeometryUtils.CalculateCentroid(points);

                                Console.WriteLine($"[INFO] Punto geométrico calculado: {centroid.Latitude}, {centroid.Longitude}");

                                using HttpClient httpClient = new HttpClient();
                                OTP otp = new OTP(httpClient);

                                OTP.Coordenada destination = new OTP.Coordenada(centroid.Latitude, centroid.Longitude);

                                foreach (UserLocation location in locations)
                                {
                                    OTP.Coordenada origin = new OTP.Coordenada(location.Latitude, location.Longitude);

                                    string jsonResponse = await otp.ConsultarAsync(origin, destination, "foot");
                                    int duration = otp.ExtraerDuracion(jsonResponse);

                                    Console.WriteLine($"[INFO] Usuario {location.UserId} tarda {duration} segundos al punto geométrico.");
                                }
                            }

                            break;
                        }



                        return; // salimos del lobby            

                    default:
                        // Opción no válida: seguimos en el lobby
                        break;
                }
            }
        }

    }
}