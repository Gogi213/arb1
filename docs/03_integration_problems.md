# Критичные Проблемы Интеграции

**Дата:** 2025-11-10
**Навигация:** [← Диаграммы](02_architecture_diagrams.md) | [Roadmap →](04_implementation_roadmap.md) | [User Guide →](05_user_guide.md)

---

## 🔴 Проблема 1: Symbol Normalization (БЛОКЕР)

**Описание:**
Collections нормализует символы удаляя ВСЕ разделители, Trader ожидает формат с подчеркиванием.

**Локация:**
- Collections: `SpreadAggregator.Application/Services/OrchestrationService.cs:127`
- Trader: `TraderBot.Core/ArbitrageTrader.cs:44-45`

**Текущий код Collections:**
```csharp
var normalizedSymbol = spreadData.Symbol
    .Replace("/", "")
    .Replace("-", "")
    .Replace("_", "")
    .Replace(" ", "");
// VIRTUAL/USDT → VIRTUALUSDT
```

**Текущий код Trader:**
```csharp
_baseAsset = symbol.Split('_')[0];  // Ожидает VIRTUAL_USDT
_quoteAsset = symbol.Split('_')[1]; // Падает если нет '_'
```

**Решение:**
Изменить Collections нормализацию на:
```csharp
var normalizedSymbol = spreadData.Symbol
    .Replace("/", "_")
    .Replace("-", "_")
    .Replace(" ", "");
// VIRTUAL/USDT → VIRTUAL_USDT
// BTC-USDT → BTC_USDT
```

**Приоритет:** КРИТИЧНЫЙ
**Estimate:** 10 минут

---

## 🟡 Проблема 2: DecisionMaker не доделан

**Описание:**
DecisionMaker только логирует события, не запускает трейдеры.

**Что нужно:**
1. Читать конфиг из `appsettings.json`:
   - Enabled symbol
   - Start exchange (GateIo или Bybit)
   - LEG1 threshold (например 0.4%)
   - LEG2 threshold (например 0.0%)
   - Trade amount (например 6)

2. Инициализировать трейдеры через DI в Program.cs:
   - GateIoExchange, BybitExchange
   - ArbitrageTrader, ReverseArbitrageTrader
   - ArbitrageCycleState

3. Логика в DecisionMaker:
```csharp
// LEG 1: Открытие позиции при достижении threshold
if (spread >= leg1Threshold && !_isCycleInProgress)
{
    _isCycleInProgress = true;
    if (startExchange == "GateIo")
        await _arbitrageTrader.StartAsync(...);
    else
        await _reverseArbitrageTrader.StartAsync(...);
    _waitingForLeg2 = true;
}

// LEG 2: Закрытие позиции при возврате к ~0%
if (_waitingForLeg2 && Math.Abs(spread) <= 0.05)
{
    if (startExchange == "GateIo")
        await _reverseArbitrageTrader.StartAsync(...);
    else
        await _arbitrageTrader.StartAsync(...);

    _isCycleInProgress = false;
    _waitingForLeg2 = false;
}
```

**Приоритет:** КРИТИЧНЫЙ
**Estimate:** 2 часа

---

**Следующий шаг:** [Посмотреть детальный Roadmap →](04_implementation_roadmap.md)
