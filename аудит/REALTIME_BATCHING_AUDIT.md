# REAL-TIME BATCHING AUDIT

**Дата:** 2025-11-09
**Проблема:** Графики обновляются синхронно (одновременно), несмотря на отправку отдельных WebSocket сообщений

---

## 🔍 ROOT CAUSE ANALYSIS

### Архитектура real-time updates:

```
┌─────────────────────────────────────────────────────────────┐
│ RealTimeController.StreamRealtimeData()                     │
│                                                              │
│ while (webSocket.State == Open) {                          │
│   ┌──────────────────────────────────────────────────┐     │
│   │ opportunities = GetFilteredOpportunities()       │ ← 1 │
│   └──────────────────────────────────────────────────┘     │
│                                                              │
│   foreach (opp in opportunities) {                         │
│     ┌────────────────────────────────────────────────┐     │
│     │ chartData = JoinRealtimeWindows(...)          │ ← 2 │
│     └────────────────────────────────────────────────┘     │
│     ┌────────────────────────────────────────────────┐     │
│     │ SendAsync(chartData)                          │ ← 3 │
│     └────────────────────────────────────────────────┘     │
│   }                                                          │
│   ┌──────────────────────────────────────────────────┐     │
│   │ await Task.Yield()                               │ ← 4 │
│   └──────────────────────────────────────────────────┘     │
│ }                                                            │
└─────────────────────────────────────────────────────────────┘
```

### Проблемы:

#### 1. **GetFilteredOpportunities() - синхронное получение списка**
- Возвращает **весь список** opportunities одновременно
- Кеш на 10 секунд
- Результат: **все opportunities обрабатываются вместе**

```csharp
// OpportunityFilterService.cs:32
public List<Opportunity> GetFilteredOpportunities()
{
    // Returns ALL opportunities at once
    return _cachedOpportunities;
}
```

#### 2. **JoinRealtimeWindows() - синхронное чтение данных**
- Читает из `ConcurrentDictionary<string, RollingWindowData>`
- Данные **уже готовы** - не ждем новых обновлений
- Результат: **все chartData генерируются мгновенно**

```csharp
// RollingWindowService.cs:91
public RealtimeChartData? JoinRealtimeWindows(string symbol, string exchange1, string exchange2)
{
    var window1 = GetWindowData(exchange1, symbol);  // Instant
    var window2 = GetWindowData(exchange2, symbol);  // Instant
    // ... calculations ...
    return chartData;  // Instant
}
```

#### 3. **foreach - синхронная итерация**
- Обрабатывает **все opportunities последовательно**
- Каждая SendAsync() отправляет сразу
- Результат: **все сообщения отправляются подряд за ~1-5ms**

```csharp
// RealTimeController.cs:60
foreach (var opp in opportunities)  // Sequential
{
    var chartData = JoinRealtimeWindows(...);  // Instant
    await webSocket.SendAsync(...);            // ~1ms
}
```

#### 4. **Task.Yield() - единая задержка для всех**
- Выполняется **после** обработки всех opportunities
- Результат: **все графики обновляются синхронно, затем пауза**

---

## 📊 ВРЕМЕННАЯ ДИАГРАММА

```
Time (ms) │ Action
─────────────────────────────────────────────────────────
0         │ GetFilteredOpportunities() → [Opp1, Opp2, Opp3, ...]
1         │ foreach start
2         │   Opp1: JoinRealtimeWindows() → chartData
3         │   Opp1: SendAsync(chartData)
4         │   Opp2: JoinRealtimeWindows() → chartData
5         │   Opp2: SendAsync(chartData)
6         │   Opp3: JoinRealtimeWindows() → chartData
7         │   Opp3: SendAsync(chartData)
...       │   ... (все остальные)
100       │ foreach end
101       │ Task.Yield() → pause
102-1000  │ (waiting)
1000      │ Loop repeats → GetFilteredOpportunities() again
```

**Результат:** Все графики обновляются в момент времени 0-100ms, затем пауза до 1000ms, затем снова все обновляются.

---

## ❌ ПОЧЕМУ ТЕКУЩЕЕ РЕШЕНИЕ НЕ РАБОТАЕТ

**Проблема:** Отправка отдельных WebSocket сообщений **не решает** синхронизацию

**До исправления:**
```csharp
var chartDataList = new List<object>();
foreach (var opp in opportunities) {
    chartDataList.Add(chartData);
}
await webSocket.SendAsync(JsonSerializer.Serialize(chartDataList));  // One message
```

**После исправления:**
```csharp
foreach (var opp in opportunities) {
    var chartData = ...;
    await webSocket.SendAsync(JsonSerializer.Serialize(chartData));  // Individual messages
}
```

**Разница:** Вместо одного большого JSON массива отправляем N маленьких JSON объектов.
**НО:** Все N сообщений отправляются **последовательно за 0-100ms** → клиент получает их практически одновременно.

