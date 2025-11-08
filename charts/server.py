# To run this server:
# 1. Install dependencies: pip install fastapi uvicorn polars python-multipart websockets
# 2. Run from the project root ('c:/visual projects/arb1'): uvicorn charts.server:app --port 8002 --reload

import os
import polars as pl
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from datetime import datetime, timedelta, timezone
import psutil
import sys
import asyncio
import logging
import json
import websockets
from fastapi.responses import StreamingResponse
from fastapi import WebSocket, WebSocketDisconnect, Request
from typing import Dict, List, Optional, AsyncGenerator
from contextlib import asynccontextmanager
from dataclasses import dataclass
from threading import Timer
import threading

# Configure logging
logging.basicConfig(level=logging.DEBUG, format='%(asctime)s - %(levelname)s - %(message)s')
logging.getLogger("websockets").setLevel(logging.INFO)

# Data classes
@dataclass
class SpreadData:
    exchange: str
    symbol: str
    timestamp: datetime
    best_bid: float
    best_ask: float
    spread_percentage: float
    min_volume: float
    max_volume: float

@dataclass
class TradeData:
    exchange: str
    symbol: str
    timestamp: datetime
    price: float
    quantity: float
    side: str

@dataclass
class RollingWindowData:
    exchange: str
    symbol: str
    window_start: datetime
    window_end: datetime
    spreads: List[SpreadData]
    trades: List[TradeData]

class RollingWindow:
    def __init__(self):
        self.window_size = timedelta(hours=1)
        self.windows: Dict[str, RollingWindowData] = {}
        self.cleanup_timer: Optional[Timer] = None

    def start_cleanup(self):
        """Starts the periodic cleanup timer."""
        if self.cleanup_timer:
            self.cleanup_timer.cancel()
        self.cleanup_timer = Timer(60.0, self._cleanup_old_data)
        self.cleanup_timer.start()
        logging.info("Rolling window cleanup timer started.")

    def stop_cleanup(self):
        """Stops the cleanup timer."""
        if self.cleanup_timer:
            self.cleanup_timer.cancel()
            logging.info("Rolling window cleanup timer stopped.")

    def _cleanup_old_data(self):
        now = datetime.now(timezone.utc)
        keys_to_remove = [
            key for key, window in self.windows.items()
            if window.window_end < now - self.window_size
        ]
        for key in keys_to_remove:
            del self.windows[key]
        # Restart the timer for the next interval
        self.start_cleanup()

    def process_spread_data(self, data: dict):
        key = f"{data['Exchange']}_{data['Symbol']}"
        now = datetime.now(timezone.utc)

        if key not in self.windows:
            self.windows[key] = RollingWindowData(
                exchange=data['Exchange'],
                symbol=data['Symbol'],
                window_start=now - self.window_size,
                window_end=now,
                spreads=[],
                trades=[]
            )

        window = self.windows[key]
        window.window_end = now

        # Remove old spreads
        window.spreads = [
            s for s in window.spreads
            if s.timestamp >= window.window_start
        ]

        # Add new spread
        spread = SpreadData(
            exchange=data['Exchange'],
            symbol=data['Symbol'],
            timestamp=datetime.fromisoformat(data['Timestamp'].replace('Z', '+00:00')),
            best_bid=data['BestBid'],
            best_ask=data['BestAsk'],
            spread_percentage=data['SpreadPercentage'],
            min_volume=data.get('MinVolume', 0),
            max_volume=data.get('MaxVolume', 0)
        )
        window.spreads.append(spread)

    def process_trade_data(self, data: dict):
        key = f"{data['Exchange']}_{data['Symbol']}"
        now = datetime.now(timezone.utc)

        if key not in self.windows:
            self.windows[key] = RollingWindowData(
                exchange=data['Exchange'],
                symbol=data['Symbol'],
                window_start=now - self.window_size,
                window_end=now,
                spreads=[],
                trades=[]
            )

        window = self.windows[key]
        window.window_end = now

        # Remove old trades
        window.trades = [
            t for t in window.trades
            if t.timestamp >= window.window_start
        ]

        # Add new trade
        trade = TradeData(
            exchange=data['Exchange'],
            symbol=data['Symbol'],
            timestamp=datetime.fromisoformat(data['Timestamp'].replace('Z', '+00:00')),
            price=data['Price'],
            quantity=data['Quantity'],
            side=data['Side']
        )
        window.trades.append(trade)

    def get_window_data(self, exchange: str, symbol: str) -> Optional[RollingWindowData]:
        key = f"{exchange}_{symbol}"
        return self.windows.get(key)

    def get_all_windows(self) -> List[RollingWindowData]:
        return list(self.windows.values())

# Global rolling window instance
rolling_window = RollingWindow()

# WebSocket client for collections
ws_task = None

