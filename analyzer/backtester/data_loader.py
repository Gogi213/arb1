#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Loads and processes historical market data for backtesting using Polars.
This version is adapted to read Parquet files with a new data structure.
"""

import os
import polars as pl
import logging
from typing import Optional

def load_market_data(session_path: str, logger: Optional[logging.Logger] = None) -> pl.DataFrame:
    """
    Loads all market data from a given session directory using Polars,
    processes it, and returns a single sorted DataFrame. This version
    reads Parquet files and handles the new data structure.

    Args:
        session_path: The path to the session directory containing .parquet files.
        logger: A configured logger instance.

    Returns:
        A Polars DataFrame containing all market data, sorted by timestamp.
    """
    if logger is None:
        logger = logging.getLogger(__name__)

    logger.info(f"Scanning for Parquet data files in session: {session_path}")

    if not os.path.isdir(session_path):
        logger.error(f"Session path does not exist or is not a directory: {session_path}")
        return pl.DataFrame()

    all_files = []
    for root, _, files in os.walk(session_path):
        for file in files:
            if file.endswith('.parquet'):
                all_files.append(os.path.join(root, file))

    if not all_files:
        logger.warning("No .parquet data files found recursively in the session directory.")
        return pl.DataFrame()

    # Filter out empty files that would cause a crash
    valid_files = [f for f in all_files if os.path.getsize(f) > 8]
    
    if not valid_files:
        logger.warning("All found .parquet files are empty or invalid.")
        return pl.DataFrame()

    skipped_count = len(all_files) - len(valid_files)
    if skipped_count > 0:
        logger.info(f"Skipping {skipped_count} empty or invalid .parquet files.")

    logger.info(f"Found {len(valid_files)} valid files. Reading files individually to catch errors...")

    dfs = []
    for file_path in valid_files:
        try:
            df = pl.read_parquet(file_path)
            dfs.append(df)
        except Exception as e:
            logger.warning(f"Skipping corrupted file: {file_path}. Error: {e}")

    if not dfs:
        logger.warning("No valid Parquet files could be read.")
        return pl.DataFrame()

    logger.info(f"Successfully read {len(dfs)} files. Concatenating and processing...")
    
    # Combine all dataframes and start processing
    lazy_df = pl.concat(dfs).lazy()

    numeric_cols = ["BestBid", "BestAsk", "MinVolume", "MaxVolume"]

    processed_lf = lazy_df.with_columns(
        *[pl.col(c).cast(pl.Float64, strict=False) for c in numeric_cols],
        pl.from_epoch(pl.col("Timestamp"), time_unit="ms").alias("timestamp"),
        pl.col("Symbol").str.replace_all("[-_]", "").alias("symbol")
    ).rename({
        "BestBid": "bestBid",
        "BestAsk": "bestAsk",
        "Exchange": "exchange"
    })

    allowed_exchanges = ['Binance', 'Bybit', 'GateIo', 'Kucoin', 'OKX']
    logger.info(f"Filtering data for specified exchanges: {allowed_exchanges}")
    
    filtered_lf = processed_lf.filter(pl.col("exchange").is_in(allowed_exchanges))

    logger.info("Collecting, sorting, and finalizing DataFrame...")
    try:
        full_df = filtered_lf.collect().sort("timestamp")
    except Exception as e:
        logger.error(f"A critical error occurred during final data processing: {e}")
        return pl.DataFrame()

    if full_df.is_empty():
        logger.warning("Data is empty after processing and filtering.")
        return pl.DataFrame()

    logger.info(f"Data loading complete. Total records: {len(full_df)}")
    return full_df
