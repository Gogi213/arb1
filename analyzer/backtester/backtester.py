#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Orchestrates a backtest on historical market data, testing multiple
coins and profit thresholds in isolated simulations using Polars.
"""

import polars as pl
from typing import List, Dict
from datetime import datetime
from .logger import LoggerManager
from .data_loader import stream_market_data
from arbitrage_analyzer import MultiExchangeArbitrageAnalyzer
from .statistics_collector import StatisticsCollector
from .report_generator import ReportGenerator

class Backtester:
    """
    Orchestrates a backtest on historical market data.
    """
    def __init__(self, data_path: str, start_date: datetime, end_date: datetime, exchanges: List[str], symbols: List[str]):
        self.data_path = data_path
        self.start_date = start_date
        self.end_date = end_date
        self.exchanges = exchanges
        self.symbols = symbols
        
        self.log_manager = LoggerManager(session_name="backtest")
        self.system_log = self.log_manager.get_logger('system')
        self.summary_log = self.log_manager.get_logger('summary')
        self.trade_log = self.log_manager.get_logger('trade')

        self.analyzer = MultiExchangeArbitrageAnalyzer(logger=self.system_log)
        self.commission_pct = 0.2  # 0.1% buy + 0.1% sell = 0.2% total
        self.profit_thresholds = [0.25, 0.3, 0.35, 0.4]
        self.stats_collector = StatisticsCollector()
        
        # --- Simulation State ---
        self.initial_usdt_balance = 1000.0
        self.initial_crypto_balance = 1.0

    def run(self):
        """
        Main entry point for the backtester.
        """
        self.system_log.info("--- Starting Backtest ---")
        
        data_stream = stream_market_data(
            data_path=self.data_path,
            start_date=self.start_date,
            end_date=self.end_date,
            exchanges=self.exchanges,
            symbols=self.symbols,
            logger=self.system_log
        )

        is_first_chunk = True
        for daily_data in data_stream:
            if daily_data.is_empty():
                continue

            if is_first_chunk:
                if not self.symbols:
                    self.symbols = daily_data.select('symbol').unique().to_series().to_list()
                if not self.exchanges:
                    self.exchanges = daily_data.select('exchange').unique().to_series().to_list()
                is_first_chunk = False

            self._processing_loop(daily_data)
        
        if is_first_chunk:
            self.system_log.error("No data was loaded for the specified criteria. Exiting.")
            return

        self.system_log.info("Backtest finished. Generating report...")
        report_generator = ReportGenerator(self.stats_collector.get_results(), logger=self.summary_log)
        report_generator.generate()
        self.system_log.info("--- Backtest Completed ---")

    def _run_vectorized_simulation(self, opportunities_df: pl.DataFrame, symbol: str, threshold: float):
        """
        Runs a simulation with balanced trades between exchange pairs (communicating vessels).

        Strategy:
        - Fixed budget: 1000 USDT split 50/50 per exchange pair
        - Trades alternate direction: A→B, then B→A, then A→B, etc.
        - This keeps balances balanced between pairs like communicating vessels
        - Each trade uses 500 USDT
        - Tests ALL exchange pairs for this symbol at this profit threshold
        """
        # Filter for valid opportunities above threshold
        valid_opps = opportunities_df.filter(pl.col('profit_pct') >= threshold).sort('timestamp')
        if valid_opps.is_empty():
            return

        # Create exchange pair identifier (sorted to treat A→B and B→A as same pair)
        valid_opps = valid_opps.with_columns([
            pl.min_horizontal(pl.col("buy_exchange"), pl.col("sell_exchange")).alias("exchange_min"),
            pl.max_horizontal(pl.col("buy_exchange"), pl.col("sell_exchange")).alias("exchange_max")
        ]).with_columns([
            (pl.col("exchange_min") + "_" + pl.col("exchange_max")).alias("exchange_pair")
        ])

        # Track last direction for each pair to alternate
        pair_last_direction = {}
        executed_trades = []

        trade_size_usdt = 500.0

        # Process ALL opportunities chronologically (no filtering by timestamp)
        # Each exchange pair is independent and tracks its own alternating direction
        for row in valid_opps.to_dicts():
            pair = row["exchange_pair"]
            buy_ex = row["buy_exchange"]
            sell_ex = row["sell_exchange"]

            # Current direction: which exchange is buying
            current_direction = f"{buy_ex}→{sell_ex}"

            # Check if we can execute this trade
            if pair not in pair_last_direction:
                # First trade for this pair - allow it
                pair_last_direction[pair] = current_direction
                executed_trades.append(row)
            else:
                last_direction = pair_last_direction[pair]
                # Allow trade only if direction changed (alternating)
                if last_direction != current_direction:
                    pair_last_direction[pair] = current_direction
                    executed_trades.append(row)
                # Otherwise skip this trade (would unbalance the pair)

        if not executed_trades:
            return

        # Convert back to DataFrame
        trades = pl.DataFrame(executed_trades).with_columns([
            (pl.lit(trade_size_usdt) / pl.col('buy_price')).alias('trade_amount_crypto'),
            pl.lit(trade_size_usdt).alias('trade_size_usdt')
        ])

        # Record trades in bulk
        self.stats_collector.record_trades_bulk(
            profit_threshold=threshold,
            symbol=symbol,
            trades_df=trades
        )
        self.system_log.info(f"Simulation found {len(trades)} balanced trades for {symbol} @ {threshold}%.")

    def _processing_loop(self, data: pl.DataFrame):
        """
        Processes data by finding opportunities and simulating trades.
        """
        for symbol in self.symbols:
            symbol_data = data.filter(pl.col('symbol') == symbol)
            if symbol_data.is_empty():
                continue

            opportunities_df = self.analyzer.find_maker_taker_opportunities(
                spread_data=symbol_data,
                commission_pct=self.commission_pct
            )
            if opportunities_df.is_empty():
                continue

            for threshold in self.profit_thresholds:
                self._run_vectorized_simulation(opportunities_df, symbol, threshold)

def main():
    """
    Example entry point for the backtester.
    """
    from datetime import datetime

    # --- Configuration ---
    DATA_ROOT = 'data/market_data'
    # Run for all available data by default
    START = None
    END = None
    EXCHANGES = []
    SYMBOLS = []
    # ---------------------

    backtester = Backtester(
        data_path=DATA_ROOT,
        start_date=START,
        end_date=END,
        exchanges=EXCHANGES,
        symbols=SYMBOLS
    )
    backtester.run()

if __name__ == "__main__":
    main()