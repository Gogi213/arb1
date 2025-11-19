# Code Quality Report: Dead Code & Redundancies

## 🔴 Critical: Dead Code

### 1. analyzer/run_all_ultra_old.py & run_all_ultra_v2.py
**Problem**: **3 versions** одного и того же скрипта:
- `run_all_ultra.py` (текущий, использует `lib/`)
- `run_all_ultra_v2.py` (копия `run_all_ultra.py`)
- `run_all_ultra_old.py` (старая версия, 673 строки, дублирует всю логику)

**Impact**: 
- 27KB мертвого кода
- Confusion: какой файл запускать?
- Риск: случайно отредактировать не тот файл

**Recommendation**: 
```bash
# Удалить
rm analyzer/run_all_ultra_old.py
rm analyzer/run_all_ultra_v2.py

# Оставить только 
analyzer/run_all_ultra.py
```

---

### 2. collections: Class1.cs (Placeholder Classes)
**Problem**: **2 пустых класса** созданных Visual Studio:
- `SpreadAggregator.Domain/Class1.cs`
- `SpreadAggregator.Infrastructure/Class1.cs`

```csharp
public class Class1  // Ничего не делает
{
}
```

**Impact**: Zero функциональности, захламляет проект.

**Recommendation**: Удалить оба.

---

## 🟡 Medium: Избыточные слои

### 3. collections: SpreadAggregator.Analyzer (unused project?)
**Observation**: В `collections/src/` есть проект `SpreadAggregator.Analyzer`, но:
- Не подключен к solution?
- Нет импортов в других проектах?

**Question**: Этот проект часть системы или мертвая ветка?

**Recommendation**: Проверить `.sln` файл. Если не используется — удалить.

---

### 4. trader: DecisionMaker (hollow stub)
**Current state**:
```csharp
// DecisionMaker.cs:25-28
// TODO:
// 1. Get Orchestrator/Traders via DI
// 2. Start the correct trader based on 'direction'
// 3. Wait for the cycle to complete and set _isCycleInProgress = false;
```

**Problem**: Класс существует, но логика не реализована. Просто флаг `_isCycleInProgress`.

**Impact**: Создает иллюзию функциональности. "Вроде есть DecisionMaker", но он ничего не решает.

**Recommendation**: 
- Либо удалить (переместить логику в `Program.cs`)
- Либо реализовать TODO (PROPOSAL 004)

---

## 🟢 Low: Naming & Clarity

### 5. Collections: Dублирование логики нормализации символов

**Location 1**: `OrchestrationService.cs:214-228`
```csharp
var normalizedSymbol = spreadData.Symbol
    .Replace("/", "")
    .Replace("-", "")...
```

**Location 2**: `analyzer/lib/data_loader.py:126-128`
```python
symbol_formats = [
    symbol.replace('/', '#'),
    symbol.replace('/', '').replace('_', '')
]
```

**Problem**: Две системы (Collections + Analyzer) делают нормализацию **по-разному**.
- Collections: убирает `/`, добавляет `_USDT`
- Analyzer: заменяет `/` на `#`

**Impact**: Если Collection сохранит `VIRTUAL_USDT`, а Analyzer ищет `VIRTUAL#USDT` — мисс.

**Recommendation**: Вынести нормализацию в **shared constant** или **config**.

---

## Summary

| Issue | Severity | LOC | Action |
|-------|----------|-----|--------|
| 3 версии analyzer скрипта | Critical | ~900 | DELETE 2 files |
| Class1.cs × 2 | Critical | 10 | DELETE 2 files |
| SpreadAggregator.Analyzer? | Medium | ? | Verify + DELETE |
| DecisionMaker hollow | Medium | 30 | Реализовать или удалить |
| Symbol normalization дубль | Low | - | Centralize logic |

**Total Dead Code**: ~1000 строк (~4% кодовой базы)

Продолжить с удалением?
