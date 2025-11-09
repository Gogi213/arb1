using SpreadAggregator.Domain.Entities;
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
    private bool _disposed;

    // Event-driven: raised when window data is updated
    public event EventHandler<WindowDataUpdatedEventArgs>? WindowDataUpdated;

    public RollingWindowService(Channel<MarketData> channel, Abstractions.IBidBidLogger? bidBidLogger = null)
    {
        _channelReader = channel.Reader;
        _bidBidLogger = bidBidLogger;
        _cleanupTimer = new Timer(CleanupOldData, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await foreach (var data in _channelReader.ReadAllAsync(cancellationToken))
        {
            ProcessData(data);
        }
    }

    private void ProcessData(MarketData data)
    {
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

        foreach (var key in keysToRemove)
        {
            _windows.TryRemove(key, out _);
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

        // AsOf join with 2s tolerance - keep full history for quantile calculations
        var joined = AsOfJoin(data1, data2, TimeSpan.FromSeconds(2));

        if (joined.Count == 0)
            return null;

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

        // Return RECENT points for chart display (last 100 points to show trend)
        // This gives enough points for uPlot to draw a proper line chart
        const int maxChartPoints = 100;
        var startIndex = Math.Max(0, joined.Count - maxChartPoints);

        var chartPoints = joined.Skip(startIndex).ToList();
        var chartSpreads = spreads.Skip(startIndex).ToList();
        var chartUpperBands = upperBands.Skip(startIndex).ToList();
        var chartLowerBands = lowerBands.Skip(startIndex).ToList();

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

    private List<(DateTime timestamp, decimal bid1, decimal bid2)> AsOfJoin(
        List<(DateTime ts, decimal bid)> data1,
        List<(DateTime ts, decimal bid)> data2,
        TimeSpan tolerance)
    {
        var result = new List<(DateTime, decimal, decimal)>();
        int j = 0;

        for (int i = 0; i < data1.Count; i++)
        {
            var targetTs = data1[i].ts;

            // Find closest backward match within tolerance
            while (j < data2.Count && data2[j].ts <= targetTs)
            {
                j++;
            }

            if (j == 0) continue; // No backward match

            var matchTs = data2[j - 1].ts;
            if ((targetTs - matchTs) > tolerance) continue; // Outside tolerance

            result.Add((targetTs, data1[i].bid, data2[j - 1].bid));
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
