# ISSUE-002: Real-time Chart Event Storm

**Status:** 🔴 CRITICAL - Application Freeze  
**Created:** 2025-11-21  
**Priority:** P0 (Blocks production deployment)  
**Component:** `RealTimeController`, `RollingWindowService`

---

## Problem Statement

Application **freezes immediately** upon WebSocket connection to real-time charts endpoint (`ws://localhost:5000/ws/realtime_charts`). The freeze occurs within seconds of browser connection and renders the application unresponsive.

### Symptoms

1. ✅ Application starts normally
2. ✅ WebSocket connection establishes successfully
3. ❌ **Immediate freeze** upon chart data streaming
4. ❌ High CPU usage (100% on multiple cores)
5. ❌ UI becomes unresponsive
6. ❌ Requires force-kill to stop

### Metrics (Telemetry)

```
Event handler invocations: 41,800/sec average
Expected rate: ~100/sec (20 charts × 4 updates/sec)
Overhead: 418x unnecessary work
```

---

## Root Cause Analysis

### Architecture Problem: Broadcast Event Pattern

**Current Implementation (BROKEN):**

```csharp
// RollingWindowService.cs
public event EventHandler<WindowDataUpdatedEventArgs>? WindowDataUpdated;

private void OnWindowDataUpdated(string exchange, string symbol)
{
    // BROADCAST to ALL subscribers (20 charts)
    WindowDataUpdated?.Invoke(this, new WindowDataUpdatedEventArgs
    {
        Exchange = exchange,
        Symbol = symbol,
        Timestamp = DateTime.UtcNow
    });
}
```

```csharp
// RealTimeController.cs (×20 times, one per chart)
foreach (var opp in opportunities) // 20 opportunities
{
    EventHandler<WindowDataUpdatedEventArgs> handler = async (sender, e) =>
    {
        // FILTER: Check if this event is relevant to THIS chart
        if ((e.Exchange == opp.Exchange1 || e.Exchange == opp.Exchange2) && e.Symbol == opp.Symbol)
        {
            // Only 2 out of 20 charts pass this check!
            // But ALL 20 charts execute this lambda!
            throttle and process...
        }
    };
    
    _rollingWindow.WindowDataUpdated += handler;  // Subscribe to global event
}
```

### The Math

```
Incoming data rate: ~2,000 events/sec (WebSocket messages)
Number of chart subscribers: 20
Filter checks per event: 20 (one per subscriber)

Total handler invocations = 2,000 × 20 = 40,000/sec
Relevant processing = 2,000 × 2 = 4,000/sec (only ~10% needed!)
Wasted CPU = 36,000 unnecessary filter checks/sec (90% waste!)
```

### Why This Fails

1. **Single global event** broadcasts to ALL subscribers
2. Each subscriber must check `if (e.Exchange == my.Exchange && e.Symbol == my.Symbol)`
3. For a `BTCUSDT@Binance` update:
   - ✅ 2 charts care (Binance↔Bybit, Binance↔GateIo)
   - ❌ 18 charts don't care but still execute lambda
4. With throttling, CPU spends more time checking filters than processing data

---

## Investigation Timeline

### Hypothesis #1: `JoinRealtimeWindows` Performance Bottleneck ❌

**Suspected:** The `OrderBy` and `CalculateRollingQuantile` operations in `JoinRealtimeWindows` were causing slowdown.

**Actions Taken:**
- Removed `OrderBy` call (O(N log N) under lock)
- Removed `CalculateRollingQuantile` calls
- Added performance profiling

**Files Modified:**
- `RollingWindowService.cs:343-377`

**Result:** ❌ **Did NOT fix the freeze**
- Freeze continued even after removing heavy operations
- Profiling showed `JoinRealtimeWindows` was fast (<5ms)
- Root cause was elsewhere

---

### Hypothesis #2: ThreadPool Starvation from Disk I/O ❌

**Suspected:** `ParquetDataWriter` async file writes were blocking ThreadPool threads.

**Actions Taken:**
- Added ThreadPool telemetry
- Checked available worker threads
- Investigated disk I/O patterns
- Reviewed `ParquetDataWriter` fire-and-forget tasks

**Files Reviewed:**
- `ParquetDataWriter.cs`
- Profiling logs

**Result:** ❌ **Not the cause**
- ThreadPool had plenty of available threads
- Disk I/O was non-blocking
- Profiling showed healthy thread availability

---

### Hypothesis #3: Event Storm from Broadcast Architecture ✅

**Discovery:** Telemetry revealed **41,800 event handler invocations/sec** for only 20 chart subscriptions.

**Investigation:**
```csharp
// Added telemetry in RealTimeController:
var eventHandlerInvocations = 0;

handler = async (sender, e) => {
    Interlocked.Increment(ref eventHandlerInvocations);  // Count every invocation
    ...
};

// Every 5 seconds:
var rate = eventHandlerInvocations / elapsed;
// Output: 41,800/sec average
```

**Analysis:**
- Expected: 20 charts × 4 updates/sec = 80 calls/sec
- Actual: 41,800 calls/sec
- **418x overhead!**

