# План рефакторинга Collections - Устранение дублирования Exchange Clients

**Дата начала:** 2025-11-01
**Статус:** 🟡 В работе

---

## Контекст

Все 8 биржевых клиентов используют библиотеки **JKorf** с унифицированным API:
- ✅ Binance.Net
- ✅ Bybit.Net
- ✅ Kucoin.Net
- ✅ Bitget.Net
- ✅ BingX.Net
- ✅ GateIo.Net
- ✅ Mexc.Net
- ✅ OKX.Net

**Ключевое преимущество:** JKorf специально создал одинаковую структуру API для всех бирж → дублирование кода не связано с особенностями бирж, а является техническим долгом.

---

## Анализ дублирования (детальный)

### Идентичные блоки кода (построчное сравнение)

#### Блок 1: Поля и конструктор (100% идентичны)
```csharp
private readonly List<ManagedConnection> _connections = new List<ManagedConnection>();
private Action<SpreadData>? _onData;

public {Exchange}ExchangeClient()
{
    _restClient = new {Exchange}RestClient();
}
```
**Встречается:** 8/8 файлов
**Строк на файл:** ~5
**Всего дублируемых строк:** 40

---

#### Блок 2: SubscribeToTickersAsync (95% идентичны)
```csharp
public async Task SubscribeToTickersAsync(IEnumerable<string> symbols, Action<SpreadData> onData)
{
    _onData = onData;

    foreach (var connection in _connections)
    {
        await connection.StopAsync();
    }
    _connections.Clear();

    var symbolsList = symbols.ToList();
    const int chunkSize = {VARIES};  // ← ЕДИНСТВЕННОЕ РАЗЛИЧИЕ

    for (int i = 0; i < symbolsList.Count; i += chunkSize)
    {
        var chunk = symbolsList.Skip(i).Take(chunkSize).ToList();
        if (chunk.Any())
        {
            var connection = new ManagedConnection(chunk, _onData);
            _connections.Add(connection);
        }
    }

    await Task.WhenAll(_connections.Select(c => c.StartAsync()));
}
```
**Встречается:** 8/8 файлов
**Строк на файл:** ~25
**Всего дублируемых строк:** 200

**ChunkSize различия:**
| Биржа | ChunkSize | Причина |
|-------|-----------|---------|
| Binance | 20 | Документированный лимит JKorf |
| Bybit | 10 | Документированный лимит |
| GateIo | 30 | Документированный лимит |
| OKX | 20 | 20% от официального лимита 100 |
| BingX | 100 | Conservative estimate |
| Kucoin | 100 | 20% от официального лимита 100 |
| Bitget | 100 | Не задокументировано |
| MEXC | 6 | Message size limit 1024 bytes! |

---

#### Блок 3: SubscribeToTradesAsync (100% идентичны)
```csharp
public Task SubscribeToTradesAsync(IEnumerable<string> symbols, Action<TradeData> onData)
{
    // Not implemented for this exchange yet.
    return Task.CompletedTask;
}
```
**Встречается:** 6/8 файлов (Binance, GateIo реализовали)
**Строк на файл:** ~4
**Всего дублируемых строк:** 24

---

#### Блок 4: ManagedConnection класс - конструктор (100% идентичны)
```csharp
private class ManagedConnection
{
    private readonly List<string> _symbols;
    private readonly Action<SpreadData> _onData;
    private readonly {Exchange}SocketClient _socketClient;
    private readonly SemaphoreSlim _resubscribeLock = new SemaphoreSlim(1, 1);

    public ManagedConnection(List<string> symbols, Action<SpreadData> onData)
    {
        _symbols = symbols;
        _onData = onData;
        _socketClient = new {Exchange}SocketClient();
    }
}
```
**Встречается:** 8/8 файлов
**Строк на файл:** ~13
**Всего дублируемых строк:** 104

---

#### Блок 5: StartAsync, StopAsync (100% идентичны по логике)
```csharp
public async Task StartAsync()
{
    await SubscribeInternalAsync();
}

public async Task StopAsync()
{
    await _socketClient.{Api}.UnsubscribeAllAsync();  // ← API path варьируется
    _socketClient.Dispose();
}
```
**Встречается:** 8/8 файлов
**Строк на файл:** ~9
**Всего дублируемых строк:** 72

