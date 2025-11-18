# Ключевые метрики и состояния в проекте Trader

Этот документ описывает основные состояния и события, которые отслеживаются и логируются в приложении `Trader`, особенно в рамках активной стратегии `ConvergentTrader`.

## 1. Состояния цикла `ConvergentTrader`

`ConvergentTrader` представляет собой конечный автомат. Ключевые состояния и переходы между ними логируются для отладки и анализа.

### 1.1. `ArbitrageCycleState`
**Файл:** [`ArbitrageCycleState.cs`](trader/src/Core/ArbitrageCycleState.cs)

Это класс, который отслеживает состояние одного полного торгового цикла.

**Ключевые поля (метрики):**
- `CurrentState` (Enum): Текущее состояние цикла (например, `WaitingForBuyOrderFill`, `WaitingForSellOrderFill`).
- `EntryPrice` (decimal): Средняя цена исполненного ордера на покупку.
- `ExitPrice` (decimal): Средняя цена исполненного ордера на продажу.
- `BuyOrderQuantity` (decimal): Объем исполненного ордера на покупку.
- `SellOrderQuantity` (decimal): Объем исполненного ордера на продажу.
- `InitialBalance` (decimal): Баланс котируемой валюты (например, USDT) до начала цикла.
- `FinalBalance` (decimal): Баланс котируемой валюты после завершения цикла.
- `Profit` (decimal): Расчетная прибыль (`FinalBalance - InitialBalance`).

### 1.2. Логируемые события
**Файл:** [`FileLogger.cs`](trader/src/Core/FileLogger.cs)

Все важные события в жизненном цикле трейдера записываются в текстовые логи. Эти логи являются основным источником данных для анализа производительности и отладки.

**Примеры ключевых сообщений:**
- `Initializing {ExchangeName} trader...`
- `Starting manual ConvergentTrader with amount: {amount} for {symbol}`
- `Placing trailing BUY order...`
- `BUY order filled. Avg price: {price}, Quantity: {qty}`
- `Waiting for balance update...`
- `Balance confirmed. Waiting 5s before sell.`
- `Placing market SELL order for quantity: {qty}`
- `SELL order assumed filled. Estimated proceeds: {proceeds}`
- `ConvergentTrader completed with result: {result}`

## 2. Метрики `SpreadListener` (Legacy)

Эти метрики относятся к устаревшей части системы, которая только слушает, но не торгует.

### 2.1. `OnProfitableSpreadDetected`
**Файл:** [`SpreadListener.cs`](trader/src/Core/SpreadListener.cs)

Это событие генерируется, когда `SpreadListener` обнаруживает спред, превышающий заданный порог.

**Ключевые данные события:**
- `SpreadData` (объект): Содержит полную информацию о спреде, включая:
  - `Exchange`
  - `Symbol`
  - `BestBid`, `BestAsk`
  - `SpreadPercentage`

### 2.2. Логируемые события `DecisionMaker`
**Файл:** [`DecisionMaker.cs`](trader/src/Core/DecisionMaker.cs)

`DecisionMaker` логирует обнаружение прибыльного спреда, но не предпринимает никаких действий.

**Ключевое сообщение:**
- `Profitable spread detected: {e.Exchange1} -> {e.Exchange2} for {e.Symbol}. Spread: {e.SpreadPercentage:F4}%`

Эти логи подтверждают, что система видит возможности, даже если не торгует по ним.
