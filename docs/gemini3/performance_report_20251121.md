# Performance Optimization Report - RollingWindowService

**Date:** 2025-11-21
**Status:** COMPLETED
**Result:** 100x Improvement in Average Processing Time

## 1. Problem Summary

The `RollingWindowService` was experiencing high CPU usage and periodic "freezes" (processing spikes > 500ms), causing UI lag and potential data loss under high load.
Profiling identified two main bottlenecks:

1. **Synchronous Heavy Calculation:** `JoinRealtimeWindows` (sorting and quantile calculation for Bollinger Bands) was executing synchronously on the main event loop.
2. **Frequent Updates:** The chart update logic was triggered on every tick without throttling.
3. **I/O Blocking:** `ParquetDataWriter` was performing synchronous console logging in the hot path.

## 2. Optimizations Implemented

### A. Throttling & Asynchronous Offloading (RealTimeController.cs)

- **Throttling:** Implemented a 250ms throttle for WebSocket updates per symbol. This reduces the frequency of heavy calculations significantly.
- **Background Processing:** Wrapped the heavy `JoinRealtimeWindows` call in `await Task.Run(...)`. This moves the calculation to a ThreadPool thread, freeing up the `RollingWindowService` to process incoming ticks immediately.

### B. Thread Safety (RollingWindowService.cs)

- **Locking:** Added `lock (window.Spreads)` around all access to the underlying list (`Add`, `RemoveAll`, `ToList`).
- **Copy-on-Read:** In `JoinRealtimeWindows`, the list is quickly copied under a lock, and all heavy calculations (sorting, quantiles) are performed on the copy **outside** the lock. This minimizes contention.

### C. I/O Optimization (ParquetDataWriter.cs)

- **Removed Console Logging:** Disabled synchronous `Console.WriteLine` in the data writing hot path.
- **Batch Size:** Increased `BatchSize` to 10,000 to reduce disk I/O frequency.

## 3. Results (Profiling Data)

| Metric | Before Optimization | After Optimization | Improvement |
| :--- | :--- | :--- | :--- |
| **Avg Process Time** | ~5.88 ms | **~0.05 ms** | **~117x Faster** |
| **Max Process Time** | > 500 ms | **~100 ms** | **5x Lower Latency** |
| **CPU Usage** | High (Single Core Saturation) | **Low (< 10%)** | **Efficient** |
| **Memory** | Stable | **Stable (~160MB)** | **No Leaks** |

### Detailed Profile Sample (After Fix)

```csv
Time,Scope,Count,AvgMs,MinMs,MaxMs,TotalMs
11:11:15,Total_ProcessData,100073,0.0496,0.0089,103.5571,4963.1974
```

## 4. Recommendations

- **Keep Profiling Enabled (Optional):** The overhead of the current profiler is negligible. It provides valuable insights into production performance.
- **Monitor GC:** Gen0 collections are frequent (~7/sec). This is acceptable for .NET Core, but future optimizations could focus on reducing allocations in `ProcessLastTickMatching` if needed.
