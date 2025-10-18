#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Multi-Pair Trading Engine (Fake Money Mode)
Manages simultaneous trading of multiple cryptocurrency pairs
"""

import time
from dataclasses import dataclass
from typing import Dict, List
from balance_manager import BalanceManager
from arbitrage_analyzer import ArbitrageOpportunity


@dataclass
class TradingSession:
    """
    Represents an active trading session for a specific pair

    Attributes:
        symbol: Trading pair symbol
        exchanges: List of exchanges for this pair
        last_trade_time: Timestamp of last executed trade
        trade_count: Number of trades executed
        total_profit: Cumulative profit in USDT
    """
    symbol: str
    exchanges: List[str]
    last_trade_time: float
    trade_count: int
    total_profit: float


class MultiPairTrader:
    """
    Manages trading for multiple pairs simultaneously (fake money simulation)

    Features:
    - Dynamic pair management (add/remove based on filters)
    - Per-pair balance tracking via BalanceManager
    - Cooldown periods to prevent over-trading
    - Profit tracking per pair and total
    """

    def __init__(
        self,
        balance_manager: BalanceManager,
        mode: str = 'fake-money',
        min_trade_interval: float = 1.0,
        trade_amount_pct: float = 0.1
    ):
        """
        Initialize multi-pair trader

        Args:
            balance_manager: BalanceManager instance for capital management
            mode: Trading mode ('fake-money' or 'real')
            min_trade_interval: Minimum seconds between trades on same pair
            trade_amount_pct: Percentage of crypto balance to trade per opportunity (0.1 = 10%)

        Raises:
            ValueError: If mode is invalid
        """
        if mode not in ['fake-money', 'real']:
            raise ValueError(f"Invalid mode: {mode}. Must be 'fake-money' or 'real'")

        self.balance_manager = balance_manager
        self.mode = mode
        self.min_trade_interval = min_trade_interval
        self.trade_amount_pct = trade_amount_pct

        # Active trading sessions: {symbol: TradingSession}
        self.active_sessions: Dict[str, TradingSession] = {}

        # Fee structure (default 0.1% maker/taker)
        self.default_fees = {'base': 0.001, 'quote': 0.001}

    def start_trading_pair(self, opportunity: ArbitrageOpportunity) -> bool:
        """
        Start trading a new pair and allocate capital

        Args:
            opportunity: Initial arbitrage opportunity for this pair

        Returns:
            True if pair started successfully, False otherwise
        """
        symbol = opportunity.symbol

        if symbol in self.active_sessions:
            # Already trading this pair
            return False

        # Check if we have available capital
        if self.balance_manager.available_usdt <= 0:
            return False  # No capital available

        # Determine exchanges for this pair
        # For simplicity, use the two exchanges from the opportunity
        exchanges = [opportunity.min_ask_exchange, opportunity.max_bid_exchange]

        # Allocate capital for this pair
        try:
            allocation = self.balance_manager.allocate_for_pair(symbol, exchanges)
        except Exception as e:
            print(f"Failed to allocate capital for {symbol}: {e}")
            return False

        # Initialize crypto positions (buy at current ask price)
        buy_price = opportunity.min_ask
        usdt_per_exchange = allocation[exchanges[0]]['usdt']
        crypto_amount = usdt_per_exchange * 0.5 / buy_price  # Use 50% of USDT to buy crypto

        for exchange in exchanges:
            self.balance_manager.set_initial_crypto(symbol, exchange, crypto_amount, buy_price)

        # Create trading session
        session = TradingSession(
            symbol=symbol,
            exchanges=exchanges,
            last_trade_time=0,
            trade_count=0,
            total_profit=0.0
        )

        self.active_sessions[symbol] = session

        return True

    def stop_trading_pair(self, symbol: str, current_price: float) -> bool:
        """
        Stop trading a pair and deallocate capital

        Args:
            symbol: Trading pair to stop
            current_price: Current price for liquidation

        Returns:
            True if stopped successfully, False if wasn't trading
        """
        if symbol not in self.active_sessions:
            # Not trading this pair
            return False

        # Remove session
        del self.active_sessions[symbol]

        # Deallocate capital (if allocated in balance manager)
        try:
            self.balance_manager.deallocate_pair(symbol, current_price)
        except KeyError:
            # Pair wasn't allocated in balance manager, that's ok
            pass

        return True

    def process_opportunity(self, opportunity: ArbitrageOpportunity) -> bool:
        """
        Process an arbitrage opportunity

        Args:
            opportunity: Arbitrage opportunity to potentially trade

        Returns:
            True if trade was executed, False otherwise
        """
        symbol = opportunity.symbol

        # Check if we're trading this pair
        if symbol not in self.active_sessions:
            return False

        session = self.active_sessions[symbol]

        # Check cooldown
        time_since_last_trade = time.time() - session.last_trade_time
        if time_since_last_trade < self.min_trade_interval:
            return False  # Still in cooldown

        # Check if pair is allocated in balance manager
        try:
            balances = self.balance_manager.get_pair_balances(symbol)
        except KeyError:
            # Pair not allocated, skip
            return False

        # Determine trade parameters
        buy_exchange = opportunity.min_ask_exchange
        sell_exchange = opportunity.max_bid_exchange

        # Calculate trade amount (percentage of available crypto)
        if buy_exchange not in balances or sell_exchange not in balances:
            return False

        sell_balance = balances[sell_exchange]['crypto']
        trade_amount = sell_balance * self.trade_amount_pct

        if trade_amount <= 0:
            return False  # No crypto to sell

        # Execute simulated trade
        fees = {
            buy_exchange: self.default_fees,
            sell_exchange: self.default_fees
        }

        try:
            self.balance_manager.execute_trade(
                symbol=symbol,
                buy_exchange=buy_exchange,
                sell_exchange=sell_exchange,
                amount=trade_amount,
                buy_price=opportunity.min_ask,
                sell_price=opportunity.max_bid,
                fees=fees
            )

            # Update session statistics
            session.last_trade_time = time.time()
            session.trade_count += 1

            # Calculate actual profit with fees
            # Buy side: pay price + quote_fee, receive amount - base_fee
            buy_cost = trade_amount * opportunity.min_ask * (1 + self.default_fees['quote'])
            crypto_received = trade_amount * (1 - self.default_fees['base'])

            # Sell side: pay amount + base_fee, receive price - quote_fee
            # We sell the same amount we intended to trade
            sell_proceeds = trade_amount * opportunity.max_bid * (1 - self.default_fees['quote'])
            crypto_paid = trade_amount * (1 + self.default_fees['base'])

            # Net profit = sell proceeds - buy cost
            net_profit = sell_proceeds - buy_cost
            session.total_profit += net_profit

            return True

        except Exception as e:
            # Trade failed
            print(f"Trade failed for {symbol}: {e}")
            return False

    def get_active_pairs(self) -> List[str]:
        """
        Get list of currently active trading pairs

        Returns:
            List of pair symbols
        """
        return list(self.active_sessions.keys())

    def get_statistics(self) -> Dict:
        """
        Get overall trading statistics

        Returns:
            Dictionary with stats: total_pairs, total_trades, total_profit
        """
        total_trades = sum(session.trade_count for session in self.active_sessions.values())
        total_profit = sum(session.total_profit for session in self.active_sessions.values())

        return {
            'total_pairs': len(self.active_sessions),
            'total_trades': total_trades,
            'total_profit': total_profit,
            'sessions': {
                symbol: {
                    'trades': session.trade_count,
                    'profit': session.total_profit
                }
                for symbol, session in self.active_sessions.items()
            }
        }


# Example usage / demonstration
def main():
    """Example usage of MultiPairTrader"""
    print("=== Multi-Pair Trader Demo ===\n")

    # Initialize with balance manager
    balance_manager = BalanceManager(total_usdt=10000)
    trader = MultiPairTrader(balance_manager, mode='fake-money')

    # Start trading BTC/USDT
    opp_btc = ArbitrageOpportunity(
        symbol='BTC/USDT',
        min_ask=42500,
        min_ask_exchange='Binance',
        max_bid=42520,
        max_bid_exchange='BingX',
        profit_pct=0.047,
        exchange_count=2
    )

    print(f"Starting to trade {opp_btc.symbol}...")
    trader.start_trading_pair(opp_btc)

    # Process opportunity
    print(f"Processing opportunity...")
    result = trader.process_opportunity(opp_btc)
    print(f"Trade executed: {result}")

    # Get statistics
    stats = trader.get_statistics()
    print(f"\nStatistics:")
    print(f"  Active pairs: {stats['total_pairs']}")
    print(f"  Total trades: {stats['total_trades']}")
    print(f"  Total profit: ${stats['total_profit']:.2f}")

    for symbol, session_stats in stats['sessions'].items():
        print(f"  {symbol}: {session_stats['trades']} trades, ${session_stats['profit']:.2f} profit")


if __name__ == "__main__":
    main()
