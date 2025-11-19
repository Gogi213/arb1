"""
Analyzer library for cryptocurrency arbitrage ratio analysis.

This package provides modular components for:
- Loading market data from parquet files
- Analyzing price ratio deviations between exchanges
- Discovering trading opportunities based on mean-reversion patterns
"""

__version__ = "1.0.0"

from .config import AnalyzerConfig, load_config
from .data_loader import load_exchange_symbol_data
from .analysis import analyze_pair_fast
from .discovery import discover_data

__all__ = [
    'AnalyzerConfig',
    'load_config',
    'load_exchange_symbol_data',
    'analyze_pair_fast',
    'discover_data'
]
