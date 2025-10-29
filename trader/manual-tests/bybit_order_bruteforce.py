import asyncio
import json
import websockets
import time
import hmac
import uuid

# --- Ваши API ключи ---
API_KEY = "UVSbRqLBEY30RnPaiH"
API_SECRET = "Fg45sn0nH4FhqZaxctj54Nf9cO03Qf9s0zds"
# ---------------------

async def connect_and_listen():
    uri = "wss://stream.bybit.com/v5/private"
    print(f"Connecting to {uri}...")

    async with websockets.connect(uri) as websocket:
        print("Connection successful.")

        # 1. Аутентификация
        expires = int((time.time() + 1) * 1000)
        signature = hmac.new(
            bytes(API_SECRET, "utf-8"),
            msg=bytes(f"GET/realtime{expires}", "utf-8"),
            digestmod="sha256"
        ).hexdigest()

        auth_payload = {
            "op": "auth",
            "args": [API_KEY, expires, signature]
        }
        await websocket.send(json.dumps(auth_payload))
        print(f"Sent auth request: {json.dumps(auth_payload)}")
        auth_response = await websocket.recv()
        print(f"Received auth response: {auth_response}")

        # 2. Подписка на ордера
        subscribe_payload = {
            "op": "subscribe",
            "args": ["order"]
        }
        await websocket.send(json.dumps(subscribe_payload))
        print(f"Sent subscription request: {json.dumps(subscribe_payload)}")
        subscribe_response = await websocket.recv()
        print(f"Received subscription response: {subscribe_response}")

        # 3. Размещение ордера
        client_order_id = f"bruteforce-{uuid.uuid4()}"
        place_order_payload = {
            "op": "order.create",
            "header": {
                "X-BAPI-TIMESTAMP": str(int(time.time() * 1000)),
                "X-BAPI-RECV-WINDOW": "5000"
            },
            "reqId": client_order_id,
            "args": [{
                "category": "spot",
                "symbol": "H_USDT",
                "side": "Buy",
                "orderType": "Limit",
                "qty": "1", # Минимальное количество для теста
                "price": "0.0001", # Очень низкая цена, чтобы не исполнился сразу
                "orderLinkId": client_order_id
            }]
        }
        await websocket.send(json.dumps(place_order_payload))
        print(f"Sent place order request: {json.dumps(place_order_payload)}")

        # 4. Ожидание и вывод всех сообщений
        print("\n--- Listening for all incoming messages for 10 seconds ---")
        try:
            for _ in range(10):
                 message = await asyncio.wait_for(websocket.recv(), timeout=1.0)
                 data = json.loads(message)
                 print("\n--- RAW MESSAGE RECEIVED ---")
                 print(json.dumps(data, indent=4))
                 print("--------------------------\n")
        except asyncio.TimeoutError:
            print("No more messages received in the last second.")


        # 5. Отмена ордера
        cancel_order_payload = {
            "op": "order.cancel",
             "header": {
                "X-BAPI-TIMESTAMP": str(int(time.time() * 1000)),
                "X-BAPI-RECV-WINDOW": "5000"
            },
            "reqId": f"cancel-{client_order_id}",
            "args": [{
                "category": "spot",
                "symbol": "H_USDT",
                "orderLinkId": client_order_id
            }]
        }
        await websocket.send(json.dumps(cancel_order_payload))
        print(f"Sent cancel order request: {json.dumps(cancel_order_payload)}")
        
        # 6. Ожидание подтверждения отмены
        try:
            for _ in range(5):
                 message = await asyncio.wait_for(websocket.recv(), timeout=1.0)
                 data = json.loads(message)
                 print("\n--- CANCEL RAW MESSAGE RECEIVED ---")
                 print(json.dumps(data, indent=4))
                 print("---------------------------------\n")
        except asyncio.TimeoutError:
            print("No more cancel messages received.")


if __name__ == "__main__":
    asyncio.run(connect_and_listen())