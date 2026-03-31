using System.Net;
using System.Net.Sockets;
using NetUtils;

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

        static void Main(string[] args)
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
                        ProcessRegister(ip, port);
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

        public static void ProcessRegister(string ip, int port)
        {
            bool register = true;

            while (register)
            {
                try
                {
                    using Socket socketClient = createSocketConnection(ip, port);

                    bool datoEnviado = MainRegister(socketClient);

                    if (!datoEnviado)
                    {
                        continue;
                    }

                    bool answerRegister = SocketTools.receiveBool(socketClient);

                    if (answerRegister)
                    {
                        Console.WriteLine("Ha sido registrado correctamente");
                        register = false;
                    }
                    else
                    {
                        Console.WriteLine("Ha habido algún error en el registro");
                    }

                    Console.ReadKey();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("No se ha podido completar el registro");
                    Console.WriteLine(ex.Message);
                    Console.ReadKey();
                }
            }
        }

        public static bool MainRegister(Socket socket)
        {
            Console.Clear();

            SocketTools.sendInt(socket, (int)MainMenuOption.Register);

            Console.WriteLine("JUST MEETING POINT");
            Console.WriteLine("==================");
            Console.WriteLine("    REGISTER");
            Console.WriteLine("==================");

            Console.WriteLine("- Usuario");
            Console.Write("> ");
            string user = Console.ReadLine();

            Console.WriteLine("- Email");
            Console.Write("> ");
            string email = Console.ReadLine();

            Console.WriteLine("- Password");
            Console.Write("> ");
            string password = Console.ReadLine();

            Console.WriteLine("- Repita el Password");
            Console.Write("> ");
            string repeatPassword = Console.ReadLine();

            Console.WriteLine("- Fecha de Nacimiento (yyyy-MM-dd)");
            Console.Write("> ");
            string inputDate = Console.ReadLine();

            if (password != repeatPassword)
            {
                Console.WriteLine("Verifique la contraseña");
                Console.ReadKey();
                return false;
            }

            if (!DateOnly.TryParse(inputDate, out DateOnly birthDate))
            {
                Console.WriteLine("Fecha no válida");
                Console.ReadKey();
                return false;
            }

            try
            {
                sendRegister(socket, user, email, password, birthDate);

                Console.WriteLine("Datos enviados correctamente");
                return true;
            }
            catch
            {
                Console.WriteLine("Verifique los datos, no se ha podido ejecutar el registro");
                Console.ReadKey();
                return false;
            }
        }

        public static void sendRegister(Socket socket, string user, string email, string password, DateOnly birthDate)
        {
            SocketTools.sendString(user, socket);
            SocketTools.sendString(email, socket);
            SocketTools.sendString(password, socket);
            SocketTools.sendDate(birthDate, socket);
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