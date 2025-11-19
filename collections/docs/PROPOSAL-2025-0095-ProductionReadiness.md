# PROPOSAL-2025-0095: Production Readiness Refactoring

**Status:** APPROVED
**Priority:** CRITICAL
**Target:** MVP Evolution Foundation
**Author:** Claude Code
**Date:** 2025-11-19

## Executive Summary

Transform SpreadAggregator from fragile prototype to production-ready, fault-tolerant HFT system capable of 24/7 operation without OOM crashes or data loss.

**Critical Issues Blocking MVP Evolution:**
- 8 Memory Leaks (static + unbounded collections)
- No Graceful Shutdown (data loss on restart)
- Fire-and-Forget Async (thread pool starvation)
- No Error Boundaries (cascade failures)
- No Backpressure (system overload)

**Expected Outcomes:**
- ✅ Zero memory leaks
- ✅ Graceful shutdown with data preservation
- ✅ Bounded resource usage
- ✅ Circuit breakers for all external dependencies
- ✅ Health monitoring and self-healing

---

## Sprint Structure

### Sprint 1: Memory Safety Foundations (2-3 hours)
**Goal:** Eliminate all memory leaks and establish bounded resource limits

**Tasks:**
1. Remove static memory leaks (`_testLogPaths`, `_lastLogTimes`)
2. Reduce rolling window from 30min → 15min
3. Add hard limits to `_windows` (max 10k windows)
4. Add hard limits to `_latestTicks` (max 50k ticks)
5. Enforce max spreads per window (10k → 5k points)
6. Add memory pressure detection
7. Cleanup `_allSymbolInfo` and `_exchangeTasks`

**Success Criteria:**
- Memory usage stable after 24h run
- No unbounded collection growth
- GC pressure reduced by 50%

---

### Sprint 2: Graceful Lifecycle Management (2-3 hours)
**Goal:** Implement proper startup/shutdown with zero data loss

**Tasks:**
1. Implement `IHostedService` proper shutdown
2. Complete channels on shutdown
3. Flush all Parquet buffers on shutdown
4. Close WebSocket connections gracefully
5. Unsubscribe from all exchange streams
6. Dispose all `ManagedConnection` resources
7. Add startup health checks

**Success Criteria:**
- Clean shutdown in <5 seconds
- Zero data loss on restart
- All resources properly disposed

---

### Sprint 3: Resilience & Backpressure (3-4 hours)
**Goal:** Add fault tolerance and prevent system overload

**Tasks:**
1. Replace fire-and-forget async with bounded concurrency
2. Add error boundaries around exchange operations
3. Implement circuit breakers for each exchange
4. Add retry policies with exponential backoff
5. Implement backpressure in channels
6. Add dead WebSocket connection cleanup
7. Event handler leak protection

**Success Criteria:**
- Single exchange failure doesn't affect others
- Automatic recovery from transient failures
- No thread pool starvation under load

---

### Sprint 4: Performance & Monitoring (2-3 hours)
**Goal:** Add observability and optimize hot paths

**Tasks:**
1. Add health check endpoint
2. Add memory/CPU metrics
3. Add Prometheus-compatible metrics
4. Optimize `CalculateRollingQuantile` (use ArrayPool)
5. Add structured logging
6. Add performance counters
7. Add alerting thresholds

**Success Criteria:**
- Real-time visibility into system health
- Performance metrics available for analysis
- Alerts on memory/CPU/errors

---

## Technical Design

### 1. Memory Safety Architecture

#### Current Problems
```csharp
// BEFORE: Unbounded static leak
private static readonly Dictionary<string, string> _testLogPaths = new();
private static readonly ConcurrentDictionary<string, DateTime> _lastLogTimes = new();

// BEFORE: No size limits
private readonly ConcurrentDictionary<string, RollingWindowData> _windows = new();
private readonly ConcurrentDictionary<string, (DateTime, decimal, decimal)> _latestTicks = new();
```

#### Solution
```csharp
// AFTER: Remove static leaks entirely OR make instance-scoped with cleanup
// AFTER: Add hard limits with LRU eviction
private const int MAX_WINDOWS = 10_000;
private const int MAX_LATEST_TICKS = 50_000;
private const int MAX_SPREADS_PER_WINDOW = 5_000;

private readonly LruCache<string, RollingWindowData> _windows = new(MAX_WINDOWS);
private readonly LruCache<string, TickData> _latestTicks = new(MAX_LATEST_TICKS);
```

**Implementation:**
- Create `LruCache<TKey, TValue>` helper class with automatic eviction
- Add memory pressure listener to trigger aggressive cleanup
- Change window size from 30min → 15min
- Enforce max spreads in `AddSpreadPointToWindow`

