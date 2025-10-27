# Журнал изменений

## Задача: Предоставить инструкции по сборке и запуску проектов

- [ ] Определить запускаемые проекты в репозитории.
- [x] Предоставить команды для сборки и запуска проекта `TraderBot.Host`.
- [x] Предоставить команды для сборки и запуска проекта `SpreadAggregator.Presentation`.
- [x] Предоставить команды для сборки и запуска проекта `analyzer`.

---

## Разработка: Two-Leg Arbitrage System

### LEG 1 (X1-X7): Gate.io BUY → Bybit SELL
- [x] X1 INIT - Инициализация, подписка на события
- [x] X2 SETUP - Получение фильтров, отмена ордеров
- [x] X3 TRAIL - Gate.io trailing limit order
- [x] X4 FILL - Детекция исполнения Gate.io ордера
- [x] X5 MARKET - Размещение market SELL на Bybit
- [x] X6 CONFIRM - Подтверждение исполнения Bybit ордера
- [x] X7 CLEANUP - Остановка trailing, отписка от событий

**Статус LEG 1**: ✅ Имплементация завершена, но обнаружена **критическая проблема**

### LEG 2 (Y1-Y7): Bybit BUY → Gate.io SELL
- [x] Y1 INIT - Инициализация ReverseArbitrageTrader
- [x] Y2 SETUP - Подключение Bybit WS, получение фильтров
- [x] Y3 TRAIL - Bybit trailing limit order через BybitTrailingTrader
- [x] Y4 FILL - Детекция исполнения Bybit ордера
- [x] Y5 MARKET - Размещение market SELL на Gate.io
- [x] Y6 CONFIRM - Подтверждение исполнения Gate.io ордера
- [x] Y7 CLEANUP - Остановка Bybit trailing, отписка

**Статус LEG 2**: ✅ Код имплементирован корректно, но **не запускается из-за блокировки LEG 1**

---

## 📊 ТЕКУЩЕЕ СОСТОЯНИЕ СИСТЕМЫ

**Последний запуск**: 2025-10-27 19:35:35
**Результат**: LEG 1 выполняется до X5 (market SELL размещен), застревает на X6 (CONFIRM)
**Логи**: `c:\visual projects\arb1\trader\logs\logs.txt` (91 строка)

### Что работает ✅
- ✅ Bybit low-latency WS подключение (trade + public channels)
- ✅ Bybit WS аутентификация
- ✅ Gate.io WS подключение и order updates subscription
- ✅ Gate.io trailing limit order (X3 TRAIL)
- ✅ Gate.io order fill detection (X4 FILL) - WS latency 163ms
- ✅ Bybit market order placement через WS (X5 MARKET) - 227ms latency
- ✅ Bybit order creation confirmation: OrderId=2070792782947290114

### Что НЕ работает ❌
- ❌ **Bybit order FILL confirmation через WebSocket (X6 CONFIRM)**
- ❌ LEG 1 cleanup (X7 CLEANUP)
- ❌ LEG 2 запуск (Y1-Y7)

### Последовательность событий (из logs.txt)
```
Line 74-76: Gate.io BUY order filled (OrderId=946591260752)
Line 84-85: Bybit SELL order created успешно (OrderId=2070792782947290114, retCode=0)
Line 86-87: PlaceOrderAsync took 227ms
Line 90:    "Waiting for WS confirmation..." <- ПРОГРАММА ЗАСТРЯЛА ЗДЕСЬ
Line 91:    "Order created" confirmation received
Line 92+:   НИЧЕГО - нет fill confirmation от Bybit
```

---

## 🔴 КРИТИЧЕСКИЕ ПРОБЛЕМЫ

### P1: LEG 1 (X1-X7) не завершается - блокирует запуск LEG 2
**Приоритет**: CRITICAL
**Статус**: 🔴 БЛОКИРУЕТ ДАЛЬНЕЙШУЮ РАЗРАБОТКУ
**Воспроизводится**: ДА (100% reproducible)

**Описание проблемы**:
LEG 1 успешно проходит фазы X1-X5, но застревает на X6 (CONFIRM):

1. ✅ **X1-X4**: Gate.io BUY trailing работает, order filled
2. ✅ **X5 MARKET**: Bybit market SELL размещен успешно через WS
   - OrderId: `2070792782947290114`
   - retCode: `0` (OK)
   - Latency: `227ms`
3. ✅ **Order created confirmation**: Bybit прислал подтверждение создания ордера
4. ❌ **X6 CONFIRM**: Bybit НИКОГДА не присылает fill confirmation через WS
5. ❌ `ArbitrageTrader.HandleSellOrderUpdate()` не вызывается
6. ❌ `_arbitrageCycleTcs.Task` в `ArbitrageTrader.StartAsync()` ждёт бесконечно
7. ❌ `Program.cs:40` застряла на `await arbitrageTrader.StartAsync()`
8. ❌ LEG 2 никогда не запускается

