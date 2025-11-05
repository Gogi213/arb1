using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services;

/// <summary>
/// A simple static logger to write WebSocket connection status to a file.
/// </summary>
public static class WebSocketLogger
{
    private static readonly string LogDirectory = @"C:\visual projects\arb1\collections\logs";
    private static readonly string LogFilePath = Path.Combine(LogDirectory, "websocket.log");
    private static readonly object FileLock = new();

    static WebSocketLogger()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL] Failed to create log directory: {ex.Message}");
        }
    }

    /// <summary>
    /// Logs a message to the websocket.log file.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Log(string message)
    {
        try
        {
            lock (FileLock)
            {
                var logMessage = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} | {message}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, logMessage, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to write to log file: {ex.Message}");
        }
    }
}