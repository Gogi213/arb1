using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Domain.Models;
using SpreadAggregator.Application.Helpers;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using System;
using System.Diagnostics;
using System.Text;
using System.IO;
using SpreadAggregator.Application.Diagnostics;

namespace SpreadAggregator.Application.Services;

public class RollingWindowService : IDisposable
{
    // PROPOSAL-2025-0095: Memory safety - bounded collections with LRU eviction
    private const int MAX_WINDOWS = 10_000;
    private const int MAX_LATEST_TICKS = 50_000;

    private readonly ChannelReader<MarketData> _channelReader;
    private readonly TimeSpan _windowSize = TimeSpan.FromMinutes(5); // Rolling window: 5 minutes
    private readonly LruCache<string, RollingWindowData> _windows;
    private readonly Timer _cleanupTimer;
    private readonly Abstractions.IBidBidLogger? _bidBidLogger;
    private readonly ILogger<RollingWindowService> _logger;
    private bool _disposed;

    private readonly LruCache<string, TickData> _latestTicks;
    private readonly Timer _lastTickCleanupTimer;

    // PERFORMANCE: Index to quickly find which exchanges trade a symbol
    private readonly ConcurrentDictionary<string, HashSet<string>> _symbolExchanges = new();

    // TARGETED EVENTS: Index mapping "Exchange_Symbol" → ["WindowKey1", "WindowKey2", ...]
    // Allows efficient lookup of which windows are affected by an exchange+symbol update
    private readonly ConcurrentDictionary<string, HashSet<string>> _exchangeSymbolIndex = new();
    
    // TARGETED EVENTS: Per-window event handlers (replaces global broadcast)
    // Key format: "Exchange1_Exchange2_Symbol"
    private readonly ConcurrentDictionary<string, EventHandler<WindowDataUpdatedEventArgs>?> _windowEvents = new();

    // PROFILING SYSTEM
    private readonly Profiler _profiler = new();
    private readonly Timer _profilingTimer;
    private int _cleanupRunning = 0; // Atomic flag for cleanup
    
    // PERFORMANCE MONITOR
    private readonly PerformanceMonitor? _perfMonitor;

    [Obsolete("Use SubscribeToWindow instead for better performance. Global broadcast causes unnecessary CPU load.")]
    public event EventHandler<WindowDataUpdatedEventArgs>? WindowDataUpdated;