---

### 2. Graceful Shutdown Architecture

#### Current Problems
```csharp
// BEFORE: Does nothing!
public Task StopAsync(CancellationToken cancellationToken)
{
    return Task.CompletedTask;
}
```

#### Solution
```csharp
// AFTER: Proper shutdown sequence
public async Task StopAsync(CancellationToken cancellationToken)
{
    _logger.LogInformation("Shutting down gracefully...");

    // 1. Stop accepting new data
    await _orchestrationService.StopAsync(cancellationToken);

    // 2. Complete channels (stop producers)
    _rawDataChannel.Writer.Complete();
    _rollingWindowChannel.Writer.Complete();

    // 3. Wait for consumers to drain
    await Task.WhenAll(
        _rawDataChannel.Reader.Completion,
        _rollingWindowChannel.Reader.Completion
    );

    // 4. Flush buffered data
    await _dataWriter.FlushAsync();

    // 5. Close connections
    _webSocketServer.Dispose();
    _rollingWindowService.Dispose();

    _logger.LogInformation("Shutdown complete");
}
```

**Implementation:**
- Add `FlushAsync()` to `IDataWriter`
- Add proper `Dispose()` to `ManagedConnection`
- Implement shutdown timeout (30 seconds max)
- Add shutdown health check endpoint

---

### 3. Resilience Architecture

#### Current Problems
```csharp
// BEFORE: Fire-and-forget = thread pool starvation
_ = _webSocketServer.BroadcastRealtimeAsync(message);

// BEFORE: No error isolation
await ProcessExchange(exchangeClient, exchangeName);
```

#### Solution
```csharp
// AFTER: Bounded task concurrency
private readonly SemaphoreSlim _broadcastSemaphore = new(100, 100);

await _broadcastSemaphore.WaitAsync();
try
{
    await _webSocketServer.BroadcastRealtimeAsync(message);
}
finally
{
    _broadcastSemaphore.Release();
}

// AFTER: Error boundaries with circuit breaker
private readonly Dictionary<string, CircuitBreaker> _exchangeCircuitBreakers = new();

await _exchangeCircuitBreakers[exchangeName].ExecuteAsync(async () =>
{
    await ProcessExchange(exchangeClient, exchangeName);
});
```

**Implementation:**
- Create `CircuitBreaker` class (Open/HalfOpen/Closed states)
- Add Polly library for retry policies
- Limit concurrent broadcast operations
- Add dead connection cleanup timer in FleckWebSocketServer

---

### 4. Monitoring Architecture

#### Health Check Endpoint
```csharp
[HttpGet("/health")]
public IActionResult GetHealth()
{
    var health = new
    {
        status = "healthy",
        uptime = DateTime.UtcNow - _startTime,
        memory = new
        {
            workingSet = GC.GetTotalMemory(false) / 1024 / 1024,
            gen0Collections = GC.CollectionCount(0),
            gen1Collections = GC.CollectionCount(1),
            gen2Collections = GC.CollectionCount(2)
        },
        services = new
        {
            rollingWindow = new
            {
                activeWindows = _rollingWindow.GetWindowCount(),
                totalSpreads = _rollingWindow.GetTotalSpreadCount()
            },
            exchanges = _orchestration.GetExchangeHealth()
        }
    };

    return Ok(health);
}
```

**Metrics to Track:**
- Memory usage (working set, GC collections)
- Active windows count
- Total spreads/trades count
- Channel queue depths
- Exchange connection states
- Error rates per exchange
- WebSocket connection count

---

## Implementation Plan

### Sprint 1: Memory Safety
**Session 1.1 - Remove Static Leaks**
- Delete `_testLogPaths` static dictionary
- Delete `_lastLogTimes` static dictionary
- Move test logging to instance-scoped logger

**Session 1.2 - Add Bounded Collections**
- Create `LruCache<TKey, TValue>` helper
- Replace `_windows` with bounded LRU cache
- Replace `_latestTicks` with bounded LRU cache
- Reduce window from 30min → 15min

**Session 1.3 - Enforce Limits**
- Add MAX_SPREADS_PER_WINDOW check in AddSpreadPointToWindow
- Add memory pressure detection
- Cleanup _allSymbolInfo and _exchangeTasks

**Tests:**
```csharp
[Fact]
public void RollingWindow_Should_Not_Exceed_MaxWindows()
{
    // Add 20k windows, verify only 10k retained
}

[Fact]
public void RollingWindow_Should_Evict_Oldest_Data()
{
    // Verify LRU eviction policy
}
```

---

### Sprint 2: Graceful Lifecycle
**Session 2.1 - Shutdown Orchestration**
- Implement OrchestrationService.StopAsync
- Complete channels on shutdown
- Add shutdown timeout

