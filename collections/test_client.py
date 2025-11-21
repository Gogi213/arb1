import asyncio
import websockets

async def hello():
    uri = "ws://localhost:5000/ws/realtime_charts"
    async with websockets.connect(uri) as websocket:
        print("Connected!")
        while True:
            await websocket.recv()
            # print("Received")

asyncio.get_event_loop().run_until_complete(hello())
