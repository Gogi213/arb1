#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Main entry point for running a backtest on a specific data session.
"""

import argparse
from backtester.backtester import Backtester
from backtester.logger import log

def main():
    """
    Parses command-line arguments and runs the backtest.
    """
    parser = argparse.ArgumentParser(
        description="Run an arbitrage backtest on a specific historical data session."
    )
    parser.add_argument(
        "session_path",
        type=str,
        help="The relative path to the session directory (e.g., 'data/market_data/2025-10-21_01-57-39/')."
    )
    args = parser.parse_args()

    log.info(f"Starting backtest for session: {args.session_path}")

    try:
        backtester = Backtester(session_path=args.session_path)
        backtester.run()
    except Exception as e:
        log.error(f"A critical error occurred during the backtest: {e}", exc_info=True)
    
    log.info("Backtest script finished.")

if __name__ == "__main__":
    main()