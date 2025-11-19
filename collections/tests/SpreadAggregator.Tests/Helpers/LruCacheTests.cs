using SpreadAggregator.Application.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SpreadAggregator.Tests.Helpers;

/// <summary>
/// Tests for LruCache, focusing on concurrency issues
/// </summary>
public class LruCacheTests
{
    [Fact]
    public void AddOrUpdate_BasicFunctionality_Works()
    {
        // Arrange
        var cache = new LruCache<string, int>(10);

        // Act
        cache.AddOrUpdate("key1", 100);
        var found = cache.TryGetValue("key1", out var value);

        // Assert
        Assert.True(found);
        Assert.Equal(100, value);
    }

    [Fact]
    public void AddOrUpdate_UpdateExisting_UpdatesValue()
    {
        // Arrange
        var cache = new LruCache<string, int>(10);
        cache.AddOrUpdate("key1", 100);

        // Act
        cache.AddOrUpdate("key1", 200);
        cache.TryGetValue("key1", out var value);

        // Assert
        Assert.Equal(200, value);
    }

    [Fact]
    public void Eviction_WhenOverCapacity_EvictsOldest()
    {
        // Arrange
        var cache = new LruCache<string, int>(5);
        
        // Add 6 items (triggers eviction)
        for (int i = 0; i < 6; i++)
        {
            cache.AddOrUpdate($"key{i}", i);
            Thread.Sleep(10); // Ensure different access times
        }

        // Act
        var oldestExists = cache.TryGetValue("key0", out _);
        var newestExists = cache.TryGetValue("key5", out _);

        // Assert
        Assert.False(oldestExists, "Oldest item should be evicted");
        Assert.True(newestExists, "Newest item should exist");
    }

    /// <summary>
    /// ISSUE #10: Test for data race in AddOrUpdate
    /// This test tries to reproduce the scenario where one thread reads
    /// while another thread mutates the CacheEntry
    /// </summary>
    [Fact]
    public void AddOrUpdate_ConcurrentReadWrite_NoDataCorruption()
    {
        // Arrange
        var cache = new LruCache<string, TestObject>(100);
        var errors = new List<string>();
        var errorsLock = new object();
        var iterations = 10000;

        cache.AddOrUpdate("shared", new TestObject { Value = 0, Tag = "initial" });

        // Act: Concurrent read and write
        var writer = Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                cache.AddOrUpdate("shared", new TestObject { Value = i, Tag = $"write-{i}" });
            }
        });

        var reader = Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                if (cache.TryGetValue("shared", out var obj))
                {
                    // ❌ BUG: If old CacheEntry is mutated, we might see:
                    // - obj.Value = 5 but obj.Tag = "write-10" (inconsistent state)
                    // - or obj = null (partial write)
                    
                    if (obj == null)
                    {
                        lock (errorsLock)
                        {
                            errors.Add($"Iteration {i}: Read null object!");
                        }
                    }
                    else if (obj.Tag != null && !obj.Tag.EndsWith(obj.Value.ToString()))
                    {
                        lock (errorsLock)
                        {
                            errors.Add($"Iteration {i}: Inconsistent state! Value={obj.Value}, Tag={obj.Tag}");
                        }
                    }
                }
            }
        });

        Task.WaitAll(writer, reader);

        // Assert
        Assert.Empty(errors); // Should be no data corruption
    }

    /// <summary>
    /// ISSUE #10: Test for TOCTOU in eviction check
    /// Multiple threads adding simultaneously should not exceed maxSize significantly
    /// </summary>
    [Fact]
    public void AddOrUpdate_ConcurrentAdds_RespectsMaxSize()
    {
        // Arrange
        var maxSize = 100;
        var cache = new LruCache<string, int>(maxSize);
        var threadCount = 10;
        var itemsPerThread = 50;

        // Act: 10 threads each adding 50 items concurrently
        var tasks = Enumerable.Range(0, threadCount).Select(threadId =>
            Task.Run(() =>
            {
                for (int i = 0; i < itemsPerThread; i++)
                {
                    cache.AddOrUpdate($"thread{threadId}-key{i}", i);
                }
            })
        ).ToArray();

        Task.WaitAll(tasks);

        // Assert
        // Due to TOCTOU bug, cache.Count might be >> maxSize
        // After fix, it should be close to maxSize (allowing for 10% overhead due to eviction strategy)
        Assert.True(cache.Count <= maxSize * 1.2, 
            $"Cache size {cache.Count} exceeds maxSize {maxSize} by more than 20%");
    }

    /// <summary>
    /// Stress test: Concurrent AddOrUpdate and TryGetValue from multiple threads
    /// </summary>
    [Fact]
    public void LruCache_StressTest_NoExceptions()
    {
        // Arrange
        var cache = new LruCache<int, string>(1000);
        var duration = TimeSpan.FromSeconds(5);
        var cts = new CancellationTokenSource(duration);
        var exceptions = new List<Exception>();
        var exceptionsLock = new object();

        // Act: Spawn many reader and writer threads
        var tasks = new List<Task>();

        // 5 writer threads
        for (int t = 0; t < 5; t++)
        {
            var threadId = t;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    var random = new Random(threadId);
                    while (!cts.Token.IsCancellationRequested)
                    {
                        var key = random.Next(0, 2000);
                        cache.AddOrUpdate(key, $"value-{key}-{DateTime.Now.Ticks}");
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptionsLock)
                    {
                        exceptions.Add(ex);
                    }
                }
            }));
        }

        // 5 reader threads
        for (int t = 0; t < 5; t++)
        {
            var threadId = t;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    var random = new Random(threadId + 1000);
                    while (!cts.Token.IsCancellationRequested)
                    {
                        var key = random.Next(0, 2000);
                        cache.TryGetValue(key, out var _);
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptionsLock)
                    {
                        exceptions.Add(ex);
                    }
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        if (exceptions.Any())
        {
            throw new AggregateException("Stress test failed with exceptions", exceptions);
        }
    }

    /// <summary>
    /// Test object with multiple fields to detect partial writes
    /// </summary>
    private class TestObject
    {
        public int Value { get; set; }
        public string? Tag { get; set; }
    }
}
