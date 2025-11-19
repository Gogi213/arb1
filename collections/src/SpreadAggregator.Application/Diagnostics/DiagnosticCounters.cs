using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace SpreadAggregator.Application.Diagnostics;

/// <summary>
/// Diagnostic counters for testing update frequency
/// Tracks incoming data vs outgoing chart updates per symbol
/// </summary>
public class DiagnosticCounters
{
    private static readonly DiagnosticCounters _instance = new();
    public static DiagnosticCounters Instance => _instance;

    // Track incoming SpreadData per exchange+symbol
    private readonly ConcurrentDictionary<string, long> _incomingData = new();
    
    // Track outgoing WindowDataUpdated events per symbol
    private readonly ConcurrentDictionary<string, long> _outgoingEvents = new();

    private DiagnosticCounters() { }

    public void RecordIncomingData(string exchange, string symbol)
    {
        var key = $"{exchange}_{symbol}";
        _incomingData.AddOrUpdate(key, 1, (k, count) => count + 1);
    }

    public void RecordOutgoingEvent(string exchange, string symbol)
    {
        var key = $"{exchange}_{symbol}";
        _outgoingEvents.AddOrUpdate(key, 1, (k, count) => count + 1);
    }

    public DiagnosticSnapshot GetSnapshot()
    {
        return new DiagnosticSnapshot
        {
            IncomingData = _incomingData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            OutgoingEvents = _outgoingEvents.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };
    }

    public void Reset()
    {
        _incomingData.Clear();
        _outgoingEvents.Clear();
    }
}

public class DiagnosticSnapshot
{
    public Dictionary<string, long> IncomingData { get; set; } = new();
    public Dictionary<string, long> OutgoingEvents { get; set; } = new();
    
    public long GetIncoming(string symbol)
    {
        return IncomingData
            .Where(kvp => kvp.Key.EndsWith($"_{symbol}"))
            .Sum(kvp => kvp.Value);
    }

    public long GetOutgoing(string symbol)
    {
        return OutgoingEvents
            .Where(kvp => kvp.Key.EndsWith($"_{symbol}"))
            .Sum(kvp => kvp.Value);
    }
}
