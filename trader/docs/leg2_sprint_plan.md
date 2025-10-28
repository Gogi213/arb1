# План спринтов для реализации "Leg 2" (Bybit -> Gate.io)

### Спринт 1: Базовая инфраструктура и подключения

*   `[x]` **Host (`Program.cs`)**: Создание экземпляров `BybitExchange`, `GateIoExchange`, `ReverseArbitrageTrader` реализовано.
*   `[x]` **BybitExchange & BybitLowLatencyWs**: Архитектура с 3-мя сокетами, `ConnectAsync` и `AuthenticateAsync` реализованы.
*   `[x]` **GateIoExchange**: `InitializeAsync` реализован.
*   `[x]` **ReverseArbitrageTrader**: Базовый класс и конструктор созданы.

### Спринт 2: Реализация трейлинг-покупки на Bybit

*   `[x]` **ReverseArbitrageTrader**: Создание `BybitTrailingTrader` реализовано.
*   `[x]` **BybitTrailingTrader**: `StartAsync` и подписки реализованы.
*   `[x]` **BybitTrailingTrader**: `CalculateTargetPriceForBuy` реализована.
*   `[x]` **BybitTrailingTrader**: Основной цикл трейлинга реализован, и проблема с подпиской на стакан решена (реализована логика snapshot/delta).
*   `[x]` **BybitExchange & BybitLowLatencyWs**: Все необходимые методы (`Subscribe...`, `Place...`, `Modify...`) существуют.

### Спринт 3: Реализация продажи на Gate.io и обработка событий

*   `[x]` **BybitExchange & BybitLowLatencyWs**: `SubscribeToOrderUpdatesAsync` реализована.
*   `[x]` **BybitTrailingTrader**: `HandleOrderUpdate` и событие `OnOrderFilled` реализованы.
*   `[x]` **ReverseArbitrageTrader**: `HandleBuyOrderFilled` и вызов продажи на Gate.io реализованы.
*   `[x]` **GateIoExchange**: `PlaceOrderAsync` (Market Sell) реализован.

### Спринт 4: Завершение цикла и очистка

*   `[x]` **GateIoExchange**: `SubscribeToOrderUpdatesAsync` для `ReverseArbitrageTrader` реализована.
*   `[x]` **ReverseArbitrageTrader**: `HandleSellOrderUpdate` для подтверждения продажи реализован.
*   `[x]` **ReverseArbitrageTrader**: Расчет End-to-End Latency реализован.
*   `[-]` **ReverseArbitrageTrader**: `CleanupAndSignalCompletionAsync` реализован, но `BybitTrailingTrader.StopAsync` и `CancelAllOrdersAsync` являются заглушками.
*   `[x]` **Host (`Program.cs`)**: Механизм ожидания (`TaskCompletionSource`) реализован.