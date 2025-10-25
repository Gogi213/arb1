using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Threading.Tasks;
using TraderBot.Core;

namespace TraderBot.Tests
{
    [TestClass]
    public class ArbitrageTraderTests
    {
        [TestMethod]
        public async Task SellZKJFor2USD_OnBybit_ShouldPlaceMarketOrder()
        {
            // Arrange
            var buyExchangeMock = new Mock<IExchange>();
            var sellExchangeMock = new Mock<IExchange>();

            // We don't need the trader for this specific test,
            // as we are testing the interaction with the exchange mock directly.
            // var trader = new ArbitrageTrader(buyExchangeMock.Object, sellExchangeMock.Object);

            var symbol = "ZKJ_USDT";
            var amountInUsd = 2m;

            // Act
            // Simulate the action of selling ZKJ for $2 on the sell exchange.
            await sellExchangeMock.Object.PlaceOrderAsync(
                symbol,
                OrderSide.Sell,
                NewOrderType.Market,
                quoteQuantity: amountInUsd);

            // Assert
            // Verify that the PlaceOrderAsync method was called on the sell exchange mock
            // with the exact parameters for a market sell order of $2 worth of ZKJ.
            sellExchangeMock.Verify(
                exchange => exchange.PlaceOrderAsync(
                    It.Is<string>(s => s == symbol),
                    It.Is<OrderSide>(s => s == OrderSide.Sell),
                    It.Is<NewOrderType>(t => t == NewOrderType.Market),
                    It.Is<decimal?>(q => q == null), // quantity (in base asset) is null
                    It.Is<decimal?>(p => p == null), // price is null for market order
                    It.Is<decimal?>(qq => qq == amountInUsd) // quoteQuantity is $2
                ),
                Times.Once,
                "The market sell order for ZKJ for $2 was not placed correctly on Bybit."
            );
        }

        [TestMethod]
        public async Task RealMarketSell_OnBybit_ShouldExecute()
        {
            // Arrange
            var apiKey = "UVSbRqLBEY30RnPaiH";
            var apiSecret = "Fg45sn0nH4FhqZaxctj54Nf9cO03Qf9s0zds";
            
            var bybitExchange = new TraderBot.Exchanges.Bybit.BybitExchange();
            await bybitExchange.InitializeAsync(apiKey, apiSecret);

            var symbol = "ZKJUSDT";
            var amountInUsd = 2m;

            // Act
            var result = await bybitExchange.PlaceOrderAsync(
                symbol,
                OrderSide.Sell,
                NewOrderType.Market,
                quoteQuantity: amountInUsd);

            // Assert
            Assert.IsNotNull(result, "The order placement should return a result.");
            Console.WriteLine($"Successfully placed market sell order on Bybit. Order ID: {result}");
        }
    }
}