---

## 🎯 ИСТИННАЯ ПРОБЛЕМА

**Графики независимы друг от друга, но архитектура их синхронизирует:**

1. **Единый цикл while** обрабатывает **все** opportunities
2. **Нет независимости** между графиками - все обновляются в одном цикле
3. **Нет стохастичности** - каждый раз один и тот же порядок
4. **Клиент получает burst** из N сообщений каждую секунду

---

## ✅ ПРАВИЛЬНОЕ РЕШЕНИЕ - EVENT-DRIVEN (РЕАЛИЗОВАНО)

### ✅ Подход 3: Event-driven architecture (IMPLEMENTED)

**Архитектура:**
```csharp
// RollingWindowService - raises events when data is updated
public event EventHandler<WindowDataUpdatedEventArgs>? WindowDataUpdated;

private void ProcessData(MarketData data)
{
    // ... add data to window ...

    // Raise event
    OnWindowDataUpdated(data.Exchange, data.Symbol);
}

// RealTimeController - subscribes to events
private async Task StreamRealtimeData(WebSocket webSocket)
{
    var opportunities = _opportunityFilter.GetFilteredOpportunities();

    foreach (var opp in opportunities)
    {
        EventHandler<WindowDataUpdatedEventArgs> handler = async (sender, e) =>
        {
            // Only process if event is relevant to this opportunity
            if ((e.Exchange == opp.Exchange1 || e.Exchange == opp.Exchange2)
                && e.Symbol == opp.Symbol)
            {
                var chartData = _rollingWindow.JoinRealtimeWindows(...);
                await webSocket.SendAsync(...);  // Thread-safe with SemaphoreSlim
            }
        };

        _rollingWindow.WindowDataUpdated += handler;
    }

    // Keep alive until WebSocket closes
    while (webSocket.State == WebSocketState.Open)
    {
        await Task.Delay(1000);
    }

    // Unsubscribe from all events
}
```

**Плюсы:**
- ✅ Идеальная архитектура
- ✅ Обновления ТОЛЬКО когда данные меняются (event-driven)
- ✅ Истинная независимость между графиками
- ✅ Нет polling, нет искусственных задержек
- ✅ Естественная асинхронность
- ✅ Thread-safe WebSocket отправка (SemaphoreSlim)
- ✅ Автоматическая отписка при закрытии WebSocket

**Минусы:**
- Требовалось добавить события в RollingWindowService
- Нужна thread-safe отправка (SemaphoreSlim)

---

### ❌ Подход 1: Polling с задержкой (DEPRECATED)

```csharp
while (webSocket.State == WebSocketState.Open)
{
    var opportunities = _opportunityFilter.GetFilteredOpportunities();
    foreach (var opp in opportunities) {
        var chartData = _rollingWindow.JoinRealtimeWindows(...);
        await webSocket.SendAsync(...);
        await Task.Delay(Random.Shared.Next(10, 100));  // Artificial delay
    }
    await Task.Delay(500);
}
```

**Проблемы:**
- ❌ Polling вместо events
- ❌ Искусственные задержки
- ❌ Обновления даже когда данные не меняются
- ❌ Не истинная асинхронность

---

## 📋 РЕАЛИЗАЦИЯ ЗАВЕРШЕНА

**Status:** ✅ Event-driven architecture implemented (2025-11-09)
**Files changed:**
- `RollingWindowService.cs` - added WindowDataUpdated event
- `RealTimeController.cs` - event-based subscriptions instead of polling

---

## 📊 RESULT

**До (polling):**
```
Time    │ Updates
────────┼────────────────────────────────
0ms     │ Chart1, Chart2, Chart3, Chart4, Chart5 (burst)
500ms   │ (pause - waiting for next iteration)
1000ms  │ Chart1, Chart2, Chart3, Chart4, Chart5 (burst)
1500ms  │ (pause)
```

**После (event-driven):**
```
Time    │ Updates                          │ Trigger
────────┼──────────────────────────────────┼──────────────────────
0ms     │                                  │
15ms    │ Chart2 (BTCUSDT Binance/Bybit)  │ New Binance BTCUSDT data
127ms   │ Chart4 (ETHUSDT Binance/GateIo) │ New GateIo ETHUSDT data
243ms   │ Chart1 (ICPUSDT Bybit/GateIo)   │ New Bybit ICPUSDT data
...     │ ... (only when data changes)     │ ... (event-driven)
```

**Ключевое отличие:**
- ❌ До: Polling каждые 500ms → burst обновлений даже если данные не менялись
- ✅ После: Event-driven → обновление ТОЛЬКО когда приходят новые данные

---

**Вывод:** Проблема была в **polling архитектуре**. Event-driven решение обеспечивает истинную независимость графиков.