**Файлы**:
- `c:\visual projects\arb1\trader\src\Core\ArbitrageTrader.cs:125-174` - HandleSellOrderUpdate() ждёт confirmation
- `c:\visual projects\arb1\trader\src\Core\ArbitrageTrader.cs:27-43` - StartAsync() возвращает Task
- `c:\visual projects\arb1\trader\src\Host\Program.cs:38-50` - Sequential execution LEG 1 → LEG 2
- `c:\visual projects\arb1\trader\logs\logs.txt:90-91` - последний лог "Waiting for WS confirmation..."

**Root Cause Analysis**:
Bybit WebSocket присылает TWO типа сообщений:
1. ✅ **order.create response** (op="order.create") - ПРИХОДИТ
2. ❌ **order.update event** (topic="order") - НЕ ПРИХОДИТ

Проблема: после размещения market order через WS, Bybit НЕ присылает order update events с fill confirmation. Market orders исполняются мгновенно, но WS stream не получает updates.

**Возможные причины**:
1. ❌ Bybit не присылает order updates для market orders на Spot
2. ❌ Требуется отдельная подписка на `order` topic (не только auth для order.create)
3. ❌ Market orders исполняются ДО того как WS subscription успевает зарегистрироваться
4. ❌ Order updates приходят в другой WebSocket канал

**Требуется**:
- [x] Подтверждено: Bybit order.create работает (retCode=0, OrderId получен)
- [ ] **СРОЧНО**: Добавить explicit subscription на `order` topic после auth
- [ ] Проверить Bybit docs: требуется ли подписка на order updates для Spot
- [ ] Добавить fallback: REST API query для проверки order status через 500ms
- [ ] Альтернатива: добавить timeout (5s) и считать order filled если timeout
- [ ] Логировать ВСЕ incoming WS messages для debugging

---

## ⚠️ ТЕХНИЧЕСКИЙ ДОЛГ

### TD-1: Balance query не имплементирована через Bybit WS
**Приоритет**: MEDIUM
**Компонент**: ReverseArbitrageTrader (Y2 SETUP)
**Файл**: `c:\visual projects\arb1\trader\src\Exchanges\Bybit\ReverseArbitrageTrader.cs:75`

Текущий код:
```csharp
Console.WriteLine("[Y2] Balance query skipped (not yet implemented via WS)");
```

**Требуется**: Имплементировать запрос баланса через Bybit low-latency WS

---

### TD-2: Hardcoded symbol filters вместо WS query
**Приоритет**: MEDIUM
**Компонент**: ReverseArbitrageTrader (Y2 SETUP)
**Файл**: `c:\visual projects\arb1\trader\src\Exchanges\Bybit\ReverseArbitrageTrader.cs:72-74`

Текущий код:
```csharp
_tickSize = 0.00001m;
_basePrecision = 0;
```

**Требуется**: Получать tickSize и basePrecision через Bybit WS API запрос инструмента

---

### TD-3: Placeholder количество в Y5 MARKET phase
**Приоритет**: HIGH
**Компонент**: ReverseArbitrageTrader (Y5 MARKET)
**Файл**: `c:\visual projects\arb1\trader\src\Exchanges\Bybit\ReverseArbitrageTrader.cs:140`

Текущий код:
```csharp
var sellQuantity = 5m; // TODO Y5: Use actual filled quantity
```

**Требуется**: Использовать реальное количество из `filledOrder.Quantity` вместо hardcoded 5m

---

### TD-4: Bybit trailing stop в cleanup не имплементирован
**Приоритет**: LOW
**Компонент**: ReverseArbitrageTrader (Y7 CLEANUP)
**Файл**: `c:\visual projects\arb1\trader\src\Exchanges\Bybit\ReverseArbitrageTrader.cs:93`

Текущий комментарий:
```csharp
// Stop Bybit trailing
await _bybitTrailingTrader.StopAsync(bybitSymbol);
```

**Требуется**: Убедиться что BybitTrailingTrader.StopAsync() корректно отменяет активные ордера

---

## 📋 СЛЕДУЮЩИЕ ШАГИ

1. **Разблокировать P1** - Исследовать почему Bybit не присылает order updates
2. После разблокировки P1 - Запустить полный цикл LEG 1 → LEG 2
3. Проверить логи на наличие [Y1]-[Y7] фазовых маркеров
4. Закрыть технический долг TD-1, TD-2, TD-3
5. Провести нагрузочное тестирование двуногого арбитража
