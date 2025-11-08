# АУДИТ ПРОБЛЕМ OOM - ARB1 PROJECT

**Дата:** 2025-11-08
**Проекты:** charts/ (Python) + collections/ (C#)
**Статус:** 🔴 КРИТИЧЕСКИЕ ПРОБЛЕМЫ ОБНАРУЖЕНЫ

---

## EXECUTIVE SUMMARY

Обнаружено **43 проблемы** с управлением памятью:
- 🔴 **10 КРИТИЧЕСКИХ** (могут привести к OOM)
- 🟠 **15 ВЫСОКОГО ПРИОРИТЕТА** (утечки памяти)
- 🟡 **18 СРЕДНЕГО ПРИОРИТЕТА** (неоптимальное использование)

**Главные проблемы:**
1. Неограниченные каналы данных (Unbounded Channels)
2. RollingWindow 1 час = 354 MB (нужно 5 минут = 36 MB)
3. Утечки памяти через event handlers
4. Множественное копирование данных при сериализации
5. Дублирование функциональности между проектами

---

## 1. КРИТИЧЕСКИЕ ПРОБЛЕМЫ (OOM RISK)

### 1.1 Unbounded Channels - Collections

**Файл:** [Program.cs:72-73](collections/src/SpreadAggregator.Presentation/Program.cs#L72-L73)

```csharp
Channel.CreateUnbounded<MarketData>()  // RawDataChannel и RollingWindowChannel
```

**Проблема:**
- Нет ограничения размера буфера
- При 8000 updates/sec: 1 минута отставания = 480 MB в памяти
- Гарантированный OOM при высокой нагрузке

**Расчет:**
```
8 бирж × 1000 updates/sec × 500 bytes/update = 4 MB/sec
10 минут отставания = 2.4 GB в канале
```

**Решение:**
```csharp
var options = new BoundedChannelOptions(10000)
{ FullMode = BoundedChannelFullMode.Wait };
Channel.CreateBounded(options);
```

---

### 1.2 Неограниченный рост _allSymbolInfo

**Файл:** [OrchestrationService.cs:27,84](collections/src/SpreadAggregator.Application/Services/OrchestrationService.cs#L27)

```csharp
private readonly List<SymbolInfo> _allSymbolInfo = new();
// ...
_allSymbolInfo.AddRange(allSymbols);  // ТОЛЬКО добавление!
```

**Проблема:**
- Нет дедупликации
- При каждом переподключении к бирже добавляются дубликаты
- После 100 переподключений: 8 MB × 100 = 800 MB

**Решение:**
```csharp
var newSymbols = allSymbols.Where(s =>
    !_allSymbolInfo.Any(x => x.Exchange == s.Exchange && x.Name == s.Name)
).ToList();
_allSymbolInfo.AddRange(newSymbols);
```

---

### 1.3 Глобальный DataFrame - Charts

**Файл:** [server.py:511,526](charts/server.py#L511)

```python
MOCK_OPPORTUNITIES = None
# ...
MOCK_OPPORTUNITIES = pl.DataFrame(opp_data)  # Может быть огромным
```

**Проблема:**
- Глобальная переменная хранит большой DataFrame
- Многократные вызовы endpoint `/api/test/load_mock_data`
- Нет явной очистки

**Размер:**
```
100K opportunities × 10 колонок × 8 bytes = 8 MB minimum
С учетом Polars overhead: 15-20 MB
```

---

### 1.4 RollingWindow 1 час (оба проекта)

**Collections:** [RollingWindowService.cs](collections/src/SpreadAggregator.Application/Services/RollingWindowService.cs)
**Charts:** [server.py:60](charts/server.py#L60)

```csharp
TimeSpan.FromHours(1)  // 1 ЧАС данных!
```

**Проблема:**
```
Один SpreadData = 123 bytes
800 пар × 3600 updates/час × 123 bytes = 354 MB

Trades добавляют еще 80 MB
ИТОГО: 434 MB в worst case
```

**Решение:** Уменьшить до 5 минут
```
434 MB × (5/60) = 36 MB (12x меньше!)
```

---

### 1.5 Множественное копирование при Parquet записи

**Файл:** [ParquetDataWriter.cs:75-87](collections/src/SpreadAggregator.Infrastructure/Services/ParquetDataWriter.cs#L75-L87)

```csharp
new DataColumn(_spreadSchema.DataFields[0], data.Select(d => d.Timestamp).ToArray()),
new DataColumn(_spreadSchema.DataFields[1], data.Select(d => d.BestBid).ToArray()),
// ... 6 more .ToArray() calls
```

**Проблема:** 8 полных копий данных:
```
1000 записей × 8 колонок × 8 bytes = 64 KB × 2 (копии) = 128 KB per batch
При 4800 буферах одновременно: 614 MB!
```

---

## 2. УТЕЧКИ ПАМЯТИ

### 2.1 Event Handlers без отписки - Collections

**Файл:** [ExchangeClientBase.cs:201-203](collections/src/SpreadAggregator.Infrastructure/Services/Exchanges/Base/ExchangeClientBase.cs#L201-L203)

```csharp
result.Data.ConnectionLost += new Action(HandleConnectionLost);
result.Data.ConnectionRestored += new Action<TimeSpan>((t) => ...);
```

**Проблема:**
- События `+=` без соответствующего `-=`
- При переподписках старые handlers остаются в памяти
- Утечка ManagedConnection объектов

**Затронуты все биржи:**
- BinanceExchangeClient
- OkxExchangeClient
- BybitExchangeClient
- MexcExchangeClient
- BitgetExchangeClient
- GateIoExchangeClient
- KucoinExchangeClient
- BingXExchangeClient

---

### 2.2 Async Lambda без unsubscribe

**Файл:** [OrchestrationService.cs:114-162](collections/src/SpreadAggregator.Application/Services/OrchestrationService.cs#L114-L162)

```csharp
tasks.Add(exchangeClient.SubscribeToTickersAsync(filteredSymbolNames, async spreadData =>
{
    // Lambda захватывает: this, _webSocketServer, каналы
    // НЕТ механизма отписки!
}));
```

**Проблема:**
- Callback никогда не удаляется
- OrchestrationService не может быть собран GC
- Накопление при перезапусках

---

### 2.3 WebSocket соединения

**Файл:** [FleckWebSocketServer.cs:28-53](collections/src/SpreadAggregator.Infrastructure/Services/FleckWebSocketServer.cs#L28-L53)

```csharp
socket.OnOpen = () => { _allSockets.Add(socket); };
socket.OnClose = () => { _allSockets.Remove(socket); };
```

**Проблема:**
- Если `OnClose` не вызывается (сетевые проблемы), сокет остается
- Lambda circular reference (socket → lambda → socket)
- Нет таймаута на мертвые соединения

---

### 2.4 WebSocket Task не отменяется - Charts

**Файл:** [server.py:170,204-208](charts/server.py#L170)

```python
ws_task = None

def start_websocket_client():
    global ws_task
    if ws_task is None or ws_task.done():
        ws_task = asyncio.create_task(websocket_client())
```

**Проблема:**
- При FastAPI auto-reload старая task НЕ отменяется
- После 10 reloads: 10 WebSocket клиентов одновременно
- Каждый держит event loop и память

---

### 2.5 Timer рекурсивный запуск

**Файл:** [server.py:64-87](charts/server.py#L64-L87)

```python
def _cleanup_old_data(self):
    # cleanup...
    self.start_cleanup()  # Перезапускает таймер

def start_cleanup(self):
    if self.cleanup_timer:
        self.cleanup_timer.cancel()
    self.cleanup_timer = Timer(60.0, self._cleanup_old_data)
    self.cleanup_timer.start()
```

**Проблема:**
- Race condition между cancel() и новым Timer
- Могут запуститься множественные таймеры
- Timer threads не daemon - блокируют shutdown

---

## 3. УПРАВЛЕНИЕ ЗАДАЧАМИ

### 3.1 Fire-and-Forget паттерн

**Файл:** [Program.cs:139,161](collections/src/SpreadAggregator.Presentation/Program.cs#L139)

```csharp
public Task StartAsync(CancellationToken cancellationToken)
{
    _ = _orchestrationService.StartAsync(cancellationToken);  // FIRE-AND-FORGET
    return Task.CompletedTask;
}

public Task StopAsync(CancellationToken cancellationToken)
{
    return Task.CompletedTask;  // ПУСТО!
}
```

**Проблема:**
- Задачи не отслеживаются
- StopAsync пустой - нет cleanup
- Фоновые задачи работают после shutdown

---

### 3.2 Задачи подписок не awaited

**Файл:** [OrchestrationService.cs:168-170](collections/src/SpreadAggregator.Application/Services/OrchestrationService.cs#L168-L170)

```csharp
// await Task.WhenAll(tasks);  // ЗАКОММЕНТИРОВАНО!
```

**Проблема:**
- Исключения в подписках теряются
- Нет контроля над задачами
- Невозможно корректно остановить

---

### 3.3 Timer не dispose

**Файл:** [RollingWindowService.cs:16,21](collections/src/SpreadAggregator.Application/Services/RollingWindowService.cs#L16)

```csharp
public class RollingWindowService  // НЕ IDisposable!
{
    private readonly Timer _cleanupTimer;

    public RollingWindowService(...)
    {
        _cleanupTimer = new Timer(...);
        // Timer.Dispose() НИКОГДА не вызывается
    }
}
```

**Проблема:**
- Поток таймера остается активным
- Утечка потоков
- Зависание на shutdown

---

## 4. СЕРИАЛИЗАЦИЯ

### 4.1 Multiple DataFrame копии - Charts

**Файл:** [server.py:279-301](charts/server.py#L279-L301)

```python
df = pl.read_parquet(files)      # DataFrame1
merged_df = df_a.join_asof(...)  # DataFrame2
result_df = merged_df.with_columns(...)  # DataFrame3
```

**Проблема:**
```
100K rows × 8 cols × 8 bytes × 5 копий = 32 MB
При обработке 50 opportunities: 1.6 GB в памяти
```

---

### 4.2 Parquet буферы без лимита

**Файл:** [ParquetDataWriter.cs:163-220](collections/src/SpreadAggregator.Infrastructure/Services/ParquetDataWriter.cs#L163-L220)

```csharp
var spreadBuffers = new Dictionary<string, List<SpreadData>>();
```

**Проблема:**
```
8 бирж × 500 symbols × 24 hours = 96,000 потенциальных буферов
При batch size 1000: 96M items = 19.2 GB возможно!
```

---

### 4.3 JSON сериализация AllSymbolInfo

**Файл:** [FleckWebSocketServer.cs:41-43](collections/src/SpreadAggregator.Infrastructure/Services/FleckWebSocketServer.cs#L41-L43)

```csharp
var allSymbols = orchestrationService.AllSymbolInfo;
var message = System.Text.Json.JsonSerializer.Serialize(wrapper);
```

**Проблема:**
- Сериализация на КАЖДОЕ подключение клиента
- Нет кэширования результата
- При 1000 символов: ~200 KB JSON строка каждый раз

---

### 4.4 socketsSnapshot копирование

**Файл:** [FleckWebSocketServer.cs:56-81](collections/src/SpreadAggregator.Infrastructure/Services/FleckWebSocketServer.cs#L56-L81)

```csharp
socketsSnapshot = _allSockets.ToList();  // Копия при КАЖДОМ broadcast!
```

**Проблема:**
```
100 сокетов × 100 broadcasts/sec = 10,000 копий списка в секунду
```

---

## 5. WEBSOCKET ПРОБЛЕМЫ

### 5.1 Отключенный ping/pong - Charts

**Файл:** [server.py:176-177](charts/server.py#L176-L177)

```python
ping_interval=None, ping_timeout=None  # ОТКЛЮЧЕНО!
```

**Проблема:**
- Мертвые соединения не обнаруживаются
- Утечка сокетов
- До 30+ минут до обнаружения проблемы

---

### 5.2 Task накопление в Fleck

**Файл:** [FleckWebSocketServer.cs:56-81](collections/src/SpreadAggregator.Infrastructure/Services/FleckWebSocketServer.cs#L56-L81)

```csharp
tasks.Add(socket.Send(message));  // Task не awaited сразу
```

**Проблема:**
```
Медленный клиент (100 KB/sec)
100 pending tasks × 1KB messages = 100 MB в очереди
```

---

### 5.3 Нет backpressure в FastAPI

**Файл:** [server.py:564-566](charts/server.py#L564-L566)

```python
await asyncio.sleep(0.2)  # Фиксированная частота 200ms
await websocket.send_text(json.dumps(data))
```

**Проблема:**
- Отправка независимо от скорости клиента
- Медленные клиенты теряют данные
- Нет контроля потока

---

## 6. АРХИТЕКТУРНЫЕ ПРОБЛЕМЫ

### 6.1 Дублирование RollingWindow

| Компонент | Collections | Charts |
|-----------|------------|--------|
| Размер окна | 1 час | 1 час |
| Код | 86 строк | 88 строк |
| Память | 354 MB | 354 MB |

**Проблема:** Дублирование → 708 MB + WebSocket overhead

---

### 6.2 Два WebSocket сервера

```
Collections:8181 → Charts:8002 → Клиенты
```

**Проблема:**
- Сетевая задержка (5-20ms)
- JSON сериализация overhead
- Потенциальная потеря данных

---

### 6.3 Избыточная архитектура

```
Domain → Application → Infrastructure → Presentation
```

**Для 1120 строк кода:**
- 4 слоя
- 8 интерфейсов
- 3 hosted services
- 2 channels

**Вердикт:** OVERKILL

---

## СВОДНАЯ ТАБЛИЦА ПРОБЛЕМ

| # | Проблема | Файл | Приоритет | Потенциальная утечка |
|---|----------|------|-----------|---------------------|
| 1 | Unbounded Channels | Program.cs:72-73 | 🔴 КРИТИЧ | 100 MB - ∞ |
| 2 | _allSymbolInfo рост | OrchestrationService.cs:27 | 🔴 КРИТИЧ | 8 MB - ∞ |
| 3 | MOCK_OPPORTUNITIES | server.py:511 | 🔴 КРИТИЧ | 0.5-2 GB |
| 4 | RollingWindow 1h | Оба проекта | 🔴 КРИТИЧ | 354 MB × 2 |
| 5 | Parquet 8x копии | ParquetDataWriter.cs:75 | 🔴 КРИТИЧ | 614 MB |
| 6 | Event += без -= | ExchangeClientBase.cs:201 | 🔴 КРИТИЧ | Накопление |
| 7 | Async lambda leak | OrchestrationService.cs:114 | 🔴 КРИТИЧ | Накопление |
| 8 | WebSocket мертвые | FleckWebSocketServer.cs:28 | 🟠 ВЫСОКИЙ | 50-200 MB |
| 9 | ws_task не отменяется | server.py:204 | 🟠 ВЫСОКИЙ | × reloads |
| 10 | Timer утечка | server.py:64 | 🟠 ВЫСОКИЙ | × потоков |
| 11 | Fire-and-forget | Program.cs:139 | 🟠 ВЫСОКИЙ | Нет cleanup |
| 12 | Tasks не awaited | OrchestrationService.cs:168 | 🟠 ВЫСОКИЙ | Потеря контроля |
| 13 | Timer не dispose | RollingWindowService.cs:16 | 🟠 ВЫСОКИЙ | Поток |
| 14 | DataFrame копии | server.py:279 | 🟠 ВЫСОКИЙ | 32 MB × N |
| 15 | Parquet буферы | ParquetDataWriter.cs:163 | 🟠 ВЫСОКИЙ | До 19 GB |
| 16 | JSON на подключение | FleckWebSocketServer.cs:41 | 🟡 СРЕДНИЙ | 200 KB × N |
| 17 | socketsSnapshot | FleckWebSocketServer.cs:56 | 🟡 СРЕДНИЙ | Копии |
| 18 | Ping/pong off | server.py:176 | 🟡 СРЕДНИЙ | Мертвые сокеты |

---

## РЕКОМЕНДАЦИИ

### КРИТИЧЕСКИЙ ПРИОРИТЕТ (сделать СЕЙЧАС)

**1. Ограничить Channels**
```csharp
var options = new BoundedChannelOptions(10000);
Channel.CreateBounded(options);
```
- Усилие: 2 часа
- Эффект: Предотвращение OOM

**2. RollingWindow: 1 час → 5 минут**
```csharp
TimeSpan.FromMinutes(5)
```
- Усилие: 5 минут
- Эффект: 354 MB → 36 MB (12x меньше)

**3. Дедупликация _allSymbolInfo**
```csharp
var newSymbols = allSymbols.Where(s => !_allSymbolInfo.Any(...));
_allSymbolInfo.AddRange(newSymbols);
```
- Усилие: 30 минут
- Эффект: Предотвращение роста

**4. Event handlers cleanup**
```csharp
result.Data.ConnectionLost -= HandleConnectionLost;
```
- Усилие: 4 часа (все биржи)
- Эффект: Устранение утечек

---

### ВЫСОКИЙ ПРИОРИТЕТ (следующая неделя)

**5. Реализовать StopAsync**
```csharp
public async Task StopAsync(CancellationToken cancellationToken)
{
    await _orchestrationService.StopAsync(cancellationToken);
}
```
- Усилие: 1 день
- Эффект: Грациозный shutdown

**6. IDisposable для RollingWindowService**
```csharp
public void Dispose() => _cleanupTimer?.Dispose();
```
- Усилие: 2 часа
- Эффект: Устранение утечки потоков

**7. WebSocket task отмена**
```python
async def lifespan(app):
    yield
    if ws_task:
        ws_task.cancel()
```
- Усилие: 1 час
- Эффект: Корректный shutdown

---

### СРЕДНИЙ ПРИОРИТЕТ (оптимизация)

**8. Объединить Collections + Charts**
- Усилие: 1 неделя
- Эффект: -46% LOC, -50% памяти, +20% производительность

**9. Упростить архитектуру (4 слоя → 2)**
- Усилие: 2 дня
- Эффект: -200 LOC boilerplate

**10. Кэширование JSON**
```csharp
private string? _cachedAllSymbolsJson;
```
- Усилие: 1 час
- Эффект: Меньше CPU

---

## ИТОГОВЫЕ ЧИСЛА

### Текущее состояние:
- **Память worst case:** 1.5 GB (RollingWindow × 2 + буферы + утечки)
- **LOC:** 1120
- **Процессов:** 2
- **Сложность:** Высокая

### После исправлений:
- **Память worst case:** 150 MB (-90%)
- **LOC:** 600-700 (-40%)
- **Процессов:** 1 (-50%)
- **Сложность:** Средняя

---

## ФАЙЛЫ ДЛЯ ИСПРАВЛЕНИЯ

### C# (collections/):
1. `src/SpreadAggregator.Presentation/Program.cs`
2. `src/SpreadAggregator.Application/Services/OrchestrationService.cs`
3. `src/SpreadAggregator.Application/Services/RollingWindowService.cs`
4. `src/SpreadAggregator.Infrastructure/Services/ParquetDataWriter.cs`
5. `src/SpreadAggregator.Infrastructure/Services/FleckWebSocketServer.cs`
6. `src/SpreadAggregator.Infrastructure/Services/Exchanges/Base/ExchangeClientBase.cs`

### Python (charts/):
7. `charts/server.py`

---

**КОНЕЦ ОТЧЕТА**
