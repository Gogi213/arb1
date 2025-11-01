# Краткое резюме аудита Collections

## Архитектура проекта

```
SpreadAggregator/
├── Domain/               ← Доменная модель
│   ├── Entities/        (SpreadData, TradeData, MarketData)
│   └── Services/        (SpreadCalculator, VolumeFilter)
├── Application/         ← Сервисы и абстракции
│   ├── Abstractions/    (IExchangeClient, IDataWriter)
│   └── Services/        (OrchestrationService, DataCollectorService)
├── Infrastructure/      ← Внешние интеграции
│   └── Services/
│       ├── Exchanges/   (8 биржевых клиентов)
│       ├── ParquetDataWriter
│       └── FleckWebSocketServer
└── Presentation/        ← Entry point
    └── Program.cs
```

---

## Top 10 проблем

| # | Проблема | Приоритет | Влияние | Усилия |
|---|----------|-----------|---------|--------|
| 1 | **85% дублирования кода** в 8 Exchange Clients (~1200 строк) | 🔴 Критично | Memory/Maintainability | 3-5 дней |
| 2 | **UnboundedChannel** → потенциальный Memory Leak | 🔴 Критично | Stability | 1 час |
| 3 | **IExchangeClient несогласован** с реализациями | 🔴 Критично | Correctness | 2 часа |
| 4 | **Fire-and-Forget** в OrchestrationServiceHost | 🔴 Критично | Error Handling | 3 часа |
| 5 | **3 файла Class1.cs** (мертвый код) | 🟠 Высокий | Clutter | 5 минут |
| 6 | **ParquetDataWriter God Class** (6 ответственностей) | 🟠 Высокий | Maintainability | 2 дня |
| 7 | **Отсутствие ILogger** (везде Console.WriteLine) | 🟠 Высокий | Observability | 1 день |
| 8 | **DataCollectorService** - лишний прокси-слой | 🟡 Средний | Simplicity | 1 час |
| 9 | **Хардкод регистрации бирж** (нет Assembly Scanning) | 🟡 Средний | Extensibility | 4 часа |
| 10 | **Отсутствие Health Checks** | 🟢 Низкий | Monitoring | 1 день |

---

## Визуализация дублирования кода

### Exchange Clients - 85% идентичного кода

```
┌─────────────────────────────────────────────────────┐
│ BinanceExchangeClient    (185 строк)               │
│ ┌─────────────────────────────────────────────────┐ │
│ │ ДУБЛИРУЕМЫЙ КОД (85%)                           │ │
│ │ • ManagedConnection class                       │ │
│ │ • Setup connections logic                       │ │
│ │ • HandleConnectionLost                          │ │
│ │ • Chunking strategy                             │ │
│ │                                         ~157 ст │ │
│ ├─────────────────────────────────────────────────┤ │
│ │ УНИКАЛЬНЫЙ КОД (15%)                            │ │
│ │ • API вызовы Binance.Net                        │ │
│ │ • Mapping в SpreadData                 ~28 ст  │ │
│ └─────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘

    ↕ ПОВТОРЯЕТСЯ 8 РАЗ

┌─────────────────────────────────────────────────────┐
│ BybitExchangeClient     (154 строки)                │
│ GateIoExchangeClient    (185 строк)                 │
│ OkxExchangeClient       (150 строк)                 │
│ BingXExchangeClient     (154 строки)                │
│ KucoinExchangeClient    (149 строк)                 │
│ BitgetExchangeClient    (~150 строк)                │
│ MexcExchangeClient      (~150 строк)                │
└─────────────────────────────────────────────────────┘

ИТОГО: ~1200 строк дублируемого кода из ~1400 общих
```

### Решение: ExchangeClientBase

