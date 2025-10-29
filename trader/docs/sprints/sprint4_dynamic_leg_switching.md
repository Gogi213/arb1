# Sprint 4: Dynamic Leg Switching

## Goal
Implement a "supervisor" or "dispatcher" component that dynamically decides which arbitrage leg to execute based on real-time market conditions. This will allow the bot to capitalize on opportunities in both directions (Leg 1 and Leg 2) without manual reconfiguration.

## Key Tasks
1.  **Create a Supervisor/Dispatcher:**
    - This component will subscribe to order book updates from both Gate.io and Bybit.
    - It will continuously calculate the potential spread for both `Leg 1` (Gate -> Bybit) and `Leg 2` (Bybit -> Gate).

2.  **Implement Decision Logic:**
    - When a profitable spread is detected for either leg, the supervisor will trigger the execution of the corresponding `ITrader` (`ArbitrageTrader` or `ReverseArbitrageTrader`).

3.  **Refactor Host Application:**
    - Modify the application's startup logic so that `ITrader` instances are no longer started automatically. Instead, they will be instantiated and run on-demand by the new supervisor.