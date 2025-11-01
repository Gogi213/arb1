namespace SpreadAggregator.Domain.Entities;

public abstract class MarketData
{
    public required string Exchange { get; init; }
    public required string Symbol { get; init; }
    public DateTime Timestamp { get; set; }
}