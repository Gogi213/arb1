#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Orchestrates a backtest on historical market data, testing multiple
coins and profit thresholds in isolated simulations using Polars.
"""

import polars as pl
from typing import List, Dict
from collections import Counter
from .logger import LoggerManager
from .data_loader import load_market_data
from .balance_manager import BalanceManager
from arbitrage_analyzer import MultiExchangeArbitrageAnalyzer
from .statistics_collector import StatisticsCollector
from .report_generator import ReportGenerator
from .stationarity_report import StationarityReport

class Backtester:
    """
    Orchestrates a backtest on historical market data, testing multiple
    coins and profit thresholds in isolated simulations using Polars.
    """
    def __init__(self, session_path: str):
        self.session_path = session_path
        
        # Setup logging for this specific backtest instance
        self.log_manager = LoggerManager(session_name="backtest")
        self.system_log = self.log_manager.get_logger('system')
        self.indicator_log = self.log_manager.get_logger('indicator')
        self.summary_log = self.log_manager.get_logger('summary')
        self.trade_log = self.log_manager.get_logger('trade')

        self.analyzer = MultiExchangeArbitrageAnalyzer(logger=self.system_log)
        self.commission_pct = 0.02
        self.profit_thresholds = [0.25, 0.3, 0.35, 0.4, 0.5]
        self.stats_collector = StatisticsCollector()
        self.balance_managers: Dict[str, Dict[float, BalanceManager]] = {}
        self.top_exchanges = []
        self.top_symbols = []

    def _analyze_initial_data(self, data: pl.DataFrame, duration_minutes: int = 30, top_n_symbols: int = 20):
        """
        Analyzes the first N minutes of data to find the most promising
        exchanges and symbols for arbitrage. This version is optimized to
        process data symbol by symbol to avoid memory overload.
        """
        self.system_log.info(f"Analyzing first {duration_minutes} minutes of data to select pools...")

        start_time = data.select(pl.min("timestamp"))[0, 0]
        end_time = start_time + pl.duration(minutes=duration_minutes)
        initial_df = data.filter(pl.col('timestamp') <= end_time)

        if initial_df.is_empty():
            self.system_log.error("No data found in the first 30 minutes. Exiting.")
            return False

        unique_symbols = initial_df.select('symbol').unique().to_series().to_list()
        self.system_log.info(f"Found {len(unique_symbols)} unique symbols in the initial period. Analyzing each...")

        all_profitable_opportunities = []

        for i, symbol in enumerate(unique_symbols):
            self.system_log.info(f"  ({i+1}/{len(unique_symbols)}) Analyzing symbol: {symbol}")
            symbol_data = initial_df.filter(pl.col('symbol') == symbol)
            
            opportunities_df = self.analyzer.find_maker_taker_opportunities(
                spread_data=symbol_data,
                commission_pct=self.commission_pct
            )

            if not opportunities_df.is_empty():
                profitable_opps = opportunities_df.filter(pl.col('profit_pct') >= self.profit_thresholds[0])
                if not profitable_opps.is_empty():
                    all_profitable_opportunities.append(profitable_opps)

        if not all_profitable_opportunities:
            self.system_log.error(f"No opportunities found meeting the minimum profit threshold of {self.profit_thresholds[0]}%. Exiting.")
            return False

        # Combine all profitable opportunities into a single DataFrame
        profitable_opportunities_df = pl.concat(all_profitable_opportunities)

        if profitable_opportunities_df.is_empty():
            self.system_log.error("Concatenated profitable opportunities DataFrame is empty. This should not happen. Exiting.")
            return False

        exchange_counts = Counter(profitable_opportunities_df["buy_exchange"].to_list() + profitable_opportunities_df["sell_exchange"].to_list())
        symbol_counts = Counter(profitable_opportunities_df["symbol"].to_list())

        self.top_exchanges = [item[0] for item in exchange_counts.most_common(3)]
        self.top_symbols = [item[0] for item in symbol_counts.most_common(top_n_symbols)]

        if len(self.top_exchanges) < 2:
            self.system_log.error("Could not identify at least 2 active exchanges. Exiting.")
            return False

        self.system_log.info(f"Selected Top 3 Exchanges: {self.top_exchanges}")
        self.system_log.info(f"Selected Top {top_n_symbols} Symbols: {self.top_symbols}")
        return True

    def run(self):
        """
        Main entry point for the backtester.
        """
        full_data = load_market_data(self.session_path, logger=self.system_log)
        if full_data.is_empty(): return

        # --- Generate Stationarity Report ---
        stationarity_reporter = StationarityReport(
            full_data=full_data,
            exchanges=['Bybit', 'Binance', 'OKX', 'GateIo', 'Kucoin'],
            log_dir=self.log_manager.logs_dir,
            logger=self.system_log
        )
        stationarity_reporter.generate_report()
        # --- End of Stationarity Report Generation ---

        # --- Use fixed list of exchanges and all symbols as per user request ---
        self.top_exchanges = ['Bybit', 'Binance', 'OKX', 'GateIo', 'Kucoin']
        self.top_symbols = full_data.select('symbol').unique().to_series().to_list()
        
        self.system_log.info(f"Using fixed exchange list: {self.top_exchanges}")
        self.system_log.info(f"Using all {len(self.top_symbols)} symbols found in the dataset.")
        # --- End of modification ---

        self.system_log.info("Initializing simulation instances for each coin and threshold...")
        
        # Use a small, recent slice of data for initial asset setup to be faster
        initial_data_df = full_data.tail(50000) # Use last 50k records as a sample
        initial_data_list = initial_data_df.to_dicts()

        for symbol in self.top_symbols:
            self.balance_managers[symbol] = {}
            # Create a single template balance manager for the symbol
            template_bm = BalanceManager(
                exchanges=self.top_exchanges,
                symbol=symbol,
                system_log=self.system_log,
                trade_log=self.trade_log
            )
            template_bm.setup_initial_assets(initial_data_list)
            
            for threshold in self.profit_thresholds:
                # Clone the template for each threshold
                import copy
                self.balance_managers[symbol][threshold] = copy.deepcopy(template_bm)
        self.system_log.info(f"Initialized {len(self.top_symbols) * len(self.profit_thresholds)} simulation instances.")

        # Test on the entire dataset
        self._processing_loop(full_data)
        
        self.system_log.info("Backtest finished. Generating report...")
        report_generator = ReportGenerator(self.stats_collector.get_results(), logger=self.summary_log)
        report_generator.generate()
        self.system_log.info("Backtest completed.")

    def _processing_loop(self, data: pl.DataFrame):
        """
        Processes data by iterating through each simulation instance (coin + threshold)
        and applying its strategy to the entire dataset.
        """
        self.system_log.info(f"Starting vectorized processing loop...")

        for symbol in self.top_symbols:
            symbol_data = data.filter(pl.col('symbol') == symbol)
            
            # Find all theoretical opportunities for this symbol across the entire dataset
            opportunities_df = self.analyzer.find_maker_taker_opportunities(
                spread_data=symbol_data,
                commission_pct=self.commission_pct
            )
            
            if opportunities_df.is_empty():
                self.system_log.debug(f"No theoretical opportunities found for {symbol}.")
                continue

            # --- DIAGNOSTIC LOGGING: Show best opportunities found, regardless of threshold ---
            top_opps = opportunities_df.sort('profit_pct', descending=True).head(5)
            if not top_opps.is_empty():
                self.system_log.debug(f"Top 5 theoretical opportunities for {symbol} (before threshold filter):")
                for row in top_opps.to_dicts():
                    self.system_log.debug(f"  -> Profit: {row['profit_pct']:.4f}%, Buy: {row['buy_exchange']}@{row['buy_price']:.6f}, Sell: {row['sell_exchange']}@{row['sell_price']:.6f}")
            # --- END DIAGNOSTIC ---

            for threshold in self.profit_thresholds:
                self.system_log.info(f"--- Running simulation for {symbol} @ {threshold}% ---")
                balance_manager = self.balance_managers[symbol][threshold]
                
                # Filter by profit threshold and sort by time
                valid_opportunities = opportunities_df.filter(pl.col('profit_pct') >= threshold).sort('timestamp')
                
                if valid_opportunities.is_empty():
                    max_profit_found = top_opps.select(pl.max('profit_pct'))[0, 0] if not top_opps.is_empty() else 0
                    self.system_log.info(f"No valid opportunities found for this simulation. Best profit found was {max_profit_found:.4f}%, which is below the {threshold}% threshold.")
                    continue

                self.system_log.debug(f"Found {len(valid_opportunities)} theoretical opportunities for {symbol} @ {threshold}%. Simulating trades chronologically...")
                
                self._simulate_trades_for_instance(valid_opportunities.to_dicts(), balance_manager, symbol, threshold)

    def _simulate_trades_for_instance(self, opportunities: List[Dict], balance_manager: BalanceManager, symbol: str, threshold: float):
        """
        Iterates through a list of chronological opportunities for a single
        simulation instance, executing them if balances permit.
        """
        trade_count = 0
        for opp in opportunities:
            buy_wallet = balance_manager.get_balance(opp['buy_exchange'])
            sell_wallet = balance_manager.get_balance(opp['sell_exchange'])
            base_currency = symbol[:-4]

            self.system_log.debug(f"--- Checking opportunity at {opp['timestamp']} for {symbol} ---")
            self.system_log.debug(f"Buy on {opp['buy_exchange']} at {opp['buy_price']}, Sell on {opp['sell_exchange']} at {opp['sell_price']}")
            self.system_log.debug(f"Balances before check: {balance_manager.get_all_balances_str()}")

            max_buy_crypto = buy_wallet.get('USDT', 0) / opp['buy_price'] if opp['buy_price'] > 0 else 0
            max_sell_crypto = sell_wallet.get(base_currency, 0)
            
            self.system_log.debug(f"Max potential BUY crypto ({opp['buy_exchange']}): {max_buy_crypto:.8f} ({buy_wallet.get('USDT', 0):.2f} USDT / {opp['buy_price']})")
            self.system_log.debug(f"Max potential SELL crypto ({opp['sell_exchange']}): {max_sell_crypto:.8f} {base_currency}")

            trade_amount_crypto = min(max_buy_crypto, max_sell_crypto)
            
            self.system_log.debug(f"Calculated trade amount: {trade_amount_crypto:.8f} {base_currency}")

            if trade_amount_crypto < 1e-9:
                self.system_log.debug(f"  [FAIL] Opp for {symbol} | Reason: Insufficient balance. Needed USDT: {opp['buy_price'] * trade_amount_crypto:.2f} or {base_currency}: {trade_amount_crypto:.8f}")
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
                profit_usdt = trade_value_usdt * (opp['profit_pct'] / 100)
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
    Main entry point for the backtester.
    """
    session_to_test = 'data/market_data/2025-10-21_01-57-39/'
    backtester = Backtester(session_path=session_to_test)
    backtester.run()

if __name__ == "__main__":
    main()