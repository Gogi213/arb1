# План Имплементации (Minimum Viable Product)

**Дата:** 2025-11-10
**Общее время:** ~5 часов
**Навигация:** [← Проблемы](03_integration_problems.md) | [User Guide →](05_user_guide.md)

---

## Фаза 0: Исправление Блокеров

**Время:** 45 минут
**Приоритет:** КРИТИЧНЫЙ

### ✅ Задача 0.1: Исправить Symbol Normalization в Collections

**Файл:** `collections/src/SpreadAggregator.Application/Services/OrchestrationService.cs:127`

**Изменение:**
```csharp
// Было:
var normalizedSymbol = spreadData.Symbol.Replace("/", "").Replace("-", "").Replace("_", "").Replace(" ", "");

// Стало:
var normalizedSymbol = spreadData.Symbol.Replace("/", "_").Replace("-", "_").Replace(" ", "");
```

**Статус:** ✅ **ИСПРАВЛЕНО**

**Тест:**
1. Запустить Collections
2. Подключиться через wscat: `wscat -c ws://127.0.0.1:8181`
3. Проверить формат Symbol в сообщениях (должно быть `VIRTUAL_USDT`)

**Estimate:** 10 минут

---

### ✅ Задача 0.2: Протестировать SpreadListener

**Действия:**
1. Запустить Collections:
   ```bash
   cd collections/src/SpreadAggregator.Presentation
   dotnet run
   ```

2. Запустить Trader:
   ```bash
   cd trader/src/Host
   dotnet run
   ```

3. Проверить логи Trader:
   - Должны появляться сообщения о получении spreads
   - Формат: `[SpreadListener] Received message of type 'Spread'`

4. Если ошибки десериализации - исправить модель SpreadData в Trader

**Estimate:** 30 минут

---

### ✅ Задача 0.3: Добавить TradeConfig в appsettings.json

**Файл:** `trader/src/Host/appsettings.json`

**Добавить:**
```json
{
  "TradingConfig": {
    "Symbol": "VIRTUAL_USDT",
    "StartExchange": "GateIo",
    "Leg1Threshold": 0.4,
    "Leg2Threshold": 0.0,
    "Amount": 6
  },
  "ExchangeConfigs": [...]
}
```

**Estimate:** 5 минут

---

## Фаза 1: Базовая Интеграция

**Время:** 3 часа
**Приоритет:** КРИТИЧНЫЙ

### ✅ Задача 1.1: Инициализация Exchanges и Traders в Program.cs

**Файл:** `trader/src/Host/Program.cs`

**Что добавить:**

1. Чтение конфигов бирж:
```csharp
var gateConfig = configuration.GetSection("ExchangeConfigs")
    .GetChildren().First(x => x["ExchangeName"] == "GateIo");
var bybitConfig = configuration.GetSection("ExchangeConfigs")
    .GetChildren().First(x => x["ExchangeName"] == "Bybit");
```

2. Создание exchanges:
```csharp
var gateIoExchange = new GateIoExchange(
    gateConfig["ApiKey"],
    gateConfig["ApiSecret"]
);

var bybitExchange = new BybitExchange(
    bybitConfig["ApiKey"],
    bybitConfig["ApiSecret"]
);
```

3. Создание state и traders:
```csharp
var state = new ArbitrageCycleState();
var arbitrageTrader = new ArbitrageTrader(gateIoExchange, bybitExchange);
var reverseArbitrageTrader = new ReverseArbitrageTrader(bybitExchange, gateIoExchange, state);
```

4. Передача в DecisionMaker:
```csharp
var decisionMaker = new DecisionMaker(
    configuration,
    arbitrageTrader,
    reverseArbitrageTrader
);
decisionMaker.Subscribe(spreadListener);
```

**Estimate:** 1 час

---

### ✅ Задача 1.2: Доделать DecisionMaker

**Файл:** `trader/src/Core/DecisionMaker.cs`

**Что добавить:**

