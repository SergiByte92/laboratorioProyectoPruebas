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
        public static AppDbContext context;
        public static string connectionString = "Host=localhost;Port=5432;Database=SGSDatabase;Username=postgres;Password=postgres\"";
        static void serverAPI()
        {
            IPAddress address = IPAddress.Parse("192.168.1.33"); // hacerla auto
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
        static void serverIdentity() // En realidad puede hacer tanto registro como login, seria disternir la opcion, mandarle una opcion.
        {
            IPAddress address = IPAddress.Parse("192.168.1.33"); // hacerla auto
            IPEndPoint endPoint = new IPEndPoint(address, 1001);

            Socket socketServer = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socketServer.Bind(endPoint);
            socketServer.Listen();
            Console.WriteLine("Server Identity escuchando en el puerto 1001");
            Console.WriteLine("Esperando clientes...");

            // Si es login entonces bloque login, si es Register, Bloque Register
            while (socketServer.IsBound)
            {
                Socket socketAccept = socketServer.Accept();
                Console.WriteLine("Cliente aceptado");
                Thread threadsServer = new Thread(serviceIdentity);
                threadsServer.Start(socketAccept);
            }

        }
        static void serviceIdentity(Object o)
        {
            Socket socket = (Socket)o;
            
            //checkLogin(usuario,password,socket);
            byte[] bytes = new byte[sizeof(int)];
            socket.Receive(bytes);
            int option = BitConverter.ToInt32(bytes);

            // Enum Opciones
            if (option == (int)MainUser.Login)
            {

            }
            else if (option == (int)MainUser.Register)
            {
                // Menu registro

                try
                {

                    register(socket, context);
                }
                catch (Exception ex) 
                {
                    Console.WriteLine("El usuario no ha sido registrado correctamente");
                    Console.WriteLine(ex.ToString());
                    sendBool(socket, false);
                }
            }
            // Aplicar clases api a partir de lo que me da el cliente
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
        }
        public static string receiveString(Socket socket) // Encapsular esto en un metodo de recibir credenciales
        {
            byte[] bytes = new byte[sizeof(int)];
            socket.Receive(bytes); // Recibo el Tamaño
            socket.Receive(bytes); // Recibo el mensaje
            return Encoding.UTF8.GetString(bytes);
        }
        public static int receiveInt(Socket socket)
        {
            byte[] bytes = new byte[sizeof(int)];
            socket.Receive(bytes);
            return BitConverter.ToInt32(bytes);
        }
        public static void sendInt(Socket socket,int num) 
        {
            byte[]bytes = BitConverter.GetBytes(num);
            socket.Send(bytes);
        }
        public static void sendBool(Socket socket, bool booleano) 
        {
            byte[] bytes = BitConverter.GetBytes(booleano);
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
            string data = receiveString(socket);
            
            addUser(context, user, email, password, data);

            sendBool(socket, true);
            
        }
        public static void addUser(AppDbContext context,string user, string email,string password,string data) 
        {
            AppDbContext.users userAdd = new AppDbContext.users(); // En este caso daremos por hecho que es nuevo usuario pero habria que verificar si existe

            userAdd.username = user;
            userAdd.email = email;
            userAdd.password = password;
            userAdd.birth_date = DateOnly.Parse(data);

            context.User.Add(userAdd);

            Console.WriteLine($"El usuario {user} ha sido registrado correctamente en la base de datos ");
            Console.WriteLine($"Se le manda al {user} la confirmación)");
        }
    }
}
