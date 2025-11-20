using SpreadAggregator.Application.Services;
using SpreadAggregator.Domain.Entities;
using Xunit;
using Moq;
// using TraderBot.Core; // REMOVED to avoid SpreadData ambiguity

namespace SpreadAggregator.Tests.Integration;

/// <summary>
/// End-to-end integration tests for Phase 1: Signal Detection & Execution
/// Tests the full flow: Deviation → Signal → TradeExecutor
/// </summary>
public class SignalExecutionIntegrationTests
{
    private TradeExecutor CreateExecutor()
    {
        var mockGate = new Mock<TraderBot.Core.IExchange>();
        var mockBybit = new Mock<TraderBot.Core.IExchange>();
        
        // Setup successful order placement
        mockGate.Setup(x => x.PlaceOrderAsync(
            It.IsAny<string>(), 
            It.IsAny<TraderBot.Core.OrderSide>(), 
            It.IsAny<TraderBot.Core.NewOrderType>(), 
            It.IsAny<decimal?>(), 
            It.IsAny<decimal?>(), 
            It.IsAny<decimal?>()))
            .ReturnsAsync(12345L);
            
        mockBybit.Setup(x => x.PlaceOrderAsync(
            It.IsAny<string>(), 
            It.IsAny<TraderBot.Core.OrderSide>(), 
            It.IsAny<TraderBot.Core.NewOrderType>(), 
            It.IsAny<decimal?>(), 
            It.IsAny<decimal?>(), 
            It.IsAny<decimal?>()))
            .ReturnsAsync(67890L);

        var exchanges = new Dictionary<string, TraderBot.Core.IExchange>
        {
            ["gate"] = mockGate.Object,
            ["bybit"] = mockBybit.Object
        };

        return new TradeExecutor(exchanges);
    }

    [Fact]
    public void EndToEnd_DeviationToExecution_EntrySignal()
    {
        // Arrange
        var deviationCalc = new DeviationCalculator(minDeviationThreshold: 0.10m);
        var signalDetector = new SignalDetector(entryThreshold: 0.35m, exitThreshold: 0.05m);
        var tradeExecutor = CreateExecutor();

        // Wire components
        deviationCalc.OnDeviationDetected += signalDetector.ProcessDeviation;
        
        Signal? executedSignal = null;
        signalDetector.OnEntrySignal += async signal =>
        {
            executedSignal = signal;
            await tradeExecutor.ExecuteEntryAsync(signal);
        };

        // Act - Simulate spreads from two exchanges
        var gateSpread = new SpreadData
        {
            Exchange = "Gate",
            Symbol = "BTC_USDT",
            BestBid = 50000m,
            BestAsk = 50100m,
            SpreadPercentage = 0.20m,
            Timestamp = DateTime.UtcNow
        };

        var bybitSpread = new SpreadData
        {
            Exchange = "Bybit",
            Symbol = "BTC_USDT",
            BestBid = 50200m, // +0.40% deviation
            BestAsk = 50300m,
            SpreadPercentage = 0.20m,
            Timestamp = DateTime.UtcNow
        };

        deviationCalc.ProcessSpread(gateSpread);
        deviationCalc.ProcessSpread(bybitSpread);

        // Assert - Entry signal should be generated and executed
        Assert.NotNull(executedSignal);
        Assert.Equal("BTC_USDT", executedSignal.Symbol);
        Assert.Equal(SignalType.Entry, executedSignal.Type);
        Assert.Equal("Gate", executedSignal.CheapExchange);
        Assert.Equal("Bybit", executedSignal.ExpensiveExchange);
        Assert.True(Math.Abs(executedSignal.Deviation) >= 0.35m);
    }

