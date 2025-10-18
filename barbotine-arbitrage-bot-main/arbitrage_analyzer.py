#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Arbitrage Opportunity Analyzer
Analyzes cross-exchange price differences for arbitrage opportunities
"""

from dataclasses import dataclass
from typing import List, Dict
from collections import defaultdict


@dataclass
class ArbitrageOpportunity:
    """
    Represents a cross-exchange arbitrage opportunity

    Attributes:
        symbol: Trading pair symbol (e.g., "BTC/USDT")
        min_ask: Lowest ask price (where to buy)
        min_ask_exchange: Exchange with lowest ask
        max_bid: Highest bid price (where to sell)
        max_bid_exchange: Exchange with highest bid
        profit_pct: Profit percentage ((max_bid - min_ask) / min_ask * 100)
        exchange_count: Number of exchanges trading this pair
    """
    symbol: str
    min_ask: float
    min_ask_exchange: str
    max_bid: float
    max_bid_exchange: str
    profit_pct: float
    exchange_count: int


class ArbitrageAnalyzer:
    """
    Analyzes spread data to find cross-exchange arbitrage opportunities

    Strategy:
    - For each symbol, find the exchange with lowest ask (buy here)
    - Find the exchange with highest bid (sell here)
    - Calculate potential profit percentage
    - Sort by profitability
    """

    def analyze(self, spread_data: List[Dict]) -> List[ArbitrageOpportunity]:
        """
        Analyze spread data for arbitrage opportunities

        Args:
            spread_data: List of spread records from WebSocket
                [
                    {"symbol": "BTC/USDT", "exchange": "Binance", "bestBid": 42500, "bestAsk": 42501},
                    {"symbol": "BTC/USDT", "exchange": "BingX", "bestBid": 42510, "bestAsk": 42511},
                    ...
                ]

        Returns:
            List of ArbitrageOpportunity objects sorted by profit_pct (descending)
        """
        # Group data by symbol
        symbol_data = defaultdict(list)
        for record in spread_data:
            symbol = record.get('symbol')
            if symbol:
                symbol_data[symbol].append(record)

        # Find opportunities for each symbol
        opportunities = []

        for symbol, records in symbol_data.items():
            if len(records) < 2:
                # Need at least 2 exchanges for arbitrage
                continue

            # Find min ask and max bid across all exchanges
            min_ask = float('inf')
            min_ask_exchange = None
            max_bid = float('-inf')
            max_bid_exchange = None

            for record in records:
                ask = record.get('bestAsk')
                bid = record.get('bestBid')
                exchange = record.get('exchange')

                if ask is not None and ask < min_ask:
                    min_ask = ask
                    min_ask_exchange = exchange

                if bid is not None and bid > max_bid:
                    max_bid = bid
                    max_bid_exchange = exchange

            # Check if arbitrage is possible
            if min_ask == float('inf') or max_bid == float('-inf'):
                continue

            if min_ask >= max_bid:
                # No profit possible
                continue

            # Calculate profit percentage
            profit_pct = (max_bid - min_ask) / min_ask * 100

            opportunity = ArbitrageOpportunity(
                symbol=symbol,
                min_ask=min_ask,
                min_ask_exchange=min_ask_exchange,
                max_bid=max_bid,
                max_bid_exchange=max_bid_exchange,
                profit_pct=profit_pct,
                exchange_count=len(records)
            )

            opportunities.append(opportunity)

        # Sort by profitability (descending)
        opportunities.sort(key=lambda x: x.profit_pct, reverse=True)

        return opportunities


# Example usage / demonstration
def main():
    """Example usage of ArbitrageAnalyzer"""
    print("=== Arbitrage Analyzer Demo ===\n")

    # Sample spread data
    spread_data = [
        {"symbol": "BTC/USDT", "exchange": "Binance", "bestBid": 42500, "bestAsk": 42501},
        {"symbol": "BTC/USDT", "exchange": "BingX", "bestBid": 42510, "bestAsk": 42511},
        {"symbol": "ETH/USDT", "exchange": "Binance", "bestBid": 2250, "bestAsk": 2251},
        {"symbol": "ETH/USDT", "exchange": "BingX", "bestBid": 2260, "bestAsk": 2261},
        {"symbol": "SOL/USDT", "exchange": "Binance", "bestBid": 100, "bestAsk": 100.5},
        {"symbol": "SOL/USDT", "exchange": "BingX", "bestBid": 101, "bestAsk": 101.5},
    ]

    analyzer = ArbitrageAnalyzer()
    opportunities = analyzer.analyze(spread_data)

    print(f"Found {len(opportunities)} arbitrage opportunities:\n")

    for opp in opportunities:
        print(f"{opp.symbol}:")
        print(f"  Buy at {opp.min_ask_exchange}: ${opp.min_ask:.2f}")
        print(f"  Sell at {opp.max_bid_exchange}: ${opp.max_bid:.2f}")
        print(f"  Profit: {opp.profit_pct:.3f}%")
        print(f"  Exchanges: {opp.exchange_count}")
        print()


if __name__ == "__main__":
    main()
