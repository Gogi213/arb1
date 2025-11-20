# Phase 1: Brain - Signal Detection

**Status:** ⚪ Next (After Phase 0 complete)  
**Priority:** 🔴 CRITICAL  
**Goal:** Intelligent trading - знать КОГДА входить в сделку

---

## 🎯 Business Goal

**Current state:** Collections собирает spreads, но не торгует (слепая система)  
**Target state:** Automated mean-reversion arbitrage между Gate/Bybit

**Strategy:**

- **Entry:** |deviation| >= 0.35% между биржами
- **Exit:** deviation → 0 (±0.05% tolerance)
- **Exchanges:** Gate.io + Bybit (2 exchanges only)

**Key Metrics:**

- Opportunities detected/day (target: >50)
- Signal latency (target: <10ms)
- API response time (target: <20ms)
- Fill rate (manual validation week 1)

---

## 🔍 Gap Analysis

### Current State

- ✅ Collections: spread calculation WITHIN exchange (`(ask-bid)/ask`)
- ✅ Trader: ConvergentTrader (buy → wait → sell)
- ❌ No cross-exchange deviation calculation
- ❌ No signal detection logic
- ❌ No API connection between collections ↔ trader

### Target State

- ✅ Collections: deviation calculation BETWEEN exchanges
- ✅ Collections: entry/exit signal detection
- ✅ Collections: REST API for signals
- ✅ Trader: subscribes to signals, auto-executes
- ✅ End-to-end: collections → signal → trader → order

---

## 📋 Tasks

### Task 1.1: Cross-Exchange Deviation Calculator ✅ CRITICAL

**Problem:**  
Collections вычисляет spread ВНУТРИ биржи, а нужен deviation МЕЖДУ биржами.

**Solution:**  
Новый сервис `DeviationCalculator` который:

1. Subscribes к spread updates от Gate и Bybit
2. Groups spreads by symbol (BTC_USDT, ETH_USDT, etc)
3. Calculates deviation:

   ```csharp
   // Example: Gate=50000, Bybit=50175
   deviation = (price_bybit - price_gate) / price_gate * 100
   // = +0.35% (Bybit дороже)
   ```

4. Emits `DeviationData` events в real-time

**Target File:** `collections/src/SpreadAggregator.Application/Services/DeviationCalculator.cs` (NEW)

**Acceptance Criteria:**

- ✅ Deviation calculated для каждой пары Gate/Bybit
- ✅ Precision: 0.01% (two decimal places)
- ✅ Latency: **<10ms** после spread update
- ✅ Unit tests: validate formula accuracy
- ✅ Handles missing data (one exchange offline)

**Estimate:** 2-3 hours

---

### Task 1.2: Signal Detector ✅ CRITICAL

**Problem:**  
Нет логики для детекции profitable opportunities (entry/exit thresholds).

**Solution:**  
Новый сервис `SignalDetector` который:

1. Subscribes к `DeviationCalculator` events
2. Detects **entry signal:** `|deviation| >= 0.35%`
3. Detects **exit signal:** existing signal + `|deviation| <= 0.05%`
4. Tracks active signals (avoid duplicate entries)
5. Emits `Signal` objects:

   ```csharp
   public record Signal(
       string Symbol,
       decimal Deviation,
       SignalDirection Direction, // UP or DOWN
       string CheapExchange,
       string ExpensiveExchange,
       DateTime Timestamp
   );
   ```

**Target File:** `collections/src/SpreadAggregator.Application/Services/SignalDetector.cs` (NEW)

**Acceptance Criteria:**

- ✅ Entry signals when |deviation| >= 0.35%
- ✅ Exit signals when deviation returns close to 0
- ✅ No duplicate signals (track active state)
- ✅ Cooldown: 10s between signals for same pair
- ✅ Unit tests: threshold crossing logic

**Estimate:** 2-3 hours

---

### Task 1.3: Signals REST API ✅ CRITICAL

**Problem:**  
Trader не может получить signals от collections (no API).

**Solution:**  
Новый REST endpoint в collections:

```http
GET /api/signals/active
```

**Response:**

```json
{
  "signals": [
    {
      "symbol": "BTC_USDT",
      "deviation": -0.37,
      "direction": "DOWN",
      "cheapExchange": "Gate",
      "expensiveExchange": "Bybit",
      "timestamp": "2025-11-20T20:00:00Z",
      "ageMs": 1234
    }
  ],
  "count": 1
}
```

**Target File:** `collections/src/SpreadAggregator.Presentation/Controllers/SignalsController.cs` (NEW)

**Acceptance Criteria:**

- ✅ Endpoint returns active entry signals
- ✅ Response time: **<20ms** (aggressive target for HFT)
- ✅ Empty array when no signals
- ✅ Optional: WebSocket endpoint for push notifications
- ✅ Integration test: validate JSON format

**Estimate:** 1-2 hours

---

### Task 1.4: Trader Integration ✅ CRITICAL

**Problem:**  
Trader работает standalone, no connection to collections signals.

**Solution:**  
Modify trader to:

1. Poll `/api/signals/active` every 1-2s (or subscribe via WebSocket)
2. On **entry signal:**
   - If direction=DOWN (Gate cheaper) → buy Gate
   - If direction=UP (Bybit cheaper) → buy Bybit
3. On **exit signal** → market sell
4. Track cycle state (bought → waiting exit → sold)

**Target Files:**

