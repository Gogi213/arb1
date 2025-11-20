using SpreadAggregator.Application.Services;
using SpreadAggregator.Domain.Entities;
using Xunit;

namespace SpreadAggregator.Tests.Application.Services;

/// <summary>
/// Integration tests for Phase 1, Task 1.2: SignalDetector
/// </summary>
public class SignalDetectorTests
{
    [Fact]
    public void ProcessDeviation_EntryThreshold_EmitsEntrySignal()
    {
        // Arrange
        var detector = new SignalDetector(entryThreshold: 0.35m, exitThreshold: 0.05m);
        Signal? capturedSignal = null;
        detector.OnEntrySignal += (signal) => capturedSignal = signal;

        var deviation = new DeviationData
        {
            Symbol = "BTC_USDT",
            DeviationPercentage = 0.40m, // Above 0.35% threshold
            CheapExchange = "Gate",
            ExpensiveExchange = "Bybit",
            CheapPrice = 50000,
            ExpensivePrice = 50200,
            Timestamp = DateTime.UtcNow
        };

        // Act
        detector.ProcessDeviation(deviation);

        // Assert
        Assert.NotNull(capturedSignal);
        Assert.Equal("BTC_USDT", capturedSignal.Symbol);
        Assert.Equal(SignalType.Entry, capturedSignal.Type);
        Assert.Equal("Gate", capturedSignal.CheapExchange);
        Assert.Equal(0.40m, capturedSignal.Deviation);
    }

    [Fact]
    public void ProcessDeviation_BelowEntryThreshold_NoSignal()
    {
        // Arrange
        var detector = new SignalDetector(entryThreshold: 0.35m, exitThreshold: 0.05m);
        Signal? capturedSignal = null;
        detector.OnEntrySignal += (signal) => capturedSignal = signal;

        var deviation = new DeviationData
        {
            Symbol = "BTC_USDT",
            DeviationPercentage = 0.20m, // Below 0.35% threshold
            CheapExchange = "Gate",
            ExpensiveExchange = "Bybit",
            CheapPrice = 50000,
            ExpensivePrice = 50100,
            Timestamp = DateTime.UtcNow
        };

        // Act
        detector.ProcessDeviation(deviation);

        // Assert
        Assert.Null(capturedSignal);
    }

    [Fact]
    public void ProcessDeviation_DuplicateEntry_OnlyOneSignal()
    {
        // Arrange
        var detector = new SignalDetector(entryThreshold: 0.35m, exitThreshold: 0.05m);
        int signalCount = 0;
        detector.OnEntrySignal += (_) => signalCount++;

        var deviation = new DeviationData
        {
            Symbol = "BTC_USDT",
            DeviationPercentage = 0.40m,
            CheapExchange = "Gate",
            ExpensiveExchange = "Bybit",
            CheapPrice = 50000,
            ExpensivePrice = 50200,
            Timestamp = DateTime.UtcNow
        };

        // Act - Process same deviation twice
        detector.ProcessDeviation(deviation);
        detector.ProcessDeviation(deviation);

        // Assert - Should only emit once (duplicate prevention)
        Assert.Equal(1, signalCount);
    }

    [Fact]
    public void ProcessDeviation_ExitThreshold_EmitsExitSignal()
    {
        // Arrange
        var detector = new SignalDetector(entryThreshold: 0.35m, exitThreshold: 0.05m);
        Signal? entrySignal = null;
        Signal? exitSignal = null;
        detector.OnEntrySignal += (signal) => entrySignal = signal;
        detector.OnExitSignal += (signal) => exitSignal = signal;

        // Act - First entry signal
        var entryDeviation = new DeviationData
        {
            Symbol = "BTC_USDT",
            DeviationPercentage = 0.40m,
            CheapExchange = "Gate",
            ExpensiveExchange = "Bybit",
            CheapPrice = 50000,
            ExpensivePrice = 50200,
            Timestamp = DateTime.UtcNow
        };
        detector.ProcessDeviation(entryDeviation);

        // Act - Then convergence (deviation → 0)
        var exitDeviation = new DeviationData
        {
            Symbol = "BTC_USDT",
            DeviationPercentage = 0.02m, // Below 0.05% exit threshold
            CheapExchange = "Gate",
            ExpensiveExchange = "Bybit",
            CheapPrice = 50000,
            ExpensivePrice = 50010,
            Timestamp = DateTime.UtcNow
        };
        detector.ProcessDeviation(exitDeviation);

        // Assert
        Assert.NotNull(entrySignal);
        Assert.NotNull(exitSignal);
        Assert.Equal(SignalType.Entry, entrySignal.Type);
        Assert.Equal(SignalType.Exit, exitSignal.Type);
        Assert.Equal("BTC_USDT", exitSignal.Symbol);
    }

