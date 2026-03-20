using System.Net;
using System.Net.Sockets;

namespace clasesConsultaAPI
{
    internal class Program
    {
        // Fases // División // Server

        // Server para Login & Register
        // Server para la consulta ( solo? )
        // Server para las matematicas
        // Server para la gestion del grupo?
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
        static void serverIdentity()
        {
            IPAddress address = IPAddress.Parse("192.168.111.43"); // hacerla auto
            IPEndPoint endPoint = new IPEndPoint(address, 1001);

            Socket socketServer = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socketServer.Bind(endPoint);
            socketServer.Listen();
            while (socketServer.IsBound)
            {
                Socket socketAccept = socketServer.Accept();
                Thread threadsServer = new Thread(serviceIdentity);
                threadsServer.Start(socketAccept);
            }

        }
        static void serviceIdentity(Object o)
        {
            Socket socket = (Socket)o;
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
    }
}