**API path различия:**
| Биржа | API Path |
|-------|----------|
| Binance | `SpotApi` |
| Bybit | `V5SpotApi` |
| GateIo | `SpotApi` |
| OKX | `UnifiedApi` |
| BingX | `SpotApi` |
| Kucoin | `SpotApi` |
| Bitget | `SpotApiV2` |
| MEXC | `SpotApi` |

---

#### Блок 6: HandleConnectionLost (100% идентичны)
```csharp
private async void HandleConnectionLost()
{
    await _resubscribeLock.WaitAsync();
    try
    {
        Console.WriteLine($"[{Exchange}] Connection lost for chunk starting with {_symbols.FirstOrDefault()}. Attempting to resubscribe...");
        await Task.Delay(1000);
        await SubscribeInternalAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] [{Exchange}] Failed to resubscribe for chunk: {ex.Message}");
    }
    finally
    {
        _resubscribeLock.Release();
    }
}
```
**Встречается:** 8/8 файлов
**Строк на файл:** ~18
**Всего дублируемых строк:** 144

---

#### Блок 7: SubscribeInternalAsync - структура (80% идентичны)

Общая структура:
```csharp
private async Task SubscribeInternalAsync()
{
    Console.WriteLine($"[{Exchange}ExchangeClient] Subscribing to a chunk of {_symbols.Count} symbols.");

    await _socketClient.{Api}.UnsubscribeAllAsync();

    var result = await _socketClient.{Api}.Subscribe{Method}Async(_symbols, {depth?}, data =>
    {
        // МАППИНГ ДАННЫХ ← УНИКАЛЬНАЯ ЧАСТЬ (10-20 строк)
    });

    if (!result.Success)
    {
        Console.WriteLine($"[ERROR] [{Exchange}] Failed to subscribe...: {result.Error}");
    }
    else
    {
        Console.WriteLine($"[{Exchange}] Successfully subscribed...");
        result.Data.ConnectionLost += HandleConnectionLost;
        result.Data.ConnectionRestored += (t) => Console.WriteLine($"[{Exchange}] Connection restored...");
    }
}
```
**Встречается:** 8/8 файлов
**Строк на файл:** ~35
**Уникальных строк на файл:** ~15 (маппинг)
**Дублируемых строк:** 20 × 8 = **160**

---

### Таблица различий в Subscribe методах

| Биржа | API Path | Метод | Параметр depth | Маппинг сложность |
|-------|----------|-------|----------------|-------------------|
| Binance | `SpotApi.ExchangeData` | `SubscribeToBookTickerUpdatesAsync` | ❌ | Простой (data.Data.Symbol) |
| Bybit | `V5SpotApi` | `SubscribeToOrderbookUpdatesAsync` | ✅ depth=1 | Средний (FirstOrDefault на Bids/Asks) |
| GateIo | `SpotApi` | `SubscribeToBookTickerUpdatesAsync` | ❌ | Простой |
| OKX | `UnifiedApi.ExchangeData` | `SubscribeToTickerUpdatesAsync` | ❌ | Средний (nullable checks) |
| BingX | `SpotApi` | `SubscribeToBookPriceUpdatesAsync` | ❌ | Простой (loop по символам!) |
| Kucoin | `SpotApi` | `SubscribeToBookTickerUpdatesAsync` | ❌ | Средний (nullable checks) |
| Bitget | `SpotApiV2` | `SubscribeToOrderBookUpdatesAsync` | ✅ depth=1 | Средний (FirstOrDefault) |
| MEXC | `SpotApi` | `SubscribeToBookTickerUpdatesAsync` | ❌ | Простой (null checks) |

---

### Итоговая статистика дублирования

