#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Generates a summary report from the test results.
"""

from typing import Dict
from .logger import log

class ReportGenerator:
    """
    Formats and prints the final statistics report.
    """
    def __init__(self, results: Dict):
        self.results = results

    def generate(self):
        """
        Prints the formatted report to the logger.
        """
        if not self.results or not self.results.get("total_simulated_trades", 0):
            log.info("No successful arbitrage opportunities were found during the test.")
            return

        log.info("\n" + "="*60)
        log.info(" " * 17 + "Live Test Final Report")
        log.info("="*60 + "\n")

        log.info("Model Used: Maker/Taker (Buy at Bid, Sell at Bid)")
        log.info("Method: Independent simulation for each profit threshold.\n")

        log.info(f"Total Successfully Simulated Trades (all thresholds): {self.results.get('total_simulated_trades', 0)}")

        self._print_estimated_earnings()
        self._print_top_combo()
        self._print_round_trips()
        self._print_exchange_ranking()
        self._print_coin_ranking()
        
        log.info("="*60)
        log.info(" " * 22 + "End of Report")
        log.info("="*60)

    def _print_top_combo(self):
        """Prints the most effective 3-exchange combination."""
        combo, count = self.results.get("top_3_combo", (None, 0))
        log.info("\n--- Top 3 Exchange Combination (by total trades) ---")
        if not combo:
            log.info("Not enough data to determine a top 3 combo.")
        else:
            log.info(f"Best Combo: {', '.join(combo)} with {count} total trades between them.")

    def _print_round_trips(self):
        """Prints profitable round-trip opportunities."""
        round_trips = self.results.get("round_trip_opportunities", {})
        log.info("\n--- Profitable Round-Trip Opportunities (Coin & Exchange Pair) ---")
        if not round_trips:
            log.info("No round-trip opportunities found.")
        else:
            for trip_key, data in round_trips.items():
                log.info(f"{trip_key} | Total Profit: {data['total_profit']:.4f} USDT")

    def _print_exchange_ranking(self):
        """Prints the ranking of exchange pairs."""
        ranking = self.results.get("exchange_pair_ranking", [])
        log.info("\n--- Exchange Pair Ranking (by total trades) ---")
        if not ranking:
            log.info("No data.")
        else:
            for (ex1, ex2), count in ranking:
                log.info(f"{ex1}-{ex2}: {count} trades")

    def _print_coin_ranking(self):
        """Prints the ranking of coins."""
        ranking = self.results.get("coin_ranking", [])
        log.info("\n--- Coin Ranking (by total trades) ---")
        if not ranking:
            log.info("No data.")
        else:
            for symbol, count in ranking:
                log.info(f"{symbol}: {count} trades")

    def _print_estimated_earnings(self):
        """Prints the estimated earnings and trade counts per profit threshold."""
        earnings = self.results.get("estimated_earnings", {})
        trades_count = self.results.get("trades_per_threshold", {})
        log.info("\n--- Results per Profit Threshold ---")
        if not earnings:
            log.info("No trades were simulated.")
            return
        for threshold in sorted(earnings.keys()):
            profit = earnings[threshold]
            count = trades_count[threshold]
            log.info(f"Threshold >= {threshold}%: {profit:.4f} USDT from {count} trades")