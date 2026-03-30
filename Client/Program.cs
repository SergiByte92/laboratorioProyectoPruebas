using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

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


/// <summary>
///         
/// EJEMPLO DE COMO DEBERIA QUEDAR EL CODIGO

//        while (appEjecutandose) {
//            // El menú principal solo coordina, no "sabe" cómo se hace un login
//            var opcion = ui.ObtenerOpcionMenuPrincipal();

//            switch (opcion) {
//                case MenuPrincipal.Login:
//                    EjecutarFlujoPostLogin(); // Encapsula: Login -> Grupo -> Lobby
//                    break;
//                case MenuPrincipal.Registro:
//                    ProcesarRegistro();
//                    break;
//                case MenuPrincipal.Salir:
//                    appEjecutandose = false;
//                    break;
//            }
//}
/// </summary>
/// <param name="args"></param>

static void Main(string[] args) 
        {
            bool login = false;
            bool register = false;

            while (true)
            {
                string ip = "192.168.1.34";
                int port = 1001;
                Socket socketClient = createSocketConnection(ip, port);

                Console.WriteLine("JUST MEETING POINT");
                Console.WriteLine("================");
                Console.WriteLine("1- Iniciar Sesión");
                Console.WriteLine("2- Registrarse");
                Console.WriteLine("0- Salir");
                Console.WriteLine("================");
                Console.Write(">");

                // CAMBIO 1: TryParse — si el usuario escribe letras, no explota
                if (!int.TryParse(Console.ReadLine(), out int option))
                {
                    Console.WriteLine("Opción no válida.");

                    continue;
                }

                // CAMBIO 2: switch en lugar de if/if — es la construcción correcta para menús
                switch (option)
                {
                    case (int)MainMenuOption.Login:

                        string user;
                        string password;

                        Console.WriteLine("Usuario");
                        Console.Write(">");
                        user = Console.ReadLine();

                        Console.WriteLine("Contraseña");
                        Console.Write(">");
                        password = Console.ReadLine();

                        sendString(user, socketClient);
                        sendString(password, socketClient);

                        login = receiveBool(socketClient);
                        Console.WriteLine($"Acceso: {login}");

                        while (login)
                        {
                            Console.WriteLine("JUST MEETING POINT");
                            Console.WriteLine("==================");
                            Console.WriteLine("1.- Iniciar Meeting Point");
                            Console.WriteLine("0.- Salir");
                            Console.Write(">");

                            // CAMBIO 1: TryParse también aquí
                            if (!int.TryParse(Console.ReadLine(), out int loginOption))
                            {
                                Console.WriteLine("Opción no válida.");
                                continue;
                            }

                            // CAMBIO 2: switch también en el menú logueado
                            switch (loginOption)
                            {
                                case (int)MainLogin.MeetingPoint:
                                    Console.WriteLine("JUST MEETING POINT");
                                    Console.WriteLine("==================");
                                    Console.WriteLine("Envie Coordenadas");

                                    Console.WriteLine("Coordenada de Longitud?");
                                    Console.Write(">");
                                    double longitud = double.Parse(Console.ReadLine());
                                    sendDouble(longitud, socketClient);

                                    Console.WriteLine("Coordenada de Latitud?");
                                    Console.Write(">");
                                    double latitud = double.Parse(Console.ReadLine());
                                    sendDouble(latitud, socketClient);
                                    break;

                                case (int)MainLogin.Exit:
                                    login = false;
                                    break;

                                default:
                                    Console.WriteLine("Opción no válida.");
                                    break;
                            }
                        }

                        break;

                    case (int)MainMenuOption.Register:

                        register = true;

                        while (register)
                        {
                            bool datoEnviado = MainRegister(socketClient);
                            
                            if (!datoEnviado) 
                            {
                                continue;
                            }
                            bool answerRegister = receiveBool(socketClient);

                            if (answerRegister)
                            {
                                Console.WriteLine("Ha sido registrado correctamente");
                                register = false;
                            }
                            else
                            {
                                Console.WriteLine("Ha habido algun error en el registro");
                            }
                        }

                        break;

                    case 0:
                        socketClient.Close(); // CAMBIO 5: cerramos antes de salir
                        return;

                    default:
                        Console.WriteLine("Opción no válida.");
                        break;
                }
            }
        }

        public static bool MainRegister(Socket socket)
        {
            Console.Clear();
            byte[] bytes = BitConverter.GetBytes(2);
            socket.Send(bytes);

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

            Console.WriteLine("- Fecha de Nacimiento (yyyy/mm/dd)");
            Console.Write("> ");
            DateOnly birth_date = DateOnly.Parse(Console.ReadLine());

            if (password != repeatPassword)
            {
                Console.WriteLine("Verifique la contraseña");
                Console.ReadKey();
                return false;
                
            }
            else
            {

                try
                {

                    sendRegister(socket, user, email, password, birth_date);

                    Console.WriteLine("Datos enviados correctamente");
                    Console.ReadKey();
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Verifique los datos, no se ha podido ejecutar el registro");
                    return false;
                }
            }

        }

        public static void sendRegister(Socket socket, string user, string email, string password, DateOnly birth_date)
        {
            sendString(user, socket);
            sendString(email, socket);
            sendString(password, socket);
            sendDate(birth_date, socket);
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
        public static void sendDate(DateOnly date, Socket socket)
        {
            sendString(date.ToString("yyyy-MM-dd"), socket);
        }
    }
}