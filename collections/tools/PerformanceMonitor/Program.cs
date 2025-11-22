using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;

namespace PerformanceMonitor.Console;

class Program
{
    // Peak values
    static double _maxCpu = 0;
    static long _maxMemory = 0;
    static long _maxEvents = 0;
    static long _maxFreeze = 0;

    static void Main(string[] args)
    {
        var logDir = args.Length > 0 
            ? args[0] 
            : @"C:\visual projects\arb1\collections\logs\performance";

        System.Console.Title = "Performance Monitor - Collections";
        System.Console.Clear();
        System.Console.CursorVisible = false;

        var lastLine = "";
        var history = new string[10];
        var historyIndex = 0;

        while (true)
        {
            try
            {
                // Find latest performance_*.csv file
                if (!Directory.Exists(logDir))
                {
                    DrawStatus("Waiting for log directory...", ConsoleColor.Yellow);
                    Thread.Sleep(1000);
                    continue;
                }

                var latestFile = Directory.GetFiles(logDir, "performance_*.csv")
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .FirstOrDefault();

                if (latestFile == null)
                {
                    DrawStatus("Waiting for performance log...", ConsoleColor.Yellow);
                    Thread.Sleep(1000);
                    continue;
                }

                // Read last line
                var lines = File.ReadAllLines(latestFile);
                if (lines.Length < 2) // Skip header
                {
                    Thread.Sleep(500);
                    continue;
                }

                var currentLine = lines[^1];
                if (currentLine == lastLine)
                {
                    Thread.Sleep(200);
                    continue;
                }

                lastLine = currentLine;

                // Parse: Timestamp,Elapsed_sec,CPU%,Memory_MB,Events/sec,Freeze_ms,LastActivity
                var parts = currentLine.Split(',');
                if (parts.Length < 7) continue;

                var timestamp = parts[0];
                var elapsed = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                var cpu = double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                var memory = long.Parse(parts[3]);
                var eventsPerSec = long.Parse(parts[4]);
                var freezeMs = long.Parse(parts[5]);
                var activity = parts[6];
                
                int cpuSpikes = 0, memLeaks = 0, eventStorms = 0;
                if (parts.Length >= 10)
                {
                     cpuSpikes = int.Parse(parts[7]);
                     memLeaks = int.Parse(parts[8]);
                     eventStorms = int.Parse(parts[9]);
                }

                // Update peaks
                if (cpu > _maxCpu) _maxCpu = cpu;
                if (memory > _maxMemory) _maxMemory = memory;
                if (eventsPerSec > _maxEvents) _maxEvents = eventsPerSec;
                if (freezeMs > _maxFreeze) _maxFreeze = freezeMs;

                // Add to history
                history[historyIndex % 10] = $"{timestamp} | CPU:{cpu,5:F1}% | Mem:{memory,4}MB | Events:{eventsPerSec,5}/s | Freeze:{freezeMs,4}ms | {activity}";
                historyIndex++;

                // Draw dashboard
                DrawDashboard(timestamp, elapsed, cpu, memory, eventsPerSec, freezeMs, activity, history, historyIndex, cpuSpikes, memLeaks, eventStorms);

                Thread.Sleep(200);
            }
            catch (Exception ex)
            {
                DrawStatus($"Error: {ex.Message}", ConsoleColor.Red);
                Thread.Sleep(1000);
            }
        }
    }

    static void DrawDashboard(string timestamp, double elapsed, double cpu, long memory, 
        long eventsPerSec, long freezeMs, string activity, string[] history, int historyCount,
        int cpuSpikes, int memLeaks, int eventStorms)
    {
        System.Console.SetCursorPosition(0, 0);
        System.Console.ForegroundColor = ConsoleColor.Cyan;
        System.Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║                        PERFORMANCE MONITOR - COLLECTIONS                      ║");
        System.Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        System.Console.ResetColor();

        System.Console.WriteLine($"\n⏱  Time:     {timestamp}  (Running: {elapsed:F0}s)");
        
        // CPU bar
        System.Console.Write("🔥 CPU:      ");
        DrawBar(cpu, 100, ConsoleColor.Red, ConsoleColor.DarkRed);
        System.Console.WriteLine($"  {cpu,5:F1}%  (Peak: {_maxCpu:F1}%)");

        // Memory bar
        System.Console.Write("💾 Memory:   ");
        DrawBar(memory, 1000, ConsoleColor.Green, ConsoleColor.DarkGreen); // 1GB scale
        System.Console.WriteLine($"  {memory,4} MB  (Peak: {_maxMemory} MB)");

        // Events/sec bar
        System.Console.Write("📊 Events:   ");
        DrawBar(eventsPerSec, 5000, ConsoleColor.Blue, ConsoleColor.DarkBlue); // 5000/s scale
        System.Console.WriteLine($"  {eventsPerSec,5}/sec (Peak: {_maxEvents}/s)");

        // Freeze indicator
        System.Console.Write("⚡ Freeze:    ");
        if (freezeMs == 0)
        {
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.Write("✅ OK (0 ms)                    ");
        }
        else if (freezeMs < 500)
        {
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write($"⚠️  WARNING ({freezeMs} ms)      ");
        }
        else
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.Write($"❌ CRITICAL ({freezeMs} ms)     ");
        }
        System.Console.ResetColor();
        System.Console.WriteLine($" (Peak: {_maxFreeze} ms)");

        System.Console.WriteLine($"🔨 Activity:  {activity}                              ");

        // Alerts Section
        if (cpuSpikes > 0 || memLeaks > 0 || eventStorms > 0)
        {
            System.Console.WriteLine("\n⚠️  ALERTS DETECTED:");
            System.Console.ForegroundColor = ConsoleColor.Red;
            if (cpuSpikes > 0) System.Console.WriteLine($"   🔥 CPU Spikes (>99%):   {cpuSpikes}");
            if (memLeaks > 0)  System.Console.WriteLine($"   💾 Mem Leaks (>5GB):    {memLeaks}");
            if (eventStorms > 0) System.Console.WriteLine($"   🌊 Event Storms (>20k): {eventStorms}");
            System.Console.ResetColor();
        }

        // History
        System.Console.WriteLine("\n" + new string('─', 80));
        System.Console.ForegroundColor = ConsoleColor.Gray;
        System.Console.WriteLine("Last 10 heartbeats:");
        for (int i = 0; i < 10; i++)
        {
            var idx = (historyCount - 1 - i + 10) % 10;
            if (history[idx] != null)
            {
                System.Console.WriteLine($"  {history[idx]}");
            }
        }
        System.Console.ResetColor();
    }

    static void DrawBar(double value, double max, ConsoleColor fillColor, ConsoleColor bgColor)
    {
        const int barWidth = 30;
        var filled = (int)((value / max) * barWidth);
        filled = Math.Min(filled, barWidth);

        System.Console.ForegroundColor = fillColor;
        System.Console.Write(new string('█', filled));
        System.Console.ForegroundColor = bgColor;
        System.Console.Write(new string('░', barWidth - filled));
        System.Console.ResetColor();
    }

    static void DrawStatus(string message, ConsoleColor color)
    {
        System.Console.Clear();
        System.Console.ForegroundColor = color;
        System.Console.WriteLine($"\n  {message}\n");
        System.Console.ResetColor();
    }
}
