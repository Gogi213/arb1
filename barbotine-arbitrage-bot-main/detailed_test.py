#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Detailed 8-Minute Test with Full Trade Logging
Logs every single trade with bid/ask, balances, fees, and profit
"""

import asyncio
import time
from datetime import datetime
from ws_client import SpreadAggregatorClient
from arbitrage_analyzer import ArbitrageAnalyzer
from filter_engine import FilterEngine, FilterConfig
from balance_manager import BalanceManager
from multi_pair_trader import MultiPairTrader


class DetailedTestRunner:
    """Runs test with detailed trade-by-trade logging"""

    def __init__(self):
        self.start_time = None
        self.update_count = 0
        self.trade_log = []

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

    def log_trade(self, trade_num: int, symbol: str, opportunity, trade_amount: float,
                  balances_before: dict, balances_after: dict, profit: float):
        """Log detailed trade information"""
        elapsed = time.time() - self.start_time

        log_entry = f"""
{'='*80}
TRADE #{trade_num} - {symbol} @ {elapsed:.1f}s
{'='*80}
Opportunity:
  Buy from:  {opportunity.min_ask_exchange} @ ${opportunity.min_ask:.6f} (ask)
  Sell to:   {opportunity.max_bid_exchange} @ ${opportunity.max_bid:.6f} (bid)
  Gross profit: {opportunity.profit_pct:.3f}%
  Trade amount: {trade_amount:.8f} {symbol.split('/')[0]}

Balances BEFORE trade:
  {opportunity.min_ask_exchange} (buy side):
    USDT:   ${balances_before[opportunity.min_ask_exchange]['usdt']:.2f}
    Crypto: {balances_before[opportunity.min_ask_exchange]['crypto']:.8f}
  {opportunity.max_bid_exchange} (sell side):
    USDT:   ${balances_before[opportunity.max_bid_exchange]['usdt']:.2f}
    Crypto: {balances_before[opportunity.max_bid_exchange]['crypto']:.8f}

Trade execution:
  Buy  {trade_amount:.8f} @ ${opportunity.min_ask:.6f} = ${trade_amount * opportunity.min_ask:.2f}
  Sell {trade_amount:.8f} @ ${opportunity.max_bid:.6f} = ${trade_amount * opportunity.max_bid:.2f}

Fees (0.1% each side):
  Buy fee (quote):  {self.trader.default_fees['quote']*100}% on USDT
  Buy fee (base):   {self.trader.default_fees['base']*100}% on crypto
  Sell fee (quote): {self.trader.default_fees['quote']*100}% on USDT
  Sell fee (base):  {self.trader.default_fees['base']*100}% on crypto

Balances AFTER trade:
  {opportunity.min_ask_exchange} (buy side):
    USDT:   ${balances_after[opportunity.min_ask_exchange]['usdt']:.2f}
    Crypto: {balances_after[opportunity.min_ask_exchange]['crypto']:.8f}
  {opportunity.max_bid_exchange} (sell side):
    USDT:   ${balances_after[opportunity.max_bid_exchange]['usdt']:.2f}
    Crypto: {balances_after[opportunity.max_bid_exchange]['crypto']:.8f}

Balance changes:
  {opportunity.min_ask_exchange}: USDT {balances_after[opportunity.min_ask_exchange]['usdt'] - balances_before[opportunity.min_ask_exchange]['usdt']:+.2f}, Crypto {balances_after[opportunity.min_ask_exchange]['crypto'] - balances_before[opportunity.min_ask_exchange]['crypto']:+.8f}
  {opportunity.max_bid_exchange}: USDT {balances_after[opportunity.max_bid_exchange]['usdt'] - balances_before[opportunity.max_bid_exchange]['usdt']:+.2f}, Crypto {balances_after[opportunity.max_bid_exchange]['crypto'] - balances_before[opportunity.max_bid_exchange]['crypto']:+.8f}

