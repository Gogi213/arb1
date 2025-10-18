#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Balance Manager for Multi-Pair Trading
Manages capital allocation across trading pairs and exchanges
"""

from typing import Dict


class BalanceManager:
    """
    Manages balances across multiple trading pairs and exchanges (fake money simulation)

    Features:
    - Allocate capital to specific pairs
    - Track USDT and crypto balances per exchange
    - Execute simulated trades with fee calculations
    - Calculate total portfolio value
    """

    def __init__(self, total_usdt: float):
        """
        Initialize balance manager

        Args:
            total_usdt: Total starting capital in USDT
        """
        self.total_usdt = total_usdt
        self.available_usdt = total_usdt
        self.allocated_usdt = 0.0

        # Structure: {symbol: {exchange: {'usdt': float, 'crypto': float}}}
        self.pair_balances: Dict[str, Dict[str, Dict[str, float]]] = {}

    def allocate_for_pair(self, symbol: str, exchanges: list) -> Dict:
        """
        Allocate capital for a trading pair across exchanges

        Args:
            symbol: Trading pair symbol (e.g., "BTC/USDT")
            exchanges: List of exchange names

        Returns:
            Allocation dict: {exchange: {'usdt': amount, 'crypto': 0}}

        Raises:
            ValueError: If insufficient capital available
        """
        if self.available_usdt <= 0:
            raise ValueError("No capital available for allocation")

        # Distribute available capital evenly across exchanges
        usdt_per_exchange = self.available_usdt / len(exchanges)

        allocation = {}
        for exchange in exchanges:
            allocation[exchange] = {
                'usdt': usdt_per_exchange,
                'crypto': 0.0
            }

        # Store allocation
        self.pair_balances[symbol] = allocation

        # Update tracking
        self.allocated_usdt += self.available_usdt
        self.available_usdt = 0.0

        return allocation

    def set_initial_crypto(self, symbol: str, exchange: str, crypto_amount: float, purchase_price: float) -> None:
        """
        Set initial crypto position (converts USDT to crypto)

        Args:
            symbol: Trading pair symbol
            exchange: Exchange name
            crypto_amount: Amount of crypto purchased
            purchase_price: Price per unit of crypto

        Raises:
            KeyError: If pair or exchange not allocated
        """
        if symbol not in self.pair_balances:
            raise KeyError(f"Pair {symbol} not allocated")

        if exchange not in self.pair_balances[symbol]:
            raise KeyError(f"Exchange {exchange} not allocated for {symbol}")

        # Calculate cost
        cost = crypto_amount * purchase_price

        # Update balances
        self.pair_balances[symbol][exchange]['crypto'] = crypto_amount
        self.pair_balances[symbol][exchange]['usdt'] -= cost

    def execute_trade(
        self,
        symbol: str,
        buy_exchange: str,
        sell_exchange: str,
        amount: float,
        buy_price: float,
        sell_price: float,
        fees: Dict[str, Dict[str, float]]
    ) -> None:
        """
        Execute a simulated arbitrage trade

        Args:
            symbol: Trading pair symbol
            buy_exchange: Exchange to buy from
            sell_exchange: Exchange to sell on
            amount: Amount of crypto to trade
            buy_price: Buy price per unit
            sell_price: Sell price per unit
            fees: Fee structure {exchange: {'base': float, 'quote': float}}
                  e.g., {'Binance': {'base': 0.001, 'quote': 0.001}}

        Raises:
            KeyError: If pair or exchange not allocated
        """
        if symbol not in self.pair_balances:
            raise KeyError(f"Pair {symbol} not allocated")

        # Buy side
        buy_fee_base = fees.get(buy_exchange, {}).get('base', 0.001)
        buy_fee_quote = fees.get(buy_exchange, {}).get('quote', 0.001)

        # Cost: amount * price + fee on quote currency
        buy_cost = amount * buy_price * (1 + buy_fee_quote)
        # Crypto received: amount - fee on base currency
        crypto_bought = amount * (1 - buy_fee_base)

        self.pair_balances[symbol][buy_exchange]['usdt'] -= buy_cost
        self.pair_balances[symbol][buy_exchange]['crypto'] += crypto_bought

        # Sell side
        sell_fee_base = fees.get(sell_exchange, {}).get('base', 0.001)
        sell_fee_quote = fees.get(sell_exchange, {}).get('quote', 0.001)

        # Crypto needed: amount + fee on base currency
        crypto_to_sell = amount * (1 + sell_fee_base)
        # Proceeds: amount * price - fee on quote currency
        sell_proceeds = amount * sell_price * (1 - sell_fee_quote)

        self.pair_balances[symbol][sell_exchange]['crypto'] -= crypto_to_sell
        self.pair_balances[symbol][sell_exchange]['usdt'] += sell_proceeds

    def get_pair_balances(self, symbol: str) -> Dict:
        """
        Get balances for a specific pair

        Args:
            symbol: Trading pair symbol

        Returns:
            Balance dict: {exchange: {'usdt': float, 'crypto': float}}

        Raises:
            KeyError: If pair not allocated
        """
        if symbol not in self.pair_balances:
            raise KeyError(f"Pair {symbol} not allocated")

        return self.pair_balances[symbol]

    def get_total_value(self, current_prices: Dict[str, float]) -> float:
        """
        Calculate total portfolio value

        Args:
            current_prices: Current prices {symbol: price}

        Returns:
            Total value in USDT
        """
        total = self.available_usdt + self.allocated_usdt

        # Add value of all USDT balances
        for symbol, exchanges in self.pair_balances.items():
            price = current_prices.get(symbol, 0)
            for exchange, balances in exchanges.items():
                total += balances['usdt']
                total += balances['crypto'] * price

        return total

    def deallocate_pair(self, symbol: str, current_price: float) -> None:
        """
        Deallocate a pair (sell all crypto positions back to USDT)

        Args:
            symbol: Trading pair symbol
            current_price: Current price for liquidation

        Raises:
            KeyError: If pair not allocated
        """
        if symbol not in self.pair_balances:
            raise KeyError(f"Pair {symbol} not allocated")

        # Convert all crypto to USDT at current price
        total_freed = 0.0
        for exchange, balances in self.pair_balances[symbol].items():
            crypto_value = balances['crypto'] * current_price
            total_freed += balances['usdt'] + crypto_value

        # Remove allocation
        del self.pair_balances[symbol]

        # Update tracking
        self.allocated_usdt -= total_freed
        self.available_usdt += total_freed


# Example usage / demonstration
def main():
    """Example usage of BalanceManager"""
    print("=== Balance Manager Demo ===\n")

    # Initialize with $10,000
    balance_manager = BalanceManager(total_usdt=10000)
    print(f"Starting capital: ${balance_manager.total_usdt:.2f}\n")

    # Allocate for BTC/USDT on 2 exchanges
    print("Allocating for BTC/USDT...")
    allocation = balance_manager.allocate_for_pair('BTC/USDT', ['Binance', 'BingX'])
    print(f"Allocation: {allocation}\n")

    # Set initial crypto positions
    print("Buying initial BTC positions...")
    balance_manager.set_initial_crypto('BTC/USDT', 'Binance', 0.05, 42500)
    balance_manager.set_initial_crypto('BTC/USDT', 'BingX', 0.05, 42500)

    balances = balance_manager.get_pair_balances('BTC/USDT')
    print("Balances after initial purchase:")
    for exchange, bal in balances.items():
        print(f"  {exchange}: {bal['crypto']:.6f} BTC, ${bal['usdt']:.2f} USDT")

    # Execute arbitrage trade
    print("\nExecuting arbitrage trade...")
    fees = {
        'Binance': {'base': 0.001, 'quote': 0.001},
        'BingX': {'base': 0.001, 'quote': 0.001}
    }
    balance_manager.execute_trade(
        symbol='BTC/USDT',
        buy_exchange='Binance',
        sell_exchange='BingX',
        amount=0.01,
        buy_price=42500,
        sell_price=42520,
        fees=fees
    )

    balances_after = balance_manager.get_pair_balances('BTC/USDT')
    print("Balances after trade:")
    for exchange, bal in balances_after.items():
        print(f"  {exchange}: {bal['crypto']:.6f} BTC, ${bal['usdt']:.2f} USDT")

    # Calculate total value
    total_value = balance_manager.get_total_value({'BTC/USDT': 42510})
    print(f"\nTotal portfolio value: ${total_value:.2f}")
    print(f"Profit: ${total_value - balance_manager.total_usdt:.2f}")


if __name__ == "__main__":
    main()
