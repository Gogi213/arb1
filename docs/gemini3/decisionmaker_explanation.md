# DecisionMaker: Что это и зачем

## Краткий ответ
**DecisionMaker** — это **deprecated** (устаревший) компонент **двуногого арбитража** (two-legged arbitrage), который был **УДАЛЕН из кодовой базы** (2025-11-19).

**Текущий статус**: ❌ **REMOVED** (только упоминание в комментарии Program.cs:39)

---

## История

### Изначальная идея (2024-2025)
Проект начинался как **two-legged arbitrage bot**:
1. **SpreadListener** слушает WebSocket от Collections
2. Если спред Gate→Bybit > 0.25% → событие `OnProfitableSpreadDetected`
3. **DecisionMaker** должен был:
   - Поймать событие
   - Запустить `ArbitrageTrader` (купить на Gate, продать на Bybit)
   - Управлять состоянием цикла

### Почему deprecated?
Из `backlog.md`:
> **Legacy Two-Legged Arbitrage (`ArbitrageTrader`, `DecisionMaker`):** **Deprecated and inactive.** While the code remains for historical context, the complex two-legged arbitrage strategy is no longer the active trading mechanism. **It was found to be prone to issues related to synchronization, fees, and state management.**

**Проблемы**:
- Синхронизация двух бирж (Gate медленнее, Bybit быстрее)
- Комиссии съедали прибыль
- Race conditions (balance updates приходят async)

---

## Что делает сейчас?

### В коде (`DecisionMaker.cs`):
```csharp
private void HandleProfitableSpread(string direction, decimal spread)
{
    if (_isCycleInProgress)
    {
        FileLogger.LogOther("Ignoring"); // Уже идет цикл
        return;
    }

    _isCycleInProgress = true;
    FileLogger.LogOther("Profitable spread detected! Starting arbitrage cycle...");
    
    // TODO:
    // 1. Get Orchestrator/Traders via DI
    // 2. Start the correct trader based on 'direction'
    // 3. Wait for the cycle to complete and set _isCycleInProgress = false;
}
```

**Реально делает**: НИЧЕГО.  
Просто **логирует** "Starting arbitrage cycle...", но торговлю **НЕ запускает**.

### В `Program.cs`:
```csharp
// Если нет аргументов командной строки:
var listener = new SpreadListener(spreadListenerUrl);
var decisionMaker = new DecisionMaker();
decisionMaker.Subscribe(listener);

await listener.StartAsync(CancellationToken.None);
```

**По дефолту** (без аргументов) бот:
1. Подключается к Collections
2. Слушает спреды
3. Логирует их
4. **НЕ ТОРГУЕТ**

---

## Реальный функционал сейчас

### Если запустить `dotnet run`:
- Запустится deprecated путь (SpreadListener + DecisionMaker)
- Логирует спреды, но **не торгует**

### Если запустить `dotnet run gate` или `dotnet run bybit`:
- Запустится **ConvergentTrader** (активная стратегия)
- Торгует на одной бирже (buy → wait → sell)
- DecisionMaker **НЕ задействован**

---

## Зачем он до сих пор в коде?

Из документации:
> **While the code remains for historical context**

То есть:
- Сохранен для истории
- Возможно планируется реанимация
- Но **не активен**

---

## Рекомендация

**Варианты**:
1. **Удалить** (если двуногий арбитраж не планируется)
2. **Оставить** как есть (если есть идея вернуться к нему)
3. **Реализовать TODO** (если хочешь использовать его снова)

**Мое мнение**: Если ConvergentTrader работает и прибыльный — оставь DecisionMaker "законсервированным" (как сейчас). Если места на диске не хватает или путает — удали.

Что решаешь?
