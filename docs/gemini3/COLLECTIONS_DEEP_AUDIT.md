# Collections Project: Deep Audit Report (Extended)
**Дата:** 2025-11-20  
**Тип:** Поиск скрытых багов, логических ошибок, race conditions, memory leaks  
**Версия:** 2.0 (Extended Deep Dive)

---

## 🔍 Критические находки (новые)

### 🚨 ISSUE #10: LruCache — НЕАТОМАРНЫЙ AddOrUpdate
**Файл:** `LruCache.cs:53-71`  
**Серьезность:** **CRITICAL**  
**Статус:** 🔴 **АКТИВНЫЙ БАГ**

**Проблема:**
```csharp
public void AddOrUpdate(TKey key, TValue value)
{
    var tick = Interlocked.Increment(ref _currentTick);
    var entry = new CacheEntry { Value = value, LastAccessTick = tick };

    _cache.AddOrUpdate(key, entry, (k, old) =>
    {
        old.Value = value;           // ❌ MUTATION старого объекта!
        old.LastAccessTick = tick;   // ❌ Другой поток может его читать!
        return old;
    });

    // ❌ RACE CONDITION: Count может измениться между проверкой и вызовом
    if (_cache.Count > _maxSize)
    {
        EvictOldest();
    }
}
```

**Три проблемы:**

1. **Mutation старого CacheEntry:**
   - `TryGetValue` в другом потоке может читать `entry` одновременно с мутацией в `AddOrUpdate`
   - `entry.Value` и `entry.LastAccessTick` модифицируются без синхронизации
   - **Data race** на уровне полей класса!

2. **TOCTOU (Time-Of-Check-Time-Of-Use):**
   ```csharp
   if (_cache.Count > _maxSize)  // Check
   {
       EvictOldest();             // Use (может быть уже не нужно)
   }
   ```
   Между проверкой и вызовом другой поток мог удалить элементы.

3. **Eviction может не сработать:**
   - 10 потоков одновременно добавляют элементы
   - Все видят `Count = _maxSize + 1`
   - Все пытаются вызвать `EvictOldest()`, но только один зайдет в lock
   - Остальные 9 вернутся, но `Count` может быть уже `_maxSize + 9`!

**Exploitability:**
- **Corruption данных:** Reader поток видит частично обновленный `CacheEntry`
- **Memory leak:** Cache растет выше `_maxSize` при высокой нагрузке
- **Stale data:** Timestamp обновлен,но Value еще старый (или наоборот)

**Решение:**
```csharp
_cache.AddOrUpdate(key, 
    addValueFactory: k => new CacheEntry { Value = value, LastAccessTick = tick },
    updateValueFactory: (k, old) => new CacheEntry { Value = value, LastAccessTick = tick }
    // ^^^ Создаем НОВЫЙ объект вместо мутации!
);
```

---

### 💀 ISSUE #11: ParquetDataWriter — Fire-and-Forget с утечкой
**Файл:** `ParquetDataWriter.cs:216, 237`  
**Серьезность:** **HIGH**  
**Статус:** 🔴 **АКТИВНЫЙ БАГ**

**Код:**
```csharp
if (buffer.Count >= batchSize)
{
    Directory.CreateDirectory(hourlyPartitionDir);
    var filePath = Path.Combine(hourlyPartitionDir, $"spreads-{data.Timestamp:mm-ss.fffffff}.parquet");
    _ = FlushSpreadBufferAsync(filePath, buffer); // ❌ Fire-and-forget
}
```

**Проблемы:**

1. **Buffer НЕ КОПИРУЕТСЯ:**
   - `FlushSpreadBufferAsync` получает **ссылку** на `buffer`
   - Эта же `buffer` продолжает использоваться в main loop
   - Async flush может читать данные, пока main loop их модифицирует!

2. **Race condition:**
   ```
   Thread 1 (main):           Thread 2 (async flush):
   buffer.Add(spreadData)     
                              buffer.Count  // читает Count
   buffer.Clear()             
                              await WriteSpreadsAsync(filePath, buffer)  // пустой массив!
   ```

3. **Exception swallowing:**
   - Если `FlushSpreadBufferAsync` кинет exception, никто его не поймает
   - Data loss без логирования

