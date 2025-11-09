namespace SpreadAggregator.Application.Abstractions;

/// <summary>
/// Logger for bid/bid arbitrage data (chart data)
/// </summary>
public interface IBidBidLogger
{
    /// <summary>
    /// Logs a single bid/bid arbitrage point
    /// </summary>
    Task LogAsync(string symbol, string exchange1, string exchange2,
                  DateTime timestamp, decimal bid1, decimal bid2, double spread);
}