| Блок кода | Дублируемых строк | % от общего |
|-----------|-------------------|-------------|
| Поля и конструктор | 40 | 3% |
| SubscribeToTickersAsync | 200 | 16% |
| SubscribeToTradesAsync | 24 | 2% |
| ManagedConnection конструктор | 104 | 8% |
| StartAsync, StopAsync | 72 | 6% |
| HandleConnectionLost | 144 | 11% |
| SubscribeInternalAsync (структура) | 160 | 13% |
| GetSymbolsAsync, GetTickersAsync | 80 | 6% |
| **ИТОГО ДУБЛИРУЕМЫХ** | **824** | **65%** |
| Уникальный код (маппинг, API вызовы) | 440 | 35% |
| **ВСЕГО СТРОК** | **1264** | **100%** |

---

## Решение: ExchangeClientBase с Generic Abstractions

### Архитектура

```
┌──────────────────────────────────────────────────────┐
│ ExchangeClientBase<TRestClient, TSocketClient>       │
│                                                       │
│ ┌──────────────────────────────────────────────────┐ │
│ │ ОБЩИЙ КОД (824 строки)                           │ │
│ │ • SubscribeToTickersAsync (chunking logic)       │ │
│ │ • SubscribeToTradesAsync (stub)                  │ │
│ │ • ManagedConnection<TSocketClient>               │ │
│ │   - StartAsync, StopAsync                        │ │
│ │   - HandleConnectionLost                         │ │
│ │   - SubscribeInternalAsync (orchestration)       │ │
│ ├──────────────────────────────────────────────────┤ │
│ │ АБСТРАКТНЫЕ СВОЙСТВА                             │ │
│ │ • string ExchangeName { get; }                   │ │
│ │ • int ChunkSize { get; }                         │ │
│ │ • string ApiPath { get; }                        │ │
│ ├──────────────────────────────────────────────────┤ │
│ │ АБСТРАКТНЫЕ МЕТОДЫ (для переопределения)         │ │
│ │ • Task<CallResult> SubscribeToTickersCore(...)   │ │
│ │ • Task<CallResult> SubscribeToTradesCore(...)    │ │
│ │ • Task UnsubscribeAllAsync(TSocketClient)        │ │
│ └──────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────┘
                        ▲
                        │ наследует
                        │
┌───────────────────────┴──────────────────────────────┐
│ BinanceExchangeClient :                              │
│   ExchangeClientBase<BinanceRestClient,              │
│                      BinanceSocketClient>            │
│                                                       │
│ protected override string ExchangeName => "Binance"; │
│ protected override int ChunkSize => 20;              │
│ protected override string ApiPath => "SpotApi";      │
│                                                       │
│ protected override Task<CallResult>                  │
│   SubscribeToTickersCore(...) {                      │
│     return client.SpotApi.ExchangeData               │
│       .SubscribeToBookTickerUpdatesAsync(            │
│         symbols, data => callback(MapData(data)));   │
│   }                                                   │
│                                                       │
│ private SpreadData MapData(DataEvent data) { ... }   │
└──────────────────────────────────────────────────────┘

РЕЗУЛЬТАТ: 1264 строки → ~350 строк (72% сокращение)
```

---

## Sprint Plan

### Sprint 1: Создание базового класса (2-3 дня)

#### Day 1: Подготовка и дизайн

**Задачи:**
- [x] Создать REFACTORING-PLAN.md
- [ ] Создать ExchangeClientBase.cs
- [ ] Определить generic constraints
- [ ] Определить абстрактные методы/свойства

**Deliverables:**
```csharp
// Infrastructure/Services/Exchanges/Base/ExchangeClientBase.cs
public abstract class ExchangeClientBase<TRestClient, TSocketClient>
    : IExchangeClient
    where TRestClient : class
    where TSocketClient : class, IDisposable
{
    protected abstract string ExchangeName { get; }
    protected abstract int ChunkSize { get; }

    // ... остальные абстракции
}
```

---

#### Day 2: Реализация общей логики

**Задачи:**
- [ ] Перенести SubscribeToTickersAsync в базовый класс
- [ ] Создать ManagedConnection<TSocketClient>
- [ ] Перенести HandleConnectionLost
- [ ] Определить ISocketApiAccessor для унификации API paths

