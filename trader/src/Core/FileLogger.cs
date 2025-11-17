using System;
using System.IO;
using System.Threading;

namespace TraderBot.Core
{
    public static class FileLogger
    {
        private static readonly string LogsDir = Path.Combine(AppContext.BaseDirectory, "../../../../logs");
        private static readonly string WebsocketLogPath = Path.Combine(LogsDir, "websocket.txt");
        private static readonly string OtherLogPath = Path.Combine(LogsDir, "other.txt");
        private static readonly string SpreadLogPath = Path.Combine(LogsDir, "spread.txt");
        private static readonly object _lock = new object();

        static FileLogger()
        {
            // Ensure log directory exists
            Directory.CreateDirectory(LogsDir);
            // Clear logs on start
            File.WriteAllText(WebsocketLogPath, string.Empty);
            File.WriteAllText(OtherLogPath, string.Empty);
            File.WriteAllText(SpreadLogPath, string.Empty);
        }

        public static void LogWebsocket(string message)
        {
            Log(WebsocketLogPath, message);
        }

        public static void LogOther(string message)
        {
            Log(OtherLogPath, message);
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] OTHER: {message}");
        }

        public static void LogSpread(string message)
        {
            Log(SpreadLogPath, message);
        }

        private static void Log(string path, string message)
        {
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(path, $"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}{Environment.NewLine}");
                }
            }
            catch (Exception ex)
            {
                // Fallback to console if file logging fails
                Console.WriteLine($"[LOGGER-ERROR] Failed to write to {path}: {ex.Message}");
                Console.WriteLine($"[ORIGINAL-LOG] {message}");
            }
        }
    }
}
