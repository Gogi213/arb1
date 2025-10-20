#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Manages isolated balances for live testing simulation.
"""

from typing import Dict, List, Tuple

class BalanceManager:
    """
    Handles the simulation of isolated balances for each exchange and trading pair.
    """
    def __init__(self, exchanges: List[str], symbols: List[str]):
        """
        Initializes the balance manager.

        Args:
            exchanges: A list of exchange names.
            symbols: A list of trading pair symbols.
        """
        self.exchanges = exchanges
        self.symbols = symbols
        self.initial_usdt_balance = 1000.0
        self.usdt_for_base_asset = 500.0
        self.balances: Dict[str, Dict[str, Dict[str, float]]] = {}
        self._initialize_wallets()

    def _initialize_wallets(self):
        """Creates an empty wallet for each exchange/symbol combination."""
        for exchange in self.exchanges:
            self.balances[exchange] = {}
            for symbol in self.symbols:
                if symbol.endswith('USDT'):
                    base_currency = symbol[:-4]
                    self.balances[exchange][symbol] = {
                        'USDT': self.initial_usdt_balance,
                        base_currency: 0.0
                    }

    def setup_initial_balances(self, market_data: List[Dict]):
        """
        Simulates the initial purchase of base assets for each wallet.
        
        Args:
            market_data: A list of dictionaries containing live market prices.
        """
        print("Setting up initial simulated balances...")
        market_prices: Dict[str, Dict[str, float]] = {}
        for data in market_data:
            exchange = data.get('exchange')
            symbol = data.get('symbol')
            ask_price = data.get('bestAsk')
            if exchange and symbol and ask_price > 0:
                if exchange not in market_prices:
                    market_prices[exchange] = {}
                market_prices[exchange][symbol] = ask_price

        for exchange in self.exchanges:
            for symbol in self.symbols:
                if symbol.endswith('USDT') and exchange in market_prices and symbol in market_prices[exchange]:
                    price = market_prices[exchange][symbol]
                    base_currency = symbol[:-4]
                    
                    if symbol not in self.balances[exchange]:
                        continue

                    wallet = self.balances[exchange][symbol]
                    
                    # Simulate buying base asset with 500 USDT
                    if price > 0:
                        amount_to_buy = self.usdt_for_base_asset / price
                        wallet[base_currency] += amount_to_buy
                        wallet['USDT'] -= self.usdt_for_base_asset
        print("Initial balances set up complete.")

    def get_balance(self, exchange: str, symbol: str) -> Dict[str, float]:
        """
        Retrieves the balance for a specific exchange and symbol.

        Returns:
            A dictionary with 'USDT' and base currency balances.
        """
        if not symbol.endswith('USDT'):
            return {'USDT': 0.0}
        base_currency = symbol[:-4]
        return self.balances.get(exchange, {}).get(symbol, {'USDT': 0.0, base_currency: 0.0})

    def simulate_trade(self, buy_exchange: str, sell_exchange: str, symbol: str, buy_price: float, sell_price: float, amount_crypto: float, commission_pct: float) -> bool:
        """
        Simulates an arbitrage trade and updates balances. Assumes balance check was done prior.
        """
        if not symbol.endswith('USDT'):
            return False
        base_currency = symbol[:-4]
        buy_wallet = self.get_balance(buy_exchange, symbol)
        sell_wallet = self.get_balance(sell_exchange, symbol)

        cost_usdt = amount_crypto * buy_price
        revenue_usdt = amount_crypto * sell_price

        # Apply commissions
        buy_commission_crypto = amount_crypto * (commission_pct / 100)
        sell_commission_usdt = revenue_usdt * (commission_pct / 100)

        # Update balances
        # Buy side
        buy_wallet['USDT'] -= cost_usdt
        buy_wallet[base_currency] += (amount_crypto - buy_commission_crypto)

        # Sell side
        sell_wallet[base_currency] -= amount_crypto
        sell_wallet['USDT'] += (revenue_usdt - sell_commission_usdt)
        
        return True

    def get_all_balances(self) -> Dict[str, Dict[str, Dict[str, float]]]:
        """Returns all balances."""
        return self.balances