#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Main entry point for running a backtest on a specific data session.
"""

import argparse
import logging
from backtester.backtester import Backtester

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

    backtester = None
    try:
        # Initialize backtester and its dedicated loggers
        backtester = Backtester(session_path=args.session_path)
        backtester.system_log.info(f"Starting backtest for session: {args.session_path}")
        backtester.run()
        backtester.system_log.info("Backtest script finished.")
    except Exception as e:
        # If backtester was initialized, use its logger. Otherwise, use a basic logger.
        log = backtester.system_log if backtester else logging.getLogger(__name__)
        log.error(f"A critical error occurred: {e}", exc_info=True)

if __name__ == "__main__":
    main()