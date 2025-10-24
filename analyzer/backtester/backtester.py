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
from .data_loader import load_market_data
from .balance_manager import BalanceManager
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
        self.commission_pct = 0.02
        self.profit_thresholds = [0.1, 0.25, 0.3, 0.35, 0.4, 0.5]
        self.stats_collector = StatisticsCollector()
        self.balance_managers: Dict[str, Dict[float, BalanceManager]] = {}

    def run(self):
        """
        Main entry point for the backtester.
        """
        self.system_log.info("--- Starting Backtest ---")
        
        full_data = load_market_data(
            data_path=self.data_path,
            start_date=self.start_date,
            end_date=self.end_date,
            exchanges=self.exchanges,
            symbols=self.symbols,
            logger=self.system_log
        )
        
        if full_data.is_empty():
            self.system_log.error("No data loaded for the specified criteria. Exiting.")
            return

        # If no symbols or exchanges were provided, use all unique ones from the loaded data.
        if not self.symbols:
            self.system_log.info("No symbols specified, using all unique symbols from the dataset.")
            self.symbols = full_data.select('symbol').unique().to_series().to_list()
            self.system_log.info(f"Found {len(self.symbols)} symbols.")
        
        if not self.exchanges:
            self.system_log.info("No exchanges specified, using all unique exchanges from the dataset.")
            self.exchanges = full_data.select('exchange').unique().to_series().to_list()
            self.system_log.info(f"Found {len(self.exchanges)} exchanges.")

        self.system_log.info("Initializing simulation instances for each coin and threshold...")

        # Correctly find the first price for each symbol/exchange pair to set up initial assets.
        # This is crucial for ensuring all managers are properly initialized.
        self.system_log.info("Finding first price for each symbol/exchange pair...")
        initial_prices_df = full_data.group_by(["symbol", "exchange"]).first()
        initial_prices_list = initial_prices_df.to_dicts()
        self.system_log.info(f"Found {len(initial_prices_list)} initial price points.")

        for symbol in self.symbols:
            self.balance_managers[symbol] = {}
            
            # Filter the initial prices for the current symbol to speed up setup
            symbol_initial_prices = [p for p in initial_prices_list if p['symbol'] == symbol]

            template_bm = BalanceManager(
                exchanges=self.exchanges,
                symbol=symbol,
                system_log=self.system_log,
                trade_log=self.trade_log
            )
            template_bm.setup_initial_assets(symbol_initial_prices)
            
            for threshold in self.profit_thresholds:
                import copy
                self.balance_managers[symbol][threshold] = copy.deepcopy(template_bm)
        
        self.system_log.info(f"Initialized {len(self.symbols) * len(self.profit_thresholds)} simulation instances.")

        self._processing_loop(full_data)
        
        self.system_log.info("Backtest finished. Generating report...")
        report_generator = ReportGenerator(self.stats_collector.get_results(), logger=self.summary_log)
        report_generator.generate()
        self.system_log.info("--- Backtest Completed ---")

    def _processing_loop(self, data: pl.DataFrame):
        """
        Processes data by finding opportunities and simulating trades for each instance.
        """
        self.system_log.info("Starting vectorized processing loop...")

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
                self.system_log.info(f"--- Running simulation for {symbol} @ {threshold}% ---")
                balance_manager = self.balance_managers[symbol][threshold]
                
                valid_opportunities = opportunities_df.filter(pl.col('profit_pct') >= threshold).sort('timestamp')
                
                if valid_opportunities.is_empty():
                    self.system_log.info("No valid opportunities found for this simulation.")
                    continue

                self.system_log.debug(f"Found {len(valid_opportunities)} opportunities. Simulating trades...")
                
                self._simulate_trades_for_instance(valid_opportunities.to_dicts(), balance_manager, symbol, threshold)

    def _simulate_trades_for_instance(self, opportunities: List[Dict], balance_manager: BalanceManager, symbol: str, threshold: float):
        """
        Iterates through chronological opportunities, executing them if balances permit.
        """
        trade_count = 0
        for opp in opportunities:
            buy_wallet = balance_manager.get_balance(opp['buy_exchange'])
            sell_wallet = balance_manager.get_balance(opp['sell_exchange'])
            base_currency = symbol[:-4]

            max_buy_crypto = buy_wallet.get('USDT', 0) / opp['buy_price'] if opp['buy_price'] > 0 else 0
            max_sell_crypto = sell_wallet.get(base_currency, 0)
            
            trade_amount_crypto = min(max_buy_crypto, max_sell_crypto)
            
            if trade_amount_crypto < 1e-9:
                continue

            trade_value_usdt = trade_amount_crypto * opp['buy_price']
            
            success = balance_manager.simulate_trade(
                buy_exchange=opp['buy_exchange'],
                sell_exchange=opp['sell_exchange'],
                symbol=symbol,
                buy_price=opp['buy_price'],
                sell_price=opp['sell_price'],
                amount_crypto=trade_amount_crypto,
                commission_pct=self.commission_pct
            )

            if success:
                trade_count += 1
                self.stats_collector.record_trade(
                    profit_threshold=threshold,
                    symbol=symbol,
                    buy_exchange=opp['buy_exchange'],
                    sell_exchange=opp['sell_exchange'],
                    net_profit_pct=opp['profit_pct'],
                    amount_usdt=trade_value_usdt
                )
        self.system_log.info(f"Simulated {trade_count} trades for {symbol} @ {threshold}%.")

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