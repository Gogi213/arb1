namespace SpreadAggregator.Domain.Entities;

/// <summary>
/// Представляет одно событие сделки с биржи.
/// </summary>
public class TradeData : MarketData
{
    public decimal Price { get; init; }
    public decimal Quantity { get; init; }
    public required string Side { get; init; } // "Buy" или "Sell"
}