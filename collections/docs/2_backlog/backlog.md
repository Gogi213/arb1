# Бэклог проекта Collections (SpreadAggregator)

Этот документ отслеживает технический долг, идеи по улучшению и новые функции для проекта `SpreadAggregator`. Обновлено: 2025-11-19

## Исправленные критические проблемы (2025-11-19)

| ID | Проблема | Компонент | Дата исправления | Описание |
|----|----------|-----------|------------------|----------|
| FIX-001 | Исправлены конкурирующие потребители | `Program.cs:75-84` | 2025-11-19 | Создание двух независимых каналов: RawDataChannel и RollingWindowChannel вместо общего. |
| FIX-002 | Добавлены HFT оптимизации | `OrchestrationService.cs:134-146` | 2025-11-19 | WebSocket-first approach, TryWrite синхронный, zero-allocation. |
| FIX-003 | Внедрена нормализация символов | `OrchestrationService.cs:106-119` | 2025-11-19 | Унифицированная нормализация symbol names для консистентности между биржами. |

## Текущий технический долг

| ID | Задача | Компонент | Приоритет | Статус | Описание |
|----|--------|-----------|----------|--------|----------|
| TD-001 | Улучшить обработку ошибок в exchange clients | `Infrastructure/Exchanges/*` | High | To Do | Добавить retry logic, circuit breaker patterns, детальное логирование для 8 exchange clients. |
| TD-002 | Добавить мониторинг каналов | `Application/Services` | High | To Do | Метрики для System.Threading.Channels: размер очереди, throughput, dropped messages. |
| TD-003 | Конфигурируемые exchange connections | `Infrastructure` | Medium | To Do | Вынести API ключи, rate limits, timeouts в централизованную конфигурацию. |
| TD-004 | Оптимизация Parquet записи | `ParquetDataWriter` | Medium | To Do | Буферизация, сжатие, batch writing для больших объемов данных. |
| TD-005 | WebSocket connection management | `FleckWebSocketServer` | Medium | To Do | Graceful disconnect handling, reconnection logic, connection pooling. |
| TD-006 | Memory leak prevention | `RealTimeController` | Low | To Do | Проверить unsubscribe от WindowDataUpdated событий при WS disconnect. |

## Рефакторинг

| ID | Задача | Компонент | Приоритет | Статус | Описание |
|----|--------|-----------|----------|--------|----------|
| REF-001 | Декомпозиция OrchestrationService.ProcessExchange | `OrchestrationService.cs:65` | High | To Do | Метод 200+ строк нарушает SRP. Разбить на: ConfigureExchange, FilterSymbols, SetupSubscriptions. |
| REF-002 | Извлечение ISymbolNormalizer | `OrchestrationService.cs:106` | Medium | To Do | Вынести логику нормализации символов в отдельный сервис с тестируемой логикой. |
| REF-003 | Разделение IExchangeClient интерфейса | `Application/Abstractions` | Medium | To Do | Разделить на IDataStreamClient, IHistoricalDataClient для лучшего SOLID compliance. |
| REF-004 | Унификация MarketData моделей | `Domain/Entities` | Low | To Do | Создать единую модель для всех бирж, убрать дублирование трансформации данных. |
| REF-005 | Async/await в SpreadCalculator | `Domain/Services` | Low | To Do | Если выполняются долгие операции, рассмотреть асинхронные методы. |

## Тестирование

| ID | Задача | Компонент | Приоритет | Статус | Описание |
|----|--------|-----------|----------|--------|----------|
| TEST-001 | Unit-тесты для SpreadCalculator | `Domain/Services` | Critical | To Do | Тесты для расчета спредов с различными сценариями рыночных данных. |
| TEST-002 | Integration-тесты каналов | `Application/Services` | High | To Do | Тестирование полного потока: Exchange → Channel → Services → Parquet. |
| TEST-003 | WebSocket server тесты | `Infrastructure` | High | To Do | Проверка подключения/отключения клиентов, broadcast functionality. |
| TEST-004 | Exchange clients тесты | `Infrastructure/Exchanges` | Medium | To Do | Мок-тесты для 8 exchange clients с различными response сценариями. |
| TEST-005 | HFT performance тесты | `OrchestrationService` | Medium | To Do | Бенчмарки latency для WebSocket broadcast, TryWrite performance. |

## Новые функции и улучшения

| ID | Фича | Компонент | Приоритет | Статус | Описание |
|----|-------|-----------|----------|--------|----------|
| FEAT-001 | Поддержка новых бирж | `Infrastructure/Exchanges` | Medium | To Do | Добавить Huobi, Kraken, Coinbase Pro clients для расширения coverage. |
| FEAT-002 | Расширенные арбитражные алгоритмы | `Domain/Services` | Medium | To Do | Треугольный арбитраж, depth-based спреды, multi-leg opportunities. |
| FEAT-003 | WebSocket authentication | `Infrastructure` | Medium | To Do | JWT tokens, API key validation для secure WS connections. |
| FEAT-004 | Database configuration storage | `Infrastructure` | Low | To Do | Перенести конфигурацию symbols, exchange settings в БД для динамического управления. |
| FEAT-005 | Alert system интеграция | `Application` | Low | To Do | Email/Telegram notifications для large spreads, system issues. |
| FEAT-006 | Real-time dashboards | `Presentation/Controllers` | Low | To Do | Расширить dashboard с more metrics, custom filters, export functionality. |

