using System;
using System.IO;
using System.Threading;

namespace TraderBot.Core
{
    public static class FileLogger
    {
        // Logs go to trader/logs directory
        // AppContext.BaseDirectory is: trader/src/Host/bin/Debug/net9.0/
        // Need to go up 5 levels: net9.0 -> Debug -> bin -> Host -> src -> trader
        private static readonly string LogsDir = Path.Combine(AppContext.BaseDirectory, "../../../../../logs");
        private static readonly string WebsocketLogPath = Path.Combine(LogsDir, "ws.log");
        private static readonly string MainLogPath = Path.Combine(LogsDir, "main.log");
        private static readonly object _lock = new object();

        static FileLogger()
        {
            // Ensure log directory exists
            Directory.CreateDirectory(LogsDir);

            // Clear logs on start
            File.WriteAllText(WebsocketLogPath, string.Empty);
            File.WriteAllText(MainLogPath, string.Empty);

            // Write header with timestamp
            var startTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
            File.AppendAllText(MainLogPath, $"========== LOG STARTED: {startTime} UTC =========={Environment.NewLine}{Environment.NewLine}");
            File.AppendAllText(WebsocketLogPath, $"========== WS LOG STARTED: {startTime} UTC =========={Environment.NewLine}{Environment.NewLine}");
        }

        public static void LogWebsocket(string message)
        {
            // WS logs go to ws.log and console
            Log(WebsocketLogPath, message, "WS");
        }

        public static void LogOther(string message)
        {
            // Other logs go to main.log and console
            Log(MainLogPath, message, "");
        }

        public static void LogSpread(string message)
        {
            // Spread logs also go to main.log
            Log(MainLogPath, message, "SPREAD");
        }

        private static void Log(string path, string message, string prefix)
        {
            try
            {
                var timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
                var formattedMessage = string.IsNullOrEmpty(prefix)
                    ? $"[{timestamp}] {message}"
                    : $"[{timestamp}] [{prefix}] {message}";

                lock (_lock)
                {
                    File.AppendAllText(path, formattedMessage + Environment.NewLine);
                }

                // Also output to console
                Console.WriteLine(formattedMessage);
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
