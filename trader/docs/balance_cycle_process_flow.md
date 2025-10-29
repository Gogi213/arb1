# Диаграмма последовательности: Цикл баланса активов

Эта диаграмма описывает полный цикл движения средств в рамках одной арбитражной сделки и последующей ручной ребалансировки для подготовки к следующему циклу.

- **Quote Asset:** `USDT` (используется для покупки и как мера прибыли)
- **Base Asset:** `H` (арбитражный актив)
- **Exchange A:** Биржа, где происходит покупка (например, Gate.io для Leg 1).
- **Exchange B:** Биржа, где происходит продажа (например, Bybit для Leg 1).

```mermaid
sequenceDiagram
    participant Bot as "TraderBot"
    participant ExA as "Счет на Exchange A"
    participant ExB as "Счет на Exchange B"
    actor User as "Оператор"

    Note over ExA, ExB: Начальное состояние: <br/> ExA: есть Quote Asset (USDT) <br/> ExB: есть Base Asset (H)

    Bot->>ExA: 1. Разместить ордер на ПОКУПКУ Base Asset
    ExA-->>ExA: Обновление баланса: <br/> - Quote Asset (USDT) <br/> + Base Asset (H)
    ExA-->>Bot: Уведомление о выполнении ордера

    Bot->>ExB: 2. Разместить ордер на ПРОДАЖУ Base Asset
    ExB-->>ExB: Обновление баланса: <br/> - Base Asset (H) <br/> + Quote Asset (USDT)
    ExB-->>Bot: Уведомление о выполнении ордера

    Bot->>Bot: 3. Рассчитать PnL и дисбаланс активов

    Note over ExA, ExB: Конечное состояние (до ребалансировки): <br/> ExA: стало больше Base Asset <br/> ExB: стало больше Quote Asset

    par Ручная ребалансировка
        User->>ExA: 4a. Вывод Base Asset (H)
        User->>ExB: 4b. Ввод Base Asset (H)
    and
        User->>ExB: 5a. Вывод Quote Asset (USDT)
        User->>ExA: 5b. Ввод Quote Asset (USDT)
    end

    Note over ExA, ExB: Финальное состояние: <br/> Балансы восстановлены для нового цикла. <br/> Чистая прибыль зафиксирована в Quote Asset.