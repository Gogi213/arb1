# Trader Project Architecture
**Version:** 3.0 (Validated on 2025-11-18)

## 1. Overview

The `Trader` project is a .NET application designed to execute arbitrage or short-term trading strategies. This document outlines two distinct architectural phases: the **Legacy Two-Legged Arbitrage MVP** and the **Current Convergent Trader MVP**.

## 2. Legacy Architecture (Deprecated MVP)

This section describes the initial vision and implementation of the `Trader` project, which focused on a complex two-legged arbitrage strategy between two different exchanges. While the code for this architecture remains in the codebase, it is **no longer actively used or maintained** as the primary trading strategy. It serves as a historical record of the project's evolution.

### 2.1. Overview (Legacy)

The system was designed to listen for profitable spreads, execute a two-legged trade (buy on one exchange, sell on another), and meticulously track the state and timing of each step. This approach proved to be complex and prone to issues related to timing, fees, and state management, leading to its deprecation in favor of a simpler model.

### 2.2. Core Components (Legacy)

The architecture was divided into three main logical modules: `Host`, `Core`, and `Exchanges`.

#### 2.2.1. `Host` (Entry Point - Legacy)

*   **`Program.cs`**: The console application serving as the entry point. In scenarios where the `ConvergentTrader` is not explicitly invoked via command-line arguments, `Program.cs` proceeds with the setup for the legacy components:
    1.  Reading configuration from `appsettings.json` (e.g., `SpreadListenerUrl`).
    2.  Initializes and wires up key services like `SpreadListener` and `DecisionMaker`.
    3.  Starts the `SpreadListener` via `listener.StartAsync()`.

#### 2.2.2. `Core` (Business Logic - Legacy)

This module contained the primary decision-making and trade execution logic for the two-legged arbitrage.

*   **`SpreadListener`**:
    *   **Responsibility:** Connects via WebSocket to the `SpreadAggregator` (`Collections` project) to receive real-time spread data. **This component is still instantiated and started if the `ConvergentTrader` is not invoked via command-line arguments.**
    *   **Logic:** When a spread exceeded a predefined threshold (e.g., 0.25%), it fired an `OnProfitableSpreadDetected` event, indicating an arbitrage direction (e.g., `GateIo_To_Bybit`).

*   **`DecisionMaker` (Legacy - Placeholder):**
    *   **Responsibility:** Reacts to the `OnProfitableSpreadDetected` event.
    *   **State:** This was largely a **placeholder implementation**. It subscribed to the event and used an `_isCycleInProgress` flag to prevent overlapping cycles, but it **did not initiate a trade**. It only logged profitable spread detections. **This component still receives events from the `SpreadListener` but performs no trading actions.** The intention was to integrate and start the `ArbitrageTrader`.

*   **`ArbitrageTrader` (Legacy - Intended State Machine):**
    *   **Status:** This component represented the **intended, future-state design** for the first leg of the two-legged trade cycle (buy on one exchange, sell on another). It was **never fully integrated or actively used** by the `DecisionMaker` in its operational phase.
    *   **Intended State Flow:**
        1.  **Start:** Initiated a `TrailingTrader` to place the "buy" leg.
        2.  **Wait for Buy Fill:** Subscribed to `OnOrderFilled` from the `TrailingTrader`.
        3.  **Wait for Balance:** Upon buy fill, it waited for a `TaskCompletionSource` (`_baseAssetBalanceTcs`) to be signaled by a balance update, ensuring asset availability. A debounce timer handled fluctuating balance updates.
        4.  **Execute Sell:** Once balance was confirmed, it placed an immediate market "sell" order on the second exchange.
        5.  **Wait for Sell Fill:** Waited for a WebSocket order update confirming the sell order fill.
        6.  **Complete:** Calculated P&L, logged latencies, and signaled completion via `_arbitrageCycleTcs`.
    *   **Concurrency Control:** Used a `SemaphoreSlim` to protect critical logic.

*   **`ReverseArbitrageTrader` (Legacy - Intended State Machine for Leg 2):**
    *   **Status:** This component represented the **intended design for the second leg** of the two-legged arbitrage, aimed at rebalancing assets. Like `ArbitrageTrader`, it was **never fully integrated or actively used**.
    *   **Intended Responsibility:** To perform a reverse trade, buying back on the secondary exchange and selling the original quantity on the primary exchange, utilizing state from `ArbitrageTrader`.