    public RollingWindowService(
        Channel<MarketData> channel,
        Abstractions.IBidBidLogger? bidBidLogger = null,
        ILogger<RollingWindowService>? logger = null,
        PerformanceMonitor? perfMonitor = null)
    {
        _channelReader = channel.Reader;
        _bidBidLogger = bidBidLogger;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RollingWindowService>.Instance;
        _perfMonitor = perfMonitor;

        _windows = new LruCache<string, RollingWindowData>(MAX_WINDOWS);
        _latestTicks = new LruCache<string, TickData>(MAX_LATEST_TICKS);

        _cleanupTimer = new Timer(CleanupOldData, null, TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(30)), TimeSpan.FromMinutes(5));
        _lastTickCleanupTimer = new Timer(CleanupStaleLastTicks, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
        
        // PROFILING: Log stats every 10 seconds
        _profilingTimer = new Timer(LogProfilingStats, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    private void LogProfilingStats(object? state)
    {
        var stats = _profiler.GetAndReset();
        if (stats.Count == 0) return;

        try 
        {
            var dir = @"C:\visual projects\arb1\docs\gemini3\profiling";
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"detailed_profile_{DateTime.UtcNow:yyyyMMdd}.csv");
            
            var sb = new StringBuilder();
            if (!File.Exists(file))
                sb.AppendLine("Time,Scope,Count,AvgMs,MinMs,MaxMs,TotalMs,GC0,GC1,GC2,MemMB");

            var now = DateTime.UtcNow.ToString("HH:mm:ss");
            
            var gc0 = GC.CollectionCount(0);
            var gc1 = GC.CollectionCount(1);
            var gc2 = GC.CollectionCount(2);
            var mem = GC.GetTotalMemory(false) / 1024 / 1024; // MB

            foreach (var kvp in stats.OrderBy(k => k.Key))
            {
                var s = kvp.Value;
                if (s.Count == 0) continue;
                
                var avg = s.TotalTicks / (double)s.Count / TimeSpan.TicksPerMillisecond;
                var min = s.MinTicks / (double)TimeSpan.TicksPerMillisecond;
                var max = s.MaxTicks / (double)TimeSpan.TicksPerMillisecond;
                var total = s.TotalTicks / (double)TimeSpan.TicksPerMillisecond;

                sb.AppendLine($"{now},{kvp.Key},{s.Count},{avg:F4},{min:F4},{max:F4},{total:F4},{gc0},{gc1},{gc2},{mem}");
            }
            
            File.AppendAllText(file, sb.ToString());
        }
        catch {}
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("RollingWindowService started, waiting for data...");
        await foreach (var data in _channelReader.ReadAllAsync(cancellationToken))
        {
            ProcessData(data);
        }
    }

    private void ProcessData(MarketData data)
    {
        using var _ = _profiler.Measure("Total_ProcessData");
        _perfMonitor?.RecordEvent("ProcessData");

        if (data is not SpreadData spreadData)
            return;

        // Diagnostics.DiagnosticCounters.Instance.RecordIncomingData(data.Exchange, data.Symbol);

        // 1. MATCHING
        using (_profiler.Measure("1_Matching"))
        {
            ProcessLastTickMatching(spreadData);
        }

        // 2. UPDATE LATEST TICKS
        using (_profiler.Measure("2_UpdateTicks"))
        {
            var tickKey = GetTickKey(data.Exchange, data.Symbol);
            _latestTicks.AddOrUpdate(tickKey, new TickData
            {
                Timestamp = data.Timestamp,
                Bid = spreadData.BestBid,
                Ask = spreadData.BestAsk
            });
        }

        // 3. UPDATE INDEX
        using (_profiler.Measure("3_UpdateIndex"))
        {
            _symbolExchanges.AddOrUpdate(data.Symbol, 
                new HashSet<string> { data.Exchange }, 
                (key, set) => {
                    lock (set) { set.Add(data.Exchange); }
                    return set;
                });
        }
    }

    private void ProcessLastTickMatching(SpreadData currentData)
    {
        var currentExchange = currentData.Exchange;
        var symbol = currentData.Symbol;
        var now = currentData.Timestamp;

        List<(string key, TickData tick)> otherExchangeTicks;

        // 1.1 INDEX LOOKUP
        using (_profiler.Measure("1.1_IndexLookup"))
        {
            if (!_symbolExchanges.TryGetValue(symbol, out var exchanges))
                return;

            lock (exchanges)
            {
                otherExchangeTicks = exchanges
                    .Where(ex => ex != currentExchange)
                    .Select(ex => 
                    {
                        var key = $"{ex}_{symbol}";
                        return (key, tick: _latestTicks.TryGetValue(key, out var t) ? t : default(TickData?));
                    })
                    .Where(x => x.tick.HasValue)
                    .Select(x => (x.key, x.tick!.Value))
                    .ToList();
            }
        }

        // 1.2 CALCULATION LOOP
        using (_profiler.Measure("1.2_CalcLoop"))
        {
            foreach (var (key, oppositeTick) in otherExchangeTicks)
            {
                var oppositeExchange = key.Split('_')[0];
                var staleness = now - oppositeTick.Timestamp;

                var (ex1, ex2) = string.Compare(currentExchange, oppositeExchange, StringComparison.Ordinal) < 0
                    ? (currentExchange, oppositeExchange)
                    : (oppositeExchange, currentExchange);
                
                var bid1 = ex1 == currentExchange ? currentData.BestBid : oppositeTick.Bid;
                var bid2 = ex2 == currentExchange ? currentData.BestBid : oppositeTick.Bid;
                
                if (bid1 > 0 && bid2 > 0)
                {
                    var spread = ((bid1 / bid2) - 1) * 100;
                    
                    var spreadPoint = new SpreadPoint
                    {
                        Timestamp = now,
                        Symbol = symbol,
                        Exchange1 = ex1,
                        Exchange2 = ex2,
                        BestBid = bid1,
                        BestAsk = bid2,
                        SpreadPercent = spread,
                        Staleness = staleness,
                        TriggeredBy = currentExchange
                    };
                    
                    // 1.3 ADD TO WINDOW
                    using (_profiler.Measure("1.3_AddToWindow"))
                    {
                        AddSpreadPointToWindow(spreadPoint);
                    }
                }
            }
        }
    }

    private void AddSpreadPointToWindow(SpreadPoint spreadPoint)
    {
        var windowKey = $"{spreadPoint.Exchange1}_{spreadPoint.Exchange2}_{spreadPoint.Symbol}";

        RollingWindowData? window;
        
        // 1.3.1 DICT LOOKUP
        using (_profiler.Measure("1.3.1_DictLookup"))
        {
            if (!_windows.TryGetValue(windowKey, out window) || window == null)
            {
                window = new RollingWindowData
                {
                    Exchange = $"{spreadPoint.Exchange1}→{spreadPoint.Exchange2}",
                    Symbol = spreadPoint.Symbol,
                    WindowStart = spreadPoint.Timestamp - _windowSize,
                    WindowEnd = spreadPoint.Timestamp
                };
                _windows.AddOrUpdate(windowKey, window);
                
                // TARGETED EVENTS: Populate index for efficient event routing
                // Map both exchanges to this window so we can find it quickly
                var index1 = $"{spreadPoint.Exchange1}_{spreadPoint.Symbol}";
                var index2 = $"{spreadPoint.Exchange2}_{spreadPoint.Symbol}";
                
                _exchangeSymbolIndex.AddOrUpdate(index1,
                    new HashSet<string> { windowKey },
                    (k, set) => { lock (set) { set.Add(windowKey); } return set; });
                
                _exchangeSymbolIndex.AddOrUpdate(index2,
                    new HashSet<string> { windowKey },
                    (k, set) => { lock (set) { set.Add(windowKey); } return set; });
            }
        }

        // 1.3.2 LIST ADD
        using (_profiler.Measure("1.3.2_ListAdd"))
        {
            window.WindowEnd = spreadPoint.Timestamp;
            window.WindowStart = spreadPoint.Timestamp - _windowSize;

            var legacySpread = new SpreadData
            {
                Exchange = window.Exchange,
                Symbol = spreadPoint.Symbol,
                Timestamp = spreadPoint.Timestamp,
                BestBid = spreadPoint.BestBid,
                BestAsk = spreadPoint.BestAsk
            };

            // PERFORMANCE FIX: Sliding Window with Queue (O(1) removal)
            lock (window.Spreads)
            {
                window.Spreads.Enqueue(legacySpread);
                
                // Incremental cleanup: Remove old data immediately (Sliding Window)
                var threshold = spreadPoint.Timestamp - _windowSize;
                while (window.Spreads.Count > 0 && window.Spreads.Peek().Timestamp < threshold)
                {
                    window.Spreads.Dequeue();
                }
                
                // Safety Cap: Prevent infinite growth if timestamps are weird
                while (window.Spreads.Count > 5000)
                {
                    window.Spreads.Dequeue();
                }
            }
        }

        OnWindowDataUpdated(spreadPoint.TriggeredBy, spreadPoint.Symbol);
    }

    private string GetTickKey(string exchange, string symbol) => $"{exchange}_{symbol}";

    private void OnWindowDataUpdated(string exchange, string symbol)
    {
        // _logger.LogDebug($"RollingWindow event: {exchange}/{symbol}");
        // Diagnostics.DiagnosticCounters.Instance.RecordOutgoingEvent(exchange, symbol);
        
        // TARGETED EVENTS: Find affected windows and notify only their subscribers
        var indexKey = $"{exchange}_{symbol}";
        if (_exchangeSymbolIndex.TryGetValue(indexKey, out var affectedWindows))
        {
            var eventArgs = new WindowDataUpdatedEventArgs
            {
                Exchange = exchange,
                Symbol = symbol,
                Timestamp = DateTime.UtcNow
            };
            
            HashSet<string> windowsCopy;
            lock (affectedWindows)
            {
                windowsCopy = new HashSet<string>(affectedWindows);
            }
            
            foreach (var windowKey in windowsCopy)
            {
                if (_windowEvents.TryGetValue(windowKey, out var handler) && handler != null)
                {
                    handler.Invoke(this, eventArgs);
                }
            }
        }
        
        // LEGACY: Support old global event (marked as Obsolete)
        // TODO: Remove after all subscribers migrated to SubscribeToWindow
        #pragma warning disable CS0618 // Type or member is obsolete
        WindowDataUpdated?.Invoke(this, new WindowDataUpdatedEventArgs
        {
            Exchange = exchange,
            Symbol = symbol,
            Timestamp = DateTime.UtcNow
        });
        #pragma warning restore CS0618
    }
    
    /// <summary>
    /// Subscribe to updates for a specific window (targeted event delivery).
    /// Replaces global WindowDataUpdated event for better performance.
    /// </summary>
    public void SubscribeToWindow(string symbol, string exchange1, string exchange2, 
        EventHandler<WindowDataUpdatedEventArgs> handler)
    {
        var windowKey = $"{exchange1}_{exchange2}_{symbol}";
        _windowEvents.AddOrUpdate(windowKey, handler, (k, existing) => existing + handler);
        
        _logger.LogDebug($"[RollingWindow] Subscribed to window: {windowKey}");
    }
    
    /// <summary>
    /// Unsubscribe from updates for a specific window.
    /// </summary>
    public void UnsubscribeFromWindow(string symbol, string exchange1, string exchange2,
        EventHandler<WindowDataUpdatedEventArgs> handler)
    {
        var windowKey = $"{exchange1}_{exchange2}_{symbol}";
        if (_windowEvents.TryGetValue(windowKey, out var existing) && existing != null)
        {
            _windowEvents[windowKey] = existing - handler;
            _logger.LogDebug($"[RollingWindow] Unsubscribed from window: {windowKey}");
        }
    }

    private void CleanupOldData(object? state)
    {
        // TASK 2: Prevent concurrent cleanup execution
        if (Interlocked.CompareExchange(ref _cleanupRunning, 1, 0) != 0)
        {
            _logger.LogWarning("[RollingWindow] Cleanup skipped - previous cleanup still running");
            return;
        }

        // Offload to background thread to avoid blocking Timer thread
        Task.Run(async () =>
        {
            try
            {
                await CleanupAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RollingWindow] Error during cleanup");
            }
            finally
            {
                Interlocked.Exchange(ref _cleanupRunning, 0);
            }
        });
    }

