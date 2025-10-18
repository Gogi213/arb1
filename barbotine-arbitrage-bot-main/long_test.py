#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
8-Minute Long-Running Test
Tests the bot for 8 minutes and logs detailed statistics
"""

import asyncio
import time
from datetime import datetime
from ws_client import SpreadAggregatorClient
from arbitrage_analyzer import ArbitrageAnalyzer
from filter_engine import FilterEngine, FilterConfig
from balance_manager import BalanceManager
from multi_pair_trader import MultiPairTrader


class LongTestRunner:
    """Runs 8-minute test with detailed logging"""

    def __init__(self):
        self.start_time = None
        self.update_count = 0
        self.opportunity_count = 0
        self.filtered_count = 0
        self.trade_count = 0

        # Initialize components
        self.balance_manager = BalanceManager(total_usdt=10000)
        self.trader = MultiPairTrader(self.balance_manager, mode='fake-money', min_trade_interval=1.0)
        self.analyzer = ArbitrageAnalyzer()
        self.filter_config = FilterConfig(min_exchanges=3, min_profit_pct=0.5)
        self.filter_engine = FilterEngine(self.filter_config)
        self.ws_client = SpreadAggregatorClient()

        # Trading config
        self.max_trading_pairs = 3
        self.auto_trading_enabled = True

        # Statistics
        self.stats_log = []

    def log(self, message: str):
        """Log message with timestamp"""
        elapsed = time.time() - self.start_time if self.start_time else 0
        timestamp = f"[{elapsed:7.2f}s]"
        print(f"{timestamp} {message}")
        self.stats_log.append(f"{timestamp} {message}")

    async def run_test(self):
        """Run 8-minute test"""
        self.start_time = time.time()
        self.log("=== Starting 8-Minute Long Test ===")
        self.log(f"Initial capital: ${self.balance_manager.total_usdt:.2f}")
        self.log(f"Max trading pairs: {self.max_trading_pairs}")
        self.log(f"Filters: >= {self.filter_config.min_exchanges} exchanges, >= {self.filter_config.min_profit_pct}% profit")
        self.log("")

        # Connect to WebSocket
        try:
            await self.ws_client.connect("ws://127.0.0.1:8181")
            self.log("✓ Connected to SpreadAggregator")
        except Exception as e:
            self.log(f"✗ Connection failed: {e}")
            return

        # Data callback
        def on_data(parsed_data):
            if not parsed_data:
                return

            self.update_count += 1

            # Analyze opportunities
            opportunities = self.analyzer.analyze(parsed_data)
            self.opportunity_count += len(opportunities)

            # Apply filters
            filtered = self.filter_engine.filter(opportunities)
            self.filtered_count += len(filtered)

            # Log every 10 updates
            if self.update_count % 10 == 0:
                stats = self.trader.get_statistics()
                elapsed = time.time() - self.start_time
                self.log(f"Update #{self.update_count}: {len(filtered)} opportunities | "
                        f"Active pairs: {stats['total_pairs']} | "
                        f"Total trades: {stats['total_trades']} | "
                        f"Profit: ${stats['total_profit']:.2f}")

            # Auto-trading logic
            if self.auto_trading_enabled:
                # Start trading for top pairs if we have capacity
                active_count = len(self.trader.active_sessions)
                if active_count < self.max_trading_pairs:
                    # Find pairs not yet trading
                    for opp in filtered[:self.max_trading_pairs]:
                        if opp.symbol not in self.trader.active_sessions:
                            if self.trader.start_trading_pair(opp):
                                self.log(f"→ Started trading {opp.symbol} ({opp.profit_pct:.2f}% profit)")
                                break

                # Process opportunities for active pairs
                for opp in filtered[:10]:
                    if opp.symbol in self.trader.active_sessions:
                        if self.trader.process_opportunity(opp):
                            self.trade_count += 1

        # Listen for 8 minutes (480 seconds)
        try:
            await asyncio.wait_for(self.ws_client.listen(on_data), timeout=480.0)
        except asyncio.TimeoutError:
            pass
        finally:
            await self.ws_client.close()

        # Final statistics
        self.log("")
        self.log("=== Test Completed ===")
        elapsed = time.time() - self.start_time
        self.log(f"Duration: {elapsed:.1f} seconds ({elapsed/60:.1f} minutes)")
        self.log(f"Total updates: {self.update_count}")
        self.log(f"Total opportunities found: {self.opportunity_count}")
        self.log(f"Opportunities after filters: {self.filtered_count}")

        stats = self.trader.get_statistics()
        self.log(f"Active pairs: {stats['total_pairs']}")
        self.log(f"Total trades executed: {stats['total_trades']}")
        self.log(f"Total profit: ${stats['total_profit']:.2f}")

        self.log("")
        self.log("Per-pair statistics:")
        for symbol, session_stats in stats['sessions'].items():
            self.log(f"  {symbol}: {session_stats['trades']} trades, ${session_stats['profit']:.2f} profit")

        # Performance metrics
        if elapsed > 0:
            self.log("")
            self.log("Performance metrics:")
            self.log(f"  Updates per second: {self.update_count / elapsed:.2f}")
            self.log(f"  Trades per minute: {stats['total_trades'] / (elapsed/60):.2f}")
            if stats['total_trades'] > 0:
                self.log(f"  Average profit per trade: ${stats['total_profit'] / stats['total_trades']:.2f}")

        # Save log to file
        log_filename = f"test_log_{datetime.now().strftime('%Y%m%d_%H%M%S')}.txt"
        with open(log_filename, 'w', encoding='utf-8') as f:
            f.write('\n'.join(self.stats_log))
        self.log(f"Log saved to {log_filename}")


async def main():
    runner = LongTestRunner()
    await runner.run_test()


if __name__ == "__main__":
    asyncio.run(main())
