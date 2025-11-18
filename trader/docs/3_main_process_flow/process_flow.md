# Trader Project: Detailed Process Flow
**Version:** 4.0 (Validated on 2025-11-18)

This document describes the detailed, step-by-step data and execution flow of the `Trader` application. It covers both the **Legacy Two-Legged Arbitrage MVP Process Flow** and the **Current Convergent Trader MVP Process Flow**.

## 1. Legacy Process Flow (Deprecated MVP)

This section outlines the intended (but not fully realized) process flow for the initial two-legged arbitrage strategy. While the code components still exist, this flow is **no longer active** in the `Trader` application.

### Step 1: Application Startup (`Program.cs` - Legacy Context)

1.  **Conditional Execution:** In scenarios where the `ConvergentTrader` is not explicitly invoked via command-line arguments, `Program.cs` proceeds with the setup for the legacy components.
2.  **Configuration:** The application loaded settings from `appsettings.json`, primarily the `SpreadListenerUrl`.
3.  **Initialization:** It instantiated the `SpreadListener` and the `DecisionMaker`.
4.  **Subscription:** The `DecisionMaker` subscribed to the `OnProfitableSpreadDetected` event from the `SpreadListener`.
5.  **Execution:** The `SpreadListener` was started by calling `listener.StartAsync()`, making it an active component in this execution path.

### Step 2: Spread Detection (`SpreadListener` - Legacy Context)

1.  **Connection:** `SpreadListener` establishes a WebSocket connection to the `SpreadAggregator` (`Collections` project). **This component is still instantiated and started if the `ConvergentTrader` is not invoked via command-line arguments.**
2.  **Data Reception:** It receives real-time spread data.
3.  **Event Trigger:** If a spread exceeds a predefined threshold (e.g., 0.25%), `SpreadListener` fired the `OnProfitableSpreadDetected` event, providing the arbitrage direction (e.g., `GateIo_To_Bybit`).

### Step 3: Trade Initiation (`DecisionMaker` - Legacy Context)

1.  **Event Handling:** The `DecisionMaker`'s `HandleProfitableSpread` method was triggered by the event.
2.  **State Lock:** It checked an `_isCycleInProgress` flag. If a trade was already active, the new signal was ignored. If not, the flag was set to `true`.
3.  **Process Halts (Legacy):** The `DecisionMaker` logged that a profitable spread was detected. **This component still receives events from the `SpreadListener` but performs no trading actions.** The code to start a trade was a `//TODO` placeholder and was never fully integrated.

### Step 4: Intended Arbitrage Cycle (`ArbitrageTrader` - Legacy Concept)

The following steps describe the **intended, future implementation** of the two-legged trade cycle, which was never made active in the code. This was envisioned as a complex, asynchronous state machine.

1.  **Leg 1: Trailing Buy Order:**
    *   The `ArbitrageTrader` would start a `TrailingTrader`, placing and modifying a "buy" order on the first exchange.

2.  **State: Waiting for Buy Fill:**
    *   The `ArbitrageTrader` would await the `OnOrderFilled` event.

3.  **State: Waiting for Balance Confirmation:**
    *   The system would wait for the purchased asset to be reflected in the exchange's balance using a `TaskCompletionSource` (`_baseAssetBalanceTcs`) and a debounce timer to ensure balance stability.

4.  **Leg 2: Market Sell Order:**
    *   Once the balance was confirmed, the `ArbitrageTrader` would place an immediate market "sell" order for the precise quantity on the second exchange.

5.  **State: Waiting for Sell Confirmation:**
    *   The `ArbitrageTrader` would await a WebSocket message confirming the sell order fill.

6.  **State: Cycle Completion:**
    *   Upon sell confirmation, the cycle would be complete. Performance metrics would be logged, proceeds calculated, and cleanup (`CleanupAndSignalCompletionAsync`) performed.

## 2. Current Process Flow (Convergent Trader MVP)

This section details the currently active and operational process flow within the `Trader` application, driven by the `ConvergentTrader`. This is a simplified, single-exchange trading strategy.

### Step 1: Application Startup (`Program.cs` - Current Context)

1.  **Configuration:** The application loads its settings from `appsettings.json`, including API keys, trading parameters (symbol, quantity, sell delay), and exchange-specific configurations.
2.  **Exchange Initialization:** An instance of the chosen `IExchange` implementation (e.g., `BybitExchange`) is created based on configuration.
3.  **`ConvergentTrader` Initialization:** A `ConvergentTrader` instance is created, injected with the initialized `IExchange` and trading parameters.
4.  **Execution:** The `Program.cs` now directly calls a method like `RunManualConvergentTrader()` which orchestrates the `ConvergentTrader`'s execution loop.

### Step 2: Trading Cycle (`ConvergentTrader`)

The `ConvergentTrader` manages a complete buy-then-sell cycle on a single exchange.

1.  **Initialize & Cancel Orders:**
    *   Upon startup, the `ConvergentTrader` cancels all existing open orders for the specified symbol to ensure a clean state.
2.  **Initiate Trailing Buy:**
    *   The `ConvergentTrader` delegates the initial buy order placement to an internal `TrailingTrader`. This means the buy order will dynamically chase the market price to get an optimal entry.
3.  **Handle Buy Fill:**
    *   Upon receiving confirmation that the buy order has been filled, the `ConvergentTrader` records the exact purchased quantity and the average entry price.
4.  **Wait for Balance Update and Confirmation:**
    *   Crucially, after a buy fill, the system explicitly waits for a balance update to confirm the acquired asset is available. It uses a `TaskCompletionSource` (`_baseAssetBalanceTcs`) and a debounce timer (150ms) similar to the legacy architecture for robustness.
5.  **Fixed Delay Before Sell:**
    *   After balance confirmation, the system waits for a precise **5 seconds** (`Task.Delay(5000);`) before proceeding to the sell phase.
6.  **Initiate Market Sell:**
    *   After the delay, the `ConvergentTrader` places a market sell order for the *entire confirmed available quantity* on the *same exchange*.
7.  **Assume Sell Fill & Estimate Proceeds:**
    *   The current implementation includes a `Task.Delay(1000);` after placing the market sell, *assuming* the market sell fills almost immediately.
    *   A rough estimate of the proceeds is calculated internally (`sellQuantity * 0.98m`) rather than from actual exchange fill details. This is a placeholder for actual P&L calculation.
8.  **Loop or Stop:**
    *   Depending on the application's overall configuration (`Program.cs` logic), the `ConvergentTrader` may either repeat this cycle (initiating a new buy order) or terminate its operation.

This current process flow is significantly simpler, reducing external dependencies and inter-exchange complexities, making it a more stable and manageable MVP.

