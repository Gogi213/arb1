using Microsoft.AspNetCore.Mvc;
using SpreadAggregator.Application.Diagnostics;
using System.Linq;

namespace SpreadAggregator.Presentation.Controllers;

/// <summary>
/// Diagnostic API for testing update frequency fix
/// </summary>
[ApiController]
[Route("api/diagnostics")]
public class DiagnosticsController : ControllerBase
{
    [HttpGet("counters")]
    public IActionResult GetCounters()
    {
        var snapshot = DiagnosticCounters.Instance.GetSnapshot();
        
        return Ok(new
        {
            incoming = snapshot.IncomingData.OrderByDescending(kvp => kvp.Value).Take(20),
            outgoing = snapshot.OutgoingEvents.OrderByDescending(kvp => kvp.Value).Take(20)
        });
    }

    [HttpGet("counters/{symbol}")]
    public IActionResult GetCountersForSymbol(string symbol)
    {
        var snapshot = DiagnosticCounters.Instance.GetSnapshot();
        
        var incoming = snapshot.IncomingData
            .Where(kvp => kvp.Key.EndsWith($"_{symbol}"))
            .OrderByDescending(kvp => kvp.Value)
            .ToList();
        
        var outgoing = snapshot.OutgoingEvents
            .Where(kvp => kvp.Key.EndsWith($"_{symbol}"))
            .OrderByDescending(kvp => kvp.Value)
            .ToList();
        
        var totalIncoming = incoming.Sum(kvp => kvp.Value);
        var totalOutgoing = outgoing.Sum(kvp => kvp.Value);
        
        return Ok(new
        {
            symbol,
            totalIncoming,
            totalOutgoing,
            ratio = totalIncoming > 0 ? (double)totalOutgoing / totalIncoming : 0,
            incomingDetails = incoming,
            outgoingDetails = outgoing,
            explanation = totalOutgoing > totalIncoming 
                ? $"✅ FIX WORKING: {totalOutgoing} events from {totalIncoming} inputs ({totalOutgoing / (double)totalIncoming:F2}x multiplier - both directions calculated)"
                : $"⚠️ BEFORE FIX: {totalOutgoing} events from {totalIncoming} inputs (only one direction)"
        });
    }

    [HttpPost("counters/reset")]
    public IActionResult ResetCounters()
    {
        DiagnosticCounters.Instance.Reset();
        return Ok(new { message = "Counters reset" });
    }
}
