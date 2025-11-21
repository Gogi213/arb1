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

### Task 1.1: Cross-Exchange Deviation Calculator ✅ COMPLETE

**Problem:**  
Collections вычисляет spread ВНУТРИ биржи, а нужен deviation МЕЖДУ биржами.

**Solution:**  
Новый сервис `DeviationCalculator` который:

1. Subscribes к spread updates от Gate и Bybit
2. Groups spreads by symbol (BTC_USDT, ETH_USDT, etc)
3. Calculates deviation (bid/bid comparison):

   ```csharp
   // Example: Gate bid=50000, Bybit bid=50175
   deviation = (bid_bybit - bid_gate) / bid_gate * 100
   // = +0.35% (Bybit дороже)
   ```

4. Emits `DeviationData` events в real-time

**Implementation:**

- ✅ `DeviationData.cs` entity created
- ✅ `DeviationCalculator.cs` service (bid-only comparison)
- ✅ Integrated into `OrchestrationService`
- ✅ Registered in DI (`Program.cs`)

**Tests:**

- ✅ 6 integration tests created  
- ✅ 42/42 total tests passing
- ✅ Build: 0 errors

**Performance:**

- ✅ Latency: ~5ms (target <10ms)
- ✅ Thread-safe: ConcurrentDictionary

**Acceptance Criteria:**

- ✅ Deviation calculated для каждой пары Gate/Bybit
- ✅ Precision: 0.01% (two decimal places)
- ✅ Latency: <10ms после spread update (achieved ~5ms)
- ✅ Unit tests: validate formula accuracy
- ✅ Handles missing data (one exchange offline)

**Estimate:** 2-3 hours  
**Actual:** 2 hours

---

### Task 1.2: Signal Detector ⚪ NEXT

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

### Task 1.3: Signal Broadcasting ✅ COMPLETE

**Problem:**  
Trader needs to receive signals from Collections with low latency.

**Solution: Distributed Architecture (WebSocket)**

**Collections broadcasts signals via WebSocket**:

```csharp
// Program.cs
detector.OnEntrySignal += signal => 
{
    var wrapper = new WebSocketMessage { 
        MessageType = "Signal", 
        Payload = signal 
    };
    _webSocketServer.BroadcastRealtimeAsync(JsonSerializer.Serialize(wrapper));
};
```

**Trader subscribes and executes**:

- Trader connects to Collections WebSocket.
- On "Signal" message -> Triggers ArbitrageTrader.

**Architecture:**

```
collections.exe (Server):
  - WebSocket → exchanges (spreads)
  - DeviationCalculator → SignalDetector
  - OnEntrySignal → WebSocket Broadcast

trader.exe (Client):
  - Connects to collections WebSocket
  - Listens for Signals
  - Executes trades via Exchange API
```

**Target Files:**

- `collections/src/Presentation/Program.cs` (Broadcast logic)
- `trader/src/Core/SpreadListener.cs` (Listener logic)

**Acceptance Criteria:**

- ✅ WebSocket broadcast for monitoring/trading
- ✅ Latency: <10ms (local network)
- ✅ Integration test: signal triggers broadcast

**Estimate:** 2-3 hours

---

### Task 1.4: Trader Execution Wiring ⚪ NEXT

**Problem:**
Trader has the logic (`ArbitrageTrader`, `SpreadListener`) but is not wired to listen to Collections automatically.

**Solution:**
Update `trader/src/Host/Program.cs` to support an "Auto-Trading" mode that:

1. Connects to Collections WebSocket using `SpreadListener`.
2. On Signal -> Calls `ArbitrageTrader.ExecuteAsync`.

**Target Files:**

- `trader/src/Host/Program.cs`

**Acceptance Criteria:**

- ✅ Trader starts in "Auto" mode
- ✅ Connects to Collections
- ✅ Receives Signal -> Places Order

---

### Task 1.3: Signal Broadcasting & Execution ✅ COMPLETE

**Implementation:**

- ✅ WebSocket broadcast for Entry/Exit signals (monitoring)
- ✅ Direct execution via TradeExecutor (monolith, <1ms latency)
- ✅ Non-blocking async broadcast (doesn't affect execution)

**Files Modified:**

- `Program.cs`: Added WebSocket broadcast + TradeExecutor wiring

**Latency:**

- SignalDetector → TradeExecutor: <1ms (direct call) ✅
- WebSocket broadcast: async, non-blocking ✅

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
