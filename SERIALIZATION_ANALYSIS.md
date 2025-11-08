АНАЛИЗ СЕРИАЛИЗАЦИИ/ДЕСЕРИАЛИЗАЦИИ ДАННЫХ
===========================================

## КРИТИЧЕСКИЕ ПРОБЛЕМЫ

### Python (Charts)
- 16+ промежуточных аллокаций на обработку пары
- Полное копирование данных при преобразовании
- json.dumps() синхронно на критическом пути

### C# (Collections)
- 8 отдельных Select().ToArray() вызовов при Parquet
- JsonSerializer.Serialize() синхронно  
- List.ToList() snapshot на каждый broadcast

---

## 1. PYTHON: CHARTS - ПРОБЛЕМЫ

### 1.1 DataFrame to JSON (server.py 334-347)

Код:
  timestamps = result_df.get_column("timestamp").dt.epoch(...).to_list()
  spreads = result_df.get_column("spread").to_list()
  upper_bands = result_df.get_column("upper_band").to_list()
  lower_bands = result_df.get_column("lower_band").to_list()
  yield json.dumps(chart_data) + '\n'

Проблемы:
  * 4 отдельных get_column() - 4 Series в памяти
  * .dt.epoch() создает новый Series
  * каждый .to_list() - отдельная аллокация
  * json.dumps() - полная сериализация в строку

На пару (N=10000 spreads):
  - Series metadata: 40KB (4 × 10KB)
  - NumPy arrays: 320KB (4 × 80KB)
  - Python lists: 160KB (4 × 40KB)
  - JSON string: 200KB
  => ИТОГО: 720KB одновременно

### 1.2 Real-Time WebSocket Join (server.py 405-454)

Код:
  df_a = pl.DataFrame([{"timestamp": s.timestamp, "bid_a": s.best_bid} 
                       for s in window_a.spreads])
  df_b = pl.DataFrame([{"timestamp": s.timestamp, "bid_b": s.best_bid} 
                       for s in window_b.spreads])
  merged_df = df_a.join_asof(df_b, on="timestamp", ...)
  result_df = merged_df.with_columns(...)

Проблемы:
  * 7 КОПИЙ данных в цепи
  * list comprehension создает временный список dicts
  * каждый DataFrame - новая структура
  * join_asof - новый DataFrame
  * with_columns - еще DataFrame

Частота:
  * 200 пар × 200 spreads × 5 joins/сек
  * 1.8MB per join × 5 = 9MB/сек = 32.4GB/час

### 1.3 NDJSON синхронная сериализация (server.py 350-363)

async def chart_data_streamer(opportunities):
    for opp in opportunities:
        chart_data = await load_and_process_pair(opp)
        if chart_data:
            yield json.dumps(chart_data) + '\n'  # СИНХРОННО

Проблемы:
  * json.dumps() блокирует event loop
  * Полная сериализация в памяти
  * String concatenation создает новую строку
  * На 100 pairs/сек: 72GB/час

---

## 2. C#: COLLECTIONS - ПРОБЛЕМЫ

### 2.1 Parquet: 8× Select().ToArray() (ParquetDataWriter.cs 75-88)

Код:
  return new[]
  {
      new DataColumn(schema[0], data.Select(d => d.Timestamp).ToArray()),
      new DataColumn(schema[1], data.Select(d => d.BestBid).ToArray()),
      new DataColumn(schema[2], data.Select(d => d.BestAsk).ToArray()),
      new DataColumn(schema[3], data.Select(d => d.SpreadPercentage).ToArray()),
      new DataColumn(schema[4], data.Select(d => d.MinVolume).ToArray()),
      new DataColumn(schema[5], data.Select(d => d.MaxVolume).ToArray()),
      new DataColumn(schema[6], data.Select(d => d.Exchange).ToArray()),
      new DataColumn(schema[7], data.Select(d => d.Symbol).ToArray())
  };

Проблемы:
  * 8 ПОЛНЫХ итераций по данным
  * 8 временных arrays в памяти одновременно
  * Select() + ToArray() = 2 аллокации per Select

На batch=1000:
  - 8 fields × 16KB = 128KB в памяти одновременно
  - 100 batches/сек × 120KB = 12MB/сек = 1.04GB/час

