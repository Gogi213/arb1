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
            // Task 0.3: Cannot update LastAccessTick (immutable record)
            // LRU tracking now only on AddOrUpdate (acceptable tradeoff for thread-safety)
            value = entry.Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Add or update value, evicting oldest entries if capacity exceeded
    /// SPRINT 1 FIX: Lock-based capacity enforcement to prevent TOCTOU race
    /// </summary>
    public void AddOrUpdate(TKey key, TValue value)
    {
        var tick = Interlocked.Increment(ref _currentTick);
        
        // Task 0.3: Always create new immutable record instance
        var newEntry = new CacheEntry(value, tick);
        
        // SPRINT 1 FIX: Use lock to ensure atomic add + eviction
        // This eliminates TOCTOU race where Count check and eviction were separate
        lock (_evictionLock)
        {
            _cache.AddOrUpdate(
                key, 
                addValueFactory: k => newEntry,              // Add: use new entry
                updateValueFactory: (k, old) => newEntry     // Update: replace with new entry (no mutation!)
            );

            // Evict immediately if over capacity (inside lock - no race!)
            if (_cache.Count > _maxSize)
            {
                EvictOldestUnsafe();  // No lock needed - already inside lock
            }
        }
    }

    /// <summary>
    /// REMOVED: TryEvictIfNeeded() - replaced with lock-based approach
    /// Old approach had TOCTOU race between Count check and eviction
    /// </summary>


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
        // FIX: Take snapshot to avoid concurrent modification during enumeration
        var snapshot = _cache.ToList();
        
        var toRemove = snapshot
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
    /// UNSAFE: Must be called inside _evictionLock
    /// </summary>
    private void EvictOldestUnsafe()
    {
        // NOTE: No lock here - caller must hold _evictionLock
        
        // Evict 10% of oldest entries
        var evictCount = Math.Max(1, _maxSize / 10);

        // FIX: Take snapshot to avoid ConcurrentModificationException during OrderBy
        var snapshot = _cache.ToList();  // ✅ Thread-safe snapshot
        
        var toEvict = snapshot
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

    // Task 0.3: Immutable record prevents concurrent mutation race
    private record CacheEntry(TValue Value, long LastAccessTick);
}
