# Refactoring Log - Exchange Clients Deduplication

**Проект:** Collections / SpreadAggregator
**Цель:** Устранение 65% дублирования кода в Exchange Clients (824 строки)
**Начало:** 2025-11-01

---

## Текущий статус

🟡 **Sprint 1 Day 1** - Подготовка и дизайн

**Прогресс:** 2/4 задач завершено

---

## Day-by-day Log

### 2025-11-01 - Day 1: Анализ и планирование

#### Выполнено ✅
- [x] Создан AUDIT-2025-11-01-collections-architecture.md
- [x] Создан AUDIT-SUMMARY.md
- [x] Создан REFACTORING-PLAN.md
- [x] Создан REFACTORING-LOG.md (этот файл)
- [x] Проанализированы все 8 Exchange Clients
- [x] Подтверждено использование JKorf библиотек
- [x] Построчное сравнение дублируемого кода

#### Метрики анализа
```
Файлов проанализировано: 8
Строк кода всего:        1264
Дублируемых строк:       824 (65%)
Уникальных строк:        440 (35%)
```

#### Ключевые находки
1. ✅ Все биржи используют JKorf с единым API паттерном
2. ⚠️ BingX имеет особенность: loop по символам (не batch subscribe)
3. ⚠️ API paths различаются: SpotApi, V5SpotApi, UnifiedApi, SpotApiV2
4. ⚠️ ChunkSize варьируется: от 6 (MEXC) до 100 (Kucoin, BingX, Bitget)
5. ✅ HandleConnectionLost идентичен на 100%
6. ✅ ManagedConnection структура идентична на 95%

#### Решения по архитектуре
- **Generic base class:** `ExchangeClientBase<TRestClient, TSocketClient>`
- **API path abstraction:** Interface-based (не reflection для производительности)
- **BingX workaround:** `SupportsMultipleSymbols` property
- **ChunkSize:** Abstract property (позже вынести в config)

#### Следующие шаги
- [ ] Создать ExchangeClientBase.cs
- [ ] Определить IExchangeSocketApi интерфейс
- [ ] Перенести общую логику в базовый класс
- [ ] Proof of concept: мигрировать BinanceExchangeClient

---

### 2025-11-01 - Продолжение (ожидание)

#### Планируется
- [ ] Создание Infrastructure/Services/Exchanges/Base/ExchangeClientBase.cs
- [ ] Создание Infrastructure/Services/Exchanges/Base/IExchangeSocketApi.cs
- [ ] Перенос SubscribeToTickersAsync в базовый класс
- [ ] Перенос ManagedConnection<TSocketClient>
- [ ] Тестовая миграция BinanceExchangeClient

#### Время
- Оценка: 2-3 часа
- Реальное: TBD

---

## Детали реализации

### Архитектурные решения

#### 1. Generic Constraints
```csharp
public abstract class ExchangeClientBase<TRestClient, TSocketClient>
    : IExchangeClient
    where TRestClient : class
    where TSocketClient : class, IDisposable
{
    // ...
}
```

**Почему `where TRestClient : class`?**
- Все JKorf RestClient классы - reference types
- Позволяет использовать null checks
- Совместимо с DI injection

**Почему `where TSocketClient : class, IDisposable`?**
- Все SocketClient реализуют IDisposable
- Необходимо для корректного Dispose в StopAsync

---

#### 2. API Path Abstraction

**Проблема:**
```
Binance:  client.SpotApi.UnsubscribeAllAsync()
Bybit:    client.V5SpotApi.UnsubscribeAllAsync()
OKX:      client.UnifiedApi.UnsubscribeAllAsync()
Bitget:   client.SpotApiV2.UnsubscribeAllAsync()
```

**Решение А (отклонено): Reflection**
```csharp
❌ protected virtual string ApiPath => "SpotApi";

var apiProperty = typeof(TSocketClient).GetProperty(ApiPath);
var api = apiProperty.GetValue(client);
// Медленно, не type-safe
```

**Решение Б (выбрано): Interface Adapter**
```csharp
✅ public interface IExchangeSocketApi
{
    Task UnsubscribeAllAsync();
    Task<CallResult<UpdateSubscription>> SubscribeToBookTickerAsync(
        IEnumerable<string> symbols,
        Action<DataEvent<...>> handler);
}

// В каждом клиенте создать адаптер:
protected override IExchangeSocketApi CreateSocketApi(TSocketClient client)
{
    return new BinanceSocketApiAdapter(client.SpotApi);
}
```