**Root Cause Confirmed:**
- Global `WindowDataUpdated` event broadcasts to ALL 20 subscribers
- Each subscriber executes filter lambda: `if ((e.Exchange == opp.Exchange1 ...) && e.Symbol == opp.Symbol)`
- For each event, 18 out of 20 subscribers fail the filter check
- **90% of CPU spent on unnecessary filter checks**

**Result:** ✅ **THIS IS THE ROOT CAUSE**

---

## Attempted Solutions

### Attempt #1: Targeted Events (Incomplete) ⚠️

**Approach:** Replace global event with per-window events using an index.

**Implementation Attempt:**
```csharp
// RollingWindowService.cs - Added:
private readonly ConcurrentDictionary<string, EventHandler<...>> _windowEvents = new();
private readonly ConcurrentDictionary<string, HashSet<string>> _exchangeSymbolIndex = new();

public void SubscribeToWindow(string symbol, string ex1, string ex2, EventHandler<...> handler)
{
    var key = $"{ex1}_{ex2}_{symbol}";
    _windowEvents.AddOrUpdate(key, handler, (k, old) => old + handler);
}

private void OnWindowDataUpdated(string exchange, string symbol)
{
    // Find affected windows
    var indexKey = $"{exchange}_{symbol}";
    if (_exchangeSymbolIndex.TryGetValue(indexKey, out var affectedWindows))
    {
        foreach (var windowKey in affectedWindows)
        {
            _windowEvents[windowKey]?.Invoke(...);  // Only call relevant subscribers!
        }
    }
    
    // Legacy support (should be removed)
    WindowDataUpdated?.Invoke(...);
}
```

**Status:** ⚠️ Started but not completed
- Infrastructure added but not fully integrated
- Legacy `WindowDataUpdated` still being called
- Index not fully populated
- **Rollback required before proper implementation**

---

### Attempt #2: Polling + Dirty Flags (Rejected - Workaround) ❌

**Approach:** Remove events entirely, use polling with dirty flags.

```csharp
// RollingWindowService - dirty flags:
private ConcurrentDictionary<string, bool> _updatedWindows = new();

void AddSpreadPointToWindow(...) {
    _updatedWindows[windowKey] = true;
}

public RealtimeChartData? GetIfUpdated(symbol, ex1, ex2) {
    var key = $"{ex1}_{ex2}_{symbol}";
    if (_updatedWindows.TryRemove(key, out _)) {
        return JoinRealtimeWindows(...);
    }
    return null;
}

// RealTimeController - polling loop:
while (webSocket.Open) {
    await Task.Delay(250);
    var data = _rollingWindow.GetIfUpdated(...);
    if (data != null) send(data);
}
```

**Why Rejected:** This is a **workaround**, not a **solution**
- Polling adds artificial latency (250ms)
- Doesn't fix the architectural problem
- Event-driven is better for real-time systems
- **We should fix the events, not abandon them**

---

## Correct Solution: Targeted Event Delivery

### Architecture Design

Replace **broadcast** pattern with **targeted routing**:

**Before (Broadcast - WRONG):**
```
Event: "BTCUSDT@Binance updated"
  ↓
WindowDataUpdated event (GLOBAL)
  ↓ ↓ ↓ ↓ ↓ ↓ ↓ ↓ ↓ ↓ (broadcast to all 20)
Chart 1: BTC Binance↔Bybit   → ✅ processes
Chart 2: BTC Binance↔GateIo  → ✅ processes
Chart 3: ETH Binance↔Bybit   → ❌ checks filter, rejects
Chart 4: ETH Binance↔GateIo  → ❌ checks filter, rejects
...
Chart 20: SOL GateIo↔Bybit   → ❌ checks filter, rejects

Result: 20 lambda calls, 18 wasted
```

**After (Targeted - CORRECT):**
```
Event: "BTCUSDT@Binance updated"
  ↓
Find affected windows using index:
  - Index["Binance_BTCUSDT"] → ["Binance_Bybit_BTCUSDT", "Binance_GateIo_BTCUSDT"]
  ↓ ↓ (only to relevant subscribers)
Chart 1: BTC Binance↔Bybit   → ✅ processes
Chart 2: BTC Binance↔GateIo  → ✅ processes

Result: 2 lambda calls, 0 wasted
```

### Implementation Plan

#### Step 1: Build Index in `RollingWindowService`

```csharp
// Maps "Exchange_Symbol" → ["WindowKey1", "WindowKey2", ...]
private readonly ConcurrentDictionary<string, HashSet<string>> _exchangeSymbolIndex = new();

// When creating a new window:
void AddSpreadPointToWindow(...)
{
    var windowKey = $"{exchange1}_{exchange2}_{symbol}";
    
    // Update index for both exchanges
    var index1 = $"{exchange1}_{symbol}";
    var index2 = $"{exchange2}_{symbol}";
    
    _exchangeSymbolIndex.AddOrUpdate(index1, 
        new HashSet<string> { windowKey },
        (k, set) => { set.Add(windowKey); return set; });
    
    _exchangeSymbolIndex.AddOrUpdate(index2,
        new HashSet<string> { windowKey },
        (k, set) => { set.Add(windowKey); return set; });
}
```

