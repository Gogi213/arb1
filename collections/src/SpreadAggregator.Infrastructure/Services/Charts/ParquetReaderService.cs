using Microsoft.Data.Analysis;
using Parquet;
using SpreadAggregator.Application.Abstractions;

namespace SpreadAggregator.Infrastructure.Services.Charts;

/// <summary>
/// Service for reading and processing parquet files for chart data
/// Replaces Python charts/server.py functionality
/// </summary>
public class ParquetReaderService
{
    private readonly string _dataLakePath;

    public ParquetReaderService(string dataLakePath)
    {
        _dataLakePath = dataLakePath ?? throw new ArgumentNullException(nameof(dataLakePath));
    }

    /// <summary>
    /// Load parquet data for a specific exchange and symbol
    /// </summary>
    public async Task<DataFrame?> LoadExchangeDataAsync(string exchange, string symbol)
    {
        // TODO Sprint 2: Implement parquet loading
        // 1. Convert symbol format (BTC/USDT -> BTC#USDT)
        // 2. Scan directory for parquet files
        // 3. Load and combine into DataFrame

        await Task.CompletedTask;
        return null;
    }

    /// <summary>
    /// Load and process pair data for chart display
    /// Equivalent to Python load_and_process_pair()
    /// </summary>
    public async Task<ChartData?> LoadAndProcessPairAsync(
        string symbol,
        string exchange1,
        string exchange2)
    {
        // TODO Sprint 2: Implement full processing pipeline
        // 1. Load data for both exchanges
        // 2. AsOf join (backward strategy, 2s tolerance)
        // 3. Calculate spread
        // 4. Calculate rolling quantiles (97%, 3%)
        // 5. Clean invalid values (NaN, Inf)
        // 6. Convert to epoch timestamps

        await Task.CompletedTask;
        return null;
    }
}

/// <summary>
/// Chart data model (temporary, will be moved to proper location)
/// </summary>
public class ChartData
{
    public required string Symbol { get; set; }
    public required string Exchange1 { get; set; }
    public required string Exchange2 { get; set; }
    public required List<double> Timestamps { get; set; }
    public required List<double?> Spreads { get; set; }
    public required List<double?> UpperBand { get; set; }
    public required List<double?> LowerBand { get; set; }
}
