#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Multi-Exchange Arbitrage Engine
Supports 4 exchanges with multi-directional arbitrage search
"""

from dataclasses import dataclass
from typing import List, Dict, Optional, Tuple


@dataclass
class DirectionalOpportunity:
    """
    Arbitrage opportunity with specific direction between two exchanges

    Attributes:
        symbol: Trading pair symbol
        buy_exchange: Exchange to buy from
        sell_exchange: Exchange to sell to
        buy_price: Ask price on buy exchange
        sell_price: Bid price on sell exchange
        profit_pct: Profit percentage
        exchange_count: Total number of exchanges trading this pair
    """
    symbol: str
    buy_exchange: str
    sell_exchange: str
    buy_price: float
    sell_price: float
    profit_pct: float
    exchange_count: int


class MultiExchangeArbitrageAnalyzer:
    """
    Analyzes arbitrage opportunities across multiple exchanges in ALL directions

    Key features:
    - Supports up to 4 exchanges
    - Finds opportunities in ALL directions (A→B, B→A, A→C, etc.)
    - Filters by balance availability
    - Prioritizes most profitable opportunities
    """

    def __init__(self, max_exchanges: int = 4):
        """
        Initialize analyzer

        Args:
            max_exchanges: Maximum number of exchanges to consider (default 4)
        """
        self.max_exchanges = max_exchanges

    def find_all_opportunities(
        self,
        spread_data: List[Dict],
        balances: Dict[str, Dict[str, float]],
        min_profit_pct: float = 0.5
    ) -> List[DirectionalOpportunity]:
        """
        Find ALL arbitrage opportunities across exchanges in ALL directions

        Args:
            spread_data: List of spread records from WebSocket
            balances: Current balances {exchange: {'usdt': float, 'crypto': float}}
            min_profit_pct: Minimum profit percentage threshold

        Returns:
            List of DirectionalOpportunity sorted by profit_pct (descending)

        Example:
            spread_data = [
                {"symbol": "BTC/USDT", "exchange": "Bybit", "bestBid": 42500, "bestAsk": 42501},
                {"symbol": "BTC/USDT", "exchange": "Binance", "bestBid": 42510, "bestAsk": 42511},
                {"symbol": "BTC/USDT", "exchange": "MEXC", "bestBid": 42520, "bestAsk": 42521},
                {"symbol": "BTC/USDT", "exchange": "BingX", "bestBid": 42505, "bestAsk": 42506},
            ]

            balances = {
                'Bybit': {'usdt': 1000, 'crypto': 0.5},
                'Binance': {'usdt': 500, 'crypto': 0.2},
                'MEXC': {'usdt': 2000, 'crypto': 0.1},
                'BingX': {'usdt': 100, 'crypto': 0.8}
            }
        """
        # Group data by symbol
        symbol_data = {}
        for record in spread_data:
            symbol = record.get('symbol')
            if symbol:
                if symbol not in symbol_data:
                    symbol_data[symbol] = []
                symbol_data[symbol].append(record)

        opportunities = []

        for symbol, records in symbol_data.items():
            # Limit to max_exchanges
            if len(records) > self.max_exchanges:
                # Sort by volume or another metric, for now just take first N
                records = records[:self.max_exchanges]

            # Find opportunities between ALL pairs of exchanges
            for i, record_a in enumerate(records):
                exchange_a = record_a.get('exchange')
                ask_a = record_a.get('bestAsk')
                bid_a = record_a.get('bestBid')

                if not exchange_a or ask_a is None or bid_a is None:
                    continue

                for j, record_b in enumerate(records):
                    if i == j:
                        continue  # Skip same exchange

                    exchange_b = record_b.get('exchange')
                    ask_b = record_b.get('bestAsk')
                    bid_b = record_b.get('bestBid')

                    if not exchange_b or ask_b is None or bid_b is None:
                        continue

                    # Check opportunity: Buy on A, Sell on B
                    if ask_a < bid_b:
                        profit_pct = (bid_b - ask_a) / ask_a * 100

                        if profit_pct >= min_profit_pct:
                            # Check if we have balances to execute this
                            can_execute = self._can_execute_trade(
                                exchange_a, exchange_b, balances
                            )

                            if can_execute:
                                opportunities.append(DirectionalOpportunity(
                                    symbol=symbol,
                                    buy_exchange=exchange_a,
                                    sell_exchange=exchange_b,
                                    buy_price=ask_a,
                                    sell_price=bid_b,
                                    profit_pct=profit_pct,
                                    exchange_count=len(records)
                                ))

        # Sort by profitability (descending)
        opportunities.sort(key=lambda x: x.profit_pct, reverse=True)

        return opportunities

    def _can_execute_trade(
        self,
        buy_exchange: str,
        sell_exchange: str,
        balances: Dict[str, Dict[str, float]],
        min_usdt: float = 10.0,
        min_crypto: float = 0.001
    ) -> bool:
        """
        Check if we have sufficient balances to execute a trade

        Args:
            buy_exchange: Exchange to buy from
            sell_exchange: Exchange to sell to
            balances: Current balances
            min_usdt: Minimum USDT required to buy
            min_crypto: Minimum crypto required to sell

        Returns:
            True if trade can be executed, False otherwise
        """
        # Check if exchanges exist in balances
        if buy_exchange not in balances or sell_exchange not in balances:
            return False

        # Check if we have USDT to buy
        has_usdt = balances[buy_exchange].get('usdt', 0) >= min_usdt

        # Check if we have crypto to sell
        has_crypto = balances[sell_exchange].get('crypto', 0) >= min_crypto

        return has_usdt and has_crypto

    def get_opportunity_matrix(
        self,
        spread_data: List[Dict],
        symbol: str
    ) -> Dict[Tuple[str, str], float]:
        """
        Get profit matrix for all exchange pairs

        Returns:
            Dictionary {(buy_exchange, sell_exchange): profit_pct}

        Example output:
            {
                ('Bybit', 'Binance'): 0.023,   # Buy Bybit, Sell Binance = +0.023%
                ('Binance', 'Bybit'): -0.015,  # Buy Binance, Sell Bybit = -0.015% (loss)
                ('Bybit', 'MEXC'): 0.045,
                ('MEXC', 'Bybit'): -0.030,
                ...
            }
        """
        # Filter records for this symbol
        records = [r for r in spread_data if r.get('symbol') == symbol]

        matrix = {}

        for record_a in records:
            exchange_a = record_a.get('exchange')
            ask_a = record_a.get('bestAsk')

            if not exchange_a or ask_a is None:
                continue

            for record_b in records:
                if record_a == record_b:
                    continue

                exchange_b = record_b.get('exchange')
                bid_b = record_b.get('bestBid')

                if not exchange_b or bid_b is None:
                    continue

                # Calculate profit for this direction
                profit_pct = (bid_b - ask_a) / ask_a * 100
                matrix[(exchange_a, exchange_b)] = profit_pct

        return matrix

    def get_best_direction_for_balance(
        self,
        opportunities: List[DirectionalOpportunity],
        balances: Dict[str, Dict[str, float]]
    ) -> Optional[DirectionalOpportunity]:
        """
        Get the best opportunity that can be executed with current balances

        Args:
            opportunities: List of all opportunities
            balances: Current balances

        Returns:
            Best executable opportunity or None
        """
        for opp in opportunities:
            if self._can_execute_trade(opp.buy_exchange, opp.sell_exchange, balances):
                return opp

        return None
