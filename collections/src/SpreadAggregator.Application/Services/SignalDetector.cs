using SpreadAggregator.Domain.Entities;
using System.Collections.Concurrent;

namespace SpreadAggregator.Application.Services;

/// <summary>
/// Phase 1, Task 1.2: Detects entry/exit signals based on deviation thresholds.
/// User strategy: Buy at bid on cheap exchange, sell at bid when converged.
/// </summary>
public class SignalDetector
{
    private readonly decimal _entryThreshold; // |deviation| >= this → entry signal (e.g., 0.35%)
    private readonly decimal _exitThreshold;  // |deviation| <= this → exit signal (e.g., 0.05%)
    private readonly TimeSpan _signalCooldown; // Min time between signals for same symbol
    private readonly TimeSpan _signalExpiry; // How long signal stays active

    // Track active signals: Symbol → Signal
    private readonly ConcurrentDictionary<string, Signal> _activeSignals = new();

    // Track last signal time per symbol (for cooldown)
    private readonly ConcurrentDictionary<string, DateTime> _lastSignalTime = new();

    /// <summary>
    /// Event fired when entry signal detected.
    /// </summary>
    public event Action<Signal>? OnEntrySignal;

    /// <summary>
    /// Event fired when exit signal detected.
    /// </summary>
    public event Action<Signal>? OnExitSignal;

    public SignalDetector(
        decimal entryThreshold = 0.35m,
        decimal exitThreshold = 0.05m,
        TimeSpan? signalCooldown = null,
        TimeSpan? signalExpiry = null)
    {
        _entryThreshold = entryThreshold;
        _exitThreshold = exitThreshold;
        _signalCooldown = signalCooldown ?? TimeSpan.FromSeconds(10);
        _signalExpiry = signalExpiry ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Process deviation data and detect entry/exit signals.
    /// Called by DeviationCalculator when deviation event fires.
    /// </summary>
    public void ProcessDeviation(DeviationData deviation)
    {
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
        CleanupExpiredSignals();
    }

    private void DetectEntrySignal(DeviationData deviation)
    {
        var symbol = deviation.Symbol;

        // Check cooldown: prevent spam signals
        if (_lastSignalTime.TryGetValue(symbol, out var lastTime))
        {
            if (DateTime.UtcNow - lastTime < _signalCooldown)
            {
                // Still in cooldown period
                return;
            }
        }

        // Check if signal already active (avoid duplicates)
        if (_activeSignals.ContainsKey(symbol))
        {
            // Already have active entry signal for this symbol
            return;
        }

        // Create entry signal
        var signal = new Signal
        {
            Symbol = symbol,
            Deviation = deviation.DeviationPercentage,
            Type = SignalType.Entry,
            CheapExchange = deviation.CheapExchange,
            ExpensiveExchange = deviation.ExpensiveExchange,
            Timestamp = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(_signalExpiry)
        };

        // Mark as active
        _activeSignals[symbol] = signal;
        _lastSignalTime[symbol] = DateTime.UtcNow;

        // Emit event
        OnEntrySignal?.Invoke(signal);
    }

    private void DetectExitSignal(DeviationData deviation)
    {
        var symbol = deviation.Symbol;

        // Get active entry signal
        if (!_activeSignals.TryRemove(symbol, out var entrySignal))
        {
            return; // No active signal (race condition?)
        }

        // Create exit signal
        var exitSignal = new Signal
        {
            Symbol = symbol,
            Deviation = deviation.DeviationPercentage,
            Type = SignalType.Exit,
            CheapExchange = deviation.CheapExchange,
            ExpensiveExchange = deviation.ExpensiveExchange,
            Timestamp = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow // Exit signals don't need expiry
        };

        // Emit event
        OnExitSignal?.Invoke(exitSignal);
    }

    private void CleanupExpiredSignals()
    {
        var now = DateTime.UtcNow;
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

    /// <summary>
    /// Get all currently active entry signals.
    /// Used by API endpoints.
    /// </summary>
    public List<Signal> GetActiveSignals()
    {
        CleanupExpiredSignals();
        return _activeSignals.Values.ToList();
    }

    /// <summary>
    /// Get active signal for specific symbol (if any).
    /// </summary>
    public Signal? GetSignal(string symbol)
    {
        CleanupExpiredSignals();
        _activeSignals.TryGetValue(symbol, out var signal);
        return signal;
    }
}
