"""
Data loading utilities for market data.

Handles loading parquet files for exchange/symbol pairs with date filtering.
"""

from pathlib import Path
from typing import Optional
import polars as pl


def load_exchange_symbol_data(
    data_path: str,
    exchange: str,
    symbol: str,
    start_date: Optional[str] = None,
    end_date: Optional[str] = None
) -> Optional[pl.DataFrame]:
    """
    Load all data for (exchange, symbol) pair - OPTIMIZED with single scan.

    Args:
        data_path: Base path to market data
        exchange: Exchange name (e.g., "Binance", "Bybit")
        symbol: Symbol name (e.g., "BTC/USDT")
        start_date: Start date filter (YYYY-MM-DD format), inclusive. If None, no start filter.
        end_date: End date filter (YYYY-MM-DD format), inclusive. If None, no end filter.

    Returns:
        Polars DataFrame with columns: timestamp, bestBid, bestAsk
        Or None if no data found

    Notes:
        - Supports multiple symbol formats (with/without separators)
        - Filters null values early (filter pushdown optimization)
        - Casts decimals to Float64 for faster calculations
        - Single parquet scan for all files (2-4x faster I/O)
    """
    import os

    base_path = Path(data_path)
    exchange_path = base_path / f"exchange={exchange}"

    if not exchange_path.exists():
        return None

    # IMPORTANT: Collections saves as "SYMBOL_USDT" format (e.g., "VIRTUAL_USDT")
    # Try formats in order of likelihood:
    # 1. SYMBOL_USDT (Collections standard)
    # 2. SYMBOL#USDT (legacy format)
    # 3. SYMBOLUSDT (no separator)
    symbol_formats = [
        symbol.replace('/', '_'),  # VIRTUAL/USDT -> VIRTUAL_USDT (COLLECTIONS FORMAT)
        symbol.replace('/', '#'),  # VIRTUAL/USDT -> VIRTUAL#USDT (legacy)
        symbol.replace('/', '').replace('_', '')  # VIRTUAL/USDT -> VIRTUALUSDT (fallback)
    ]

    symbol_path = None
    for fmt in symbol_formats:
        candidate = exchange_path / f"symbol={fmt}"
        if candidate.exists():
            symbol_path = candidate
            break

    if symbol_path is None:
        return None

    # OPTIMIZATION #8: Single parquet scan for ALL dates (2-4x faster I/O)
    # Now supports date filtering with improved file collection
    if start_date or end_date:
        # Filter by date range during file collection
        available_dates = []
        for item in os.scandir(symbol_path):
            if item.is_dir() and item.name.startswith('date='):
                date_str = item.name.split('=')[1]
                if (not start_date or date_str >= start_date) and (not end_date or date_str <= end_date):
                    available_dates.append(date_str)

        if not available_dates:
            return None

        # Collect ALL parquet files for the filtered dates (single scan approach)
        all_files = []
        for date in available_dates:
            date_path = symbol_path / f"date={date}"
            if date_path.exists():
                for hour_dir in date_path.glob("hour=*"):
                    if hour_dir.is_dir():
                        all_files.extend(hour_dir.glob("*.parquet"))
    else:
        # Original behavior: collect all files
        all_files = []
        for date_dir in symbol_path.glob("date=*"):
            if date_dir.is_dir():
                for hour_dir in date_dir.glob("hour=*"):
                    if hour_dir.is_dir():
                        all_files.extend(hour_dir.glob("*.parquet"))

    if not all_files:
        return None

    # Single scan for ALL collected files (much faster than multiple scans)
    try:
        df = pl.scan_parquet(all_files) \
            .select(['Timestamp', 'BestBid', 'BestAsk']) \
            .rename({
                'Timestamp': 'timestamp',
                'BestBid': 'bestBid',
                'BestAsk': 'bestAsk'
            }) \
            .with_columns([
                pl.col('bestBid').cast(pl.Float64),
                pl.col('bestAsk').cast(pl.Float64)
            ]) \
            .filter(
                pl.col('bestBid').is_not_null() &
                pl.col('bestAsk').is_not_null()
            ) \
            .collect() \
            .sort('timestamp')

        return df if not df.is_empty() else None
    except Exception:
        return None