1. **Constructor и поля:**
```csharp
private readonly IConfiguration _config;
private readonly ITrader _arbitrageTrader;
private readonly ITrader _reverseArbitrageTrader;
private bool _isCycleInProgress = false;
private bool _waitingForLeg2 = false;
private string _symbol;
private string _startExchange;
private decimal _leg1Threshold;
private decimal _leg2Threshold;
private decimal _amount;

public DecisionMaker(
    IConfiguration config,
    ITrader arbitrageTrader,
    ITrader reverseArbitrageTrader)
{
    _config = config;
    _arbitrageTrader = arbitrageTrader;
    _reverseArbitrageTrader = reverseArbitrageTrader;

    // Читаем конфиг
    var tradingConfig = _config.GetSection("TradingConfig");
    _symbol = tradingConfig["Symbol"];
    _startExchange = tradingConfig["StartExchange"];
    _leg1Threshold = decimal.Parse(tradingConfig["Leg1Threshold"]);
    _leg2Threshold = decimal.Parse(tradingConfig["Leg2Threshold"]);
    _amount = decimal.Parse(tradingConfig["Amount"]);
}
```

2. **Логика HandleProfitableSpread:**
```csharp
private async void HandleProfitableSpread(string direction, decimal spread)
{
    // Проверяем символ
    if (_symbol != null && !direction.Contains(_symbol))
        return;

    // LEG 1: Открытие позиции
    if (spread >= _leg1Threshold && !_isCycleInProgress)
    {
        _isCycleInProgress = true;
        FileLogger.LogOther($"[LEG 1] Starting at spread {spread:F2}%");

        try
        {
            if (_startExchange == "GateIo")
                await _arbitrageTrader.StartAsync(_symbol, _amount, 60, new ArbitrageCycleState());
            else
                await _reverseArbitrageTrader.StartAsync(_symbol, _amount, 60, new ArbitrageCycleState());

            _waitingForLeg2 = true;
            FileLogger.LogOther($"[LEG 1] Completed. Waiting for spread ~{_leg2Threshold:F2}%");
        }
        catch (Exception ex)
        {
            FileLogger.LogOther($"[LEG 1 ERROR] {ex.Message}");
            _isCycleInProgress = false;
        }
    }

    // LEG 2: Закрытие позиции
    else if (_waitingForLeg2 && Math.Abs(spread) <= 0.05m)
    {
        FileLogger.LogOther($"[LEG 2] Starting at spread {spread:F2}%");

        try
        {
            if (_startExchange == "GateIo")
                await _reverseArbitrageTrader.StartAsync(_symbol, _amount, 60, new ArbitrageCycleState());
            else
                await _arbitrageTrader.StartAsync(_symbol, _amount, 60, new ArbitrageCycleState());

            FileLogger.LogOther($"[LEG 2] Completed. Full cycle done.");
        }
        catch (Exception ex)
        {
            FileLogger.LogOther($"[LEG 2 ERROR] {ex.Message}");
        }
        finally
        {
            _isCycleInProgress = false;
            _waitingForLeg2 = false;
        }
    }
}
```

**Estimate:** 2 часа

---

## Итоговый Timeline

| Фаза | Задачи | Estimate | Priority |
|------|--------|----------|----------|
| **Фаза 0** | Исправление блокеров | 45 мин | КРИТИЧНАЯ |
| **Фаза 1** | Базовая интеграция | 3 часа | КРИТИЧНАЯ |
| **ИТОГО** | **Минимум для работы** | **~4 часа** | |

---

## Acceptance Criteria (MVP Ready)

- [ ] Collections запускается и транслирует spreads на ws://127.0.0.1:8181
- [ ] Trader SpreadListener получает spreads без ошибок десериализации
- [ ] Analyzer создает CSV с метриками в `analyzer/summary_stats/`
- [ ] Dashboard (localhost:5000) показывает profitable пары (читает CSV через OpportunityFilterService)
- [ ] Пользователь может выбрать пару из Dashboard и прописать в `trader/appsettings.json`
- [ ] DecisionMaker запускает ArbitrageTrader при LEG1 threshold
- [ ] DecisionMaker запускает ReverseArbitrageTrader при LEG2 threshold
- [ ] Полный цикл LEG1 → LEG2 завершается успешно
- [ ] Логи содержат latencies, балансы, profit

---

## Риски и Mitigation

| Риск | Mitigation |
|------|-----------|
| Collections не стартует | Добавить health check endpoint, логирование startup |
| WebSocket обрывается | Reconnection logic в SpreadListener |
| Trader падает при ошибке ордера | Try-catch + retry logic в трейдерах |
| Analyzer данные устаревают | Hourly cron, timestamp validation |
| Несовместимость моделей данных | JSON schema validation, integration tests |

---

**Следующий шаг:** [User Guide для ежедневной работы →](05_user_guide.md)
