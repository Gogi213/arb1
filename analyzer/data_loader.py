#!/usr/bin/env python3
"""
Streaming data loader for market data.
Loads data in chunks, not all at once.
"""

import polars as pl
from pathlib import Path
from datetime import datetime
from typing import Generator

def stream_exchange_data(
    data_path: str,
    exchange: str,
    symbol: str,
    start_date: datetime = None,
    end_date: datetime = None
) -> Generator[pl.DataFrame, None, None]:
    """
    Yields daily chunks of data for one exchange.
    Correctly handles hour-based partitioning.
    """
    base_path = Path(data_path)
    exchange_path = base_path / f"exchange={exchange}"

    if not exchange_path.exists():
        print(f"Warning: No data for exchange {exchange}")
        return

    # Actual structure: exchange=X/symbol=Y/date=Z/hour=H
    symbol_path_str = symbol.replace('/', '#')
    symbol_path = exchange_path / f"symbol={symbol_path_str}"

    if not symbol_path.exists():
        print(f"Warning: No symbol path {symbol_path}")
        return

    for date_dir in sorted(symbol_path.glob("date=*")):
        try:
            date_str = date_dir.name.split("=")[1]
            date = datetime.strptime(date_str, "%Y-%m-%d")
        except (IndexError, ValueError):
            continue

        if start_date and date.date() < start_date.date():
            continue
        if end_date and date.date() > end_date.date():
            break

        glob_pattern = str(date_dir / "hour=*" / "*.parquet")
        
        try:
            df = pl.scan_parquet(glob_pattern).collect()
        except pl.exceptions.ComputeError:
            continue

        if not df.is_empty():
            yield df.select(['Timestamp', 'BestBid', 'BestAsk']).rename({
                'Timestamp': 'timestamp',
                'BestBid': 'bestBid',
                'BestAsk': 'bestAsk'
            }).sort('timestamp')

def stream_synced_data(
    data_path: str,
    symbol: str,
    exchange1: str,
    exchange2: str,
    start_date: datetime = None,
    end_date: datetime = None
) -> Generator[pl.DataFrame, None, None]:
    """
    Yields synchronized data between two exchanges using a reliable dictionary cache.
    """
    # Cache all data from the second exchange by date
    data2_by_date = {}
    for df2 in stream_exchange_data(data_path, exchange2, symbol, start_date, end_date):
        if not df2.is_empty():
            day = df2['timestamp'][0].date()
            if day not in data2_by_date:
                data2_by_date[day] = []
            data2_by_date[day].append(df2)

    # Consolidate chunks for each day in the cache
    for day, chunks in data2_by_date.items():
        data2_by_date[day] = pl.concat(chunks)

    # Stream data from the first exchange and join with cached data
    gen1 = stream_exchange_data(data_path, exchange1, symbol, start_date, end_date)
    for df1 in gen1:
        if df1.is_empty():
            continue

        day1 = df1['timestamp'][0].date()

        if day1 in data2_by_date:
            df2_full_day = data2_by_date[day1]
            
            joined = df1.rename({
                'bestBid': 'bid_ex1',
                'bestAsk': 'ask_ex1'
            }).join_asof(
                df2_full_day.rename({
                    'bestBid': 'bid_ex2',
                    'bestAsk': 'ask_ex2'
                }),
                on='timestamp'
            )

            joined = joined.filter(
                pl.col('bid_ex1').is_not_null() &
                pl.col('bid_ex2').is_not_null()
            )

            if not joined.is_empty():
                yield joined