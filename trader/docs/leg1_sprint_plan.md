# План спринтов для реализации "Leg 1" (Gate.io -> Bybit)

### Спринт 1: Базовая инфраструктура и подключения

*   `[x]` **Host (`Program.cs`)**: Реализован запуск, чтение конфигурации и создание всех экземпляров.
*   `[x]` **GateIoExchange**: Реализован `InitializeAsync` для подключения.
*   `[x]` **BybitExchange & BybitLowLatencyWs**: Реализована архитектура с 3-мя WebSocket-соединениями, `ConnectAsync` и `AuthenticateAsync`.
*   `[x]` **ArbitrageTrader**: Создан базовый класс и конструктор.

### Спринт 2: Реализация трейлинг-покупки на Gate.io

*   `[x]` **ArbitrageTrader**: Создание `TrailingTrader` реализовано.
*   `[x]` **TrailingTrader**: `StartAsync`, подписки, `CalculateTargetPrice` и основной цикл трейлинга реализованы.
*   `[x]` **GateIoExchange**: `SubscribeToOrderBookUpdatesAsync`, `PlaceOrderAsync`, `ModifyOrderAsync` реализованы и работают.

### Спринт 3: Реализация продажи на Bybit и обработка событий

*   `[x]` **GateIoExchange**: `SubscribeToOrderUpdatesAsync` реализована.
*   `[x]` **TrailingTrader**: `HandleOrderUpdate` и событие `OnOrderFilled` реализованы.
*   `[x]` **ArbitrageTrader**: `HandleBuyOrderFilled` и вызов продажи на Bybit реализованы.
*   `[x]` **BybitExchange & BybitLowLatencyWs**: `PlaceOrderAsync` (Market Sell) и `SubscribeToOrderUpdatesAsync` реализованы и работают.

### Спринт 4: Завершение цикла и очистка

*   `[x]` **ArbitrageTrader**: `HandleSellOrderUpdate` для подтверждения продажи реализован.
*   `[x]` **ArbitrageTrader**: Расчет End-to-End Latency реализован.
*   `[x]` **ArbitrageTrader**: `CleanupAndSignalCompletionAsync` реализован. Основной цикл завершается корректно.
*   `[x]` **Host (`Program.cs`)**: Механизм ожидания (`TaskCompletionSource`) реализован.