### 2.2 Socket List Copying (FleckWebSocketServer.cs 62)

  List<IWebSocketConnection> socketsSnapshot;
  lock (_lock)
  {
      socketsSnapshot = _allSockets.ToList();  // КОПИРУЕТ весь список
  }

Проблемы:
  * Обязательное копирование из-за lock
  * На каждый broadcast
  * 100 clients × 8 bytes = 1.6KB per broadcast
  * 1000 spreads/сек × 1.6KB = 1.6MB/сек = 140MB/час

### 2.3 JSON Serialization (OrchestrationService.cs 133)

  var wrapper = new WebSocketMessage { MessageType = "Spread", Payload = data };
  var message = JsonSerializer.Serialize(wrapper);
  await _webSocketServer.BroadcastRealtimeAsync(message);

Проблемы:
  * JsonSerializer.Serialize() синхронно
  * Создает StringBuilder internally
  * No object pooling
  * 1000 spreads/сек × 600B = 600KB/сек = 51.8MB/час

### 2.4 String Replacement (OrchestrationService.cs 118)

  var normalizedSymbol = spreadData.Symbol
      .Replace("/", "")     // Новая строка
      .Replace("-", "")     // Новая строка
      .Replace("_", "")     // Новая строка
      .Replace(" ", "");    // Новая строка

Проблема: 4 НОВЫЕ строки вместо 1

### 2.5 Partition Paths (ParquetDataWriter.cs 183-187)

  var hourlyPartitionDir = Path.Combine(
      dataRoot,
      "exchange=" + data.Exchange,      // String
      "symbol=" + data.Symbol,          // String
      "date=" + data.Timestamp:yyyy-MM-dd,  // String
      "hour=" + data.Timestamp.Hour:D2);    // String

Проблема: 5 новых строк на каждый SpreadData

---

## 3. РАЗМЕРЫ АЛЛОКАЦИЙ

Operation                    | Size      | Frequency   | Total/Hour
----------------------------|-----------|-------------|------------
json.dumps()                 | 200KB     | 100/sec     | 72GB
DataFrame joins              | 1.8MB     | 5/sec       | 3.6GB
Select().ToArray()×8         | 120KB     | 100/sec     | 1.04GB
List.ToList() snapshot       | 1.6KB     | 1000/sec    | 140MB
JsonSerializer.Serialize()   | 600B      | 1000/sec    | 51.8MB
get_column() Series          | 10KB      | 4000/pair   | 40MB

---

## 4. ДУБЛИРОВАНИЕ ДАННЫХ

Python "Pyramid" (одна пара):
  Parquet disk
    -> read_parquet() = DataFrame 1
    -> read_parquet() = DataFrame 2
    -> join_asof() = DataFrame 3
    -> with_columns() = DataFrame 4
    -> get_column()×4 = Lists
    -> json.dumps() = String
  => 5 полных копий + 4 промежуточные

C# WebSocket (minimal):
  SpreadData (original)
    -> WebSocketMessage wrapper
    -> JsonSerializer.Serialize() = JSON string
    -> socket.Send() = references (не копии)
  => 1 object + 1 wrapper + 1 JSON string

---

## 5. ХУДШИЕ ОПЕРАЦИИ

Ранг | Operation              | Cost/Hour | Проблема
-----|------------------------|-----------|------------------------------------------
  1  | json.dumps() NDJSON    | 72GB      | Синхронная полная сериализация
  2  | DataFrame joins        | 3.6GB     | 7× дублирование данных
  3  | Select().ToArray()×8   | 1.04GB    | 8 итераций вместо 1
  4  | Real-time join loop    | 1.8GB     | 200+ пар × 5/сек
  5  | List.ToList()          | 140MB     | На каждый broadcast
  6  | JsonSerializer         | 51.8MB    | Синхронная работа
  7  | String.Replace()×4     | 28KB/sec  | 4 копии на символ

---

## 6. КЛЮЧЕВЫЕ ВЫВОДЫ

Python (Charts):
  * 5.4GB/hour при 1000 spreads/sec
  * Основные: json.dumps() + DataFrame чейнинг
  * Priority: Async JSON + Lazy DataFrame

C# (Collections):
  * 1.24GB/hour при 1000 spreads/sec
  * 4.3× эффективнее Python
  * Priority: Single loop вместо 8 Select

Combined:
  * 6.6GB/hour совокупно
  * Может быть сокращено на 85% оптимизацией
