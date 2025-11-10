# User Guide (После MVP)

**Дата:** 2025-11-10
**Навигация:** [← Roadmap](04_implementation_roadmap.md)

---

## Ежедневный Workflow

### 1. Запуск Analyzer (Каждый Час)

**Цель:** Найти profitable пары для торговли

**Команды:**
```bash
cd analyzer
python run_all_ultra.py --date 2025-11-10
```

**Что происходит:**
- Читает Parquet файлы из `C:\visual projects\arb1\data\market_data`
- Рассчитывает метрики для всех пар:
  - `cycles_040bp_per_hour` - сколько раз спред пересекал 0.4%
  - `zero_crossings_per_minute` - частота возврата к нулю
  - `deviation_asymmetry` - асимметрия спреда
- Сохраняет CSV: `analyzer/summary_stats/summary_2025-11-10.csv`

**Пример вывода в консоли:**
```
Top 10 pairs by opportunity cycles:
1. VIRTUAL/USDT (GateIo-Bybit): 12.5 cycles/hour
2. BTC/USDT (Binance-Bybit): 8.3 cycles/hour
...
```

---

### 2. Просмотр Profitable Пар через Dashboard

**Где:** Dashboard Collections на http://localhost:5000

**Что делать:**
1. Убедитесь что Collections запущен (см. шаг 3)
2. Откройте браузер: http://localhost:5000
3. Нажмите кнопку **"Load Historical Analysis"**
4. Dashboard автоматически:
   - Читает последний CSV из `analyzer/summary_stats/`
   - Фильтрует пары с `opportunity_cycles > 40`
   - Показывает графики spreads с quantiles (97th, 3rd percentile)

**Что смотреть:**
- **Symbol** и **Exchanges** - какую пару торговать
- **Spread chart** - как спред двигался исторически
- **Upper/Lower bands** - пороги для LEG1/LEG2

**Выбор пары:**
- Ищите пары с частыми пересечениями upper band
- Убедитесь что спред возвращается к нулю (mean reversion)
- Проверьте что обе биржи поддерживаются в Trader (GateIo, Bybit)

---

### 3. Выбор Пары и Конфигурация Trader

**Файл:** `trader/src/Host/appsettings.json`

**Что добавить:**
```json
{
  "TradingConfig": {
    "Symbol": "VIRTUAL_USDT",
    "StartExchange": "GateIo",
    "Leg1Threshold": 0.4,
    "Leg2Threshold": 0.0,
    "Amount": 6
  },
  "ExchangeConfigs": [
    {
      "ExchangeName": "GateIo",
      "ApiKey": "your_gateio_key",
      "ApiSecret": "your_gateio_secret"
    },
    {
      "ExchangeName": "Bybit",
      "ApiKey": "your_bybit_key",
      "ApiSecret": "your_bybit_secret"
    }
  ]
}
```

**Параметры:**
- `Symbol`: Выбранная пара из Dashboard (формат `VIRTUAL_USDT` с подчеркиванием)
- `StartExchange`: `"GateIo"` или `"Bybit"` (где начать LEG1)
- `Leg1Threshold`: 0.4 (открытие позиции при спреде 0.4%)
- `Leg2Threshold`: 0.0 (закрытие позиции при спреде ~0%)
- `Amount`: Количество базового актива для торговли

---

### 4. Запуск Collections (24/7 Service)

**Цель:** Получение real-time bid/ask и трансляция spreads

**Команды:**
```bash
cd collections/src/SpreadAggregator.Presentation
dotnet run
```

**Проверка работоспособности:**
1. Открыть Dashboard: http://localhost:5000
2. Нажать кнопку "Load Real-time Window"
3. Убедиться, что появляются графики с обновлениями

**Или через wscat:**
```bash
wscat -c ws://127.0.0.1:8181
```

Вы должны видеть сообщения:
```json
{
  "MessageType": "Spread",
  "Data": {
    "Symbol": "VIRTUAL_USDT",
    "Exchange": "GateIo",
    "BestBid": 0.123,
    "BestAsk": 0.125,
    "Timestamp": "2025-11-10T14:30:00Z"
  }
}
```

