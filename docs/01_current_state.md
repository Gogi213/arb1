# Текущее Состояние Проектов arb1

**Дата:** 2025-11-10
**Версия:** 2.1
**Навигация:** [Диаграммы →](02_architecture_diagrams.md) | [Проблемы →](03_integration_problems.md) | [Roadmap →](04_implementation_roadmap.md)

---

## ✅ Ключевое открытие: Dashboard Integration

Collections уже имеет `OpportunityFilterService`, который автоматически читает CSV от Analyzer и фильтрует profitable пары (cycles > 40). Пользователь видит топ пары через Dashboard (localhost:5000) → выбирает → прописывает в `trader/appsettings.json`.

**JSON export из Analyzer НЕ НУЖЕН** - интеграция уже работает через Dashboard!

---

## Collections (SpreadAggregator .NET)
**Роль:** Data Hub - сбор и трансляция market data

**Что работает:**
- ✅ Подключение к 8 биржам (Binance, Bybit, GateIo, OKX, Kucoin, Bitget, BingX, Mexc)
- ✅ Real-time получение bid/ask через WebSocket
- ✅ Сохранение в Parquet (data lake по пути `C:\visual projects\arb1\data\market_data`)
- ✅ FleckWebSocketServer на `ws://127.0.0.1:8181` для трансляции
- ✅ RollingWindowService (15-минутное скользящее окно)
- ✅ Dashboard (index.html) для визуализации графиков

**Что НЕ работает:**
- ❌ Нормализация символов несовместима с Trader (удаляет ВСЕ разделители: `VIRTUAL/USDT` → `VIRTUALUSDT`)
- ❌ Нет REST API для получения исторических данных
- ❌ Нет endpoint для списка активных символов

---

## Analyzer (Python)
**Роль:** Offline анализ исторических данных для поиска profitable пар

**Что работает:**
- ✅ Читает Parquet файлы напрямую из data lake
- ✅ Рассчитывает метрики (opportunity_cycles, zero_crossings, asymmetry)
- ✅ Выводит топ пары в console и CSV (`analyzer/summary_stats/`)
- ✅ Запускается вручную с CLI аргументами (--date, --start-date, --end-date)

**Что НЕ работает:**
- ❌ Не работает как сервис (только ручной запуск)
- ❌ Нет автоматического обновления (планируется hourly cron)

**Примечание:** CSV результаты автоматически читаются Dashboard (Collections) через `OpportunityFilterService`

---

## Trader (TraderBot .NET)
**Роль:** Execution engine - исполнение арбитражных циклов

**Что работает:**
- ✅ SpreadListener (WebSocket клиент для подключения к Collections)
- ✅ ArbitrageTrader (Gate BUY limit → Bybit SELL market)
- ✅ ReverseArbitrageTrader (Bybit BUY limit → Gate SELL market)
- ✅ TrailingTrader (следит за order book и двигает лимитный ордер)
- ✅ Стратегия: 2-leg rebalancing cycle (LEG1 при spread 0.4% → LEG2 при spread ~0%)

**Что НЕ работает:**
- ❌ SpreadListener НЕ СОВМЕСТИМ с Collections (ожидает `VIRTUAL_USDT`, получает `VIRTUALUSDT`)
- ❌ DecisionMaker не доделан (только логирует, не запускает трейдеры)
- ❌ Нет конфигурации "какие пары торговать"
- ❌ Нет выбора начальной биржи (Gate первым или Bybit первым)

**Примечание:** Profitable пары выбираются вручную из Dashboard (Collections на localhost:5000)

---

**Следующий шаг:** [Посмотреть диаграммы архитектуры →](02_architecture_diagrams.md)
