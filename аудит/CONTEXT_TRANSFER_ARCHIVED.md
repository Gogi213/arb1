# КОНТЕКСТ ДЛЯ ПЕРЕДАЧИ В НОВЫЙ ЧАТ

**Дата:** 2025-11-08
**Проект:** Arbitrage Trading System (charts + collections)

---

## КРАТКОЕ ОПИСАНИЕ СИСТЕМЫ

Система для мониторинга арбитражных возможностей на криптовалютных биржах:

- **Collections (C#/.NET 9.0)** - сбор данных от 8 бирж, WebSocket сервер
- **Charts (Python/FastAPI)** - визуализация, real-time графики, HTTP API

---

## АРХИТЕКТУРА

### Collections (Backend Data Collector)

```
8 бирж (Binance, Bybit, MEXC, GateIO, Kucoin, OKX, Bitget, BingX)
    ↓
ExchangeClientBase → подписка на тикеры и трейды
    ↓
OrchestrationService → обработка, расчет спредов
    ├─→ RawDataChannel (unbounded)
    │       ↓
    │   ParquetDataWriter → запись на диск (партиционированные Parquet)
    │
    ├─→ RollingWindowChannel (unbounded)
    │       ↓
    │   RollingWindowService → in-memory кеш (1 час)
    │
    └─→ FleckWebSocketServer (ws://localhost:8181)
            ↓
        Charts + Браузеры
```

**Технологии:**
- Clean Architecture (Domain, Application, Infrastructure, Presentation)
- System.Threading.Channels для асинхронной обработки
- Fleck для WebSocket сервера
- Parquet для хранения данных
- Singleton services через DI

---

### Charts (Frontend Visualization)

```
WebSocket Client → подключается к Collections (ws://127.0.0.1:8181)
    ↓
RollingWindow → in-memory кеш спредов/трейдов (1 час)
    ↓
FastAPI Endpoints:
    ├─ /api/dashboard_data (HTTP streaming, исторические данные из Parquet)
    ├─ /api/rolling_window_data (HTTP streaming, real-time из памяти)
    └─ /ws/realtime_charts (WebSocket для браузеров)
```

**Технологии:**
- FastAPI (asyncio)
- Polars для анализа Parquet
- websockets library
- threading.Timer для cleanup
- Global RollingWindow singleton

---

## ПРОБЛЕМА: OUT OF MEMORY (OOM)

### Симптомы
- Collections и Charts периодически падают с OOM
- Memory usage растет до нескольких GB
- GC не успевает освобождать память

### Выполненный анализ

Создан полный отчет: `аудит/OOM_ANALYSIS.md`

**Найдено 20 проблем:**
- 5 критических
- 8 высокого приоритета
- 7 среднего приоритета

---

## ТОП-5 КРИТИЧЕСКИХ ПРОБЛЕМ

### 1. Unbounded Channels (C#)
**Файл:** `collections/src/SpreadAggregator.Presentation/Program.cs:72-73`

Каналы созданы без ограничений размера. При высокой нагрузке могут расти до OOM.

```csharp
Channel.CreateUnbounded<MarketData>()  // NO LIMIT!
```

**Расчет риска:** 800K сообщений/сек → 9.4 GB за 1 минуту

---

### 2. Event Handlers Без Отписки
**Файл:** `collections/src/SpreadAggregator.Infrastructure/Services/Exchanges/Base/ExchangeClientBase.cs:201-202`

При каждом переподключении создаются новые handlers, но старые никогда не удаляются.

```csharp
result.Data.ConnectionLost += new Action(HandleConnectionLost);  // NO -= EVER
```

**Статистика:** MEXC: 63 отключения, Bybit: 20 → 166+ leaked handlers

---

### 3. Recursive Timer Pattern (Python)
**Файл:** `charts/server.py:68-87`

Timer рекурсивно перезапускает сам себя, создавая цепочку объектов.

```python
def _cleanup_old_data(self):
    # ... cleanup logic
    self.start_cleanup()  # Creates NEW Timer!
```

**Эффект:** Timer1 → Timer2 → Timer3 → ... бесконечная цепочка

---

### 4. Fire-and-Forget Tasks (C#)
**Файл:** `collections/src/SpreadAggregator.Application/Services/OrchestrationService.cs:73-75`

Все 8 подписок на биржи запускаются без await, исключения игнорируются.

```csharp
// Do not await long-running tasks, let them run in the background.
// await Task.WhenAll(tasks);  ← COMMENTED OUT!
```

---

### 5. AllSymbolInfo Accumulation
**Файл:** `collections/src/SpreadAggregator.Application/Services/OrchestrationService.cs:27,84`

При переподключениях символы добавляются повторно без дедупликации.

```csharp
_allSymbolInfo.AddRange(allSymbols);  // DUPLICATES ON RECONNECT
```

**Расчет:** 40,000 символов × 10 переподключений = 400,000 записей

---

## ДОПОЛНИТЕЛЬНЫЕ НАХОДКИ

### Race Conditions
- **Python:** `RollingWindow.windows` (dict) - доступ из asyncio task + Timer thread
- **C#:** `RollingWindowData.Spreads` (List) - concurrent modification

### Медленные Клиенты
**Файл:** `collections/src/SpreadAggregator.Infrastructure/Services/FleckWebSocketServer.cs:78`

```csharp
return Task.WhenAll(tasks);  // Ждет ВСЕХ браузеров
```

1 медленный браузер (500ms) блокирует весь broadcast → backpressure на биржи → unbounded channel растет.

### Missing Heartbeat
- **Charts WebSocket Client:** `ping_interval=None` - мертвые соединения не обнаруживаются
- **Collections Fleck Server:** нет ping/pong - dead connections остаются в памяти

### Allocation Storms
- `FleckWebSocketServer.BroadcastRealtimeAsync()`: `.ToList()` создает копию 100 раз/сек
- `JsonSerializer.Serialize()`: 100+ вызовов/сек без pooling
- `json.dumps/loads` (Python): каждое сообщение создает новый объект

---

## СТРУКТУРА ПРОЕКТА

```
arb1/
├── charts/
│   ├── server.py (FastAPI app, RollingWindow, WebSocket client)
│   └── requirements.txt
│
├── collections/
│   ├── src/
│   │   ├── SpreadAggregator.Domain/ (Entities, DTOs)
│   │   ├── SpreadAggregator.Application/ (Services, Orchestration)
│   │   ├── SpreadAggregator.Infrastructure/ (Exchange clients, Parquet writer)
│   │   └── SpreadAggregator.Presentation/ (Program.cs, DI setup)
│   └── docs/
│       ├── proposals/ (PROPOSAL-2025-0002/0003 - race condition fixes)
│       └── websocket_stability_analysis.md
│
└── аудит/
    ├── OOM_ANALYSIS.md (полный отчет об утечках)
    └── CONTEXT_TRANSFER.md (этот файл)
```

---

## КЛЮЧЕВЫЕ ФАЙЛЫ ДЛЯ АНАЛИЗА

### Collections (C#)

1. **Program.cs** (`collections/src/SpreadAggregator.Presentation/Program.cs`)
   - DI configuration
   - Channel setup (строки 72-73) 🔴 КРИТИЧНО
   - IHostedService registration

2. **OrchestrationService.cs** (`collections/src/SpreadAggregator.Application/Services/OrchestrationService.cs`)
   - Fire-and-forget tasks (строки 73-75) 🔴 КРИТИЧНО
   - Callback closures (строки 114-142) 🟠 ВЫСОКИЙ
   - AllSymbolInfo accumulation (строка 84) 🔴 КРИТИЧНО

3. **ExchangeClientBase.cs** (`collections/src/SpreadAggregator.Infrastructure/Services/Exchanges/Base/ExchangeClientBase.cs`)
   - Event handler leaks (строки 201-202) 🔴 КРИТИЧНО
   - Connection management

4. **FleckWebSocketServer.cs** (`collections/src/SpreadAggregator.Infrastructure/Services/FleckWebSocketServer.cs`)
   - Task.WhenAll sync (строка 78) 🟠 ВЫСОКИЙ
   - Allocation storm (строка 62) 🟠 ВЫСОКИЙ
   - Dead connections (строки 35, 49) 🟠 ВЫСОКИЙ

5. **RollingWindowService.cs** (`collections/src/SpreadAggregator.Application/Services/RollingWindowService.cs`)
   - Race condition на List (строки 49, 57) 🟠 ВЫСОКИЙ
   - Timer disposal (строка 21) 🟡 СРЕДНИЙ

6. **ParquetDataWriter.cs** (`collections/src/SpreadAggregator.Infrastructure/Services/ParquetDataWriter.cs`)
   - Buffer growth (строки 163-164) 🟡 СРЕДНИЙ

### Charts (Python)

1. **server.py** (`charts/server.py`)
   - Recursive Timer (строки 68-87) 🔴 КРИТИЧНО
   - Race condition Dict (строки 61, 89-161) 🟠 ВЫСОКИЙ
   - WebSocket client (строки 172-208)
     - Missing heartbeat (строки 176-177) 🟠 ВЫСОКИЙ
     - Fire-and-forget task (строка 207) 🟡 СРЕДНИЙ
   - Global DataFrame (строки 511, 526) 🟠 ВЫСОКИЙ
   - RollingWindow unbounded growth (строки 61, 78-87) 🟡 СРЕДНИЙ

---

## ДАННЫЕ ИЗ МОНИТОРИНГА

### WebSocket Stability Analysis
**Файл:** `collections/docs/websocket_stability_analysis.md`

Отключения по биржам за анализируемый период:
- MEXC: 63 раза (очень нестабильна)
- Bybit: 20 раз
- Bitget: 5 раз
- Kucoin: 5 раз
- GateIO: 5 раз
- OKX: 1 раз
- BingX: 1 раз
- Binance: 0 раз

**Интерпретация:** Частые переподключения усугубляют проблемы с event handlers и AllSymbolInfo.

---

## ПРЕДЛОЖЕННЫЕ ИСПРАВЛЕНИЯ

Документированы в `collections/docs/proposals/`:

1. **PROPOSAL-2025-0002** - Fix WebSocket Race Condition (полное решение)
2. **PROPOSAL-2025-0003** - Simple Fix (упрощенная версия)

**Статус:** НЕ ПРИМЕНЕНЫ

---

## ТЕХНИЧЕСКИЕ ДЕТАЛИ

### Memory Layout Estimate

**Collections при полной нагрузке:**
```
RawDataChannel (unbounded):           0 - ∞ GB (зависит от backpressure)
RollingWindowChannel (unbounded):     0 - ∞ GB
RollingWindowService._windows:        10-50 GB (1 час × 8000 пар)
ParquetDataWriter buffers:            200-300 MB
_allSymbolInfo:                       8 MB - ∞ (при дубликатах)
FleckWebSocketServer._allSockets:     50-200 MB
Event handlers (leaked):              10-100 MB (при переподключениях)
```

**Charts при полной нагрузке:**
```
rolling_window.windows:               10-50 GB (1 час × 8000 пар)
MOCK_OPPORTUNITIES:                   0.5-2 GB (только тесты)
WebSocket buffers:                    50-100 MB
Timer chain:                          1-10 MB (рекурсивные объекты)
```

### Data Flow Rates

**Входящие данные:**
- 8 бирж × 1000 пар (в среднем) = 8000 активных пар
- Каждая пара: 100-200 обновлений/сек (spread updates)
- Total throughput: ~800K - 1.6M сообщений/сек

**Исходящие данные:**
- WebSocket broadcast к браузерам: ~100 сообщений/сек
- Parquet запись: batch по 1000 записей
- HTTP streaming: зависит от запросов

---

## РЕКОМЕНДОВАННЫЕ ДЕЙСТВИЯ

### IMMEDIATE (критично, 1-2 дня)

1. ✅ **Bounded Channels**
   ```csharp
   Channel.CreateBounded<MarketData>(new BoundedChannelOptions(100000))
   ```

2. ✅ **Event Handler Cleanup**
   ```csharp
   // В StopAsync():
   result.Data.ConnectionLost -= HandleConnectionLost;
   ```

3. ✅ **AsyncIO Timer**
   ```python
   async def periodic_cleanup():
       while True:
           await asyncio.sleep(60)
           await cleanup_old_data()
   ```

4. ✅ **Await Tasks**
   ```csharp
   await Task.WhenAll(tasks);  // Раскомментировать
   ```

### SHORT-TERM (1-2 недели)

5. Heartbeat механизмы (ping/pong)
6. Timeout на broadcast с fire-and-forget
7. Thread-safe коллекции
8. Дедупликация AllSymbolInfo

### LONG-TERM (1-2 месяца)

9. Object pooling (ArrayPool, StringBuilder)
10. Monitoring и алерты
11. Load testing (1000+ браузеров, 24+ часа)
12. Архитектурный рефакторинг (см. следующий раздел)

---

## СЛЕДУЮЩИЙ ШАГ: АУДИТ РАЦИОНАЛЬНОСТИ

После исправления критических утечек памяти, необходимо провести архитектурный аудит:

### Вопросы для анализа

1. **Нужны ли 2 Unbounded Channels?**
   - RawDataChannel и RollingWindowChannel дублируют данные
   - Можно ли использовать один канал с multiple readers?

2. **Эффективна ли архитектура RollingWindow?**
   - Дублирование in-memory кеша в Collections и Charts
   - Можно ли объединить или использовать Redis?

3. **Оптимален ли Fleck для 1000+ клиентов?**
   - Task.WhenAll блокирует на медленных клиентах
   - Рассмотреть SignalR или Server-Sent Events (SSE)?

4. **Нужен ли глобальный broadcast всем клиентам?**
   - Каждый браузер получает ВСЕ пары (8000+)
   - Можно ли фильтровать по подпискам?

5. **Эффективно ли хранение в Parquet?**
   - Batch по 1000 записей
   - Партиционирование по часам
   - Альтернативы: ClickHouse, TimescaleDB?

6. **Правильная ли Clean Architecture?**
   - OrchestrationService имеет слишком много ответственностей
   - Можно ли разделить на микросервисы?

---

## МЕТРИКИ ДЛЯ MONITORING

### Критичные метрики

```csharp
// Collections
- RawDataChannel.Reader.Count (алерт > 10,000)
- RollingWindowChannel.Reader.Count (алерт > 10,000)
- Process.PrivateMemorySize64 (алерт > 80% доступной)
- GC.CollectionCount(2) per minute (алерт > 10)
- FleckWebSocketServer._allSockets.Count (алерт > 1,000)

// Charts
- len(rolling_window.windows) (алерт > 10,000)
- sum(len(w.spreads) for w in windows) (алерт > 10M)
- process.memory_info().rss (алерт > 80%)
```

### Dashboard Requirements

1. Real-time memory usage graph
2. Channel queue sizes
3. WebSocket connections count
4. GC metrics (Gen0, Gen1, Gen2 collections)
5. Message throughput (in/out)
6. Latency percentiles (p50, p95, p99)

---

## ТЕСТИРОВАНИЕ

### Load Test Scenarios

1. **1000 браузеров × 24 часа**
   - Проверка утечек памяти
   - Профилирование с dotMemory / memory_profiler

2. **Reconnect storm**
   - 100 переподключений/мин от MEXC
   - Проверка event handler cleanup

3. **Slow client simulation**
   - 10% браузеров с 500ms latency
   - Проверка backpressure handling

4. **GC pressure test**
   - Полный цикл Gen0 → Gen1 → Gen2
   - Проверка allocation storms

5. **Graceful shutdown test**
   - Kill -SIGTERM
   - Проверка Dispose всех ресурсов

---

## КОНТАКТЫ И ССЫЛКИ

### Документация
- OOM Analysis: `аудит/OOM_ANALYSIS.md`
- WebSocket Stability: `collections/docs/websocket_stability_analysis.md`
- Proposals: `collections/docs/proposals/`

### Git Status
- Branch: `main`
- Modified files: ~60 (binaries + source)
- Untracked: `SERIALIZATION_ANALYSIS.md`, `аудит/`

### Environment
- Collections: .NET 9.0, Windows
- Charts: Python 3.x, FastAPI, Polars
- Working directory: `c:\visual projects\arb1\`

---

## КРАТКИЙ SUMMARY ДЛЯ НОВОГО ЧАТА

**Контекст:** Система мониторинга арбитража на 8 криптобиржах падает с OOM.

**Проблема:** 20 утечек памяти найдено (5 критических, 8 высоких, 7 средних).

**Главные виновники:**
1. Unbounded Channels (C#) - неограниченный рост
2. Event handlers без отписки - accumulation при reconnect
3. Recursive Timer (Python) - бесконечная цепочка
4. Fire-and-forget tasks - нет контроля исключений
5. AllSymbolInfo дубликаты - рост при reconnect

**Решения готовы:** См. `аудит/OOM_ANALYSIS.md` разделы 6-8.

**Следующий шаг:** Аудит рациональности архитектуры (дублирование данных, эффективность WebSocket broadcast, оптимизация хранения).

**Все детали:** См. файлы в `аудит/` и `collections/docs/`.
