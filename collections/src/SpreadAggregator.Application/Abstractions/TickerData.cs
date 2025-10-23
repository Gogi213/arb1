namespace SpreadAggregator.Application.Abstractions;

/// <summary>
/// A Data Transfer Object for ticker information, including volume.
/// </summary>
public class TickerData
{
    public required string Symbol { get; init; }
    public decimal QuoteVolume { get; init; }
    public decimal BestBid { get; init; }
    public decimal BestAsk { get; init; }
    public DateTime Timestamp { get; set; }
}