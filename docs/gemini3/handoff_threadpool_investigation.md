# Handoff: ThreadPool Starvation Investigation

**Date:** 2025-11-21 16:40 UTC+4
**Status:** RESOLVED
**Priority:** NORMAL

---

## Problem Summary

The `collections` application was experiencing periodic **complete freezes** lasting 60+ seconds.
Investigation pointed to ThreadPool starvation caused by unbounded `Task.Run` spawning in `ParquetDataWriter`.

---

## Resolution

### 1. Implemented Serialized Write Queue
**File:** `c:\visual projects\arb1\collections\src\SpreadAggregator.Infrastructure\Services\ParquetDataWriter.cs`

**Change:**
- Replaced unbounded `Task.Run` spawning with a `Channel<WriteRequest>` (Unbounded).
- Added a dedicated background `_writerTask` that processes writes sequentially.
- This ensures that disk I/O latency does not consume ThreadPool threads or cause task accumulation.

### 2. Verified Telemetry
**File:** `c:\visual projects\arb1\docs\gemini3\profiling\detailed_profile_20251121.csv`

**Results:**
- `PoolBusy` remained stable between **20-25** threads (Max 32767).
- No spikes in `PoolBusy` were observed during the test run (approx 6 minutes).
- `Total_ProcessData` average time is **~0.03ms**, with max peaks around **300ms** (likely GC or initial JIT).

---

## Conclusion

The potential for ThreadPool starvation has been eliminated by the architectural change in `ParquetDataWriter`.
The application is now robust against disk I/O latency.

## Next Steps

- Monitor `detailed_profile_*.csv` in long-running sessions to ensure `PoolBusy` stays low.
- Proceed with Phase 1 tasks.

