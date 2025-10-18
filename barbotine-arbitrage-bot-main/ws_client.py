#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
WebSocket Client for SpreadAggregator
Connects to C# WebSocket server and receives real-time spread data
"""

import asyncio
import json
import sys
from typing import List, Dict, Callable
import websockets
from colorama import init

# Initialize colorama for Windows
init()

# Fix Windows Unicode encoding issues
if sys.platform == 'win32':
    sys.stdout.reconfigure(encoding='utf-8')


def parse_spread_data(raw_message: str) -> List[Dict]:
    """
    Parse spread data message from C# WebSocket server

    C# sends data in columnar format:
    {
        "Fields": ["symbol", "exchange", "bestBid", "bestAsk"],
        "Data": [
            ["BTC/USDT", "Binance", 42500.5, 42501.2],
            ["ETH/USDT", "Binance", 2250.0, 2251.5],
            ...
        ]
    }

    Args:
        raw_message: Raw JSON string from WebSocket

    Returns:
        List of dictionaries with field names as keys

    Example:
        [
            {"symbol": "BTC/USDT", "exchange": "Binance", "bestBid": 42500.5, "bestAsk": 42501.2},
            {"symbol": "ETH/USDT", "exchange": "Binance", "bestBid": 2250.0, "bestAsk": 2251.5},
            ...
        ]
    """
    try:
        message = json.loads(raw_message)

        # C# uses PascalCase, handle both cases
        fields = message.get('Fields') or message.get('fields')
        data_rows = message.get('Data') or message.get('data')

        if not fields or not data_rows:
            return []

        # Convert columnar format to row format
        result = []
        for row in data_rows:
            record = {}
            for i, field_name in enumerate(fields):
                record[field_name] = row[i]
            result.append(record)

        return result

    except json.JSONDecodeError as e:
        print(f"Error parsing JSON: {e}")
        return []
    except Exception as e:
        print(f"Error processing message: {e}")
        return []


class SpreadAggregatorClient:
    """
    WebSocket client for connecting to SpreadAggregator C# server

    Features:
    - Connects to ws://127.0.0.1:8181
    - Parses columnar data format
    - Handles PascalCase/camelCase field names
    - Invokes callback for each data update
    """

    def __init__(self):
        self.websocket = None

    async def connect(self, url: str) -> None:
        """
        Connect to WebSocket server

        Args:
            url: WebSocket URL (e.g., ws://127.0.0.1:8181)
        """
        self.websocket = await websockets.connect(url)
        print(f"✓ Connected to {url}")

    async def listen(self, callback: Callable[[List[Dict]], None]) -> None:
        """
        Listen for messages and invoke callback with parsed data

        Args:
            callback: Function to call with parsed data (sync function)
        """
        if not self.websocket:
            raise RuntimeError("Not connected. Call connect() first.")

        async for message in self.websocket:
            parsed_data = parse_spread_data(message)
            if parsed_data:
                # Callback is synchronous
                callback(parsed_data)

    async def close(self) -> None:
        """Close WebSocket connection"""
        if self.websocket:
            await self.websocket.close()


# Example usage / integration test
async def main():
    """Example usage of SpreadAggregatorClient"""
    print("=== SpreadAggregator WebSocket Client ===\n")

    client = SpreadAggregatorClient()

    # Connect to C# server
    await client.connect("ws://127.0.0.1:8181")

    # Message counter
    message_count = 0

    def on_data(parsed_data: List[Dict]):
        nonlocal message_count
        message_count += 1

        if message_count == 1:
            print(f"First message received: {len(parsed_data)} records")
            if parsed_data:
                print(f"Sample record: {parsed_data[0]}")

    # Listen for 5 seconds
    try:
        await asyncio.wait_for(client.listen(on_data), timeout=5.0)
    except asyncio.TimeoutError:
        print(f"\nReceived {message_count} messages in 5 seconds")
    finally:
        await client.close()


if __name__ == "__main__":
    asyncio.run(main())
