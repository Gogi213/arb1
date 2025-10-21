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
        if not self.results or not self.results.get("total_trades", 0):
            log.info("No successful arbitrage opportunities were found during the backtest.")
            return

        log.info("\n" + "="*70)
        log.info(" " * 22 + "Backtest Final Report")
        log.info("="*70 + "\n")

        log.info(f"Total Simulated Trades: {self.results.get('total_trades', 0)}")
        log.info(f"Total Estimated Profit: {self.results.get('total_profit', 0):.4f} USDT\n")

        self._print_results_by_symbol()
        
        log.info("="*70)
        log.info(" " * 27 + "End of Report")
        log.info("="*70)

    def _print_results_by_symbol(self):
        """Prints the detailed results grouped by symbol."""
        by_symbol_results = self.results.get("by_symbol", {})
        if not by_symbol_results:
            log.info("No data to display.")
            return

        for symbol, data in by_symbol_results.items():
            log.info("-" * 70)
            log.info(f"Symbol: {symbol}")
            log.info(f"  Total Profit: {data['total_profit']:.4f} USDT")
            log.info(f"  Total Trades: {data['total_trades']}")
            log.info("  --- Performance by Profit Threshold ---")
            
            by_threshold = data.get("by_threshold", {})
            if not by_threshold:
                log.info("    No trades recorded for this symbol.")
                continue

            # Find the best performing threshold for this symbol
            best_threshold = max(by_threshold, key=lambda k: by_threshold[k]['profit'])
            
            for threshold, stats in by_threshold.items():
                is_best = " (Best)" if threshold == best_threshold else ""
                log.info(f"    - Threshold >= {threshold}%: {stats['profit']:.4f} USDT from {stats['trades']} trades{is_best}")
            log.info("-" * 70 + "\n")
