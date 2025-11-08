# ПОЛНЫЙ АНАЛИЗ ПРОБЛЕМ OOM (Out of Memory)

**Дата:** 2025-11-08
**Проекты:** charts (Python/FastAPI), collections (C#/.NET 9.0)
**Цель:** Выявление источников утечек памяти

---

## EXECUTIVE SUMMARY

Обнаружено **20 критических проблем** с управлением памятью:

- **5 КРИТИЧЕСКИХ** - приводят к OOM за минуты/часы
- **8 ВЫСОКИХ** - деградация производительности, утечки
- **7 СРЕДНИХ** - накопление мусора, allocation storms

**Главные виновники OOM:**
1. Unbounded Channels (C#) - неограниченный рост очередей
2. Event handlers без отписки - accumulation при переподключениях
3. Recursive Timer pattern (Python) - цепочка Timer объектов
4. Race conditions на non-thread-safe коллекциях
5. Медленные клиенты блокируют весь поток данных

---

## 1. КРИТИЧЕСКИЕ ПРОБЛЕМЫ (IMMEDIATE ACTION REQUIRED)

### 🔴 1.1 Unbounded Channels - НЕОГРАНИЧЕННЫЙ РОСТ

**Файл:** `collections/src/SpreadAggregator.Presentation/Program.cs:72-73`

```csharp
services.AddSingleton<RawDataChannel>(
    new RawDataChannel(Channel.CreateUnbounded<MarketData>())
);
services.AddSingleton<RollingWindowChannel>(
    new RollingWindowChannel(Channel.CreateUnbounded<MarketData>())
);
```

**Проблема:**
- Каналы созданы без ограничений размера
- Если `ParquetDataWriter` или `RollingWindowService` читают медленнее, чем пишет `OrchestrationService`
- Канал растет до полного исчерпания памяти

**Расчет:**
```
8 бирж × 1000 пар = 8000 пар
Каждая пара: 100 обновлений/сек
= 800K сообщений/сек входящих

Если reader обрабатывает только 10K/сек:
Накопление: 790K/сек
1 минута = 47 млн объектов × 200 bytes = ~9.4 GB
```

**Приоритет:** КРИТИЧНЫЙ
**Решение:** `Channel.CreateBounded<MarketData>(new BoundedChannelOptions(100000))`

---

### 🔴 1.2 Event Handlers Без Отписки

**Файл:** `collections/src/SpreadAggregator.Infrastructure/Services/Exchanges/Base/ExchangeClientBase.cs:201-202`

```csharp
result.Data.ConnectionLost += new Action(HandleConnectionLost);
result.Data.ConnectionRestored += new Action<TimeSpan>((t) =>
    WebSocketLogger.Log($"[{_parent.ExchangeName}] {streamType} connection restored..."));
```

**Проблема:**
- Обработчики НИКОГДА не отписываются (`-=`)
- При каждом переподключении создаются НОВЫЕ handlers
- Lambda захватывает `_parent` → circular reference
- Накопление: каждое переподключение = +2 handler

**Данные из websocket_stability_analysis.md:**
- MEXC: 63 отключения
- Bybit: 20 отключений
- Total: ~100 переподключений

**Результат:** 200 handlers остаются в памяти навсегда

**Приоритет:** КРИТИЧНЫЙ
**Решение:** Добавить отписку в `StopAsync()` или использовать weak events

---

### 🔴 1.3 Recursive Timer Pattern (Python)

**Файл:** `charts/server.py:68-87`

```python
def start_cleanup(self):
    if self.cleanup_timer:
        self.cleanup_timer.cancel()
    self.cleanup_timer = Timer(60.0, self._cleanup_old_data)
    self.cleanup_timer.start()

def _cleanup_old_data(self):
    now = datetime.now(timezone.utc)
    keys_to_remove = [key for key, window in self.windows.items() ...]
    for key in keys_to_remove:
        del self.windows[key]
    self.start_cleanup()  # ← РЕКУРСИЯ!
```

**Проблема:**
- `_cleanup_old_data` вызывает `start_cleanup()`
- Создается новый Timer, но старый может еще выполняться
- Цепочка: Timer1 → Timer2 → Timer3 → ...
- Каждый Timer захватывает `self.windows` (dict с 1000+ записями)

**Приоритет:** КРИТИЧНЫЙ
**Решение:** Использовать `asyncio.create_task()` с периодическим `sleep(60)` вместо Timer

---

### 🔴 1.4 Fire-and-Forget Tasks (C#)

**Файл:** `collections/src/SpreadAggregator.Application/Services/OrchestrationService.cs:73-75`

```csharp
var tasks = new List<Task>();
foreach (var exchangeName in exchangeNames)
{
    tasks.Add(ProcessExchange(exchangeClient, exchangeName));
}

// Do not await long-running tasks, let them run in the background.
// await Task.WhenAll(tasks);  ← ЗАКОММЕНТИРОВАНО!
```

**Проблема:**
- Все 8 подписок на биржи запускаются без контроля
- Если task падает с исключением → данные теряются молча
- `StartAsync()` возвращается до инициализации
- Клиенты получают пустые ответы

**Приоритет:** КРИТИЧНЫЙ
**Решение:** Разкомментировать `await Task.WhenAll(tasks)` или использовать background service

---

### 🔴 1.5 AllSymbolInfo Accumulation

**Файл:** `collections/src/SpreadAggregator.Application/Services/OrchestrationService.cs:27,84`

```csharp
private readonly List<SymbolInfo> _allSymbolInfo = new();

// В ProcessExchange:
_allSymbolInfo.AddRange(allSymbols);  // ТОЛЬКО добавление!
```

**Проблема:**
- При переподключении к бирже `ProcessExchange` вызывается снова
- `AddRange` добавляет дубликаты
- Нет дедупликации, нет очистки
- Список растет с каждым reconnect

**Расчет:**
```
8 бирж × 5000 пар = 40,000 символов × 200 bytes = 8 MB
После 10 переподключений = 80 MB
После 100 = 800 MB
```

**Приоритет:** КРИТИЧНЫЙ
**Решение:** Дедупликация по `symbol.Name` перед `AddRange` или использовать `HashSet<SymbolInfo>`

---

## 2. ВЫСОКИЕ ПРИОРИТЕТЫ

### 🟠 2.1 Task.WhenAll Синхронизация на Медленных Клиентах

**Файл:** `collections/src/SpreadAggregator.Infrastructure/Services/FleckWebSocketServer.cs:56-81`

```csharp
public Task BroadcastRealtimeAsync(string message)
{
    var tasks = new List<Task>();
    foreach (var socket in socketsSnapshot)
    {
        if (socket.IsAvailable)
            tasks.Add(socket.Send(message));
    }
    return Task.WhenAll(tasks);  // ← ЖДЁТ ВСЕХ
}
```

**Проблема:**
- 1 медленный браузер (500ms latency) замедляет всех остальных
- `Task.WhenAll` не завершится пока все не отправят
- Весь `OrchestrationService.ProcessExchange` callback замораживается
- Backpressure распространяется на биржи

**Сценарий:**
```
100 браузеров:
- 99 отправили за 5ms
- 1 медленный: 500ms
→ Весь broadcast блокируется на 500ms
→ OrchestrationService callback замораживается
→ Данные от бирж накапливаются в Unbounded Channel
```

**Приоритет:** ВЫСОКИЙ
**Решение:** Fire-and-forget broadcast с timeout на каждый socket

---

### 🟠 2.2 Dead WebSocket Connections

**Файл:** `collections/src/SpreadAggregator.Infrastructure/Services/FleckWebSocketServer.cs:15,35,49`

```csharp
private readonly List<IWebSocketConnection> _allSockets;

socket.OnOpen = () => { _allSockets.Add(socket); };
socket.OnClose = () => { _allSockets.Remove(socket); };
```

**Проблема:**
- Если `OnClose` не вызывается (network issues, browser crash)
- Socket остается в `_allSockets` навсегда
- Каждый broadcast пытается отправить в мертвое соединение
- Memory leak + performance degradation

**Приоритет:** ВЫСОКИЙ
**Решение:** Heartbeat/ping механизм с таймаутом, удаление неактивных соединений

---

### 🟠 2.3 WebSocket Client Missing Heartbeat (Python)

**Файл:** `charts/server.py:176-177`

```python
async with websockets.connect(uri,
                              ping_interval=None,  # ← ОТКЛЮЧЕНО
                              ping_timeout=None) as websocket:
```

**Проблема:**
- Пинги отключены намеренно (комментарий: "prevent timeouts with servers that don't respond")
- Dead connection к Collections может не быть обнаружена 10+ минут (TCP keepalive)
- Charts продолжает слушать несуществующее соединение

**Приоритет:** ВЫСОКИЙ
**Решение:** Включить `ping_interval=30, ping_timeout=10` или application-level heartbeat

---

### 🟠 2.4 Race Condition на Dict (Python)

**Файл:** `charts/server.py:61,89-161`

```python
class RollingWindow:
    def __init__(self):
        self.windows: Dict[str, RollingWindowData] = {}  # NOT THREAD-SAFE

    def process_spread_data(self, data: dict):
        # Вызывается из asyncio WebSocket task
        if key not in self.windows:
            self.windows[key] = RollingWindowData(...)
        window.spreads = [s for s in window.spreads if ...]  # NOT ATOMIC

    def _cleanup_old_data(self):
        # Вызывается из Timer thread
        for key in keys_to_remove:
            del self.windows[key]  # CONCURRENT MODIFICATION
```

**Проблема:**
- `self.windows` - обычный dict (не thread-safe)
- Доступ из 3 мест: asyncio task, Timer thread, HTTP endpoints
- Race condition между чтением и удалением

**Приоритет:** ВЫСОКИЙ
**Решение:** `asyncio.Lock` или перенести cleanup в asyncio task

---

### 🟠 2.5 Race Condition на List (C#)

**Файл:** `collections/src/SpreadAggregator.Application/Services/RollingWindowService.cs:49,57`

```csharp
private readonly ConcurrentDictionary<string, RollingWindowData> _windows = new();

private void ProcessData(MarketData data)
{
    var window = _windows.GetOrAdd(key, _ => new RollingWindowData { ... });

    // ПРОБЛЕМА:
    window.Spreads.RemoveAll(s => s.Timestamp < window.WindowStart);  // Operation 1
    window.Spreads.Add(spreadData);  // Operation 2
}

private void CleanupOldData(object? state)
{
    _windows.TryRemove(key, out _);  // Может удалить между Op1 и Op2
}
```

**Проблема:**
- `RollingWindowData.Spreads` это `List<SpreadData>` (не thread-safe)
- `ProcessData` вызывается из main thread
- `CleanupOldData` вызывается из Timer thread
- Между `RemoveAll` и `Add` может произойти удаление window

**Приоритет:** ВЫСОКИЙ
**Решение:** `ConcurrentBag<SpreadData>` или lock на каждый window

---

### 🟠 2.6 Closure Захват в Callbacks

**Файл:** `collections/src/SpreadAggregator.Application/Services/OrchestrationService.cs:114-142`

```csharp
tasks.Add(exchangeClient.SubscribeToTickersAsync(filteredSymbolNames,
    async spreadData =>
    {
        // Захватывает: _webSocketServer, _spreadCalculator, _rawDataChannel,
        // minVolume, maxVolume, _rollingWindowChannel, _configuration

        await _rawDataChannel.Writer.WriteAsync(normalizedSpreadData);
        var message = JsonSerializer.Serialize(wrapper);
        await _webSocketServer.BroadcastRealtimeAsync(message);
    }));
```

**Проблема:**
- Callback захватывает 7+ полей класса (closure)
- Подписка живет бесконечно → callback никогда не освобождается
- Весь `OrchestrationService` остается в памяти через closure
- Включая все DI dependencies

**Приоритет:** ВЫСОКИЙ
**Решение:** Избегать capture полей, передавать через параметры или weak references

---

### 🟠 2.7 Allocation Storm при Broadcast

**Файл:** `collections/src/SpreadAggregator.Infrastructure/Services/FleckWebSocketServer.cs:62`

```csharp
public Task BroadcastRealtimeAsync(string message)
{
    List<IWebSocketConnection> socketsSnapshot;
    lock (_lock)
    {
        socketsSnapshot = _allSockets.ToList();  // ← NEW LIST каждый раз
    }

    var tasks = new List<Task>();  // ← ЕЩЕ ОДИН NEW LIST
    foreach (var socket in socketsSnapshot)
    {
        tasks.Add(socket.Send(message));
    }
    return Task.WhenAll(tasks);  // ← Task[] allocation
}
```

**Проблема:**
- Вызывается ~100 раз/сек (по количеству спредов)
- При 1000 браузерах: `.ToList()` копирует 1000 ссылок
- `new List<Task>()` создается 100 раз/сек
- GC Gen0 pressure

**Расчет:**
```
100 broadcasts/sec × (1000 sockets × 8 bytes + 1000 tasks × 8 bytes)
= 100 × 16KB = 1.6 MB/sec allocation
= 96 MB/min только на временные списки
```

**Приоритет:** ВЫСОКИЙ
**Решение:** Object pooling или ArrayPool для временных буферов

---

### 🟠 2.8 MOCK_OPPORTUNITIES Global Variable

**Файл:** `charts/server.py:511,526`

```python
MOCK_OPPORTUNITIES = None

@app.post("/api/test/load_mock_data")
async def load_mock_data():
    global MOCK_OPPORTUNITIES
    MOCK_OPPORTUNITIES = pl.DataFrame(opp_data)  # ← Может быть ОГРОМНЫЙ
    return {"status": "success"}
```

**Проблема:**
- Глобальная переменная с DataFrame
- Может быть перезаписана несколько раз
- Старые DataFrame остаются в памяти если есть ссылки
- Нет ограничения на размер

**Приоритет:** ВЫСОКИЙ (только для тестов, но опасно)
**Решение:** Явная очистка при shutdown, weak references

---

## 3. СРЕДНИЕ ПРИОРИТЕТЫ

### 🟡 3.1 JsonSerializer Без Pooling

**Файл:** `collections/src/SpreadAggregator.Application/Services/OrchestrationService.cs:133,153`

```csharp
var message = JsonSerializer.Serialize(wrapper);  // ~100/сек
```

**Проблема:**
- Создается временный string для каждого сообщения
- Allocation storm при высокой частоте
- Нет повторного использования буферов

**Приоритет:** СРЕДНИЙ
**Решение:** `Utf8JsonWriter` с `ArrayBufferWriter` pooling

---

### 🟡 3.2 JSON Dumps/Loads Overhead (Python)

**Файл:** `charts/server.py:181,358,507`

```python
data = json.loads(message)  # Каждое WebSocket сообщение
yield json.dumps(chart_data) + '\n'  # Streaming
```

**Проблема:**
- `json.loads()` создает новый dict для каждого сообщения
- `json.dumps()` создает новый string
- Нет кеширования повторяющихся структур

**Приоритет:** СРЕДНИЙ
**Решение:** orjson или msgpack для меньших allocations

---

### 🟡 3.3 RollingWindow Unbounded Growth

**Файл:** `charts/server.py:61,78-87`

```python
class RollingWindow:
    def __init__(self):
        self.windows: Dict[str, RollingWindowData] = {}

    def _cleanup_old_data(self):
        keys_to_remove = [key for key, window in self.windows.items()
                         if window.window_end < now - self.window_size]
```

**Проблема:**
- TTL: 1 час
- Cleanup: каждые 60 секунд
- Если cleanup отстает → неограниченный рост
- Нет max size limit

**Потенциал:**
```
8 бирж × 5000 пар = 40,000 окон
Каждое окно: 3600+ spreads × 200 bytes = 720 KB
Total: 40,000 × 720 KB = ~28 GB потенциально
```

**Приоритет:** СРЕДНИЙ
**Решение:** Max window count limit (например, 10,000 окон)

---

### 🟡 3.4 ParquetDataWriter Buffer Growth

**Файл:** `collections/src/SpreadAggregator.Infrastructure/Services/ParquetDataWriter.cs:163-164`

```csharp
var spreadBuffers = new Dictionary<string, List<SpreadData>>();
var tradeBuffers = new Dictionary<string, List<TradeData>>();
var batchSize = _configuration.GetValue<int>("Recording:BatchSize", 1000);
```

**Проблема:**
- Ключ: hourly partition (exchange_symbol_hour)
- Может быть 8000+ уникальных партиций
- Каждая до 1000 записей перед flush
- Total: 8M записей потенциально

**Приоритет:** СРЕДНИЙ
**Решение:** Лимит на количество активных партиций или уменьшить batchSize

---

### 🟡 3.5 Timer Resource Leak (C#)

**Файл:** `collections/src/SpreadAggregator.Application/Services/RollingWindowService.cs:21`

```csharp
_cleanupTimer = new Timer(CleanupOldData, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
```

**Файл:** `collections/src/SpreadAggregator.Presentation/Program.cs:165-167`

```csharp
public Task StopAsync(CancellationToken cancellationToken)
{
    return Task.CompletedTask;  // НЕ dispose Timer!
}
```

**Проблема:**
- Timer не останавливается при shutdown
- Thread leak
- `CleanupOldData` может выполняться после остановки других сервисов

**Приоритет:** СРЕДНИЙ
**Решение:** Implement `IAsyncDisposable`, dispose timer в `StopAsync`

---

### 🟡 3.6 WebSocket Fire-and-Forget Task (Python)

**Файл:** `charts/server.py:207`

```python
ws_task = None

def start_websocket_client():
    global ws_task
    if ws_task is None or ws_task.done():
        ws_task = asyncio.create_task(websocket_client())  # FIRE-AND-FORGET
```

**Проблема:**
- Task создается без await
- Исключения игнорируются (Python 3.8+ warnings)
- Нет graceful shutdown

**Приоритет:** СРЕДНИЙ
**Решение:** `asyncio.TaskGroup` для управления жизненным циклом

---

### 🟡 3.7 Lock на File I/O

**Файл:** `collections/src/SpreadAggregator.Infrastructure/Services/WebSocketLogger.cs:41-44`

```csharp
lock (FileLock)
{
    File.AppendAllText(LogFilePath, logMessage, Encoding.UTF8);  // BLOCKING I/O
}
```

**Проблема:**
- Sync lock блокирует thread при файловом I/O
- Может быть bottleneck если много потоков логируют
- Anti-pattern для async contexts

**Приоритет:** СРЕДНИЙ (логирование не критично)
**Решение:** `SemaphoreSlim` для async, или буфер + background writer

---

## 4. СВОДНАЯ ТАБЛИЦА ВСЕХ ПРОБЛЕМ

| # | Компонент | Файл | Строки | Проблема | Критичность |
|---|-----------|------|--------|----------|-------------|
| 1 | Unbounded Channels | Program.cs | 72-73 | Неограниченный рост очередей | 🔴 КРИТИЧ |
| 2 | Event Handlers | ExchangeClientBase.cs | 201-202 | Нет отписки, accumulation | 🔴 КРИТИЧ |
| 3 | Recursive Timer | server.py | 68-87 | Цепочка Timer объектов | 🔴 КРИТИЧ |
| 4 | Fire-and-Forget Tasks | OrchestrationService.cs | 73-75 | Нет await на подписки | 🔴 КРИТИЧ |
| 5 | AllSymbolInfo | OrchestrationService.cs | 27,84 | Дубликаты при reconnect | 🔴 КРИТИЧ |
| 6 | Task.WhenAll Sync | FleckWebSocketServer.cs | 56-81 | Медленный клиент блокирует всех | 🟠 ВЫСОКИЙ |
| 7 | Dead Connections | FleckWebSocketServer.cs | 15,35,49 | Нет heartbeat, накопление | 🟠 ВЫСОКИЙ |
| 8 | Missing Heartbeat | server.py | 176-177 | ping_interval=None | 🟠 ВЫСОКИЙ |
| 9 | Race Dict | server.py | 61,89-161 | Не thread-safe доступ | 🟠 ВЫСОКИЙ |
| 10 | Race List | RollingWindowService.cs | 49,57 | Concurrent modification | 🟠 ВЫСОКИЙ |
| 11 | Closure Capture | OrchestrationService.cs | 114-142 | Захват 7+ полей | 🟠 ВЫСОКИЙ |
| 12 | Allocation Storm | FleckWebSocketServer.cs | 62 | ToList() 100/сек | 🟠 ВЫСОКИЙ |
| 13 | Global DataFrame | server.py | 511,526 | MOCK_OPPORTUNITIES | 🟠 ВЫСОКИЙ |
| 14 | JsonSerializer | OrchestrationService.cs | 133,153 | Нет pooling | 🟡 СРЕДНИЙ |
| 15 | JSON Overhead | server.py | 181,358,507 | dumps/loads storm | 🟡 СРЕДНИЙ |
| 16 | RollingWindow Growth | server.py | 61,78-87 | Нет max limit | 🟡 СРЕДНИЙ |
| 17 | Buffer Growth | ParquetDataWriter.cs | 163-164 | 8000+ партиций | 🟡 СРЕДНИЙ |
| 18 | Timer Leak | RollingWindowService.cs | 21 | Нет dispose | 🟡 СРЕДНИЙ |
| 19 | WS Task Leak | server.py | 207 | Fire-and-forget | 🟡 СРЕДНИЙ |
| 20 | File Lock | WebSocketLogger.cs | 41-44 | Blocking I/O в lock | 🟡 СРЕДНИЙ |

---

## 5. СЦЕНАРИЙ PRODUCTION FAILURE

### Timeline: OOM Event

**14:00 - Нормальная работа**
- 8 бирж подключены
- 100 браузеров
- Memory: 500 MB

**14:15 - Начало проблем**
- MEXC переподключается (5-й раз за час)
- Event handlers накопились: 10 старых handlers
- AllSymbolInfo: 40,000 × 5 = 200,000 записей (дубликаты)
- Memory: 800 MB

**14:30 - Деградация**
- 5 медленных браузеров на спутниковой связи
- Task.WhenAll блокируется на 500ms
- Unbounded Channel начинает расти:
  - 800K сообщений/сек входящих
  - 10K обрабатываются (блокировка на broadcast)
  - Накопление: 790K/сек
- Memory: 2 GB

**14:35 - Critical**
- Channel содержит 150M объектов (5 минут × 790K/сек)
- Memory: 30+ GB
- GC не успевает
- System.OutOfMemoryException

**14:36 - Crash**
- Collections падает
- Charts WebSocket client пытается переподключиться
- Браузеры теряют connection
- Данные теряются

**Логи:**
```
14:35:42 [ERROR] System.OutOfMemoryException: Insufficient memory to continue execution
14:35:43 [ERROR] ObjectDisposedException in FleckWebSocketServer (race condition)
14:35:44 [WARN] MEXC WebSocket disconnected, retrying...
14:35:45 [ERROR] Application shutdown due to unhandled exception
```

---

## 6. РЕКОМЕНДАЦИИ ПО ПРИОРИТЕТАМ

### IMMEDIATE (1-2 дня)

1. **Bounded Channels** - заменить CreateUnbounded
2. **Event Handlers Cleanup** - добавить отписку
3. **Await Tasks** - раскомментировать Task.WhenAll
4. **AsyncIO Timer** - заменить threading.Timer на asyncio

### SHORT-TERM (1-2 недели)

5. **Heartbeat** - добавить ping/pong
6. **Timeout на Broadcast** - fire-and-forget с таймаутом
7. **Thread-Safe Collections** - asyncio.Lock / ConcurrentBag
8. **Дедупликация** - AllSymbolInfo

### LONG-TERM (1-2 месяца)

9. **Object Pooling** - ArrayPool, StringBuilderPool
10. **Monitoring** - Memory metrics, queue sizes
11. **Load Testing** - 1000+ браузеров, 24+ часа
12. **Архитектурный рефакторинг** - см. RATIONALITY_AUDIT.md

---

## 7. МЕТРИКИ ДЛЯ МОНИТОРИНГА

```csharp
// Collections
- RawDataChannel.Reader.Count
- RollingWindowChannel.Reader.Count
- FleckWebSocketServer._allSockets.Count
- GC.GetTotalMemory(false)
- Process.PrivateMemorySize64

// Charts
- len(rolling_window.windows)
- sum(len(w.spreads) for w in rolling_window.windows.values())
- asyncio.all_tasks() length
```

**Алерты:**
- Channel count > 10,000
- Memory > 80% available
- GC Gen2 collections/min > 10
- WebSocket connections > 1,000

---

## 8. TESTING CHECKLIST

- [ ] Load test: 1000 браузеров × 24 часа
- [ ] Memory profiler: dotMemory / memory_profiler
- [ ] Reconnect storm: 100 reconnects/min
- [ ] Slow client simulation: 500ms latency
- [ ] GC pressure test: полный цикл Gen0→Gen1→Gen2
- [ ] Race condition: concurrent читатели/писатели
- [ ] Graceful shutdown: все resources disposed

---

**Следующий шаг:** См. RATIONALITY_AUDIT.md для анализа архитектурных решений
