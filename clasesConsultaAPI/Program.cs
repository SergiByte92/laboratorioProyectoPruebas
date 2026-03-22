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

        public static string usuario = "Pepe";
        public static int password = 1234;
        static void serverAPI() 
        {
            IPAddress address = IPAddress.Parse("192.168.111.43"); // hacerla auto
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
            IPAddress address = IPAddress.Parse("192.168.111.43"); // hacerla auto
            IPEndPoint endPoint = new IPEndPoint(address, 1001);

            Socket socketServer = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socketServer.Bind(endPoint);
            socketServer.Listen();
            Console.WriteLine("Server Identity escuchando en el puerto 1001");
            Console.WriteLine("Esperando clientes...");
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
            checkLogin(usuario,password,socket);    
            // Aplicar clases api a partir de lo que me da el cliente
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");
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
        public static void checkLogin(string user,int password,Socket socket) // Faltaria metodo que coge el usuario y el password
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
    }
}
