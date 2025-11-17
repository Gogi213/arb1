# Trader Project: Detailed Process Flow
**Version:** 2.0 (Validated on 2025-11-17)

This document describes the detailed, step-by-step data and execution flow of the `Trader` application, reflecting the complete, stateful trading logic.

## Step 1: Application Startup (`Program.cs`)

1.  **Configuration:** The application loads its settings from `appsettings.json`, primarily the `SpreadListenerUrl`.
2.  **Initialization:** It instantiates the `SpreadListener` and the `DecisionMaker`.
3.  **Subscription:** The `DecisionMaker` subscribes to the `OnProfitableSpreadDetected` event from the `SpreadListener`.
4.  **Execution:** The main application loop is started by calling `listener.StartAsync()`.

## Step 2: Spread Detection (`SpreadListener`)

1.  **Connection:** `SpreadListener` establishes a WebSocket connection to the `SpreadAggregator` (`Collections` project).
2.  **Data Reception:** It receives real-time spread data.
3.  **Event Trigger:** If a spread exceeds a predefined threshold (e.g., 0.25%), `SpreadListener` fires the `OnProfitableSpreadDetected` event, providing the arbitrage direction (e.g., `GateIo_To_Bybit`).

## Step 3: Trade Initiation (`DecisionMaker`)

1.  **Event Handling:** The `DecisionMaker`'s `HandleProfitableSpread` method is triggered by the event.
2.  **State Lock:** It checks an `_isCycleInProgress` flag. If a trade is already active, a log message is written and the new signal is ignored. If not, the flag is set to `true`.
3.  **Process Halts:** The `DecisionMaker` logs that a profitable spread was detected. **The current implementation stops here.** The code to start a trade is a `//TODO` placeholder.

## Step 4: Intended Arbitrage Cycle (`ArbitrageTrader`)

The following steps describe the **intended future implementation** of the trade cycle, which is not yet active in the code.

This is the core of the application, a complex, asynchronous state machine that executes the two-legged trade.

1.  **Leg 1: Trailing Buy Order:**
    *   The `ArbitrageTrader` starts a `TrailingTrader`, which begins placing and modifying a "buy" order on the first exchange, chasing the price to get the best possible entry.

2.  **State: Waiting for Buy Fill:**
    *   The `ArbitrageTrader` awaits the `OnOrderFilled` event from the `TrailingTrader`.
    *   When the buy order is filled, the `HandleBuyOrderFilled` method is invoked.

3.  **State: Waiting for Balance Confirmation:**
    *   **Crucial Step:** Instead of immediately selling, the system waits for the purchased asset to be reflected in the exchange's balance.
    *   It subscribes to balance updates and uses a `TaskCompletionSource` (`_baseAssetBalanceTcs`) as a signal.
    *   A **debounce timer** is used: the system waits for a brief period of stability (e.g., 150ms) in balance updates before it considers the balance confirmed. This handles cases where the exchange sends multiple, fluctuating balance messages.

4.  **Leg 2: Market Sell Order:**
    *   Once the `_baseAssetBalanceTcs` is completed, the `ArbitrageTrader` knows the exact quantity of the asset it holds.
    *   It immediately places a market "sell" order for that precise quantity on the second exchange, using a low-latency WebSocket command.

5.  **State: Waiting for Sell Confirmation:**
    *   The `ArbitrageTrader` now awaits a WebSocket message from the second exchange confirming that the sell order has been filled.
    *   The `HandleSellOrderUpdate` method processes this confirmation.

6.  **State: Cycle Completion:**
    *   Upon sell confirmation, the cycle is complete.
    *   The trader logs critical performance metrics, including:
        *   **End-to-End Server Latency:** Time from the buy order fill timestamp (server-side) to the sell order fill timestamp (server-side).
        *   **End-to-End Local Latency:** Time from the local handling of the buy fill to the local handling of the sell confirmation.
    *   It calculates the final proceeds (`USDT` received) from the sale.
    *   It calls `CleanupAndSignalCompletionAsync`, which unsubscribes from all streams and sets the result on the main `_arbitrageCycleTcs`, allowing the `DecisionMaker` to finally complete the `await` and reset its `_isCycleInProgress` flag.
