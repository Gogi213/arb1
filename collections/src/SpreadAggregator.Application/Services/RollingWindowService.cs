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
    private bool _disposed;

    public RollingWindowService(Channel<MarketData> channel)
    {
        _channelReader = channel.Reader;
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
        }
        else if (data is TradeData tradeData)
        {
            // Удаляем старые трейды
            window.Trades.RemoveAll(t => t.Timestamp < window.WindowStart);

            // Добавляем новый
            window.Trades.Add(tradeData);
        }
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

    public void Dispose()
    {
        if (_disposed) return;

        _cleanupTimer?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