    [Fact]
    public void EndToEnd_DeviationToExecution_ExitSignal()
    {
        // Arrange
        var deviationCalc = new DeviationCalculator(minDeviationThreshold: 0.01m);
        var signalDetector = new SignalDetector(entryThreshold: 0.35m, exitThreshold: 0.05m);
        var tradeExecutor = CreateExecutor();

        deviationCalc.OnDeviationDetected += signalDetector.ProcessDeviation;
        
        Signal? entrySignal = null;
        Signal? exitSignal = null;
        
        signalDetector.OnEntrySignal += async signal =>
        {
            entrySignal = signal;
            await tradeExecutor.ExecuteEntryAsync(signal);
        };
        
        signalDetector.OnExitSignal += async signal =>
        {
            exitSignal = signal;
            await tradeExecutor.ExecuteExitAsync(signal);
        };

        // Act - Step 1: Create entry signal (large deviation)
        deviationCalc.ProcessSpread(new SpreadData
        {
            Exchange = "Gate",
            Symbol = "BTC_USDT",
            BestBid = 50000m,
            BestAsk = 50100m,
            SpreadPercentage = 0.20m,
            Timestamp = DateTime.UtcNow
        });

        deviationCalc.ProcessSpread(new SpreadData
        {
            Exchange = "Bybit",
            Symbol = "BTC_USDT",
            BestBid = 50200m, // +0.40% deviation → ENTRY
            BestAsk = 50300m,
            SpreadPercentage = 0.20m,
            Timestamp = DateTime.UtcNow
        });

        // Act - Step 2: Prices converge (deviation → 0)
        deviationCalc.ProcessSpread(new SpreadData
        {
            Exchange = "Bybit",
            Symbol = "BTC_USDT",
            BestBid = 50010m, // +0.02% deviation → EXIT
            BestAsk = 50110m,
            SpreadPercentage = 0.20m,
            Timestamp = DateTime.UtcNow
        });

        // Assert
        Assert.NotNull(entrySignal);
        Assert.NotNull(exitSignal);
        Assert.Equal(SignalType.Entry, entrySignal.Type);
        Assert.Equal(SignalType.Exit, exitSignal.Type);
        Assert.True(Math.Abs(exitSignal.Deviation) <= 0.05m);
    }

    [Fact]
    public void EndToEnd_BelowThreshold_NoExecution()
    {
        // Arrange
        var deviationCalc = new DeviationCalculator(minDeviationThreshold: 0.10m);
        var signalDetector = new SignalDetector(entryThreshold: 0.35m, exitThreshold: 0.05m);
        var tradeExecutor = CreateExecutor();

        deviationCalc.OnDeviationDetected += signalDetector.ProcessDeviation;
        
        Signal? executedSignal = null;
        signalDetector.OnEntrySignal += async signal =>
        {
            executedSignal = signal;
            await tradeExecutor.ExecuteEntryAsync(signal);
        };

        // Act - Small deviation (below 0.35% threshold)
        deviationCalc.ProcessSpread(new SpreadData
        {
            Exchange = "Gate",
            Symbol = "BTC_USDT",
            BestBid = 50000m,
            BestAsk = 50100m,
            SpreadPercentage = 0.20m,
            Timestamp = DateTime.UtcNow
        });

        deviationCalc.ProcessSpread(new SpreadData
        {
            Exchange = "Bybit",
            Symbol = "BTC_USDT",
            BestBid = 50100m, // Only +0.20% deviation
            BestAsk = 50200m,
            SpreadPercentage = 0.20m,
            Timestamp = DateTime.UtcNow
        });

        // Assert - No signal should be generated
        Assert.Null(executedSignal);
    }

    [Fact]
    public void EndToEnd_Cooldown_PreventsDuplicateExecution()
    {
        // Arrange
        var deviationCalc = new DeviationCalculator(minDeviationThreshold: 0.10m);
        var signalDetector = new SignalDetector(
            entryThreshold: 0.35m,
            exitThreshold: 0.05m,
            signalCooldown: TimeSpan.FromSeconds(10)
        );
        var tradeExecutor = CreateExecutor();

        deviationCalc.OnDeviationDetected += signalDetector.ProcessDeviation;
        
        int executionCount = 0;
        signalDetector.OnEntrySignal += async signal =>
        {
            executionCount++;
            await tradeExecutor.ExecuteEntryAsync(signal);
        };

        var gateSpread = new SpreadData
        {
            Exchange = "Gate",
            Symbol = "BTC_USDT",
            BestBid = 50000m,
            BestAsk = 50100m,
            SpreadPercentage = 0.20m,
            Timestamp = DateTime.UtcNow
        };

        var bybitSpread = new SpreadData
        {
            Exchange = "Bybit",
            Symbol = "BTC_USDT",
            BestBid = 50200m,
            BestAsk = 50300m,
            SpreadPercentage = 0.20m,
            Timestamp = DateTime.UtcNow
        };

        // Act - Send same deviation twice (should be blocked by cooldown)
        deviationCalc.ProcessSpread(gateSpread);
        deviationCalc.ProcessSpread(bybitSpread);
        
        // Second execution attempt (should be ignored due to active signal)
        deviationCalc.ProcessSpread(bybitSpread);

        // Assert - Only 1 execution (cooldown blocks second)
        Assert.Equal(1, executionCount);
    }
}
