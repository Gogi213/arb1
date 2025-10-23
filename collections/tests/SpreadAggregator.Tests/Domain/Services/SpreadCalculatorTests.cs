using SpreadAggregator.Domain.Services;
using Xunit;

namespace SpreadAggregator.Tests.Domain.Services;

public class SpreadCalculatorTests
{
    [Theory]
    [InlineData(100, 101, 0.9900990099009901)]
    [InlineData(99.5, 100, 0.5)]
    [InlineData(0.123, 0.125, 1.6)]
    public void Calculate_ShouldReturnCorrectSpreadPercentage(decimal bestBid, decimal bestAsk, double expectedSpread)
    {
        // Arrange
        var spreadCalculator = new SpreadCalculator();

        // Act
        var spread = spreadCalculator.Calculate(bestBid, bestAsk);

        // Assert
        Assert.Equal((decimal)expectedSpread, spread, 15);
    }

    [Fact]
    public void Calculate_WhenAskIsZero_ShouldThrowDivideByZeroException()
    {
        // Arrange
        var spreadCalculator = new SpreadCalculator();

        // Act & Assert
        Assert.Throws<DivideByZeroException>(() => spreadCalculator.Calculate(100, 0));
    }
}