#### Step 2: Per-Window Events

```csharp
// Replace single global event with dictionary of events
private readonly ConcurrentDictionary<string, EventHandler<WindowDataUpdatedEventArgs>?> _windowEvents = new();

// Subscription method
public void SubscribeToWindow(string symbol, string exchange1, string exchange2, 
    EventHandler<WindowDataUpdatedEventArgs> handler)
{
    var windowKey = $"{exchange1}_{exchange2}_{symbol}";
    _windowEvents.AddOrUpdate(windowKey, handler, (k, existing) => existing + handler);
}

public void UnsubscribeFromWindow(string symbol, string exchange1, string exchange2,
    EventHandler<WindowDataUpdatedEventArgs> handler)
{
    var windowKey = $"{exchange1}_{exchange2}_{symbol}";
    if (_windowEvents.TryGetValue(windowKey, out var existing))
    {
        _windowEvents[windowKey] = existing - handler;
    }
}
```

#### Step 3: Targeted Event Firing

```csharp
private void OnWindowDataUpdated(string exchange, string symbol)
{
    var indexKey = $"{exchange}_{symbol}";
    
    // Find all windows that use this exchange+symbol combination
    if (_exchangeSymbolIndex.TryGetValue(indexKey, out var affectedWindows))
    {
        var eventArgs = new WindowDataUpdatedEventArgs
        {
            Exchange = exchange,
            Symbol = symbol,
            Timestamp = DateTime.UtcNow
        };
        
        // Invoke ONLY the handlers for affected windows
        foreach (var windowKey in affectedWindows)
        {
            if (_windowEvents.TryGetValue(windowKey, out var handler) && handler != null)
            {
                handler.Invoke(this, eventArgs);
            }
        }
    }
}
```

#### Step 4: Update `RealTimeController`

```csharp
// Replace global subscription with targeted subscription
foreach (var opp in opportunities)
{
    EventHandler<WindowDataUpdatedEventArgs> handler = async (sender, e) =>
    {
        // NO FILTER NEEDED! Event only fires if relevant!
        throttle and process...
    };
    
    // Subscribe to THIS window only
    _rollingWindow.SubscribeToWindow(opp.Symbol, opp.Exchange1, opp.Exchange2, handler);
    
    // Store for cleanup
    subscriptions[key] = (opp, handler);
}

// Cleanup
foreach (var (key, (opp, handler)) in subscriptions)
{
    _rollingWindow.UnsubscribeFromWindow(opp.Symbol, opp.Exchange1, opp.Exchange2, handler);
}
```

#### Step 5: Remove Legacy Global Event

```csharp
// REMOVE (after migration complete):
[Obsolete("Use SubscribeToWindow instead")]
public event EventHandler<WindowDataUpdatedEventArgs>? WindowDataUpdated;
```

---

## Expected Results

### Performance Improvement

**Before:**
- 41,800 handler invocations/sec
- 90% wasted CPU on filter checks
- Application freeze

**After:**
- ~4,000 handler invocations/sec (10x reduction)
- 0% wasted CPU (no filters needed)
- Smooth operation

### System Behavior

- ✅ Real-time charts update without lag
- ✅ CPU usage proportional to actual data rate
- ✅ Scalable to 50+ charts without performance degradation
- ✅ Event-driven architecture preserved (no polling)

---

## Files to Modify

1. **`collections/src/SpreadAggregator.Application/Services/RollingWindowService.cs`**
   - Add `_exchangeSymbolIndex` field
   - Add `_windowEvents` dictionary
   - Add `SubscribeToWindow` / `UnsubscribeFromWindow` methods
   - Update `AddSpreadPointToWindow` to populate index
   - Replace `OnWindowDataUpdated` with targeted firing
   - Mark `WindowDataUpdated` as `[Obsolete]`

2. **`collections/src/SpreadAggregator.Presentation/Controllers/RealTimeController.cs`**
   - Replace `_rollingWindow.WindowDataUpdated += handler` 
   - With `_rollingWindow.SubscribeToWindow(..., handler)`
   - Remove filter check in lambda (no longer needed)
   - Update cleanup to use `UnsubscribeFromWindow`

---

## Testing Plan

1. **Unit Tests:** Verify index population and targeted event firing
2. **Load Test:** Run with 20 charts, measure handler invocation rate
3. **Stress Test:** Scale to 50 charts, verify no performance degradation
4. **Regression:** Ensure all charts still receive correct updates

---

## Related Documents

- `docs/gemini3/handoff_threadpool_investigation.md` - ThreadPool analysis
- `docs/gemini3/performance_report_20251121.md` - Telemetry data
- `collections/logs/diagnostics/events.log` - Event processing logs

---

## Lessons Learned

1. **Don't broadcast when you can route** - Targeted delivery is always better
2. **Measure before optimizing** - Telemetry revealed the real problem
3. **Avoid workarounds** - Polling would have masked the architectural issue
4. **Event-driven is good** - The pattern was right, the implementation was wrong

---

**Next Steps:** Implement targeted event delivery (Estimated: 2-3 hours)
