# 🔴 ПРОБЛЕМА: Почему бот перестаёт зарабатывать после 110 сделок

## 📊 Факты из 8-минутного теста:

- **Trade #1**: profit = $5.55
- **Trade #50**: profit = $0.03
- **Trade #110**: profit = $0.00
- **Trade #111-275**: profit = $0.00 (все нули!)

**Итого**: $55.18 за 275 сделок, но только первые 110 сделок прибыльные.

---

## 🔍 КОД ПРОБЛЕМЫ

### 1. Начальная аллокация (multi_pair_trader.py:110-115)

```python
# Allocate capital for this pair
allocation = self.balance_manager.allocate_for_pair(symbol, exchanges)

# Initialize crypto positions (buy at current ask price)
buy_price = opportunity.min_ask
usdt_per_exchange = allocation[exchanges[0]]['usdt']  # $5000 на биржу
crypto_amount = usdt_per_exchange * 0.5 / buy_price   # ← ПРОБЛЕМА #1: только 50%!
```

**Что происходит:**
- Начальный капитал: $10,000
- Распределяется на 2 биржи: $5,000 каждой
- Но покупается крипты только на **50% от $5,000 = $2,500**
- Остальные $2,500 остаются в USDT

**Начальные балансы:**
```
Bybit:   $2500 USDT + 6510 ZBTUSDT  (купили на $2500)
Binance: $2500 USDT + 6510 ZBTUSDT  (купили на $2500)
```

---

### 2. Логика сделок (multi_pair_trader.py:196)

```python
# Calculate trade amount (percentage of available crypto)
sell_balance = balances[sell_exchange]['crypto']  # Крипта на бирже продажи
trade_amount = sell_balance * self.trade_amount_pct  # ← 10% от баланса
```

**Что происходит каждую сделку:**

#### Trade #1:
```
Opportunity: Buy Bybit @ $0.384, Sell Binance @ $0.393 (2.42% profit)

BEFORE:
  Bybit (buy):    $2500 USDT, 6510 crypto
  Binance (sell): $2500 USDT, 6510 crypto

ACTION:
  - Sell 10% from Binance = 651 crypto @ $0.393 = $256
  - Buy on Bybit = 651 crypto @ $0.384 = $250

AFTER:
  Bybit (buy):    $2250 USDT, 7161 crypto ← Крипты стало БОЛЬШЕ
  Binance (sell): $2756 USDT, 5859 crypto ← Крипты стало МЕНЬШЕ

Net profit: $5.55 ✅
```

#### Trade #2 (всё повторяется):
```
BEFORE:
  Bybit (buy):    $2250 USDT, 7161 crypto
  Binance (sell): $2756 USDT, 5859 crypto ← Уже на 10% меньше

ACTION:
  - Sell 10% from Binance = 586 crypto @ $0.393
  - Buy on Bybit = 586 crypto @ $0.384

AFTER:
  Bybit (buy):    $2025 USDT, 7746 crypto ← ЕЩЁ БОЛЬШЕ
  Binance (sell): $2986 USDT, 5272 crypto ← ЕЩЁ МЕНЬШЕ

Net profit: $5.05 ✅
```

---

## 🔴 КОРЕНЬ ПРОБЛЕМЫ

### Баланс всегда идёт в **ОДНУ СТОРОНУ**:

**Buy from Bybit → Sell to Binance** (всё время одно направление!)

1. Каждая сделка:
   - **Bybit**: Покупаем → крипты становится БОЛЬШЕ, USDT меньше
   - **Binance**: Продаём → крипты становится МЕНЬШЕ, USDT больше

2. После ~100 сделок:
   - **Bybit**: $0.71 USDT, **13,008 crypto** ← ВСЁ в крипте!
   - **Binance**: $5,054 USDT, **0.00 crypto** ← КРИПТЫ НЕТ!

3. Дальше:
   - Trade amount = `0.00 * 10% = 0.00` ← **Нет крипты на продажу!**
   - Profit = $0.00

