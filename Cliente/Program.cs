using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Cliente
{
    internal class Program
    {
        public enum MainMenuOption 
        {
            Login = 1,
            Register = 2,

        }
        static void Main(string[] args)
        {
            string ip = "12234454";
            int port = 1000;
            Socket socketClient = createSocketConnection(ip,port);
            string line;
            bool login = false;
            bool register = false;  
            while (true)
            {

                Console.WriteLine("JUST MEET POINT");
                Console.WriteLine("================");
                Console.WriteLine("1- Iniciar Sesión");
                Console.WriteLine("2- Registrarse"); // Usuario , Contraseña, Mail, => No corre prisa de momento
                Console.WriteLine("0- Salir");
                Console.WriteLine("================");
                Console.Write(">");

                int option = Int32.Parse(Console.ReadLine());


                if(option == (int)MainMenuOption.Login) 
                {
                    Console.WriteLine("Usuario");
                    Console.Write(">");
                    line = Console.ReadLine();
                    sendString(line, socketClient);

                    Console.WriteLine("Contraseña");
                    Console.Write(">");
                    int password = Int32.Parse(Console.ReadLine());
                    sendInt(password, socketClient);

                    // recibir respuesta true o false

                    if(login == true) 
                    {

                    }

             
                }
                else if(option == (int)MainMenuOption.Register) 
                {
                    Console.WriteLine("Usuario");
                    Console.Write(">");
                    line = Console.ReadLine();
                    sendString(line, socketClient);

                    Console.WriteLine("Contraseña");
                    Console.Write(">");
                    int password = Int32.Parse(Console.ReadLine());
                    sendInt(password, socketClient);

                    // recibir respues, register true o false

                    if(register == true) 
                    {

                    }
                }
                else 
                {
                    return;
                }
                

            }



            // Manda Contraseña
            // De momento Solo en memoria => Deberia ser ficheros => Aprobechar y encriptar?





        }
        public static Socket createSocketConnection(string ip, int port)
        {

            IPAddress address = IPAddress.Parse(ip);
            IPEndPoint endpoint = new IPEndPoint(address, port);

            Socket socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(endpoint);
            return socket;

        }
        public static void sendInt(int num, Socket socket)
        {
            byte[] bytes = BitConverter.GetBytes(num);
            socket.Send(bytes);
        }
        public static void sendString(string message, Socket socket)
        {
            int size = message.Length;
            byte[] bytes = BitConverter.GetBytes(size);
            socket.Send(bytes);

            bytes = Encoding.UTF8.GetBytes(message);
            socket.Send(bytes);

        }




    }

}

