using SpreadAggregator.Domain.Entities;
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

        var key = $"{data.Exchange}_{data.Symbol}";
        var window = _windows.GetOrAdd(key, _ => new RollingWindowData
        {
            Exchange = data.Exchange,
            Symbol = data.Symbol,
            WindowStart = data.Timestamp - _windowSize,
            WindowEnd = data.Timestamp
        });

        // Обновляем окно
        window.WindowEnd = data.Timestamp;

        if (data is SpreadData spreadData)
        {
            // Удаляем старые спреды
            window.Spreads.RemoveAll(s => s.Timestamp < window.WindowStart);

            // Добавляем новый
            window.Spreads.Add(spreadData);

            // Raise event for spread data update
            OnWindowDataUpdated(data.Exchange, data.Symbol);
        }
        else if (data is TradeData tradeData)
        {
            // Удаляем старые трейды
            window.Trades.RemoveAll(t => t.Timestamp < window.WindowStart);

            // Добавляем новый
            window.Trades.Add(tradeData);

            // Raise event for trade data update
            OnWindowDataUpdated(data.Exchange, data.Symbol);
        }
    }

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
    /// Join real-time windows for two exchanges and calculate spread
    /// Returns recent points for chart display, latest point for real-time updates
    /// Replaces Python _join_realtime_windows()
    /// </summary>
    public RealtimeChartData? JoinRealtimeWindows(string symbol, string exchange1, string exchange2)
    {
        var window1 = GetWindowData(exchange1, symbol);
        var window2 = GetWindowData(exchange2, symbol);

        // DEBUG: Log why returning NULL
        if (window1 == null)
            _logger.LogDebug($"JoinRealtimeWindows NULL: window1 ({exchange1}/{symbol}) not found");
        else if (window1.Spreads.Count == 0)
            _logger.LogDebug($"JoinRealtimeWindows NULL: window1 ({exchange1}/{symbol}) has 0 spreads");

        if (window2 == null)
            _logger.LogDebug($"JoinRealtimeWindows NULL: window2 ({exchange2}/{symbol}) not found");
        else if (window2.Spreads.Count == 0)
            _logger.LogDebug($"JoinRealtimeWindows NULL: window2 ({exchange2}/{symbol}) has 0 spreads");

        if (window1 == null || window2 == null ||
            window1.Spreads.Count == 0 || window2.Spreads.Count == 0)
            return null;

        // Convert to lists with timestamps and bids
        var data1 = window1.Spreads
            .OrderBy(s => s.Timestamp)
            .Select(s => (ts: s.Timestamp, bid: s.BestBid))
            .ToList();

        var data2 = window2.Spreads
            .OrderBy(s => s.Timestamp)
            .Select(s => (ts: s.Timestamp, bid: s.BestAsk)) // Use BestAsk for second exchange
            .ToList();

        // AsOf join with 20ms tolerance for HFT real-time trading
        var joined = AsOfJoin(data1, data2, TimeSpan.FromMilliseconds(20));

        if (joined.Count == 0)
            return null;

        // Memory protection: limit joined data to prevent OOM
        const int maxJoinedPoints = 10000;
        if (joined.Count > maxJoinedPoints)
        {
            _logger.LogWarning($"RollingWindow Warning: {joined.Count} joined points exceeds limit {maxJoinedPoints}, truncating to recent {maxJoinedPoints}");
            joined = joined.Skip(joined.Count - maxJoinedPoints).ToList();
        }

        // Calculate spreads for the entire history (needed for quantiles)
        var spreads = joined.Select(x =>
        {
            if (x.bid2 == 0) return (double?)null;
            return (double)(((x.bid1 / x.bid2) - 1) * 100);
        }).ToList();

        // Log ONLY the latest bid/bid point - but only once per unique exchange pair
        // Use a static cache to prevent duplicate logging across multiple opportunity calls
        var pairKey = $"{exchange1}_{exchange2}_{symbol}";
        if (_bidBidLogger != null && ShouldLogBidBid(pairKey))
        {
            var lastPoint = joined.Last();
            var lastSpread = spreads.Last();
            if (lastSpread.HasValue)
            {
                _ = Task.Run(async () =>
                {
                    await _bidBidLogger.LogAsync(symbol, exchange1, exchange2,
                                                 lastPoint.timestamp, lastPoint.bid1, lastPoint.bid2, lastSpread.Value);
                });
            }
        }

        // Calculate rolling quantiles using FULL history (for accurate bands)
        var upperBands = CalculateRollingQuantile(spreads, 0.97, 200);
        var lowerBands = CalculateRollingQuantile(spreads, 0.03, 200);

        // Return points for chart display (last 15 minutes for dynamic window)
        // This gives enough points for uPlot to draw a proper line chart with time-based window
        var now = DateTime.UtcNow;
        var fifteenMinutesAgo = now - TimeSpan.FromMinutes(15);

        // Filter data to last 15 minutes
        var recentIndices = joined
            .Select((point, index) => (point, index))
            .Where(x => x.point.timestamp >= fifteenMinutesAgo)
            .Select(x => x.index)
            .ToList();

        if (recentIndices.Count == 0)
        {
            // If no data in last 15 minutes, return last 10 points as fallback
            var startIndex = Math.Max(0, joined.Count - 10);
            recentIndices = Enumerable.Range(startIndex, joined.Count - startIndex).ToList();
        }

        var chartPoints = recentIndices.Select(i => joined[i]).ToList();
        var chartSpreads = recentIndices.Select(i => spreads[i]).ToList();
        var chartUpperBands = recentIndices.Select(i => upperBands[i]).ToList();
        var chartLowerBands = recentIndices.Select(i => lowerBands[i]).ToList();

        // Convert to epoch timestamps
        var epochTimestamps = chartPoints.Select(x =>
            ((DateTimeOffset)x.timestamp).ToUnixTimeMilliseconds() / 1000.0
        ).ToList();

        return new RealtimeChartData
        {
            Symbol = symbol,
            Exchange1 = exchange1,
            Exchange2 = exchange2,
            Timestamps = epochTimestamps,
            Spreads = chartSpreads,
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

    /// <summary>
    /// AsOf join with binary search for HFT performance: O(n log n) instead of O(n²)
    /// Tracks detailed loss metrics for HFT optimization
    /// </summary>
    private List<(DateTime timestamp, decimal bid1, decimal bid2)> AsOfJoin(
        List<(DateTime ts, decimal bid)> data1,
        List<(DateTime ts, decimal bid)> data2,
        TimeSpan tolerance)
    {
        var result = new List<(DateTime, decimal, decimal)>();

        // HFT Metrics: track losses
        int noBackwardMatch = 0;
        int outsideTolerance = 0;
        var delays = new List<double>(); // Track actual delays in ms

        for (int i = 0; i < data1.Count; i++)
        {
            var targetTs = data1[i].ts;

            // Binary search for largest timestamp <= targetTs in data2
            int idx = BinarySearchFloor(data2, targetTs);

            if (idx == -1)
            {
                noBackwardMatch++;
                continue; // No backward match
            }

            var matchTs = data2[idx].ts;
            var delay = (targetTs - matchTs).TotalMilliseconds;

            if ((targetTs - matchTs) > tolerance)
            {
                outsideTolerance++;
                continue; // Outside tolerance
            }

            delays.Add(delay);
            result.Add((targetTs, data1[i].bid, data2[idx].bid));
        }

        // Log detailed statistics for HFT monitoring
        if (data1.Count > 0)
        {
            var successRate = (result.Count * 100.0) / data1.Count;
            var lossRate = 100.0 - successRate;

            var avgDelay = delays.Any() ? delays.Average() : 0;
            var maxDelay = delays.Any() ? delays.Max() : 0;

            _logger.LogInformation(
                $"[HFT AsOfJoin] Tolerance={tolerance.TotalMilliseconds}ms | " +
                $"Input={data1.Count}x{data2.Count} | " +
                $"Joined={result.Count} ({successRate:F1}%) | " +
                $"Lost={data1.Count - result.Count} ({lossRate:F1}%): " +
                $"NoMatch={noBackwardMatch}, OutsideTolerance={outsideTolerance} | " +
                $"AvgDelay={avgDelay:F2}ms, MaxDelay={maxDelay:F2}ms"
            );
        }

        return result;
    }

    /// <summary>
    /// Binary search: найти наибольший индекс где data[idx].ts <= target
    /// HFT optimization: O(log n) вместо O(n) linear scan
    /// </summary>
    private int BinarySearchFloor(List<(DateTime ts, decimal bid)> data, DateTime target)
    {
        int left = 0, right = data.Count - 1;
        int result = -1;

        while (left <= right)
        {
            int mid = left + (right - left) / 2;

            if (data[mid].ts <= target)
            {
                result = mid;
                left = mid + 1;
            }
            else
            {
                right = mid - 1;
            }
        }

        return result;
    }

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
