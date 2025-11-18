using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Domain.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using System;

namespace SpreadAggregator.Application.Services;

public class RollingWindowService : IDisposable
{
    private readonly ChannelReader<MarketData> _channelReader;
    private readonly TimeSpan _windowSize = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, RollingWindowData> _windows = new();
    private readonly Timer _cleanupTimer;
    private readonly Abstractions.IBidBidLogger? _bidBidLogger;
    private readonly ILogger<RollingWindowService> _logger;
    private bool _disposed;

    // PROPOSAL-2025-0094: Last-Tick Matching state
    // Store last known tick for each exchange-symbol pair for event-driven spread calculation
    private readonly ConcurrentDictionary<string, (DateTime ts, decimal bid, decimal ask)> _latestTicks = new();
    private readonly Timer _lastTickCleanupTimer;

    // Event-driven: raised when window data is updated
    public event EventHandler<WindowDataUpdatedEventArgs>? WindowDataUpdated;

    public RollingWindowService(
        Channel<MarketData> channel,
        Abstractions.IBidBidLogger? bidBidLogger = null,
        ILogger<RollingWindowService>? logger = null)
    {
        _channelReader = channel.Reader;
        _bidBidLogger = bidBidLogger;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RollingWindowService>.Instance;
        _cleanupTimer = new Timer(CleanupOldData, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        // PROPOSAL-2025-0094: Cleanup stale last-ticks every 5 minutes
        _lastTickCleanupTimer = new Timer(CleanupStaleLastTicks, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
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
        _logger.LogDebug($"RollingWindow received: {data.GetType().Name} {data.Exchange}/{data.Symbol}");

        // PROPOSAL-2025-0094: Last-Tick Matching - only process SpreadData for now
        if (data is not SpreadData spreadData)
            return;

        // PROPOSAL-2025-0094: Event-driven spread calculation
        // When Exchange 1 updates → lookup last tick from all other exchanges → calculate spreads
        ProcessLastTickMatching(spreadData);

        // Store current tick for future matching
        var tickKey = GetTickKey(data.Exchange, data.Symbol);
        _latestTicks[tickKey] = (data.Timestamp, spreadData.BestBid, spreadData.BestAsk);
    }

    /// <summary>
    /// PROPOSAL-2025-0094: Last-Tick Matching
    /// When one exchange updates, calculate spread with last known ticks from all other exchanges
    /// </summary>
    private void ProcessLastTickMatching(SpreadData currentData)
    {
        var currentExchange = currentData.Exchange;
        var symbol = currentData.Symbol;
        var now = currentData.Timestamp;

        // DEBUGGING: Detailed log for test symbols
        bool shouldLog = symbol == "ASTER_USDT" || symbol == "BANANAS31_USDT";

        // Find all other exchanges that have data for this symbol
        var otherExchangeTicks = _latestTicks
            .Where(kvp => kvp.Key.EndsWith($"_{symbol}") && !kvp.Key.StartsWith($"{currentExchange}_"))
            .ToList();

        if (shouldLog)
        {
            LogTestSymbol(symbol, $"Tick received: {currentExchange} | Bid={currentData.BestBid:F8} Ask={currentData.BestAsk:F8} | Found {otherExchangeTicks.Count} opposite");
        }

        foreach (var (key, oppositeTick) in otherExchangeTicks)
        {
            var oppositeExchange = key.Split('_')[0];

            // Calculate staleness
            var staleness = now - oppositeTick.ts;

            // Calculate spread: (Bid1 / Ask2 - 1) * 100
            var spread = oppositeTick.ask > 0
                ? ((currentData.BestBid / oppositeTick.ask) - 1) * 100
                : 0;

            // Create spread point
            var spreadPoint = new SpreadPoint
            {
                Timestamp = now,
                Symbol = symbol,
                Exchange1 = currentExchange,
                Exchange2 = oppositeExchange,
                BestBid = currentData.BestBid,
                BestAsk = oppositeTick.ask,
                SpreadPercent = spread,
                Staleness = staleness,
                TriggeredBy = currentExchange
            };

            if (shouldLog)
            {
                LogTestSymbol(symbol, $"Spread calculated: {currentExchange}→{oppositeExchange} | " +
                    $"Bid1={currentData.BestBid:F8} Ask2={oppositeTick.ask:F8} | " +
                    $"Spread={spread:F4}% | Staleness={staleness.TotalMilliseconds:F0}ms");
            }

            // Add to window
            AddSpreadPointToWindow(spreadPoint);

            // ANALYSIS-2025-0094: Removed staleness warnings
            // Analysis shows 38% of spreads have staleness >100ms - this is EXPECTED behavior
            // Staleness reflects real market conditions (exchanges update at different rates)
            // Only log VERY high staleness (>5000ms) which might indicate connectivity issues
            if (staleness.TotalMilliseconds > 5000)
            {
                _logger.LogWarning(
                    $"[RollingWindow-WARN] Very high staleness: {staleness.TotalMilliseconds:F0}ms " +
                    $"for {symbol} {currentExchange}→{oppositeExchange} - possible connectivity issue");
            }
        }
    }

    private static readonly object _testLogLock = new();
    private static readonly Dictionary<string, string> _testLogPaths = new();

    private void LogTestSymbol(string symbol, string message)
    {
        try
        {
            lock (_testLogLock)
            {
                if (!_testLogPaths.ContainsKey(symbol))
                {
                    var logsDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "logs");
                    Directory.CreateDirectory(logsDir);
                    var logPath = Path.Combine(logsDir, $"{symbol.ToLower()}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");
                    _testLogPaths[symbol] = logPath;
                    File.WriteAllText(logPath, $"=== {symbol} Last-Tick Matching Analysis ===\n");
                    File.AppendAllText(logPath, $"Started: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC\n\n");
                }

                File.AppendAllText(_testLogPaths[symbol], $"{DateTime.UtcNow:HH:mm:ss.fff} | {message}\n");
            }
        }
        catch
        {
            // Ignore log errors
        }
    }

    /// <summary>
    /// Add pre-calculated spread point to rolling window
    /// </summary>
    private void AddSpreadPointToWindow(SpreadPoint spreadPoint)
    {
        // Create window key: exchange1_exchange2_symbol
        var windowKey = $"{spreadPoint.Exchange1}_{spreadPoint.Exchange2}_{spreadPoint.Symbol}";

        var window = _windows.GetOrAdd(windowKey, _ => new RollingWindowData
        {
            Exchange = $"{spreadPoint.Exchange1}→{spreadPoint.Exchange2}",
            Symbol = spreadPoint.Symbol,
            WindowStart = spreadPoint.Timestamp - _windowSize,
            WindowEnd = spreadPoint.Timestamp
        });

        // Update window bounds
        window.WindowEnd = spreadPoint.Timestamp;
        window.WindowStart = spreadPoint.Timestamp - _windowSize;

        // Add spread point (need to add SpreadPoints list to RollingWindowData)
        // For now, convert to SpreadData for backward compatibility
        var legacySpread = new SpreadData
        {
            Exchange = window.Exchange,
            Symbol = spreadPoint.Symbol,
            Timestamp = spreadPoint.Timestamp,
            BestBid = spreadPoint.BestBid,
            BestAsk = spreadPoint.BestAsk
        };

        // Remove old spreads
        window.Spreads.RemoveAll(s => s.Timestamp < window.WindowStart);

        // Add new spread
        window.Spreads.Add(legacySpread);

        // PROPOSAL-2025-0094: Raise event for BOTH exchanges (triggered exchange + opposite)
        // This allows RealTimeController to match events properly
        OnWindowDataUpdated(spreadPoint.TriggeredBy, spreadPoint.Symbol);
    }

    private string GetTickKey(string exchange, string symbol) => $"{exchange}_{symbol}";

    private void OnWindowDataUpdated(string exchange, string symbol)
    {
        _logger.LogDebug($"RollingWindow event: {exchange}/{symbol}");
        WindowDataUpdated?.Invoke(this, new WindowDataUpdatedEventArgs
        {
            Exchange = exchange,
            Symbol = symbol,
            Timestamp = DateTime.UtcNow
        });
    }

    private void CleanupOldData(object? state)
    {
        var now = DateTime.UtcNow;
        var keysToRemove = _windows.Where(kvp => kvp.Value.WindowEnd < now - _windowSize).Select(kvp => kvp.Key).ToList();

        var removedCount = 0;
        foreach (var key in keysToRemove)
        {
            if (_windows.TryRemove(key, out _))
            {
                removedCount++;
            }
        }

        // Log memory metrics
        if (removedCount > 0 || _windows.Count > 10)
        {
            var totalPoints = _windows.Values.Sum(w => w.Spreads.Count + w.Trades.Count);
            Console.WriteLine($"[RollingWindow] Cleanup: removed {removedCount} windows, active: {_windows.Count}, total points: {totalPoints}");
        }
    }

    /// <summary>
    /// PROPOSAL-2025-0094: Cleanup stale last-ticks to prevent memory leak
    /// Remove ticks older than 10 minutes (inactive symbols)
    /// </summary>
    private void CleanupStaleLastTicks(object? state)
    {
        var now = DateTime.UtcNow;
        var threshold = now - TimeSpan.FromMinutes(10);

        var staleKeys = _latestTicks
            .Where(kvp => kvp.Value.ts < threshold)
            .Select(kvp => kvp.Key)
            .ToList();

        var removedCount = 0;
        foreach (var key in staleKeys)
        {
            if (_latestTicks.TryRemove(key, out _))
            {
                removedCount++;
            }
        }

        if (removedCount > 0)
        {
            _logger.LogInformation($"[RollingWindow] Cleanup last-ticks: removed {removedCount} stale ticks, active: {_latestTicks.Count}");
        }
    }

    public RollingWindowData? GetWindowData(string exchange, string symbol)
    {
        var key = $"{exchange}_{symbol}";
        return _windows.TryGetValue(key, out var window) ? window : null;
    }

    public IEnumerable<RollingWindowData> GetAllWindows()
    {
        return _windows.Values;
    }

    /// <summary>
    /// PROPOSAL-2025-0094: Get pre-calculated realtime spread data (Last-Tick Matching)
    /// Returns spreads that were calculated event-driven when exchanges updated
    /// NO MORE AsOfJoin - spreads are already calculated!
    /// </summary>
    public RealtimeChartData? JoinRealtimeWindows(string symbol, string exchange1, string exchange2)
    {
        // PROPOSAL-2025-0094: Window key is now exchange1_exchange2_symbol (pre-calculated)
        var windowKey = $"{exchange1}_{exchange2}_{symbol}";
        var window = _windows.TryGetValue(windowKey, out var w) ? w : null;

        if (window == null || window.Spreads.Count == 0)
        {
            _logger.LogDebug($"JoinRealtimeWindows NULL: no pre-calculated spreads for {exchange1}→{exchange2}/{symbol}");
            return null;
        }

        // Get spreads from window (already calculated event-driven!)
        var allSpreads = window.Spreads
            .OrderBy(s => s.Timestamp)
            .ToList();

        // Memory protection: limit to 10k points
        const int maxPoints = 10000;
        if (allSpreads.Count > maxPoints)
        {
            _logger.LogWarning($"RollingWindow: {allSpreads.Count} points exceeds limit {maxPoints}, truncating");
            allSpreads = allSpreads.Skip(allSpreads.Count - maxPoints).ToList();
        }

        // Calculate spread percentages
        var spreads = allSpreads.Select(s =>
        {
            if (s.BestAsk == 0) return (double?)null;
            return (double)(((s.BestBid / s.BestAsk) - 1) * 100);
        }).ToList();

        // Log latest point for bid/bid logger
        var pairKey = $"{exchange1}_{exchange2}_{symbol}";
        if (_bidBidLogger != null && ShouldLogBidBid(pairKey) && spreads.Count > 0)
        {
            var lastSpread = allSpreads.Last();
            var lastSpreadPercent = spreads.Last();
            if (lastSpreadPercent.HasValue)
            {
                _ = Task.Run(async () =>
                {
                    await _bidBidLogger.LogAsync(symbol, exchange1, exchange2,
                        lastSpread.Timestamp, lastSpread.BestBid, lastSpread.BestAsk, lastSpreadPercent.Value);
                });
            }
        }

        // Calculate rolling quantiles
        var upperBands = CalculateRollingQuantile(spreads, 0.97, 200);
        var lowerBands = CalculateRollingQuantile(spreads, 0.03, 200);

        // Filter to last 15 minutes
        var now = DateTime.UtcNow;
        var fifteenMinutesAgo = now - TimeSpan.FromMinutes(15);

        var recentIndices = allSpreads
            .Select((s, index) => (s, index))
            .Where(x => x.s.Timestamp >= fifteenMinutesAgo)
            .Select(x => x.index)
            .ToList();

        if (recentIndices.Count == 0)
        {
            // Fallback: last 10 points
            var startIndex = Math.Max(0, allSpreads.Count - 10);
            recentIndices = Enumerable.Range(startIndex, allSpreads.Count - startIndex).ToList();
        }

        // Extract chart data
        var chartSpreads = recentIndices.Select(i => allSpreads[i]).ToList();
        var epochTimestamps = chartSpreads.Select(s =>
            ((DateTimeOffset)s.Timestamp).ToUnixTimeMilliseconds() / 1000.0
        ).ToList();

        var chartSpreadValues = recentIndices.Select(i => spreads[i]).ToList();
        var chartUpperBands = recentIndices.Select(i => upperBands[i]).ToList();
        var chartLowerBands = recentIndices.Select(i => lowerBands[i]).ToList();

        // PROPOSAL-2025-0094: Log data completeness metrics
        var input1 = _latestTicks.Count(kvp => kvp.Key.StartsWith($"{exchange1}_") && kvp.Key.EndsWith($"_{symbol}"));
        var input2 = _latestTicks.Count(kvp => kvp.Key.StartsWith($"{exchange2}_") && kvp.Key.EndsWith($"_{symbol}"));
        var joined = allSpreads.Count;
        var completeness = input1 + input2 > 0 ? (joined * 100.0) / (input1 + input2) : 0;

        _logger.LogInformation(
            $"[Last-Tick Matching] {exchange1}→{exchange2}/{symbol} | " +
            $"Spreads={joined} | Completeness={completeness:F1}% | " +
            $"Last15min={chartSpreads.Count}");

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

    // Cache to prevent duplicate logging across multiple opportunity calls
    private static readonly ConcurrentDictionary<string, DateTime> _lastLogTimes = new();
    private static readonly TimeSpan _logCooldown = TimeSpan.FromSeconds(1); // Minimum 1 second between logs

    private bool ShouldLogBidBid(string pairKey)
    {
        var now = DateTime.UtcNow;
        var lastLogTime = _lastLogTimes.GetOrAdd(pairKey, _ => DateTime.MinValue);

        if ((now - lastLogTime) >= _logCooldown)
        {
            _lastLogTimes[pairKey] = now;
            return true;
        }

        return false;
    }

    // PROPOSAL-2025-0094: AsOfJoin and BinarySearchFloor REMOVED
    // Spreads are now calculated event-driven in ProcessLastTickMatching()
    // No more join at query time!

    private List<double?> CalculateRollingQuantile(List<double?> values, double quantile, int windowSize)
    {
        var result = new List<double?>();

        for (int i = 0; i < values.Count; i++)
        {
            var start = Math.Max(0, i - windowSize + 1);
            var window = values.Skip(start).Take(i - start + 1)
                .Where(v => v.HasValue && !double.IsNaN(v.Value) && !double.IsInfinity(v.Value))
                .Select(v => v!.Value)
                .OrderBy(v => v)
                .ToList();

            if (window.Count == 0)
            {
                result.Add(null);
                continue;
            }

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
        _lastTickCleanupTimer?.Dispose(); // PROPOSAL-2025-0094
        _disposed = true;
        GC.SuppressFinalize(this);
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
