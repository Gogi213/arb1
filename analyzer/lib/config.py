"""
Configuration management for analyzer.

Loads configuration from config.yaml file.
"""

from pathlib import Path
from typing import Optional, List
from dataclasses import dataclass
import yaml


@dataclass
class AnalyzerConfig:
    """Configuration for the analyzer."""

    # Paths
    data_directory: str
    output_directory: str

    # Analysis parameters
    zero_threshold: float
    thresholds: List[float]

    # Performance
    workers: Optional[int]
    chunk_size: int

    # Filters
    exchanges: Optional[List[str]]

    # Date range
    start_date: Optional[str]
    end_date: Optional[str]


def load_config(config_path: Optional[Path] = None) -> AnalyzerConfig:
    """
    Load configuration from YAML file.

    Args:
        config_path: Path to config.yaml. If None, uses default location.

    Returns:
        AnalyzerConfig instance

    Raises:
        FileNotFoundError: If config file doesn't exist
        ValueError: If config is invalid
    """
    if config_path is None:
        # Default: config.yaml in analyzer directory
        config_path = Path(__file__).parent.parent / "config.yaml"

    if not config_path.exists():
        raise FileNotFoundError(f"Config file not found: {config_path}")

    with open(config_path, 'r', encoding='utf-8') as f:
        config_data = yaml.safe_load(f)

    # Extract configuration sections
    paths = config_data.get('paths', {})
    analysis = config_data.get('analysis', {})
    performance = config_data.get('performance', {})
    date_range = config_data.get('date_range', {})

    return AnalyzerConfig(
        # Paths
        data_directory=paths.get('data_directory', 'C:/visual projects/arb1/data/market_data'),
        output_directory=paths.get('output_directory', 'C:/visual projects/arb1/analyzer/summary_stats'),

        # Analysis parameters
        zero_threshold=analysis.get('zero_threshold', 0.05),
        thresholds=analysis.get('thresholds', [0.3, 0.5, 0.4]),

        # Performance
        workers=performance.get('workers'),
        chunk_size=performance.get('chunk_size', 1),

        # Filters
        exchanges=config_data.get('exchanges'),

        # Date range
        start_date=date_range.get('start_date'),
        end_date=date_range.get('end_date')
    )


def get_default_config() -> AnalyzerConfig:
    """
    Get default configuration (without loading from file).

    Returns:
        AnalyzerConfig with default values
    """
    return AnalyzerConfig(
        data_directory='C:/visual projects/arb1/data/market_data',
        output_directory='C:/visual projects/arb1/analyzer/summary_stats',
        zero_threshold=0.05,
        thresholds=[0.3, 0.5, 0.4],
        workers=None,
        chunk_size=1,
        exchanges=None,
        start_date=None,
        end_date=None
    )
