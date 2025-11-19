# Evolution Roadmap: From Manual Scripts to Autonomous Ecosystem

**Current State Analysis**:
- **Trader**: Capable of smart execution (`TrailingTrader` with liquidity offset), but requires manual startup with hardcoded args.
- **Collections**: Knows the "Top Opportunities" (via `OpportunityFilterService` reading Analyzer CSV), but only uses them for charts.
- **Analyzer**: Generates the intelligence (CSV), but is disconnected from execution.

**The Gap**: `Collections` has the targets, `Trader` has the gun, but they don't talk. A human must manually bridge them.

---

## ✅ Phase 1: Parameter Externalization (The Tuning Knob)
*Goal: Allow tuning of the existing `ConvergentTrader` logic without recompilation.*


### Tasks:
- [x] Create `TradingSettings` class (SpreadThreshold, MaxDataAge, SellDelay, LiquidityOffset).
- [x] Inject `IOptionsMonitor<TradingSettings>` into `ConvergentTrader` and `TrailingTrader`.
- [x] Update `appsettings.json` with default values matching current hardcoded ones.
- [x] Verify `dotnet run` works exactly as before.

### 🏁 State after Phase 1:
- **Functionality**: Same as before, but tunable.
- **Workflow**: Edit `appsettings.json` -> Restart Trader -> New parameters applied.
- **Value**: Can experiment with `$10` offset vs `$50` offset easily.

---

## 🔗 Phase 2: Dynamic Targeting (The Bridge)
*Goal: Automate the selection of trading pairs.*

### Tasks:
1.  **Expose Opportunities**:
    - Create an API endpoint in `Collections`: `GET /api/opportunities/top`.
    - This endpoint uses existing `OpportunityFilterService` to return the best pairs.
2.  **Consume Opportunities**:
    - Update `Trader` to accept a new mode: `dotnet run -- auto`.
    - In `auto` mode, Trader polls `GET /api/opportunities/top`.
3.  **Execution Loop**:
    - Trader picks the top opportunity (e.g., `VIRTUAL_USDT` on `Bybit`).
    - Runs one cycle of `ConvergentTrader`.
    - Re-evaluates top opportunity.

### 🏁 State after Phase 2:
- **Functionality**: Autonomous Trading.
- **Workflow**: Run `dotnet run -- auto`. Bot trades whatever is best according to Analyzer.
- **Value**: No more manual restarts to change symbols.

---

## 🎛️ Phase 3: Hot Control & UI (The Cockpit)
*Goal: Real-time control via Dashboard.*

### Tasks:
1.  **Config API**:
    - Add `POST /api/config` to `Collections`.
    - This updates the shared `appsettings.json`.
2.  **Hot Reload**:
    - Ensure `Trader` picks up config changes instantly (via `IOptionsMonitor`).
3.  **UI Panel**:
    - Add "Bot Settings" to `index.html`.
    - Sliders for `TrailingOffset`, `SellDelay`.
    - Toggle for `Auto Mode`.

### 🏁 State after Phase 3:
- **Functionality**: Full Ecosystem.
- **Workflow**: Open Dashboard -> Adjust Sliders -> Watch Bot adapt.
- **Value**: Professional trading terminal experience.

---

## Summary of Evolution

| Phase | Trader Intelligence | Target Selection | Parameter Control |
|-------|---------------------|------------------|-------------------|
| **Current** | Smart Execution (Hardcoded) | Manual (CLI Args) | Recompile |
| **Phase 1** | Smart Execution (Configurable) | Manual (CLI Args) | JSON Edit |
| **Phase 2** | Smart Execution (Configurable) | **Auto (API)** | JSON Edit |
| **Phase 3** | Smart Execution (Configurable) | Auto (API) | **Web UI** |
