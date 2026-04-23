using System;
using System.IO;

namespace Server.Infrastructure
{
    public static class AppLogger
    {
        private static readonly object _lock = new();
        private static readonly string logDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
        private static readonly string logFilePath = Path.Combine(logDirectory, $"server-{DateTime.Now:yyyy-MM-dd}.log");

        static AppLogger()
        {
            Directory.CreateDirectory(logDirectory);
        }

        private static void Write(string level, string category, string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] [{level}] [{category}] {message}";

            lock (_lock)
            {
                // Configurar color según el nivel de log
                Console.ForegroundColor = level switch
                {
                    "INFO" => ConsoleColor.Green,  // Verde para info (éxito/normal)
                    "WARN" => ConsoleColor.Yellow, // Amarillo para advertencias
                    "ERROR" => ConsoleColor.Red,    // Rojo para errores críticos
                    "DEBUG" => ConsoleColor.Gray,   // Gris para depuración (menos distracción)
                    _ => ConsoleColor.White   // Blanco por defecto
                };

                // Imprimir en consola con color
                Console.WriteLine(line);

                // IMPORTANTE: Resetear el color para no pintar el resto de la consola
                Console.ResetColor();

                // Escribir en el archivo (el archivo siempre guarda texto plano, sin colores)
                try
                {
                    File.AppendAllText(logFilePath, line + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    // Si falla la escritura en archivo, al menos lo vemos en consola
                    Console.WriteLine($"[CRITICAL] No se pudo escribir en el log: {ex.Message}");
                }
            }
        }

        public static void Info(string category, string message) => Write("INFO", category, message);
        public static void Warn(string category, string message) => Write("WARN", category, message);
        public static void Error(string category, string message) => Write("ERROR", category, message);
        public static void Debug(string category, string message) => Write("DEBUG", category, message);
    }
}