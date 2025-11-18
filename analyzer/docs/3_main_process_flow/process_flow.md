# Детальный процесс выполнения Analyzer (`run_all_ultra.py`)

Этот документ описывает пошаговый процесс выполнения скрипта `run_all_ultra.py`, предназначенного для анализа арбитражных возможностей. Обновлено: 2025-11-19

## Визуализация процесса

```mermaid
%%{init: {'theme': 'base', 'themeVariables': { 'primaryColor': '#f0f0f0', 'edgeLabelBackground':'#f0f0f0', 'clusterBkg': '#f0f0f0'}}}%%
graph TD
    subgraph "Шаг 1: Инициализация"
        A[Start] --> B{Парсинг аргументов CLI};
        B --> C[run_ultra_fast_analysis];
    end

    subgraph "Шаг 2: Оркестрация"
        C --> D{discover_data};
        D --> E{Фильтрация символов};
        E --> F[Формирование задач (1 задача/символ)];
        F --> G[multiprocessing.Pool];
    end

    subgraph "Шаг 3: Параллельная обработка (внутри Pool)"
        G --> H(analyze_symbol_batch);
        H --> I{ThreadPoolExecutor};
        I --> J[load_exchange_symbol_data];
        J --> K[scan_parquet];
        K --> L[DataFrame в памяти];
        L --> M{Анализ всех пар};
        M --> N(analyze_pair_fast);
    end

    subgraph "Шаг 4: Анализ пары (analyze_pair_fast)"
        N --> O[join_asof];
        O --> P[Расчет ratio/deviation];
        P --> Q[Расчет zero_crossings];
        Q --> R[count_complete_cycles];
        R --> S[Сбор всех метрик];
    end

    subgraph "Шаг 5: Завершение"
        S --> T{Сбор результатов из Pool};
        T --> U[Формирование итогового DataFrame];
        U --> V[Сортировка и вывод в консоль];
        V --> W[Сохранение в CSV];
        W --> X[End];
    end

    style H fill:#f9f,stroke:#333,stroke-width:2px
    style N fill:#ccf,stroke:#333,stroke-width:2px
```

## Шаг 1: Инициализация и парсинг аргументов

**Файл:** [`run_all_ultra.py:595`](analyzer/run_all_ultra.py:595)

Процесс начинается в блоке `if __name__ == "__main__":` с инициализации `argparse` парсера.

### 1.1. Определение параметров командной строки
```python
parser = argparse.ArgumentParser(...)
parser.add_argument("--data-path", type=str, default="data/market_data")
parser.add_argument("--exchanges", type=str, nargs='+', default=None)
parser.add_argument("--workers", type=int, default=None)
parser.add_argument("--date", type=str, default=None)
parser.add_argument("--start-date", type=str, default=None)
parser.add_argument("--end-date", type=str, default=None)
parser.add_argument("--thresholds", type=float, nargs=3, default=[0.3, 0.5, 0.4])
parser.add_argument("--today", action="store_true")
```

### 1.2. Обработка флагов дат
```python
# Обработка --today flag
if args.today:
    today_str = date.today().strftime('%Y-%m-%d')
    start_date = today_str
    end_date = today_str
# Обработка --date shortcut  
elif args.date:
    start_date = args.date
    end_date = args.date
else:
    start_date = args.start_date
    end_date = args.end_date
```

### 1.3. Валидация входных данных
```python
# Базовая валидация формата дат
for date_str, name in [(start_date, "start-date"), (end_date, "end-date")]:
    if date_str:
        datetime.strptime(date_str, '%Y-%m-%d')
```

### 1.4. Запуск основного анализа
Вызов `run_ultra_fast_analysis` с распарсенными параметрами.

## Шаг 2: `run_ultra_fast_analysis` - Оркестрация анализа

**Файл:** [`run_all_ultra.py:430`](analyzer/run_all_ultra.py:430)

Главная функция-оркестратор всего процесса.

### 2.1. Отображение информации о фильтрации
```python
if start_date or end_date:
    if start_date and end_date:
        print(f"\n>>> Filtering data: {start_date} to {end_date} <<<")
    elif start_date:
        print(f"\n>>> Filtering data: from {start_date} onwards <<<")
    else:
        print(f"\n>>> Filtering data: up to {end_date} <<<")
else:
    print("\n>>> Analyzing ALL available data <<<")
```

