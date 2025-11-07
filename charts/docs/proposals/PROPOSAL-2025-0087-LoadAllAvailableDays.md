# PROPOSAL-2025-0087: Загрузка данных за все доступные дни

## Диагностика
Текущая реализация `charts` загружает данные только за один, жестко заданный день. Поступил запрос на изменение этого поведения для загрузки и отображения данных за все доступные дни.

## Предлагаемое изменение (KISS-подход)
В качестве первого шага, чтобы избежать усложнения UI, предлагается реализовать следующую логику: для каждой пары инструментов сканировать все доступные даты в директории с данными, загружать их и объединять в один непрерывный временной ряд.

### Изменения в `charts/server.py`
-   Модифицировать `load_and_process_pair`, чтобы убрать параметр `date`.
-   Внутри `load_data` сканировать все поддиректории `date=*` и собирать все `.parquet` файлы.
-   Убрать логику определения даты из `get_dashboard_data`.

```python
<<<<<<< SEARCH
:start_line:43
-------
async def load_and_process_pair(opportunity: dict, date: str):
    symbol = opportunity['symbol']
    exchange_a = opportunity['exchange1']
    exchange_b = opportunity['exchange2']
    logging.info(f"Starting processing for pair: {symbol} ({exchange_a} vs {exchange_b})")

    async def load_data(exchange):
        date_path = os.path.join(DATA_LAKE_PATH, f"exchange={exchange}", f"symbol={symbol}", f"date={date}")
        if not os.path.exists(date_path):
            logging.warning(f"Data path not found for {exchange}/{symbol} on {date}")
            return None

        files = [os.path.join(hour_path, f)
                 for hour in range(24)
                 for hour_path in [os.path.join(date_path, f"hour={str(hour).zfill(2)}")] if os.path.exists(hour_path)
                 for f in os.listdir(hour_path) if f.endswith('.parquet')]
=======
async def load_and_process_pair(opportunity: dict):
    symbol = opportunity['symbol']
    exchange_a = opportunity['exchange1']
    exchange_b = opportunity['exchange2']
    logging.info(f"Starting processing for pair: {symbol} ({exchange_a} vs {exchange_b}) for ALL available dates.")

    async def load_data(exchange):
        symbol_path = os.path.join(DATA_LAKE_PATH, f"exchange={exchange}", f"symbol={symbol}")
        if not os.path.exists(symbol_path):
            logging.warning(f"Symbol path not found for {exchange}/{symbol}")
            return None

        # Scan all date and hour directories for parquet files
        files = [os.path.join(root, f)
                 for root, _, filenames in os.walk(symbol_path)
                 for f in filenames if f.endswith('.parquet')]
>>>>>>> REPLACE
```
```python
<<<<<<< SEARCH
:start_line:160
-------
async def chart_data_streamer(opportunities, date_to_use):
    """Yields newline-delimited JSON strings for each chart."""
    logging.info(f"Streaming data for {len(opportunities)} opportunities.")
    processed_count = 0
    for opp in opportunities:
        try:
            chart_data = await load_and_process_pair(opp, date_to_use)
            if chart_data:
                yield json.dumps(chart_data) + '\n'
=======
async def chart_data_streamer(opportunities):
    """Yields newline-delimited JSON strings for each chart."""
    logging.info(f"Streaming data for {len(opportunities)} opportunities.")
    processed_count = 0
    for opp in opportunities:
        try:
            chart_data = await load_and_process_pair(opp)
            if chart_data:
                yield json.dumps(chart_data) + '\n'
>>>>>>> REPLACE
```
```python
<<<<<<< SEARCH
:start_line:180
-------
        latest_file = max(csv_files, key=lambda f: os.path.getmtime(os.path.join(analyzer_stats_dir, f)))
        stats_file = os.path.join(analyzer_stats_dir, latest_file)
        logging.info(f"Using stats file: {stats_file}")

        from datetime import timedelta
        date_to_use = (datetime.now() - timedelta(days=1)).strftime("%Y-%m-%d")

        df_opps = pl.read_csv(stats_file)
=======
        latest_file = max(csv_files, key=lambda f: os.path.getmtime(os.path.join(analyzer_stats_dir, f)))
        stats_file = os.path.join(analyzer_stats_dir, latest_file)
        logging.info(f"Using stats file: {stats_file}")

        df_opps = pl.read_csv(stats_file)
>>>>>>> REPLACE
```
```python
<<<<<<< SEARCH
:start_line:189
-------
        ).to_dicts()
        
        return StreamingResponse(chart_data_streamer(opportunities, date_to_use), media_type="application/x-ndjson")
=======
        ).to_dicts()
        
        return StreamingResponse(chart_data_streamer(opportunities), media_type="application/x-ndjson")
>>>>>>> REPLACE
```

## Обоснование
Этот подход реализует требуемую функциональность (загрузка всех дней) с минимальными изменениями. Он позволит сразу увидеть общую картину по каждой паре за весь период.

## Оценка рисков
- **Производительность:** Загрузка и обработка данных за несколько дней для одной пары может быть очень ресурсоемкой и медленной. Существующий механизм потоковой передачи на уровне пар смягчит это, но каждая отдельная пара все равно будет обрабатываться дольше.
- **Визуализация:** Графики могут стать очень "длинными" и трудными для анализа из-за большого количества точек.

## План тестирования
1.  Перезапустить сервер `charts`.
2.  Открыть `charts/index.html`.
3.  Убедиться, что графики загружаются и содержат данные за несколько дней (если они доступны).
4.  Проверить логи сервера на наличие ошибок.

## План отката
1.  Вернуть все изменения в файле `charts/server.py`.