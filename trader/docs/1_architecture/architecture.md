# Trader Project Architecture
**Version:** 2.0 (Validated on 2025-11-17)

## 1. Overview

The `Trader` project is a .NET application designed to execute high-frequency arbitrage trades based on spread data received from the `Collections` project. It is architected for low latency and robust state management throughout the trade lifecycle.

The system listens for profitable spreads, executes a two-legged trade (buy on one exchange, sell on another), and meticulously tracks the state and timing of each step.

## 2. Core Components

The architecture is divided into three main logical modules: `Host`, `Core`, and `Exchanges`.

### 2.1. `Host` (Entry Point)

*   **`Program.cs`**: A console application that serves as the entry point.
    *   **Responsibilities:**
        1.  Reads configuration from `appsettings.json` (specifically, the `SpreadListenerUrl`).
        2.  Initializes and wires up the key services: `SpreadListener` and `DecisionMaker`.
        3.  Starts the main application loop by calling `listener.StartAsync()`.

### 2.2. `Core` (Business Logic)

This module contains the primary decision-making and trade execution logic.

*   **`SpreadListener`**:
    *   **Responsibility:** Connects via WebSocket to the `SpreadAggregator` (`Collections` project) to receive real-time spread data.
    *   **Logic:** When a spread exceeds a hardcoded threshold (e.g., 0.25%), it fires an `OnProfitableSpreadDetected` event, passing the arbitrage direction (e.g., `GateIo_To_Bybit`).

*   **`DecisionMaker` (Partially Implemented):**
    *   **Responsibility:** Reacts to the `OnProfitableSpreadDetected` event.
    *   **Current State:** The implementation is a **placeholder**. It correctly subscribes to the event and uses an `_isCycleInProgress` flag to prevent overlapping cycles, but it **does not initiate a trade**. It only logs that a profitable spread was detected.
    *   **Future Goal (`TODO`):** The code contains a `TODO` block indicating the plan is to integrate and start the `ArbitrageTrader` to execute the trade cycle.

*   **`ArbitrageTrader` (Intended State Machine):**
    *   **Status:** This component represents the **intended, future-state design** for the trade execution logic. It is **not currently called or used** by the `DecisionMaker`. The following describes its planned functionality:
    *   **Responsibility:** To act as a state machine for a complete arbitrage cycle.
    *   **Intended State Flow:**
        1.  **Start:** Initiates a `TrailingTrader` to place the "buy" leg of the trade.
        2.  **Wait for Buy Fill:** Subscribes to the `OnOrderFilled` event from the `TrailingTrader`.
        3.  **Wait for Balance:** Upon buy fill, it does **not** immediately sell. Instead, it waits for a `TaskCompletionSource` (`_baseAssetBalanceTcs`) to be signaled by a balance update from the exchange, ensuring the asset is available. A debounce timer handles fluctuating balance updates.
        4.  **Execute Sell:** Once the balance is confirmed, it places an immediate market "sell" order on the second exchange.
        5.  **Wait for Sell Fill:** It then waits for a WebSocket order update confirming the sell order has been filled.
        6.  **Complete:** Calculates P&L, logs all latencies (E2E, WebSocket propagation), and signals completion via another `TaskCompletionSource` (`_arbitrageCycleTcs`).
    *   **Concurrency Control:** Uses a `SemaphoreSlim` to ensure that the critical logic from buy-fill to sell-placement is not interrupted.

*   **`IExchange` (Interface):**
    *   **Responsibility:** Defines a universal contract (Adapter pattern) for interacting with any exchange. It includes methods for placing orders, getting balances, and subscribing to WebSocket updates for orders and balances.

### 2.3. `Exchanges` (Adapters)

*   **Responsibility:** Contains concrete implementations of the `IExchange` interface for each supported exchange (e.g., `BybitExchange`, `GateIoExchange`). This layer isolates the core logic from the specific API details of each exchange.
*   **`BybitExchange` & `BybitLowLatencyWs`:** The Bybit implementation is a prime example of a low-latency adapter. It does not use a standard REST/WebSocket library but instead employs a custom-built `BybitLowLatencyWs` client that communicates directly over WebSockets for all critical operations, including placing and canceling orders, to minimize latency.

## 3. Architectural Patterns

*   **State Machine:** The `ArbitrageTrader` is a clear implementation of a state machine, using `TaskCompletionSource` to transition between asynchronous states (awaiting fill, awaiting balance, awaiting confirmation).
*   **Observer:** `SpreadListener` acts as the publisher, and `DecisionMaker` is the subscriber.
*   **Adapter:** The `IExchange` interface allows the core logic to work with any exchange implementation.
*   **Singleton/State Lock:** The `_isCycleInProgress` flag in `DecisionMaker` and the `SemaphoreSlim` in `ArbitrageTrader` are used to manage state and prevent race conditions.

## 4. External Dependencies

*   **`SpreadAggregator` (`Collections` project):** The source of spread data.
*   **`appsettings.json`:** Configuration file for the `SpreadListenerUrl`.
*   **Exchange-specific libraries:** `Bybit.Net`, `GateIo.Net`, etc., are used within the `Exchanges` layer.