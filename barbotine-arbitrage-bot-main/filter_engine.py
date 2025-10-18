#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Filter Engine for Arbitrage Opportunities
Filters opportunities based on configurable criteria
"""

from dataclasses import dataclass
from typing import List
from arbitrage_analyzer import ArbitrageOpportunity


@dataclass
class FilterConfig:
    """
    Configuration for filtering arbitrage opportunities

    Attributes:
        min_exchanges: Minimum number of exchanges a pair must be on
        min_profit_pct: Minimum profit percentage threshold
    """
    min_exchanges: int = 3
    min_profit_pct: float = 0.5


class FilterEngine:
    """
    Filters arbitrage opportunities based on dynamic criteria

    Features:
    - Filter by minimum number of exchanges
    - Filter by minimum profit percentage
    - Dynamic config updates
    - Pass rate statistics
    """

    def __init__(self, config: FilterConfig = None):
        """
        Initialize filter engine

        Args:
            config: FilterConfig instance (uses defaults if None)
        """
        self.config = config or FilterConfig()

    def filter(self, opportunities: List[ArbitrageOpportunity]) -> List[ArbitrageOpportunity]:
        """
        Filter opportunities based on current config

        Args:
            opportunities: List of arbitrage opportunities

        Returns:
            Filtered list of opportunities that pass all criteria
        """
        return [opp for opp in opportunities if self._passes_filters(opp)]

    def _passes_filters(self, opportunity: ArbitrageOpportunity) -> bool:
        """
        Check if opportunity passes all filters

        Args:
            opportunity: Arbitrage opportunity to check

        Returns:
            True if passes all filters, False otherwise
        """
        # Check exchange count
        if opportunity.exchange_count < self.config.min_exchanges:
            return False

        # Check profit percentage
        if opportunity.profit_pct < self.config.min_profit_pct:
            return False

        return True

    def update_config(self, min_exchanges: int = None, min_profit_pct: float = None) -> None:
        """
        Update filter configuration dynamically

        Args:
            min_exchanges: New minimum exchanges (None = no change)
            min_profit_pct: New minimum profit % (None = no change)
        """
        if min_exchanges is not None:
            self.config.min_exchanges = min_exchanges

        if min_profit_pct is not None:
            self.config.min_profit_pct = min_profit_pct

    def get_statistics(self, opportunities: List[ArbitrageOpportunity]) -> dict:
        """
        Get filter statistics for a list of opportunities

        Args:
            opportunities: List of opportunities to analyze

        Returns:
            Dictionary with statistics:
            - total: Total opportunities
            - passed: Number that passed filters
            - pass_rate: Percentage that passed
        """
        total = len(opportunities)
        if total == 0:
            return {'total': 0, 'passed': 0, 'pass_rate': 0.0}

        passed = len(self.filter(opportunities))
        pass_rate = (passed / total) * 100

        return {
            'total': total,
            'passed': passed,
            'pass_rate': pass_rate
        }


# Example usage / demonstration
def main():
    """Example usage of FilterEngine"""
    print("=== Filter Engine Demo ===\n")

    # Create sample opportunities
    opportunities = [
        ArbitrageOpportunity("BTC/USDT", 42500, "Binance", 42520, "BingX", 0.047, 5),
        ArbitrageOpportunity("ETH/USDT", 2250, "Binance", 2260, "BingX", 0.444, 4),
        ArbitrageOpportunity("SOL/USDT", 100, "Binance", 100.3, "BingX", 0.3, 2),  # Too few exchanges
        ArbitrageOpportunity("ADA/USDT", 0.5, "Binance", 0.501, "BingX", 0.2, 3),  # Low profit
        ArbitrageOpportunity("DOT/USDT", 7.5, "Binance", 7.55, "BingX", 0.667, 4),
    ]

    # Create filter with defaults (>= 3 exchanges, >= 0.5% profit)
    filter_engine = FilterEngine()

    print("Initial filter config:")
    print(f"  Min exchanges: {filter_engine.config.min_exchanges}")
    print(f"  Min profit %: {filter_engine.config.min_profit_pct}\n")

    # Filter opportunities
    filtered = filter_engine.filter(opportunities)

    print(f"Opportunities before filter: {len(opportunities)}")
    print(f"Opportunities after filter: {len(filtered)}\n")

    print("Passed opportunities:")
    for opp in filtered:
        print(f"  {opp.symbol}: {opp.profit_pct:.3f}% profit, {opp.exchange_count} exchanges")

    # Get statistics
    stats = filter_engine.get_statistics(opportunities)
    print(f"\nStatistics:")
    print(f"  Total: {stats['total']}")
    print(f"  Passed: {stats['passed']}")
    print(f"  Pass rate: {stats['pass_rate']:.1f}%")

    # Update filters dynamically
    print("\n--- Updating filters ---")
    filter_engine.update_config(min_exchanges=4, min_profit_pct=0.6)
    print(f"New config: >= {filter_engine.config.min_exchanges} exchanges, >= {filter_engine.config.min_profit_pct}% profit\n")

    filtered_new = filter_engine.filter(opportunities)
    print(f"Opportunities after new filter: {len(filtered_new)}")
    for opp in filtered_new:
        print(f"  {opp.symbol}: {opp.profit_pct:.3f}% profit, {opp.exchange_count} exchanges")


if __name__ == "__main__":
    main()
