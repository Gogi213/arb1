using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace SpreadAggregator.Application.Helpers;

/// <summary>
/// Thread-safe LRU (Least Recently Used) cache with automatic eviction.
/// PROPOSAL-2025-0095: Memory safety - prevents unbounded collection growth.
/// </summary>
/// <typeparam name="TKey">Key type</typeparam>
/// <typeparam name="TValue">Value type</typeparam>
public class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _maxSize;
    private readonly ConcurrentDictionary<TKey, CacheEntry> _cache;
    private readonly object _evictionLock = new();
    private long _currentTick = 0;

    public LruCache(int maxSize)
    {
        if (maxSize <= 0)
            throw new ArgumentException("Max size must be positive", nameof(maxSize));

        _maxSize = maxSize;
        _cache = new ConcurrentDictionary<TKey, CacheEntry>();
    }

    public int Count => _cache.Count;
    public int MaxSize => _maxSize;

    /// <summary>
    /// Get value by key, updating access time (LRU)
    /// </summary>
    public bool TryGetValue(TKey key, out TValue? value)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            // Update access time (LRU tracking)
            entry.LastAccessTick = Interlocked.Increment(ref _currentTick);
            value = entry.Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Add or update value, evicting oldest entries if capacity exceeded
    /// </summary>
    public void AddOrUpdate(TKey key, TValue value)
    {
        var tick = Interlocked.Increment(ref _currentTick);
        var entry = new CacheEntry { Value = value, LastAccessTick = tick };

        _cache.AddOrUpdate(key, entry, (k, old) =>
        {
            // Update existing entry
            old.Value = value;
            old.LastAccessTick = tick;
            return old;
        });

        // Check if eviction needed (single-threaded eviction)
        if (_cache.Count > _maxSize)
        {
            EvictOldest();
        }
    }

    /// <summary>
    /// Remove specific key
    /// </summary>
    public bool TryRemove(TKey key, out TValue? value)
    {
        if (_cache.TryRemove(key, out var entry))
        {
            value = entry.Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Get all keys (snapshot)
    /// </summary>
    public IEnumerable<TKey> Keys => _cache.Keys;

    /// <summary>
    /// Get all values (snapshot)
    /// </summary>
    public IEnumerable<TValue> Values => _cache.Values.Select(e => e.Value);

    /// <summary>
    /// Clear all entries
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Evict entries matching predicate
    /// </summary>
    public int EvictWhere(Func<TKey, TValue, bool> predicate)
    {
        var toRemove = _cache
            .Where(kvp => predicate(kvp.Key, kvp.Value.Value))
            .Select(kvp => kvp.Key)
            .ToList();

        var removed = 0;
        foreach (var key in toRemove)
        {
            if (_cache.TryRemove(key, out _))
                removed++;
        }

        return removed;
    }

    /// <summary>
    /// Evict oldest 10% of entries when capacity exceeded
    /// Single-threaded to prevent concurrent evictions
    /// </summary>
    private void EvictOldest()
    {
        lock (_evictionLock)
        {
            // Re-check count after acquiring lock
            if (_cache.Count <= _maxSize)
                return;

            // Evict 10% of oldest entries
            var evictCount = Math.Max(1, _maxSize / 10);

            var toEvict = _cache
                .OrderBy(kvp => kvp.Value.LastAccessTick)
                .Take(evictCount)
                .Select(kvp => kvp.Key)
                .ToList();

            var evicted = 0;
            foreach (var key in toEvict)
            {
                if (_cache.TryRemove(key, out _))
                    evicted++;
            }

            // Log eviction event
            if (evicted > 0)
            {
                Console.WriteLine($"[LruCache] Evicted {evicted} oldest entries (capacity: {_cache.Count}/{_maxSize})");
            }
        }
    }

    private class CacheEntry
    {
        public TValue Value { get; set; } = default!;
        public long LastAccessTick { get; set; }
    }
}
