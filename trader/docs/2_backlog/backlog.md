# Trader Project Backlog
**Version:** 2.0 (Validated on 2025-11-17)

## Current Project Status (November 2025)

**State:** The project is in an **experimental and partially implemented** state.

*   **Spread Detection:** Working.
*   **Trade Execution:** **Not yet implemented.** The `DecisionMaker` currently only detects and logs profitable spreads. The logic to instantiate and run the `ArbitrageTrader` to execute the full, two-legged trade cycle is a `//TODO` placeholder and is not active.

**Priority Task:** Implementation of the `ArbitrageTrader` integration and subsequent stabilization of the full trading cycle.

---

## Epics and Key Tasks

| ID | Task | Component | Priority | Status | Description |
|---|---|---|---|---|---|
| EPIC-001 | Stabilize and Configure Trading Logic | `Core`, `Bybit` | Critical | In Progress | Focus on making the implemented trading cycle robust. This includes fetching symbol filters from the exchange, making parameters configurable, and improving error handling. |

## Technical Debt

| ID | Task | Component | Priority | Status | Description |
|---|---|---|---|---|---|
| TD-005 | Fetch symbol filters from the exchange | `Bybit` | High | To Do | Currently, `tickSize` and `basePrecision` are hardcoded. These must be fetched from the exchange's API to ensure correct order quantity rounding. |
| TD-002 | Make "magic numbers" configurable | `Core`, `Bybit` | High | To Do | Order sizes, trailing percentages, and other parameters should be moved to `appsettings.json` instead of being hardcoded. |
| TD-013 | Use `Math.Truncate` for quantity calculations | `Core` | Medium | To Do | Replace `Math.Round` with `Math.Truncate` to avoid rounding up and potentially exceeding available balance. |
| TD-009 | Add error handling for `ModifyOrderAsync` | `Core`, `Bybit` | Medium | To Do | The system should be able to handle failures when modifying an order. |
| TD-010 | Implement a local order book cache | `Bybit` | Medium | To Do | Maintain a local order book (snapshot + deltas) to enable faster decision-making without constant REST calls. |
| TD-007 | Implement balance query via WebSocket | `Bybit` | Low | To Do | The `GetBalanceAsync` method is not implemented. While balance updates are received via WS, a direct query mechanism is needed. |
| TD-006 | Replace `Task.Delay` in `AuthenticateAsync` | `Bybit` | Low | To Do | Authentication should wait for a confirmation message, not a fixed delay. |

## New Features and Improvements

| ID | Feature | Description | Priority | Status |
|---|---|---|---|---|
| FEAT-001 | Dynamic Order Sizing | Calculate order size based on available balance and risk parameters from the configuration. | High | To Do |
| FEAT-004 | P&L and Performance Tracking | Persist the results of each trade cycle (P&L, latencies) to a database or log file for performance analysis. | High | To Do |
| FEAT-002 | Add Support for More Exchanges | Implement `IExchange` for other exchanges like OKX, Binance, etc., to expand arbitrage opportunities. | Medium | To Do |
| FE.AT-003 | Monitoring and UI | Create a simple web interface to monitor the bot's status, live P&L, and logs in real-time. | Low | To Do |

---

## Completed / Obsolete Tasks

| ID | Task | Status | Notes |
|---|---|---|---|
| EPIC-001 (Old) | Implement trade execution logic | Done | The core logic has been implemented in `ArbitrageTrader`. |
| TD-001 | Use real buy quantity for sell order | Done | Implemented via the balance confirmation step in `ArbitrageTrader`. |
| TD-011 | Await Leg 2 completion | Done | The `ArbitrageTrader` state machine correctly awaits the sell confirmation. |
| TD-012 | Reliable balance management | Done | Implemented via the debounce timer and `TaskCompletionSource` for balance updates. |
| TD-008 | Subscribe to balance updates | Done | Implemented in `BybitExchange` and used by `ArbitrageTrader`. |
| PROPOSAL-* | Various historical proposals | Obsolete | Most old proposals are now obsolete due to the implementation of the full trading cycle. |