**Проблема:** API paths различаются (SpotApi, V5SpotApi, UnifiedApi)

**Решение 1 - Reflection (гибко, но медленно):**
```csharp
protected virtual async Task UnsubscribeAllAsync(TSocketClient client)
{
    var apiProperty = typeof(TSocketClient).GetProperty(ApiPath);
    var api = apiProperty.GetValue(client);
    var method = api.GetType().GetMethod("UnsubscribeAllAsync");
    await (Task)method.Invoke(api, null);
}
```

**Решение 2 - Interface (быстро, безопасно):** ✅ ВЫБИРАЕМ ЭТО
```csharp
public interface IExchangeSocketApi
{
    Task UnsubscribeAllAsync();
}

// В каждом клиенте:
protected override IExchangeSocketApi GetSocketApi(BinanceSocketClient client)
    => new BinanceSocketApiAdapter(client.SpotApi);

// Адаптер:
class BinanceSocketApiAdapter : IExchangeSocketApi
{
    private readonly IBinanceSocketClientSpotApi _api;
    public BinanceSocketApiAdapter(IBinanceSocketClientSpotApi api) => _api = api;
    public Task UnsubscribeAllAsync() => _api.UnsubscribeAllAsync();
}
```

---

#### Day 3: Рефакторинг первой биржи (proof of concept)

**Задачи:**
- [ ] Рефакторить BinanceExchangeClient → использовать ExchangeClientBase
- [ ] Запустить проект, проверить что всё работает
- [ ] Измерить метрики (строки кода до/после)

**Ожидаемый результат:**
```
BinanceExchangeClient.cs
До:  185 строк
После: 45 строк (24% от оригинала)
```

---

### Sprint 2: Миграция остальных бирж (3-4 дня)

#### Day 4-5: Простые биржи (BookTicker API)

**Очередность (от простого к сложному):**
1. [ ] GateIoExchangeClient (идентичен Binance)
2. [ ] MexcExchangeClient (идентичен Binance + chunkSize=6)
3. [ ] KucoinExchangeClient (nullable checks)

**Критерий готовности:** Каждая биржа < 50 строк

---

#### Day 6: Средние биржи (OrderBook API)

4. [ ] BybitExchangeClient (depth parameter, FirstOrDefault)
5. [ ] BitgetExchangeClient (аналогично Bybit)

**Особенности:**
- Передача `depth: 1` в Subscribe метод
- Маппинг `Bids.FirstOrDefault()`, `Asks.FirstOrDefault()`

---

#### Day 7: Сложные биржи

6. [ ] OkxExchangeClient (nullable checks, UnifiedApi)
7. [ ] BingXExchangeClient (⚠️ **loop по символам!**)

**BingX особенность:**
```csharp
// Текущий код:
foreach (var symbol in _symbols)
{
    var result = await _socketClient.SpotApi
        .SubscribeToBookPriceUpdatesAsync(symbol, data => ...);
}
```

**Решение:** Абстрактный метод `SupportsMultipleSymbols`:
```csharp
protected virtual bool SupportsMultipleSymbols => true;

// В BingXExchangeClient:
protected override bool SupportsMultipleSymbols => false;

// В базовом классе:
if (SupportsMultipleSymbols)
    await SubscribeToTickersCore(client, symbols, callback);
else
    foreach (var symbol in symbols)
        await SubscribeToTickersCore(client, new[] { symbol }, callback);
```

---

### Sprint 3: Trades Implementation (2 дня)

**Текущий статус:** Только Binance и GateIo реализовали Trades

**Задачи:**
- [ ] Добавить базовую реализацию SubscribeToTradesAsync
- [ ] Реализовать TradesCore для Binance
- [ ] Реализовать TradesCore для GateIo
- [ ] (Опционально) Добавить для остальных бирж