*   **`IExchange` (Interface - Legacy):**
    *   **Responsibility:** Defined a universal contract (Adapter pattern) for interacting with any exchange, including methods for placing orders, getting balances, and subscribing to WebSocket updates.

### 2.3. `Exchanges` (Adapters - Legacy)

*   **Responsibility:** Contained concrete implementations of the `IExchange` interface for each supported exchange (e.g., `BybitExchange`, `GateIoExchange`). This layer isolated the core logic from specific exchange API details.
*   **Example (`BybitExchange` & `BybitLowLatencyWs`):** Used a custom `BybitLowLatencyWs` client for critical operations to minimize latency.

### 2.4. Architectural Patterns (Legacy)

*   **State Machine:** The `ArbitrageTrader` and `ReverseArbitrageTrader` were designed as state machines.
*   **Observer:** `SpreadListener` acted as the publisher, `DecisionMaker` as the subscriber.
*   **Adapter:** The `IExchange` interface provided adaptability.
*   **Singleton/State Lock:** `_isCycleInProgress` and `SemaphoreSlim` managed state and race conditions.

## 3. Current Architecture (Convergent Trader MVP)

This section describes the currently active and operational trading strategy within the `Trader` project. This architecture represents a simplified approach, focusing on single-exchange trading, which proved more robust and manageable than the legacy two-legged arbitrage.

### 3.1. Overview (Current)

The current strategy involves a `ConvergentTrader` that executes a buy order on a single exchange, waits for a short, fixed duration, and then sells the entire acquired balance on the *same* exchange. This simpler model reduces complexity associated with cross-exchange synchronization and asset transfer.

### 3.2. Core Components (Current)

The `Current Architecture` leverages parts of the existing `Host` and `Exchanges` modules, but the core trading logic is encapsulated in the `ConvergentTrader`.

#### 3.2.1. `Host` (Entry Point - Current)

*   **`Program.cs`**: The main entry point has been refactored to directly instantiate and run the `ConvergentTrader`.
    *   **Key Method:** `RunManualConvergentTrader()` is now the primary execution path.
    *   **Responsibilities:**
        1.  Reads configuration (e.g., API keys, order parameters for `ConvergentTrader`).
        2.  Initializes the specific `IExchange` implementation (e.g., `BybitExchange`).
        3.  Creates and starts the `ConvergentTrader` with the configured parameters.

#### 3.2.2. `Core` (Business Logic - Current)

The `ConvergentTrader` is the central component of the current trading strategy.

*   **`ConvergentTrader`**:
    *   **Responsibility:** Manages the entire buy-then-sell cycle on a single exchange.
    *   **State Flow:**
        1.  **Initialization:** Configured with exchange instance, symbol, order quantity, and other parameters.
        2.  **Start:** Initiates a buy order. It often employs a trailing mechanism or market order depending on configuration.
        3.  **Handle Buy Fill:** Once the buy order is filled, it records the purchased quantity and average price.
        4.  **Wait and Sell:** After a configured delay (e.g., `_sellDelayMs`), it places a market sell order for the entire acquired quantity.
        5.  **Handle Sell Fill:** Records the sold quantity and average price, completing one cycle.
        6.  **Loop/Stop:** Depending on configuration, it may repeat the cycle or stop.

*   **`IExchange` (Interface - Current):**
    *   **Responsibility:** Remains the universal contract for exchange interaction, used by `ConvergentTrader` for order placement and balance queries.

### 3.3. `Exchanges` (Adapters - Current)

*   **Responsibility:** Continues to provide concrete implementations of the `IExchange` interface, abstracting exchange-specific API details. The `ConvergentTrader` utilizes these adapters.

### 3.4. Architectural Patterns (Current)

*   **Command Pattern:** `ConvergentTrader` acts as a command executor for a predefined trading sequence.
*   **Adapter:** `IExchange` still serves as the adapter for exchange integration.
*   **Simple State Management:** Internal state within `ConvergentTrader` tracks the current phase (buy, wait, sell).
*   **Dependency Injection:** Configuration and exchange instances are injected into `ConvergentTrader`.

## 4. External Dependencies

*   **`appsettings.json`:** Configuration file for API keys, order parameters, and other settings relevant to the `ConvergentTrader`.
*   **Exchange-specific libraries:** `Bybit.Net`, `GateIo.Net`, etc., are used within the `Exchanges` layer.
*   **No direct dependency on `SpreadAggregator` from `Collections` for the current active strategy.**