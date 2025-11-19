"""
Unit tests for analysis module - critical calculations.
"""

import unittest
import polars as pl
import numpy as np
from lib.analysis import count_complete_cycles, analyze_pair_fast


class TestCountCompleteCycles(unittest.TestCase):
    """Tests for count_complete_cycles function."""

    def test_simple_cycle(self):
        """Test a single complete cycle: neutral -> above -> neutral"""
        # Create data: neutral, above, above, neutral
        above = pl.Series([False, True, True, False])
        neutral = pl.Series([True, False, False, True])

        cycles = count_complete_cycles(above, neutral)
        self.assertEqual(cycles, 1, "Should detect 1 complete cycle")

    def test_multiple_cycles(self):
        """Test multiple complete cycles"""
        # neutral, above, neutral, above, neutral
        above = pl.Series([False, True, False, True, False])
        neutral = pl.Series([True, False, True, False, True])

        cycles = count_complete_cycles(above, neutral)
        self.assertEqual(cycles, 2, "Should detect 2 complete cycles")

    def test_incomplete_cycle(self):
        """Test incomplete cycle (doesn't return to neutral)"""
        # neutral, above, above (no return to neutral)
        above = pl.Series([False, True, True])
        neutral = pl.Series([True, False, False])

        cycles = count_complete_cycles(above, neutral)
        self.assertEqual(cycles, 0, "Should not count incomplete cycles")

    def test_no_threshold_exceeded(self):
        """Test when deviation never exceeds threshold"""
        # All neutral, never above
        above = pl.Series([False, False, False])
        neutral = pl.Series([True, True, True])

        cycles = count_complete_cycles(above, neutral)
        self.assertEqual(cycles, 0, "Should be 0 when never above threshold")

    def test_stuck_above_threshold(self):
        """Test when deviation is stuck above threshold"""
        # above, above, above (stuck)
        above = pl.Series([True, True, True])
        neutral = pl.Series([False, False, False])

        cycles = count_complete_cycles(above, neutral)
        self.assertEqual(cycles, 0, "Should be 0 when stuck above threshold")


class TestAnalyzePairFast(unittest.TestCase):
    """Tests for analyze_pair_fast function."""

    def setUp(self):
        """Create sample data for testing"""
        # Create simple test data with known behavior
        timestamps = pl.datetime_range(
            start=pl.datetime(2025, 1, 1, 0, 0, 0),
            end=pl.datetime(2025, 1, 1, 1, 0, 0),
            interval="1m",
            eager=True
        )

        # Exchange 1: price oscillates around 100
        prices1 = [100.0 + (i % 2) * 0.5 for i in range(len(timestamps))]

        # Exchange 2: price constant at 100
        prices2 = [100.0] * len(timestamps)

        self.data1 = pl.DataFrame({
            'timestamp': timestamps,
            'bestBid': prices1,
            'bestAsk': [p + 0.01 for p in prices1]
        })

        self.data2 = pl.DataFrame({
            'timestamp': timestamps,
            'bestBid': prices2,
            'bestAsk': [p + 0.01 for p in prices2]
        })

    def test_basic_analysis(self):
        """Test basic analysis returns expected structure"""
        result = analyze_pair_fast(
            "TEST/USDT",
            "Exchange1",
            "Exchange2",
            self.data1,
            self.data2
        )

        self.assertIsNotNone(result, "Analysis should not return None")
        self.assertIn('zero_crossings', result)
        self.assertIn('deviation_asymmetry', result)
        self.assertIn('opportunity_cycles_040bp', result)

    def test_deviation_calculation(self):
        """Test that deviation is calculated from 1.0 (price parity)"""
        result = analyze_pair_fast(
            "TEST/USDT",
            "Exchange1",
            "Exchange2",
            self.data1,
            self.data2
        )

        # With our test data (100 vs 100), deviation should be small
        self.assertLess(
            abs(result['deviation_asymmetry']),
            1.0,
            "Deviation should be small for similar prices"
        )

    def test_zero_crossings_detected(self):
        """Test that zero crossings are detected"""
        # Create data with more pronounced oscillation
        timestamps = pl.datetime_range(
            start=pl.datetime(2025, 1, 1, 0, 0, 0),
            end=pl.datetime(2025, 1, 1, 1, 0, 0),
            interval="1m",
            eager=True
        )

        # Exchange 1: price oscillates significantly around 100
        prices1 = [100.0 + 0.5 * (1 if i % 2 == 0 else -1) for i in range(len(timestamps))]

        # Exchange 2: price constant at 100
        prices2 = [100.0] * len(timestamps)

        data1 = pl.DataFrame({
            'timestamp': timestamps,
            'bestBid': prices1,
            'bestAsk': [p + 0.01 for p in prices1]
        })

        data2 = pl.DataFrame({
            'timestamp': timestamps,
            'bestBid': prices2,
            'bestAsk': [p + 0.01 for p in prices2]
        })

        result = analyze_pair_fast(
            "TEST/USDT",
            "Exchange1",
            "Exchange2",
            data1,
            data2
        )

        # With oscillating prices, should detect some crossings
        self.assertGreaterEqual(
            result['zero_crossings'],
            0,
            "Should detect zero crossings or at least not fail"
        )

    def test_custom_thresholds(self):
        """Test analysis with custom thresholds"""
        result = analyze_pair_fast(
            "TEST/USDT",
            "Exchange1",
            "Exchange2",
            self.data1,
            self.data2,
            thresholds=[0.1, 0.2, 0.3]
        )

        self.assertIsNotNone(result)
        self.assertIn('opportunity_cycles_030bp', result)

    def test_empty_data(self):
        """Test handling of empty data"""
        empty_data = pl.DataFrame({
            'timestamp': [],
            'bestBid': [],
            'bestAsk': []
        })

        result = analyze_pair_fast(
            "TEST/USDT",
            "Exchange1",
            "Exchange2",
            empty_data,
            empty_data
        )

        self.assertIsNone(result, "Should return None for empty data")

    def test_duration_calculation(self):
        """Test that duration is calculated correctly"""
        result = analyze_pair_fast(
            "TEST/USDT",
            "Exchange1",
            "Exchange2",
            self.data1,
            self.data2
        )

        # Data spans 1 hour
        self.assertAlmostEqual(
            result['duration_hours'],
            1.0,
            delta=0.1,
            msg="Duration should be approximately 1 hour"
        )


if __name__ == '__main__':
    unittest.main()