### 2.2. Обнаружение данных (`discover_data`)
**Файл:** [`run_all_ultra.py:400`](analyzer/run_all_ultra.py:400)

**Входные данные:** путь к данным (`data_path`)

**Логика:**
- Сканирование директории, поиск всех подпапок `exchange=*` и `symbol=*`
- Создание `defaultdict(set)` где ключ = имя символа, значение = множество бирж
- Фильтрация только символов, торгующихся на минимум 2 биржах

**Выходные данные:** словарь `symbols_to_analyze` с валидными символами

### 2.3. Фильтрация по биржам
```python
if exchanges_filter:
    exchanges_filter_set = set(exchanges_filter)
    filtered_symbols = {}
    for symbol, exchanges in symbols_to_analyze.items():
        filtered_exchanges = exchanges.intersection(exchanges_filter_set)
        if len(filtered_exchanges) >= 2:
            filtered_symbols[symbol] = filtered_exchanges
    symbols_to_analyze = filtered_symbols
```

### 2.4. Создание задач для батч-обработки
**Ключевая оптимизация:** Одна задача на *символ*, а не на *пару бирж*.

```python
tasks = []
total_pairs = 0
for symbol, exchanges in symbols_to_analyze.items():
    n_pairs = len(list(combinations(exchanges, 2)))
    total_pairs += n_pairs
    tasks.append((symbol, list(exchanges), DATA_PATH, start_date, end_date, thresholds))
```

### 2.5. Параллельное выполнение (`multiprocessing.Pool`)
```python
# Определение количества воркеров
if n_workers is None:
    n_workers = cpu_count() * 3

# Создание пула процессов
with Pool(processes=n_workers) as pool:
    results_batches = pool.imap_unordered(analyze_symbol_batch, tasks, chunksize=1)
    
    # Обработка результатов по мере завершения
    for batch_results in results_batches:
        for result in batch_results:
            processed_pairs += 1
            # обработка SUCCESS/SKIPPED статусов
```

### 2.6. Агрегация и сохранение результатов
```python
if all_stats:
    stats_df = pl.DataFrame(all_stats)
    stats_df = stats_df.sort('zero_crossings_per_minute', descending=True)
    
    # Создание директории summary_stats
    analyzer_dir = Path(__file__).parent
    save_dir = analyzer_dir / "summary_stats"
    os.makedirs(save_dir, exist_ok=True)
    
    # Сохранение с timestamp
    timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')
    stats_filename = save_dir / f"summary_stats_{timestamp}.csv"
    stats_df.write_csv(stats_filename)
```

### 2.7. Отображение топ-результатов
Два отчета топ-10:
1. По частоте среднего реверсирования (`zero_crossings_per_minute`)
2. По количеству полных циклов (`opportunity_cycles_040bp`)

## Шаг 3: `analyze_symbol_batch` - Обработка одного символа

**Файл:** [`run_all_ultra.py:34`](analyzer/run_all_ultra.py:34)

Функция выполняется в отдельном процессе для анализа одного символа на всех доступных биржах.

### 3.1. Параллельная загрузка данных (`ThreadPoolExecutor`)
**Файл:** [`run_all_ultra.py:47`](analyzer/run_all_ultra.py:47)

**Критическая оптимизация:** Одновременная загрузка данных со всех бирж.

```python
with ThreadPoolExecutor(max_workers=len(exchanges)) as executor:
    future_to_exchange = {
        executor.submit(load_exchange_symbol_data, data_path, exchange, symbol, start_date, end_date): exchange
        for exchange in exchanges
    }
    
    # Сбор результатов по мере завершения
    for future in as_completed(future_to_exchange):
        exchange = future_to_exchange[future]
        try:
            data = future.result()
            if data is not None and not data.is_empty():
                exchange_data[exchange] = data
        except Exception:
            pass  # Обработка ошибок загрузки
```

