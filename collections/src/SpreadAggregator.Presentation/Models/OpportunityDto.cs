namespace SpreadAggregator.Presentation.Models;

/// <summary>
/// DTO for arbitrage opportunity
/// </summary>
public class OpportunityDto
{
    public required string Symbol { get; set; }
    public required string Exchange1 { get; set; }
    public required string Exchange2 { get; set; }
    public int OpportunityCycles { get; set; }
}