Net profit this trade: ${profit:.4f}
"""
        self.trade_log.append(log_entry)
        print(f"[{elapsed:7.1f}s] Trade #{trade_num}: {symbol} profit=${profit:.4f}")

    async def run_test(self):
        """Run detailed 8-minute test"""
        self.start_time = time.time()
        print("=== Starting Detailed 8-Minute Test ===")
        print(f"Initial capital: ${self.balance_manager.total_usdt:.2f}")
        print(f"Every trade will be logged in detail\n")

        # Connect to WebSocket
        try:
            await self.ws_client.connect("ws://127.0.0.1:8181")
            print("✓ Connected to SpreadAggregator\n")
        except Exception as e:
            print(f"✗ Connection failed: {e}")
            return

        trade_counter = 0

        # Data callback
        def on_data(parsed_data):
            nonlocal trade_counter

            if not parsed_data:
                return

            self.update_count += 1

            # Analyze opportunities
            opportunities = self.analyzer.analyze(parsed_data)

            # Apply filters
            filtered = self.filter_engine.filter(opportunities)

            # Auto-trading logic
            if self.auto_trading_enabled:
                # Start trading for top pairs if we have capacity
                active_count = len(self.trader.active_sessions)
                if active_count < self.max_trading_pairs:
                    for opp in filtered[:self.max_trading_pairs]:
                        if opp.symbol not in self.trader.active_sessions:
                            if self.trader.start_trading_pair(opp):
                                elapsed = time.time() - self.start_time
                                print(f"[{elapsed:7.1f}s] Started trading {opp.symbol} ({opp.profit_pct:.2f}% profit)")

                                # Log initial allocation
                                balances = self.balance_manager.get_pair_balances(opp.symbol)
                                log_entry = f"""
{'='*80}
PAIR STARTED: {opp.symbol} @ {elapsed:.1f}s
{'='*80}
Initial allocation:
"""
                                for exchange, bal in balances.items():
                                    log_entry += f"  {exchange}: ${bal['usdt']:.2f} USDT, {bal['crypto']:.8f} crypto\n"

                                self.trade_log.append(log_entry)
                                break

                # Process opportunities for active pairs
                for opp in filtered[:10]:
                    if opp.symbol in self.trader.active_sessions:
                        # Get balances before trade
                        try:
                            balances_before = {
                                ex: dict(bal) for ex, bal in
                                self.balance_manager.get_pair_balances(opp.symbol).items()
                            }
                        except:
                            continue

                        # Calculate trade amount (same logic as in trader)
                        sell_balance = balances_before.get(opp.max_bid_exchange, {}).get('crypto', 0)
                        trade_amount = sell_balance * self.trader.trade_amount_pct

                        if trade_amount <= 0:
                            continue

                        # Get profit before trade
                        stats_before = self.trader.get_statistics()
                        profit_before = stats_before['sessions'].get(opp.symbol, {}).get('profit', 0)

                        # Execute trade
                        if self.trader.process_opportunity(opp):
                            trade_counter += 1

                            # Get balances after trade
                            balances_after = {
                                ex: dict(bal) for ex, bal in
                                self.balance_manager.get_pair_balances(opp.symbol).items()
                            }

                            # Get profit after trade
                            stats_after = self.trader.get_statistics()
                            profit_after = stats_after['sessions'].get(opp.symbol, {}).get('profit', 0)
                            profit_this_trade = profit_after - profit_before

                            # Log detailed trade
                            self.log_trade(
                                trade_counter, opp.symbol, opp, trade_amount,
                                balances_before, balances_after, profit_this_trade
                            )

        # Listen for 8 minutes (480 seconds)
        try:
            await asyncio.wait_for(self.ws_client.listen(on_data), timeout=480.0)
        except asyncio.TimeoutError:
            pass
        finally:
            await self.ws_client.close()

        # Final summary
        elapsed = time.time() - self.start_time
        print(f"\n{'='*80}")
        print(f"TEST COMPLETED - {elapsed:.1f}s ({elapsed/60:.1f} minutes)")
        print(f"{'='*80}")
        print(f"Total updates: {self.update_count}")
        print(f"Total trades logged: {trade_counter}")

        stats = self.trader.get_statistics()
        print(f"Total profit: ${stats['total_profit']:.2f}")

        if trade_counter > 0:
            print(f"Average profit per trade: ${stats['total_profit'] / trade_counter:.4f}")

        # Save detailed log
        log_filename = f"detailed_trades_{datetime.now().strftime('%Y%m%d_%H%M%S')}.txt"

        # Add summary at the end
        summary = f"""

{'='*80}
FINAL SUMMARY
{'='*80}
Duration: {elapsed:.1f} seconds ({elapsed/60:.1f} minutes)
Total updates: {self.update_count}
Total trades: {trade_counter}
Total profit: ${stats['total_profit']:.2f}

Per-pair statistics:
"""
        for symbol, session_stats in stats['sessions'].items():
            summary += f"  {symbol}: {session_stats['trades']} trades, ${session_stats['profit']:.2f} profit\n"

        if trade_counter > 0:
            summary += f"\nAverage profit per trade: ${stats['total_profit'] / trade_counter:.4f}\n"

        summary += f"\nFinal balances:\n"
        for symbol in self.trader.active_sessions.keys():
            balances = self.balance_manager.get_pair_balances(symbol)
            summary += f"\n{symbol}:\n"
            for exchange, bal in balances.items():
                summary += f"  {exchange}: ${bal['usdt']:.2f} USDT, {bal['crypto']:.8f} crypto\n"

        self.trade_log.append(summary)

        with open(log_filename, 'w', encoding='utf-8') as f:
            f.write('\n'.join(self.trade_log))

        print(f"\nDetailed log saved to: {log_filename}")
        print(f"Total lines: {len(self.trade_log)}")


async def main():
    runner = DetailedTestRunner()
    await runner.run_test()


if __name__ == "__main__":
    asyncio.run(main())
