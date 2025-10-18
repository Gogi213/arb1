#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Multi-Pair Trading Engine V2 (Fake Money Mode)
Supports 4 exchanges with multi-directional arbitrage
"""

import time
from dataclasses import dataclass
from typing import Dict, List, Optional
from balance_manager import BalanceManager
from arbitrage_analyzer import MultiExchangeArbitrageAnalyzer, DirectionalOpportunity


@dataclass
class TradingSession:
    """
    Represents an active trading session for a specific pair

    Attributes:
        symbol: Trading pair symbol
        exchanges: List of exchanges for this pair (max 4)
        last_trade_time: Timestamp of last executed trade
        trade_count: Number of trades executed
        total_profit: Cumulative profit in USDT
        last_direction: Last trade direction (for logging)
    """
    symbol: str
    exchanges: List[str]
    last_trade_time: float
    trade_count: int
    total_profit: float
    last_direction: str = ""  # e.g. "Bybit→Binance"


class MultiPairTrader:
    """
    Multi-directional arbitrage trader for up to 4 exchanges

    Key features:
    - Searches for arbitrage in ALL directions between exchanges
    - Automatically selects direction based on balances
    - No forced rebalancing - waits for natural market reversals
    - Supports up to 4 exchanges per pair
    """

    def __init__(
        self,
        balance_manager: BalanceManager,
        mode: str = 'fake-money',
        min_trade_interval: float = 1.0,
        trade_amount_pct: float = 0.1,
        max_exchanges: int = 4
    ):
        """
        Initialize multi-directional trader

        Args:
            balance_manager: BalanceManager instance
            mode: Trading mode ('fake-money' or 'real')
            min_trade_interval: Minimum seconds between trades on same pair
            trade_amount_pct: Percentage of crypto balance to trade per opportunity
            max_exchanges: Maximum number of exchanges to use (default 4)
        """
        if mode not in ['fake-money', 'real']:
            raise ValueError(f"Invalid mode: {mode}")

        self.balance_manager = balance_manager
        self.mode = mode
        self.min_trade_interval = min_trade_interval
        self.trade_amount_pct = trade_amount_pct
        self.max_exchanges = max_exchanges

        # Arbitrage analyzer
        self.arbitrage_analyzer = MultiExchangeArbitrageAnalyzer(max_exchanges=max_exchanges)

        # Active trading sessions: {symbol: TradingSession}
        self.active_sessions: Dict[str, TradingSession] = {}

        # Fee structure (default 0.1% maker/taker)
        self.default_fees = {'base': 0.001, 'quote': 0.001}

    def start_trading_pair(
        self,
        symbol: str,
        exchanges: List[str],
        spread_data: List[Dict]
    ) -> bool:
        """
        Start trading a new pair with multiple exchanges

        Args:
            symbol: Trading pair symbol
            exchanges: List of exchanges (max 4)
            spread_data: Current spread data to get real ask prices per exchange

        Returns:
            True if pair started successfully, False otherwise
        """
        if symbol in self.active_sessions:
            return False  # Already trading

        if len(exchanges) > self.max_exchanges:
            exchanges = exchanges[:self.max_exchanges]  # Limit to max

        if self.balance_manager.available_usdt <= 0:
            return False  # No capital

        # Allocate capital for this pair
        try:
            allocation = self.balance_manager.allocate_for_pair(symbol, exchanges)
        except Exception as e:
            print(f"Failed to allocate capital for {symbol}: {e}")
            return False

        # Filter spread_data for this symbol
        symbol_data = [r for r in spread_data if r.get('symbol') == symbol]

        # Create exchange -> ask price mapping
        exchange_asks = {}
        for record in symbol_data:
            ex = record.get('exchange')
            ask = record.get('bestAsk')
            if ex and ask:
                exchange_asks[ex] = ask

        # Initialize crypto positions (90% of USDT → crypto)
        # Buy at REAL ask price on each exchange
        usdt_per_exchange = allocation[exchanges[0]]['usdt']

        for exchange in exchanges:
            # Get real ask price for this exchange
            ask_price = exchange_asks.get(exchange)
            if not ask_price:
                # Fallback to average if exchange not found
                ask_price = sum(exchange_asks.values()) / len(exchange_asks) if exchange_asks else 0

            if ask_price <= 0:
                print(f"Warning: No valid ask price for {exchange}, skipping initialization")
                continue

            crypto_amount = usdt_per_exchange * 0.9 / ask_price  # 90% in crypto
            self.balance_manager.set_initial_crypto(symbol, exchange, crypto_amount, ask_price)

        # Create trading session
        session = TradingSession(
            symbol=symbol,
            exchanges=exchanges,
            last_trade_time=0,
            trade_count=0,
            total_profit=0.0,
            last_direction=""
        )

        self.active_sessions[symbol] = session
        return True

    def process_opportunity(
        self,
        symbol: str,
        spread_data: List[Dict]
    ) -> Optional[Dict]:
        """
        Process arbitrage opportunities for a symbol

        Args:
            symbol: Trading pair symbol
            spread_data: Spread data from WebSocket

        Returns:
            Trade result dict or None if no trade executed
        """
        # Check if we're trading this pair
        if symbol not in self.active_sessions:
            return None

        session = self.active_sessions[symbol]

        # Check cooldown
        time_since_last_trade = time.time() - session.last_trade_time
        if time_since_last_trade < self.min_trade_interval:
            return None  # Still in cooldown

        # Get current balances
        try:
            balances = self.balance_manager.get_pair_balances(symbol)
        except KeyError:
            return None  # Pair not allocated

        # Find ALL opportunities in ALL directions
        opportunities = self.arbitrage_analyzer.find_all_opportunities(
            spread_data,
            balances,
            min_profit_pct=0.3  # 0.3% minimum after fees
        )

        if not opportunities:
            return None  # No profitable opportunities

        # Get best executable opportunity
        best_opp = opportunities[0]  # Already sorted by profit

        # Calculate trade amount based on available crypto on sell exchange
        sell_balance = balances[best_opp.sell_exchange]['crypto']
        trade_amount = sell_balance * self.trade_amount_pct

        if trade_amount <= 0:
            return None  # No crypto to sell

        # Execute trade
        fees = {
            best_opp.buy_exchange: self.default_fees,
            best_opp.sell_exchange: self.default_fees
        }

        try:
            self.balance_manager.execute_trade(
                symbol=symbol,
                buy_exchange=best_opp.buy_exchange,
                sell_exchange=best_opp.sell_exchange,
                amount=trade_amount,
                buy_price=best_opp.buy_price,
                sell_price=best_opp.sell_price,
                fees=fees
            )

            # Calculate actual profit
            buy_cost = trade_amount * best_opp.buy_price * (1 + self.default_fees['quote'])
            sell_proceeds = trade_amount * best_opp.sell_price * (1 - self.default_fees['quote'])
            net_profit = sell_proceeds - buy_cost

            # Update session
            session.last_trade_time = time.time()
            session.trade_count += 1
            session.total_profit += net_profit
            session.last_direction = f"{best_opp.buy_exchange}→{best_opp.sell_exchange}"

            return {
                'success': True,
                'direction': session.last_direction,
                'amount': trade_amount,
                'buy_price': best_opp.buy_price,
                'sell_price': best_opp.sell_price,
                'profit': net_profit,
                'profit_pct': best_opp.profit_pct
            }

        except Exception as e:
            print(f"Trade failed for {symbol}: {e}")
            return None

    def get_balance_status(self, symbol: str) -> Optional[Dict]:
        """
        Get balance status for a trading pair

        Returns:
            Dict with balance info or None if pair not active
        """
        if symbol not in self.active_sessions:
            return None

        try:
            balances = self.balance_manager.get_pair_balances(symbol)
        except KeyError:
            return None

        session = self.active_sessions[symbol]

        status = {
            'symbol': symbol,
            'exchanges': session.exchanges,
            'balances': {},
            'can_trade_directions': []
        }

        # Analyze which directions are tradeable
        for buy_ex in session.exchanges:
            for sell_ex in session.exchanges:
                if buy_ex == sell_ex:
                    continue

                has_usdt = balances[buy_ex]['usdt'] >= 10
                has_crypto = balances[sell_ex]['crypto'] >= 0.001

                if has_usdt and has_crypto:
                    status['can_trade_directions'].append(f"{buy_ex}→{sell_ex}")

            status['balances'][buy_ex] = {
                'usdt': round(balances[buy_ex]['usdt'], 2),
                'crypto': round(balances[buy_ex]['crypto'], 8)
            }

        return status

    def get_statistics(self) -> Dict:
        """
        Get overall trading statistics

        Returns:
            Dictionary with stats
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
                    'profit': session.total_profit,
                    'last_direction': session.last_direction,
                    'exchanges': session.exchanges
                }
                for symbol, session in self.active_sessions.items()
            }
        }

    def stop_trading_pair(self, symbol: str, current_price: float) -> bool:
        """
        Stop trading a pair and deallocate capital

        Args:
            symbol: Trading pair to stop
            current_price: Current price for liquidation

        Returns:
            True if stopped successfully
        """
        if symbol not in self.active_sessions:
            return False

        # Remove session
        del self.active_sessions[symbol]

        # Deallocate capital
        try:
            self.balance_manager.deallocate_pair(symbol, current_price)
        except KeyError:
            pass

        return True
