using Client.MainMenu;
using NetUtils;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Client
{
    internal class Program
    {
        public enum MainMenuOption
        {
            Login = 1,
            Register = 2,
        }

        public enum MainGroup
        {
            CreateGroup = 1,
            JoinGroup = 2,
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

        public enum LobbyOption
        {
            Refresh = 1,
            Exit = 2,
            Start = 3,
            SendLocation = 4
        }

        static void Main(string[] args)
        {
            string ip = "192.168.1.37";
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
                string? user = Console.ReadLine();

                Console.WriteLine("Contraseña");
                Console.Write(">");
                string? password = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
                {
                    Console.WriteLine("Usuario y contraseña son obligatorios.");
                    Console.ReadKey();
                    continue;
                }

                try
                {
                    using Socket socketClient = CreateSocketConnection(ip, port);

                    SocketTools.sendInt(socketClient, (int)MainMenuOption.Login);
                    SocketTools.sendString(user, socketClient);
                    SocketTools.sendString(password, socketClient);

                    bool login = SocketTools.receiveBool(socketClient);

                    if (!login)
                    {
                        Console.WriteLine("Compruebe que escribe bien sus datos.");
                        Console.ReadKey();
                    }
                    else
                    {
                        Console.WriteLine("Login correcto.");
                        Console.ReadKey();

                        ShowLoggedMenu(socketClient);
                        tryLogin = false;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("No se ha podido completar el login.");
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

                switch (currentTab)
                {
                    case 1:
                        PrintHomeContent();
                        break;
                    case 2:
                        PrintGroupContent();
                        break;
                    case 3:
                        PrintMapContent();
                        break;
                    case 4:
                        PrintProfileContent();
                        break;
                }

                Console.WriteLine("\n==============================");
                Console.WriteLine($"{(currentTab == 1 ? "[HOME]" : " Home ")} | " +
                                  $"{(currentTab == 2 ? "[GROUP]" : " Group ")} | " +
                                  $"{(currentTab == 3 ? "[MAP]" : " Map ")} | " +
                                  $"{(currentTab == 4 ? "[PERFIL]" : " Perfil ")}");
                Console.WriteLine("==============================");
                Console.WriteLine("Selecciona (1-4) o usa las letras de acción (C, U, S, etc.)");
                Console.WriteLine("Pulsa 0 para salir.");
                Console.Write(">");

                string? input = Console.ReadLine()?.Trim().ToLower();

                if (int.TryParse(input, out int tab) && tab >= 1 && tab <= 4)
                {
                    currentTab = tab;
                    continue;
                }

                if (input == "0")
                {
                    inApp = false;
                    continue;
                }

                switch (currentTab)
                {
                    case 2:
                        {
                            // Si devuelve false, significa que el flujo de grupo
                            // ha consumido/cerrado la sesión lógica actual
                            bool keepUsingSocket = HandleGroupTab(socket, input);

                            if (!keepUsingSocket)
                            {
                                inApp = false;
                            }

                            break;
                        }

                    case 4:
                        if (input == "s")
                        {
                            Console.WriteLine("\n[Lógica] Entrando en Ajustes...");
                            Console.ReadKey();
                        }
                        else
                        {
                            Console.WriteLine("\nOpción no reconocida.");
                            Thread.Sleep(800);
                        }
                        break;

                    default:
                        Console.WriteLine("\nOpción no reconocida.");
                        Thread.Sleep(800);
                        break;
                }
            }
        }

        // Devuelve false cuando el flujo ya no debe seguir usando el socket actual
        public static bool HandleGroupTab(Socket socket, string? input)
        {
            if (input == "c")
            {
                return CreateGroupFlow(socket);
            }
            else if (input == "u")
            {
                return JoinGroupFlow(socket);
            }
            else
            {
                Console.WriteLine("\nOpción no reconocida.");
                Thread.Sleep(800);
                return true;
            }
        }

        // IMPORTANTE:
        // Tu servidor aún no implementa CreateGroup real en Program.cs
        // así que por ahora probablemente devolverá false.
        public static bool CreateGroupFlow(Socket socket)
        {
            Console.WriteLine("\n[Lógica] Iniciando creación de grupo...");
            Thread.Sleep(600);
            Console.Clear();

            string nameGroup;
            while (true)
            {
                Console.WriteLine("Nombre del grupo (obligatorio):");
                Console.Write(">");

                nameGroup = Console.ReadLine()?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(nameGroup))
                {
                    Console.WriteLine("El nombre no puede estar vacío.");
                    continue;
                }

                break;
            }

            string labelGroup = string.Empty;
            while (true)
            {
                Console.WriteLine("Elige una etiqueta:");
                Console.WriteLine("1. Bailar | 2. Cena | 3. Deporte | 4. Paseo | 5. Cafe");
                Console.Write(">");

                string? inputLabel = Console.ReadLine();

                if (!int.TryParse(inputLabel, out int opcion))
                {
                    Console.WriteLine("Por favor, introduce un número válido.");
                    continue;
                }

                if (!Enum.IsDefined(typeof(Etiqueta), opcion))
                {
                    Console.WriteLine("Ese número no está en la lista.");
                    continue;
                }

                labelGroup = ((Etiqueta)opcion).ToString();
                Console.WriteLine($"Has elegido: {labelGroup}");
                break;
            }

            string groupDescription;
            while (true)
            {
                Console.WriteLine("Descripción (obligatorio o '.' para vacío):");
                Console.Write(">");

                string inputDescription = Console.ReadLine()?.Trim() ?? string.Empty;

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

                string inputMethod = Console.ReadLine()?.Trim() ?? string.Empty;

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

            SocketTools.sendInt(socket, (int)MainGroup.CreateGroup);
            SocketTools.sendString(nameGroup, socket);
            SocketTools.sendString(labelGroup, socket);
            SocketTools.sendString(groupDescription, socket);
            SocketTools.sendString(methodAlgorithm, socket);

            bool responseCreateGroup = SocketTools.receiveBool(socket);

            if (responseCreateGroup)
            {
                string receiveGroupCode = SocketTools.receiveString(socket);

                Console.WriteLine("Grupo creado correctamente.");
                Console.WriteLine($"Código de grupo: {receiveGroupCode}");
                Console.WriteLine("Pulsa una tecla para entrar al lobby...");
                Console.ReadKey();

                // Esto solo funcionará de verdad cuando el servidor,
                // después de crear, te meta también en LobbyGroup.
                LobbyGroupFlow(socket, receiveGroupCode, true);

                // Con tu servidor actual, al salir del lobby el socket se cerrará.
                return false;
            }
            else
            {
                Console.WriteLine("No ha sido posible crear el grupo.");
                Console.WriteLine("Esto es normal si el servidor aún no tiene implementado CreateGroup.");
                Console.ReadKey();
                Console.Clear();

                return true;
            }
        }

        public static bool JoinGroupFlow(Socket socket)
        {
            Console.WriteLine("\nIntroduce el código del grupo:");
            Console.Write(">");

            string? groupCode = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(groupCode))
            {
                Console.WriteLine("El código no puede estar vacío.");
                Console.ReadKey();
                return true;
            }

            SocketTools.sendInt(socket, (int)MainGroup.JoinGroup);
            SocketTools.sendString(groupCode, socket);

            bool success = SocketTools.receiveBool(socket);

            if (success)
            {
                Console.WriteLine("Te has unido correctamente al grupo.");
                Console.ReadKey();

                // A partir de aquí el servidor entra en LobbyGroup(...)
                LobbyGroupFlow(socket, groupCode, false);

                // El servidor cerrará la conexión al salir del lobby.
                return false;
            }
            else
            {
                Console.WriteLine("El grupo no está activo o el código no es válido.");
                Console.ReadKey();
                return true;
            }
        }

        // Este método ya habla con el lobby real del servidor
        public static void LobbyGroupFlow(Socket socket, string groupCode, bool userOwner)
        {
            bool inLobby = true;
            bool locationSent = false;

            while (inLobby)
            {
                Console.Clear();
                Console.WriteLine($"👥 Grupo: {groupCode}");
                Console.WriteLine("--------------------------------");
                Console.WriteLine("      [ . . . SALA DE ESPERA . . . ]      ");
                Console.WriteLine("--------------------------------");

                if (userOwner)
                {
                    Console.WriteLine("Eres el creador del grupo.");
                    Console.WriteLine("Comparte el código con los demás miembros.");
                }
                else
                {
                    Console.WriteLine("Has entrado al grupo correctamente.");
                }

                int memberCount = SocketTools.receiveInt(socket);
                bool hasStarted = SocketTools.receiveBool(socket);

                Console.WriteLine($"\nMiembros actuales en sala: {memberCount}");
                Console.WriteLine($"Estado del grupo: {(hasStarted ? "INICIADO" : "ESPERANDO START")}");

                if (hasStarted && !locationSent)
                {
                    Console.WriteLine("\nEl grupo ya ha sido iniciado. Introduce tu ubicación.");

                    Console.WriteLine("Latitud?");
                    string? inputLatitude = Console.ReadLine()?.Trim().Replace(',', '.');

                    if (!double.TryParse(inputLatitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double latitud))
                    {
                        Console.WriteLine("Latitud no válida.");
                        Console.ReadKey();
                        continue;
                    }

                    Console.WriteLine("Longitud?");
                    string? inputLongitude = Console.ReadLine()?.Trim().Replace(',', '.');

                    if (!double.TryParse(inputLongitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double longitud))
                    {
                        Console.WriteLine("Longitud no válida.");
                        Console.ReadKey();
                        continue;
                    }

                    SocketTools.sendInt(socket, (int)LobbyOption.SendLocation);
                    SocketTools.sendDouble(socket, latitud);
                    SocketTools.sendDouble(socket, longitud);

                    double destinationLatitude = SocketTools.receiveDouble(socket);
                    double destinationLongitude = SocketTools.receiveDouble(socket);
                    int durationSeconds = SocketTools.receiveInt(socket);

                    if (durationSeconds == -1)
                    {
                        Console.WriteLine("Ubicación enviada correctamente.");
                        Console.WriteLine("Esperando a que el resto del grupo envíe la suya...");
                        Console.ReadKey();
                    }
                    else
                    {
                        Console.WriteLine($"\nPunto de encuentro: {destinationLatitude}, {destinationLongitude}");
                        Console.WriteLine($"Tiempo estimado: {durationSeconds} segundos");
                        Console.ReadKey();
                    }

                    locationSent = true;
                    continue;
                }

                Console.WriteLine("\nOpciones:");
                Console.WriteLine("1. Refrescar");
                Console.WriteLine("2. Salir del grupo");

                if (userOwner && !hasStarted)
                {
                    Console.WriteLine("3. Start");
                }

                Console.Write(">");

                string? input = Console.ReadLine()?.Trim();

                if (!int.TryParse(input, out int option))
                {
                    Console.WriteLine("Opción no válida.");
                    Thread.Sleep(700);

                    SocketTools.sendInt(socket, (int)LobbyOption.Refresh);
                    continue;
                }

                switch (option)
                {
                    case (int)LobbyOption.Refresh:
                        SocketTools.sendInt(socket, (int)LobbyOption.Refresh);
                        break;

                    case (int)LobbyOption.Exit:
                        SocketTools.sendInt(socket, (int)LobbyOption.Exit);
                        Console.WriteLine("\nHas salido del grupo.");
                        Console.ReadKey();
                        inLobby = false;
                        break;

                    case (int)LobbyOption.Start:
                        if (!userOwner)
                        {
                            Console.WriteLine("Solo el owner puede iniciar el grupo.");
                            Console.ReadKey();
                            break;
                        }

                        SocketTools.sendInt(socket, (int)LobbyOption.Start);

                        bool responseStart = SocketTools.receiveBool(socket);

                        if (!responseStart)
                        {
                            Console.WriteLine("\nNo se ha podido iniciar el grupo.");
                            Console.ReadKey();
                            break;
                        }

                        Console.WriteLine("\nGrupo iniciado correctamente.");
                        Console.ReadKey();
                        break;

                    default:
                        Console.WriteLine("Opción no válida.");
                        Thread.Sleep(700);
                        SocketTools.sendInt(socket, (int)LobbyOption.Refresh);
                        break;
                }
            }
        }

        public static Socket CreateSocketConnection(string ip, int port)
        {
            IPAddress address = IPAddress.Parse(ip);
            IPEndPoint endpoint = new IPEndPoint(address, port);

            Socket socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(endpoint);

            return socket;
        }

        public static void PrintHomeContent()
        {
            Console.WriteLine("🏠 INICIO - Actividad reciente");
            Console.WriteLine("--------------------------------");
            Console.WriteLine("> Noticia: ¡Nueva actualización de mapas disponible!");
            Console.WriteLine("> Tip: crea un grupo rápido usando la pestaña Group.");
            Console.WriteLine("> Historial: Quedada en 'Bar Central' (hace 2 días).");
        }

        public static void PrintGroupContent()
        {
            Console.WriteLine("👥 GRUPOS - Gestión de reuniones");
            Console.WriteLine("--------------------------------");
            Console.WriteLine("1. [C]rear nuevo grupo");
            Console.WriteLine("2. [U]nirse con código");
            Console.WriteLine("\nEstado: Sin grupo activo.");
        }

        public static void PrintMapContent()
        {
            Console.WriteLine("📍 MAPA - Punto de encuentro");
            Console.WriteLine("--------------------------------");
            Console.WriteLine("      [ . . . MAPA CARGANDO . . . ]      ");
            Console.WriteLine("\n(Aquí aparecerá la ubicación final calculada)");
        }

        public static void PrintProfileContent()
        {
            Console.WriteLine("👤 PERFIL - Mi cuenta");
            Console.WriteLine("--------------------------------");
            Console.WriteLine("Usuario: Invitado_123");
            Console.WriteLine("Estado: Online");
            Console.WriteLine("\n[S] Settings (Ajustes)");
        }
    }
}