using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SpreadAggregator.Application.Services;

/// <summary>
/// DIAGNOSTIC PATCH - Counts CleanupExpiredSignals calls
/// Add this to SignalDetector.cs temporarily to prove the hypothesis
/// </summary>
public partial class SignalDetector
{
    // DIAGNOSTIC: Add these fields
    private static long _cleanupCallCount = 0;
    private static long _processDeviationCallCount = 0;
    private static DateTime _lastReport = DateTime.UtcNow;
    
    // DIAGNOSTIC: Add this method call to ProcessDeviation
    public void ProcessDeviation_INSTRUMENTED(DeviationData deviation)
    {
        Interlocked.Increment(ref _processDeviationCallCount);
        
        var absDeviation = Math.Abs(deviation.DeviationPercentage);
        var symbol = deviation.Symbol;

        // Check for ENTRY signal
        if (absDeviation >= _entryThreshold)
        {
            DetectEntrySignal(deviation);
        }
        // Check for EXIT signal (only if active entry exists)
        else if (absDeviation <= _exitThreshold && _activeSignals.ContainsKey(symbol))
        {
            DetectExitSignal(deviation);
        }

        // Cleanup expired signals
        CleanupExpiredSignals_INSTRUMENTED();
    }
    
    // DIAGNOSTIC: Replace CleanupExpiredSignals with this
    private void CleanupExpiredSignals_INSTRUMENTED()
    {
        Interlocked.Increment(ref _cleanupCallCount);
        
        // Report every 1 second
        var now = DateTime.UtcNow;
        if ((now - _lastReport).TotalSeconds >= 1.0)
        {
            var cleanupRate = _cleanupCallCount;
            var processRate = _processDeviationCallCount;
            
            Console.WriteLine($"");
            Console.WriteLine($"========== DIAGNOSTIC REPORT ==========");
            Console.WriteLine($"[DIAGNOSTIC] ProcessDeviation calls/sec: {processRate}");
            Console.WriteLine($"[DIAGNOSTIC] CleanupExpiredSignals calls/sec: {cleanupRate}");
            Console.WriteLine($"[DIAGNOSTIC] Active signals: {_activeSignals.Count}");
            Console.WriteLine($"=======================================");
            Console.WriteLine($"");
            
            Interlocked.Exchange(ref _cleanupCallCount, 0);
            Interlocked.Exchange(ref _processDeviationCallCount, 0);
            _lastReport = now;
        }
        
        // Original cleanup logic
        var expiredSymbols = _activeSignals
            .Where(kvp => kvp.Value.ExpiresAt < now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var symbol in expiredSymbols)
        {
            _activeSignals.TryRemove(symbol, out _);
            Console.WriteLine($"[SignalDetector] Expired signal for {symbol}");
        }
    }
}
