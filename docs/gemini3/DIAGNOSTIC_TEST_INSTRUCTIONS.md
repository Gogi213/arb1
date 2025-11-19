# Diagnostic Test: Проверка исправления частоты обновлений

## Что добавлено:
1. **DiagnosticCounters** — счетчики входящих/исходящих обновлений
2. **DiagnosticsController** — API для просмотра счетчиков

## Как использовать:

### 1. Запустить приложение
```bash
cd c:\visual projects\arb1\collections
dotnet build
dotnet run --project src/SpreadAggregator.Presentation
```

### 2. Подождать несколько минут (пусть данные накопятся)

### 3. Проверить счетчики для XPL_USDT
Открыть в браузере:
```
http://localhost:5000/api/diagnostics/counters/XPL_USDT
```

## Что означают цифры:

### До исправления (ожидалось):
```json
{
  "totalIncoming": 1000,   // Получено 1000 WS обновлений
  "totalOutgoing": 500,    // Отправлено ~500 событий (только одна сторона)
  "ratio": 0.5             // Коэффициент ~0.5
}
```

### После исправления (ожидается):
```json
{
  "totalIncoming": 1000,   // Получено 1000 WS обновлений
  "totalOutgoing": 2000,   // Отправлено ~2000 событий (обе стороны!)
  "ratio": 2.0             // Коэффициент ~2.0
}
```

## Объяснение:

**До исправления:**
- Получили обновление от Gate → рассчитали спред `Gate→Bybit` → 1 событие
- Получили обновление от Bybit → рассчитали спред `Bybit→Gate` → 1 событие
- **Итого:** 2 обновления → 2 события (ratio = 1.0)

**После исправления:**
- Получили обновление от Gate:
  - Рассчитали `Gate→Bybit` (используем Gate Bid + Bybit Ask) → 1 событие
  - Рассчитали `Bybit→Gate` (используем Bybit Bid + Gate Ask) → 1 событие
- Получили обновление от Bybit:
  - Рассчитали `Bybit→Gate` (используем Bybit Bid + Gate Ask) → 1 событие
  - Рассчитали `Gate→Bybit` (используем Gate Bid + Bybit Ask) → 1 событие
- **Итого:** 2 обновления → 4 события (ratio = 2.0)

**Важно:** Ratio может быть не ровно 2.0, потому что:
- Если есть 3+ биржи, он будет выше (3 биржи → ratio ≈ 3-4)
- Если какие-то spread points отбрасываются (staleness > 5 сек), ratio будет ниже

## Другие полезные endpoints:

### Топ-20 символов по активности:
```
http://localhost:5000/api/diagnostics/counters
```

### Сбросить счетчики:
```
POST http://localhost:5000/api/diagnostics/counters/reset
```

## Ожидаемый результат:
Если исправление работает правильно, вы увидите сообщение:
```
"✅ FIX WORKING: 2000 events from 1000 inputs (2.00x multiplier - both directions calculated)"
```
