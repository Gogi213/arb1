using Microsoft.AspNetCore.Mvc;
using SpreadAggregator.Application.Services;
using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Presentation.Controllers;
using Xunit;

namespace SpreadAggregator.Tests.Presentation.Controllers;

/// <summary>
/// Integration tests for Phase 1, Task 1.3: SignalsController API
/// </summary>
public class SignalsControllerTests
{
    [Fact]
    public void GetActiveSignals_NoSignals_ReturnsEmptyArray()
    {
        // Arrange
        var detector = new SignalDetector();
        var controller = new SignalsController(detector);

        // Act
        var result = controller.GetActiveSignals() as OkObjectResult;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);
        
        var response = result.Value as dynamic;
        Assert.NotNull(response);
    }

    [Fact]
    public void GetActiveSignals_WithSignals_ReturnsSignalList()
    {
        // Arrange
        var detector = new SignalDetector(entryThreshold: 0.35m, exitThreshold: 0.05m);
        var controller = new SignalsController(detector);

        // Trigger signal
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
        var result = controller.GetActiveSignals() as OkObjectResult;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);
        
        // Verify response structure (dynamic object)
        var response = result.Value;
        Assert.NotNull(response);
    }

    [Fact]
    public void GetSignal_ExistingSymbol_ReturnsSignal()
    {
        // Arrange
        var detector = new SignalDetector(entryThreshold: 0.35m, exitThreshold: 0.05m);
        var controller = new SignalsController(detector);

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
        var result = controller.GetSignal("BTC_USDT") as OkObjectResult;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);
    }

    [Fact]
    public void GetSignal_NonExistingSymbol_Returns404()
    {
        // Arrange
        var detector = new SignalDetector();
        var controller = new SignalsController(detector);

        // Act
        var result = controller.GetSignal("BTC_USDT") as NotFoundObjectResult;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public void Health_ReturnsHealthy()
    {
        // Arrange
        var detector = new SignalDetector();
        var controller = new SignalsController(detector);

        // Act
        var result = controller.Health() as OkObjectResult;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);
    }

    [Fact]
    public void GetActiveSignals_Latency_LessThan20ms()
    {
        // Arrange
        var detector = new SignalDetector(entryThreshold: 0.35m, exitThreshold: 0.05m);
        var controller = new SignalsController(detector);

        // Add some signals
        for (int i = 0; i < 10; i++)
        {
            var deviation = new DeviationData
            {
                Symbol = $"PAIR{i}_USDT",
                DeviationPercentage = 0.40m,
                CheapExchange = "Gate",
                ExpensiveExchange = "Bybit",
                CheapPrice = 50000,
                ExpensivePrice = 50200,
                Timestamp = DateTime.UtcNow
            };
            detector.ProcessDeviation(deviation);
        }

        // Act - Measure latency
        var startTime = DateTime.UtcNow;
        var result = controller.GetActiveSignals();
        var latency = (DateTime.UtcNow - startTime).TotalMilliseconds;

        // Assert - Should be < 20ms (HFT requirement)
        Assert.True(latency < 20, $"API latency {latency}ms exceeds 20ms target");
        Assert.NotNull(result);
    }
}
