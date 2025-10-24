#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Main entry point for running a backtest on a partitioned dataset.
"""

import argparse
import logging
from datetime import datetime
from backtester.backtester import Backtester

def main():
    """
    Parses command-line arguments and runs the backtest.
    """
    parser = argparse.ArgumentParser(
        description="Run an arbitrage backtest on a partitioned Parquet dataset."
    )
    parser.add_argument(
        "--data-path",
        type=str,
        default="data/market_data",
        help="Root path to the partitioned 'market_data' directory."
    )
    parser.add_argument(
        "--start-date",
        type=str,
        default=None,
        help="Start date for the backtest in YYYY-MM-DD format. (Optional)"
    )
    parser.add_argument(
        "--end-date",
        type=str,
        default=None,
        help="End date for the backtest in YYYY-MM-DD format. (Optional)"
    )
    parser.add_argument(
        "--exchanges",
        type=str,
        default=None,
        help="Comma-separated list of exchanges. (Optional)"
    )
    parser.add_argument(
        "--symbols",
        type=str,
        default=None,
        help="Comma-separated list of symbols. (Optional)"
    )
    args = parser.parse_args()

    # --- Parse arguments ---
    start_date = datetime.strptime(args.start_date, "%Y-%m-%d") if args.start_date else None
    end_date = datetime.strptime(args.end_date, "%Y-%m-%d") if args.end_date else None
    exchanges = [e.strip() for e in args.exchanges.split(',')] if args.exchanges else []
    symbols = [s.strip() for s in args.symbols.split(',')] if args.symbols else []

    backtester = None
    try:
        backtester = Backtester(
            data_path=args.data_path,
            start_date=start_date,
            end_date=end_date,
            exchanges=exchanges,
            symbols=symbols
        )
        backtester.system_log.info("--- Backtest Configuration ---")
        backtester.system_log.info(f"Data Path: {args.data_path}")
        backtester.system_log.info(f"Date Range: {args.start_date} to {args.end_date}")
        backtester.system_log.info(f"Exchanges: {exchanges}")
        backtester.system_log.info(f"Symbols: {symbols}")
        backtester.system_log.info("--------------------------")
        
        backtester.run()
        
        backtester.system_log.info("Backtest script finished successfully.")
    except Exception as e:
        log = backtester.system_log if backtester else logging.getLogger(__name__)
        log.error(f"A critical error occurred: {e}", exc_info=True)

if __name__ == "__main__":
    main()