    private async Task CleanupAsync()
    {
        using (_profiler.Measure("Cleanup_Async"))
        {
            _perfMonitor?.RecordEvent("Cleanup_Start");
            
            var now = DateTime.UtcNow;
            var threshold = now - _windowSize;
            
            // 1. Evict empty/old windows (fast)
            var removedCount = _windows.EvictWhere((key, window) => window.WindowEnd < threshold);

            // 2. Cleanup internal lists (slow - needs batching)
            int pointsRemoved = 0;
            var windowsSnapshot = _windows.Values.ToList(); // Snapshot to avoid locking dictionary
            int processedCount = 0;
            const int BATCH_SIZE = 100;

            foreach(var window in windowsSnapshot)
            {
                // Cleanup is now handled incrementally in AddSpreadPointToWindow
                // We only need to check for empty windows here (which is handled by EvictWhere above)
                // But strictly speaking, we might want to ensure consistency
                
                // Optional: Double check cap (fast O(1) check)
                if (window.Spreads.Count > 5000)
                {
                    lock (window.Spreads)
                    {
                        while (window.Spreads.Count > 5000) window.Spreads.Dequeue();
                    }
                }
                
                processedCount++;
                
                // Yield every BATCH_SIZE windows to prevent ThreadPool starvation
                if (processedCount % BATCH_SIZE == 0)
                {
                    await Task.Yield(); 
                }
            }

            _perfMonitor?.RecordEvent("Cleanup_End");

            if (removedCount > 0 || pointsRemoved > 0)
            {
                _logger.LogInformation($"[RollingWindow] Cleanup: removed {removedCount} windows, {pointsRemoved} points");
            }
        }
    }

