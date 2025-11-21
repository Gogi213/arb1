using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SpreadAggregator.Application.Diagnostics;

public static class DiagnosticLogger
{
    private static readonly string LogDir = @"c:\visual projects\arb1\collections\logs\diagnostics";
    private static readonly Channel<LogEntry> _logQueue = Channel.CreateUnbounded<LogEntry>();
    private static Task? _writerTask;

    public enum LogType
    {
        WindowGrowth,
        EventProcessing,
        Performance,
        Warning
    }

    private record LogEntry(LogType Type, string Message);

    static DiagnosticLogger()
    {
        // Clear old logs on startup
        Directory.CreateDirectory(LogDir);
        File.WriteAllText(Path.Combine(LogDir, "window_growth.log"), $"=== RUN STARTED {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
        File.WriteAllText(Path.Combine(LogDir, "events.log"), $"=== RUN STARTED {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
        File.WriteAllText(Path.Combine(LogDir, "performance.log"), $"=== RUN STARTED {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
        File.WriteAllText(Path.Combine(LogDir, "warnings.log"), $"=== RUN STARTED {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");

        // Start writer task
        _writerTask = Task.Run(ProcessLogQueue);
    }

    public static void Log(LogType type, string message)
    {
        _logQueue.Writer.TryWrite(new LogEntry(type, message));
    }

    private static async Task ProcessLogQueue()
    {
        await foreach (var entry in _logQueue.Reader.ReadAllAsync())
        {
            try
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                var logMessage = $"[{timestamp}] {entry.Message}\n";

                var fileName = entry.Type switch
                {
                    LogType.WindowGrowth => "window_growth.log",
                    LogType.EventProcessing => "events.log",
                    LogType.Performance => "performance.log",
                    LogType.Warning => "warnings.log",
                    _ => "unknown.log"
                };

                var filePath = Path.Combine(LogDir, fileName);
                File.AppendAllText(filePath, logMessage);
            }
            catch
            {
                // Swallow errors to prevent diagnostic logging from crashing the app
            }
        }
    }
}
