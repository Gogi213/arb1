using Microsoft.AspNetCore.Mvc;
using SpreadAggregator.Application.Services;
using System;

namespace SpreadAggregator.Presentation.Controllers;

/// <summary>
/// PROPOSAL-2025-0095: Health check endpoint for monitoring
/// </summary>
[ApiController]
[Route("")]
public class HealthController : ControllerBase
{
    private readonly OrchestrationService _orchestration;
    private readonly RollingWindowService _rollingWindow;
    private static readonly DateTime _startTime = DateTime.UtcNow;

    public HealthController(
        OrchestrationService orchestration,
        RollingWindowService rollingWindow)
    {
        _orchestration = orchestration;
        _rollingWindow = rollingWindow;
    }

    /// <summary>
    /// Health check endpoint - returns system status and metrics
    /// </summary>
    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        var uptime = DateTime.UtcNow - _startTime;

        var health = new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            uptime = new
            {
                days = uptime.Days,
                hours = uptime.Hours,
                minutes = uptime.Minutes,
                totalSeconds = (int)uptime.TotalSeconds
            },
            memory = new
            {
                workingSetMB = GC.GetTotalMemory(false) / 1024 / 1024,
                gen0Collections = GC.CollectionCount(0),
                gen1Collections = GC.CollectionCount(1),
                gen2Collections = GC.CollectionCount(2)
            },
            services = new
            {
                rollingWindow = new
                {
                    activeWindows = _rollingWindow.GetWindowCount(),
                    totalSpreads = _rollingWindow.GetTotalSpreadCount()
                },
                exchanges = _orchestration.GetExchangeHealth()
            }
        };

        return Ok(health);
    }

    /// <summary>
    /// Simple alive check - minimal response
    /// </summary>
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new { status = "alive", timestamp = DateTime.UtcNow });
    }
}
