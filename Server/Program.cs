using Data;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Server
{
    internal class Program
    {
        // Fases // División // Server

        // Server para Login & Register
        // Server para la consulta ( solo? )
        // Server para las matematicas
        // Server para la gestion del grupo?
        public  enum MainUser 
        {
            Login = 1,
            Register = 2
        }

        public static string usuario = "Pepe";
        public static int password = 1234;
        public static string connectionString = "Host=localhost;Port=5432;Database=SGSDatabase;Username=postgres;Password=postgres123";
        public static AppDbContext context = new AppDbContext(connectionString);
        static void serverAPI()
        {
            IPAddress address = IPAddress.Parse("192.168.1.34"); // hacerla auto
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
                IPAddress address = IPAddress.Parse("192.168.1.34"); // hacerla auto
                IPEndPoint endPoint = new IPEndPoint(address, 1001);

                Socket socketServer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
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
                int option = receiveInt(socket);

                if (option == (int)MainUser.Register)
                {
                    using AppDbContext db = new AppDbContext(connectionString);
                    register(socket, db);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                try { sendBool(socket, false); } catch { }
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

            context = new AppDbContext(connectionString);
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

            Thread threadServerAPI = new Thread(serverAPI);
            threadServerAPI.Start();
            Thread threadServerIdentity = new Thread(serverIdentity);
            threadServerIdentity.Start();

            Console.WriteLine("Servidores corriendo. Pulsa ENTER para detenerlos.");
            Console.ReadLine(); // <--- ESTO evita que el programa se cierre
        }
        public static string receiveString(Socket socket)
        {
            int length = receiveInt(socket);
            byte[] bytes = ReceiveExact(socket, length);
            return Encoding.UTF8.GetString(bytes);
        }
        public static int receiveInt(Socket socket)
        {
            byte[] bytes = ReceiveExact(socket, sizeof(int));
            return BitConverter.ToInt32(bytes, 0);
        }
        public static byte[] ReceiveExact(Socket socket, int size)
        {
            byte[] buffer = new byte[size];
            int totalRead = 0;

            while (totalRead < size)
            {
                int read = socket.Receive(buffer, totalRead, size - totalRead, SocketFlags.None);

                if (read == 0)
                    throw new SocketException((int)SocketError.ConnectionReset);

                totalRead += read;
            }

            return buffer;
        }
        public static void sendInt(Socket socket, int num)
        {
            byte[] bytes = BitConverter.GetBytes(num);
            socket.Send(bytes);
        }

        public static void sendBool(Socket socket, bool value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            socket.Send(bytes);
        }
        public static void checkLogin(string user, int password, Socket socket) // Faltaria metodo que coge el usuario y el password
        {
            string receiveUser = receiveString(socket);
            int receivePassword = receiveInt(socket);
            if (receiveUser == user && receivePassword == password)
            {
                byte[] bytes = BitConverter.GetBytes(true);
                socket.Send(bytes);
            }
            else
            {
                byte[] bytes = BitConverter.GetBytes(false);
                socket.Send(bytes);
            }
        }
        public static void register(Socket socket, AppDbContext context)
        {
            string user = receiveString(socket);
            string email = receiveString(socket);
            string password = receiveString(socket);
            string date = receiveString(socket);

            addUser(context, user, email, password, date);

            sendBool(socket, true);
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
