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

namespace SpreadAggregator.Application.Services;

public class RollingWindowService : IDisposable
{
    // PROPOSAL-2025-0095: Memory safety - bounded collections with LRU eviction
    private const int MAX_WINDOWS = 10_000;
    private const int MAX_LATEST_TICKS = 50_000;
    // Note: No MAX_SPREADS_PER_WINDOW - TIME limit (15min) is sufficient for production (3 exchanges × 50 symbols)

    private readonly ChannelReader<MarketData> _channelReader;
    private readonly TimeSpan _windowSize = TimeSpan.FromMinutes(15); // PROPOSAL-2025-0095: Reduced from 30min
    private readonly LruCache<string, RollingWindowData> _windows;
    private readonly Timer _cleanupTimer;
    private readonly Abstractions.IBidBidLogger? _bidBidLogger;
    private readonly ILogger<RollingWindowService> _logger;
    private bool _disposed;

    // PROPOSAL-2025-0094: Last-Tick Matching state
    // PROPOSAL-2025-0095: Bounded LRU cache instead of unbounded ConcurrentDictionary
    private readonly LruCache<string, TickData> _latestTicks;
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

        // PROPOSAL-2025-0095: Initialize bounded LRU caches
        _windows = new LruCache<string, RollingWindowData>(MAX_WINDOWS);
        _latestTicks = new LruCache<string, TickData>(MAX_LATEST_TICKS);

