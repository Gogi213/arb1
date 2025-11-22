# Performance Investigation - 2025-11-22 00:19

## Симптомы
- "Охуительное количество операций в секунду" сразу после запуска localhost
- Приложение работает ~5 секунд и "глохнет"
- Проблема появилась в коммитах 80-88

---

## ✅ STATUS UPDATE - 2025-11-22 02:03

### 🎉 CPU LEAK РЕШЁН (FIX 2+3 Applied)

**Дата**: 2025-11-22 01:30  
**Fix**: Secondary index в `DeviationCalculator.cs`  
**Результат**: Приложение работает **стабильно >10 минут** БЕЗ freeze!

**Что было:**
- O(N²) complexity: 2500 calls/sec × 750k items = 1.875B operations/sec
- CPU leak: рост со временем → freeze через 5 минут

**Что стало:**
- O(1) complexity: 2500 calls/sec × 3 items = 7,500 operations/sec
- CPU stable: ~30-40% константно, НЕ растёт

**Proof**: `docs/gemini3/context/cpu_leak_mathematical_proof.md`

---

### ⚠️ ОСТАЛИСЬ ФРИЗЫ (Logging Overhead)

**Источник**: Избыточное логирование в hot path  
**Impact**: Периодические micro-freezes  
**Audit**: `docs/gemini3/context/logging_audit_collections.md`

**Топ-3 Critical Sources:**
1. **OrchestrationService.cs:275, 279** - Console.WriteLine при channel overflow (до 2500/sec)
2. **FleckWebSocketServer.cs:81** - Console.WriteLine при каждой WebSocket ошибке
3. **RollingWindowService.cs:542, 547** - Stopwatch в hot path (зачем??)

**Next Step**: Убрать/rate-limit критичные logs

---

## 🎯 ROOT CAUSE (ФИНАЛЬНАЯ ДИАГНОСТИКА) - ✅ РЕШЕНО

**ВАЖНО**: Binance был активен ДО коммитов 84-85 и работал нормально. Проблема НЕ в количестве бирж!

### Реальная Цепочка Проблемы:

```
1. DeviationCalculator.ProcessSpread() вызывается на КАЖДОМ spread update
   (сотни calls/сек для 3 бирж × 100+ символов)
                    ↓
2. ❌ TryCalculateDeviation(): ЛИНЕЙНЫЙ ПОИСК по ConcurrentDictionary
   (_latestSpreads.Where(EndsWith/StartsWith) на каждом вызове)
                    ↓
3. _latestSpreads растёт: N(t) = 2500t items
   → Iterations: 2500/sec × N(t) = 12,500t iterations/sec
                    ↓
4. CPU usage растёт ЛИНЕЙНО со временем
   T=300sec: 3,750,000 iterations/sec → FREEZE
```

### ✅ Fix Applied (FIX 2+3)

**Файл**: `DeviationCalculator.cs`

**BEFORE:**
```csharp
// O(N) linear search через все exchanges
var otherExchangeSpreads = _latestSpreads
    .Where(kvp => kvp.Key.EndsWith($":{newSpread.Symbol}") && ...)
```

**AFTER:**
```csharp
// O(1) dictionary lookup
private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SpreadData>> _spreadsBySymbol;

if (!_spreadsBySymbol.TryGetValue(newSpread.Symbol, out var exchangeDict))
    return;
var otherExchangeSpreads = exchangeDict  // Only ~3 items!
    .Where(kvp => kvp.Key != newSpread.Exchange)
```

**Impact**: 500x faster на t=300sec!

---

## 🐛 Конкретные Проблемы в Коде

### ✅ FIXED: ConcurrentBag CPU Leak
**Файл**: `collections/src/SpreadAggregator.Application/Services/DeviationCalculator.cs`
**Status**: **РЕШЕНО** (FIX 2+3)
**Proof**: Mathematical proof в `cpu_leak_mathematical_proof.md`

---

### ⚠️ OPEN: Excessive Logging
**Status**: **ТРЕБУЕТ ВНИМАНИЯ**  
**Файлы**: 
- `OrchestrationService.cs` (lines 275, 279)
- `FleckWebSocketServer.cs` (line 81)
- `RollingWindowService.cs` (lines 542, 547)

**Audit**: `logging_audit_collections.md`

---

### ❌ Проблема 1: Binance активен (не по roadmap)
**Файл**: `collections/src/SpreadAggregator.Presentation/appsettings.json:20-25`
**Roadmap Phase 1**: Указано только **Gate + Bybit**
**Факт**: **3 биржи активны** (Binance включён)

**Evidence**: websocket.log показывает:
- Bybit: 100 символов
- Gate: 142 символа
- Binance: 277 символов ← НЕ ДОЛЖЕН БЫТЬ АКТИВЕН

---

### ❌ Проблема 2: MinDeviationThreshold слишком низкий
**Файл**: `collections/src/SpreadAggregator.Presentation/Program.cs:166`
**Код**:
```csharp
var minThreshold = configuration.GetValue<decimal>("Arbitrage:MinDeviationThreshold", 0.10m);
```

**Проблема**: 
- В `appsettings.json` НЕТ секции `Arbitrage`
- Используется дефолт **0.10%**
- При таком пороге **почти любое** колебание цены генерирует event

**Impact**: Лавина событий даже при низкой волатильности

---

## References
- ✅ CPU Leak Fix: `cpu_leak_mathematical_proof.md`
- ⚠️ Logging Audit: `logging_audit_collections.md`
- Roadmap: `docs/gemini3/roadmap/phase-1-brain.md`
- Code commits: 80 (accf4ef1) → 88 (5e5da648)
- Websocket.log: Last session 2025-11-21 20:06-20:07

