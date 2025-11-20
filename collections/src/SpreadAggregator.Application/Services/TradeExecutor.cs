using SpreadAggregator.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace SpreadAggregator.Application.Services;

/// <summary>
/// Phase 1, Task 1.4: Trade execution service.
/// Current: Mock implementation (logs to console).
/// Future: Real exchange API integration.
/// </summary>
public class TradeExecutor
{
    private readonly ILogger<TradeExecutor>? _logger;

    public TradeExecutor(ILogger<TradeExecutor>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Execute entry trade on signal (buy on cheap exchange).
    /// </summary>
    public async Task ExecuteEntryAsync(Signal signal)
    {
        // Phase 1: Mock implementation
        var message = $"[TradeExecutor] ENTRY SIGNAL - BUY {signal.Symbol} on {signal.CheapExchange} " +
                     $"(deviation: {signal.Deviation:F2}%)";
        
        _logger?.LogInformation(message);
        Console.WriteLine(message);

        // TODO Phase 1.4: Real order placement
        // await exchange.PlaceOrderAsync(OrderSide.Buy, signal.Symbol, ...);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Execute exit trade on signal (sell when converged).
    /// </summary>
    public async Task ExecuteExitAsync(Signal signal)
    {
        // Phase 1: Mock implementation
        var message = $"[TradeExecutor] EXIT SIGNAL - SELL {signal.Symbol} " +
                     $"(deviation converged: {signal.Deviation:F2}%)";
        
        _logger?.LogInformation(message);
        Console.WriteLine(message);

        // TODO Phase 1.4: Real order placement
        // await exchange.PlaceOrderAsync(OrderSide.Sell, signal.Symbol, ...);

        await Task.CompletedTask;
    }
}
