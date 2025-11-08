import os
import sys
import pytest
from fastapi.testclient import TestClient
from datetime import datetime, timedelta, timezone

# Add project root to sys.path for correct imports
project_root = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

# Set environment variable to disable external WS client during tests
os.environ["DISABLE_WEBSOCKET_CLIENT"] = "true"

from charts.server import app

client = TestClient(app)

@pytest.mark.timeout(10)
def test_realtime_websocket_endpoint():
    """
    Regression test for the /ws/realtime_charts endpoint.
    It seeds the server with both mock opportunity and mock window data,
    then verifies that the WebSocket endpoint sends the correct calculated data.
    """
    now = datetime.now(timezone.utc)
    
    # 1. --- Define Mock Data ---
    mock_opportunities = {
        "opportunities": [
            {"symbol": "MOCK/USDT", "exchange1": "Bybit", "exchange2": "GateIo", "opportunity_cycles_040bp": 100}
        ]
    }
    
    mock_windows = {
        "windows": [
            {
                "exchange": "Bybit", "symbol": "MOCK/USDT",
                "window_start": (now - timedelta(hours=1)).isoformat(), "window_end": now.isoformat(),
                "spreads": [
                    {"exchange": "Bybit", "symbol": "MOCK/USDT", "timestamp": (now - timedelta(seconds=2)).isoformat(), "best_bid": 101.0, "best_ask": 101.1, "spread_percentage": 0, "min_volume": 0, "max_volume": 0},
                    {"exchange": "Bybit", "symbol": "MOCK/USDT", "timestamp": now.isoformat(), "best_bid": 102.0, "best_ask": 102.1, "spread_percentage": 0, "min_volume": 0, "max_volume": 0}
                ]
            },
            {
                "exchange": "GateIo", "symbol": "MOCK/USDT",
                "window_start": (now - timedelta(hours=1)).isoformat(), "window_end": now.isoformat(),
                "spreads": [
                    {"exchange": "GateIo", "symbol": "MOCK/USDT", "timestamp": (now - timedelta(seconds=2)).isoformat(), "best_bid": 100.0, "best_ask": 100.1, "spread_percentage": 0, "min_volume": 0, "max_volume": 0},
                    {"exchange": "GateIo", "symbol": "MOCK/USDT", "timestamp": now.isoformat(), "best_bid": 100.0, "best_ask": 100.1, "spread_percentage": 0, "min_volume": 0, "max_volume": 0}
                ]
            }
        ]
    }

    # 2. --- Seed the server ---
    seed_response = client.post("/api/test/load_mock_data", json={**mock_opportunities, **mock_windows})
    assert seed_response.status_code == 200
    assert seed_response.json()["status"] == "mock data loaded"
    print("\nSuccessfully seeded server with mock opportunity and window data.")

    # 3. --- Connect and Test WebSocket ---
    with client.websocket_connect("/ws/realtime_charts") as websocket:
        print("WebSocket connection established.")
        
        data = websocket.receive_json()
        print(f"Received data from WebSocket: {data}")

        # 4. --- Assertions ---
        assert isinstance(data, list)
        assert len(data) == 1
        
        chart = data[0]
        assert chart["symbol"] == "MOCK/USDT"
        assert chart["exchange1"] == "Bybit"
        assert chart["exchange2"] == "GateIo"
        assert "spreads" in chart
        assert len(chart["spreads"]) > 0
        assert abs(chart["spreads"][-1] - 2.0) < 0.0001 # (102.0 / 100.0 - 1) * 100

        print("Successfully received and validated data from WebSocket.")
