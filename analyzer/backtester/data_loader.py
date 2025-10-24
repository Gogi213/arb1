#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Loads and processes historical market data for backtesting using Polars.
This version is optimized to read partitioned Parquet datasets efficiently.
"""

import os
import glob
import polars as pl
import logging
from datetime import datetime, date
from typing import Optional, List

def get_date_range(data_path: str, start_date: Optional[datetime], end_date: Optional[datetime], logger: logging.Logger) -> List[str]:
    """
    Determines the range of date strings to process by scanning the filesystem.
    """
    logger.info("Discovering available dates from filesystem using glob...")
    available_dates = set()
    try:
        # Pattern matches exchange=*/symbol=*/date=*
        pattern = os.path.join(data_path, "*", "*", "date=*")
        date_dirs = glob.glob(pattern)

        for path in date_dirs:
            try:
                date_str = os.path.basename(path).split('=')[1]
                datetime.strptime(date_str, "%Y-%m-%d")  # Validate format
                available_dates.add(date_str)
            except (ValueError, IndexError):
                continue
    except Exception as e:
        logger.error(f"Failed to discover dates from filesystem: {e}", exc_info=True)
        return []

    if not available_dates:
        logger.warning("No dates found in the filesystem with the expected partition structure.")
        return []

    s_date_str = start_date.strftime("%Y-%m-%d") if start_date else min(available_dates)
    e_date_str = end_date.strftime("%Y-%m-%d") if end_date else max(available_dates)
    
    return sorted([d for d in available_dates if s_date_str <= d <= e_date_str])

def load_and_process_daily_data(
    data_path: str,
    target_date: str,
    exchanges: Optional[List[str]],
    symbols: Optional[List[str]],
    logger: logging.Logger
) -> pl.DataFrame:
    """
    Loads and processes data for a single day by reading only the specific date partition.
    """
    logger.info(f"Processing data for date: {target_date}")

    # Build partition paths for the specific date
    # Pattern: data_path/exchange=*/symbol=*/date=target_date/hour=*/*.parquet
    partition_pattern = os.path.join(data_path, "*", "*", f"date={target_date}", "*", "*.parquet")
    files = glob.glob(partition_pattern)

    if not files:
        logger.warning(f"No files found for date: {target_date}")
        return pl.DataFrame()

    # Filter files by exchange and symbol if specified
    if exchanges or symbols:
        filtered_files = []
        for file_path in files:
            # Parse exchange and symbol from path: .../exchange=XXX/symbol=YYY/date=.../file.parquet
            parts = file_path.split(os.sep)
            try:
                exchange_part = [p for p in parts if p.startswith("exchange=")][0]
                symbol_part = [p for p in parts if p.startswith("symbol=")][0]

                file_exchange = exchange_part.split("=")[1]
                file_symbol = symbol_part.split("=")[1]

                if exchanges and file_exchange not in exchanges:
                    continue
                if symbols and file_symbol not in symbols:
                    continue

                filtered_files.append(file_path)
            except (IndexError, ValueError):
                continue
        files = filtered_files

    if not files:
        logger.warning(f"No matching files found for date: {target_date} with specified filters")
        return pl.DataFrame()

    logger.info(f"Loading {len(files)} parquet files for {target_date}")

    # Read files directly using scan_parquet with hive partitioning
    try:
        lazy_df = pl.scan_parquet(files, hive_partitioning=True).rename({
            "Timestamp": "timestamp",
            "BestBid": "bestBid",
            "BestAsk": "bestAsk",
            "Exchange": "exchange_from_file",
            "Symbol": "symbol_from_file"
        }).drop(["exchange_from_file", "symbol_from_file"])

        # Cast numeric columns, handle date column properly (it may come as string or date type)
        processed_df = lazy_df.collect().with_columns([
            pl.col("bestBid").cast(pl.Float64),
            pl.col("bestAsk").cast(pl.Float64),
        ])

        # Convert date column to proper date type if it's a string
        if processed_df["date"].dtype == pl.String or processed_df["date"].dtype == pl.Utf8:
            processed_df = processed_df.with_columns([
                pl.col("date").str.to_date("%Y-%m-%d")
            ])

        processed_df = processed_df.sort("timestamp")

        logger.info(f"Loaded {len(processed_df)} records for {target_date}")
        return processed_df
    except Exception as e:
        logger.error(f"Error loading data for {target_date}: {e}", exc_info=True)
        return pl.DataFrame()

def stream_market_data(
    data_path: str,
    start_date: Optional[datetime],
    end_date: Optional[datetime],
    exchanges: Optional[List[str]],
    symbols: Optional[List[str]],
    logger: Optional[logging.Logger] = None
):
    """
    Streams market data from a partitioned Parquet dataset day by day.
    Optimized to read only needed date partitions directly.
    """
    if logger is None:
        logger = logging.getLogger(__name__)

    logger.info(f"Initializing data stream from: {data_path}")

    if not os.path.isdir(data_path):
        logger.error(f"Data path does not exist or is not a directory: {data_path}")
        return

    try:
        date_range = get_date_range(data_path, start_date, end_date, logger)
        if not date_range:
            logger.warning("Date range is empty. No data to process.")
            return

        logger.info(f"Processing data for {len(date_range)} days from {min(date_range)} to {max(date_range)}.")

        for target_date in date_range:
            daily_df = load_and_process_daily_data(data_path, target_date, exchanges, symbols, logger)
            if not daily_df.is_empty():
                yield daily_df

    except Exception as e:
        logger.error(f"An error occurred while streaming data: {e}", exc_info=True)
