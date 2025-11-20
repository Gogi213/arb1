namespace SpreadAggregator.Domain.Entities;

/// <summary>
/// Represents a trading signal based on deviation threshold.
/// Phase 1, Task 1.2: Signal detection logic.
/// </summary>
public class Signal
{
    /// <summary>
    /// Trading pair symbol (e.g., BTC_USDT).
    /// </summary>
    public required string Symbol { get; init; }

    /// <summary>
    /// Deviation percentage that triggered this signal.
    /// </summary>
    public decimal Deviation { get; init; }

    /// <summary>
    /// Signal direction (BUY on cheap exchange, SELL on expensive).
    /// </summary>
    public SignalType Type { get; init; }

    /// <summary>
    /// Exchange with lower bid (buy here).
    /// </summary>
    public required string CheapExchange { get; init; }

    /// <summary>
    /// Exchange with higher bid (target for exit).
    /// </summary>
    public required string ExpensiveExchange { get; init; }

    /// <summary>
    /// Timestamp when signal was generated.
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// When this signal should expire (for cleanup).
    /// </summary>
    public DateTime ExpiresAt { get; init; }
}

/// <summary>
/// Signal type for arbitrage strategy.
/// </summary>
public enum SignalType
{
    /// <summary>
    /// Entry signal: deviation crossed threshold, open position.
    /// </summary>
    Entry,

    /// <summary>
    /// Exit signal: deviation converged to zero, close position.
    /// </summary>
    Exit
}