**Session 2.2 - Resource Cleanup**
- Add IDataWriter.FlushAsync
- Implement ManagedConnection.Dispose
- Close all WebSocket connections

**Session 2.3 - Health Checks**
- Add startup health checks
- Add shutdown health verification

**Tests:**
```csharp
[Fact]
public async Task Shutdown_Should_Complete_Within_5_Seconds()
{
    // Verify shutdown timeout
}

[Fact]
public async Task Shutdown_Should_Flush_All_Buffers()
{
    // Verify no data loss
}
```

---

### Sprint 3: Resilience
**Session 3.1 - Bounded Concurrency**
- Add SemaphoreSlim for broadcast operations
- Add SemaphoreSlim for file writes
- Replace all fire-and-forget

**Session 3.2 - Circuit Breakers**
- Create CircuitBreaker class
- Add circuit breaker per exchange
- Add retry policies

**Session 3.3 - Error Boundaries**
- Wrap all exchange operations in try-catch
- Add error event aggregation
- Add dead connection cleanup

**Tests:**
```csharp
[Fact]
public async Task CircuitBreaker_Should_Open_After_5_Failures()
{
    // Verify circuit breaker behavior
}

[Fact]
public async Task Exchange_Failure_Should_Not_Crash_System()
{
    // Verify error isolation
}
```

---

### Sprint 4: Monitoring
**Session 4.1 - Health Endpoint**
- Create HealthController
- Add memory metrics
- Add service health checks

**Session 4.2 - Performance Metrics**
- Add Prometheus metrics
- Add performance counters
- Add structured logging

**Session 4.3 - Optimization**
- Optimize CalculateRollingQuantile with ArrayPool
- Add metric alerts
- Add performance benchmarks

**Tests:**
```csharp
[Fact]
public void Health_Endpoint_Should_Return_All_Metrics()
{
    // Verify health response
}

[Fact]
public void CalculateRollingQuantile_Should_Use_ArrayPool()
{
    // Verify zero allocations
}
```

---

## Migration Strategy

### Phase 1: Memory Safety (Week 1)
- Deploy Sprint 1 changes
- Monitor memory for 48 hours
- Validate no OOM issues

### Phase 2: Lifecycle (Week 1)
- Deploy Sprint 2 changes
- Test shutdown/restart cycles
- Validate zero data loss

### Phase 3: Resilience (Week 2)
- Deploy Sprint 3 changes
- Chaos testing (kill exchanges)
- Validate automatic recovery

### Phase 4: Monitoring (Week 2)
- Deploy Sprint 4 changes
- Set up alerting
- Establish SLOs

---

## Success Metrics

### Memory
- ✅ Stable memory usage after 24h (±10%)
- ✅ Gen2 GC collections <10 per hour
- ✅ No unbounded collection growth

### Reliability
- ✅ 99.9% uptime over 30 days
- ✅ Zero crashes from OOM
- ✅ Zero data loss on restart

### Performance
- ✅ Spread calculation latency <1ms p99
- ✅ WebSocket broadcast latency <10ms p99
- ✅ Channel processing lag <100ms

### Resilience
- ✅ Automatic recovery from exchange failures
- ✅ Circuit breakers prevent cascade failures
- ✅ Graceful degradation under load

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Breaking existing functionality | High | Comprehensive tests, gradual rollout |
| Performance regression | Medium | Benchmarks before/after, profiling |
| LRU eviction too aggressive | Medium | Configurable limits, metrics |
| Circuit breaker false positives | Low | Tune thresholds based on metrics |

---

## Rollback Plan

Each sprint is independently deployable. If issues arise:
1. Revert to previous version via git
2. Restore configuration
3. Restart service

Rollback time: <5 minutes

---

## Acceptance Criteria

- [ ] All tests pass
- [ ] Memory stable for 24h continuous run
- [ ] Clean shutdown in <5 seconds
- [ ] Zero data loss on restart
- [ ] Single exchange failure isolated
- [ ] Health endpoint operational
- [ ] Metrics dashboards deployed
- [ ] Documentation updated

---

## Next Steps

1. **Approve proposal** - Review and sign-off
2. **Sprint 1 execution** - Memory safety foundations
3. **Sprint 2 execution** - Graceful lifecycle
4. **Sprint 3 execution** - Resilience
5. **Sprint 4 execution** - Monitoring
6. **Production deployment** - Gradual rollout with monitoring

---

## References

- AUDIT-2025-11-19: Collections Architecture Audit
- PROPOSAL-2025-0093: HFT Hot Path Optimization
- PROPOSAL-2025-0094: Last-Tick Matching