```
┌─────────────────────────────────────────────────────┐
│ ExchangeClientBase<TRest, TSocket>                  │
│ ┌─────────────────────────────────────────────────┐ │
│ │ ОБЩИЙ КОД (переиспользуемый)                    │ │
│ │ • ManagedConnection                             │ │
│ │ • SetupConnections                              │ │
│ │ • HandleConnectionLost                          │ │
│ │ • Chunking strategy                   ~200 ст  │ │
│ ├─────────────────────────────────────────────────┤ │
│ │ АБСТРАКТНЫЕ МЕТОДЫ                              │ │
│ │ • SubscribeToTickersCore()                      │ │
│ │ • SubscribeToTradesCore()             ~20 ст   │ │
│ └─────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘
                      ▲
                      │ наследует
                      │
┌─────────────────────┴───────────────────────────────┐
│ BinanceExchangeClient : ExchangeClientBase          │
│ • ChunkSize = 20                                    │
│ • SubscribeToTickersCore() { ... }       ~30 ст    │
└─────────────────────────────────────────────────────┘

ЭКОНОМИЯ: 1400 строк → 350 строк (75% сокращение)
```

---

## Граф зависимостей (проблемные места)

```
OrchestrationService
├─ IWebSocketServer ✅
├─ SpreadCalculator ⚠️ (слишком тонкий)
├─ VolumeFilter ⚠️ (слишком тонкий)
├─ IConfiguration 🔴 (прямая зависимость - нарушение DI)
├─ IEnumerable<IExchangeClient> ✅
├─ Channel<MarketData> 🔴 (UNBOUNDED! Memory leak!)
└─ IDataWriter? ✅

ParquetDataWriter 🔴 GOD CLASS
├─ Ответственность 1: Запись Parquet
├─ Ответственность 2: Чтение Parquet
├─ Ответственность 3: Буферизация
├─ Ответственность 4: Партиционирование
├─ Ответственность 5: Фоновый сбор
└─ Ответственность 6: Flush при shutdown

DataCollectorService 🔴 PROXY (лишний)
└─ 100% делегирует IDataWriter.InitializeCollectorAsync

OrchestrationServiceHost 🔴 FIRE-AND-FORGET
└─ _ = _orchestrationService.StartAsync() (проглатывает ошибки)
```

---

## Метрики технического долга

### По категориям

```
ZERO CODE (мертвый код):
▓▓░░░░░░░░ 3 файла (Class1.cs)

ДУБЛИРОВАНИЕ:
▓▓▓▓▓▓▓▓▓░ 85% (1200/1400 строк в Exchange Clients)

ИЗБЫТОЧНОСТЬ:
▓▓▓░░░░░░░ 3 класса (SpreadCalculator, VolumeFilter, DataCollectorService)

ЛИШНИЕ СЛОИ:
▓▓░░░░░░░░ 2 класса (DataCollectorService, OrchestrationServiceHost)

ОТСУТСТВУЮЩИЕ АБСТРАКЦИИ:
▓▓▓▓▓░░░░░ Retry, Logging, Health Checks, Metrics

СМЕШЕНИЕ ОТВЕТСТВЕННОСТИ:
▓▓▓▓░░░░░░ ParquetDataWriter (6 SRP violations)
```

### По влиянию на production

```
КРИТИЧНОСТЬ (может упасть в production):
🔴 UnboundedChannel Memory Leak          HIGH
🔴 Fire-and-Forget exceptions            HIGH
🔴 No retry на GetTickersAsync           MEDIUM
🔴 No graceful shutdown                  MEDIUM

MAINTAINABILITY (сложность изменений):
🟠 85% дублирования                      EXTREME
🟠 God Class ParquetDataWriter           HIGH
🟠 Хардкод регистрации бирж              MEDIUM

OBSERVABILITY (видимость проблем):
🟡 Console.WriteLine вместо ILogger      HIGH
🟡 No metrics                            HIGH
🟡 No health checks                      MEDIUM
```

---

## План рефакторинга (быстрые победы)

### 🏆 Quick Wins (< 1 час)

