using NetUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using static Client.Program;

namespace Client.MainMenu
{
    internal class Menu
    {
        public static bool Register(Socket socket)
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
        public static void ProcessRegister(string ip, int port)
        {
            bool register = true;

            while (register)
            {
                try
                {
                    using Socket socketClient = createSocketConnection(ip, port);

                    bool datoEnviado = Menu.Register(socketClient);

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
        public static void sendRegister(Socket socket, string user, string email, string password, DateOnly birthDate)
        {
            SocketTools.sendString(user, socket);
            SocketTools.sendString(email, socket);
            SocketTools.sendString(password, socket);
            SocketTools.sendDate(birthDate, socket);
        }
    }
}
