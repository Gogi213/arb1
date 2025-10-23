using SpreadAggregator.Domain.Services;
using Xunit;

namespace SpreadAggregator.Tests.Domain.Services;

public class VolumeFilterTests
{
    private readonly VolumeFilter _filter = new();

    [Theory]
    [InlineData(500, 100, 1000, true)]  // Volume within range
    [InlineData(100, 100, 1000, true)]  // Volume at min edge
    [InlineData(1000, 100, 1000, true)] // Volume at max edge
    [InlineData(50, 100, 1000, false)]   // Volume below range
    [InlineData(1500, 100, 1000, false)] // Volume above range
    public void IsVolumeSufficient_ShouldReturnExpectedResult(decimal volume, decimal min, decimal max, bool expected)
    {
        // Act
        var result = _filter.IsVolumeSufficient(volume, min, max);

        // Assert
        Assert.Equal(expected, result);
    }
}