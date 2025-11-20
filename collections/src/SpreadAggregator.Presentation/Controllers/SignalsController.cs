using Microsoft.AspNetCore.Mvc;
using SpreadAggregator.Application.Services;
using SpreadAggregator.Domain.Entities;

namespace SpreadAggregator.Presentation.Controllers;

/// <summary>
/// Phase 1, Task 1.3: REST API for arbitrage signals.
/// Target latency: <20ms for HFT requirements.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SignalsController : ControllerBase
{
    private readonly SignalDetector _signalDetector;

    public SignalsController(SignalDetector signalDetector)
    {
        _signalDetector = signalDetector;
    }

    /// <summary>
    /// Get all currently active entry signals.
    /// </summary>
    /// <returns>List of active signals with metadata</returns>
    [HttpGet("active")]
    public IActionResult GetActiveSignals()
    {
        var signals = _signalDetector.GetActiveSignals();
        
        var response = new
        {
            signals = signals.Select(s => new
            {
                symbol = s.Symbol,
                deviation = s.Deviation,
                type = s.Type.ToString().ToLower(),
                cheapExchange = s.CheapExchange,
                expensiveExchange = s.ExpensiveExchange,
                timestamp = s.Timestamp.ToString("O"), // ISO 8601 format
                ageMs = (int)(DateTime.UtcNow - s.Timestamp).TotalMilliseconds
            }),
            count = signals.Count,
            timestamp = DateTime.UtcNow.ToString("O")
        };

        return Ok(response);
    }

    /// <summary>
    /// Get signal for specific symbol (if active).
    /// </summary>
    /// <param name="symbol">Trading pair symbol (e.g., BTC_USDT)</param>
    /// <returns>Signal if active, 404 if not found</returns>
    [HttpGet("{symbol}")]
    public IActionResult GetSignal(string symbol)
    {
        var signal = _signalDetector.GetSignal(symbol);
        
        if (signal == null)
        {
            return NotFound(new { message = $"No active signal for {symbol}" });
        }

        var response = new
        {
            symbol = signal.Symbol,
            deviation = signal.Deviation,
            type = signal.Type.ToString().ToLower(),
            cheapExchange = signal.CheapExchange,
            expensiveExchange = signal.ExpensiveExchange,
            timestamp = signal.Timestamp.ToString("O"),
            ageMs = (int)(DateTime.UtcNow - signal.Timestamp).TotalMilliseconds
        };

        return Ok(response);
    }

    /// <summary>
    /// Health check endpoint for signals service.
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "healthy",
            service = "signals",
            timestamp = DateTime.UtcNow.ToString("O")
        });
    }
}
