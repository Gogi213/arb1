import asyncio
import json
import os
import sys
from datetime import datetime, timedelta, timezone
from unittest.mock import patch, MagicMock

import polars as pl
import pytest

# Add project root to sys.path for correct imports
project_root = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from charts.server import rolling_window_data_streamer, RollingWindowData, SpreadData, rolling_window

@pytest.mark.asyncio
async def test_realtime_streamer_logic():
    """
    Unit test for the `rolling_window_data_streamer` to verify
    opportunity filtering and inter-exchange spread calculation.
    """
    # 1. --- Mock Analyzer Data ---
    mock_opp_data = pl.DataFrame({
        "symbol": ["MOCK/USDT"],
        "exchange1": ["Bybit"],
        "exchange2": ["GateIo"],
        "opportunity_cycles_040bp": [100]
    })

    # 2. --- Mock Rolling Window Data ---
    now = datetime.now(timezone.utc)
    # Window A (Bybit)
    window_a = RollingWindowData(
        exchange="Bybit", symbol="MOCK/USDT", window_start=now, window_end=now, spreads=[], trades=[]
    )
    window_a.spreads.append(SpreadData("Bybit", "MOCK/USDT", now - timedelta(seconds=2), 101.0, 101.1, 0, 0, 0))
    window_a.spreads.append(SpreadData("Bybit", "MOCK/USDT", now, 102.0, 102.1, 0, 0, 0))

    # Window B (GateIo)
    window_b = RollingWindowData(
        exchange="GateIo", symbol="MOCK/USDT", window_start=now, window_end=now, spreads=[], trades=[]
    )
    window_b.spreads.append(SpreadData("GateIo", "MOCK/USDT", now - timedelta(seconds=2), 100.0, 100.1, 0, 0, 0))
    window_b.spreads.append(SpreadData("GateIo", "MOCK/USDT", now, 100.0, 100.1, 0, 0, 0))

    # Inject mock data into the global rolling_window object
    rolling_window.windows = {
        "Bybit_MOCK/USDT": window_a,
        "GateIo_MOCK/USDT": window_b
    }

    # 3. --- Patch external dependencies and run the generator ---
    with patch('polars.read_csv', return_value=mock_opp_data), \
         patch('os.path.exists', return_value=True), \
         patch('os.listdir', return_value=['summary_stats_mock.csv']), \
         patch('os.path.getmtime', return_value=0):
        
        # Collect results from the async generator
        results = [json.loads(res) async for res in rolling_window_data_streamer()]

    # 4. --- Assertions ---
    # We should get exactly one chart for our single opportunity
    assert len(results) == 1
    
    chart = results[0]
    # Check that the exchanges are correct, not "aggregated"
    assert chart["exchange1"] == "Bybit"
    assert chart["exchange2"] == "GateIo"
    assert chart["symbol"] == "MOCK/USDT"

    # Check the spread calculation.
    # For the latest timestamp: (102.0 / 100.0 - 1) * 100 = 2.0
    # The asof join should match the latest bid from A with the latest from B.
    assert len(chart["spreads"]) > 0
    # The last calculated spread should be approximately 2.0
    assert abs(chart["spreads"][-1] - 2.0) < 0.0001

    print("\nUnit test passed: Real-time streamer correctly calculates inter-exchange spread.")

    # Cleanup global state
    rolling_window.windows.clear()
