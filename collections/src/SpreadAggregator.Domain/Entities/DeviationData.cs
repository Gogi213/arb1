namespace SpreadAggregator.Domain.Entities;

/// <summary>
/// Represents price deviation between two exchanges for the same symbol.
/// Phase 1, Task 1.1: Cross-exchange deviation calculation.
/// </summary>
public class DeviationData
{
    /// <summary>
    /// Trading pair symbol (e.g., BTC_USDT).
    /// </summary>
    public required string Symbol { get; init; }

    /// <summary>
    /// Deviation percentage between exchanges.
    /// Positive = ExpensiveExchange is higher, Negative = CheapExchange is lower.
    /// Formula: (ExpensivePrice - CheapPrice) / CheapPrice * 100
    /// </summary>
    public decimal DeviationPercentage { get; init; }

    /// <summary>
    /// Exchange with lower price (cheaper).
    /// </summary>
    public required string CheapExchange { get; init; }

    /// <summary>
    /// Exchange with higher price (more expensive).
    /// </summary>
    public required string ExpensiveExchange { get; init; }

    /// <summary>
    /// Midpoint price on cheap exchange: (bid + ask) / 2
    /// </summary>
    public decimal CheapPrice { get; init; }

    /// <summary>
    /// Midpoint price on expensive exchange: (bid + ask) / 2
    /// </summary>
    public decimal ExpensivePrice { get; init; }

    /// <summary>
    /// Timestamp when deviation was calculated.
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Server timestamp from exchanges (if available, for accuracy).
    /// </summary>
    public DateTime? ServerTimestamp { get; init; }
}
