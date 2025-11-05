namespace SpreadAggregator.Domain.Entities;

/// <summary>
/// Represents detailed information about a trading symbol on an exchange.
/// </summary>
public class SymbolInfo
{
    /// <summary>
    /// The name of the exchange.
    /// </summary>
    public string Exchange { get; set; } = string.Empty;

    /// <summary>
    /// The symbol name (e.g., BTCUSDT).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The minimum price change for the symbol.
    /// </summary>
    public decimal PriceStep { get; set; }

    /// <summary>
    /// The minimum quantity change for the symbol.
    /// </summary>
    public decimal QuantityStep { get; set; }

    /// <summary>
    /// The minimum notional value for an order.
    /// </summary>
    public decimal MinNotional { get; set; }
}