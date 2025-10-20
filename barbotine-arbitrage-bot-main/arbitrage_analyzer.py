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

    def find_maker_taker_opportunities(
        self,
        spread_data: List[Dict],
        balances: Dict[str, Dict[str, float]],
        commission_pct: float,
        min_profit_pct: float = 0.1
    ) -> List[DirectionalOpportunity]:
        """
        Finds Maker/Taker opportunities (Buy at Bid, Sell at Bid).
        """
        return self._find_opportunities(spread_data, balances, commission_pct, "maker")

    def _find_opportunities(
        self,
        spread_data: List[Dict],
        balances: Dict[str, Dict[str, float]],
        commission_pct: float,
        buy_model: str
    ) -> List[DirectionalOpportunity]:
        symbol_data = {}
        for record in spread_data:
            symbol = record.get('symbol')
            if symbol:
                if symbol not in symbol_data:
                    symbol_data[symbol] = []
                symbol_data[symbol].append(record)

        opportunities = []

        for symbol, records in symbol_data.items():
            if len(records) > self.max_exchanges:
                records = records[:self.max_exchanges]

            for i, record_a in enumerate(records):
                exchange_a = record_a.get('exchange')
                ask_a = record_a.get('bestAsk')
                bid_a = record_a.get('bestBid')

                if buy_model == "taker":
                    buy_price = ask_a
                else: # maker
                    buy_price = bid_a

                if not exchange_a or buy_price is None or buy_price == 0:
                    continue

                for j, record_b in enumerate(records):
                    if i == j:
                        continue

                    exchange_b = record_b.get('exchange')
                    sell_price = record_b.get('bestBid') # We always sell to the best bid (Taker)
                    
                    if not exchange_b or sell_price is None:
                        continue

                    if buy_price < sell_price:
                        gross_profit_pct = ((sell_price - buy_price) / buy_price) * 100
                        net_profit_pct = gross_profit_pct - (2 * commission_pct)

                        # For maker-taker, we can check against all thresholds
                        # For taker-taker, it's a direct check
                        if net_profit_pct > 0: # Check for any profitability before deeper checks
                            if symbol.endswith('USDT'):
                                base_currency = symbol[:-4]
                                can_execute = self._can_execute_trade(
                                    buy_wallet=balances.get(exchange_a, {}).get(symbol, {}),
                                    sell_wallet=balances.get(exchange_b, {}).get(symbol, {}),
                                    base_currency=base_currency
                                )

                                if can_execute:
                                    opportunities.append(DirectionalOpportunity(
                                        symbol=symbol,
                                        buy_exchange=exchange_a,
                                        sell_exchange=exchange_b,
                                        buy_price=buy_price,
                                        sell_price=sell_price,
                                        profit_pct=net_profit_pct,
                                        exchange_count=len(records)
                                    ))

        opportunities.sort(key=lambda x: x.profit_pct, reverse=True)
        return opportunities

    def _can_execute_trade(
        self,
        buy_wallet: Dict[str, float],
        sell_wallet: Dict[str, float],
        base_currency: str,
        min_usdt: float = 10.0
    ) -> bool:
        """
        Check if we have sufficient balances to execute a trade.
        """
        if not buy_wallet or not sell_wallet:
            return False

        has_usdt = buy_wallet.get('USDT', 0) >= min_usdt
        has_crypto = sell_wallet.get(base_currency, 0) > 0
        return has_usdt and has_crypto

    def get_opportunity_matrix(
        self,
        spread_data: List[Dict],
        symbol: str
    ) -> Dict[Tuple[str, str], float]:
        """
        Get profit matrix for all exchange pairs
        """
        records = [r for r in spread_data if r.get('symbol') == symbol]
        matrix = {}

        for record_a in records:
            exchange_a = record_a.get('exchange')
            ask_a = record_a.get('bestAsk')
            if not exchange_a or ask_a is None or ask_a == 0:
                continue

            for record_b in records:
                if record_a == record_b:
                    continue
                exchange_b = record_b.get('exchange')
                bid_b = record_b.get('bestBid')
                if not exchange_b or bid_b is None:
                    continue
                
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
        """
        for opp in opportunities:
            if opp.symbol.endswith('USDT'):
                base_currency = opp.symbol[:-4]
                if self._can_execute_trade(
                    buy_wallet=balances.get(opp.buy_exchange, {}).get(opp.symbol, {}),
                    sell_wallet=balances.get(opp.sell_exchange, {}).get(opp.symbol, {}),
                    base_currency=base_currency
                ):
                    return opp
        return None
