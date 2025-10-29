# Диаграмма последовательности: Сбор метрик в цикле

Эта диаграмма показывает, в какие моменты времени в рамках одного арбитражного цикла собираются ключевые метрики производительности и финансового результата.

```mermaid
sequenceDiagram
    participant Bot as "TraderBot"
    participant ExA as "Exchange A (Покупка)"
    participant ExB as "Exchange B (Продажа)"

    Bot->>ExA: 1. Отправка ордера на ПОКУПКУ
    Note right of Bot: T0 = UtcNow

    ExA-->>Bot: 2. Уведомление о ПОЛНОМ ИСПОЛНЕНИИ ордера
    Note right of Bot: T1 = UtcNow <br/> Metric: `Leg1_Fill_Latency` = T1 - T0 <br/> Metric: `Buy_Price` (средняя цена покупки)
    
    ExA-->>Bot: 2a. Уведомления о балансе (зачисление, списание комиссии)
    Bot->>Bot: 2b. Ожидание стабилизации баланса (debouncing)
    Note right of Bot: T_stable = UtcNow <br/> Metric: `Balance_Stabilization_Latency` = T_stable - T1 <br/> Metric: `Buy_Quantity` (финальный объем после комиссии)

    Bot->>ExB: 3. Отправка ордера на ПРОДАЖУ
    Note right of Bot: T2 = UtcNow <br/> Metric: `Inter_Exchange_Latency` = T2 - T1

    ExB-->>Bot: 4. Уведомление о ПОЛНОМ ИСПОЛНЕНИИ ордера
    Note right of Bot: T3 = UtcNow <br/> Metric: `Leg2_Latency` = T3 - T2 <br/> Metric: `Sell_Price` (средняя цена продажи) <br/> Metric: `Sell_Quantity` (исполненный объем)

    Bot->>Bot: 5. Финальный расчет метрик
    Note right of Bot: Metric: `End_to_End_Latency` = T3 - T0 <br/> Metric: `PnL_Quote` = (Sell_Price * Sell_Quantity) - (Buy_Price * Buy_Quantity) <br/> Metric: `Slippage_Buy` = Buy_Price - Target_Buy_Price <br/> Metric: `Slippage_Sell` = Target_Sell_Price - Sell_Price <br/> Metric: `Asset_Imbalance` = Buy_Quantity - Sell_Quantity