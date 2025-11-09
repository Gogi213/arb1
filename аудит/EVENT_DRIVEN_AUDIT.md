# EVENT-DRIVEN ARCHITECTURE AUDIT

**Дата:** 2025-11-09
**Проблема:** Графики обновляются батчами, несколько точек на одном таймстемпе, не выглядит как event-driven

---

## 🔍 КРИТИЧЕСКИЕ ПРОБЛЕМЫ

### ❌ ПРОБЛЕМА #1: JoinRealtimeWindows() возвращает ВЕСЬ график

**RollingWindowService.cs:112-182**

```csharp
public RealtimeChartData? JoinRealtimeWindows(string symbol, string exchange1, string exchange2)
{
    var window1 = GetWindowData(exchange1, symbol);  // ← 30 минут данных
    var window2 = GetWindowData(exchange2, symbol);  // ← 30 минут данных

    var data1 = window1.Spreads.OrderBy(s => s.Timestamp).ToList();  // ← ВСЕ точки
    var data2 = window2.Spreads.OrderBy(s => s.Timestamp).ToList();  // ← ВСЕ точки

    var joined = AsOfJoin(data1, data2, ...);  // ← Join ВСЕХ точек

    // Returns ALL data points (30 minutes worth)
    return new RealtimeChartData {
        Timestamps = epochTimestamps,  // ← Тысячи точек
        Spreads = spreads,
        UpperBand = upperBands,
        LowerBand = lowerBands
    };
}
```

**Что происходит:**
1. Event приходит: "New Bybit ICPUSDT data"
2. RealTimeController вызывает `JoinRealtimeWindows()`
3. `JoinRealtimeWindows()` возвращает **весь rolling window** (30 минут данных)
4. WebSocket отправляет **весь график** (тысячи точек)
5. Клиент делает `uplot.setData(dataForPlot)` → перерисовывает **весь график**

**Результат:**
- ❌ Event-driven по триггеру, но НЕ по данным
- ❌ Отправляются тысячи точек вместо одной новой
- ❌ Клиент перерисовывает весь график каждый раз
- ❌ Выглядит как batching, потому что все точки приходят сразу

---

### ❌ ПРОБЛЕМА #2: Таймстемпы в СЕКУНДАХ, не миллисекундах

**RollingWindowService.cs:167-170**

```csharp
// Convert to epoch timestamps
var epochTimestamps = joined.Select(x =>
    ((DateTimeOffset)x.timestamp).ToUnixTimeSeconds()  // ← СЕКУНДЫ, не миллисекунды
).Select(x => (double)x).ToList();
```

**Что происходит:**
- DateTime имеет точность миллисекунды
- AsOfJoin сохраняет таймстемп из data1 (миллисекундная точность)
- Конвертация в **ToUnixTimeSeconds()** → теряется миллисекундная точность
- Несколько точек в одну секунду → одинаковый epoch timestamp

**Пример:**
```
Input:
2025-11-09 12:34:56.123 → spread 0.5
2025-11-09 12:34:56.456 → spread 0.6
2025-11-09 12:34:56.789 → spread 0.7

Output (ToUnixTimeSeconds):
1731155696 → spread 0.5
1731155696 → spread 0.6  ← SAME timestamp
1731155696 → spread 0.7  ← SAME timestamp
```

**Результат:**
- ❌ Несколько точек на одном таймстемпе
- ❌ uPlot рисует несколько значений на одной X-координате
- ❌ Выглядит как "приклеивание" точек

---

### ❌ ПРОБЛЕМА #3: AsOfJoin берёт таймстемп только из data1

**RollingWindowService.cs:184-211**

```csharp
private List<(DateTime timestamp, decimal bid1, decimal bid2)> AsOfJoin(...)
{
    var result = new List<(DateTime, decimal, decimal)>();

    for (int i = 0; i < data1.Count; i++)
    {
        var targetTs = data1[i].ts;  // ← Берём таймстемп из data1

        // Find closest backward match within tolerance
        var matchTs = data2[j - 1].ts;
        if ((targetTs - matchTs) > tolerance) continue;

        result.Add((targetTs, data1[i].bid, data2[j - 1].bid));  // ← Используем targetTs
        //          ^^^^^^^^
        //          Всегда таймстемп из data1
    }

    return result;
}
```

**Что происходит:**
- Итерируем по data1 (Bybit)
- Для каждой точки ищем closest match в data2 (GateIo)
- Результирующий таймстемп = таймстемп из data1
- Если data1 имеет 10 точек в секунду → 10 точек с разными миллисекундами
- Но после ToUnixTimeSeconds() → все 10 точек на одной секунде

**Результат:**
- ❌ Таймстемпы не синхронизированы между биржами
- ❌ "Приклеивание" к таймстемпам data1

---

## 📊 FLOW ANALYSIS

**Текущий flow (НЕПРАВИЛЬНЫЙ):**

```
1. New Bybit ICPUSDT data arrives
   ↓
2. RollingWindowService.ProcessData()
   - Adds to window.Spreads (List<SpreadData>)
   - Raises WindowDataUpdated event
   ↓
3. RealTimeController.handler (event subscriber)
   ↓
4. JoinRealtimeWindows(ICPUSDT, Bybit, GateIo)
   - Gets window1.Spreads (ALL 30 minutes of Bybit data) ← ПРОБЛЕМА
   - Gets window2.Spreads (ALL 30 minutes of GateIo data) ← ПРОБЛЕМА
   - AsOfJoin on ALL data ← ПРОБЛЕМА
   - Returns 1000+ points ← ПРОБЛЕМА
   ↓
5. WebSocket.SendAsync(ALL 1000+ points) ← ПРОБЛЕМА
   ↓
6. Client receives 1000+ points
   ↓
7. uplot.setData(dataForPlot) → redraws entire chart ← ПРОБЛЕМА
```

