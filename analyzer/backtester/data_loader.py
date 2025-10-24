#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Loads and processes historical market data for backtesting using Polars.
This version is optimized to read partitioned Parquet datasets efficiently.
"""

import os
import polars as pl
import logging
import pyarrow.dataset as ds
from datetime import datetime
from typing import Optional, List

def load_market_data(
    data_path: str,
    start_date: Optional[datetime],
    end_date: Optional[datetime],
    exchanges: Optional[List[str]],
    symbols: Optional[List[str]],
    logger: Optional[logging.Logger] = None
) -> pl.DataFrame:
    """
    Loads market data from a partitioned Parquet dataset using Polars,
    with optional filtering.
    """
    if logger is None:
        logger = logging.getLogger(__name__)

    logger.info(f"Loading data from: {data_path}")

    if not os.path.isdir(data_path):
        logger.error(f"Data path does not exist or is not a directory: {data_path}")
        return pl.DataFrame()

    try:
        # Start building a lazy query
        dataset = ds.dataset(source=data_path, format="parquet", partitioning=["exchange", "symbol", "date", "hour"])
        
        # --- Enhanced Diagnostic Logging ---
        file_list = dataset.files
        logger.info(f"Found {len(file_list)} total Parquet files across all partitions.")
        
        if file_list:
            # Get the range of partitions found to prove we are seeing everything.
            try:
                dates_found = dataset.partitioning.schema.field("date").dictionary.to_pylist()
                hours_found = dataset.partitioning.schema.field("hour").dictionary.to_pylist()
                if dates_found:
                    logger.info(f"Data covers date range: {min(dates_found)} to {max(dates_found)}.")
                if hours_found:
                     logger.info(f"Found data for {len(hours_found)} unique hours.")
            except Exception as e:
                logger.warning(f"Could not extract full partition details for logging: {e}")
        # ------------------------------------

        lazy_df = pl.scan_pyarrow_dataset(dataset, allow_pyarrow_filter=True)

        # Dynamically build filters
        filters = []
        if exchanges:
            filters.append(pl.col("exchange").is_in(exchanges))
            logger.info(f"Filtering for exchanges: {exchanges}")
        if symbols:
            filters.append(pl.col("symbol").is_in(symbols))
            logger.info(f"Filtering for symbols: {symbols}")
        if start_date and end_date:
            filters.append(pl.col("date").is_between(start_date.date(), end_date.date()))
            logger.info(f"Filtering for date range: {start_date.date()} to {end_date.date()}")
        elif start_date:
            filters.append(pl.col("date") >= start_date.date())
            logger.info(f"Filtering for dates from: {start_date.date()}")
        elif end_date:
            filters.append(pl.col("date") <= end_date.date())
            logger.info(f"Filtering for dates up to: {end_date.date()}")

        # Apply all filters at once
        if filters:
            lazy_df = lazy_df.filter(pl.all_horizontal(filters))

        # Now, execute the query and perform final processing
        logger.info("Collecting data based on filters...")

        # Drop columns from the file that are already present from partitioning
        # The column names from partitioning are lowercase by default.
        columns_to_drop = ['exchange', 'symbol', 'date', 'hour']
        
        # The actual columns from the parquet file might have different casing
        # We identify them by lowercasing and comparing.
        
        # Rename columns first to standardize them
        renamed_lazy_df = lazy_df.rename({
            "Timestamp": "timestamp",
            "BestBid": "bestBid",
            "BestAsk": "bestAsk",
            "Exchange": "exchange_from_file", # temp rename to avoid conflict
            "Symbol": "symbol_from_file"    # temp rename
        })

        # Now drop the redundant columns that came from the file
        processed_df = renamed_lazy_df.drop(["exchange_from_file", "symbol_from_file"]).collect().with_columns([
            pl.col("bestBid").cast(pl.Float64),
            pl.col("bestAsk").cast(pl.Float64),
        ]).sort("timestamp")

        if processed_df.is_empty():
            logger.warning("No data found for the specified criteria.")
            return pl.DataFrame()

        logger.info(f"Data loading complete. Total records: {len(processed_df)}")
        return processed_df

    except Exception as e:
        logger.error(f"An error occurred while loading data: {e}", exc_info=True)
        return pl.DataFrame()
