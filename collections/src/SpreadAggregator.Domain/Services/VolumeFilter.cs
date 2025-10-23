namespace SpreadAggregator.Domain.Services;

/// <summary>
/// Filters trading pairs based on their 24-hour trading volume.
/// </summary>
public class VolumeFilter
{
    /// <summary>
    /// Checks if the provided volume is within the configured range for a specific exchange.
    /// </summary>
    /// <param name="volume">The 24-hour volume of a trading pair.</param>
    /// <param name="minVolume">The minimum allowed 24-hour volume in USD.</param>
    /// <param name="maxVolume">The maximum allowed 24-hour volume in USD.</param>
    /// <returns><c>true</c> if the volume is sufficient; otherwise, <c>false</c>.</returns>
    public bool IsVolumeSufficient(decimal volume, decimal minVolume, decimal maxVolume)
    {
        return volume >= minVolume && volume <= maxVolume;
    }
}