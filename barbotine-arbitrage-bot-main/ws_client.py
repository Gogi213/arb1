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


def standardize_symbol(symbol: str) -> str:
    """Removes special characters from a symbol string to standardize it."""
    if not isinstance(symbol, str):
        return ""
    return symbol.replace('/', '').replace('-', '').replace('_', '').replace(' ', '')

def parse_spread_data(raw_message: str) -> List[Dict]:
    """
    Parse spread data message from C# WebSocket server and standardize symbols.
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
            record = dict(zip(fields, row))
            
            # Standardize the symbol
            if 'symbol' in record:
                record['symbol'] = standardize_symbol(record['symbol'])
            
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
    WebSocket client for connecting to SpreadAggregator C# server.
    Includes a buffer to handle high-frequency real-time data.
    """

    def __init__(self):
        self.websocket = None
        self.buffer = asyncio.Queue()

    async def connect(self, url: str) -> None:
        """
        Connect to WebSocket server

        Args:
            url: WebSocket URL (e.g., ws://127.0.0.1:8181)
        """
        self.websocket = await websockets.connect(url)
        print(f"✓ Connected to {url}")

    async def listen(self) -> None:
        """
        Listens for messages and puts them into an internal buffer.
        """
        if not self.websocket:
            raise RuntimeError("Not connected. Call connect() first.")

        async for message in self.websocket:
            parsed_data = parse_spread_data(message)
            if parsed_data:
                # Since each message from realtime is just one update,
                # we put the single record dictionary into the queue.
                await self.buffer.put(parsed_data[0])
    
    async def get_batch(self) -> List[Dict]:
        """
        Gets all currently buffered messages and clears the buffer.
        """
        if self.buffer.empty():
            return []
        
        batch = []
        while not self.buffer.empty():
            batch.append(await self.buffer.get())
        return batch

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
    await client.connect("ws://127.0.0.1:8181/realtime")

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