### 3.2. Анализ всех пар бирж
```python
exchange_pairs = list(combinations(sorted(exchanges), 2))

for ex1, ex2 in exchange_pairs:
    if ex1 not in exchange_data or ex2 not in exchange_data:
        results.append({
            'symbol': symbol,
            'ex1': ex1,
            'ex2': ex2,
            'status': 'SKIPPED',
            'stats': None
        })
        continue
    
    # Анализ пары с уже загруженными данными
    stats = analyze_pair_fast(
        symbol, ex1, ex2,
        exchange_data[ex1],
        exchange_data[ex2],
        thresholds
    )
```

### 3.3. Формирование результатов
Возврат списка словарей с результатами для всех пар данного символа.

## Шаг 4: `load_exchange_symbol_data` - Загрузка данных с диска

**Файл:** [`run_all_ultra.py:107`](analyzer/run_all_ultra.py:107)

Функция чтения и предобработки данных для одной пары (биржа, символ).

### 4.1. Построение путей к данным
```python
base_path = Path(data_path)
exchange_path = base_path / f"exchange={exchange}"

# Поддержка двух форматов именования символов
symbol_formats = [
    symbol.replace('/', '#'),  # VIRTUAL#USDT
    symbol.replace('/', '').replace('_', '')  # VIRTUALUSDT
]
```

### 4.2. Фильтрация файлов по датам (до чтения)
**Ключевая оптимизация:** Фильтрация на этапе сбора файлов, а не после загрузки.

```python
if start_date or end_date:
    available_dates = []
    for item in os.scandir(symbol_path):
        if item.is_dir() and item.name.startswith('date='):
            date_str = item.name.split('=')[1]
            if (not start_date or date_str >= start_date) and (not end_date or date_str <= end_date):
                available_dates.append(date_str)
```

### 4.3. Единое сканирование Parquet
**Файл:** [`run_all_ultra.py:177`](analyzer/run_all_ultra.py:177)

**Критическая оптимизация #8:** Одно `scan_parquet` для всех файлов (2-4x быстрее I/O).

```python
# Сбор ВСЕХ parquet файлов для выбранных дат
all_files = []
for date in available_dates:
    date_path = symbol_path / f"date={date}"
    if date_path.exists():
        for hour_dir in date_path.glob("hour=*"):
            all_files.extend(hour_dir.glob("*.parquet"))

# Единое сканирование всех файлов
df = pl.scan_parquet(all_files) \
    .select(['Timestamp', 'BestBid', 'BestAsk']) \
    .rename({'Timestamp': 'timestamp', 'BestBid': 'bestBid', 'BestAsk': 'bestAsk'}) \
    .with_columns([pl.col('bestBid').cast(pl.Float64), pl.col('bestAsk').cast(pl.Float64)]) \
    .filter(pl.col('bestBid').is_not_null() & pl.col('bestAsk').is_not_null()) \
    .collect() \
    .sort('timestamp')
```

### 4.4. Обработка ошибок
Возврат `None` при отсутствии данных или ошибках загрузки.

## Шаг 5: `analyze_pair_fast` - Вычисление метрик пары

**Файл:** [`run_all_ultra.py:200`](analyzer/run_all_ultra.py:200)

Основное ядро системы, где происходят все вычисления для пары бирж.

### 5.1. Синхронизация данных (`join_asof`)
```python
joined = data1.rename({
    'bestBid': 'bid_ex1',
    'bestAsk': 'ask_ex1'
}).join_asof(
    data2.rename({
        'bestBid': 'bid_ex2', 
        'bestAsk': 'ask_ex2'
    }),
    on='timestamp'
)
```

### 5.2. Расчет отношения и отклонения
**Файл:** [`run_all_ultra.py:223`](analyzer/run_all_ultra.py:223)

**Критическая оптимизация #4:** Чистые Polars операции (1.5-2x быстрее, zero-copy).

```python# Векторизованные вычисления
joined = joined.with_columns([
    (pl.col('bid_ex1') / pl.col('bid_ex2')).alias('ratio')
])

# КРИТИЧЕСКОЕ ИСПРАВЛЕНИЕ: deviation от 1.0, НЕ от среднего
joined = joined.with_columns([
    ((pl.col('ratio') - 1.0) / 1.0 * 100).alias('deviation')
])
```

