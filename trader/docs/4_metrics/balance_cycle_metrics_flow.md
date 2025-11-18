# **УСТАРЕВШИЙ ДОКУМЕНТ**

**ВНИМАНИЕ:** Этот документ и диаграмма описывают сбор метрик для устаревшей логики двухэтапного арбитража (`two-legged arbitrage`), которая больше не используется в проекте.

Актуальное и корректное описание текущих метрик находится в файле [**metrics.md**](./metrics.md).

---

# Диаграмма последовательности: Сбор метрик в полном цикле

Эта диаграмма показывает, в какие моменты времени в рамках полного двухлегового арбитражного цикла собираются ключевые метрики.

```mermaid
sequenceDiagram
    participant Orchestrator as "Orchestrator"
    participant Leg1 as "Leg 1 (Gate.io -> Bybit)"
    participant Leg2 as "Leg 2 (Bybit -> Gate.io)"
    participant Gate as "Gate.io"
    participant Bybit as "Bybit"
    participant State as "ArbitrageCycleState"

    Orchestrator->>Gate: Get Initial H Balance
    Gate-->>Orchestrator: 0.04341 H
    Orchestrator->>State: `InitialGateIoBaseAssetBalance` = 0.04341
    
    Orchestrator->>Leg1: Start(amount, state)
    Leg1->>Gate: 1. Place BUY Order (Trailing)
    Note right of Leg1: T0_L1 (start trailing)
    
    Gate-->>Leg1: 2. Order FILLED
    Note right of Leg1: T1_L1 (fill time) <br/> `Buy_Price_L1` <br/> `Buy_Quantity_L1` (e.g., 21.48)
    Leg1->>State: `Leg1GateBuyFilledQuantity` = 21.48
    
    Leg1->>Bybit: 3. Place SELL Order
    Note right of Leg1: T2_L1 (sell order placed) <br/> Quantity is from balance (e.g., 22)
    
    Bybit-->>Leg1: 4. Order FILLED
    Note right of Leg1: T3_L1 (fill time) <br/> `Sell_Price_L1` <br/> `USDT_Proceeds` (e.g., 6.1336)
    
    Leg1-->>Orchestrator: Return `USDT_Proceeds`
    
    Orchestrator->>Leg2: Start(USDT_Proceeds, state)
    Leg2->>Bybit: 5. Place BUY Order (Trailing)
    Note right of Leg2: T0_L2 (start trailing)
    
    Bybit-->>Leg2: 6. Order FILLED
    Note right of Leg2: T1_L2 (fill time) <br/> `Buy_Price_L2` <br/> `Buy_Quantity_L2`
    
    Leg2->>State: Read `Leg1GateBuyFilledQuantity` (21.48)
    Leg2->>Gate: 7. Place SELL Order (Quantity=21.48)
    Note right of Leg2: T2_L2 (sell order placed)
    
    Gate-->>Leg2: 8. Order FILLED
    Note right of Leg2: T3_L2 (fill time) <br/> `Sell_Price_L2`
    
    Leg2-->>Orchestrator: Return Final Result
    
    Orchestrator->>Orchestrator: 9. Final PnL Calculation
    Note right of Orchestrator: `Final_USDT` - `Initial_USDT`
```

### Ключевые метрики и их сбор

-   **`Leg1_Gate_Buy_Quantity`**: `Leg1GateBuyFilledQuantity`
    -   **Источник:** Прямо из исполненного ордера на покупку на Gate.io.
    -   **Назначение:** Сохраняется в `ArbitrageCycleState` и является **ключевым значением** для ордера на продажу в `Leg 2`.

-   **`Leg1_Bybit_Sell_Quantity`**:
    -   **Источник:** Из события обновления баланса на Gate.io (менее надежно).
    -   **Проблема:** Может включать "пыль" и не совпадать с `Leg1_Gate_Buy_Quantity`.

-   **`Leg2_Gate_Sell_Quantity`**:
    -   **Источник:** `ArbitrageCycleState.Leg1GateBuyFilledQuantity`.
    -   **Результат:** Гарантирует, что в конце цикла продается ровно столько же, сколько было куплено в начале.

-   **Сквозная задержка (End-to-End Latency):**
    -   **Leg 1:** `T3_L1 - T1_L1` (от фиксации покупки на Gate.io до фиксации продажи на Bybit).
    -   **Leg 2:** `T3_L2 - T1_L2` (от фиксации покупки на Bybit до фиксации продажи на Gate.io).