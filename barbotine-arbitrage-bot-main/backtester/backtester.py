#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Orchestrates a backtest on historical market data, testing multiple
coins and profit thresholds in isolated simulations using Polars.
"""

import polars as pl
from typing import List, Dict
from collections import Counter
from .logger import log
from .data_loader import load_market_data
from .balance_manager import BalanceManager
from arbitrage_analyzer import MultiExchangeArbitrageAnalyzer
from .statistics_collector import StatisticsCollector
from .report_generator import ReportGenerator

class Backtester:
    """
    Orchestrates a backtest on historical market data, testing multiple
    coins and profit thresholds in isolated simulations using Polars.
    """
    def __init__(self, session_path: str):
        self.session_path = session_path
        self.analyzer = MultiExchangeArbitrageAnalyzer()
        self.commission_pct = 0.02
        self.profit_thresholds = [0.3, 0.4, 0.5, 0.6, 0.7]
        self.stats_collector = StatisticsCollector()
        self.balance_managers: Dict[str, Dict[float, BalanceManager]] = {}
        self.top_exchanges = []
        self.top_symbols = []

    def _analyze_initial_data(self, data: pl.DataFrame, duration_minutes: int = 30, top_n_symbols: int = 20):
        """
        Analyzes the first N minutes of data to find the most promising
        exchanges and symbols for arbitrage.
        """
        log.info(f"Analyzing first {duration_minutes} minutes of data to select pools...")
        
        start_time = data.select(pl.min("timestamp"))[0, 0]
        end_time = start_time + pl.duration(minutes=duration_minutes)
        initial_df = data.filter(pl.col('timestamp') <= end_time)

        opportunities_df = self.analyzer.find_maker_taker_opportunities(
            spread_data=initial_df,
            commission_pct=self.commission_pct
        )

        if opportunities_df.is_empty():
            log.error("No theoretical arbitrage opportunities found in the initial analysis period. Exiting.")
            return False
            
        # Filter for opportunities that meet at least the minimum profit threshold
        profitable_opportunities_df = opportunities_df.filter(pl.col('profit_pct') >= self.profit_thresholds[0])

        if profitable_opportunities_df.is_empty():
            log.error(f"No opportunities found meeting the minimum profit threshold of {self.profit_thresholds[0]}%. Exiting.")
            return False

        exchange_counts = Counter(profitable_opportunities_df["buy_exchange"].to_list() + profitable_opportunities_df["sell_exchange"].to_list())
        symbol_counts = Counter(profitable_opportunities_df["symbol"].to_list())

        self.top_exchanges = [item[0] for item in exchange_counts.most_common(3)]
        self.top_symbols = [item[0] for item in symbol_counts.most_common(top_n_symbols)]

        if len(self.top_exchanges) < 2:
            log.error("Could not identify at least 2 active exchanges. Exiting.")
            return False

        log.info(f"Selected Top 3 Exchanges: {self.top_exchanges}")
        log.info(f"Selected Top {top_n_symbols} Symbols: {self.top_symbols}")
        return True

    def run(self):
        """
        Main entry point for the backtester.
        """
        full_data = load_market_data(self.session_path)
        if full_data.is_empty(): return

        if not self._analyze_initial_data(full_data): return

        log.info("Initializing simulation instances for each coin and threshold...")
        
        initial_data_df = full_data.filter(pl.col('timestamp') <= full_data.select(pl.min("timestamp"))[0, 0] + pl.duration(minutes=30))
        initial_data_list = initial_data_df.to_dicts()

        for symbol in self.top_symbols:
            self.balance_managers[symbol] = {}
            for threshold in self.profit_thresholds:
                bm = BalanceManager(exchanges=self.top_exchanges, symbol=symbol)
                bm.setup_initial_assets(initial_data_list)
                self.balance_managers[symbol][threshold] = bm
        log.info(f"Initialized {len(self.top_symbols) * len(self.profit_thresholds)} simulation instances.")

        main_test_data = full_data.filter(pl.col('timestamp') > full_data.select(pl.min("timestamp"))[0, 0] + pl.duration(minutes=30))
        self._processing_loop(main_test_data)
        
        log.info("Backtest finished. Generating report...")
        report_generator = ReportGenerator(self.stats_collector.get_results())
        report_generator.generate()
        log.info("Backtest completed.")

    def _processing_loop(self, data: pl.DataFrame):
        """
        Processes data by iterating through each simulation instance (coin + threshold)
        and applying its strategy to the entire dataset.
        """
        log.info(f"Starting vectorized processing loop...")

        for symbol in self.top_symbols:
            symbol_data = data.filter(pl.col('symbol') == symbol)
            
            # Find all theoretical opportunities for this symbol across the entire dataset
            opportunities_df = self.analyzer.find_maker_taker_opportunities(
                spread_data=symbol_data,
                commission_pct=self.commission_pct
            )
            
            if opportunities_df.is_empty():
                log.info(f"No theoretical opportunities found for {symbol}.")
                continue

            for threshold in self.profit_thresholds:
                log.info(f"--- Running simulation for {symbol} @ {threshold}% ---")
                balance_manager = self.balance_managers[symbol][threshold]
                
                # Filter by profit threshold and sort by time
                valid_opportunities = opportunities_df.filter(pl.col('profit_pct') >= threshold).sort('timestamp')
                
                if valid_opportunities.is_empty():
                    log.info("No valid opportunities found for this simulation.")
                    continue

                log.info(f"Found {len(valid_opportunities)} theoretical opportunities. Simulating trades chronologically...")
                
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

            log.debug(f"--- Checking opportunity at {opp['timestamp']} for {symbol} ---")
            log.debug(f"Buy on {opp['buy_exchange']} at {opp['buy_price']}, Sell on {opp['sell_exchange']} at {opp['sell_price']}")
            log.debug(f"Balances before check: {balance_manager.get_all_balances_str()}")

            max_buy_crypto = buy_wallet.get('USDT', 0) / opp['buy_price'] if opp['buy_price'] > 0 else 0
            max_sell_crypto = sell_wallet.get(base_currency, 0)
            
            log.debug(f"Max potential BUY crypto ({opp['buy_exchange']}): {max_buy_crypto:.8f} ({buy_wallet.get('USDT', 0):.2f} USDT / {opp['buy_price']})")
            log.debug(f"Max potential SELL crypto ({opp['sell_exchange']}): {max_sell_crypto:.8f} {base_currency}")

            trade_amount_crypto = min(max_buy_crypto, max_sell_crypto)
            
            log.debug(f"Calculated trade amount: {trade_amount_crypto:.8f} {base_currency}")

            if trade_amount_crypto < 1e-9:
                log.debug(f"  [SKIP] Opp for {symbol} | Reason: Insufficient balance for trade.")
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
        log.info(f"Simulated {trade_count} trades for {symbol} @ {threshold}%.")


def main():
    """
    Main entry point for the backtester.
    """
    session_to_test = 'data/market_data/2025-10-21_01-57-39/'
    backtester = Backtester(session_path=session_to_test)
    backtester.run()

if __name__ == "__main__":
    main()