using SpreadAggregator.Application.Services;
using SpreadAggregator.Domain.Entities;
using Xunit;

namespace SpreadAggregator.Tests.Application.Services;

/// <summary>
/// Integration tests for Phase 1, Task 1.1: DeviationCalculator
/// </summary>
public class DeviationCalculatorTests
{
    [Fact]
    public void ProcessSpread_TwoExchanges_CalculatesDeviationCorrectly()
    {
        // Arrange
        var calculator = new DeviationCalculator(minDeviationThreshold: 0.10m);
        DeviationData? capturedDeviation = null;
        calculator.OnDeviationDetected += (deviation) => capturedDeviation = deviation;

        var gateSpread = new SpreadData
        {
            Exchange = "Gate",
            Symbol = "BTC_USDT",
            BestBid = 49900,
            BestAsk = 50100,
            Timestamp = DateTime.UtcNow
        };

        var bybitSpread = new SpreadData
        {
            Exchange = "Bybit",
            Symbol = "BTC_USDT",
            BestBid = 50150,
            BestAsk = 50350,
            Timestamp = DateTime.UtcNow
        };

        // Act
        calculator.ProcessSpread(gateSpread);
        calculator.ProcessSpread(bybitSpread);

        // Assert
        Assert.NotNull(capturedDeviation);
        Assert.Equal("BTC_USDT", capturedDeviation.Symbol);
        Assert.Equal("Gate", capturedDeviation.CheapExchange);
        Assert.Equal("Bybit", capturedDeviation.ExpensiveExchange);
        
        // Gate bid: 49900
        // Bybit bid: 50150
        // Deviation: (50150 - 49900) / 49900 * 100 = 0.50%
        Assert.Equal(0.50m, capturedDeviation.DeviationPercentage);
    }

    [Fact]
    public void ProcessSpread_BelowThreshold_NoEventFired()
    {
        // Arrange
        var calculator = new DeviationCalculator(minDeviationThreshold: 0.50m);
        DeviationData? capturedDeviation = null;
        calculator.OnDeviationDetected += (deviation) => capturedDeviation = deviation;

        var gateSpread = new SpreadData
        {
            Exchange = "Gate",
            Symbol = "BTC_USDT",
            BestBid = 50000,
            BestAsk = 50000,
            Timestamp = DateTime.UtcNow
        };

        var bybitSpread = new SpreadData
        {
            Exchange = "Bybit",
            Symbol = "BTC_USDT",
            BestBid = 50100,
            BestAsk = 50100,
            Timestamp = DateTime.UtcNow
        };

        // Act
        calculator.ProcessSpread(gateSpread);
        calculator.ProcessSpread(bybitSpread);

        // Assert
        // Deviation: (50100 - 50000) / 50000 * 100 = 0.20% < 0.50% threshold
        Assert.Null(capturedDeviation);
    }

    [Fact]
    public void ProcessSpread_ReverseOrder_DetectsBybitCheaper()
    {
        // Arrange
        var calculator = new DeviationCalculator(minDeviationThreshold: 0.10m);
        DeviationData? capturedDeviation = null;
        calculator.OnDeviationDetected += (deviation) => capturedDeviation = deviation;

        var gateSpread = new SpreadData
        {
            Exchange = "Gate",
            Symbol = "ETH_USDT",
            BestBid = 3100,
            BestAsk = 3100,
            Timestamp = DateTime.UtcNow
        };

        var bybitSpread = new SpreadData
        {
            Exchange = "Bybit",
            Symbol = "ETH_USDT",
            BestBid = 3080,
            BestAsk = 3080,
            Timestamp = DateTime.UtcNow
        };

        // Act
        calculator.ProcessSpread(gateSpread);
        calculator.ProcessSpread(bybitSpread);

        // Assert
        Assert.NotNull(capturedDeviation);
        Assert.Equal("Bybit", capturedDeviation.CheapExchange);
        Assert.Equal("Gate", capturedDeviation.ExpensiveExchange);
        
        // Deviation: (3100 - 3080) / 3080 * 100 ≈ 0.65%
        Assert.True(Math.Abs(capturedDeviation.DeviationPercentage - 0.65m) < 0.01m);
    }

