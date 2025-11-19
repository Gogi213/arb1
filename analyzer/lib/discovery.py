"""
Data discovery utilities for finding available symbols and exchanges.

Scans the data directory and builds a map of symbols to exchanges.
"""

import os
from pathlib import Path
from collections import defaultdict
from typing import Dict, Set


def discover_data(data_path: str) -> Dict[str, Set[str]]:
    """
    Scan data directory and group symbols by exchanges.

    Args:
        data_path: Path to the market data directory

    Returns:
        Dictionary mapping symbol names to sets of exchange names.
        Only includes symbols that trade on 2 or more exchanges.

    Example:
        {
            'BTC/USDT': {'Binance', 'Bybit', 'OKX'},
            'ETH/USDT': {'Binance', 'Bybit'}
        }
    """
    print(f"--- Scanning for data in: {data_path} ---")
    symbol_map = defaultdict(set)

    if not Path(data_path).exists():
        print(f"ERROR: Data path does not exist: {data_path}")
        return {}

    for item in os.scandir(data_path):
        if item.is_dir() and item.name.startswith('exchange='):
            exchange_name = item.name.split('=')[1]
            exchange_path = Path(item.path)

            for symbol_item in os.scandir(exchange_path):
                if symbol_item.is_dir() and symbol_item.name.startswith('symbol='):
                    # Collections saves as "VIRTUAL_USDT", we convert back to "VIRTUAL/USDT"
                    raw_symbol = symbol_item.name.split('=')[1]
                    
                    # Convert SYMBOL_USDT -> SYMBOL/USDT for consistency
                    if '_USDT' in raw_symbol:
                        symbol_name = raw_symbol.replace('_USDT', '/USDT')
                    elif '_USDC' in raw_symbol:
                        symbol_name = raw_symbol.replace('_USDC', '/USDC')
                    else:
                        # Fallback: legacy format with # separator
                        symbol_name = raw_symbol.replace('#', '/')
                    
                    symbol_map[symbol_name].add(exchange_name)

    print("--- Discovery Complete ---")
    valid_symbols = {s: e for s, e in symbol_map.items() if len(e) >= 2}

    if not valid_symbols:
        print("No symbols found trading on 2 or more exchanges.")
    else:
        print(f"Found {len(valid_symbols)} symbols with potential pairs")

    return valid_symbols
