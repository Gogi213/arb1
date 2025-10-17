"""WebSocket client to receive market data from C# server."""
import asyncio
import time
from typing import Callable, Awaitable
import websockets
import orjson
from .types import MarketData


class DataReceiver:
    """Receives market data from C# WebSocket server."""

    def __init__(self, url: str, reconnect_delay: int = 5):
        self.url = url
        self.reconnect_delay = reconnect_delay
        self._callback: Callable[[MarketData], Awaitable[None]] | None = None

    def on_data(self, callback: Callable[[MarketData], Awaitable[None]]) -> None:
        """Register callback for incoming market data."""
        self._callback = callback

    async def start(self) -> None:
        """Start receiving data with auto-reconnect."""
        while True:
            try:
                print(f"Connecting to {self.url}...")
                async with websockets.connect(self.url) as websocket:
                    print("✓ Connected to C# WebSocket server")
                    await self._receive_loop(websocket)
            except (websockets.exceptions.ConnectionClosed, ConnectionRefusedError) as e:
                print(f"✗ Connection failed: {e}")
                print(f"Reconnecting in {self.reconnect_delay}s...")
                await asyncio.sleep(self.reconnect_delay)
            except Exception as e:
                print(f"✗ Unexpected error: {e}")
                await asyncio.sleep(self.reconnect_delay)

    async def _receive_loop(self, websocket) -> None:
        """Main loop to receive and parse messages."""
        async for message in websocket:
            try:
                package = orjson.loads(message)
                fields = package.get("Fields", [])
                data = package.get("Data", [])

                if not fields or not data:
                    continue

                # Parse each row
                for row_data in data:
                    if len(row_data) != len(fields):
                        continue

                    # Create dict from fields and data
                    row_dict = {fields[i]: row_data[i] for i in range(len(fields))}

                    # Convert to MarketData
                    market_data: MarketData = {
                        'exchange': row_dict['exchange'],
                        'symbol': row_dict['symbol'],
                        'bid': float(row_dict['bestBid']),
                        'ask': float(row_dict['bestAsk']),
                        'spread_pct': float(row_dict['spreadPercentage']),
                        'min_volume': float(row_dict['minVolume']),
                        'max_volume': float(row_dict['maxVolume']),
                        'timestamp': time.time()
                    }

                    # Call registered callback
                    if self._callback:
                        await self._callback(market_data)

            except Exception as e:
                print(f"✗ Error parsing message: {e}")
                continue
