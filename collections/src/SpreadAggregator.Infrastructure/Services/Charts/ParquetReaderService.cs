using Microsoft.Data.Analysis;
using Microsoft.Extensions.Logging;
using Parquet;
using Parquet.Data;
using System.IO;
using System.Linq;

namespace SpreadAggregator.Infrastructure.Services.Charts;

/// <summary>
/// Service for reading and processing parquet files for chart data
/// Replaces Python charts/server.py functionality
/// </summary>
public class ParquetReaderService
{
    private readonly string _dataLakePath;
    private readonly ILogger<ParquetReaderService> _logger;

    public ParquetReaderService(string dataLakePath, ILogger<ParquetReaderService> logger)
    {
        _dataLakePath = dataLakePath ?? throw new ArgumentNullException(nameof(dataLakePath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Load parquet data for a specific exchange and symbol
    /// </summary>
    public async Task<(List<DateTime> timestamps, List<decimal> bids)?> LoadExchangeDataAsync(string exchange, string symbol)
    {
        // IMPORTANT: Must match OrchestrationService symbol normalization logic
        // Symbol is already normalized to SYMBOL_USDT format (e.g., VIRTUAL_USDT)
        // Also try legacy formats for backward compatibility
        var symbolFormats = new[] {
            symbol,                                                        // VIRTUAL_USDT (current normalized format)
            symbol.Replace("_", ""),                                       // VIRTUALUSDT (old format from Bybit/Binance)
            symbol.Replace("/", "#")                                       // VIRTUAL#USDT (legacy format)
        };

        string? symbolPath = null;
        foreach (var fmt in symbolFormats)
        {
            var candidate = Path.Combine(_dataLakePath, $"exchange={exchange}", $"symbol={fmt}");
            if (Directory.Exists(candidate))
            {
                symbolPath = candidate;
                break;
            }
        }

        if (symbolPath == null)
        {
            _logger.LogWarning($"Symbol path not found for {exchange}/{symbol} (tried formats: {string.Join(", ", symbolFormats)})");
            return null;
        }

        // Scan all parquet files
        var files = Directory.GetFiles(symbolPath, "*.parquet", SearchOption.AllDirectories);

        if (files.Length == 0)
        {
            _logger.LogWarning($"No parquet files found for {exchange}/{symbol}");
            return null;
        }

        var allTimestamps = new List<DateTime>();
        var allBids = new List<decimal>();

        // Read all parquet files
        foreach (var file in files)
        {
            using var stream = File.OpenRead(file);
            using var reader = await ParquetReader.CreateAsync(stream);

            // Get schema
            var schema = reader.Schema;
            var tsField = schema.DataFields.FirstOrDefault(f => f.Name.ToLower().Contains("time"));
            var bidField = schema.DataFields.FirstOrDefault(f => f.Name.ToLower().Contains("bestbid"));

            if (tsField == null || bidField == null)
                continue;

            for (int i = 0; i < reader.RowGroupCount; i++)
            {
                using var rowGroupReader = reader.OpenRowGroupReader(i);

                // Read columns
                var tsColumn = await rowGroupReader.ReadColumnAsync(tsField);
                var bidColumn = await rowGroupReader.ReadColumnAsync(bidField);

                var timestamps = (DateTime[])tsColumn.Data;
                var bids = bidColumn.Data;

                for (int j = 0; j < timestamps.Length; j++)
                {
                    allTimestamps.Add(timestamps[j]);

                    // Convert bid to decimal
                    var bidValue = bids.GetValue(j);
                    if (bidValue != null)
                    {
                        allBids.Add(Convert.ToDecimal(bidValue));
                    }
                    else
                    {
                        allBids.Add(0);
                    }
                }
            }
        }

        // Sort by timestamp
        var sorted = allTimestamps
            .Zip(allBids, (ts, bid) => new { Timestamp = ts, Bid = bid })
            .OrderBy(x => x.Timestamp)
            .ToList();

        return (sorted.Select(x => x.Timestamp).ToList(), sorted.Select(x => x.Bid).ToList());
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
        _logger.LogInformation($"Processing pair: {symbol} ({exchange1} vs {exchange2})");

        // Load data for both exchanges
        var data1 = await LoadExchangeDataAsync(exchange1, symbol);
        var data2 = await LoadExchangeDataAsync(exchange2, symbol);

        if (data1 == null || data2 == null)
        {
            _logger.LogWarning($"Missing data for {symbol} ({exchange1}/{exchange2})");
            return null;
        }

        // AsOf join with backward strategy, 50ms tolerance for HFT
        var joined = AsOfJoin(data1.Value.timestamps, data1.Value.bids,
                              data2.Value.timestamps, data2.Value.bids,
                              TimeSpan.FromMilliseconds(50));

        if (joined.Count == 0)
        {
            _logger.LogWarning($"AsOf join resulted in empty dataset for {symbol}");
            return null;
        }

        // Calculate spread: (bid_a / bid_b - 1) * 100
        var spreads = joined.Select(x =>
        {
            if (x.bid2 == 0) return (double?)null;
            return (double)(((x.bid1 / x.bid2) - 1) * 100);
        }).ToList();

        // Calculate rolling quantiles (window size 200)
        var upperBands = CalculateRollingQuantile(spreads, 0.97, 200);
        var lowerBands = CalculateRollingQuantile(spreads, 0.03, 200);

        // Convert to epoch timestamps
        var epochTimestamps = joined.Select(x =>
            ((DateTimeOffset)x.timestamp).ToUnixTimeSeconds()
        ).Select(x => (double)x).ToList();

        return new ChartData
        {
            Symbol = symbol,
            Exchange1 = exchange1,
            Exchange2 = exchange2,
            Timestamps = epochTimestamps,
            Spreads = spreads,
            UpperBand = upperBands,
            LowerBand = lowerBands
        };
    }

    /// <summary>
    /// AsOf join - найти ближайшее значение из второго списка для каждого timestamp из первого
    /// HFT-оптимизация: бинарный поиск O(log n) вместо линейного O(n)
    /// Tracks detailed loss metrics for HFT optimization
    /// </summary>
    private List<(DateTime timestamp, decimal bid1, decimal bid2)> AsOfJoin(
        List<DateTime> ts1, List<decimal> bids1,
        List<DateTime> ts2, List<decimal> bids2,
        TimeSpan tolerance)
    {
        var result = new List<(DateTime, decimal, decimal)>();

        // HFT Metrics: track losses
        int noBackwardMatch = 0;
        int outsideTolerance = 0;
        var delays = new List<double>(); // Track actual delays in ms

        for (int i = 0; i < ts1.Count; i++)
        {
            var targetTs = ts1[i];

            // Binary search for largest timestamp <= targetTs in ts2
            int idx = BinarySearchFloor(ts2, targetTs);

            if (idx == -1)
            {
                noBackwardMatch++;
                continue; // No backward match
            }

            var matchTs = ts2[idx];
            var delay = (targetTs - matchTs).TotalMilliseconds;

            if ((targetTs - matchTs) > tolerance)
            {
                outsideTolerance++;
                continue; // Outside tolerance
            }

            delays.Add(delay);
            result.Add((targetTs, bids1[i], bids2[idx]));
        }

        // Log detailed statistics for HFT monitoring
        if (ts1.Count > 0)
        {
            var successRate = (result.Count * 100.0) / ts1.Count;
            var lossRate = 100.0 - successRate;

            var avgDelay = delays.Any() ? delays.Average() : 0;
            var maxDelay = delays.Any() ? delays.Max() : 0;

            _logger.LogInformation(
                $"[HFT AsOfJoin Historical] Tolerance={tolerance.TotalMilliseconds}ms | " +
                $"Input={ts1.Count}x{ts2.Count} | " +
                $"Joined={result.Count} ({successRate:F1}%) | " +
                $"Lost={ts1.Count - result.Count} ({lossRate:F1}%): " +
                $"NoMatch={noBackwardMatch}, OutsideTolerance={outsideTolerance} | " +
                $"AvgDelay={avgDelay:F2}ms, MaxDelay={maxDelay:F2}ms"
            );
        }

        return result;
    }

    /// <summary>
    /// Binary search: найти наибольший индекс где sorted[idx] <= target
    /// Для HFT: O(log n) вместо O(n) linear scan
    /// </summary>
    private int BinarySearchFloor(List<DateTime> sorted, DateTime target)
    {
        int left = 0, right = sorted.Count - 1;
        int result = -1;

        while (left <= right)
        {
            int mid = left + (right - left) / 2;

            if (sorted[mid] <= target)
            {
                result = mid;
                left = mid + 1;
            }
            else
            {
                right = mid - 1;
            }
        }

        return result;
    }

    /// <summary>
    /// Calculate rolling quantile (percentile)
    /// </summary>
    private List<double?> CalculateRollingQuantile(List<double?> values, double quantile, int windowSize)
    {
        var result = new List<double?>();

        for (int i = 0; i < values.Count; i++)
        {
            var start = Math.Max(0, i - windowSize + 1);
            var window = values.Skip(start).Take(i - start + 1)
                .Where(v => v.HasValue && !double.IsNaN(v.Value) && !double.IsInfinity(v.Value))
                .Select(v => v!.Value)
                .OrderBy(v => v)
                .ToList();

            if (window.Count == 0)
            {
                result.Add(null);
                continue;
            }

            var index = (int)Math.Ceiling(window.Count * quantile) - 1;
            index = Math.Max(0, Math.Min(index, window.Count - 1));
            result.Add(window[index]);
        }

        return result;
    }
}

/// <summary>
/// Chart data model
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
