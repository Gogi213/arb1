# Audit Report: Current State of Arb1 Ecosystem

## 1. Executive Summary
The ecosystem consists of three main components: `Collections` (SpreadAggregator), `Trader` (TraderBot), and `Analyzer`.
*   **Collections**: significantly improved with recent "fragility fixes". It now features independent data channels, hot-path optimization for low latency, and error boundaries. It is a strong foundation.
*   **Trader**: Currently in an MVP state. It functions but lacks critical safety checks (timestamp validation), configurability, and risk management. It is the primary candidate for evolution.
*   **Analyzer**: A powerful, fast (Polars-based) batch processor. Currently disconnected from the live trading loop.

## 2. Component Analysis

### 2.1. Collections (SpreadAggregator)
**Strengths:**
*   **Architecture**: Clean separation of concerns (Presentation, Domain, Infrastructure).
*   **Robustness**: `PROPOSAL-2025-0093` (Independent Channels) and `PROPOSAL-2025-0095` (Graceful Shutdown/Error Boundaries) are correctly implemented.
*   **Latency**: "Hot path" broadcasting to WebSocket before channel writing is a pro-level optimization.
*   **Normalization**: Symbol normalization logic in `OrchestrationService` ensures consistency.

**Weaknesses:**
*   **Data Loss Risk**: `BoundedChannel` with `DropOldest` is used for the `RawDataChannel` (ParquetWriter). If disk I/O lags, historical data will be silently dropped. This is acceptable for live trading but bad for data quality.
*   **Hardcoded Exchanges**: Adding a new exchange requires modifying `Program.cs` DI container registration.

### 2.2. Trader (TraderBot)
**Strengths:**
*   **Latency Awareness**: Excellent logging of server-side vs. local timestamps.
*   **Execution Safety**: `ArbitrageTrader` waits for *actual* balance confirmation before the second leg. This prevents "phantom" trades.
*   **Concurrency**: Correct usage of `SemaphoreSlim` to protect state.

**Critical Weaknesses (Must Fix):**
*   **Stale Data Vulnerability**: `SpreadListener` calculates spreads based on `_lastGateBid` and `_lastBybitBid`. It **does not check timestamps**. If one exchange disconnects or lags, the bot will trade against a stale price, leading to guaranteed losses.
*   **Stateful Spread Calculation**: The spread calculation happens in the `Trader`, not the `Aggregator`. This duplicates logic and introduces the stateful bug mentioned above.
*   **Hardcoded Strategy**: "Dollar Offset" ($25) and "Spread Threshold" (0.25%) are hardcoded.
*   **Single Strategy**: Tightly coupled to "Buy A -> Sell B".

### 2.3. Analyzer
**Strengths:**
*   **Performance**: Polars-based, parallel processing is highly efficient.
*   **Depth**: Calculates advanced metrics like "Zero Crossings" (mean reversion).

**Weaknesses:**
*   **Disconnected**: Its insights (e.g., "Pair X has high mean reversion") are not fed back into the Trader.

## 3. Recommendations

1.  **Immediate Fix (Critical)**: Implement **Timestamp Validation** in `SpreadListener`. Reject any price older than X ms (e.g., 1000ms).
2.  **Evolution**:
    *   Move spread calculation logic *back* to `Collections` (or make `Trader` stateless per message).
    *   Externalize configuration (thresholds, offsets).
    *   Implement a "Circuit Breaker" (stop trading after N consecutive losses).
3.  **Integration**: Create a shared `trading_config.json` that `Analyzer` can update and `Trader` can read.