---

## 💡 ПОЧЕМУ ТАК ПРОИСХОДИТ?

### Проблема в логике арбитража:

```python
# arbitrage_analyzer.py:95
if bid is not None and bid > max_bid:
    max_bid = bid
    max_bid_exchange = exchange
```

Пример рыночных данных:
```
Bybit:   ask=$0.384 (где дешевле купить)
Binance: bid=$0.393 (где дороже продать)
```

**Результат**: Бот ВСЕГДА покупает на Bybit, ВСЕГДА продаёт на Binance!

Направление сделок **НЕ МЕНЯЕТСЯ**, потому что:
- Bybit стабильно дешевле
- Binance стабильно дороже

---

## 📉 ГРАФИК ДЕГРАДАЦИИ

```
Trade    | Binance crypto | Profit
---------|----------------|--------
#1       | 6510          | $5.55
#10      | 5859          | $2.07
#50      | 2000          | $0.03
#100     | 100           | $0.00
#110     | 0             | $0.00
#111-275 | 0             | $0.00  ← БОТ МЁРТВ
```

---

## ✅ РЕШЕНИЯ

### Вариант 1: **Увеличить начальную крипту** (quick fix)

```python
# БЫЛО:
crypto_amount = usdt_per_exchange * 0.5 / buy_price  # 50%

# СТАЛО:
crypto_amount = usdt_per_exchange * 0.9 / buy_price  # 90%
```

**Эффект**: Больше крипты в начале → больше сделок до опустошения

**Проблема**: Всё равно опустошится, просто позже

---

### Вариант 2: **Увеличить trade_amount_pct** (НЕ РЕШАЕТ!)

```python
# БЫЛО:
trade_amount_pct = 0.1  # 10%

# СТАЛО:
trade_amount_pct = 0.3  # 30%
```

**Эффект**: Опустошится БЫСТРЕЕ!

**Проблема**: Это сделает хуже, не лучше

---

### Вариант 3: **Ребалансировка** (правильное решение!)

Добавить логику ребалансировки, когда баланс сильно перекошен:

```python
def rebalance_if_needed(self, symbol):
    """Rebalance if one exchange is depleted"""
    balances = self.balance_manager.get_pair_balances(symbol)

    for exchange, bal in balances.items():
        # Если крипты меньше 10% от начального
        if bal['crypto'] < initial_crypto * 0.1:
            # Купить крипты за USDT
            # ...

        # Если USDT меньше 10% от начального
        if bal['usdt'] < initial_usdt * 0.1:
            # Продать крипту за USDT
            # ...
```

---

### Вариант 4: **Двунаправленные сделки** (сложно!)

Изменить логику так, чтобы:
- Если Binance пуст → временно торговать в обратную сторону
- Buy from Binance, Sell to Bybit (реверс)

**Проблема**: Нужна глубокая переделка логики

---

## 🎯 РЕКОМЕНДАЦИЯ

### Комбинированный подход:

1. **Увеличить начальную крипту до 90%**:
   ```python
   crypto_amount = usdt_per_exchange * 0.9 / buy_price
   ```

2. **Добавить простую ребалансировку**:
   ```python
   # Когда крипта на sell_exchange < 5%:
   if sell_balance < initial_crypto * 0.05:
       # Не торговать, подождать восстановления
       return False
   ```

3. **НЕ трогать trade_amount_pct** - оставить 10%

---

## 📝 ИТОГО

### Текущая проблема:
```
ОДНОСТОРОННИЙ АРБИТРАЖ:
Bybit (дешёвая) → всегда покупка → крипта накапливается
Binance (дорогая) → всегда продажа → крипта истощается
```

### Решение:
```
ЛИБО: Ребалансировка (правильно)
ЛИБО: Больше начальной крипты (временный fix)
```

---

**Вывод**: Проблема не в комиссиях (они рассчитаны правильно!), а в **односторонней торговле** без ребалансировки балансов.
