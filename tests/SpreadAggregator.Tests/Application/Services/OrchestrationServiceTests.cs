using Moq;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Application.Services;
using SpreadAggregator.Domain.Entities;
using System.Text.Json;
using System.Threading.Channels;
using SpreadAggregator.Domain.Services;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace SpreadAggregator.Tests.Application.Services;

public class OrchestrationServiceTests
{
    [Fact]
    public async Task ProcessExchange_Should_SetTimestamp()
    {
        // Arrange
        var mockWebSocketServer = new Mock<IWebSocketServer>();
        var spreadCalculator = new SpreadCalculator();
        var volumeFilter = new VolumeFilter();
        var mockConfiguration = new Mock<IConfiguration>();
        var mockExchangeClient = new Mock<IExchangeClient>();
        var channel = Channel.CreateUnbounded<SpreadData>();

        var exchangeName = "TestExchange";

        var tickers = new List<TickerData>
        {
            new TickerData { Symbol = "BTCUSDT", QuoteVolume = 2000000 }
        };

        mockExchangeClient.Setup(c => c.ExchangeName).Returns(exchangeName);
        mockExchangeClient.Setup(c => c.GetTickersAsync()).ReturnsAsync(tickers);
        mockExchangeClient.Setup(c => c.SubscribeToTickersAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<Action<SpreadData>>()))
            .Callback<IEnumerable<string>, Action<SpreadData>>((s, onData) =>
            {
                var spread = new SpreadData
                {
                    Exchange = exchangeName,
                    Symbol = "BTCUSDT",
                    BestBid = 50000,
                    BestAsk = 50001,
                    // Timestamp is not set here to ensure OrchestrationService sets it
                };
                onData(spread);
            });

        var configSection = new Mock<IConfigurationSection>();
        configSection.Setup(a => a.Key).Returns(exchangeName);
        var configSections = new List<IConfigurationSection> { configSection.Object };
        var mockConfig = new Mock<IConfigurationSection>();
        mockConfig.Setup(a => a.GetChildren()).Returns(configSections);
        mockConfiguration.Setup(c => c.GetSection("ExchangeSettings:Exchanges")).Returns(mockConfig.Object);

        string capturedMessage = null;
        mockWebSocketServer
            .Setup(ws => ws.BroadcastRealtimeAsync(It.IsAny<string>()))
            .Callback<string>(m => capturedMessage = m)
            .Returns(Task.CompletedTask);

        var orchestrationService = new OrchestrationService(
            mockWebSocketServer.Object,
            spreadCalculator,
            mockConfiguration.Object,
            volumeFilter,
            new[] { mockExchangeClient.Object },
            channel
        );

        // Act
        await orchestrationService.StartAsync();

        // Assert
        mockWebSocketServer.Verify(ws => ws.BroadcastRealtimeAsync(It.IsAny<string>()), Times.Once);

        Assert.NotNull(capturedMessage);
        var capturedData = JsonSerializer.Deserialize<SpreadData>(capturedMessage);
        Assert.NotNull(capturedData);
        Assert.True(capturedData.Timestamp > DateTime.MinValue);
    }
}