- `trader/src/Core/SignalClient.cs` (NEW) - HTTP client for collections API
- `trader/src/Core/SignalBasedTrader.cs` (NEW) - orchestrates signal → trade
- `trader/src/Host/Program.cs` (MODIFY) - add signal-based mode

**Acceptance Criteria:**

- ✅ Trader receives signals from collections
- ✅ Entry execution <5s after signal появления
- ✅ Exit execution when deviation → 0
- ✅ Logs all signal → trade mappings
- ✅ Error handling: API timeout, signal stale

**Estimate:** 4-6 hours

---

### Task 1.5: Live Validation ✅ CRITICAL

**Problem:**  
Need to validate signal accuracy in production before scaling.

**Solution:**  

1. Deploy Phase 1 changes to production
2. Monitor signals for 1 week
3. Manual execute 1 trade based on signal (capital: $100)
4. Track:
   - Entry price (when signal fired)
   - Exit price (when deviation → 0)
   - Fees (Gate + Bybit)
   - Net P&L

**Acceptance Criteria:**

- ✅ Signals detected: >50/day (mean reversion opportunities)
- ✅ Signal accuracy: >80% (follow-through to exit)
- ✅ At least 1 manual trade: profitable (after fees)
- ✅ Deviation calculation: matches reality (manual spot check)
- ✅ API stability: 0 crashes, <1% error rate

**Estimate:** 1 week monitoring + 2h manual trading setup

---

## 📊 Success Criteria (Phase 1 Complete)

### Technical

- ✅ `DeviationCalculator` running in collections
- ✅ `SignalDetector` emitting entry/exit signals
- ✅ `/api/signals/active` endpoint operational (<20ms)
- ✅ Trader auto-executes based on signals
- ✅ Unit tests: 100% pass for signal logic

### Business

- ✅ Opportunities detected: >50/day
- ✅ Signal latency: <10ms (deviation calc)
- ✅ API latency: <20ms (REST endpoint)
- ✅ Manual validation: 1+ profitable trade
- ✅ System stability: 7 days uptime, 0 crashes

### Metrics to Track

- Signals/day (по каждой паре)
- Signal duration (how long profitable window lasts)
- False signals (signal fires, но no follow-through)
- Entry execution time (signal → order placed)
- Exit execution time (deviation → 0 → order filled)

---

## 🎯 Deliverables

1. ✅ **Code:**
   - `DeviationCalculator.cs`
   - `SignalDetector.cs`
   - `SignalsController.cs`
   - `SignalClient.cs` (trader)
   - `SignalBasedTrader.cs` (trader)

2. ✅ **Tests:**
   - Unit tests: deviation formula
   - Unit tests: signal threshold logic
   - Integration test: API endpoint

3. ✅ **Documentation:**
   - Updated `phase-1-brain.md` (this file)
   - API documentation for `/api/signals/active`

4. ✅ **Validation:**
   - 1 week production monitoring
   - Manual trade log (entry, exit, P&L)

---

## ⚠️ Risks & Mitigation

### Risk 1: Deviation logic неправильная

**Impact:** HIGH (wrong signals → losses)  
**Mitigation:**

- Unit tests с known prices
- Backfill на historical Parquet data
- Manual spot checks during week 1

---

### Risk 2: Signals too frequent (noise)

**Impact:** MEDIUM (spam, trader overload)  
**Mitigation:**

- Min signal duration filter (hold >5s)
- Cooldown between signals (10s)
- Log all signals → analyze frequency

---

### Risk 3: 0.35% threshold слишком узкий

**Assumption:** 0.35% - fees ~0.1% = ~0.25% profit  
**Reality:** May need 0.5% for consistent profit  
**Mitigation:**

- Configurable threshold (`appsettings.json`)
- Test different values (0.25%, 0.35%, 0.5%)
- Adjust based on week 1 results

---

### Risk 4: Latency bottleneck

**Assumption:** <10ms deviation calc, <20ms API  
**Reality:** May be slower in production  
**Plan B:**

- Profile hot paths (Task 1.1, 1.3)
- If >20ms → defer optimization to Phase 3
- Phase 1 focus: correctness > speed

---

## 🔗 Dependencies

**Blocked by:**

- Phase 0 complete ✅

**Blocks:**

- Phase 2 (Monitoring) - need signals to track
- Phase 3 (Latency) - need baseline metrics
- Phase 4 (Automation) - need signal API

---

## 🏁 Definition of Done

Phase 1 is **COMPLETE** when:

1. ✅ All 5 tasks (1.1 - 1.5) marked COMPLETE
2. ✅ All unit tests passing (>90% coverage for signal logic)
3. ✅ API endpoint live: `GET /api/signals/active` (<20ms)
4. ✅ Trader executes trades based on signals (live test)
5. ✅ 1 week production monitoring: >50 signals/day
6. ✅ Manual trade validation: 1+ profitable trade ($100)
7. ✅ 0 crashes, 0 data loss

**Go/No-Go:** If manual trade unprofitable → analyze (threshold? fees? execution?)

---

## 📅 Estimate

**Total effort:** 10-14 hours

**Breakdown:**

- Task 1.1: 2-3h
- Task 1.2: 2-3h
- Task 1.3: 1-2h
- Task 1.4: 4-6h
- Task 1.5: 1 week monitoring (passive) + 2h manual

**Timeline:** 1-2 weeks (including validation)

---

[← Back to Roadmap](README.md) | [Next Phase: Monitoring →](phase-2-monitoring.md)
