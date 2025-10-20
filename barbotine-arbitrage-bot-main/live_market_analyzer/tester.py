#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Automated test for selecting the most effective exchanges and trading pairs.
"""

import asyncio
import time
from typing import List, Dict

from .logger import log
from ..ws_client import SpreadAggregatorClient
from .balance_manager import BalanceManager
from ..arbitrage_analyzer import MultiExchangeArbitrageAnalyzer, DirectionalOpportunity
from ..exchange_config import DEFAULT_COMMISSION_PCT
from .statistics_collector import StatisticsCollector
from .report_generator import ReportGenerator

class LiveTester:
    """
    Orchestrates the 30-minute live market data test.
    """
    def __init__(self, test_duration_minutes: int = 30):
        self.test_duration_seconds = test_duration_minutes * 60
        self.ws_client = SpreadAggregatorClient()
        self.start_time = None
        self.balance_managers: Dict[float, BalanceManager] = {}
        self.analyzer = MultiExchangeArbitrageAnalyzer()
        self.profit_thresholds = [0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7]
        self.commission_pct = DEFAULT_COMMISSION_PCT
        self.stats_collector = StatisticsCollector()
        self.is_initialized = False

    def _initialize_components(self, market_data: List[Dict]):
        """Initializes a separate BalanceManager for each profit threshold."""
        exchanges = list(set(d['exchange'] for d in market_data if d.get('exchange')))
        symbols = list(set(d['symbol'] for d in market_data if d.get('symbol')))
        
        for threshold in self.profit_thresholds:
            bm = BalanceManager(exchanges, symbols)
            bm.setup_initial_balances(market_data)
            self.balance_managers[threshold] = bm
        
        log.info(f"Initialized {len(self.profit_thresholds)} independent balance managers.")
        self.is_initialized = True

    async def processing_loop(self):
        """The main processing loop that fetches data batches and analyzes them."""
        self.start_time = time.time()
        log.info("Test started. Waiting for initial data to initialize components...")
        
        while time.time() - self.start_time < self.test_duration_seconds:
            batch = await self.ws_client.get_batch()
            if not batch:
                await asyncio.sleep(0.1) # Wait if no data
                continue

            if not self.is_initialized:
                self._initialize_components(batch)

            await self.analyze_and_simulate(batch)
            await asyncio.sleep(0.1) # Process batches every 100ms

    async def analyze_and_simulate(self, data: List[Dict]):
        """Analyzes a batch of data and runs simulations."""
        log.info(f"Processing batch of {len(data)} records...")

        for threshold, balance_manager in self.balance_managers.items():
            all_balances = balance_manager.get_all_balances()
            
            opportunities = self.analyzer.find_maker_taker_opportunities(
                spread_data=data,
                balances=all_balances,
                commission_pct=self.commission_pct
            )

            valid_opportunities = [opp for opp in opportunities if opp.profit_pct >= threshold]

            if valid_opportunities:
                self._simulate_trades_for_threshold(valid_opportunities, threshold, balance_manager)
    
    def _simulate_trades_for_threshold(self, opportunities: List[DirectionalOpportunity], threshold: float, balance_manager: BalanceManager):
        """
        Iterates through all valid opportunities for a given threshold and simulates trades.
        """
        for opp in opportunities:
            buy_wallet = balance_manager.get_balance(opp.buy_exchange, opp.symbol)
            sell_wallet = balance_manager.get_balance(opp.sell_exchange, opp.symbol)
            
            if not opp.symbol.endswith('USDT'): continue
            base_currency = opp.symbol[:-4]

            max_buy_crypto = buy_wallet['USDT'] / opp.buy_price if opp.buy_price > 0 else 0
            max_sell_crypto = sell_wallet.get(base_currency, 0)
            
            trade_amount_crypto = min(max_buy_crypto, max_sell_crypto)
            
            if trade_amount_crypto < 1e-9:
                continue

            trade_value_usdt = trade_amount_crypto * opp.buy_price
            
            success = balance_manager.simulate_trade(
                buy_exchange=opp.buy_exchange,
                sell_exchange=opp.sell_exchange,
                symbol=opp.symbol,
                buy_price=opp.buy_price,
                sell_price=opp.sell_price,
                amount_crypto=trade_amount_crypto,
                commission_pct=self.commission_pct
            )

            if success:
                profit_usdt = trade_value_usdt * (opp.profit_pct / 100)
                log.info(f"    [TRADE] Threshold: {threshold}% | "
                         f"{opp.symbol} ({opp.buy_exchange} -> {opp.sell_exchange}) | "
                         f"Volume: {trade_amount_crypto:.6f} {base_currency} | "
                         f"Profit: {profit_usdt:.4f} USDT")
                
                self.stats_collector.record_trade(
                    profit_threshold=threshold,
                    buy_exchange=opp.buy_exchange,
                    sell_exchange=opp.sell_exchange,
                    symbol=opp.symbol,
                    net_profit_pct=opp.profit_pct,
                    amount_usdt=trade_value_usdt
                )

    async def run_test(self):
        """
        Connects to the WebSocket and runs the listening and processing loops concurrently.
        """
        log.info(f"Starting live test for {self.test_duration_seconds / 60} minutes.")
        
        try:
            await self.ws_client.connect("ws://127.0.0.1:8181/realtime")
            
            listen_task = asyncio.create_task(self.ws_client.listen())
            processing_task = asyncio.create_task(self.processing_loop())

            await processing_task

        except Exception as e:
            log.error(f"An error occurred during the test: {e}", exc_info=True)
        finally:
            if 'listen_task' in locals() and not listen_task.done():
                listen_task.cancel()
            log.info("Test finished. Generating report...")
            await self.ws_client.close()
            report_generator = ReportGenerator(self.stats_collector.get_results())
            report_generator.generate()
            log.info("Test completed.")

async def main():
    """
    Main entry point for the live tester.
    """
    tester = LiveTester()
    await tester.run_test()

if __name__ == "__main__":
    asyncio.run(main())