```bash
# 1. Удалить мертвый код (5 минут)
rm Application/Class1.cs
rm Domain/Class1.cs
rm Infrastructure/Class1.cs

# 2. BoundedChannel (15 минут)
- services.AddSingleton(Channel.CreateUnbounded<MarketData>());
+ var options = new BoundedChannelOptions(10000) {
+     FullMode = BoundedChannelFullMode.DropOldest
+ };
+ services.AddSingleton(Channel.CreateBounded<MarketData>(options));

# 3. Синхронизировать IExchangeClient (20 минут)
- Task SubscribeAsync(...);
+ Task SubscribeToTickersAsync(...);
+ Task SubscribeToTradesAsync(...);

# 4. Убрать DataCollectorService (10 минут)
- services.AddHostedService<DataCollectorService>();
+ services.AddHostedService(sp => sp.GetRequiredService<ParquetDataWriter>());
```

### 🔧 Medium Effort (1-3 дня)

```bash
# 5. ExchangeClientBase (3 дня)
• Создать базовый класс ExchangeClientBase<TRest, TSocket>
• Перенести общую логику (ManagedConnection, reconnect)
• Рефакторить 8 клиентов на наследование
• Результат: 1400 → 350 строк кода

# 6. ILogger вместо Console.WriteLine (1 день)
• Inject ILogger<T> во все сервисы
• Заменить ~50 Console.WriteLine
• Добавить Serilog для structured logging
```

### 🚀 Big Refactoring (1-2 недели)

```bash
# 7. Разбить ParquetDataWriter (2 дня)
ParquetDataWriter → 4 компонента:
  ├─ ParquetWriter<T>
  ├─ ParquetReader<T>
  ├─ BatchBuffer<T>
  └─ MarketDataCollector : BackgroundService

# 8. Chain of Responsibility для обработки (3 дня)
OrchestrationService → Pipeline:
  ├─ NormalizationProcessor
  ├─ SpreadCalculationProcessor
  ├─ WebSocketBroadcastProcessor
  └─ ChannelWriterProcessor

# 9. Assembly Scanning для бирж (1 день)
Хардкод 8 AddSingleton → автоматическое сканирование:
services.Scan(scan => scan
  .FromAssembliesOf<BinanceExchangeClient>()
  .AddClasses(c => c.AssignableTo<IExchangeClient>())
  .AsImplementedInterfaces()
  .WithSingletonLifetime());
```

---

## ROI (Return on Investment) рефакторинга

| Рефакторинг | Усилия | Экономия времени в будущем | ROI |
|-------------|--------|---------------------------|-----|
| ExchangeClientBase | 3 дня | Добавление новой биржи: 185 строк → 30 строк | 🟢 6x |
| Разбить ParquetDataWriter | 2 дня | Изменение партиционирования: 4 часа → 30 минут | 🟢 8x |
| Assembly Scanning | 1 день | Добавление биржи: 3 файла → 1 файл | 🟢 3x |
| ILogger | 1 день | Отладка production issues: 6 часов → 1 час | 🟢 6x |
| BoundedChannel | 15 минут | Предотвращение OOM crash | 🟢 ∞ |

---

## Следующие шаги

### Сегодня (2025-11-01)
- [ ] Удалить 3 файла Class1.cs
- [ ] UnboundedChannel → BoundedChannel
- [ ] Синхронизировать IExchangeClient

### Текущий спринт
- [ ] Создать ExchangeClientBase
- [ ] Добавить ILogger
- [ ] Убрать DataCollectorService

### Следующий спринт
- [ ] Разбить ParquetDataWriter
- [ ] Assembly Scanning
- [ ] Health Checks

### Backlog
- [ ] Chain of Responsibility
- [ ] Metrics (Prometheus)
- [ ] Advanced Resilience Patterns

---

**Полный аудит:** [AUDIT-2025-11-01-collections-architecture.md](./AUDIT-2025-11-01-collections-architecture.md)
