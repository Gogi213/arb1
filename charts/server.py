# To run this server:
# 1. Install dependencies: pip install fastapi uvicorn polars python-multipart
# 2. Run from the project root ('c:/visual projects/arb1'): uvicorn charts.server:app --reload

import os
import polars as pl
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from datetime import datetime
import asyncio

app = FastAPI()

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

DATA_LAKE_PATH = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'data', 'market_data'))

def clean_price_polars(col: pl.Series) -> pl.Series:
    # This function handles multiple types by trying conversions
    # It's more robust than checking instance types row by row
    return col.cast(pl.Utf8, strict=False).str.replace_all(r'"', '').cast(pl.Float64, strict=False)

async def load_and_process_pair(opportunity: dict, date: str):
    symbol = opportunity['symbol']
    exchange_a = opportunity['exchange1']
    exchange_b = opportunity['exchange2']

    async def load_data(exchange):
        date_path = os.path.join(DATA_LAKE_PATH, f"exchange={exchange}", f"symbol={symbol}", f"date={date}")
        if not os.path.exists(date_path):
            return None
        
        files = [os.path.join(hour_path, f) 
                 for hour in range(24) 
                 for hour_path in [os.path.join(date_path, f"hour={str(hour).zfill(2)}")] if os.path.exists(hour_path)
                 for f in os.listdir(hour_path) if f.endswith('.parquet')]

        if not files:
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
        return None

    # Clean and sort
    df_a = df_a.with_columns(clean_price_polars(pl.col("BestBid")).alias("bid_a")).sort("timestamp")
    df_b = df_b.with_columns(clean_price_polars(pl.col("BestBid")).alias("bid_b")).sort("timestamp")

    # Polars asof_join is extremely fast
    # Используем 'backward' для предотвращения "заглядывания в будущее".
    # Добавляем 'tolerance', чтобы не использовать слишком старые данные.
    merged_df = df_a.join_asof(df_b, on="timestamp", strategy="backward", tolerance="2s")
    if merged_df.height == 0:
        return None

    # Calculate spread
    result_df = merged_df.with_columns(
        (((pl.col('bid_a') / pl.col('bid_b')) - 1) * 100).alias('spread')
    )

    # Фильтруем строки, где спред не мог быть вычислен (из-за отсутствия данных от df_b)
    result_df = result_df.filter(pl.col('spread').is_not_null())
    
    # Convert to epoch seconds for uPlot
    timestamps = (result_df.get_column("timestamp").dt.epoch(time_unit="ms") / 1000).to_list()
    spreads = result_df.get_column("spread").to_list()

    return {
        "symbol": symbol,
        "exchange1": exchange_a,
        "exchange2": exchange_b,
        "timestamps": timestamps,
        "spreads": spreads
    }


@app.get("/api/dashboard_data")
async def get_dashboard_data():
    stats_file = os.path.join(os.path.dirname(__file__), 'summary_stats_20251104_184912.csv')
    if not os.path.exists(stats_file):
        raise HTTPException(status_code=404, detail="Summary stats file not found.")

    try:
        date_today = datetime.now().strftime("%Y-%m-%d")
        
        df_opps = pl.read_csv(stats_file)
        df_filtered = df_opps.filter(pl.col('opportunity_cycles_040bp') > 100)
        
        df_sorted = df_filtered.sort(['symbol', 'exchange1'])
        opportunities = df_sorted.select([
            pl.col('symbol'), pl.col('exchange1'), pl.col('exchange2')
        ]).to_dicts()

        tasks = [load_and_process_pair(opp, date_today) for opp in opportunities]
        results = await asyncio.gather(*tasks)
        
        # Filter out None results where data was missing or processing failed
        valid_results = [res for res in results if res is not None]
        
        return {"charts_data": valid_results}

    except Exception as e:
        raise HTTPException(status_code=500, detail=f"An internal server error occurred: {str(e)}")