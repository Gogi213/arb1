# Refactoring Recommendations

## Confirmed Working ✅
- Collections запущен и работает
- PROPOSAL 001 реализован (Stale Data Fix)
- Dead code удален (4 файла)

---

## Что осталось для рефакторинга

### 1. 🗑️ SpreadAggregator.Analyzer (Dead Project)
**Location**: `collections/src/SpreadAggregator.Analyzer/`

**Status**: 
- Директория содержит только `bin/` и `obj/` — нет исходников
- `grep` не нашел упоминаний в кодовой базе
- Судя по всему, это остатки старого проекта

**Recommendation**: **УДАЛИТЬ** папку целиком.

**Risk**: Минимальный (нет ссылок в коде).

---

### 2. 🚧 DecisionMaker (Hollow Class)
**Location**: `trader/src/Core/DecisionMaker.cs`

**Current State**:
```csharp
// TODO:
// 1. Get Orchestrator/Traders via DI
// 2. Start the correct trader based on 'direction'
// 3. Wait for the cycle to complete and set _isCycleInProgress = false;
```

**Problem**: Класс существует, но ничего не делает. Только устанавливает флаг, но не запускает трейдера.

**Options**:
- **A) Удалить** DecisionMaker, переместить флаг `_isCycleInProgress` в `Program.cs`
- **B) Реализовать** TODO (нужен DI, фабрика трейдеров)

**Recommendation**: **Вариант A** (удалить). Сейчас это просто мертвый вес.

**Risk**: Низкий (логика не используется в реальной торговле).

---

### 3. 🔀 Symbol Normalization Duplication

**Problem**: Collections и Analyzer нормализуют символы **по-разному**.

**Collections** (`OrchestrationService.cs:214-228`):
```csharp
var normalizedSymbol = spreadData.Symbol
    .Replace("/", "")
    .Replace("-", "")
    .Replace("_", "")
    .Replace(" ", "");

if (normalizedSymbol.EndsWith("USDT"))
    normalizedSymbol = normalizedSymbol[..^4] + "_USDT";
```
Result: `VIRTUAL_USDT`

**Analyzer** (`lib/data_loader.py:126-128`):
```python
symbol_formats = [
    symbol.replace('/', '#'),  # VIRTUAL#USDT
    symbol.replace('/', '').replace('_', '')  # VIRTUALUSDT
]
```
Result: Ищет `VIRTUAL#USDT` ИЛИ `VIRTUALUSDT`

**Impact**: Если форматы не совпадут — Analyzer не найдет данные.

**Recommendation**: 
1. Проверить на реальных данных — есть ли проблема
2. Если есть — унифицировать в **один** формат (предлагаю `SYMBOL_QUOTE`)

**Risk**: Средний (может сломаться связь Collections → Analyzer).

---

## Priority

| # | Task | Priority | Risk | Effort |
|---|------|----------|------|--------|
| 1 | Удалить `SpreadAggregator.Analyzer/` | Low | Minimal | 1 min |
| 2 | Удалить `DecisionMaker.cs` | Medium | Low | 5 min |
| 3 | Проверить symbol normalization | High | Medium | 15 min |

Делать что-то из этого?
