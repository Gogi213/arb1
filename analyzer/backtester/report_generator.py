#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Generates a summary report from the test results.
"""

from typing import Dict, Optional
import logging

class ReportGenerator:
    """
    Formats and prints the final statistics report.
    """
    def __init__(self, results: Dict, logger: Optional[logging.Logger] = None):
        self.results = results
        self.logger = logger if logger else logging.getLogger(__name__)

    def generate(self):
        """
        Prints the formatted report to the logger.
        """
        if not self.results or not self.results.get("total_trades", 0):
            self.logger.info("No successful arbitrage opportunities were found during the backtest.")
            return

        self.logger.info("\n" + "="*70)
        self.logger.info(" " * 22 + "Backtest Final Report")
        self.logger.info("="*70 + "\n")

        self.logger.info(f"Total Simulated Trades: {self.results.get('total_trades', 0)}")
        self.logger.info(f"Total Estimated Profit: {self.results.get('total_profit', 0):.4f} USDT\n")

        self._print_results_by_symbol()
        
        self.logger.info("="*70)
        self.logger.info(" " * 27 + "End of Report")
        self.logger.info("="*70)

    def _print_results_by_symbol(self):
        """Prints the detailed results grouped by symbol."""
        by_symbol_results = self.results.get("by_symbol", {})

        if not by_symbol_results:
            self.logger.info("No data to display.")
            return

        for symbol, data in by_symbol_results.items():
            self.logger.info("-" * 70)
            self.logger.info(f"Symbol: {symbol}")
            self.logger.info("  --- Performance by Profit Threshold (Independent Simulations) ---")
            
            by_threshold = data.get("by_threshold", {})
            if not by_threshold:
                self.logger.info("    No trades recorded for this symbol.")
                continue

            # Find the best performing threshold for this symbol
            best_threshold = max(by_threshold, key=lambda k: by_threshold[k]['profit'])
            
            for threshold, stats in by_threshold.items():
                is_best = " (Best)" if threshold == best_threshold else ""
                self.logger.info(f"    - Threshold >= {threshold}%: {stats['profit']:.4f} USDT from {stats['trades']} trades{is_best}")
                
                pair_counts = stats.get("pair_counts", {})
                if pair_counts:
                    for pair, count in pair_counts.items():
                        self.logger.info(f"      - {pair[0]}-{pair[1]}: {count} trades")

            self.logger.info("-" * 70 + "\n")
