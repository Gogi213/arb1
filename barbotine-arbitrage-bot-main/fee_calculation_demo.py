#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Demonstration of fee calculation corrections
Shows the difference between old (wrong) and new (correct) calculations
"""

def old_calculation():
    """Old (WRONG) fee calculation"""
    print("=== OLD (WRONG) CALCULATION ===\n")

    amount = 0.01  # BTC
    buy_price = 42500  # USDT
    sell_price = 42520  # USDT
    fee_base = 0.001  # 0.1%
    fee_quote = 0.001  # 0.1%

    # Buy side (OLD)
    buy_cost_old = amount * buy_price * (1 + fee_quote + fee_base)  # ДВОЙНАЯ комиссия!
    crypto_bought_old = amount * (1 - fee_base)

    print(f"BUY {amount} BTC at {buy_price} USDT:")
    print(f"  Cost: {buy_cost_old:.2f} USDT (применена комиссия {(fee_quote + fee_base)*100:.2f}%)")
    print(f"  Received: {crypto_bought_old:.6f} BTC (минус {fee_base*100:.1f}%)")

    # Sell side (OLD)
    crypto_to_sell_old = amount * (1 + fee_base)
    sell_proceeds_old = amount * sell_price * (1 - fee_quote - fee_base)  # ДВОЙНАЯ комиссия!

    print(f"\nSELL {amount} BTC at {sell_price} USDT:")
    print(f"  Crypto spent: {crypto_to_sell_old:.6f} BTC (плюс {fee_base*100:.1f}%)")
    print(f"  Proceeds: {sell_proceeds_old:.2f} USDT (минус {(fee_quote + fee_base)*100:.2f}%)")

    # Profit (OLD - wrong in multi_pair_trader)
    gross_profit = (sell_price - buy_price) * amount
    fee_cost = amount * buy_price * (fee_quote + fee_base)
    net_profit_old = gross_profit - fee_cost

    print(f"\n Gross profit: {gross_profit:.2f} USDT")
    print(f" Fee cost (только buy): {fee_cost:.2f} USDT")
    print(f" NET PROFIT (old): {net_profit_old:.2f} USDT ❌")

    print(f"\n Actual profit: {sell_proceeds_old - buy_cost_old:.2f} USDT")


def new_calculation():
    """New (CORRECT) fee calculation"""
    print("\n\n=== NEW (CORRECT) CALCULATION ===\n")

    amount = 0.01  # BTC
    buy_price = 42500  # USDT
    sell_price = 42520  # USDT
    fee_base = 0.001  # 0.1%
    fee_quote = 0.001  # 0.1%

    # Buy side (NEW)
    buy_cost_new = amount * buy_price * (1 + fee_quote)  # Только quote fee
    crypto_bought_new = amount * (1 - fee_base)  # Только base fee

    print(f"BUY {amount} BTC at {buy_price} USDT:")
    print(f"  Cost: {buy_cost_new:.2f} USDT (комиссия {fee_quote*100:.1f}% на quote)")
    print(f"  Received: {crypto_bought_new:.6f} BTC (комиссия {fee_base*100:.1f}% на base)")

    # Sell side (NEW)
    crypto_to_sell_new = amount * (1 + fee_base)  # Только base fee
    sell_proceeds_new = amount * sell_price * (1 - fee_quote)  # Только quote fee

    print(f"\nSELL {amount} BTC at {sell_price} USDT:")
    print(f"  Crypto spent: {crypto_to_sell_new:.6f} BTC (комиссия {fee_base*100:.1f}% на base)")
    print(f"  Proceeds: {sell_proceeds_new:.2f} USDT (комиссия {fee_quote*100:.1f}% на quote)")

    # Profit (NEW)
    net_profit_new = sell_proceeds_new - buy_cost_new

    print(f"\n Buy cost: {buy_cost_new:.2f} USDT")
    print(f" Sell proceeds: {sell_proceeds_new:.2f} USDT")
    print(f" NET PROFIT (new): {net_profit_new:.2f} USDT ✅")


def comparison():
    """Compare results"""
    print("\n\n=== COMPARISON ===\n")

    amount = 0.01
    buy_price = 42500
    sell_price = 42520
    fee_base = 0.001
    fee_quote = 0.001

    # OLD
    buy_cost_old = amount * buy_price * (1 + fee_quote + fee_base)
    sell_proceeds_old = amount * sell_price * (1 - fee_quote - fee_base)
    profit_old = sell_proceeds_old - buy_cost_old

    # NEW
    buy_cost_new = amount * buy_price * (1 + fee_quote)
    sell_proceeds_new = amount * sell_price * (1 - fee_quote)
    profit_new = sell_proceeds_new - buy_cost_new

    print(f"Price difference: {sell_price - buy_price} USDT ({(sell_price - buy_price) / buy_price * 100:.3f}%)")
    print(f"Trade amount: {amount} BTC\n")

    print(f"OLD calculation:")
    print(f"  Buy cost: {buy_cost_old:.2f} USDT")
    print(f"  Sell proceeds: {sell_proceeds_old:.2f} USDT")
    print(f"  Profit: {profit_old:.4f} USDT ❌\n")

    print(f"NEW calculation:")
    print(f"  Buy cost: {buy_cost_new:.2f} USDT")
    print(f"  Sell proceeds: {sell_proceeds_new:.2f} USDT")
    print(f"  Profit: {profit_new:.4f} USDT ✅\n")

    difference = profit_new - profit_old
    print(f"Разница: {difference:.4f} USDT ({(difference / profit_old * 100):.1f}% больше)")
    print(f"\nСтарый расчет занижал прибыль из-за ДВОЙНОГО применения комиссий!")


if __name__ == "__main__":
    old_calculation()
    new_calculation()
    comparison()
