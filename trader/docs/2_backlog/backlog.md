# Trader Project Backlog
**Version:** 4.0 (Validated on 2025-11-18)

## Current Project Status (November 2025)

**State:** The project has evolved from an experimental two-legged arbitrage MVP to a **functional single-exchange Convergent Trader MVP**.

*   **Legacy Two-Legged Arbitrage (`ArbitrageTrader`, `DecisionMaker`):** **Deprecated and inactive.** While the code remains for historical context, the complex two-legged arbitrage strategy is no longer the active trading mechanism. It was found to be prone to issues related to synchronization, fees, and state management.
*   **Convergent Trader:** **Active and operational.** The `ConvergentTrader` implements a simpler, single-exchange buy-then-sell strategy and is the current focus of development.

**Priority Task:** Optimization and enhancement of the `ConvergentTrader` strategy for improved profitability and robustness.

---

## Epics and Key Tasks (Convergent Trader)

| ID | Task | Component | Priority | Status | Description |
|---|---|---|---|---|---|
| EPIC-002 | Optimize Convergent Trader Parameters | `Core` | Critical | To Do | Research and implement dynamic or adaptive parameter tuning for buy trigger, sell delay, and profit targets. |
| EPIC-003 | Expand Convergent Trading Exchanges | `Exchanges` | High | To Do | Integrate `ConvergentTrader` with additional exchanges (e.g., OKX, Binance) for broader application of the strategy. |

## Technical Debt (Convergent Trader Focus)

| ID | Task | Component | Priority | Status | Description |
|---|---|---|---|---|---|
| TD-005 | Fetch symbol filters from the exchange | `Bybit` | High | To Do | For `ConvergentTrader`, `tickSize` and `basePrecision` must be fetched from the exchange's API to ensure correct order quantity rounding. (Still relevant for any exchange integration) |
| TD-002 | Make "magic numbers" configurable | `Core` | High | To Do | Order sizes, sell delays, and other `ConvergentTrader` parameters should be moved to `appsettings.json` instead of being hardcoded. |
| TD-014 | Accurate P&L Calculation for Convergent Trader | `Core` | High | To Do | Replace the estimated proceeds calculation with actual fill prices and exchange fees for precise P&L tracking. |
| TD-015 | Implement Sell Order Status Tracking | `Core` | High | To Do | Remove the assumption of immediate market sell fill (`Task.Delay(1000);`) and implement robust tracking of sell order status via WebSocket updates. |
| TD-009 | Add robust error handling for order operations | `Core`, `Bybit` | Medium | To Do | The `ConvergentTrader` needs comprehensive error handling for placing, modifying, and canceling orders. |
| TD-010 | Implement a local order book cache | `Exchanges` | Medium | To Do | Maintain a local order book (snapshot + deltas) to enable faster decision-making for `ConvergentTrader` entry/exit points without constant REST calls. |
| TD-007 | Implement balance query via WebSocket | `Bybit` | Low | To Do | The `GetBalanceAsync` method is not implemented. While balance updates are received via WS, a direct query mechanism is needed for `ConvergentTrader` to confirm funds. |
| TD-006 | Replace `Task.Delay` in `AuthenticateAsync` | `Bybit` | Low | To Do | Authentication should wait for a confirmation message, not a fixed delay, for any exchange client used by `ConvergentTrader`. |

## New Features and Improvements (Convergent Trader)

| ID | Feature | Description | Priority | Status |
|---|---|---|---|---|
| FEAT-001 | Dynamic Order Sizing | Calculate `ConvergentTrader` order size based on available balance and risk parameters from the configuration. | High | To Do |
| FEAT-004 | P&L and Performance Tracking | Persist the results of each `ConvergentTrader` cycle (P&L, latencies) to a database or log file for performance analysis. | High | To Do |
| FEAT-005 | Adaptive Sell Logic | Implement more intelligent sell conditions for `ConvergentTrader` (e.g., target profit, stop-loss, trailing stop) instead of a fixed delay. | High | To Do |
| FEAT-002 | Add Support for More Exchanges | Implement `IExchange` for other exchanges (e.g., OKX, Binance) to expand `ConvergentTrader`'s reach. | Medium | To Do |
| FEAT-003 | Monitoring and UI | Create a simple web interface to monitor `ConvergentTrader`'s status, live P&L, and logs in real-time. | Low | To Do |
| FEAT-006 | Backtesting Module | Develop a module to backtest `ConvergentTrader` strategies using historical data. | Medium | To Do |

---

## Completed / Obsolete Tasks

| ID | Task | Status | Notes |
|---|---|---|---|
| EPIC-001 (Old) | Stabilize and Configure Trading Logic | Obsolete | Addressed by the deprecation of the two-legged arbitrage and focus on `ConvergentTrader`. |
| Priority Task (Old) | Implementation of the `ArbitrageTrader` integration | Obsolete | Superseded by `ConvergentTrader` as the active strategy. |
| TD-001 | Use real buy quantity for sell order | Obsolete | Relevant to `ArbitrageTrader`; `ConvergentTrader` handles this internally. |
| TD-011 | Await Leg 2 completion | Obsolete | Relevant to two-legged arbitrage. |
| TD-012 | Reliable balance management | Obsolete | Relevant to `ArbitrageTrader`; `ConvergentTrader` handles balance for a single exchange. |
| TD-008 | Subscribe to balance updates | Completed (for Convergent) | Essential for `ConvergentTrader` to confirm funds; context shifted. |
| PROPOSAL-* | Various historical proposals for two-legged arbitrage | Obsolete | Many old proposals are now obsolete due to the deprecation of the two-legged arbitrage and the pivot to `ConvergentTrader`. |