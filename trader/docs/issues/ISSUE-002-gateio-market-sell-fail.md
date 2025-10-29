# ISSUE-002: Сбой размещения рыночного ордера на продажу в Leg 2 (Gate.io)

**Дата:** 2025-10-28

## 1. Описание проблемы

После успешного исполнения ордера на покупку на Bybit в рамках `Leg 2`, `ReverseArbitrageTrader` пытается разместить рыночный ордер на продажу на Gate.io. Эта операция завершается неудачей.

**Наблюдаемое поведение (из `logs.txt`):**
1.  `[Y4] FILL Phase` успешно завершается.
2.  Начинается `[Y5] MARKET Phase`.
3.  Вызывается `_gateIoExchange.PlaceOrderAsync`.
4.  Логируется сообщение: `[Y5] FAILED to place market sell order on GateIoExchange.`

Это указывает на то, что вызов `_gateIoExchange.PlaceOrderAsync` возвращает `null` или генерирует исключение, которое обрабатывается, но не позволяет получить `orderId`.

## 2. Предварительный анализ

- **Проблема в `GateIoExchange`:** Логика `ReverseArbitrageTrader` для вызова продажи выглядит корректной и аналогична работающей логике в `ArbitrageTrader` для `Leg 1`. Проблема, скорее всего, находится внутри реализации `GateIoExchange.PlaceOrderAsync`.
- **Точная причина:** Логи показали, что Gate.io возвращает ошибку `Your order size 0 H is too small`. Это происходит потому, что `BybitOrderAdapter` для свойства `Quantity` использует поле `_order.Quantity` (изначальное количество в ордере), а не `_order.CumulativeQuantityFilled` (фактически исполненное количество). Когда ордер исполняется, `Quantity` может быть равно 0, в то время как `CumulativeQuantityFilled` содержит реальное значение.

## 3. Статус

- **Статус:** `Closed`
- **Приоритет:** `High`
- **Блокирует:** Завершение `Leg 2`.