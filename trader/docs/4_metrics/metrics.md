# Trader Project: Key Performance Metrics
**Version:** 2.0 (Validated on 2025-11-17)

This document describes the key performance and diagnostic metrics for the `Trader` project. Given its high-frequency nature, latency measurement is critical.

## 1. Core Detection Metric

*   **Name:** Profitable Spread Threshold
*   **Location:** `SpreadListener.cs`
*   **Value:** Hardcoded as `0.25%`.
*   **Description:** The `OnProfitableSpreadDetected` event is fired if the spread percentage received from the `SpreadAggregator` exceeds this value. This is the entry trigger for the entire trading cycle.

---

## Metrics for Future Implementation

The following metrics are part of the **intended future design** centered around the `ArbitrageTrader` component, which is not yet integrated. They are **not currently being generated** by the application.

## 2. Latency and Performance Metrics

These metrics are planned to be logged throughout the `ArbitrageTrader` lifecycle and are essential for evaluating the system's performance. They would be logged via `FileLogger.LogOther()` and `FileLogger.LogWebsocket()`.

*   **WebSocket Propagation Delay (WS Latency):**
    *   **Description:** The time elapsed between an event occurring on the exchange's server and the event being received and processed by the application's handler.
    *   **Calculation:** `(Local_Event_Handler_Entry_Time - Server_Side_Event_Timestamp)`
    *   **Importance:** Measures the speed of the WebSocket connection and the exchange's infrastructure. High values can indicate network issues or slow processing by the client library.

*   **API Command Latency:**
    *   **Description:** The time taken to execute a specific low-latency WebSocket command, such as `PlaceMarketOrderAsync`.
    *   **Calculation:** Time measured immediately before and after the `await` call for the specific command.
    *   **Importance:** Measures the round-trip time for a command to be sent to the exchange and a confirmation to be received. This is a direct measure of the exchange's API performance.

*   **End-to-End (E2E) Server Latency:**
    *   **Description:** The total time elapsed from the server-side confirmation of the "buy" leg fill to the server-side confirmation of the "sell" leg fill.
    *   **Calculation:** `(Sell_Fill_Server_Timestamp - Buy_Fill_Server_Timestamp)`
    *   **Importance:** This is the **most critical metric** for arbitrage. It represents the "pure" time the strategy was exposed to market risk, independent of any local application or network delays.

*   **End-to-End (E2E) Local Latency:**
    *   **Description:** The total time elapsed from the moment the application's "buy" fill handler is entered to the moment the "sell" fill confirmation handler is entered.
    *   **Calculation:** `(Sell_Confirmation_Handler_Entry_Time - Buy_Fill_Handler_Entry_Time)`
    *   **Importance:** Measures the total processing time of the application's state machine, including all internal logic, waits, and latencies. It helps identify bottlenecks within the `ArbitrageTrader` itself.

## 3. Financial Metrics (Post-Cycle)

*   **USDT Proceeds:**
    *   **Description:** The total amount of quote currency (e.g., USDT) received from the "sell" leg of the trade.
    *   **Source:** Extracted from the `CumulativeQuoteQuantity` field of the filled sell order update.
    *   **Importance:** This is the primary output of a successful trade cycle, used for calculating the final Profit and Loss (P&L).