**Пример:**
```csharp
// В базовом классе:
public async Task SubscribeToTradesAsync(
    IEnumerable<string> symbols,
    Action<TradeData> onData)
{
    if (!SupportsTradesStream)
    {
        Console.WriteLine($"[{ExchangeName}] Trades stream not implemented yet.");
        return;
    }

    // Аналогично SubscribeToTickersAsync
    await SetupConnectionsForTrades(symbols, onData);
}

// В производных классах:
protected virtual bool SupportsTradesStream => false;

// BinanceExchangeClient:
protected override bool SupportsTradesStream => true;
```

---

### Sprint 4: Cleanup & Polish (1 день)

**Задачи:**
- [ ] Удалить старые версии Exchange Clients (backup в git)
- [ ] Обновить DI регистрацию (если требуется)
- [ ] Добавить XML документацию
- [ ] Code review
- [ ] Обновить AUDIT.md с результатами

---

## Метрики успеха

### До рефакторинга
```
Total lines:           1264
Duplicated lines:       824 (65%)
Unique lines:           440 (35%)
Files:                    8
Average lines/file:     158
```

### После рефакторинга (прогноз)
```
Base class:            220 строк
Binance client:         35 строк
Bybit client:           40 строк
GateIo client:          35 строк
OKX client:             40 строк
BingX client:           50 строк (foreach loop)
Kucoin client:          38 строк
Bitget client:          40 строк
MEXC client:            37 строк
───────────────────────────────
Total lines:           535 строк
Reduction:             729 строк (58% сокращение)
Maintainability:       +++
Extensibility:         +++
```

### Добавление новой биржи

**До рефакторинга:**
```
1. Copy-paste один из существующих клиентов (185 строк)
2. Find-replace названия биржи
3. Поменять API вызовы (~20 строк)
4. Настроить chunkSize
Итого: ~30 минут работы, 185 строк кода
```

**После рефакторинга:**
```
1. Создать класс наследник ExchangeClientBase
2. Переопределить 4 свойства (ExchangeName, ChunkSize, etc)
3. Реализовать SubscribeToTickersCore (~15 строк)
Итого: ~15 минут работы, 35 строк кода
```

---

## Риски и митигация

### Риск 1: Производительность reflection
**Вероятность:** Низкая
**Влияние:** Среднее
**Митигация:** Используем Interface-based подход вместо reflection

### Риск 2: Особенности BingX (loop по символам)
**Вероятность:** Средняя
**Влияние:** Низкое
**Митигация:** Абстрактное свойство `SupportsMultipleSymbols`

### Риск 3: Регрессия функциональности
**Вероятность:** Средняя
**Влияние:** Высокое
**Митигация:**
- Тестирование каждой биржи после миграции
- Сравнение поведения до/после (логи)
- Постепенная миграция (по одной бирже)

### Риск 4: Новые требования JKorf к API
**Вероятность:** Низкая
**Влияние:** Низкое
**Митигация:** JKorf поддерживает обратную совместимость

---

## Checklist финального результата

### Код
- [ ] ExchangeClientBase создан и протестирован
- [ ] 8 биржевых клиентов мигрированы
- [ ] Все клиенты < 50 строк
- [ ] Удалены старые версии файлов
- [ ] XML документация добавлена

### Тестирование
- [ ] Проект компилируется без ошибок
- [ ] Все биржи подключаются успешно
- [ ] WebSocket subscriptions работают
- [ ] Reconnection logic работает
- [ ] Логи показывают корректную работу

### Документация
- [ ] Обновлен AUDIT.md с результатами
- [ ] Создан REFACTORING-LOG.md с деталями
- [ ] Добавлены комментарии в код
- [ ] README обновлён (если требуется)

---

## Следующие шаги после завершения

1. **ChunkSize в конфигурацию** (вынести в appsettings.json)
2. **Assembly Scanning** для автоматической регистрации бирж
3. **ILogger вместо Console.WriteLine**
4. **Разбить ParquetDataWriter** на компоненты

---

**Статус обновлений:**
- [ ] Sprint 1 начат
- [ ] Sprint 1 завершён
- [ ] Sprint 2 начат
- [ ] Sprint 2 завершён
- [ ] Sprint 3 начат
- [ ] Sprint 3 завершён
- [ ] Sprint 4 завершён
- [ ] Финальная проверка пройдена

