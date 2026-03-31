using Data;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using NetUtils;

namespace Server
{
    internal class Program
    {
        // Fases // División // Server

        // Server para Login & Register
        // Server para la consulta ( solo? )
        // Server para las matematicas
        // Server para la gestion del grupo?
        public enum MainUser
        {
            Login = 1,
            Register = 2
        }

        public static string usuario = "Pepe";
        public static int password = 1234;
        public static string connectionString = "Host=localhost;Port=5432;Database=SGSDatabase;Username=postgres;Password=postgres123";

        static void serverAPI()
        {
            IPAddress address = IPAddress.Parse("192.168.1.36"); // hacerla auto
            IPEndPoint endPoint = new IPEndPoint(address, 1000);

            Socket socketServer = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socketServer.Bind(endPoint);
            socketServer.Listen();
            while (socketServer.IsBound)
            {
                Socket socketAccept = socketServer.Accept();
                Thread threadsServer = new Thread(serviceAPI);
                threadsServer.Start(socketAccept);
            }

        }
        static void serviceAPI(Object o)
        {
            Socket socket = (Socket)o;
            // Aplicar clases api a partir de lo que me da el cliente
        }
        static void serverIdentity()
        {
            try
            {
                IPAddress address = IPAddress.Parse("192.168.1.36"); // hacerla auto
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

                    Thread threadServer = new Thread(serviceIdentity);
                    threadServer.Start(socketAccept);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error fatal en serverIdentity:");
                Console.WriteLine(ex);
            }
        }
        static void serviceIdentity(object o)
        {
            Socket socket = (Socket)o;

            try
            {
                int option = SocketTools.receiveInt(socket);

                if (option == (int)MainUser.Login)
                {
                    Console.WriteLine("Cliente logeandose...");
                    using AppDbContext db = new AppDbContext(connectionString);
                    checkLogin(socket, db);
                }
                else if (option == (int)MainUser.Register)
                {
                    Console.WriteLine("Cliente registrandose...");
                    using AppDbContext db = new AppDbContext(connectionString);
                    register(socket, db);
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
                using AppDbContext db = new AppDbContext(connectionString);
                db.Database.EnsureCreated();
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

            Thread threadServerAPI = new Thread(serverAPI);
            threadServerAPI.Start();

            Thread threadServerIdentity = new Thread(serverIdentity);
            threadServerIdentity.Start();

            Console.WriteLine("Servidores corriendo. Pulsa ENTER para detenerlos.");
            Console.ReadLine();
        }



        public static void checkLogin(Socket socket, AppDbContext db) // Faltaria metodo que coge el usuario y el password
        {
            // 1. Recibes los datos que envía el cliente
            string receiveUser = SocketTools.receiveString(socket);
            string receivePassword = SocketTools.receiveString(socket);

            // 2. Buscas en la tabla Users un registro que coincida con AMBOS campos
            // Usamos LINQ para decir: "Traeme el primero que coincida con esto"
            var userInDb = db.Users.FirstOrDefault(u => u.username == receiveUser && u.password == receivePassword);

            // 3. Si 'userInDb' no es nulo, significa que encontró la combinación correcta
            bool loginSuccessful = (userInDb != null);

            // 4. Envías la respuesta al socket
            SocketTools.sendBool(socket, loginSuccessful);
        }
        public static void register(Socket socket, AppDbContext context)
        {
            string user = SocketTools.receiveString(socket);
            string email = SocketTools.receiveString(socket);
            string password = SocketTools.receiveString(socket);
            string date = SocketTools.receiveString(socket);

            addUser(context, user, email, password, date);

            SocketTools.sendBool(socket, true);

        }
        public static void addUser(AppDbContext context, string user, string email, string password, string date)
        {
            bool exists = context.Users.Any(u => u.username == user || u.email == email);
            if (exists)
                throw new InvalidOperationException("El usuario o email ya existe");

            AppDbContext.User userAdd = new AppDbContext.User
            {
                username = user,
                email = email,
                password = password, // luego esto hay que hashearlo, no guardarlo en plano
                birth_date = DateOnly.Parse(date)
            };

            context.Users.Add(userAdd);
            context.SaveChanges();

            Console.WriteLine($"Usuario {user} registrado correctamente en base de datos");
        }
    }
}