    private void CleanupStaleLastTicks(object? state)
    {
        var now = DateTime.UtcNow;
        var threshold = now - TimeSpan.FromMinutes(5);
        var removedCount = _latestTicks.EvictWhere((key, tick) => tick.Timestamp < threshold);
        if (removedCount > 0)
            _logger.LogInformation($"[RollingWindow] Cleanup last-ticks: removed {removedCount}");
    }

    public RollingWindowData? GetWindowData(string exchange, string symbol)
    {
        var key = $"{exchange}_{symbol}";
        _windows.TryGetValue(key, out var window);
        return window;
    }

    public IEnumerable<RollingWindowData> GetAllWindows() => _windows.Values.ToList();
    public int GetWindowCount() => _windows.Count;
    public int GetTotalSpreadCount() => _windows.Values.Sum(w => w.Spreads.Count);

    public RealtimeChartData? JoinRealtimeWindows(string symbol, string exchange1, string exchange2)
    {
        var windowKey = $"{exchange1}_{exchange2}_{symbol}";
        var window = _windows.TryGetValue(windowKey, out var w) ? w : null;

        if (window == null) return null;

        List<SpreadData> allSpreads;
        lock (window.Spreads)
        {
            if (window.Spreads.Count == 0) return null;
            allSpreads = window.Spreads.OrderBy(s => s.Timestamp).ToList();
        }

        var spreads = allSpreads.Select(s => s.BestAsk == 0 ? (double?)null : (double)(((s.BestBid / s.BestAsk) - 1) * 100)).ToList();

        var upperBands = CalculateRollingQuantile(spreads, 0.97, 200);
        var lowerBands = CalculateRollingQuantile(spreads, 0.03, 200);

        var now = DateTime.UtcNow;
        var fifteenMinutesAgo = now - TimeSpan.FromMinutes(15);
        var recentIndices = allSpreads.Select((s, index) => (s, index)).Where(x => x.s.Timestamp >= fifteenMinutesAgo).Select(x => x.index).ToList();

        if (recentIndices.Count == 0)
        {
            var startIndex = Math.Max(0, allSpreads.Count - 10);
            recentIndices = Enumerable.Range(startIndex, allSpreads.Count - startIndex).ToList();
        }

        var chartSpreads = recentIndices.Select(i => allSpreads[i]).ToList();
        var epochTimestamps = chartSpreads.Select(s => ((DateTimeOffset)s.Timestamp).ToUnixTimeMilliseconds() / 1000.0).ToList();
        var chartSpreadValues = recentIndices.Select(i => spreads[i]).ToList();
        var chartUpperBands = recentIndices.Select(i => upperBands[i]).ToList();
        var chartLowerBands = recentIndices.Select(i => lowerBands[i]).ToList();

        return new RealtimeChartData
        {
            Symbol = symbol,
            Exchange1 = exchange1,
            Exchange2 = exchange2,
            Timestamps = epochTimestamps,
            Spreads = chartSpreadValues,
            UpperBand = chartUpperBands,
            LowerBand = chartLowerBands
        };
    }

