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
        public enum MainLogin
        {
            MeetingPoint = 1,
            Exit = 0,
        }
        static void Main(string[] args)
        {

            string line;
            bool login = false;
            bool register = false;
            while (true)
            {
                string ip = "192.168.111.43";
                int port = 1001;
                Socket socketClient = createSocketConnection(ip, port);

                Console.WriteLine("JUST MEETING POINT");
                Console.WriteLine("================");
                Console.WriteLine("1- Iniciar Sesión"); // Luego de iniciar sesion, seria 
                Console.WriteLine("2- Registrarse"); // Usuario , Contraseña, Mail, => No corre prisa de momento
                Console.WriteLine("0- Salir");
                Console.WriteLine("================");
                Console.Write(">");

                int option = Int32.Parse(Console.ReadLine());


                if (option == (int)MainMenuOption.Login) // 1 Socket solo para el login 
                {
                    // Login

                    // Recogida de datos

                    string user;
                    int password;

                    Console.WriteLine("Usuario");
                    Console.Write(">");
                    user = Console.ReadLine();

                    Console.WriteLine("Contraseña");
                    Console.Write(">");
                    password = Int32.Parse(Console.ReadLine());

                    // Ejecutar comando

                    sendString(user, socketClient);
                    sendInt(password, socketClient);

                    // Mostrar resultado
                    login = receiveBool(socketClient);

                    Console.WriteLine($"Acceso :{login}");
                   
                    // recibir respuesta true o false

                    while (login)  // Banderas y deberia ser otro menu con todo. Retroceder seria login = false, deberia ser un while
                    {
                        Console.WriteLine("JUST MEETING POINT");
                        Console.WriteLine("==================");
                        Console.WriteLine("1.- Iniciar Meeting Point");
                        Console.WriteLine("2.- Salir");
                        Console.Write(">");
                        option = Int32.Parse(Console.ReadLine());

                        if (option == (int)MainLogin.MeetingPoint)
                        {

                            Console.WriteLine("JUST MEETING POINT");
                            Console.WriteLine("==================");

                            Console.WriteLine("Envie Coordenadas");
                            Console.WriteLine("Coordenada de Longitud?");
                            double longitud = double.Parse(Console.ReadLine());
                            sendDouble(longitud, socketClient);


                            Console.WriteLine("Coordenada de latitud");
                            Console.Write(">");
                            double latitud = double.Parse(Console.ReadLine());
                            sendDouble(latitud, socketClient);

                            // Recibir Punto final y ver como lo imprimo
                        }
                        if (option == (int)MainLogin.Exit)
                        {
                            login = false;
                        }

                    }


                }
                else if (option == (int)MainMenuOption.Register) // 1 socket solo para el registro
                {
                    // Register

                    Console.WriteLine("Nombre de Usuario");
                    Console.Write(">");
                    line = Console.ReadLine();
                    sendString(line, socketClient);

                    Console.WriteLine("Contraseña");
                    Console.Write(">");
                    int password = Int32.Parse(Console.ReadLine());
                    sendInt(password, socketClient);

                    Console.WriteLine("");

                    // recibir respues, register true o false

                    if (register == true)
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
        public static bool receiveBool(Socket socket) 
        {
            byte[] bytes = new byte[sizeof(bool)];
            socket.Receive(bytes);
            return BitConverter.ToBoolean(bytes);
        }
        public static void sendDouble(double coordenadas, Socket socket)
        {
            byte[] bytes = BitConverter.GetBytes(coordenadas);
            socket.Send(bytes);
        }




    }

}