**Почему выглядит как batching:**
- Клиент получает 1000+ точек СРАЗУ
- uPlot рисует все точки ОДНОВРЕМЕННО
- Визуально выглядит как batch update, а не incremental

---

## ✅ ПРАВИЛЬНЫЙ FLOW (Event-driven + Incremental)

**Вариант 1: Отправлять только новую точку**

```
1. New Bybit ICPUSDT data arrives
   ↓
2. RollingWindowService.ProcessData()
   - Adds to window.Spreads
   - Calculate NEW joined point (only latest)
   - Raises WindowDataUpdated(newPoint)
   ↓
3. RealTimeController.handler
   ↓
4. GetLatestJoinedPoint(ICPUSDT, Bybit, GateIo)
   - Returns ТОЛЬКО новую точку
   ↓
5. WebSocket.SendAsync(SINGLE new point)
   ↓
6. Client receives SINGLE point
   ↓
7. uplot.addData(newPoint) → append to chart (incremental)
```

**Вариант 2: Кэшировать предыдущий результат**

```
1. New data arrives
   ↓
2. ProcessData()
   - Check if join result changed
   - If yes → raise event with DIFF
   ↓
3. RealTimeController
   ↓
4. WebSocket.SendAsync(ONLY changes)
   ↓
5. Client updates incrementally
```

---

## 🔧 RECOMMENDED FIX

### Fix #1: Миллисекундные таймстемпы

```csharp
// RollingWindowService.cs:167-170
// БЫЛО:
var epochTimestamps = joined.Select(x =>
    ((DateTimeOffset)x.timestamp).ToUnixTimeSeconds()  // ← СЕКУНДЫ
).Select(x => (double)x).ToList();

// ДОЛЖНО БЫТЬ:
var epochTimestamps = joined.Select(x =>
    ((DateTimeOffset)x.timestamp).ToUnixTimeMilliseconds() / 1000.0  // ← Миллисекунды → секунды с дробью
).ToList();
```

**Результат:**
```
Input:
2025-11-09 12:34:56.123 → 1731155696.123
2025-11-09 12:34:56.456 → 1731155696.456
2025-11-09 12:34:56.789 → 1731155696.789
```

---

### Fix #2: Incremental updates (сложно)

**Option A: Track last sent data**

```csharp
// RollingWindowService
private ConcurrentDictionary<string, DateTime> _lastSentTimestamp = new();

public RealtimeChartData? GetIncrementalUpdate(string symbol, string exchange1, string exchange2)
{
    var key = $"{symbol}_{exchange1}_{exchange2}";
    var lastSent = _lastSentTimestamp.GetValueOrDefault(key, DateTime.MinValue);

    var fullData = JoinRealtimeWindows(symbol, exchange1, exchange2);
    if (fullData == null) return null;

    // Filter only NEW points
    var newPoints = fullData.Timestamps
        .Zip(fullData.Spreads, (ts, spread) => (ts, spread))
        .Where(x => FromEpochSeconds(x.ts) > lastSent)
        .ToList();

    if (newPoints.Count == 0) return null;

    _lastSentTimestamp[key] = FromEpochSeconds(newPoints.Last().ts);

    return new RealtimeChartData { /* only new points */ };
}
```

**Option B: Send only last N points**

```csharp
public RealtimeChartData? GetRecentUpdates(string symbol, string exchange1, string exchange2, int count = 10)
{
    var fullData = JoinRealtimeWindows(symbol, exchange1, exchange2);
    if (fullData == null) return null;

    // Take last N points
    var recentCount = Math.Min(count, fullData.Timestamps.Count);

    return new RealtimeChartData {
        Timestamps = fullData.Timestamps.TakeLast(recentCount).ToList(),
        Spreads = fullData.Spreads.TakeLast(recentCount).ToList(),
        UpperBand = fullData.UpperBand.TakeLast(recentCount).ToList(),
        LowerBand = fullData.LowerBand.TakeLast(recentCount).ToList()
    };
}
```

---

## 📋 SUMMARY

**Почему НЕ выглядит event-driven:**

1. ❌ **Отправляется весь график (30 минут)** вместо новой точки
2. ❌ **Таймстемпы в секундах** → несколько точек на одном timestamp
3. ❌ **Клиент перерисовывает всё** вместо incremental update

**Что нужно исправить:**

1. ✅ **Immediate fix:** Миллисекундные таймстемпы (ToUnixTimeMilliseconds / 1000.0)
2. ⚠️ **Medium fix:** Отправлять только последние N точек (например, 10)
3. 🔧 **Long-term fix:** Incremental updates с отслеживанием lastSent

**Архитектура event-driven ПРАВИЛЬНАЯ, но:**
- Events триггерят обновления ✅
- НО отправляются ВСЕ данные вместо новых ❌

---

**Recommendation:** Start with Fix #1 (milliseconds), then consider Fix #2 Option B (last N points).
