#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Loads and processes historical market data for backtesting using Polars.
"""

import os
import polars as pl
from typing import List
from .logger import log

def load_market_data(session_path: str) -> pl.DataFrame:
    """
    Loads all market data from a given session directory using Polars,
    processes it, and returns a single sorted DataFrame.

    Args:
        session_path: The path to the session directory.

    Returns:
        A Polars DataFrame containing all market data, sorted by timestamp.
    """
    all_files = []
    log.info(f"Scanning for data files in session: {session_path}")

    if not os.path.isdir(session_path):
        log.error(f"Session path does not exist or is not a directory: {session_path}")
        return pl.DataFrame()

    allowed_exchanges = ['Binance', 'Bybit', 'GateIo', 'Kucoin']
    log.info(f"Limiting data load to specified exchanges: {allowed_exchanges}")

    for exchange_folder in os.listdir(session_path):
        if exchange_folder not in allowed_exchanges:
            continue
            
        exchange_path = os.path.join(session_path, exchange_folder)
        if not os.path.isdir(exchange_path):
            continue

        for symbol_folder in os.listdir(exchange_path):
            symbol_path = os.path.join(exchange_path, symbol_folder)
            data_file = os.path.join(symbol_path, 'order_book_updates.csv.gz')

            if os.path.exists(data_file):
                all_files.append({
                    "path": data_file,
                    "exchange": exchange_folder,
                    "symbol": symbol_folder
                })

    if not all_files:
        log.warning("No data files found in the session directory.")
        return pl.DataFrame()

    log.info(f"Found {len(all_files)} files. Starting parallel data load with Polars...")

    # Use Polars' lazy evaluation for efficient processing
    lazy_frames = []
    lazy_frames = []
    for file_info in all_files:
        try:
            schema_overrides = {
                "BestAsk": pl.Float64,
                "BestBid": pl.Float64,
                "MinVolume": pl.Float64,
                "MaxVolume": pl.Float64,
            }
            lf = pl.scan_csv(
                file_info["path"],
                infer_schema_length=1000,
                truncate_ragged_lines=True,
                schema_overrides=schema_overrides
            )
            lf = lf.with_columns([
                pl.lit(file_info["exchange"]).alias("exchange"),
                pl.lit(file_info["symbol"]).alias("symbol")
            ])
            lazy_frames.append(lf)
        except Exception as e:
            log.error(f"Polars failed to scan {file_info['path']}: {e}")

    if not lazy_frames:
        log.error("Failed to create any lazy frames. Check file integrity.")
        return pl.DataFrame()

    full_df = pl.concat(lazy_frames).collect()

    # Normalize symbol names (e.g., 'ETH-USDT' -> 'ETHUSDT')
    full_df = full_df.with_columns(
        pl.col("symbol").str.replace_all("[-_]", "").alias("symbol")
    )

    # Rename and convert timestamp column
    if 'Timestamp' in full_df.columns:
        full_df = full_df.rename({"Timestamp": "timestamp", "BestBid": "bestBid", "BestAsk": "bestAsk"})
        full_df = full_df.with_columns(
            pl.col("timestamp").str.to_datetime()
        ).sort("timestamp")
    else:
        log.warning("No 'Timestamp' column found. Data will not be time-sorted.")

    log.info(f"Data loading complete. Total records: {len(full_df)}")
    return full_df
