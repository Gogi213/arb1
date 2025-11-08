using Microsoft.AspNetCore.Mvc;
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

    public DashboardController(ILogger<DashboardController> logger)
    {
        _logger = logger;
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

        // TODO Sprint 2: Implement full functionality
        // 1. Load filtered opportunities (_get_filtered_opportunities)
        // 2. For each opportunity, load and process pair data
        // 3. Stream as NDJSON

        await Task.CompletedTask;

        // Placeholder - return empty for now
        yield break;
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
