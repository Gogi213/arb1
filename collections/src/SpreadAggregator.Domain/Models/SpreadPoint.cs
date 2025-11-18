namespace SpreadAggregator.Domain.Models;

/// <summary>
/// PROPOSAL-2025-0094: Spread point with staleness tracking for Last-Tick Matching
/// Represents a calculated spread at the moment when one exchange updates
/// </summary>
public class SpreadPoint
{
    /// <summary>
    /// Timestamp when this spread was calculated (when the triggering exchange updated)
    /// </summary>
    public DateTime Timestamp { get; set; }

    public string Symbol { get; set; } = string.Empty;
    public string Exchange1 { get; set; } = string.Empty;
    public string Exchange2 { get; set; } = string.Empty;

    public decimal BestBid { get; set; }
    public decimal BestAsk { get; set; }

    /// <summary>
    /// Spread percentage: (Bid - Ask) / Ask * 100
    /// </summary>
    public decimal SpreadPercent { get; set; }

    /// <summary>
    /// How stale was the opposite exchange's tick when this spread was calculated
    /// High staleness (>200ms) indicates low market activity or slow updates
    /// </summary>
    public TimeSpan Staleness { get; set; }

    /// <summary>
    /// Which exchange triggered this spread calculation ("Exchange1" or "Exchange2")
    /// </summary>
    public string TriggeredBy { get; set; } = string.Empty;
}
