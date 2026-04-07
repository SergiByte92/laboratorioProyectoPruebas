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

        public enum Etiqueta
        {
            Bailar = 1,
            Cena,
            Deporte,
            Paseo,
            Cafe
        }
        public enum Algorithm
        {
            PuntoOptimo = 1,
            Recomendacion,
        }
        static void Main(string[] args) // Siguiente paso : Grupo, recibe pass, se une, mandan localizacion y hace punto geometrico. Tambien refactorizar
        {
            string ip = "192.168.111.52";
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

                        ShowLoggedMenu(socketClient);
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

        public static void ShowLoggedMenu(Socket socket)
        {
            int currentTab = 1; // 1: Home, 2: Group, 3: Map, 4: Profile
            bool inApp = true;

            while (inApp)
            {
                Console.Clear();
                Console.WriteLine("=== JUST MEETING POINT ===");

                // --- 1. SWITCH DE VISUALIZACIÓN (Pintar contenido) ---
                switch (currentTab)
                {
                    case 1: PrintHomeContent(); break;
                    case 2: PrintGroupContent(); break;
                    case 3: PrintMapContent(); break;
                    case 4: PrintProfileContent(); break;
                }

                // --- TAB BAR (Siempre visible) ---
                Console.WriteLine("\n==============================");
                Console.WriteLine($"{(currentTab == 1 ? "[HOME]" : " Home ")} | " +
                                  $"{(currentTab == 2 ? "[GROUP]" : " Group ")} | " +
                                  $"{(currentTab == 3 ? "[MAP]" : " Map ")} | " +
                                  $"{(currentTab == 4 ? "[PERFIL]" : " Perfil ")}");
                Console.WriteLine("==============================");
                Console.WriteLine("Selecciona (1-4) o usa las letras de acción (C, U, S, etc.)");
                Console.WriteLine("Pulsa 0 para Salir.");
                Console.Write(">");

                // --- 2. CAPTURA DE INPUT (Leemos string para aceptar letras y números) ---
                string input = Console.ReadLine()?.ToLower();

                // --- 3. LÓGICA DE NAVEGACIÓN (Prioridad: Cambiar de pestaña) ---
                if (int.TryParse(input, out int tab) && tab >= 1 && tab <= 4)
                {
                    currentTab = tab;
                }
                else if (input == "0")
                {
                    inApp = false;
                }
                else
                {
                    // --- 4. SWITCH DE ACCIONES (Depende de en qué pestaña estemos) ---
                    switch (currentTab)
                    {
                        case 2: // Acciones dentro de GROUP
                            if (input == "c")
                            {
                                Console.WriteLine("\n[Lógica] Iniciando creación de grupo...");
                                Thread.Sleep(1000);
                                Console.Clear();

                                string nameGroup;

                                while (true)
                                {
                                    Console.WriteLine("Nombre del grupo (obligatorio):");
                                    Console.Write(">");

                                    nameGroup = Console.ReadLine()?.Trim();

                                    if (string.IsNullOrWhiteSpace(nameGroup))
                                    {
                                        Console.WriteLine("El nombre no puede estar vacío.");
                                        continue;
                                    }

                                    break;
                                }

                                string labelGroup = string.Empty;
                                bool groupLabel = true;

                                while (groupLabel)
                                {
                                    Console.WriteLine("Elige una etiqueta:");
                                    Console.WriteLine("1. Bailar | 2. Cena | 3. Deporte | 4. Paseo | 5. Cafe");
                                    Console.Write(">");

                                    string inputLabel = Console.ReadLine();

                                    if (int.TryParse(inputLabel, out int opcion))
                                    {
                                        if (Enum.IsDefined(typeof(Etiqueta), opcion))
                                        {
                                            Etiqueta seleccion = (Etiqueta)opcion;
                                            labelGroup = seleccion.ToString();

                                            Console.WriteLine($"Has elegido la opción número {opcion}, que es: {labelGroup}");
                                            groupLabel = false;
                                        }
                                        else
                                        {
                                            Console.WriteLine("Ese número no está en la lista.");
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine("Por favor, introduce un número válido.");
                                    }
                                }

                                string groupDescription;

                                while (true)
                                {
                                    Console.WriteLine("Descripción (obligatorio o '.' para vacío):");
                                    Console.Write(">");

                                    string inputDescription = Console.ReadLine()?.Trim();

                                    if (inputDescription == ".")
                                    {
                                        groupDescription = string.Empty;
                                        break;
                                    }

                                    if (!string.IsNullOrWhiteSpace(inputDescription))
                                    {
                                        groupDescription = inputDescription;
                                        break;
                                    }

                                    Console.WriteLine("Debes escribir algo o '.'");
                                }


                                string methodAlgorithm;

                                while (true)
                                {
                                    Console.WriteLine("Método:");
                                    Console.WriteLine("1. PuntoOptimo | 2. Recomendacion");
                                    Console.Write(">");

                                    string inputMethod = Console.ReadLine()?.Trim();

                                    if (!int.TryParse(inputMethod, out int opcion))
                                    {
                                        Console.WriteLine("Por favor, introduce un número válido.");
                                        continue;
                                    }

                                    if (!Enum.IsDefined(typeof(Algorithm), opcion))
                                    {
                                        Console.WriteLine("Ese número no está en la lista.");
                                        continue;
                                    }

                                    methodAlgorithm = ((Algorithm)opcion).ToString();

                                    Console.WriteLine($"Has elegido: {methodAlgorithm}");
                                    break;
                                }

                                Thread.Sleep(1000);
                                Console.Clear();

                                SocketTools.sendString(nameGroup, socket);
                                SocketTools.sendString(labelGroup, socket);
                                SocketTools.sendString(groupDescription, socket);
                                SocketTools.sendString(methodAlgorithm, socket);

                                string receiveGroupCode = SocketTools.receiveString(socket);
                                Console.WriteLine("Esperando código de grupo...");
                                Console.WriteLine($"Código de grupo: {receiveGroupCode}");

              

                                bool responseCreateGroup = SocketTools.receiveBool(socket);

                                if (responseCreateGroup)
                                {
                                    Console.WriteLine("Ha sido creado correctamente");
                                    Console.WriteLine("Pulsa una tecla para continuar...");
                                    Console.ReadKey();

                                    Thread.Sleep(1000);
                                    Console.Clear();
                                    //Sala de espera
                                }
                                else 
                                {
                                    Console.WriteLine("No ha sido posible crear el grupo");
                                    Console.Clear();
                                }
                            }
                            else if (input == "u")
                            {
                                Console.WriteLine("\n[Lógica] Introduce el código del grupo:");
                                Console.Write(">");
                                Console.ReadLine();

                                //Logica si es correcto o no, si lo es, se pasa al lobby
                                // JoinGroup();
                                Console.ReadKey();
                            }
                            break;

                        case 4: // Acciones dentro de PROFILE
                            if (input == "s")
                            {
                                Console.WriteLine("\n[Lógica] Entrando en Ajustes...");
                                // ShowSettings();
                                Console.ReadKey();
                            }
                            break;

                        default:
                            Console.WriteLine("\nOpción no reconocida. Intenta de nuevo.");
                            Thread.Sleep(1000); // Pausa breve para que el usuario lea el error
                            break;
                    }
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
        // --- MÉTODOS DE CONTENIDO ---

        public static void PrintHomeContent()
        {
            Console.WriteLine("🏠 INICIO - Actividad Reciente");
            Console.WriteLine("--------------------------------");
            Console.WriteLine("> Noticia: ¡Nueva actualización de mapas disponible!");
            Console.WriteLine("> Tip: Crea un grupo rápido usando el botón 'Group'.");
            Console.WriteLine("> Historial: Quedada en 'Bar Central' (hace 2 días).");
        }

        public static void PrintGroupContent()
        {
            Console.WriteLine("👥 GRUPOS - Gestión de Reuniones");
            Console.WriteLine("--------------------------------");
            // Aquí es donde meterías tu lógica de sockets para crear/unirse
            Console.WriteLine("1. [C]rear nuevo grupo");
            Console.WriteLine("2. [U]nirse con código");
            Console.WriteLine("\nEstado: Sin grupo activo.");


        }

        public static void PrintMapContent()
        {
            Console.WriteLine("📍 MAPA - Punto de Encuentro");
            Console.WriteLine("--------------------------------");
            // El mapa "en espera" hasta que haya cálculos
            Console.WriteLine("      [ . . . MAPA CARGANDO . . . ]      ");
            Console.WriteLine("\n(Aquí aparecerá la ubicación final calculada)");
        }

        public static void PrintProfileContent()
        {
            Console.WriteLine("👤 PERFIL - Mi Cuenta");
            Console.WriteLine("--------------------------------");
            Console.WriteLine("Usuario: Invitado_123");
            Console.WriteLine("Estado: Online");
            Console.WriteLine("\n[S] Settings (Ajustes) --> Pulsa 'S' para configurar");
        }
    }
}