**Реальный сценарий:**
- Buffer заполнен до 1000 элементов
- Запускается async flush (но еще не скопировал данные)
- Новый элемент добавляется → buffer.Count = 1001
- Запускается **второй** async flush для того же buffer!
- Оба flush'а читают  один массив → **дубликаты данных + race на Clear()**

**Решение:**
```csharp
if (buffer.Count >= batchSize)
{
    var bufferCopy = new List<SpreadData>(buffer);  // COPY!
    buffer.Clear();
    
    Directory.CreateDirectory(hourlyPartitionDir);
    var filePath = Path.Combine(...);
    
    _ = Task.Run(async () =>
    {
        try
        {
            await WriteSpreadsAsync(filePath, bufferCopy);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FATAL] Flush failed: {ex}");
        }
    });
}
```

---

### ⚠️ ISSUE  #12: OrchestrationService — Zombie Exchange Tasks
**Файл:** `OrchestrationService.cs:126-142`  
**Серьезность:** MEDIUM  
**Статус:** ⚠️ **ПОТЕНЦИАЛЬНАЯ ПРОБЛЕМА**

**Код:**
```csharp
var task = Task.Run(async () =>
{
    try
    {
        await ProcessExchange(exchangeClient, exchangeName, _cancellationTokenSource.Token);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine($"[{exchangeName}] Exchange stopped gracefully");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[FATAL] [{exchangeName}] Exchange failed: {ex.Message}");
        // ❌ Задача умирает, но НИКТО об этом не узнает!
    }
}, _cancellationTokenSource.Token);

tasks.Add(task);
```

**Проблема:**
Если `ProcessExchange` падает с exception:
1. Task переходит в состояние `Faulted`
2. Логируется ошибка
3. **НО:** система продолжает работать как ни в чем не бывало
4. Мониторинг не покажет проблему (нет метрики "Exchange Down")

**Сценарий:**
- Bybit disconnects из-за сетевой ошибки
- Task падает → Bybit данные перестают поступать
- Пользователь видит устаревшие спреды для Bybit
- Через 2 часа замечает, что графики не обновляются

**Решение:**
```csharp
// Добавить в Program.cs:
services.AddHostedService<ExchangeHealthMonitor>();

// Новый сервис:
public class ExchangeHealthMonitor : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var health = _orchestration.GetExchangeHealth();
            foreach (var (exchange, status) in health)
            {
                if (status == "failed" || status == "stopped")
                {
                    _logger.LogCritical($"[HealthMonitor] Exchange {exchange} is {status}!");
                    // Можно отправить alert или попытаться restart
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }
}
```

---

### 🐛 ISSUE #13: Bybit OrderBook — Missing Server Timestamp
**Файл:** `BybitExchangeClient.cs:101-108`  
**Серьезность:** MEDIUM  
**Статус:** ⚠️ **ОТСУТСТВУЮЩАЯ ФИЧА**

**Код:**
```csharp
await onData(new SpreadData
{
    Exchange = "Bybit",
    Symbol = data.Data.Symbol,
    BestBid = bestBid.Price,
    BestAsk = bestAsk.Price
    // ❌ ServerTimestamp = null!
});
```

**Проблема:**
- Bybit API возвращает `Timestamp` в orderbook update
- Но мы его **игнорируем** и не передаем в `SpreadData`
- `OrchestrationService` использует **локальное** время вместо серверного

**Последствия (HFT):**
```
Bybit Server Time: 12:00:00.100
Network Latency:   + 50ms
Local Time:        12:00:00.150  ← используем это

Spread calculation:
  Bybit Ask (12:00:00.100) vs Gate Bid (12:00:00.150)
  Staleness = 50ms (фактически 0ms!)
```

Мы **переоцениваем** staleness и можем отбрасывать валидные spreads.

**Решение:**
```csharp
await onData(new SpreadData
{
    Exchange = "Bybit",
    Symbol = data.Data.Symbol,
    BestBid = bestBid.Price,
    BestAsk = bestAsk.Price,
    ServerTimestamp = data.Data.UpdateTime  // ← Добавить!
});
```

---

### 🔥 ISSUE #14: FleckWebSocketServer — Broadcast может блокировать
**Файл:** `FleckWebSocketServer.cs` (нужно проверить реализацию)  
**Серьезность:** MEDIUM  
**Статус:** ⚠️ **REQUIRES CHECK**