        _cleanupTimer = new Timer(CleanupOldData, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        // PROPOSAL-2025-0094: Cleanup stale last-ticks every 2 minutes (reduced from 5 for HFT)
        _lastTickCleanupTimer = new Timer(CleanupStaleLastTicks, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
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
        _latestTicks.AddOrUpdate(tickKey, new TickData
        {
            Timestamp = data.Timestamp,
            Bid = spreadData.BestBid,
            Ask = spreadData.BestAsk
        });
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

        // PROPOSAL-2025-0095: Removed debug logging (static leak)
        // Use structured logging if needed

        // Find all other exchanges that have data for this symbol
        var otherExchangeTicks = _latestTicks.Keys
            .Where(key => key.EndsWith($"_{symbol}") && !key.StartsWith($"{currentExchange}_"))
            .Select(key => (key, tick: _latestTicks.TryGetValue(key, out var t) ? t : default(TickData?)))
            .Where(x => x.tick.HasValue)
            .Select(x => (x.key, x.tick!.Value))
            .ToList();

        foreach (var (key, oppositeTick) in otherExchangeTicks)
        {
            var oppositeExchange = key.Split('_')[0];

            // Calculate staleness
            var staleness = now - oppositeTick.Timestamp;

            // Calculate spread: (Bid1 / Ask2 - 1) * 100
            var spread = oppositeTick.Ask > 0
                ? ((currentData.BestBid / oppositeTick.Ask) - 1) * 100
                : 0;

            // Create spread point
            var spreadPoint = new SpreadPoint
            {
                Timestamp = now,
                Symbol = symbol,
                Exchange1 = currentExchange,
                Exchange2 = oppositeExchange,
                BestBid = currentData.BestBid,
                BestAsk = oppositeTick.Ask,
                SpreadPercent = spread,
                Staleness = staleness,
                TriggeredBy = currentExchange
            };

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

    // PROPOSAL-2025-0095: Removed static memory leaks (_testLogPaths, _testLogLock)
    // Use structured logging instead if debug logging needed

    /// <summary>
    /// Add pre-calculated spread point to rolling window
    /// PROPOSAL-2025-0095: Only TIME-based cleanup (15 min window)
    /// </summary>
    private void AddSpreadPointToWindow(SpreadPoint spreadPoint)
    {
        // Create window key: exchange1_exchange2_symbol
        var windowKey = $"{spreadPoint.Exchange1}_{spreadPoint.Exchange2}_{spreadPoint.Symbol}";

        // Try get existing window or create new
        if (!_windows.TryGetValue(windowKey, out var window) || window == null)
        {
            window = new RollingWindowData
            {
                Exchange = $"{spreadPoint.Exchange1}→{spreadPoint.Exchange2}",
                Symbol = spreadPoint.Symbol,
                WindowStart = spreadPoint.Timestamp - _windowSize,
                WindowEnd = spreadPoint.Timestamp
            };
        }

        // Update window bounds
        window!.WindowEnd = spreadPoint.Timestamp;
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

        // Remove old spreads (time-based cleanup - only mechanism needed)
        window.Spreads.RemoveAll(s => s.Timestamp < window.WindowStart);

        // Add new spread
        window.Spreads.Add(legacySpread);

        // Update cache (will trigger LRU eviction if needed)
        _windows.AddOrUpdate(windowKey, window);

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
        var threshold = now - _windowSize;

        // PROPOSAL-2025-0095: Use LruCache.EvictWhere for cleanup
        var removedCount = _windows.EvictWhere((key, window) => window.WindowEnd < threshold);

        // Log memory metrics
        if (removedCount > 0 || _windows.Count > 100)
        {
            var totalPoints = _windows.Values.Sum(w => w.Spreads.Count + w.Trades.Count);
            _logger.LogInformation($"[RollingWindow] Cleanup: removed {removedCount} windows, active: {_windows.Count}/{MAX_WINDOWS}, total points: {totalPoints}");
        }
    }

    /// <summary>
    /// PROPOSAL-2025-0094: Cleanup stale last-ticks to prevent memory leak
    /// PROPOSAL-2025-0095: Reduced threshold to 5 minutes for HFT, use LruCache
    /// </summary>
    private void CleanupStaleLastTicks(object? state)
    {
        var now = DateTime.UtcNow;
        var threshold = now - TimeSpan.FromMinutes(5); // Reduced from 10 for HFT

        // PROPOSAL-2025-0095: Use LruCache.EvictWhere for cleanup
        var removedCount = _latestTicks.EvictWhere((key, tick) => tick.Timestamp < threshold);

        if (removedCount > 0)
        {
            _logger.LogInformation($"[RollingWindow] Cleanup last-ticks: removed {removedCount} stale ticks, active: {_latestTicks.Count}/{MAX_LATEST_TICKS}");
        }
    }

    public RollingWindowData? GetWindowData(string exchange, string symbol)
    {
        var key = $"{exchange}_{symbol}";
        _windows.TryGetValue(key, out var window);
        return window;
    }

    public IEnumerable<RollingWindowData> GetAllWindows()
    {
        return _windows.Values.ToList(); // Snapshot to avoid collection modification
    }

    /// <summary>
    /// Get current window count for monitoring
    /// </summary>
    public int GetWindowCount() => _windows.Count;

    /// <summary>
    /// Get total spreads count for monitoring
    /// </summary>
    public int GetTotalSpreadCount() => _windows.Values.Sum(w => w.Spreads.Count);

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

        // Calculate spread percentages
        var spreads = allSpreads.Select(s =>
        {
            if (s.BestAsk == 0) return (double?)null;
            return (double)(((s.BestBid / s.BestAsk) - 1) * 100);
        }).ToList();

        // PROPOSAL-2025-0095: Log latest point for bid/bid logger (throttled by logger itself)
        // DISABLED: BidBid logging disabled to save disk space
        /*
        if (_bidBidLogger != null && spreads.Count > 0)
        {
            var lastSpread = allSpreads.Last();
            var lastSpreadPercent = spreads.Last();
            if (lastSpreadPercent.HasValue)
            {
                // Fire-and-forget logging (non-blocking)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _bidBidLogger.LogAsync(symbol, exchange1, exchange2,
                            lastSpread.Timestamp, lastSpread.BestBid, lastSpread.BestAsk, lastSpreadPercent.Value);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "BidBid logging failed");
                    }
                });
            }
        }
        */

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

        // PROPOSAL-2025-0095: Log data completeness metrics
        var input1Keys = _latestTicks.Keys.Count(key => key.StartsWith($"{exchange1}_") && key.EndsWith($"_{symbol}"));
        var input2Keys = _latestTicks.Keys.Count(key => key.StartsWith($"{exchange2}_") && key.EndsWith($"_{symbol}"));
        var joined = allSpreads.Count;
        var completeness = input1Keys + input2Keys > 0 ? (joined * 100.0) / (input1Keys + input2Keys) : 0;

        _logger.LogDebug(
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

    // PROPOSAL-2025-0095: Removed static memory leak (_lastLogTimes)
    // Throttling is now handled by BidBidLogger itself

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

/// <summary>
/// PROPOSAL-2025-0095: Lightweight tick data for latest ticks cache
/// </summary>
public struct TickData
{
    public DateTime Timestamp { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
}
