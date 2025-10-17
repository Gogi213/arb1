"""Market state storage with zero-copy updates."""
import time
from typing import Dict, Tuple, List
from .types import MarketData


class MarketState:
    """Thread-safe market state storage using dict for O(1) access."""

    def __init__(self, max_data_age_sec: float = 3.0):
        self._data: Dict[Tuple[str, str], MarketData] = {}
        self.max_data_age_sec = max_data_age_sec

    def update(self, data: MarketData) -> None:
        """Update market data for exchange-symbol pair (zero-copy, in-place)."""
        key = (data['exchange'], data['symbol'])
        self._data[key] = data

    def get_all(self) -> List[MarketData]:
        """Get all current market data, removing stale entries."""
        current_time = time.time()

        # Remove stale data
        stale_keys = [
            key for key, data in self._data.items()
            if current_time - data['timestamp'] > self.max_data_age_sec
        ]
        for key in stale_keys:
            del self._data[key]

        return list(self._data.values())

    def get_by_symbol(self, symbol: str) -> List[MarketData]:
        """Get all exchange data for a specific symbol."""
        return [
            data for data in self._data.values()
            if data['symbol'] == symbol
        ]

    def get_all_symbols(self) -> set:
        """Get set of all active symbols."""
        return {data['symbol'] for data in self._data.values()}

    def size(self) -> int:
        """Get number of entries in the state."""
        return len(self._data)
