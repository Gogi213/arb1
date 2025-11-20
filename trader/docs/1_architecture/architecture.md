# Trader Project Architecture
**Version:** 4.0 (Validated on 2025-11-18)

## 1. Overview

The `Trader` project is a .NET application designed to execute automated trading strategies. The architecture has evolved from a complex, two-legged arbitrage model to a simpler, more robust single-exchange strategy. This document outlines both the current, active architecture and the legacy components that remain in the codebase.

The application has two primary modes of operation, determined by command-line arguments at startup:
1.  **Convergent Trader Mode (Active):** Invoked by passing an exchange name (`bybit` or `gate`) as an argument. This is the primary, operational trading strategy.
2.  **Spread Listener Mode (Legacy/Passive):** The default mode when no arguments are provided. This mode listens for arbitrage opportunities but **does not execute trades**.

## 2. Current Architecture: Convergent Trader

This section describes the active and operational trading strategy within the `Trader` project. It represents a simplified and more reliable approach, focusing on a single-exchange, buy-then-sell cycle.

### Diagram
```mermaid
%%{init: {'theme': 'base', 'themeVariables': { 'primaryColor': '#f0f0f0', 'edgeLabelBackground':'#f0f0f0', 'clusterBkg': '#f0f0f0'}}}%%
graph TD
    subgraph "1. Startup"
        A[Start with 'bybit' or 'gate' arg] --> B{RunManualConvergentTrader};
        B --> C[Instantiate IExchange];
        C --> D[Instantiate ConvergentTrader];
    end

    subgraph "2. Trading Cycle (ConvergentTrader)"
        D --> E{StartAsync};
        E --> F[Cancel Open Orders];
        F --> G[Start TrailingTrader for BUY];
        G --> H{OnOrderFilled (BUY)};
        H --> I[Wait for Balance Update];
        I --> J[Wait 5 seconds];
        J --> K[Place Market SELL Order];
        K --> L{Assume Sell Fill};
        L --> M[Cycle Complete];
    end

    style G fill:#cde,stroke:#333,stroke-width:2px
    style K fill:#f99,stroke:#333,stroke-width:2px
```

### 2.1. Entry Point & Control Flow

*   **[`Host/Program.cs:18`](trader/src/Host/Program.cs:18)**: The application's entry point. When started with `bybit` or `gate` arguments, it executes the `RunManualConvergentTrader()` method.
*   **`RunManualConvergentTrader()`**:
    1.  Reads the relevant exchange configuration from `appsettings.json`.
    2.  Instantiates the correct `IExchange` implementation (`BybitExchange` or `GateIoExchange`).
    3.  Injects the exchange adapter into a new `ConvergentTrader` instance.
    4.  Starts the `ConvergentTrader` with parameters (symbol, amount) from the configuration.

### 2.2. Core Components

The `ConvergentTrader` is the heart of the current strategy.

*   **[`Core/ConvergentTrader.cs`](trader/src/Core/ConvergentTrader.cs)**:
    *   **Responsibility:** Manages the entire buy-wait-sell cycle on a single exchange.
    *   **State Flow:**
        1.  **Start:** Cancels any open orders for the target symbol and subscribes to balance updates. It then uses a `TrailingTrader` to execute the initial "buy" order.
        2.  **Handle Buy Fill:** Once the buy order is filled, it waits for a WebSocket balance update to confirm the asset is available in the account.
            *   **Debounce Logic:** Uses a **150ms debounce timer** to ensure the balance update is stable and final before proceeding.
        3.  **Wait and Sell:** After a configurable delay (default: **5 seconds**), it places a market "sell" order for the entire acquired quantity of the asset.
        4.  **Complete:** Assumes the market sell order fills immediately and completes the cycle.

*   **[`Core/IExchange.cs`](trader/src/Core/IExchange.cs)**:
    *   **Responsibility:** A key abstraction (Adapter pattern) that defines a universal contract for all exchange-specific implementations. It allows `ConvergentTrader` to interact with different exchanges without knowing their specific API details.

*   **`Exchanges/`**:
    *   **Responsibility:** This directory contains the concrete implementations of the `IExchange` interface (e.g., `BybitExchange`, `GateIoExchange`). This layer isolates the core logic from third-party libraries and exchange-specific protocols.

### 2.3. Configuration & Dependency Injection
*   **[`Core/Configuration/TradingSettings.cs`](trader/src/Core/Configuration/TradingSettings.cs)**:
    *   **Responsibility:** Defines the strongly-typed configuration object for trading parameters (e.g., `SpreadThreshold`, `SellDelayMilliseconds`).
    *   **Usage:** Mapped from the `TradingSettings` section in `appsettings.json`.

*   **Dependency Injection (DI)**:
    *   A minimal `ServiceCollection` is used in `Program.cs` to register configuration services.
    *   **`IOptionsMonitor<TradingSettings>`** is injected into `ConvergentTrader` and `TrailingTrader`, enabling hot-reload of parameters without restarting the application.

### 2.4. Architectural Patterns

*   **Command Pattern:** The `ConvergentTrader` can be seen as a command that executes a predefined trading sequence.
*   **Adapter Pattern:** The `IExchange` interface decouples the core logic from the exchange implementations.
*   **Dependency Injection:** The `IExchange` and `IOptionsMonitor` instances are injected into the `ConvergentTrader` at runtime.

---

## 3. Legacy Architecture: Spread Listener (Passive)
 
 **STATUS: DEPRECATED & PARTIALLY REMOVED**
 
 This section describes the initial implementation of the `Trader` project.
 
 - **SpreadListener**: Still exists in codebase (fixed by PROPOSAL 001), but not wired in `Program.cs`.
 - **DecisionMaker**: **REMOVED** from codebase.
 
 ### 3.1. Overview
 
 The legacy system was designed to listen for arbitrage opportunities between two exchanges.
 
 ### 3.2. Entry Point & Control Flow
 
 *   **[`Host/Program.cs`](trader/src/Host/Program.cs)**: Legacy setup code has been **removed**. The application now displays a usage message if no arguments are provided.
 
 ### 3.3. Core Components
 
 *   **[`Core/SpreadListener.cs`](trader/src/Core/SpreadListener.cs)**:
     *   **Status:** Exists, hardened against stale data (PROPOSAL 001).
     *   **Responsibility:** Connects to the `SpreadAggregator` WebSocket server.
     *   **Stale Data Protection:**
         *   Tracks `(Price, Timestamp)` tuples for each exchange.
         *   Enforces a strict **7-second maximum data age** (`MaxDataAge`). If data from either exchange is older than 7 seconds, spread calculation is skipped to prevent phantom opportunities.
         *   Validates WebSocket connection state *before* processing.
         *   Invalidates all cached data immediately upon disconnection.
 
 *   **[`Core/DecisionMaker.cs`](trader/src/Core/DecisionMaker.cs)**:
     *   **Status:** ❌ **REMOVED**
     *   **Reason:** Deprecated two-legged arbitrage logic.

## 4. External Dependencies

*   **`appsettings.json`:** Contains all external configuration, including the `SpreadListenerUrl` for the legacy mode and the `ExchangeConfigs` (API keys, secrets, trading amounts) for the active `ConvergentTrader` mode.
*   **Exchange Libraries:** `Bybit.Net`, `GateIo.Net`, etc., are used within the `Exchanges` layer.
*   **`SpreadAggregator` Project:** The legacy `SpreadListener` mode depends on the WebSocket server provided by the `SpreadAggregator` (from the `collections` project). The active `ConvergentTrader` mode has **no dependency** on this.
