namespace SpreadAggregator.Domain.Services;

/// <summary>
/// Provides functionality to calculate the bid-ask spread.
/// </summary>
public class SpreadCalculator
{
    /// <summary>
    /// Calculates the spread percentage between the best bid and best ask prices.
    /// </summary>
    /// <param name="bestBid">The highest price a buyer is willing to pay.</param>
    /// <param name="bestAsk">The lowest price a seller is willing to accept.</param>
    /// <returns>The spread in percentage.</returns>
    /// <exception cref="DivideByZeroException">Thrown when the best ask price is zero.</exception>
    public decimal Calculate(decimal bestBid, decimal bestAsk)
    {
        if (bestAsk == 0)
        {
            throw new DivideByZeroException("Best ask price cannot be zero.");
        }

        return (bestAsk - bestBid) / bestAsk * 100;
    }
}