    [Fact]
    public void ProcessDeviation_ExitWithoutEntry_NoExitSignal()
    {
        // Arrange
        var detector = new SignalDetector(entryThreshold: 0.35m, exitThreshold: 0.05m);
        Signal? exitSignal = null;
        detector.OnExitSignal += (signal) => exitSignal = signal;

        var deviation = new DeviationData
        {
            Symbol = "BTC_USDT",
            DeviationPercentage = 0.02m, // Below exit threshold but no active entry
            CheapExchange = "Gate",
            ExpensiveExchange = "Bybit",
            CheapPrice = 50000,
            ExpensivePrice = 50010,
            Timestamp = DateTime.UtcNow
        };

        // Act
        detector.ProcessDeviation(deviation);

        // Assert - No exit signal without active entry
        Assert.Null(exitSignal);
    }

    [Fact]
    public async Task ProcessDeviation_Cooldown_PreventsDuplicates()
    {
        // Arrange
        var cooldown = TimeSpan.FromMilliseconds(500);
        var detector = new SignalDetector(
            entryThreshold: 0.35m,
            exitThreshold: 0.05m,
            signalCooldown: cooldown
        );

        int signalCount = 0;
        detector.OnEntrySignal += (_) => signalCount++;

        var deviation = new DeviationData
        {
            Symbol = "BTC_USDT",
            DeviationPercentage = 0.40m,
            CheapExchange = "Gate",
            ExpensiveExchange = "Bybit",
            CheapPrice = 50000,
            ExpensivePrice = 50200,
            Timestamp = DateTime.UtcNow
        };

        // Act
        detector.ProcessDeviation(deviation);
        
        // Clear first signal to allow new entry
        var exitDev = new DeviationData
        {
            Symbol = "BTC_USDT",
            DeviationPercentage = 0.02m,
            CheapExchange = "Gate",
            ExpensiveExchange = "Bybit",
            CheapPrice = 50000,
            ExpensivePrice = 50010,
            Timestamp = DateTime.UtcNow
        };
        detector.ProcessDeviation(exitDev);
        
        // Try to emit again immediately (should be blocked by cooldown)
        detector.ProcessDeviation(deviation);
        
        // Wait for cooldown to expire
        await Task.Delay(cooldown + TimeSpan.FromMilliseconds(100));
        
        // Try again after cooldown
        detector.ProcessDeviation(deviation);

        // Assert - Should have 2 signals (initial + after cooldown)
        Assert.Equal(2, signalCount);
    }

    [Fact]
    public void GetActiveSignals_ReturnsCurrentSignals()
    {
        // Arrange
        var detector = new SignalDetector(entryThreshold: 0.35m, exitThreshold: 0.05m);

        var deviation = new DeviationData
        {
            Symbol = "BTC_USDT",
            DeviationPercentage = 0.40m,
            CheapExchange = "Gate",
            ExpensiveExchange = "Bybit",
            CheapPrice = 50000,
            ExpensivePrice = 50200,
            Timestamp = DateTime.UtcNow
        };

        detector.ProcessDeviation(deviation);

        // Act
        var activeSignals = detector.GetActiveSignals();

        // Assert
        Assert.Single(activeSignals);
        Assert.Equal("BTC_USDT", activeSignals[0].Symbol);
        Assert.Equal(SignalType.Entry, activeSignals[0].Type);
    }
}
