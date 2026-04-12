using NetUtils;
using Server.API;
using Server.Data;
using Server.Group;
using Server.Group.GroupCode;
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
            JoinGroup = 2,
        }

        public static string connectionString = "Host=localhost;Port=5432;Database=SGSDatabase;Username=Alumno;Password=AlumnoIFP";

        static void ServerAPI()
        {
            IPAddress address = IPAddress.Parse("192.168.111.52");
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

        static void ServiceAPI(object o)
        {
            Socket socket = (Socket)o;
            // Pendiente
        }

        static void ServerIdentity()
        {
            try
            {
                IPAddress address = IPAddress.Parse("192.168.111.52");
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
                Console.WriteLine("Error fatal en serverIdentity:");
                Console.WriteLine(ex);
            }
        }

        static async Task ServiceIdentity(object o)
        {
            Socket socket = (Socket)o;

            try
            {
                int option = SocketTools.receiveInt(socket);

                if (option == (int)MainUser.Login)
                {
                    Console.WriteLine("Cliente logeandose...");
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
                                    await createGroupService.ExecuteAsync(socket, currentUser); // de aqui si ha sido exitoso, seria irse a la pantalla de Join
                                    // Si es el que lo ha creado saldra unas cosas o otras
                                    break;
                                }

                            case (int)MainGroup.JoinGroup:
                                {
                                    SocketTools.sendBool(socket, false); // pantalla de espera
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
                    Console.WriteLine("Cliente registrandose...");
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
                Console.WriteLine(ex);
                try { SocketTools.sendBool(socket, false); } catch { }
            }
            finally
            {
                socket.Close();
            }
        }

        static void Main(string[] args)
        {
            try
            {
                using AppDbContext context = new AppDbContext(connectionString);
                context.Database.EnsureCreated();
                Console.WriteLine("Creadas las bases de datos");
                Thread.Sleep(1000);
                Console.Clear();
            }
            catch (Exception ex)
            {
                Console.WriteLine("No ha sido posible crear las tablas");
                Console.WriteLine(ex);
                Thread.Sleep(1000);
                Console.Clear();
            }

            Thread threadServerAPI = new Thread(ServerAPI);
            threadServerAPI.Start();

            Thread threadServerIdentity = new Thread(ServerIdentity);
            threadServerIdentity.Start();

            Console.WriteLine("Servidores corriendo. Pulsa ENTER para detenerlos.");
            Console.ReadLine();
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

            AppDbContext.User userAdd = new AppDbContext.User
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
    }
}