# Phase 2: Monitoring & Observability

**Status:** ⚪ Backlog  
**Priority:** 🟡 HIGH  
**Goal:** Know what's happening in production

---

## Overview

**Merged from:**

- Old Phase 2: Security (logging, auth)
- Monitoring infrastructure

**Why critical:** Can't improve what you don't measure. Need visibility into production behavior.

---

## Tasks

### Task 2.1: Metrics Endpoint

**Goal:** Expose system metrics for monitoring

**Options:**

- Prometheus endpoint (`/metrics`)
- Simple JSON endpoint (`/api/metrics`)

**Metrics to track:**

- Messages/sec processed
- WebSocket latency (p50, p95, p99)
- Exchange health status
- Cache hit/miss rates
- Memory usage, GC pauses

**Estimate:** 2-3 hours

---

### Task 2.2: Alerting System

**Goal:** Get notified of critical errors

**Implementation:**

- Telegram bot integration
- Alert on:
  - Exchange disconnect (>1 min)
  - High error rate (>10/min)
  - Memory leak (steady growth)
  - Data loss (failed parquet writes)

**Estimate:** 3-4 hours

---

### Task 2.3: Trade Journal

**Goal:** Automatic P&L tracking

**Features:**

- Log all trades (entry, exit, P&L)
- Daily/weekly/monthly summary
- Export to CSV
- Simple CLI report

**Estimate:** 4-6 hours

---

## Deliverables

- ✅ Metrics endpoint with key KPIs
- ✅ Telegram alerts for critical issues
- ✅ Trade journal with P&L tracking
- ✅ CLI dashboard for quick overview

---

## Success Criteria

- Metrics update in real-time (<1s lag)
- Alerts fire within 30s of issue
- Trade journal has 100% accuracy (vs manual calc)
- Can diagnose production issues in <5 min

---

[← Back to Roadmap](README.md) | [Next Phase: Latency →](phase-3-latency.md)