**Гипотеза:**
```csharp
_ = _webSocketServer.BroadcastRealtimeAsync(message);
```

Если `BroadcastRealtimeAsync` синхронно итерирует по всем подключенным клиентам:
```csharp
public async Task BroadcastRealtimeAsync(string message)
{
    foreach (var socket in _clients)  // ❌ Если один клиент медленный...
    {
        await socket.SendAsync(message);  // ← весь broadcast блокируется!
    }
}
```

**HFT Impact:**
- 100 подключенных клиентов
- 1 клиент на медленной сети (100ms latency)
- Каждый broadcast ждет 100ms × 100 = **10 секунд**!

**Решение (если подтвердится):**
```csharp
public Task BroadcastRealtimeAsync(string message)
{
    var tasks = _clients.Select(socket => 
        Task.Run(async () =>
        {
            try
            {
                await socket.SendAsync(message);
            }
            catch { /* log */ }
        })
    );
    
    return Task.WhenAll(tasks);  // Параллельная отправка
}
```

---

### 🧵 ISSUE #15: RollingWindowService — Cleanup может блокировать hot path
**Файл:** `RollingWindowService.cs:216-230`  
**Серьезность:** LOW-MEDIUM  
**Статус:** ⚠️ **PERFORMANCE**

**Код:**
```csharp
private void CleanupOldData(object? state)
{
    var now = DateTime.UtcNow;
    var threshold = now - _windowSize;

    var removedCount = _windows.EvictWhere((key, window) => window.WindowEnd < threshold);
    // ...
}
```

**EvictWhere реализация:**
```csharp
public int EvictWhere(Func<TKey, TValue, bool> predicate)
{
    var toRemove = _cache
        .Where(kvp => predicate(kvp.Key, kvp.Value.Value))  // ❌ Полная итерация!
        .Select(kvp => kvp.Key)
        .ToList();

    // ...удаление
}
```

**Проблема:**
- `_cache.Where()` на `ConcurrentDictionary` **НЕ** атомарно
- Во время итерации другие потоки могут добавлять/удалять элементы
- Если коллекция большая (10,000 окон), итерация занимает миллисекунды

**Сценарий:**
```
Cleanup Thread:              Hot Path Thread:
_cache.Where(...)            
  → iterate 5000 items       
                             _windows.TryGetValue(key)  ← ждет?
  → continue iterating       
                             _windows.AddOrUpdate(key)  ← конкурирует
```

`ConcurrentDictionary` использует fine-grained locking, но если cleanup итерирует **весь** словарь, это может вызвать lock contention.

**HFT Impact:**
- Cleanup запускается каждую минуту
- Если в момент cleanup приходит burst данных → latency spike

**Решение:**
Вместо `EvictWhere` использовать **lazy cleanup**:
```csharp
// В TryGetValue:
if (_cache.TryGetValue(key, out var entry))
{
    if (entry.Value.WindowEnd < DateTime.UtcNow - _windowSize)
    {
        // Удаляем старое окно "на месте"
        _cache.TryRemove(key, out _);
        return null;
    }
    // ...
}
```

Тогда active окна остаются, а stale удаляются при обращении (no iteration overhead).

---

## 🔍 Дополнительные находки (менее критичные)

### 📦 ISSUE #16: Binance Client — множественные backup файлы
**Файл:** `Exchanges/`  
**Серьезность:** LOW (Code Smell)  
**Статус:** ⚠️ **TECH DEBT**

**Найдено:**
- `BinanceExchangeClient.cs`
- `BinanceExchangeClient.cs.backup`
- `BinanceExchangeClient.cs.backup_works`
- `BinanceExchangeClient.cs.old_working`

**Проблема:**
- Захламление репозитория
- Неясно, какая версия актуальна
- Git history уже хранит все версии!

**Решение:**
Удалить backup файлы (git хранит историю).

---

### 🎯 ISSUE #17: Symbol Validation отсутствует
**Файл:** Все `ExchangeClient.cs`  
**Серьезность:** LOW  
**Статус:** ⚠️ **POTENTIAL CRASH**

**Проблема:**
Нигде не валидируется, что `symbol` не null/empty перед отправкой в API.

