using System.Net;
using System.Net.Sockets;
using NetUtils;
using Client.MainMenu;


namespace Client
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

        static void Main(string[] args) // Siguiente paso : Grupo, recibe pass, se une, mandan localizacion y hace punto geometrico. Tambien refactorizar
        {
            string ip = "192.168.1.36";
            int port = 1001;

            bool appRunning = true;

            while (appRunning)
            {
                Console.Clear();
                Console.WriteLine("JUST MEETING POINT");
                Console.WriteLine("================");
                Console.WriteLine("1- Iniciar Sesión");
                Console.WriteLine("2- Registrarse");
                Console.WriteLine("0- Salir");
                Console.WriteLine("================");
                Console.Write(">");

                if (!int.TryParse(Console.ReadLine(), out int option))
                {
                    Console.WriteLine("Opción no válida.");
                    Console.ReadKey();
                    continue;
                }

                switch (option)
                {
                    case (int)MainMenuOption.Login:
                        ProcessLogin(ip, port);
                        break;

                    case (int)MainMenuOption.Register:
                        Menu.ProcessRegister(ip, port);
                        break;

                    case 0:
                        appRunning = false;
                        break;

                    default:
                        Console.WriteLine("Opción no válida.");
                        Console.ReadKey();
                        break;
                }
            }
        }

        public static void ProcessLogin(string ip, int port)
        {
            bool tryLogin = true;

            while (tryLogin)
            {
                Console.Clear();
                Console.WriteLine("JUST MEETING POINT");
                Console.WriteLine("==================");
                Console.WriteLine("      LOGIN");
                Console.WriteLine("==================");

                Console.WriteLine("Usuario");
                Console.Write(">");
                string user = Console.ReadLine();

                Console.WriteLine("Contraseña");
                Console.Write(">");
                string password = Console.ReadLine();

                try
                {
                    using Socket socketClient = createSocketConnection(ip, port);

                    SocketTools.sendInt(socketClient, (int)MainMenuOption.Login);
                    SocketTools.sendString(user, socketClient);
                    SocketTools.sendString(password, socketClient);

                    bool login = SocketTools.receiveBool(socketClient);

                    if (!login)
                    {
                        Console.WriteLine($"Acceso: {login}");
                        Console.WriteLine("Compruebe que escribe bien sus datos");
                        Console.ReadKey();
                    }
                    else
                    {
                        Console.WriteLine("Login correcto");
                        Console.ReadKey();

                        // OJO:
                        // El serverIdentity cierra el socket después del login.
                        // Por tanto, a partir de aquí NO puedes seguir usando esta conexión.
                        // Si quieres menú post-login real, deberá ir por otro server o con otro protocolo.

                        ShowLoggedMenuPlaceholder();
                        tryLogin = false;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("No se ha podido completar el login");
                    Console.WriteLine(ex.Message);
                    Console.ReadKey();
                }
            }
        }

        public static void ShowLoggedMenuPlaceholder()
        {
            bool loginMenu = true;

            while (loginMenu)
            {
                Console.Clear();
                Console.WriteLine("JUST MEETING POINT");
                Console.WriteLine("==================");
                Console.WriteLine("1.- Iniciar Meeting Point");
                Console.WriteLine("0.- Salir");
                Console.Write(">");

                if (!int.TryParse(Console.ReadLine(), out int loginOption)) // Si no puedes pasar el valor a int entonces se activa el bloque
                {
                    Console.WriteLine("Opción no válida.");
                    Console.ReadKey();
                    continue;
                }

                switch (loginOption)
                {
                    case (int)MainLogin.MeetingPoint:
                        Console.WriteLine("Esta parte todavía no puede usar el mismo socket del login.");
                        Console.WriteLine("Necesitas otro servidor o abrir otra conexión con otro protocolo.");
                        Console.ReadKey();
                        break;

                    case (int)MainLogin.Exit:
                        loginMenu = false;
                        break;

                    default:
                        Console.WriteLine("Opción no válida.");
                        Console.ReadKey();
                        break;
                }
            }
        }


        public static Socket createSocketConnection(string ip, int port)
        {
            IPAddress address = IPAddress.Parse(ip);
            IPEndPoint endpoint = new IPEndPoint(address, port);

            Socket socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(endpoint);

            return socket;
        }
    }
}