### 5.3. Базовые статистики
```python
max_deviation_pct = float(joined['deviation'].max())
min_deviation_pct = float(joined['deviation'].min())
mean_deviation_pct = float(joined['deviation'].mean())
asymmetry = mean_deviation_pct
```

### 5.4. Детекция пересечений нуля
**Файл:** [`run_all_ultra.py:251`](analyzer/run_all_ultra.py:251)

**Исправленная логика:** Использование умножения для определения истинных смен знака.

```python
deviation_sign = pl.col('deviation').sign()
zero_crossings = int(
    joined.with_columns([
        (deviation_sign * deviation_sign.shift(1) < 0).alias('crossed')
    ])['crossed'].sum()
)
```

### 5.5. Подготовка данных для анализа порогов
**Файл:** [`run_all_ultra.py:283`](analyzer/run_all_ultra.py:283)

```python
ZERO_THRESHOLD = 0.05  # 5 базисных пунктов допуска для шума

if thresholds is None:
    thresholds = [0.3, 0.5, 0.4]

joined_with_thresholds = joined.with_columns([
    (pl.col('deviation').abs() > thresholds[0]).alias('above_030bp'),
    (pl.col('deviation').abs() > thresholds[1]).alias('above_050bp'),
    (pl.col('deviation').abs() > thresholds[2]).alias('above_040bp'),
    (pl.col('deviation').abs() < ZERO_THRESHOLD).alias('in_neutral')
])
```

### 5.6. Подсчет полных циклов (`count_complete_cycles`)
**Файл:** [`run_all_ultra.py:294`](analyzer/run_all_ultra.py:294)

**Критическая проблема:** Использование `.to_numpy()` создает копию данных и ломает zero-copy оптимизации.

**Логика подсчета циклов:**
```python
def count_complete_cycles(above_threshold_series, in_neutral_series):
    above = above_threshold_series.to_numpy()
    neutral = in_neutral_series.to_numpy()
    
    cycles = 0
    was_above = False
    
    for i in range(len(above)):
        if above[i]:
            was_above = True
        elif neutral[i] and was_above:
            cycles += 1  # Полный цикл: был выше порога → вернулся в нейтраль
            was_above = False
    
    return cycles
```

### 5.7. Расчет дополнительных метрик
```python
# Процент времени выше порогов
threshold_metrics = joined_with_thresholds.select([
    (pl.col('above_030bp').mean() * 100).alias('pct_030bp'),
    (pl.col('above_050bp').mean() * 100).alias('pct_050bp'),
    (pl.col('above_040bp').mean() * 100).alias('pct_040bp')
])

# Средняя длительность циклов
avg_duration_040bp_sec = (duration_hours * pct_040bp / 100 * 3600) / cycles_040bp if cycles_040bp > 0 else 0

# Детекция сломанных паттернов
last_deviation = float(joined['deviation'][-1])
pattern_break_040bp = abs(last_deviation) > thresholds[2]
```

### 5.8. Формирование финального словаря
Возврат словаря со всеми рассчитанными метриками.

## Временные характеристики процесса

### Последовательность выполнения:
1. **Инициализация:** < 1 секунда
2. **Обнаружение данных:** 1-10 секунд (зависит от объема данных)
3. **Параллельный анализ:** Основное время выполнения
   - Загрузка данных: I/O bound
   - Анализ пар: CPU bound
4. **Агрегация результатов:** < 5 секунд

### Критические пути:
- **I/O:** Загрузка больших Parquet файлов
- **CPU:** Вычисление метрик для множества пар
- **Memory:** Хранение DataFrame в памяти для повторного использования

## Оптимизации производительности

### Реализованные:
- ✅ **Батч-обработка по символам** - избегание повторной загрузки
- ✅ **Параллельная загрузка бирж** - ThreadPoolExecutor
- ✅ **Единое сканирование Parquet** - `scan_parquet(all_files)`
- ✅ **Чистые Polars операции** - векторизованные вычисления
- ✅ **Lazy evaluation** - отложенное выполнение

### Требуют внимания:
- ❌ **NumPy конверсия в горячем цикле** - ломает zero-copy
- ❌ **Передача больших DataFrame между процессами** - IPC overhead
- ❌ **Отсутствие кэширования** - повторные вычисления