**Преимущества:**
- ✅ Type-safe
- ✅ Быстро (no reflection)
- ✅ IntelliSense поддержка
- ⚠️ Требует ~15 строк адаптера на биржу (но это единожды)

---

#### 3. ChunkSize Strategy

**Текущее состояние:**
```csharp
const int chunkSize = 20; // Хардкод в каждом классе
```

**Решение Phase 1 (базовый класс):**
```csharp
protected abstract int ChunkSize { get; }

// В BinanceExchangeClient:
protected override int ChunkSize => 20;
```

**Решение Phase 2 (конфигурация):**
```csharp
// appsettings.json:
"ExchangeSettings": {
  "Exchanges": {
    "Binance": {
      "ChunkSize": 20,
      "VolumeFilter": { ... }
    }
  }
}

// В базовом классе:
protected virtual int GetChunkSize(IConfiguration config)
{
    return config.GetValue<int>(
        $"ExchangeSettings:Exchanges:{ExchangeName}:ChunkSize",
        defaultValue: 20);
}
```

**План:**
- Sprint 1-2: Phase 1 (abstract property)
- Sprint 5: Phase 2 (конфигурация)

---

#### 4. BingX Special Case

**Проблема:** BingX не поддерживает batch subscribe
```csharp
// Остальные биржи:
await client.SubscribeToBookTickerAsync(
    new[] { "BTCUSDT", "ETHUSDT", "BNBUSDT" },  // ← Batch!
    handler);

// BingX:
foreach (var symbol in symbols)  // ← По одному!
{
    await client.SubscribeToBookTickerAsync(symbol, handler);
}
```

**Решение:**
```csharp
// В базовом классе:
protected virtual bool SupportsMultipleSymbols => true;

protected async Task SubscribeToSymbols(
    IExchangeSocketApi api,
    List<string> symbols,
    Action<SpreadData> callback)
{
    if (SupportsMultipleSymbols)
    {
        await SubscribeToTickersCore(api, symbols, callback);
    }
    else
    {
        foreach (var symbol in symbols)
        {
            await SubscribeToTickersCore(api, new[] { symbol }, callback);
        }
    }
}

// В BingXExchangeClient:
protected override bool SupportsMultipleSymbols => false;
```

---

### Шаблон для новой биржи (после рефакторинга)

```csharp
using NewExchange.Net.Clients;
using SpreadAggregator.Infrastructure.Services.Exchanges.Base;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

public class NewExchangeClient : ExchangeClientBase<
    NewExchangeRestClient,
    NewExchangeSocketClient>
{
    // 1. Свойства (4 строки)
    protected override string ExchangeName => "NewExchange";
    protected override int ChunkSize => 20;
    protected override bool SupportsTradesStream => false;
    protected override bool SupportsMultipleSymbols => true;

    // 2. Конструктор (3 строки)
    public NewExchangeClient() : base(
        () => new NewExchangeRestClient(),
        () => new NewExchangeSocketClient())
    {
    }

    // 3. API Adapter (12 строк)
    protected override IExchangeSocketApi CreateSocketApi(
        NewExchangeSocketClient client)
    {
        return new NewExchangeSocketApiAdapter(client.SpotApi);
    }

    private class NewExchangeSocketApiAdapter : IExchangeSocketApi
    {
        private readonly INewExchangeSocketApi _api;

        public NewExchangeSocketApiAdapter(INewExchangeSocketApi api)
            => _api = api;

        public Task UnsubscribeAllAsync()
            => _api.UnsubscribeAllAsync();

        // ... остальные методы
    }

    // 4. Маппинг данных (15 строк)
    protected override Task<CallResult> SubscribeToTickersCore(
        IExchangeSocketApi api,
        IEnumerable<string> symbols,
        Action<SpreadData> callback)
    {
        return ((NewExchangeSocketApiAdapter)api)._api
            .SubscribeToBookTickerUpdatesAsync(symbols, data =>
            {
                callback(new SpreadData
                {
                    Exchange = ExchangeName,
                    Symbol = data.Data.Symbol,
                    BestBid = data.Data.BestBidPrice,
                    BestAsk = data.Data.BestAskPrice
                });
            });
    }
}
```

**Итого: ~34 строки** вместо 185!

---

## Проблемы и решения

### Проблема 1: Различные типы данных в JKorf библиотеках

**Описание:**
```csharp
// Binance:
DataEvent<BinanceStreamBookPrice> data

// Bybit:
DataEvent<BybitOrderBookEntry> data
data.Data.Bids.FirstOrDefault()

// OKX:
DataEvent<OKXTicker> data
data.Data.BestBidPrice (nullable)
```