**Например:**
```csharp
public async Task<IEnumerable<SymbolInfo>> GetSymbolsAsync()
{
    var symbolsData = await _restClient.V5Api.ExchangeData.GetSpotSymbolsAsync();
    return symbolsData.Data.List.Select(s => new SymbolInfo
    {
        Name = s.Name,  // ❌ Что если s.Name == null?
        // ...
    });
}
```

**Сценарий:**
- Биржа возвращает некорректные данные (редко, но бывает)
- `s.Name = null`
- При нормализации: `null.Replace("/", "")` → **NullReferenceException**
- Весь exchange client падает

**Решение:**
```csharp
.Where(s => !string.IsNullOrEmpty(s.Name))
.Select(s => new SymbolInfo { ... })
```

---

## 📊 Сводная таблица всех проблем

| # | Проблема | Серьезность | Тип | Приоритет |
|---|----------|-------------|-----|-----------|
| 1 | Односторонний расчет спреда | HIGH | Logic | ✅ FIXED |
| 2 | Competing Consumers | CRITICAL | Architecture | ✅ FIXED |
| 3 | Symbol Normalization Inconsistency | MEDIUM | Logic | MEDIUM |
| 4 | LruCache Race Condition | MEDIUM | Concurrency | HIGH → Детализировано в #10 |
| 5 | Parquet Writer backpressure | LOW-MEDIUM | Performance | LOW |
| 6 | WebSocket fire-and-forget | LOW | Error Handling | LOW |
| 7 | Cleanup Timers Deadlock | LOW | Concurrency | LOW → Детализировано в #15 |
| 8 | Rolling Quantile Performance | LOW | Performance | LOW |
| 9 | RealTimeController Memory Leak | MEDIUM | Memory | MEDIUM |
| **10** | **LruCache неатомарный AddOrUpdate** | **CRITICAL** | **Data Race** | **🔴 URGENT** |
| **11** | **ParquetWriter buffer race** | **HIGH** | **Data Corruption** | **🔴 URGENT** |
| 12 | Zombie Exchange Tasks | MEDIUM | Monitoring | MEDIUM |
| 13 | Bybit missing ServerTimestamp | MEDIUM | HFT Accuracy | MEDIUM |
| 14 | FleckWebSocketServer blocking | MEDIUM | Performance | REQUIRES CHECK |
| 15 | Cleanup blocks hot path | LOW-MEDIUM | Performance | LOW |
| 16 | Backup files clutter | LOW | Tech Debt | LOW |
| 17 | Symbol Validation | LOW | Crash Prevention | MEDIUM |

---

## 🚨 Критичные исправления (URGENT)

### 1. Исправить LruCache.AddOrUpdate
```csharp
_cache.AddOrUpdate(key, 
    k => new CacheEntry { Value = value, LastAccessTick = tick },
    (k, old) => new CacheEntry { Value = value, LastAccessTick = tick }
);
```

### 2. Исправить ParquetDataWriter
```csharp
if (buffer.Count >= batchSize)
{
    var bufferCopy = new List<SpreadData>(buffer);
    buffer.Clear();
    
    _ = Task.Run(async () =>
    {
        try
        {
            Directory.CreateDirectory(hourlyPartitionDir);
            var filePath = Path.Combine(hourlyPartitionDir, $"spreads-{DateTime.UtcNow:mm-ss.fffffff}.parquet");
            await WriteSpreadsAsync(filePath, bufferCopy);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FATAL] ParquetWriter flush failed: {ex}");
        }
    });
}
```

### 3. Добавить Exchange Health Monitor
```csharp
services.AddHostedService<ExchangeHealthMonitor>();
```

---

## 🎯 Следующие шаги

1. **URGENT:** Исправить LruCache (#10) и ParquetWriter (#11) — **data corruption риск**
2. **HIGH:** Добавить Symbol Validation (#17) — простой fix, критичный для stability
3. **MEDIUM:** Проверить FleckWebSocketServer (#14) и добавить ExchangeHealthMonitor (#12)
4. **LOW:** Cleanup tech debt (#16) и оптимизации (#15)

**Хочешь, чтобы я:**
- A) Создал PROPOSAL с diff'ами для исправления #10 и #11?
- B) Проверил FleckWebSocketServer на blocking broadcast (#14)?
- C) Написал unit tests для LruCache, чтобы доказать race condition?