async def websocket_client():
    uri = "ws://127.0.0.1:8181"
    while True:
        try:
            # Disable keepalive pings to prevent timeouts with servers that don't respond to them.
            async with websockets.connect(uri, ping_interval=None, ping_timeout=None) as websocket:
                logging.info("Connected to collections WebSocket")
                async for message in websocket:
                    try:
                        data = json.loads(message)
                        message_type = data.get('MessageType')

                        if message_type == 'Spread':
                            rolling_window.process_spread_data(data['Payload'])
                        elif message_type == 'Trade':
                            rolling_window.process_trade_data(data['Payload'])
                        # Ignore AllSymbolInfo for now

                    except json.JSONDecodeError as e:
                        logging.error(f"Failed to parse WS message: {e}")
                    except Exception as e:
                        logging.error(f"Error processing WS message: {e}", exc_info=True)

        except (websockets.exceptions.ConnectionClosedError,
                websockets.exceptions.ConnectionClosedOK,
                ConnectionRefusedError) as e:
            logging.warning(f"WebSocket connection failed: {e}. Retrying in 5 seconds...")
            await asyncio.sleep(5)
        except Exception as e:
            logging.error(f"Unexpected WebSocket error: {e}. Retrying in 5 seconds...")
            await asyncio.sleep(5)

def start_websocket_client():
    global ws_task
    if ws_task is None or ws_task.done():
        ws_task = asyncio.create_task(websocket_client())
        logging.info("Started WebSocket client task")

@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncGenerator[None, None]:
    """Manages the application's lifespan events."""
    logging.info("Application startup...")
    
    # Start background services
    rolling_window.start_cleanup()
    
    if os.environ.get("DISABLE_WEBSOCKET_CLIENT") != "true":
        start_websocket_client()
        logging.info("WebSocket client enabled and started.")
    else:
        logging.info("WebSocket client is disabled for this run.")
        
    yield  # Application is running
    
    logging.info("Application shutdown...")
    # Stop background services
    rolling_window.stop_cleanup()
    # WebSocket task will be cancelled automatically on app exit

app = FastAPI(lifespan=lifespan)

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
        pl.col('spread').rolling_quantile(quantile=0.97, window_size=window_size, min_samples=1).alias('upper_band'),
        pl.col('spread').rolling_quantile(quantile=0.03, window_size=window_size, min_samples=1).alias('lower_band')
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

def _get_filtered_opportunities() -> List[Dict]:
    """
    Reads the latest summary stats CSV, filters for opportunities, and returns them.
    This logic is now centralized to be used by both historical and real-time endpoints.
    """
    analyzer_stats_dir = os.path.join(os.path.dirname(__file__), '..', 'analyzer', 'summary_stats')
    if not os.path.exists(analyzer_stats_dir):
        logging.error("Analyzer summary_stats directory not found.")
        return []

    csv_files = [f for f in os.listdir(analyzer_stats_dir) if f.endswith('.csv')]
    if not csv_files:
        logging.error("No summary stats CSV files found.")
        return []

    latest_file = max(csv_files, key=lambda f: os.path.getmtime(os.path.join(analyzer_stats_dir, f)))
    stats_file = os.path.join(analyzer_stats_dir, latest_file)
    # This log is too noisy for real-time updates, so it's removed.
    # logging.info(f"Using stats file: {stats_file}")

    df_opps = pl.read_csv(stats_file)
    df_filtered = df_opps.filter(pl.col('opportunity_cycles_040bp') > 40)
    return df_filtered.sort(['symbol', 'exchange1']).select(
        ['symbol', 'exchange1', 'exchange2']
    ).to_dicts()