    [Fact]
    public void ProcessSpread_ThreeExchanges_CalculatesAllPairs()
    {
        // Arrange
        var calculator = new DeviationCalculator(minDeviationThreshold: 0.10m);
        var capturedDeviations = new List<DeviationData>();
        calculator.OnDeviationDetected += (deviation) => capturedDeviations.Add(deviation);

        var gateSpread = new SpreadData
        {
            Exchange = "Gate",
            Symbol = "BTC_USDT",
            BestBid = 50000,
            BestAsk = 50000,
            Timestamp = DateTime.UtcNow
        };

        var bybitSpread = new SpreadData
        {
            Exchange = "Bybit",
            Symbol = "BTC_USDT",
            BestBid = 50200,
            BestAsk = 50200,
            Timestamp = DateTime.UtcNow
        };

        var binanceSpread = new SpreadData
        {
            Exchange = "Binance",
            Symbol = "BTC_USDT",
            BestBid = 50100,
            BestAsk = 50100,
            Timestamp = DateTime.UtcNow
        };

        // Act
        calculator.ProcessSpread(gateSpread);
        calculator.ProcessSpread(bybitSpread);
        calculator.ProcessSpread(binanceSpread);

        // Assert
        // Should detect 3 deviations:
        // Gate vs Bybit, Gate vs Binance, Bybit vs Binance
        Assert.True(capturedDeviations.Count >= 3, $"Expected at least 3 deviations, got {capturedDeviations.Count}");
    }

    [Fact]
    public void GetCurrentDeviation_ReturnsLatestDeviation()
    {
        // Arrange
        var calculator = new DeviationCalculator(minDeviationThreshold: 0.10m);

        var gateSpread = new SpreadData
        {
            Exchange = "Gate",
            Symbol = "BTC_USDT",
            BestBid = 50000,
            BestAsk = 50000,
            Timestamp = DateTime.UtcNow
        };

        var bybitSpread = new SpreadData
        {
            Exchange = "Bybit",
            Symbol = "BTC_USDT",
            BestBid = 50250,
            BestAsk = 50250,
            Timestamp = DateTime.UtcNow
        };

        calculator.ProcessSpread(gateSpread);
        calculator.ProcessSpread(bybitSpread);

        // Act
        var deviation = calculator.GetCurrentDeviation("BTC_USDT", "Gate", "Bybit");

        // Assert
        Assert.NotNull(deviation);
        Assert.Equal(0.50m, deviation.DeviationPercentage);
    }

    [Fact]
    public void ProcessSpread_ArbitrageThreshold_0_35_Percent()
    {
        // Arrange - Test real arbitrage threshold (0.35%)
        var calculator = new DeviationCalculator(minDeviationThreshold: 0.35m);
        DeviationData? capturedDeviation = null;
        calculator.OnDeviationDetected += (deviation) => capturedDeviation = deviation;

        var gateSpread = new SpreadData
        {
            Exchange = "Gate",
            Symbol = "BTC_USDT",
            BestBid = 50000,
            BestAsk = 50000,
            Timestamp = DateTime.UtcNow
        };

        var bybitSpread = new SpreadData
        {
            Exchange = "Bybit",
            Symbol = "BTC_USDT",
            BestBid = 50180, // 0.36% higher
            BestAsk = 50180,
            Timestamp = DateTime.UtcNow
        };

        // Act
        calculator.ProcessSpread(gateSpread);
        calculator.ProcessSpread(bybitSpread);

        // Assert - Should fire because 0.36% >= 0.35%
        Assert.NotNull(capturedDeviation);
        Assert.Equal("BTC_USDT", capturedDeviation.Symbol);
        Assert.True(capturedDeviation.DeviationPercentage >= 0.35m);
    }
}
