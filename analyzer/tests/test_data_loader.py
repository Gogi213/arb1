"""
Unit tests for data_loader module.
"""

import unittest
import tempfile
import polars as pl
from pathlib import Path
from lib.data_loader import load_exchange_symbol_data


class TestDataLoader(unittest.TestCase):
    """Tests for data loading functionality."""

    def setUp(self):
        """Create temporary data structure for testing"""
        self.temp_dir = tempfile.mkdtemp()
        self.data_path = Path(self.temp_dir)

        # Create mock data structure
        # data/exchange=TestExchange/symbol=BTC#USDT/date=2025-01-01/hour=00/
        exchange_dir = self.data_path / "exchange=TestExchange"
        symbol_dir = exchange_dir / "symbol=BTC#USDT"
        date_dir = symbol_dir / "date=2025-01-01"
        hour_dir = date_dir / "hour=00"

        hour_dir.mkdir(parents=True, exist_ok=True)

        # Create mock parquet file
        mock_data = pl.DataFrame({
            'Timestamp': pl.datetime_range(
                start=pl.datetime(2025, 1, 1, 0, 0, 0),
                end=pl.datetime(2025, 1, 1, 0, 10, 0),
                interval="1m",
                eager=True
            ),
            'BestBid': [100.0] * 11,
            'BestAsk': [100.1] * 11
        })

        parquet_file = hour_dir / "data.parquet"
        mock_data.write_parquet(parquet_file)

    def tearDown(self):
        """Clean up temporary directory"""
        import shutil
        shutil.rmtree(self.temp_dir)

    def test_load_existing_data(self):
        """Test loading data that exists"""
        df = load_exchange_symbol_data(
            str(self.data_path),
            "TestExchange",
            "BTC/USDT"
        )

        self.assertIsNotNone(df, "Should load existing data")
        self.assertIsInstance(df, pl.DataFrame)
        self.assertIn('timestamp', df.columns)
        self.assertIn('bestBid', df.columns)
        self.assertIn('bestAsk', df.columns)
        self.assertEqual(len(df), 11, "Should have 11 data points")

    def test_load_nonexistent_exchange(self):
        """Test loading data for non-existent exchange"""
        df = load_exchange_symbol_data(
            str(self.data_path),
            "NonExistentExchange",
            "BTC/USDT"
        )

        self.assertIsNone(df, "Should return None for non-existent exchange")

    def test_load_nonexistent_symbol(self):
        """Test loading data for non-existent symbol"""
        df = load_exchange_symbol_data(
            str(self.data_path),
            "TestExchange",
            "ETH/USDT"
        )

        self.assertIsNone(df, "Should return None for non-existent symbol")

    def test_date_filtering(self):
        """Test date range filtering"""
        # Should find data for 2025-01-01
        df = load_exchange_symbol_data(
            str(self.data_path),
            "TestExchange",
            "BTC/USDT",
            start_date="2025-01-01",
            end_date="2025-01-01"
        )

        self.assertIsNotNone(df, "Should find data for specified date")

        # Should not find data for different date
        df = load_exchange_symbol_data(
            str(self.data_path),
            "TestExchange",
            "BTC/USDT",
            start_date="2025-01-02",
            end_date="2025-01-02"
        )

        self.assertIsNone(df, "Should not find data for different date")

    def test_data_types(self):
        """Test that data is loaded with correct types"""
        df = load_exchange_symbol_data(
            str(self.data_path),
            "TestExchange",
            "BTC/USDT"
        )

        self.assertEqual(
            df['bestBid'].dtype,
            pl.Float64,
            "bestBid should be Float64"
        )
        self.assertEqual(
            df['bestAsk'].dtype,
            pl.Float64,
            "bestAsk should be Float64"
        )

    def test_sorted_by_timestamp(self):
        """Test that data is sorted by timestamp"""
        df = load_exchange_symbol_data(
            str(self.data_path),
            "TestExchange",
            "BTC/USDT"
        )

        timestamps = df['timestamp'].to_list()
        self.assertEqual(
            timestamps,
            sorted(timestamps),
            "Data should be sorted by timestamp"
        )


if __name__ == '__main__':
    unittest.main()
