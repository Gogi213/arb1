namespace SpreadAggregator.Domain.Entities;

/// <summary>
/// Represents spread data for a trading pair.
/// </summary>
public class SpreadData
{
    /// <summary>
    /// Name of the exchange.
    /// </summary>
    public required string Exchange { get; init; }

    /// <summary>
    /// Trading symbol (e.g., BTC/USDT).
    /// </summary>
    public required string Symbol { get; init; }

    /// <summary>
    /// The highest price a buyer is willing to pay.
    /// </summary>
    public decimal BestBid { get; init; }

    /// <summary>
    /// The lowest price a seller is willing to accept.
    /// </summary>
    public decimal BestAsk { get; init; }

    /// <summary>
    /// The calculated bid-ask spread in percentage.
    /// </summary>
    public decimal SpreadPercentage { get; set; }
    public decimal MinVolume { get; set; }
    public decimal MaxVolume { get; set; }
    public DateTime Timestamp { get; set; }
}