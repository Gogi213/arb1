namespace SpreadAggregator.Presentation.Models;

/// <summary>
/// DTO for chart data sent to clients
/// </summary>
public class ChartDataDto
{
    public required string Symbol { get; set; }
    public required string Exchange1 { get; set; }
    public required string Exchange2 { get; set; }
    public required List<double> Timestamps { get; set; }
    public required List<double?> Spreads { get; set; }
    public required List<double?> UpperBand { get; set; }
    public required List<double?> LowerBand { get; set; }
}
