"""Type definitions for the arbitrage finder."""
from typing import TypedDict


class MarketData(TypedDict):
    """Market data for a single exchange-symbol pair."""
    exchange: str
    symbol: str
    bid: float
    ask: float
    spread_pct: float
    min_volume: float
    max_volume: float
    timestamp: float


class ArbitrageOpportunity(TypedDict):
    """Detected arbitrage opportunity."""
    symbol: str
    buy_exchange: str
    sell_exchange: str
    buy_price: float  # bid on buy exchange
    sell_price: float  # bid on sell exchange
    profit_pct: float
    buy_spread_pct: float
    sell_spread_pct: float
    timestamp: float
