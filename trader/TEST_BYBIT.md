# Тестирование Bybit Market Sell

## 🚀 Быстрый Запуск

```bash
cd "c:\visual projects\arb1\trader"
dotnet run --project src/Host bybit
```

## 🔍 Что Проверить в Логах

### 1. Аутентификация (первые 2-3 секунды)

Должно быть:
```
[WS-PRIVATE] Connecting to wss://stream.bybit.com/v5/private...
[WS-TRADE] Connecting to wss://stream.bybit.com/v5/trade...
[WS-PRIVATE] Authentication request sent. Waiting for confirmation...
[WS-TRADE] Authentication request sent. Waiting for confirmation...
[WS-PRIVATE-EVENT] Authentication successful.
[WS-TRADE-EVENT] Authentication successful.
[WS-PRIVATE] Authentication confirmed successfully.
[WS-TRADE] Authentication confirmed successfully.
```

❌ **Если видите timeout:**
```
[WS-TRADE-ERROR] Authentication timeout after 10 seconds!
```
→ Проблема с API ключами или сетью

### 2. Limit Buy Execution

```
[Convergent] Buy filled: X.XX XPL
[Convergent-Balance] Received update for XPL: X.XX
[Convergent] Balance updated. Available: X.XX
```

### 3. Market Sell (КРИТИЧЕСКИЙ МОМЕНТ)

**✅ Успешный сценарий:**
```
[Convergent] ========== PRE-SELL VALIDATION ==========
[Convergent] Base Asset: XPL
[Convergent] Available Balance: 6.0
[Bybit] ========== PlaceOrderAsync ENTRY ==========
[Bybit] Input Symbol: 'XPL_USDT'
[Bybit] Converted symbol: 'XPL_USDT' -> 'XPLUSDT'
[WS-TRADE] ========== PlaceMarketOrderAsync DIAGNOSTIC START ==========
[WS-TRADE] Authenticated: True
[WS-TRADE] 📤 Sending order.create message:
[WS-TRADE] ⏳ Waiting for Bybit response (timeout 10s)...
[WS-TRADE-EVENT] ========== OPERATION RESPONSE ==========
[WS-TRADE-EVENT] RetCode: 0
[WS-TRADE-EVENT] ✅ Order creation SUCCESS
[WS-TRADE] ✅ SUCCESS! Received real OrderId 'XXXXXXXXXX'
[Bybit] ✅ Successfully parsed OrderId: XXXXXXXXXX
[Convergent] ✅ Market sell placed successfully: XXXXXXXXXX
```

**❌ Ошибка - не аутентифицирован:**
```
[WS-TRADE] Authenticated: False
[WS-TRADE-ERROR] ❌ Cannot place market order: Trade WebSocket is NOT authenticated!
```

**❌ Ошибка - timeout:**
```
[WS-TRADE-ERROR] ❌ TIMEOUT after 10000.00ms waiting for OrderId!
[WS-TRADE-ERROR] Check WebSocket receive logs above for any responses with reqId 'XXXX'
[Bybit-ERROR] ❌ Failed to parse OrderId from string: 'abc123def456...'
[Bybit-ERROR]   2) OrderId is a GUID (timeout fallback from PlaceMarketOrderAsync)
[Convergent-ERROR] ❌ FAILED to place sell order! PlaceOrderAsync returned NULL
```

**❌ Ошибка от Bybit:**
```
[WS-TRADE-ERROR] ❌ Order creation FAILED!
[WS-TRADE-ERROR] Error Code: 10001
[WS-TRADE-ERROR] Error Message: Insufficient balance
```

## 🔧 Типичные Проблемы и Решения

| Симптом | Причина | Решение |
|---------|---------|---------|
| `Authentication timeout` | Неправильные API ключи | Проверить appsettings.json |
| `Authenticated: False` | Race condition (исправлен) | Перезапустить |
| `RetCode: 10001` | Недостаточно баланса | Пополнить баланс USDT |
| `RetCode: 10004` | Неправильный символ | Проверить Symbol в config |
| `TIMEOUT after 10000ms` | Bybit не ответил | Проверить сеть, баланс |
| `OrderId is a GUID` | Timeout ожидания ответа | См. выше причины timeout |

## 📋 Checklist После Запуска

- [ ] Аутентификация PRIVATE успешна
- [ ] Аутентификация TRADE успешна
- [ ] Limit buy исполнился
- [ ] Balance update получен
- [ ] PlaceMarketOrderAsync вызван с `Authenticated: True`
- [ ] Получен ответ от Bybit (OPERATION RESPONSE)
- [ ] RetCode == 0
- [ ] OrderId получен и распарсен
- [ ] PlaceOrderAsync вернул число (не NULL)

## 🐛 Сбор Информации для Отладки

Если market sell не работает, сохраните:

1. **Полный лог** от начала до конца
2. **Скриншот** секции `PlaceMarketOrderAsync DIAGNOSTIC`
3. **Скриншот** секции `OPERATION RESPONSE` (если есть)
4. **Баланс Bybit** на момент запуска

## 🔬 Продвинутая Отладка

### Проверить, что Bybit отправляет ответ:

Найти в логах:
```
[WS-TRADE-RECV] @ HH:mm:ss.fff: {"reqId":"XXXX",...}
```

Если такой строки НЕТ → Bybit не отправил ответ.

### Проверить, что ответ парсится:

Если есть `[WS-TRADE-RECV]` с `reqId`, но НЕТ `[WS-TRADE-EVENT] ========== OPERATION RESPONSE ==========`:
→ Проблема парсинга JSON (маловероятно, но возможно)

### Проверить timing:

```
[WS-TRADE] ✓ Order message sent in X.XXms. ReqId: abc...
...
[WS-TRADE-RECV] @ HH:mm:ss.fff: {"reqId":"abc",...}
```

Время между ними = network latency + Bybit processing time.
Если > 10 секунд → увеличить timeout.

## 💡 Быстрый Тест без Реального Трейдинга

Пока нет возможности тестировать без реальных ордеров, но можно:

1. **Уменьшить Amount** в appsettings.json до минимума
2. **Использовать дешевую монету** (если XPL дорогой)
3. **Тестировать на Testnet** (если есть в Bybit)

## 📞 Поддержка

Если проблема не решается:
1. Сохраните полный лог
2. Проверьте все пункты выше
3. Опишите симптомы подробно
