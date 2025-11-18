# Ключевые структуры данных (метрики) в проекте Collections

Этот документ описывает основные структуры данных, которые используются для сбора, обработки и сохранения рыночной информации в приложении `SpreadAggregator`.

## 1. `MarketData`

**Файл:** [`MarketData.cs`](collections/src/SpreadAggregator.Domain/Entities/MarketData.cs)

**Описание:**
Это базовая структура, представляющая собой "сырой" тик данных, полученный от биржи. Она может содержать либо информацию о лучшей цене покупки/продажи (тикер), либо информацию о последней сделке.

**Ключевые поля:**
- `Exchange` (string): Название биржи (например, "Bybit", "Binance").
- `Symbol` (string): Нормализованное имя символа (например, "BTC_USDT").
- `BestBid` (decimal?): Лучшая цена покупки.
- `BestAsk` (decimal?): Лучшая цена продажи.
- `LastPrice` (decimal?): Цена последней сделки.
- `ServerTimestamp` (DateTime?): Временная метка события от сервера биржи. **Критически важна для HFT-анализа.**
- `Timestamp` (DateTime): Локальная временная метка получения события. Используется как fallback.

**Использование:**
- Является основным типом данных, передаваемым от `IExchangeClient` в `OrchestrationService`.
- Записывается в `RawDataChannel` и `RollingWindowChannel` для дальнейшей обработки.

## 2. `SpreadData`

**Файл:** [`SpreadData.cs`](collections/src/SpreadAggregator.Domain/Entities/SpreadData.cs)

**Описание:**
Обогащенная версия `MarketData`, которая содержит рассчитанный спред и дополнительную метаинформацию. Эта структура используется для трансляции данных клиентам через WebSocket.

**Ключевые поля:**
- `Exchange` (string): Название биржи.
- `Symbol` (string): Нормализованное имя символа.
- `BestBid` (decimal): Лучшая цена покупки.
- `BestAsk` (decimal): Лучшая цена продажи.
- `SpreadPercentage` (decimal): Рассчитанный процент спреда. Вычисляется в `SpreadCalculator`.
- `MinVolume` / `MaxVolume` (decimal): Пороги фильтрации по объему, примененные к этому символу.
- `ServerTimestamp` (DateTime?): Временная метка от сервера.
- `Timestamp` (DateTime): Локальная временная метка.

**Использование:**
- Создается в `OrchestrationService` после получения `MarketData`.
- Сериализуется в JSON и отправляется клиентам через `FleckWebSocketServer`.
- Логируется в текстовые файлы с помощью `BidAskLogger`.

## 3. `TradeData`

**Файл:** [`TradeData.cs`](collections/src/SpreadAggregator.Domain/Entities/TradeData.cs)

**Описание:**
Представляет информацию о единичной сделке, прошедшей на бирже.

**Ключевые поля:**
- `Exchange` (string): Название биржи.
- `Symbol` (string): Нормализованное имя символа.
- `Price` (decimal): Цена сделки.
- `Quantity` (decimal): Объем сделки.
- `Side` (OrderSide): Направление сделки (Buy/Sell).
- `Timestamp` (DateTime): Временная метка сделки от сервера.

**Использование:**
- Получается от `IExchangeClient` при подписке на поток сделок.
- Транслируется через WebSocket аналогично `SpreadData`.
- Записывается в каналы для последующего сохранения.

## 4. `WebSocketMessage`

**Файл:** [`WebSocketMessage.cs`](collections/src/SpreadAggregator.Domain/Entities/WebSocketMessage.cs)

**Описание:**
Это объект-обертка, используемый для отправки данных через WebSocket. Он позволяет клиенту легко определить тип полученных данных.

**Ключевые поля:**
- `MessageType` (string): Тип сообщения, например, "Spread" или "Trade".
- `Payload` (object): Объект с данными (`SpreadData` или `TradeData`).

**Использование:**
- Создается в `OrchestrationService` непосредственно перед отправкой данных через WebSocket.
- Позволяет клиенту на JavaScript легко обрабатывать разные типы сообщений с помощью `switch (message.messageType)`.
