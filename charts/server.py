# To run this server:
# 1. Install dependencies: pip install fastapi uvicorn polars python-multipart
# 2. Run from the project root ('c:/visual projects/arb1'): uvicorn charts.server:app --reload

import os
import polars as pl
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from datetime import datetime
import psutil
import sys
import asyncio
import logging
import json
from fastapi.responses import StreamingResponse

# Configure logging
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')

app = FastAPI()

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

DATA_LAKE_PATH = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'data', 'market_data'))
async def process_in_chunks(tasks, chunk_size: int):
    """Process a list of awaitable tasks in chunks."""
    results = []
    for i in range(0, len(tasks), chunk_size):
        chunk = tasks[i:i + chunk_size]
        results.extend(await asyncio.gather(*chunk))
    return results


def clean_price_polars(col: pl.Series) -> pl.Series:
    # This function handles multiple types by trying conversions
    # It's more robust than checking instance types row by row
    return col.cast(pl.Utf8, strict=False).str.replace_all(r'"', '').cast(pl.Float64, strict=False)

async def load_and_process_pair(opportunity: dict):
    symbol = opportunity['symbol']
    exchange_a = opportunity['exchange1']
    exchange_b = opportunity['exchange2']
    logging.info(f"Starting processing for pair: {symbol} ({exchange_a} vs {exchange_b}) for ALL available dates.")

    async def load_data(exchange):
        symbol_path_str = symbol.replace('/', '#')
        symbol_path = os.path.join(DATA_LAKE_PATH, f"exchange={exchange}", f"symbol={symbol_path_str}")
        if not os.path.exists(symbol_path):
            logging.warning(f"Symbol path not found for {exchange}/{symbol}")
            return None

        # Scan all date and hour directories for parquet files
        files = [os.path.join(root, f)
                 for root, _, filenames in os.walk(symbol_path)
                 for f in filenames if f.endswith('.parquet')]

        if not files:
            logging.warning(f"No parquet files found for {exchange}/{symbol} on {date}")
            return None

        # Use Polars for fast parallel reading
        df = pl.read_parquet(files)

        # Find timestamp and bid columns
        ts_col = next((col for col in df.columns if 'time' in col.lower()), None)
        bid_col = next((col for col in df.columns if 'bestbid' in col.lower()), 'BestBid')

        if not ts_col or not bid_col in df.columns:
            return None

        return df.select([
            pl.col(ts_col).alias('timestamp'),
            pl.col(bid_col)
        ])

    df_a, df_b = await asyncio.gather(load_data(exchange_a), load_data(exchange_b))

    if df_a is None or df_b is None:
        logging.warning(f"Missing data for one of the exchanges for pair: {symbol} ({exchange_a} vs {exchange_b})")
        return None

    # Clean and sort
    df_a = df_a.with_columns(clean_price_polars(pl.col("BestBid")).alias("bid_a")).sort("timestamp")
    df_b = df_b.with_columns(clean_price_polars(pl.col("BestBid")).alias("bid_b")).sort("timestamp")

    # Polars asof_join is extremely fast
    # Используем 'backward' для предотвращения "заглядывания в будущее".
    # Добавляем 'tolerance', чтобы не использовать слишком старые данные.
    merged_df = df_a.join_asof(df_b, on="timestamp", strategy="backward", tolerance="2s")
    if merged_df.height == 0:
        logging.warning(f"Asof join resulted in empty dataframe for pair: {symbol} ({exchange_a} vs {exchange_b})")
        return None

    # Calculate spread
    result_df = merged_df.with_columns(
        (((pl.col('bid_a') / pl.col('bid_b')) - 1) * 100).alias('spread')
    )

    # Фильтруем строки, где спред не мог быть вычислен
    result_df = result_df.filter(pl.col('spread').is_not_null())

    # Рассчитываем скользящие процентили
    window_size = 200
    result_df = result_df.with_columns([
        pl.col('spread').rolling_quantile(quantile=0.97, window_size=window_size, min_periods=1).alias('upper_band'),
        pl.col('spread').rolling_quantile(quantile=0.03, window_size=window_size, min_periods=1).alias('lower_band')
    ])

    # Заменяем невалидные числовые значения на null перед сериализацией
    result_df = result_df.with_columns([
        pl.when(pl.col('spread').is_infinite()).then(None).otherwise(pl.col('spread')).fill_nan(None).alias('spread'),
        pl.when(pl.col('upper_band').is_infinite()).then(None).otherwise(pl.col('upper_band')).fill_nan(None).alias('upper_band'),
        pl.when(pl.col('lower_band').is_infinite()).then(None).otherwise(pl.col('lower_band')).fill_nan(None).alias('lower_band')
    ])

    # Convert to epoch seconds for uPlot
    timestamps = (result_df.get_column("timestamp").dt.epoch(time_unit="ms") / 1000).to_list()
    spreads = result_df.get_column("spread").to_list()
    upper_bands = result_df.get_column("upper_band").to_list()
    lower_bands = result_df.get_column("lower_band").to_list()

    return {
        "symbol": symbol,
        "exchange1": exchange_a,
        "exchange2": exchange_b,
        "timestamps": timestamps,
        "spreads": spreads,
        "upper_band": upper_bands,
        "lower_band": lower_bands
    }


async def chart_data_streamer(opportunities):
    """Yields newline-delimited JSON strings for each chart."""
    logging.info(f"Streaming data for {len(opportunities)} opportunities.")
    processed_count = 0
    for opp in opportunities:
        try:
            chart_data = await load_and_process_pair(opp)
            if chart_data:
                yield json.dumps(chart_data) + '\n'
                processed_count += 1
        except Exception as e:
            logging.error(f"Error processing pair {opp.get('symbol')}: {e}", exc_info=True)
    logging.info(f"Finished streaming. Successfully sent {processed_count} chart objects.")

@app.get("/api/dashboard_data")
async def get_dashboard_data():
    logging.info("Received request for /api/dashboard_data")
    try:
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

        df_opps = pl.read_csv(stats_file)
        df_filtered = df_opps.filter(pl.col('opportunity_cycles_040bp') > 40)
        opportunities = df_filtered.sort(['symbol', 'exchange1']).select(
            ['symbol', 'exchange1', 'exchange2']
        ).to_dicts()
        
        return StreamingResponse(chart_data_streamer(opportunities), media_type="application/x-ndjson")

    except Exception as e:
        logging.error(f"An unhandled exception occurred in get_dashboard_data: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"An internal server error occurred: {str(e)}")