@app.get("/api/dashboard_data")
async def get_dashboard_data():
    logging.info("Received request for /api/dashboard_data")
    try:
        opportunities = _get_filtered_opportunities()
        return StreamingResponse(chart_data_streamer(opportunities), media_type="application/x-ndjson")
    except Exception as e:
        logging.error(f"An unhandled exception occurred in get_dashboard_data: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"An internal server error occurred: {str(e)}")

def _join_realtime_windows(window_a: RollingWindowData, window_b: RollingWindowData):
    """Joins two real-time windows and calculates the spread, similar to historical data processing."""
    if not window_a.spreads or not window_b.spreads:
        return None

    # Convert to Polars DataFrames for efficient joining
    df_a = pl.DataFrame([
        {"timestamp": s.timestamp, "bid_a": s.best_bid} for s in window_a.spreads
    ]).sort("timestamp")

    df_b = pl.DataFrame([
        {"timestamp": s.timestamp, "bid_b": s.best_bid} for s in window_b.spreads
    ]).sort("timestamp")

    # Asof join to align timestamps
    merged_df = df_a.join_asof(df_b, on="timestamp", strategy="backward", tolerance="2s")
    if merged_df.height == 0:
        return None

    # Calculate spread
    result_df = merged_df.with_columns(
        (((pl.col('bid_a') / pl.col('bid_b')) - 1) * 100).alias('spread')
    ).filter(pl.col('spread').is_not_null())

    if result_df.height == 0:
        return None
        
    # Calculate rolling percentiles for bands
    window_size = 200 # Keep consistent with historical
    result_df = result_df.with_columns([
        pl.col('spread').rolling_quantile(quantile=0.97, window_size=window_size, min_samples=1).alias('upper_band'),
        pl.col('spread').rolling_quantile(quantile=0.03, window_size=window_size, min_samples=1).alias('lower_band')
    ])

    # Clean up non-serializable values
    result_df = result_df.with_columns([
        pl.when(pl.col(c).is_infinite()).then(None).otherwise(pl.col(c)).fill_nan(None).alias(c)
        for c in ['spread', 'upper_band', 'lower_band']
    ])

    # Convert to lists for JSON serialization
    timestamps = (result_df.get_column("timestamp").dt.epoch(time_unit="ms") / 1000).to_list()
    spreads = result_df.get_column("spread").to_list()
    upper_bands = result_df.get_column("upper_band").to_list()
    lower_bands = result_df.get_column("lower_band").to_list()

    return {
        "symbol": window_a.symbol,
        "exchange1": window_a.exchange,
        "exchange2": window_b.exchange,
        "timestamps": timestamps,
        "spreads": spreads,
        "upper_band": upper_bands,
        "lower_band": lower_bands
    }


async def _get_realtime_chart_data() -> List[Dict]:
    """
    Helper function to generate a list of chart data dictionaries for high-opportunity pairs.
    This is a non-streaming version of the logic.
    """
    global MOCK_OPPORTUNITIES
    chart_data_list = []
    
    # 1. Load opportunities from analyzer stats or mock data
    try:
        if MOCK_OPPORTUNITIES is not None:
            opportunities = MOCK_OPPORTUNITIES.to_dicts()
            logging.debug("Using mock opportunities for real-time data generation.")
        else:
            opportunities = _get_filtered_opportunities()
    except Exception as e:
        logging.error(f"Failed to load and filter opportunities for real-time stream: {e}")
        return []

    # 2. Process each opportunity
    for opp in opportunities:
        symbol = opp['symbol']
        exchange_a = opp['exchange1']
        exchange_b = opp['exchange2']

        key_a = f"{exchange_a}_{symbol}"
        key_b = f"{exchange_b}_{symbol}"

        window_a = rolling_window.windows.get(key_a)
        window_b = rolling_window.windows.get(key_b)

        if not window_a or not window_b:
            continue

        try:
            chart_data = _join_realtime_windows(window_a, window_b)
            if chart_data:
                chart_data_list.append(chart_data)
        except Exception as e:
            logging.error(f"Error joining real-time windows for {symbol} ({exchange_a}/{exchange_b}): {e}", exc_info=True)
            
    return chart_data_list

async def rolling_window_data_streamer():
    """
    Yields newline-delimited JSON strings for real-time charts of high-opportunity pairs.
    """
    chart_data_list = await _get_realtime_chart_data()
    logging.info(f"Streaming {len(chart_data_list)} real-time charts.")
    for chart_data in chart_data_list:
        yield json.dumps(chart_data) + '\n'

# Endpoint for testing purposes
# Store mock opportunities in a global variable for test access
MOCK_OPPORTUNITIES = None

@app.post("/api/test/load_mock_data")
async def load_mock_data(request: Request):
    """
    Endpoint for testing. Injects mock rolling window data AND mock opportunity data.
    """
    global MOCK_OPPORTUNITIES
    logging.info("TESTING: Loading mock data.")
    
    data = await request.json()
    
    # 1. Load mock opportunity data
    opp_data = data.get("opportunities")
    if opp_data:
        MOCK_OPPORTUNITIES = pl.DataFrame(opp_data)
        logging.info(f"TESTING: Loaded {len(MOCK_OPPORTUNITIES)} mock opportunities.")

    # 2. Load mock window data
    rolling_window.windows.clear()
    window_data = data.get("windows")
    if window_data:
        for item in window_data:
            spreads = [SpreadData(**s) for s in item['spreads']]
            # Timestamps are strings, convert them back
            for s in spreads:
                s.timestamp = datetime.fromisoformat(s.timestamp)
            
            window = RollingWindowData(
                exchange=item['exchange'],
                symbol=item['symbol'],
                window_start=datetime.fromisoformat(item['window_start']),
                window_end=datetime.fromisoformat(item['window_end']),
                spreads=spreads,
                trades=[]
            )
            key = f"{window.exchange}_{window.symbol}"
            rolling_window.windows[key] = window
        logging.info(f"TESTING: Loaded {len(rolling_window.windows)} mock windows.")

    return {"status": "mock data loaded"}

@app.get("/api/rolling_window_data")
async def get_rolling_window_data():
    logging.info("Received request for /api/rolling_window_data")
    return StreamingResponse(rolling_window_data_streamer(), media_type="application/x-ndjson")

@app.websocket("/ws/realtime_charts")
async def websocket_endpoint(websocket: WebSocket):
    await websocket.accept()
    logging.info("Real-time chart client connected.")
    try:
        while True:
            chart_data = await _get_realtime_chart_data()
            await websocket.send_json(chart_data)
            await asyncio.sleep(0.2)  # Update every 200ms
    except WebSocketDisconnect:
        logging.info("Real-time chart client disconnected.")
    except Exception as e:
        logging.error(f"Error in real-time chart websocket: {e}", exc_info=True)
