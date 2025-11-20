using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace SpreadAggregator.Tests.Infrastructure;

/// <summary>
/// SPRINT 1 TDD: Tests for ParquetDataWriter buffer race condition
/// </summary>
public class ParquetDataWriterTests
{
    private readonly string _testDataRoot = Path.Combine(Path.GetTempPath(), "parquet_test_" + Guid.NewGuid());

    public ParquetDataWriterTests()
    {
        if (Directory.Exists(_testDataRoot))
        {
            Directory.Delete(_testDataRoot, true);
        }
        Directory.CreateDirectory(_testDataRoot);
    }

    /// <summary>
    /// SPRINT 1 TDD: Reproduce buffer corruption race condition
    /// 
    /// Bug description:
    /// In InitializeCollectorAsync(), the code does:
    ///   lock (_bufferLock) {
    ///       buffer.Add(data);
    ///       if (buffer.Count >= batchSize) {
    ///           _ = FlushSpreadBufferAsync(filePath, buffer); // ❌ Passes REFERENCE to buffer
    ///       }
    ///   }
    /// 
    /// The background flush task mutates buffer (buffer.Clear()) while main thread
    /// continues adding to it. This causes:
    /// 1. Data loss (items added after flush starts but before Clear)
    /// 2. Duplicates (items added after Clear but written to next flush)
    /// 3. Possible exceptions (collection modified during enumeration in WriteSpreadsAsync)
    /// 
    /// Expected: FAILS before fix (data count mismatch or exception)
    /// Expected: PASSES after fix (all items written exactly once)
    /// </summary>
    [Fact]
    public async Task SPRINT1_TDD_ConcurrentWrites_NoDataCorruption()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Recording:DataRootPath"] = _testDataRoot,
                ["Recording:BatchSize"] = "100"  // Small batch to trigger frequent flushes
            })
            .Build();

        var channel = Channel.CreateUnbounded<MarketData>();
        var writer = new ParquetDataWriter(channel, config);

        var totalItems = 1000;
        var cts = new CancellationTokenSource();

        // Act: Start collector
        var collectorTask = writer.InitializeCollectorAsync(cts.Token);

        // Publish data rapidly (race condition!)
        for (int i = 0; i < totalItems; i++)
        {
            var spread = new SpreadData
            {
                Timestamp = DateTime.UtcNow,
                Exchange = "Bybit",
                Symbol = "BTC_USDT",
                BestBid = 50000m + i,
                BestAsk = 50001m + i,
                SpreadPercentage = 0.002m,
                MinVolume = 1.0m,
                MaxVolume = 10.0m
            };
            await channel.Writer.WriteAsync(spread);
        }

        // Stop collector
        channel.Writer.Complete();
        await collectorTask;

        // IMPORTANT: Wait for background flush tasks to complete
        await Task.Delay(2000);

        // Assert: Count all written records
        var allFiles = Directory.GetFiles(_testDataRoot, "*.parquet", SearchOption.AllDirectories);
        var totalWritten = 0;

        foreach (var file in allFiles)
        {
            var data = await writer.ReadAsync(file);
            totalWritten += data.Count;
        }

        // Before fix: This will likely FAIL (totalWritten != totalItems)
        // Possible outcomes:
        // - totalWritten < 1000 (data loss)
        // - totalWritten > 1000 (duplicates)
        // - Exception during flush (collection modified)
        Assert.Equal(totalItems, totalWritten);
    }

    /// <summary>
    /// SPRINT 1 TDD: Verify no duplicates across flushes
    /// </summary>
    [Fact]
    public async Task SPRINT1_TDD_ConcurrentWrites_NoDuplicates()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Recording:DataRootPath"] = _testDataRoot,
                ["Recording:BatchSize"] = "50"
            })
            .Build();

        var channel = Channel.CreateUnbounded<MarketData>();
        var writer = new ParquetDataWriter(channel, config);

        var totalItems = 500;
        var cts = new CancellationTokenSource();

        // Act
        var collectorTask = writer.InitializeCollectorAsync(cts.Token);

        for (int i = 0; i < totalItems; i++)
        {
            var spread = new SpreadData
            {
                Timestamp = DateTime.UtcNow.AddMilliseconds(i),  // Unique timestamp as ID
                Exchange = "Gate",
                Symbol = "ETH_USDT",
                BestBid = 3000m + i,
                BestAsk = 3001m + i,
                SpreadPercentage = 0.003m,
                MinVolume = 1.0m,
                MaxVolume = 10.0m
            };
            await channel.Writer.WriteAsync(spread);
        }

        channel.Writer.Complete();
        await collectorTask;

        // Wait for background tasks
        await Task.Delay(2000);

        // Assert: No duplicate timestamps
        var allFiles = Directory.GetFiles(_testDataRoot, "*.parquet", SearchOption.AllDirectories);
        var allTimestamps = new List<DateTime>();

        foreach (var file in allFiles)
        {
            var data = await writer.ReadAsync(file);
            allTimestamps.AddRange(data.Select(d => d.Timestamp));
        }

        var uniqueTimestamps = allTimestamps.Distinct().Count();
        
        // Before fix: Possible duplicates due to race
        // After fix: All timestamps unique
        Assert.Equal(allTimestamps.Count, uniqueTimestamps);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataRoot))
        {
            try
            {
                Directory.Delete(_testDataRoot, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
