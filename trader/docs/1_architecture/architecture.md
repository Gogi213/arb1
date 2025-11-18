# Trader Project Architecture
**Version:** 4.0 (Validated on 2025-11-18)

## 1. Overview

The `Trader` project is a .NET application designed to execute automated trading strategies. The architecture has evolved from a complex, two-legged arbitrage model to a simpler, more robust single-exchange strategy. This document outlines both the current, active architecture and the legacy components that remain in the codebase.

The application has two primary modes of operation, determined by command-line arguments at startup:
1.  **Convergent Trader Mode (Active):** Invoked by passing an exchange name (`bybit` or `gate`) as an argument. This is the primary, operational trading strategy.
2.  **Spread Listener Mode (Legacy/Passive):** The default mode when no arguments are provided. This mode listens for arbitrage opportunities but **does not execute trades**.

## 2. Current Architecture: Convergent Trader

This section describes the active and operational trading strategy within the `Trader` project. It represents a simplified and more reliable approach, focusing on a single-exchange, buy-then-sell cycle.

### 2.1. Entry Point & Control Flow

*   **`Host/Program.cs`**: The application's entry point. When started with `bybit` or `gate` arguments, it executes the `RunManualConvergentTrader()` method.
*   **`RunManualConvergentTrader()`**:
    1.  Reads the relevant exchange configuration from `appsettings.json`.
    2.  Instantiates the correct `IExchange` implementation (`BybitExchange` or `GateIoExchange`).
    3.  Injects the exchange adapter into a new `ConvergentTrader` instance.
    4.  Starts the `ConvergentTrader` with parameters (symbol, amount) from the configuration.

### 2.2. Core Components

The `ConvergentTrader` is the heart of the current strategy.

*   **`Core/ConvergentTrader.cs`**:
    *   **Responsibility:** Manages the entire buy-wait-sell cycle on a single exchange.
    *   **State Flow:**
        1.  **Start:** Cancels any open orders for the target symbol and subscribes to balance updates. It then uses a `TrailingTrader` to execute the initial "buy" order.
        2.  **Handle Buy Fill:** Once the buy order is filled, it waits for a WebSocket balance update to confirm the asset is available in the account.
        3.  **Wait and Sell:** After a hardcoded 5-second delay, it places a market "sell" order for the entire acquired quantity of the asset.
        4.  **Complete:** Assumes the market sell order fills immediately and completes the cycle.

*   **`Core/IExchange.cs`**:
    *   **Responsibility:** A key abstraction (Adapter pattern) that defines a universal contract for all exchange-specific implementations. It allows `ConvergentTrader` to interact with different exchanges without knowing their specific API details.

*   **`Exchanges/`**:
    *   **Responsibility:** This directory contains the concrete implementations of the `IExchange` interface (e.g., `BybitExchange`, `GateIoExchange`). This layer isolates the core logic from third-party libraries and exchange-specific protocols.

### 2.3. Architectural Patterns

*   **Command Pattern:** The `ConvergentTrader` can be seen as a command that executes a predefined trading sequence.
*   **Adapter Pattern:** The `IExchange` interface decouples the core logic from the exchange implementations.
*   **Dependency Injection:** The `IExchange` instance is injected into the `ConvergentTrader` at runtime.

---

## 3. Legacy Architecture: Spread Listener (Passive)

This section describes the initial implementation of the `Trader` project, which is no longer the active trading strategy but remains the default execution path if the application is started without arguments.

### 3.1. Overview

The legacy system was designed to listen for arbitrage opportunities between two exchanges, as identified by the `SpreadAggregator` project. However, the component responsible for acting on these opportunities (`DecisionMaker`) was never fully implemented to execute trades.

### 3.2. Entry Point & Control Flow

*   **`Host/Program.cs`**: If no command-line arguments are provided, the application falls back to this mode.
    1.  It reads the `SpreadListenerUrl` from `appsettings.json`.
    2.  It initializes the `SpreadListener` and the `DecisionMaker`.
    3.  The `DecisionMaker` subscribes to events from the `SpreadListener`.
    4.  The application starts the `SpreadListener` and waits indefinitely.

### 3.3. Core Components

*   **`Core/SpreadListener.cs`**:
    *   **Responsibility:** Connects to the `SpreadAggregator` WebSocket server.
    *   **Logic:** It listens for incoming spread data. If a spread exceeds a predefined threshold, it fires an `OnProfitableSpreadDetected` event.

*   **`Core/DecisionMaker.cs`**:
    *   **Responsibility:** Subscribes to the `OnProfitableSpreadDetected` event.
    *   **State:** This is a **placeholder implementation**. It logs that a profitable spread was detected and uses a simple flag (`_isCycleInProgress`) to avoid logging concurrent events.
    *   **Crucially, it does not contain any logic to start a trader or execute any orders.** The `TODO` comments in the code confirm its incomplete status.

## 4. External Dependencies

*   **`appsettings.json`:** Contains all external configuration, including the `SpreadListenerUrl` for the legacy mode and the `ExchangeConfigs` (API keys, secrets, trading amounts) for the active `ConvergentTrader` mode.
*   **Exchange Libraries:** `Bybit.Net`, `GateIo.Net`, etc., are used within the `Exchanges` layer.
*   **`SpreadAggregator` Project:** The legacy `SpreadListener` mode depends on the WebSocket server provided by the `SpreadAggregator` (from the `collections` project). The active `ConvergentTrader` mode has **no dependency** on this.
