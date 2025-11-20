using SpreadAggregator.Domain.Entities;
using Microsoft.Extensions.Logging;
using TraderBot.Core; // IExchange from trader

namespace SpreadAggregator.Application.Services;

/// <summary>
/// Phase 1, Task 1.4: Trade execution service.
/// Uses existing trader IExchange for order placement.
/// </summary>
public class TradeExecutor
{
    private readonly Dictionary<string, IExchange> _exchanges;
    private readonly ILogger<TradeExecutor>? _logger;

    public TradeExecutor(
        Dictionary<string, IExchange> exchanges,
        ILogger<TradeExecutor>? logger = null)
    {
        _exchanges = exchanges ?? throw new ArgumentNullException(nameof(exchanges));
        _logger = logger;
    }

    /// <summary>
    /// Execute entry trade on signal (buy on cheap exchange).
    /// </summary>
    public async Task ExecuteEntryAsync(Signal signal)
    {
        var exchange = GetExchange(signal.CheapExchange);
        
        _logger?.LogInformation("[TradeExecutor] ENTRY SIGNAL - BUY {Symbol} on {Exchange} (deviation: {Deviation:F2}%)",
            signal.Symbol, signal.CheapExchange, signal.Deviation);

        // Real order placement using trader's IExchange
        var orderId = await exchange.PlaceOrderAsync(
            symbol: signal.Symbol,
            side: OrderSide.Buy,
            type: NewOrderType.Market,
            quoteQuantity: 6.0m // $6 USDT test
        );

        if (orderId.HasValue)
        {
            _logger?.LogInformation("[TradeExecutor] ✅ ORDER PLACED: ID={OrderId}", orderId.Value);
        }
        else
        {
            _logger?.LogError("[TradeExecutor] ❌ ORDER FAILED");
        }
    }

    /// <summary>
    /// Execute exit trade on signal (sell when converged).
    /// </summary>
    public async Task ExecuteExitAsync(Signal signal)
    {
        _logger?.LogWarning("[TradeExecutor] EXIT SIGNAL - Need position tracking to implement sell logic");
        await Task.CompletedTask;
    }

    private IExchange GetExchange(string exchangeName)
    {
        if (!_exchanges.TryGetValue(exchangeName.ToLowerInvariant(), out var exchange))
        {
            throw new ArgumentException($"Unknown exchange: {exchangeName}");
        }
        return exchange;
    }
}
