# ISSUE-001: Real-time Charts Not Showing on Client

**Дата создания:** 2025-11-11
**Статус:** IN PROGRESS
**Приоритет:** HIGH

## Диагностика

**Проблема:** При нажатии "Load Real-time Window" на клиенте графики не приходят. WebSocket подключается, opportunities находятся, но данные не передаются.

**Текущее состояние:**
- ✅ Collections запускается и получает данные от exchanges (Binance, GateIo, Bybit)
- ✅ OpportunityFilterService находит 5 opportunities из CSV analyzer
- ✅ RealTimeController устанавливает WebSocket соединение
- ✅ RollingWindowService запускается и ждет данные
- ❌ OrchestrationService не пишет данные в каналы RollingWindowService
- ❌ RollingWindowService не получает события от exchanges
- ❌ Клиент не получает chart data по WebSocket

## Выполненные действия

### 1. Проверка базовой инфраструктуры
- ✅ Проверил CSV analyzer - opportunities есть (VIRTUAL_USDT, UNI_USDT и др.)
- ✅ Проверил Parquet файлы - данные GateIo/Bybit присутствуют
- ✅ Проверил appsettings.json - все exchanges включены

### 2. Отладка WebSocket потока
- ✅ RealTimeController получает opportunities и подписывается на события
- ✅ WebSocket соединение устанавливается успешно
- ✅ OpportunityFilterService возвращает корректные данные

### 3. Отладка RollingWindowService
- ✅ Добавил логи в StartAsync - сервис запускается
- ✅ Добавил логи в ProcessData - для отслеживания приема данных
- ✅ Добавил логи в OnWindowDataUpdated - для отслеживания событий
- ✅ Проверил подключение каналов в DI

### 4. Отладка OrchestrationService
- ✅ Добавил логи при записи в каналы
- ✅ Проверил что exchanges запускаются (Binance, GateIo, Bybit)
- ✅ Проверил что subscriptions создаются

## Решение найдено ✅

**Проблема:** В Program.cs создавались два разных Channel instances вместо shared.

**Код до исправления:**
```csharp
services.AddSingleton<RollingWindowChannel>(new RollingWindowChannel(Channel.CreateBounded<MarketData>(channelOptions)));
services.AddSingleton<RawDataChannel>(new RawDataChannel(Channel.CreateBounded<MarketData>(channelOptions)));
```

**Код после исправления:**
```csharp
var sharedChannel = Channel.CreateBounded<MarketData>(channelOptions);
services.AddSingleton<RollingWindowChannel>(new RollingWindowChannel(sharedChannel));
services.AddSingleton<RawDataChannel>(new RawDataChannel(sharedChannel));
```

**Результат:** OrchestrationService и RollingWindowService теперь используют один канал.

## СТАТУС: НУЖНА ДОПОЛНИТЕЛЬНАЯ РАБОТА

**Проблема частично решена, но остается фундаментальная issue.**

### Что исправлено ✅:
1. **Каналы DI:** Shared Channel работает
2. **Порог opportunities:** 68 opportunities вместо 5
3. **Логи очищены:** Нет verbose спама

### РЕШЕНИЕ НАЙДЕНО ✅

**Проблема решена!** Opportunities символы просто НЕ торгуются в 12-м часу.

**Проверка data lake:**
- ✅ **AVAXUSDT** на Binance: hour=12 имеет 23 parquet файла
- ❌ **AAVE_USDT** на Bybit: только до hour=09
- ❌ **FET_USDT** на Bybit: только до hour=09  
- ❌ **TA_USDT** на Bybit: только до hour=09

**Причина:** Analyzer нашел opportunities на основе historical данных, но эти пары торгуются редко или в другое время.

**Решение:** OpportunityFilterService должен фильтровать по real-time доступности.

Нужно добавить проверку: opportunity валидна только если оба символа торгуются в real-time на соответствующих exchanges.

**Текущий статус:** Каналы работают, opportunities найдены, но графики не грузятся из-за отсутствия данных.

## Логи для проверки

После перезапуска смотреть за:
```
[Orchestration] Wrote to channels: GateIo/VIRTUAL_USDT
[RollingWindow] RollingWindow received: SpreadData GateIo/VIRTUAL_USDT
[RollingWindow] RollingWindow event: GateIo/VIRTUAL_USDT
[RealTimeController] Event-driven update sent for VIRTUAL_USDT (GateIo/Bybit)
```

## Файлы изменены

- `RollingWindowService.cs` - добавлены логи
- `OrchestrationService.cs` - добавлены логи
- `Program.cs` - RollingWindowService получает logger

## Связанные файлы

- `RealTimeController.cs` - WebSocket endpoint
- `OpportunityFilterService.cs` - источник opportunities
- `ParquetReaderService.cs` - чтение historical данных