**Решение:** Generic метод с callback маппингом
```csharp
protected abstract Task<CallResult> SubscribeToTickersCore<TData>(
    IExchangeSocketApi api,
    IEnumerable<string> symbols,
    Action<SpreadData> callback);
```

---

### Проблема 2: Nullable QuoteVolume в GetTickersAsync

**Код:**
```csharp
// Kucoin, MEXC:
QuoteVolume = t.QuoteVolume ?? 0

// Остальные:
QuoteVolume = t.QuoteVolume
```

**Решение:** В базовом классе всегда использовать `?? 0`
```csharp
protected virtual decimal GetQuoteVolume(dynamic ticker)
{
    return ticker.QuoteVolume ?? 0m;
}
```

---

## Метрики прогресса

### Код

| Метрика | Начало | Текущее | Цель | Прогресс |
|---------|--------|---------|------|----------|
| Total lines | 1264 | 1264 | 535 | 0% |
| Duplicated lines | 824 | 824 | 0 | 0% |
| Files count | 8 | 8 | 9 (+ Base) | 0% |
| Avg lines/file | 158 | 158 | 40 | 0% |

### Биржи

| Биржа | Статус | Строк до | Строк после | Сокращение |
|-------|--------|----------|-------------|------------|
| Binance | ⏸️ Ожидание | 185 | - | - |
| Bybit | ⏸️ Ожидание | 154 | - | - |
| GateIo | ⏸️ Ожидание | 185 | - | - |
| OKX | ⏸️ Ожидание | 150 | - | - |
| BingX | ⏸️ Ожидание | 154 | - | - |
| Kucoin | ⏸️ Ожидание | 149 | - | - |
| Bitget | ⏸️ Ожидание | 152 | - | - |
| MEXC | ⏸️ Ожидание | 152 | - | - |

**Легенда:**
- ⏸️ Ожидание
- 🟡 В работе
- ✅ Завершено
- ❌ Проблемы

---

## Время выполнения

### Оценки vs Реальное

| Задача | Оценка | Реальное | Отклонение |
|--------|--------|----------|------------|
| Анализ и планирование | 2 часа | 1.5 часа | -25% ✅ |
| Создание базового класса | 3 часа | - | - |
| Миграция Binance | 1 час | - | - |
| Миграция остальных (x7) | 7 часов | - | - |
| Тестирование | 2 часа | - | - |
| **ИТОГО** | **15 часов** | **1.5 часа** | **10%** |

---

## Блокеры и риски

### Текущие блокеры
*Нет*

### Потенциальные риски
1. ⚠️ **Различия в JKorf API между версиями библиотек**
   - Вероятность: Низкая
   - Митигация: Проверить версии всех пакетов

2. ⚠️ **Производительность при использовании Interface вместо прямых вызовов**
   - Вероятность: Очень низкая
   - Митигация: Бенчмарк после реализации

3. ⚠️ **Регрессия функциональности при миграции**
   - Вероятность: Средняя
   - Митигация: Тестирование каждой биржи после миграции

---

## Решения для будущего

### После завершения базового рефакторинга

1. **ChunkSize в конфигурацию**
   - Приоритет: Средний
   - Оценка: 2 часа
   - Выгода: Не нужно пересобирать для изменения chunkSize

2. **ILogger вместо Console.WriteLine**
   - Приоритет: Высокий
   - Оценка: 1 день
   - Выгода: Structured logging, фильтрация, production-ready

3. **Assembly Scanning для автоматической регистрации**
   - Приоритет: Средний
   - Оценка: 4 часа
   - Выгода: Добавление биржи не требует изменения Program.cs

4. **Health Checks для мониторинга бирж**
   - Приоритет: Средний
   - Оценка: 1 день
   - Выгода: Visibility в production

---

## Заметки

### 2025-11-01
- JKorf действительно создал унифицированный API! 🎉
- Дублирование кода не связано с особенностями бирж
- BingX единственная особенная (loop вместо batch)
- MEXC имеет самый маленький chunkSize=6 из-за message size limit
- Все SocketClient реализуют IDisposable (удобно для generic constraint)

---

## Следующая запись

*(Будет добавлена после начала реализации базового класса)*

**Ожидаемая дата:** 2025-11-01 (продолжение)
**Планируемые задачи:**
- Создание ExchangeClientBase.cs
- Создание IExchangeSocketApi.cs
- Миграция BinanceExchangeClient (proof of concept)
