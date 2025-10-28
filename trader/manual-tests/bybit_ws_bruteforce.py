import websocket
import time
import json
import threading

# --- Parameters to Brute-Force ---
ENDPOINTS = [
    "wss://stream.bybit.com/v5/public/spot",
    "wss://stream.bybit.com/v5/public/linear",
]
TOPICS = [
    "orderbook.50.HUSDT",
    "orderbook.50.H_USDT",
    "orderbook.1.HUSDT",
]
PING_INTERVALS = [0, 20]  # 0 means no ping

# --- WebSocket Handlers ---
def on_message(ws, message):
    print(f"<<< RECV: {message}")

def on_error(ws, error):
    print(f"!!! ERROR: {error}")

def on_close(ws, close_status_code, close_msg):
    print("### closed ###")

def on_open(ws, endpoint, topic, ping_interval):
    print(f"### opened connection to {endpoint} ###")
    
    # Subscribe
    sub_message = json.dumps({"op": "subscribe", "args": [topic]})
    print(f">>> SEND: {sub_message}")
    ws.send(sub_message)

    # Start ping thread if needed
    if ping_interval > 0:
        def pinger():
            while ws.sock and ws.sock.connected:
                time.sleep(ping_interval)
                ping_msg = json.dumps({"op": "ping"})
                print(f">>> SEND: {ping_msg}")
                ws.send(ping_msg)
        threading.Thread(target=pinger, daemon=True).start()

# --- Main Loop ---
def run_test(endpoint, topic, ping_interval):
    print("\n" + "="*50)
    print(f"TESTING:")
    print(f"  - Endpoint: {endpoint}")
    print(f"  - Topic:    {topic}")
    print(f"  - Ping:     {ping_interval}s")
    print("="*50)

    ws = websocket.WebSocketApp(endpoint,
                              on_message=on_message,
                              on_error=on_error,
                              on_close=on_close)
    ws.on_open = lambda ws: on_open(ws, endpoint, topic, ping_interval)
    
    # Run for 30 seconds to see if we get any data
    ws.run_forever(ping_timeout=5)
    time.sleep(1) # Give it a moment before the next test

if __name__ == "__main__":
    # websocket.enableTrace(True) # Uncomment for extreme debug logging
    
    for endpoint in ENDPOINTS:
        for topic in TOPICS:
            for ping in PING_INTERVALS:
                # Run each test in a separate thread to avoid blocking
                # and allow run_forever to timeout properly.
                # We'll give each test 30 seconds to live.
                test_thread = threading.Thread(target=run_test, args=(endpoint, topic, ping))
                test_thread.daemon = True
                test_thread.start()
                test_thread.join(timeout=30)
                if test_thread.is_alive():
                    print("!!! TEST TIMEOUT - Moving to next combination !!!")
                
    print("\n\n--- ALL TESTS FINISHED ---")