## Архитектурные улучшения

| ID | Улучшение | Компонент | Приоритет | Статус | Описание |
|----|-----------|-----------|----------|--------|----------|
| ARCH-001 | Distributed architecture | Все слои | High | To Do | Переход от single-node к microservices: collection, processing, storage services. |
| ARCH-002 | Message broker интеграция | Application | High | To Do | Замена Channels на Apache Kafka/RabbitMQ для scalability. |
| ARCH-003 | Persistent message queues | Infrastructure | Medium | To Do | Redis/PostgreSQL для durability, предотвращение потери данных при crashes. |
| ARCH-004 | Horizontal scaling | Presentation | Medium | To Do | Load balancing WS connections, multiple instances coordination. |
| ARCH-005 | Stream processing | Domain | Low | To Do | Apache Flink/Azure Stream Analytics для real-time complex event processing. |

## Performance и оптимизация

| ID | Оптимизация | Компонент | Приоритет | Статус | Описание |
|----|-------------|-----------|----------|--------|----------|
| PERF-001 | GC optimization | OrchestrationService | High | To Do | Minimize allocations в hot path, использование Span<T>, Memory<T>. |
| PERF-002 | Connection pooling | Exchange clients | Medium | To Do | HTTP connection pooling для каждой биржи, reuse TCP connections. |
| PERF-003 | Parquet compression | ParquetDataWriter | Medium | To Do | Оптимальные codec settings для balance между size и CPU usage. |
| PERF-004 | WebSocket batch sending | FleckWebSocketServer | Low | To Do | Buffer multiple messages перед отправкой для reduce network overhead. |

## Безопасность и compliance

| ID | Задача | Компонент | Приоритет | Статус | Описание |
|----|--------|-----------|----------|--------|----------|
| SEC-001 | API keys encryption | Configuration | High | To Do | Шифрование API keys в конфигурации, secure vault integration. |
| SEC-002 | Rate limiting | Exchange clients | Medium | To Do | Implementation exchange-specific rate limits для избежания throttling. |
| SEC-003 | Input validation | All layers | Medium | To Do | Comprehensive validation для всех external inputs (exchange data, WS messages). |
| SEC-004 | Audit logging | Infrastructure | Low | To Do | Security audit trail для всех critical operations, compliance requirements. |

## Наблюдаемость и мониторинг

| ID | Задача | Компонент | Приоритет | Статус | Описание |
|----|--------|-----------|----------|--------|----------|
| OBS-001 | Application metrics | Application | High | To Do | Prometheus/Grafana integration, custom business metrics. |
| OBS-002 | Distributed tracing | All layers | Medium | To Do | OpenTelemetry/Jaeger для trace requests across multiple services. |
| OBS-003 | Health checks | Presentation | Medium | To Do | Extended health checks для external dependencies (exchanges, disk space). |
| OBS-004 | Log aggregation | Infrastructure | Low | To Do | Centralized logging с ELK stack, correlation IDs для debug. |

## Известные архитектурные проблемы

| ID | Проблема | Компонент | Срочность | Описание |
|----|----------|-----------|-----------|----------|
| CRIT-001 | Single point of failure | OrchestrationService | High | Один service управляет всеми 8 биржами - risk при crash. |
| CRIT-002 | In-memory channels data loss | Channels | High | При crash теряются несохраненные данные из channels. |
| CRIT-003 | O(N²) complexity | OrchestrationService | Medium | Symbol pairing для всех combinations across exchanges. |

## Статус выполнения

- **Critical Priority:** 3 задачи в работе, 0 выполнено
- **High Priority:** 8 задач To Do, 0 выполнено
- **Medium Priority:** 12 задач To Do, 0 выполнено
- **Low Priority:** 10 задач To Do, 0 выполнено
- **Исправлено недавно:** 3 критические проблемы

**Общий прогресс:** 3/36 задач выполнено (8.3% завершено, актуально на 2025-11-19)

## Приоритеты на следующий спринт

1. **REF-001:** Декомпозиция ProcessExchange (High → Medium impact на maintainability)
2. **TEST-001:** Unit-тесты для SpreadCalculator (Critical → Prevents regression bugs)
3. **TD-001:** Error handling в exchange clients (High → System stability)
4. **PERF-001:** GC optimization (High → HFT performance)
5. **ARCH-001:** Distributed architecture planning (High → Long-term scalability)
