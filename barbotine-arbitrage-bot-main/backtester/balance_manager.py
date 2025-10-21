#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Manages isolated balances for live testing simulation.
"""

from typing import Dict, List, Tuple
from .logger import log

class BalanceManager:
    """
    Manages an isolated balance for a single coin on a pool of exchanges.
    """
    def __init__(self, exchanges: List[str], symbol: str, initial_balance_usdt: float = 1000.0):
        """
        Initializes the balance manager for a specific coin.

        Args:
            exchanges: The list of exchanges to manage.
            symbol: The single trading symbol to manage (e.g., 'ETHUSDT').
            initial_balance_usdt: The starting USDT balance for each exchange.
        """
        self.exchanges = exchanges
        self.symbol = symbol
        self.base_currency = symbol[:-4]
        self.initial_balance = initial_balance_usdt
        self.balances: Dict[str, Dict[str, float]] = {}
        self._initialize_wallets()

    def _initialize_wallets(self):
        """
        Creates a wallet for each exchange with USDT and the base currency.
        """
        for exchange in self.exchanges:
            self.balances[exchange] = {
                'USDT': self.initial_balance,
                self.base_currency: 0.0
            }

    def setup_initial_assets(self, market_data: List[Dict], swap_ratio: float = 0.5):
        """
        Simulates the initial swap of USDT for the base asset on each exchange.
        """
        log.info(f"  Setting up initial assets for {self.symbol}...")
        
        for exchange in self.exchanges:
            # Find the first valid price for the symbol on this specific exchange
            price_record = next((d for d in market_data if d.get('exchange') == exchange and d.get('symbol') == self.symbol and d.get('bestAsk', 0) > 0), None)

            if price_record:
                price = price_record['bestAsk']
                usdt_to_swap = self.balances[exchange]['USDT'] * swap_ratio
                amount_to_buy = usdt_to_swap / price
                
                self.balances[exchange]['USDT'] -= usdt_to_swap
                self.balances[exchange][self.base_currency] += amount_to_buy
                log.info(f"    Swapped {usdt_to_swap:.2f} USDT for {amount_to_buy:.6f} {self.base_currency} on {exchange} at price {price}")
            else:
                log.warning(f"    Could not find any initial price for {self.symbol} on {exchange} to set up assets.")

        log.info(f"  Initial asset setup for {self.symbol} complete.")

    def get_balance(self, exchange: str) -> Dict[str, float]:
        """
        Retrieves the entire wallet balance for a specific exchange.

        Returns:
            A dictionary of currency balances for the exchange.
        """
        return self.balances.get(exchange, {})

    def simulate_trade(self, buy_exchange: str, sell_exchange: str, symbol: str, buy_price: float, sell_price: float, amount_crypto: float, commission_pct: float) -> bool:
        """
        Simulates an arbitrage trade and updates balances. Assumes balance check was done prior.
        """
        if not symbol.endswith('USDT'): return False
        
        base_currency = symbol[:-4]
        buy_wallet = self.get_balance(buy_exchange)
        sell_wallet = self.get_balance(sell_exchange)

        cost_usdt = amount_crypto * buy_price
        revenue_usdt = amount_crypto * sell_price

        # Apply commissions
        buy_commission_crypto = amount_crypto * (commission_pct / 100)
        sell_commission_usdt = revenue_usdt * (commission_pct / 100)

        # Update balances
        # Buy side (spending USDT, receiving base currency)
        buy_wallet['USDT'] -= cost_usdt
        buy_wallet[base_currency] += (amount_crypto - buy_commission_crypto)

        # Sell side (spending base currency, receiving USDT)
        sell_wallet[base_currency] -= amount_crypto
        sell_wallet['USDT'] += (revenue_usdt - sell_commission_usdt)
        
        return True

    def get_all_balances(self) -> Dict[str, Dict[str, float]]:
        """Returns all balances."""
        return self.balances

    def get_all_balances_str(self) -> str:
        """
        Returns a string representation of all balances for logging.
        """
        if not self.balances:
            return "No balances initialized."
        
        report = []
        for exchange, assets in sorted(self.balances.items()):
            asset_str = ", ".join([f"{asset}: {balance:.8f}" for asset, balance in sorted(assets.items())])
            report.append(f"  - {exchange}: {asset_str}")
        return "\n" + "\n".join(report)