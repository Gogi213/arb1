# Development Cycle - HFT Ecosystem

**Version:** 3.0  
**Updated:** 2025-11-20  
**Philosophy:** Ship fast, iterate on reality

---

## TL;DR

**Process:**

1. Pick task from roadmap
2. **Analyze against HFT requirements** (latency <5ms, no stale data)
3. If conflicts → propose alternative BEFORE coding
4. Get approval → implement
5. Ship ASAP

**Key Principle:** Reality > Plan

---

## PROJECT CONTEXT

### What We're Building

**HFT Arbitrage System:**

- Strategy: Mean-reversion (buy cheap, sell when converged)
- Exchanges: Gate.io ↔ Bybit
- Entry: |deviation| >= 0.35%
- Exit: deviation → 0
- Window: Arbitrage lives 200-500ms

**Critical Requirements:**

| Requirement | Target | Why |
|-------------|--------|-----|
| **Latency** | <5ms signal→execution | Opportunities disappear in 200ms |
| **No Stale Data** | Always fresh | Trading on old data = losses |
| **Uptime** | 99.9% | Miss uptime = miss money |

---

### Architectural Principles for HFT

**1. Latency is KING**

❌ NEVER:

- REST API for execution (2000ms polling)
- Microservices when monolith works (<5ms vs 10ms+)
- Network calls in hot path

✅ ALWAYS:

- Direct function calls (<1ms)
- Monolith for solo HFT
- WebSocket for monitoring only

**2. Data Freshness = Life**

❌ NEVER:

- Send signal over network then execute (prices changed!)

✅ ALWAYS:

- Execute immediately on signal (same process)

**3. Graceful Degradation**

**When exchange/component fails:**

- ❌ NEVER: Crash or throw unhandled exception
- ✅ ALWAYS: Stop trading gracefully, keep system running
- ✅ Log error, skip signal, wait for recovery

**Example:**

```
Gate.io unavailable → skip signals, don't crash
Bad price data → validate, reject if invalid
```

**4. Backpressure Handling**

**Expected load:** 30-50 signals/sec peak

- ✅ Drop oldest if buffer full (fresh data > old data)
- ✅ Log when dropping (monitoring)
- ❌ Don't block/wait (latency > data loss)

**5. Simplicity Budget**

| Pattern | Use When | AVOID When |
|---------|----------|------------|
| Monolith | Solo dev, need <5ms | Never for HFT solo! |
| Microservices | Team >5 people | Solo HFT (adds latency) |
| REST API | Monitoring, UI | Execution (too slow) |
| WebSocket | Real-time monitor | OK for non-critical |
| Shared Memory | Ultra-HFT <0.1ms | Overkill if monolith works |

---

## DECISION FRAMEWORK

**Before implementing ANY task:**

1. **Latency impact?**
   - If >5ms on critical path → CHALLENGE IT!

2. **Data validation?**
   - Check for bad/invalid data from exchanges
   - Reject if price = $0 or unrealistic values

3. **Failure handling?**
   - What if exchange unavailable?
   - Graceful degradation (stop trading, don't crash)

4. **Simplest solution?**
   - Order: Monolith < WebSocket < REST

5. **Matches HFT requirements?**
   - 200ms window, <5ms execution, no stale data

6. **Testable?**
   - Can mock/integration test easily

**CRITICAL:** If roadmap conflicts with HFT requirements → **STOP and PROPOSE ALTERNATIVE**

**Example:**

- ❌ Roadmap says: "REST API for signals"
- ✅ Analysis: REST = 2000ms latency, HFT needs <5ms
- ✅ Action: STOP, propose Monolith (<1ms)
- ✅ Wait for approval → implement

---

## ROLES

### Developer (You)

- Pick tasks from roadmap
- Final authority on all decisions
- **Challenge Gemini** if solution doesn't match HFT requirements

### Gemini (AI)

**Process for EVERY task:**

1. **Critical Analysis** (BEFORE coding):
   - Read task from roadmap
   - Check latency impact
   - Check data validation needs
   - Check failure handling
   - Check graceful degradation
   - Check simplicity
   - **If conflicts with HFT requirements → STOP**

2. **Propose Alternative**:
   - Explain why roadmap fails (metrics: 2000ms vs 5ms target)
   - Suggest solution that meets requirements
   - Wait for approval

3. **Implementation** (AFTER approval):
   - Write code
   - Update roadmap status

**Constraints:**

- No authority (advisor only)
- Must justify with metrics
- Cannot blindly follow roadmap

**Decision Checklist:**

```
Before implementing:
1. Latency impact? (must be <5ms critical path)
2. Data validation? (reject bad prices)
3. Failure handling? (graceful degradation)
4. Simplest solution? (monolith < WebSocket < REST)
5. Matches HFT? (200ms window, mean-reversion)
6. Testable?

If ANY fails → STOP, propose alternative
```

---

## PRE-PHASE CONSILIUM

**Mandatory before EACH phase.**

**Purpose:** Align business expectations with technical implementation.

**When:** After previous phase complete, before first commit of new phase

**Duration:** ~30 min

### Structure

**1. Business Expectations (5-10 min)**

- What changes in metrics? (fill rate, latency, P&L)
- What problem solved?
- How measure success?

**2. Current State Validation (10-15 min)**

- Validate existing code
- Find gaps (what's missing?)
- Identify blockers

**3. Phase Definition (5-10 min)**

- Tasks: concrete, not abstract
- Acceptance criteria: measurable (latency <10ms, not "fast")
- Estimate: realistic

**4. Risk Assessment (5 min)**

- What can fail?
- Assumptions made?
- Plan B?

**Deliverables:**

- Updated phase file with concrete tasks
- Clear acceptance criteria (metrics)
- Risk mitigation plan
- Go/No-Go decision

---

## TASK WORKFLOW

**Lifecycle:**

```
⏸️ PENDING → 🟡 IN PROGRESS → ✅ COMPLETE
```

**Status Updates:**

- Mark ✅ COMPLETE in phase file when done
- Update README.md with progress

**Testing:**

- TDD for bugs only
- Integration tests for features
- Pragmatic: ship at 80-90%

---

**Last Updated:** 2025-11-20  
**See Also:** `docs/gemini3/roadmap/` for tasks and phases
