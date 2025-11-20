# Phase 3: Latency Optimization

**Status:** ⚪ Backlog  
**Priority:** 🟡 HIGH  
**Goal:** Competitive advantage through speed

---

## Overview

**Renamed from:** Phase 6: Monolith

**Why critical:** In HFT, latency = advantage. 100ms vs 10ms = win or lose every opportunity.

**Current state:** ~100ms WebSocket latency (estimated)  
**Target:** <50ms end-to-end latency

---

## Tasks

### Task 3.1: Profiling & Bottleneck Analysis

**Goal:** Find where time is spent

**Tools:**

- dotnet-trace, dotnet-counters
- BenchmarkDotNet for microbenchmarks
- Custom instrumentation (Stopwatch)

**Focus areas:**

- WebSocket send/receive
- JSON serialization
- Channel write/read
- Cache lookups

**Estimate:** 2-3 hours

---

### Task 3.2: Hot Path Optimization

**Goal:** Optimize critical code paths

**Candidates:**

- Zero-allocation serialization (System.Text.Json source gen)
- Span<T> usage for string ops
- Stackalloc for small buffers
- Remove unnecessary async/await

**Target:** 50% latency reduction

**Estimate:** 6-8 hours

---

### Task 3.3: Optional: Merge Collections + Trader

**Goal:** Eliminate inter-process communication overhead

**Pros:**

- No HTTP/gRPC overhead
- Direct in-memory method calls
- Simpler deployment

**Cons:**

- Harder to scale independently
- More complex codebase

**Decision:** Defer until profiling proves IPC is bottleneck

**Estimate:** 8-12 hours (if needed)

---

## Deliverables

- ✅ Performance profile report (where time is spent)
- ✅ Optimized hot paths (50% faster)
- ✅ <50ms latency target achieved
- ⚪ Optional: Monolith architecture (if needed)

---

## Success Criteria

- End-to-end latency <50ms (p95)
- WebSocket send latency <10ms (p95)
- Zero allocations on hot path
- Competitive with commercial HFT platforms

---

[← Back to Roadmap](README.md) | [Next Phase: Automation →](phase-4-automation.md)
