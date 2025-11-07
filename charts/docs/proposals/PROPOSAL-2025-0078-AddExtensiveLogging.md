# PROPOSAL-2025-0078: Внедрение расширенного логирования для диагностики сбоев

## Диагностика
Несмотря на предыдущие исправления, ошибка `Unexpected end of JSON input` продолжает возникать при обработке большого количества пар. Это указывает на то, что серверный процесс по-прежнему аварийно завершается по неизвестной причине. Для точной локализации сбоя необходимо внедрить детальное логирование всех этапов обработки данных.

## Предлагаемое изменение
Предлагается добавить стандартный модуль `logging` в `charts/server.py` и разместить логирующие вызовы на всех ключевых этапах выполнения запроса `get_dashboard_data` и в связанных с ним функциях. Это позволит отследить, на каком именно шаге происходит сбой.

### Изменения в `charts/server.py`
```python
<<<<<<< SEARCH
:start_line:6
-------
import polars as pl
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from datetime import datetime
import asyncio

app = FastAPI()
=======
import polars as pl
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from datetime import datetime
import asyncio
import logging

# Configure logging
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')

app = FastAPI()
>>>>>>> REPLACE
```
```python
<<<<<<< SEARCH
:start_line:30
-------
async def load_and_process_pair(opportunity: dict, date: str):
    symbol = opportunity['symbol']
    exchange_a = opportunity['exchange1']
    exchange_b = opportunity['exchange2']

    async def load_data(exchange):
=======
async def load_and_process_pair(opportunity: dict, date: str):
    symbol = opportunity['symbol']
    exchange_a = opportunity['exchange1']
    exchange_b = opportunity['exchange2']
    logging.info(f"Starting processing for pair: {symbol} ({exchange_a} vs {exchange_b})")

    async def load_data(exchange):
>>>>>>> REPLACE
```
```python
<<<<<<< SEARCH
:start_line:37
-------
        if not os.path.exists(date_path):
            return None
=======
        if not os.path.exists(date_path):
            logging.warning(f"Data path not found for {exchange}/{symbol} on {date}")
            return None
>>>>>>> REPLACE
```
```python
<<<<<<< SEARCH
:start_line:45
-------
        if not files:
            return None
=======
        if not files:
            logging.warning(f"No parquet files found for {exchange}/{symbol} on {date}")
            return None
>>>>>>> REPLACE
```
```python
<<<<<<< SEARCH
:start_line:64
-------
    if df_a is None or df_b is None:
        return None
=======
    if df_a is None or df_b is None:
        logging.warning(f"Missing data for one of the exchanges for pair: {symbol} ({exchange_a} vs {exchange_b})")
        return None
>>>>>>> REPLACE
```
```python
<<<<<<< SEARCH
:start_line:76
-------
    if merged_df.height == 0:
        return None
=======
    if merged_df.height == 0:
        logging.warning(f"Asof join resulted in empty dataframe for pair: {symbol} ({exchange_a} vs {exchange_b})")
        return None
>>>>>>> REPLACE
```
```python
<<<<<<< SEARCH
:start_line:101
-------
@app.get("/api/dashboard_data")
async def get_dashboard_data():
    # Find the latest summary stats file from analyzer/summary_stats/
    analyzer_stats_dir = os.path.join(os.path.dirname(__file__), '..', 'analyzer', 'summary_stats')
    if not os.path.exists(analyzer_stats_dir):
        raise HTTPException(status_code=404, detail="Analyzer summary_stats directory not found.")

    # Get all CSV files and find the latest one by modification time
    csv_files = [f for f in os.listdir(analyzer_stats_dir) if f.endswith('.csv')]
    if not csv_files:
        raise HTTPException(status_code=404, detail="No summary stats CSV files found.")

    # Find the most recent file
    latest_file = max(csv_files, key=lambda f: os.path.getmtime(os.path.join(analyzer_stats_dir, f)))
    stats_file = os.path.join(analyzer_stats_dir, latest_file)

    try:
        # Use yesterday's date since analysis was run for previous day
        from datetime import timedelta
        date_to_use = (datetime.now() - timedelta(days=1)).strftime("%Y-%m-%d")

        df_opps = pl.read_csv(stats_file)
        df_filtered = df_opps.filter(pl.col('opportunity_cycles_040bp') > 50)

        df_sorted = df_filtered.sort(['symbol', 'exchange1'])
        opportunities = df_sorted.select([
            pl.col('symbol'), pl.col('exchange1'), pl.col('exchange2')
        ]).to_dicts()

        tasks = [load_and_process_pair(opp, date_to_use) for opp in opportunities]
        # Process in chunks to avoid memory exhaustion
        results = await process_in_chunks(tasks, 4)

        # Filter out None results where data was missing or processing failed
        valid_results = [res for res in results if res is not None]

        return {"charts_data": valid_results}

    except Exception as e:
        raise HTTPException(status_code=500, detail=f"An internal server error occurred: {str(e)}")
=======
@app.get("/api/dashboard_data")
async def get_dashboard_data():
    logging.info("Received request for /api/dashboard_data")
    try:
        # Find the latest summary stats file
        analyzer_stats_dir = os.path.join(os.path.dirname(__file__), '..', 'analyzer', 'summary_stats')
        if not os.path.exists(analyzer_stats_dir):
            logging.error("Analyzer summary_stats directory not found.")
            raise HTTPException(status_code=404, detail="Analyzer summary_stats directory not found.")

        csv_files = [f for f in os.listdir(analyzer_stats_dir) if f.endswith('.csv')]
        if not csv_files:
            logging.error("No summary stats CSV files found.")
            raise HTTPException(status_code=404, detail="No summary stats CSV files found.")

        latest_file = max(csv_files, key=lambda f: os.path.getmtime(os.path.join(analyzer_stats_dir, f)))
        stats_file = os.path.join(analyzer_stats_dir, latest_file)
        logging.info(f"Using stats file: {stats_file}")

        from datetime import timedelta
        date_to_use = (datetime.now() - timedelta(days=1)).strftime("%Y-%m-%d")

        df_opps = pl.read_csv(stats_file)
        df_filtered = df_opps.filter(pl.col('opportunity_cycles_040bp') > 50)
        opportunities = df_filtered.sort(['symbol', 'exchange1']).select(
            ['symbol', 'exchange1', 'exchange2']
        ).to_dicts()
        logging.info(f"Found {len(opportunities)} opportunities to process.")

        tasks = [load_and_process_pair(opp, date_to_use) for opp in opportunities]
        
        logging.info("Starting to process tasks in chunks.")
        results = await process_in_chunks(tasks, 4)
        logging.info("Finished processing tasks.")

        valid_results = [res for res in results if res is not None]
        logging.info(f"Successfully processed {len(valid_results)} pairs.")

        response_data = {"charts_data": valid_results}
        logging.info("Successfully created response data. Sending to client.")
        return response_data

    except Exception as e:
        logging.error(f"An unhandled exception occurred in get_dashboard_data: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"An internal server error occurred: {str(e)}")
>>>>>>> REPLACE
```

## Обоснование
Детальное логирование позволит нам увидеть, на каком именно шаге обработки (чтение файла, обработка пары, формирование ответа) происходит сбой. Это самый надежный способ диагностировать коренную причину проблемы, будь то ошибка в данных, нехватка памяти или другая непредвиденная ситуация.

## Оценка рисков
Риск минимален. Добавление логирования незначительно повлияет на производительность, но является критически важным для отладки.

## План тестирования
1.  Перезапустить сервер `charts`: `uvicorn charts.server:app --reload`.
2.  Воспроизвести ошибку, запросив большое количество пар.
3.  Проанализировать вывод в консоли сервера. Последнее сообщение в логе перед сбоем укажет на проблемный участок кода.

## План отката
1.  Отменить изменения в файле `charts/server.py` с помощью `git restore charts/server.py`.