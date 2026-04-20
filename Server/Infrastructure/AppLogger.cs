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
                Console.WriteLine(line);
                File.AppendAllText(logFilePath, line + Environment.NewLine);
            }
        }

        public static void Info(string category, string message) => Write("INFO", category, message);
        public static void Warn(string category, string message) => Write("WARN", category, message);
        public static void Error(string category, string message) => Write("ERROR", category, message);
        public static void Debug(string category, string message) => Write("DEBUG", category, message);
    }
}