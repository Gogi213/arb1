"""Arbitrage opportunity finder based on bid price differences."""
from itertools import combinations
from typing import List
from .types import MarketData, ArbitrageOpportunity
from .market_state import MarketState


class ArbitrageFinder:
    """Finds arbitrage opportunities between exchanges."""

    def __init__(self, min_profit_pct: float = 0.3):
        self.min_profit_pct = min_profit_pct

    def find_opportunities(self, market_state: MarketState) -> List[ArbitrageOpportunity]:
        """
        Find arbitrage opportunities by comparing bids across exchanges.

        Logic:
        - Buy on exchange1 at bid1 (limit order without fee)
        - Sell on exchange2 at bid2 (market order)
        - Profit if: bid2 > bid1 + min_profit_threshold
        """
        opportunities: List[ArbitrageOpportunity] = []

        # Get all active symbols
        symbols = market_state.get_all_symbols()

        for symbol in symbols:
            # Get all exchanges trading this symbol
            exchanges_data = market_state.get_by_symbol(symbol)

            # Need at least 2 exchanges for arbitrage
            if len(exchanges_data) < 2:
                continue

            # Check all pairs of exchanges
            for data1, data2 in combinations(exchanges_data, 2):
                # Calculate profit for both directions

                # Direction 1: buy on exchange1, sell on exchange2
                profit1 = self._calculate_profit(data1, data2)
                if profit1 >= self.min_profit_pct:
                    opportunities.append(self._create_opportunity(data1, data2, profit1))

                # Direction 2: buy on exchange2, sell on exchange1
                profit2 = self._calculate_profit(data2, data1)
                if profit2 >= self.min_profit_pct:
                    opportunities.append(self._create_opportunity(data2, data1, profit2))

        return opportunities

    def _calculate_profit(self, buy_data: MarketData, sell_data: MarketData) -> float:
        """
        Calculate profit percentage.

        buy_data: exchange where we buy (limit at bid)
        sell_data: exchange where we sell (market at bid)
        """
        buy_price = buy_data['bid']
        sell_price = sell_data['bid']

        if buy_price == 0:
            return 0.0

        profit_pct = ((sell_price - buy_price) / buy_price) * 100
        return profit_pct

    def _create_opportunity(
        self,
        buy_data: MarketData,
        sell_data: MarketData,
        profit_pct: float
    ) -> ArbitrageOpportunity:
        """Create ArbitrageOpportunity object."""
        return {
            'symbol': buy_data['symbol'],
            'buy_exchange': buy_data['exchange'],
            'sell_exchange': sell_data['exchange'],
            'buy_price': buy_data['bid'],
            'sell_price': sell_data['bid'],
            'profit_pct': profit_pct,
            'buy_spread_pct': buy_data['spread_pct'],
            'sell_spread_pct': sell_data['spread_pct'],
            'timestamp': buy_data['timestamp']
        }