    private List<double?> CalculateRollingQuantile(List<double?> values, double quantile, int windowSize)
    {
        var result = new List<double?>();
        for (int i = 0; i < values.Count; i++)
        {
            var start = Math.Max(0, i - windowSize + 1);
            var window = values.Skip(start).Take(i - start + 1)
                .Where(v => v.HasValue && !double.IsNaN(v.Value) && !double.IsInfinity(v.Value))
                .Select(v => v!.Value).OrderBy(v => v).ToList();

            if (window.Count == 0) { result.Add(null); continue; }
            var index = (int)Math.Ceiling(window.Count * quantile) - 1;
            index = Math.Max(0, Math.Min(index, window.Count - 1));
            result.Add(window[index]);
        }
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _cleanupTimer?.Dispose();
        _lastTickCleanupTimer?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    // --- INTERNAL PROFILER CLASS ---
    private class Profiler
    {
        private class MetricStats
        {
            public long Count;
            public long TotalTicks;
            public long MinTicks = long.MaxValue;
            public long MaxTicks = long.MinValue;
        }

        private readonly ConcurrentDictionary<string, MetricStats> _metrics = new();

        public IDisposable Measure(string scope)
        {
            return new ScopeTimer(scope, this);
        }

        public void Record(string scope, long elapsedTicks)
        {
            var stats = _metrics.GetOrAdd(scope, _ => new MetricStats());
            Interlocked.Increment(ref stats.Count);
            Interlocked.Add(ref stats.TotalTicks, elapsedTicks);
            
            // Lockless Min/Max update (approximate is fine for profiling)
            long currentMin, newMin;
            do {
                currentMin = Interlocked.Read(ref stats.MinTicks);
                newMin = Math.Min(currentMin, elapsedTicks);
            } while (newMin < currentMin && Interlocked.CompareExchange(ref stats.MinTicks, newMin, currentMin) != currentMin);

            long currentMax, newMax;
            do {
                currentMax = Interlocked.Read(ref stats.MaxTicks);
                newMax = Math.Max(currentMax, elapsedTicks);
            } while (newMax > currentMax && Interlocked.CompareExchange(ref stats.MaxTicks, newMax, currentMax) != currentMax);
        }

        public Dictionary<string, (long Count, long TotalTicks, long MinTicks, long MaxTicks)> GetAndReset()
        {
            var result = new Dictionary<string, (long, long, long, long)>();
            foreach (var key in _metrics.Keys)
            {
                if (_metrics.TryRemove(key, out var stats))
                {
                    result[key] = (stats.Count, stats.TotalTicks, stats.MinTicks, stats.MaxTicks);
                }
            }
            return result;
        }

        private struct ScopeTimer : IDisposable
        {
            private readonly string _scope;
            private readonly Profiler _profiler;
            // TASK 1: Removed Stopwatch from hot path (2500 calls/sec overhead)
            // private readonly long _startTicks;

            public ScopeTimer(string scope, Profiler profiler)
            {
                _scope = scope;
                _profiler = profiler;
                // _startTicks = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                // TASK 1: Profiling disabled in hot path
                // var elapsed = Stopwatch.GetTimestamp() - _startTicks;
                // _profiler.Record(_scope, elapsed);
            }
        }
    }
}

/// <summary>
/// Real-time chart data
/// </summary>
public class RealtimeChartData
{
    public required string Symbol { get; set; }
    public required string Exchange1 { get; set; }
    public required string Exchange2 { get; set; }
    public required List<double> Timestamps { get; set; }
    public required List<double?> Spreads { get; set; }
    public required List<double?> UpperBand { get; set; }
    public required List<double?> LowerBand { get; set; }
}

/// <summary>
/// Event args for window data updates
/// </summary>
public class WindowDataUpdatedEventArgs : EventArgs
{
    public required string Exchange { get; set; }
    public required string Symbol { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// PROPOSAL-2025-0095: Lightweight tick data for latest ticks cache
/// </summary>
public struct TickData
{
    public DateTime Timestamp { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
}
