import time
import hashlib
import hmac
import json
import websocket
import threading
from gate_api import ApiClient, SpotApi

# --- НАСТРОЙКИ ---
# ВАЖНО: Введите ваши реальные API ключи
GATE_KEY = "YOUR_API_KEY"
GATE_SECRET = "YOUR_API_SECRET"

# Параметры для тестового ордера
SYMBOL = "H_USDT"  # Торговая пара
ORDER_AMOUNT = 12  # Количество H для покупки (должно быть больше минимального)

# --- КОД ТЕСТА ---

def gen_sign(method, url, query_string, payload_string, secret):
    t = time.time()
    m = hashlib.sha512()
    m.update(payload_string.encode('utf-8'))
    hashed_payload = m.hexdigest()
    s = '%s\n%s\n%s\n%s\n%s' % (method, url, query_string, hashed_payload, t)
    sign = hmac.new(secret.encode('utf-8'), s.encode('utf-8'), hashlib.sha512).hexdigest()
    return {'KEY': GATE_KEY, 'Timestamp': str(t), 'SIGN': sign}

def on_message(ws, message):
    data = json.loads(message)
    print("\n--- [WebSocket Message Received] ---")
    # Красивый вывод JSON
    print(json.dumps(data, indent=4))
    print("------------------------------------")
    # Ищем событие об исполнении ордера
    if data.get("event") == "update" and data.get("channel") == "spot.orders":
        order_status = data.get("result", {}).get("status")
        if order_status == "filled":
            print("\n>>> Ордер исполнен (filled)! Изучите JSON выше, чтобы найти поле с общей суммой в USDT.")
            print(">>> Вероятные кандидаты: 'filled_total', 'cum_quote_qty' или похожее поле.")
            ws.close()

def on_error(ws, error):
    print(f"WebSocket Error: {error}")

def on_close(ws, close_status_code, close_msg):
    print("### WebSocket Closed ###")

def on_open(ws):
    print("### WebSocket Opened ###")
    
    def run(*args):
        # Подписка на обновления ордеров
        channel = "spot.orders"
        event = "subscribe"
        payload = [SYMBOL]
        
        t = time.time()
        hashed_payload = hmac.new(GATE_SECRET.encode('utf-8'), "".encode('utf-8'), hashlib.sha512).hexdigest()
        s = f'channel={channel}&event={event}&time={int(t)}'
        sign = hmac.new(GATE_SECRET.encode('utf-8'), s.encode('utf-8'), hashlib.sha512).hexdigest()
        
        auth_payload = {
            "time": int(t),
            "channel": channel,
            "event": event,
            "payload": payload,
            "auth": {
                "method": "api_key",
                "KEY": GATE_KEY,
                "SIGN": sign
            }
        }
        
        ws.send(json.dumps(auth_payload))
        print(f"Sent subscription request for {channel}")

        # Небольшая задержка перед размещением ордера
        time.sleep(2)

        # Размещение тестового ордера через REST API
        print("\nPlacing test order via REST API...")
        try:
            client = ApiClient(key=GATE_KEY, secret=GATE_SECRET)
            spot_api = SpotApi(client)
            order = spot_api.create_order(
                currency_pair=SYMBOL,
                order_type='market',
                side='buy',
                amount=str(ORDER_AMOUNT)
            )
            print(f"Order placed successfully. Order ID: {order.id}")
        except Exception as e:
            print(f"Error placing order: {e}")
            ws.close()

    threading.Thread(target=run).start()

if __name__ == "__main__":
    if GATE_KEY == "YOUR_API_KEY" or GATE_SECRET == "YOUR_API_SECRET":
        print("!!! ПОЖАЛУЙСТА, ВВЕДИТЕ ВАШИ API КЛЮЧИ В ПЕРЕМЕННЫЕ GATE_KEY и GATE_SECRET !!!")
    else:
        ws_url = "wss://api.gateio.ws/ws/v4/"
        print(f"Connecting to {ws_url}")
        ws = websocket.WebSocketApp(ws_url,
                                  on_open=on_open,
                                  on_message=on_message,
                                  on_error=on_error,
                                  on_close=on_close)
        ws.run_forever()