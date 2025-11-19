# Evolution Plan: Trader MVP to Robust System

## Phase 1: Safety & Robustness (Immediate)

### 1.1. Fix Stale Data Vulnerability
*   **Problem**: `SpreadListener` caches `_lastGateBid` and `_lastBybitBid` indefinitely.
*   **Solution**:
    *   Store `(Price, Timestamp)` tuples instead of just `Price`.
    *   In `CalculateAndLogSpreads`, check: `if (DateTime.UtcNow - timestamp > MaxLatency) return;`
    *   **MaxLatency** should be configurable (start with 1000ms).

### 1.2. Circuit Breaker
*   **Problem**: If the strategy goes haywire, it will drain the wallet.
*   **Solution**:
    *   Add `DailyLossLimit` (e.g., -5% of starting balance).
    *   Add `ConsecutiveFailureLimit` (e.g., 3 failed arbitrage cycles).
    *   If triggered, stop `ArbitrageTrader` and alert.

## Phase 2: Configurability & Strategy Abstraction

### 2.1. External Configuration
*   **Problem**: Magic numbers ($25 offset, 0.25% spread) are hardcoded.
*   **Solution**:
    *   Create `StrategyConfig` class.
    *   Load from `strategy_settings.json`.
    *   Allow hot-reloading (using `IOptionsMonitor` or file watcher).

### 2.2. Abstract the Strategy
*   **Problem**: `ArbitrageTrader` is hardcoded for "Trailing Buy -> Market Sell".
*   **Solution**:
    *   Define `IArbitrageStrategy` interface.
    *   Implement `ConvergentStrategy` (current logic).
    *   Implement `ParallelStrategy` (buy and sell simultaneously - riskier but faster).
    *   Use Factory pattern to select strategy based on config.

## Phase 3: Ecosystem Integration

### 3.1. Analyzer Feedback Loop
*   **Goal**: `Analyzer` tells `Trader` *what* to trade.
*   **Implementation**:
    *   `Analyzer` runs daily/hourly.
    *   Outputs `active_pairs.json` with specific thresholds per pair (based on volatility).
    *   `Trader` reads `active_pairs.json` to subscribe to specific pairs.

### 3.2. Shared Data Models
*   **Goal**: Ensure `Collections` and `Trader` speak the same language.
*   **Implementation**:
    *   Move `SpreadData`, `MarketData` to a shared NuGet package or shared project (if possible) or just keep them strictly synced.
