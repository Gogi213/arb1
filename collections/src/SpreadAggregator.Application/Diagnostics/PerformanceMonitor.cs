using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Globalization;

namespace SpreadAggregator.Application.Diagnostics;

/// <summary>
/// Lightweight performance monitor for detecting freezes and tracking metrics
/// </summary>
public class PerformanceMonitor : IDisposable
{
    private readonly string _logPath;
    private readonly Timer _heartbeatTimer;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private long _lastHeartbeatTicks;
    private long _eventsProcessed;
    private string _lastActivity = "Startup";
    private bool _disposed;
    
    // CPU Tracking
    private TimeSpan _lastProcessorTime;
    private DateTime _lastMonitorTime;

    // Alert Counters
    private int _cpuSpikes = 0;
    private int _memLeaks = 0;
    private int _eventStorms = 0;
    private readonly string _alertLogPath;

    public PerformanceMonitor(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _logPath = Path.Combine(logDirectory, $"performance_{timestamp}.csv");
        _alertLogPath = Path.Combine(logDirectory, $"alerts_{timestamp}.log");
        
        // Write CSV header
        File.WriteAllText(_logPath, "Timestamp,Elapsed_sec,CPU%,Memory_MB,Events/sec,Freeze_ms,Activity,CpuSpikes,MemLeaks,EventStorms\n");
        
        _lastHeartbeatTicks = _stopwatch.ElapsedTicks;
        
        // Initialize CPU tracking
        _lastProcessorTime = Process.GetCurrentProcess().TotalProcessorTime;
        _lastMonitorTime = DateTime.UtcNow;
        
        // Heartbeat every 1 second
        _heartbeatTimer = new Timer(WriteHeartbeat, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Record that an event was processed (called from hot path)
    /// </summary>
    public void RecordEvent(string activity)
    {
        Interlocked.Increment(ref _eventsProcessed);
        _lastActivity = activity;
    }

    private void WriteHeartbeat(object? state)
    {
        try
        {
            var now = _stopwatch.ElapsedTicks;
            var elapsedMs = (now - _lastHeartbeatTicks) * 1000.0 / Stopwatch.Frequency;
            _lastHeartbeatTicks = now;

            // Detect freeze (gap > 1200ms, assuming 1000ms interval)
            // If elapsed is 2000ms, it means we froze for 1000ms
            var freezeMs = elapsedMs > 1200 ? (long)(elapsedMs - 1000) : 0;

            // Get CPU and Memory
            var process = Process.GetCurrentProcess();
            var cpuPercent = GetCpuUsage(process);
            var memoryMB = process.WorkingSet64 / 1024 / 1024;

            // Events per second
            var eventsPerSec = Interlocked.Exchange(ref _eventsProcessed, 0);

            // Check Alerts
            CheckAlerts(cpuPercent, memoryMB, eventsPerSec);

            // Write metrics using InvariantCulture to avoid comma issues in CSV
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var elapsedSec = _stopwatch.Elapsed.TotalSeconds;
            var line = string.Format(CultureInfo.InvariantCulture, "{0},{1:F1},{2:F1},{3},{4},{5},{6},{7},{8},{9}\n",
                timestamp, elapsedSec, cpuPercent, memoryMB, eventsPerSec, freezeMs, _lastActivity, 
                _cpuSpikes, _memLeaks, _eventStorms);
            
            File.AppendAllText(_logPath, line);

            // Console warning for freezes
            if (freezeMs > 100)
            {
                Console.WriteLine($"[PERF-WARN] Freeze detected: {freezeMs}ms during {_lastActivity}");
            }
        }
        catch
        {
            // Swallow errors to prevent monitor from crashing app
        }
    }

    private void CheckAlerts(double cpu, long mem, long events)
    {
        bool alert = false;
        var msg = new StringBuilder();
        var time = DateTime.Now.ToString("HH:mm:ss");

        if (cpu >= 99.0) 
        { 
            _cpuSpikes++; 
            msg.AppendLine($"[{time}] CPU SPIKE: {cpu:F1}%");
            alert = true;
        }
        
        if (mem > 5000) 
        { 
            _memLeaks++; 
            msg.AppendLine($"[{time}] MEMORY LEAK: {mem} MB");
            alert = true;
        }
        
        if (events > 20000) 
        { 
            _eventStorms++; 
            msg.AppendLine($"[{time}] EVENT STORM: {events}/sec");
            alert = true;
        }

        if (alert)
        {
            File.AppendAllText(_alertLogPath, msg.ToString());
        }
    }

    private double GetCpuUsage(Process process)
    {
        var currentProcessorTime = process.TotalProcessorTime;
        var currentMonitorTime = DateTime.UtcNow;
        
        var cpuUsedMs = (currentProcessorTime - _lastProcessorTime).TotalMilliseconds;
        var totalMsPassed = (currentMonitorTime - _lastMonitorTime).TotalMilliseconds;
        
        _lastProcessorTime = currentProcessorTime;
        _lastMonitorTime = currentMonitorTime;
        
        if (totalMsPassed > 0 && Environment.ProcessorCount > 0)
        {
            var cpuUsage = (cpuUsedMs / (Environment.ProcessorCount * totalMsPassed)) * 100.0;
            return Math.Min(100.0, Math.Max(0, cpuUsage));
        }
        
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _heartbeatTimer?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
