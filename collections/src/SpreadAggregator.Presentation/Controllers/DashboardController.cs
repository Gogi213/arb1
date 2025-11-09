using Microsoft.AspNetCore.Mvc;
using SpreadAggregator.Infrastructure.Services.Charts;
using SpreadAggregator.Presentation.Models;

namespace SpreadAggregator.Presentation.Controllers;

/// <summary>
/// Dashboard API Controller
/// Provides historical chart data for arbitrage opportunities
/// Replaces Python charts/server.py /api/dashboard_data endpoint
/// </summary>
[ApiController]
[Route("api")]
public class DashboardController : ControllerBase
{
    private readonly ILogger<DashboardController> _logger;
    private readonly ParquetReaderService _parquetReader;
    private readonly OpportunityFilterService _opportunityFilter;

    public DashboardController(
        ILogger<DashboardController> logger,
        ParquetReaderService parquetReader,
        OpportunityFilterService opportunityFilter)
    {
        _logger = logger;
        _parquetReader = parquetReader;
        _opportunityFilter = opportunityFilter;
    }

    /// <summary>
    /// Stream chart data for all high-opportunity pairs
    /// Returns NDJSON (newline-delimited JSON)
    /// </summary>
    [HttpGet("dashboard_data")]
    [Produces("application/x-ndjson")]
    public async IAsyncEnumerable<ChartDataDto> GetDashboardData()
    {
        _logger.LogInformation("Received request for /api/dashboard_data");

        // Load filtered opportunities
        var opportunities = _opportunityFilter.GetFilteredOpportunities();
        _logger.LogInformation($"Streaming data for {opportunities.Count} opportunities");

        int processedCount = 0;

        // Stream each chart
        foreach (var opp in opportunities)
        {
            ChartData? chartData = null;

            try
            {
                chartData = await _parquetReader.LoadAndProcessPairAsync(
                    opp.Symbol, opp.Exchange1, opp.Exchange2);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing pair {opp.Symbol} ({opp.Exchange1}/{opp.Exchange2})");
            }

            if (chartData != null)
            {
                processedCount++;
                yield return new ChartDataDto
                {
                    Symbol = chartData.Symbol,
                    Exchange1 = chartData.Exchange1,
                    Exchange2 = chartData.Exchange2,
                    Timestamps = chartData.Timestamps,
                    Spreads = chartData.Spreads,
                    UpperBand = chartData.UpperBand,
                    LowerBand = chartData.LowerBand
                };
            }
        }

        _logger.LogInformation($"Finished streaming. Successfully sent {processedCount} chart objects");
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
}