---

### 5. Запуск Trader

**Цель:** Выполнение арбитражных циклов

**Команды:**
```bash
cd trader/src/Host
dotnet run
```

**Что происходит при старте:**
1. SpreadListener подключается к `ws://127.0.0.1:8181`
2. DecisionMaker переходит в режим `WAITING_LEG1`
3. При обнаружении спреда >= 0.4%:
   - Логируется: `[LEG 1] Starting at spread 0.42%`
   - Запускается ArbitrageTrader или ReverseArbitrageTrader
4. После завершения LEG1:
   - Логируется: `[LEG 1] Completed. Waiting for spread ~0.0%`
   - DecisionMaker переходит в режим `WAITING_LEG2`
5. При спреде ~0%:
   - Логируется: `[LEG 2] Starting at spread 0.03%`
   - Запускается обратный trader
6. После завершения LEG2:
   - Логируется: `[LEG 2] Completed. Full cycle done.`

---

### 6. Мониторинг

**Логи Trader:**
```bash
# Windows
dir trader\logs\
type trader\logs\other.log

# Linux/Mac
ls trader/logs/
tail -f trader/logs/other.log
```

**Ключевые логи:**
- `[SpreadListener] Connected to ws://127.0.0.1:8181`
- `[SpreadListener] Received message of type 'Spread'`
- `[LEG 1] Starting at spread 0.42%`
- `[GATE] Limit BUY order placed: OrderId=123`
- `[GATE] Balance after trade: USDT=94.12, VIRTUAL=6.00`
- `[BYBIT] Market SELL executed: 6 VIRTUAL`
- `[LEG 1] Completed. Waiting for spread ~0.0%`
- `[LEG 2] Starting at spread 0.03%`
- `[CYCLE] Profit: +0.25 USDT`

**Dashboard:**
- URL: http://localhost:5000
- Показывает spread charts для всех активных пар
- Обновляется каждую секунду

---

## Troubleshooting

### Collections не стартует

**Ошибка:** `Address already in use (port 5000)`

**Решение:**
```bash
# Windows
netstat -ano | findstr :5000
taskkill /PID <PID> /F

# Linux/Mac
lsof -i :5000
kill -9 <PID>
```

---

### SpreadListener не получает сообщения

**Проверка:**
1. Collections запущен и слушает на `ws://127.0.0.1:8181`
2. Символ в Collections и Trader совпадает (формат `VIRTUAL_USDT`)
3. Нет ошибок десериализации в логах Trader

**Команда для теста:**
```bash
wscat -c ws://127.0.0.1:8181
# Должны появляться JSON сообщения
```

---

### DecisionMaker не запускает трейдеры

**Возможные причины:**
1. Спред не достигает threshold (0.4%)
2. `_isCycleInProgress = true` (цикл уже идет)
3. Символ не совпадает с конфигурацией
4. Ошибка при создании ордера (check logs)

**Проверка:**
```bash
# В логах должны быть:
[LEG 1] Starting at spread 0.42%
[GATE] Limit BUY order placed: OrderId=123
```

Если только логи `Spread detected!` без `Starting`, значит:
- Либо цикл уже в процессе
- Либо порог не достигнут

---

### Trader падает с ошибкой API

**Ошибка:** `Insufficient balance`

**Решение:**
1. Проверить баланс на биржах вручную
2. Уменьшить `Amount` в appsettings.json
3. Убедиться, что у вас есть оба актива (например, USDT и VIRTUAL)

**Ошибка:** `Invalid API key`

**Решение:**
1. Проверить API ключи в appsettings.json
2. Убедиться, что ключи имеют права на торговлю (не только чтение)
3. Проверить IP whitelist на бирже

---

## Долгосрочный Roadmap (После MVP)

### Фаза 3: REST API в Collections (Estimate: 2 дня)

