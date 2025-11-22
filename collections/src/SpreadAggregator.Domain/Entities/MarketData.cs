namespace SpreadAggregator.Domain.Entities;

public abstract class MarketData
{
    public required string Exchange { get; init; }
    public required string Symbol { get; init; }
    public DateTime Timestamp { get; set; }
}

public class RollingWindowData
{
    public required string Exchange { get; init; }
    public required string Symbol { get; init; }
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public Queue<SpreadData> Spreads { get; set; } = new();
    public Queue<TradeData> Trades { get; set; } = new();
}