**Цель:** Упростить интеграцию и добавить historical data access

**Endpoints:**
```
GET /api/symbols
  → Список активных символов и бирж

GET /api/historical/{symbol}/{exchange1}/{exchange2}?from=2025-11-10&to=2025-11-11
  → Исторические spreads

GET /api/window/{symbol}/{exchange1}/{exchange2}
  → Текущее 15-минутное окно с quantiles
```

**Технологии:**
- ASP.NET Core Minimal API
- Swagger/OpenAPI для документации
- Кэширование (MemoryCache)

---

### Фаза 4: Dynamic Thresholds (Estimate: 1 день)

**Цель:** Адаптивные пороги вместо фиксированных 0.4%

**Изменения в DecisionMaker:**
```csharp
// Вместо:
if (spread >= 0.4m)

// Использовать:
var windowStats = await _collectionsApi.GetWindowStats(symbol, exchange1, exchange2);
var upperThreshold = windowStats.Quantile97;
var lowerThreshold = windowStats.Quantile3;

if (spread >= upperThreshold) // LEG1
if (Math.Abs(spread - windowStats.Median) <= 0.05m) // LEG2
```

**Преимущества:**
- Адаптация к волатильности рынка
- Меньше ложных сигналов
- Лучшее использование mean-reversion

---

### Фаза 5: Trading Dashboard (Estimate: 3 дня)

**Цель:** UI для управления торговлей и мониторинга

**Функции:**
- Real-time статус (LEG1/LEG2/IDLE)
- P&L tracking (profit/loss по каждому циклу)
- Кнопки Enable/Disable пар
- История сделок (таблица)
- Charts со spreads + маркерами сделок

**Технологии:**
- Blazor WebAssembly + SignalR
- Chart.js или TradingView Lightweight Charts
- Bootstrap для UI

---

### Фаза 6: Orchestration (Estimate: 2 дня)

**Цель:** Production-ready deployment

**Компоненты:**
- **Systemd Services (Linux):**
  ```bash
  sudo systemctl enable collections
  sudo systemctl enable trader
  sudo systemctl start collections
  ```

- **Windows Services:**
  ```bash
  sc create CollectionsService binPath="C:\...\SpreadAggregator.Presentation.exe"
  sc create TraderService binPath="C:\...\TraderBot.Host.exe"
  ```

- **Health Checks:**
  - `/health` endpoint в каждом сервисе
  - Auto-restart при падении

- **Metrics & Alerting:**
  - Prometheus для метрик (latencies, spreads, balances)
  - Grafana для визуализации
  - Alertmanager для уведомлений (Telegram/Email)

---

## Acceptance Criteria (MVP Ready)

Перед тем как считать MVP завершенным, убедитесь:

- [ ] Collections запускается и транслирует spreads на `ws://127.0.0.1:8181`
- [ ] Trader SpreadListener получает spreads без ошибок десериализации
- [ ] Analyzer экспортирует `analyzer_output.json` с топ парами
- [ ] Пользователь может выбрать пару и прописать в `trader/appsettings.json`
- [ ] DecisionMaker запускает ArbitrageTrader при LEG1 threshold
- [ ] DecisionMaker запускает ReverseArbitrageTrader при LEG2 threshold
- [ ] Полный цикл LEG1 → LEG2 завершается успешно
- [ ] Логи содержат latencies, балансы, profit

**Тестовый сценарий:**
1. Запустить Collections
2. Дождаться накопления данных (15 минут)
3. Запустить Analyzer
4. Выбрать топ пару из JSON
5. Сконфигурировать Trader с этой парой
6. Запустить Trader
7. Дождаться LEG1 (spread >= 0.4%)
8. Проверить логи: `[LEG 1] Starting...`
9. Дождаться LEG2 (spread ~0%)
10. Проверить логи: `[LEG 2] Completed. Full cycle done.`

---

**Следующий шаг:** Начать с [Фазы 0 Roadmap](04_implementation_roadmap.md) - исправление Symbol Normalization
