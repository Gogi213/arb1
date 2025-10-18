#!/usr/bin/env python3
"""
Test Multi-Directional Arbitrage Implementation
8-minute test with detailed logging to verify balance-aware trading
"""

import asyncio
import websockets
import json
import time
from datetime import datetime
from balance_manager import BalanceManager
from multi_pair_trader import MultiPairTrader
from ws_client import parse_spread_data

# Test configuration
WEBSOCKET_URL = "ws://127.0.0.1:8181"
TEST_DURATION_SECONDS = 480  # 8 minutes
INITIAL_BALANCE_USDT = 10000
SYMBOL = "ZBTUSDT"
EXCHANGES = ['Bybit', 'Binance', 'MEXC', 'BingX']  # 4 exchanges

# Global state
balance_manager = None
trader = None
stats = {
    'updates': 0,
    'trades': 0,
    'direction_counts': {},  # Track which directions were used
    'total_profit': 0.0,
    'start_time': None,
    'last_balance_report': 0
}

# Log file
log_filename = f"multi_directional_test_{datetime.now().strftime('%Y%m%d_%H%M%S')}.txt"

def log_trade(trade_num, result, balances_before, balances_after):
    """Log detailed trade information"""
    with open(log_filename, 'a', encoding='utf-8') as f:
        f.write(f"\n{'='*80}\n")
        f.write(f"TRADE #{trade_num} - {datetime.now().strftime('%H:%M:%S')}\n")
        f.write(f"{'='*80}\n")

        f.write(f"\nDIRECTION: {result['direction']}\n")
        f.write(f"Amount: {result['amount']:.8f} ZBT\n")
        f.write(f"Buy Price:  ${result['buy_price']:.6f}\n")
        f.write(f"Sell Price: ${result['sell_price']:.6f}\n")
        f.write(f"Profit: ${result['profit']:.4f} ({result['profit_pct']:.3f}%)\n")

        f.write(f"\nBALANCES BEFORE:\n")
        for exchange in EXCHANGES:
            b = balances_before.get(exchange, {'usdt': 0, 'crypto': 0})
            f.write(f"  {exchange:10s}: ${b['usdt']:>10.2f} USDT + {b['crypto']:>12.8f} ZBT\n")

        f.write(f"\nBALANCES AFTER:\n")
        for exchange in EXCHANGES:
            b = balances_after.get(exchange, {'usdt': 0, 'crypto': 0})
            f.write(f"  {exchange:10s}: ${b['usdt']:>10.2f} USDT + {b['crypto']:>12.8f} ZBT\n")

def log_summary():
    """Log final summary"""
    with open(log_filename, 'a', encoding='utf-8') as f:
        f.write(f"\n\n{'='*80}\n")
        f.write(f"TEST SUMMARY\n")
        f.write(f"{'='*80}\n")
        f.write(f"Duration: {TEST_DURATION_SECONDS} seconds\n")
        f.write(f"Total Updates: {stats['updates']}\n")
        f.write(f"Total Trades: {stats['trades']}\n")
        f.write(f"Total Profit: ${stats['total_profit']:.2f}\n\n")

        f.write(f"TRADES BY DIRECTION:\n")
        for direction, count in sorted(stats['direction_counts'].items(), key=lambda x: x[1], reverse=True):
            f.write(f"  {direction:20s}: {count:4d} trades\n")

        f.write(f"\nFINAL BALANCES:\n")
        try:
            final_balances = balance_manager.get_pair_balances(SYMBOL)
            for exchange in EXCHANGES:
                b = final_balances.get(exchange, {'usdt': 0, 'crypto': 0})
                f.write(f"  {exchange:10s}: ${b['usdt']:>10.2f} USDT + {b['crypto']:>12.8f} ZBT\n")
        except Exception as e:
            f.write(f"  Error getting final balances: {e}\n")

async def process_websocket():
    """Connect to WebSocket and process spread data"""
    global balance_manager, trader, stats

    print(f"Starting multi-directional arbitrage test...")
    print(f"Duration: {TEST_DURATION_SECONDS} seconds ({TEST_DURATION_SECONDS // 60} minutes)")
    print(f"Symbol: {SYMBOL}")
    print(f"Exchanges: {', '.join(EXCHANGES)}")
    print(f"Initial Balance: ${INITIAL_BALANCE_USDT:,.2f}\n")

    # Initialize components
    balance_manager = BalanceManager(total_usdt=INITIAL_BALANCE_USDT)
    trader = MultiPairTrader(
        balance_manager=balance_manager,
        mode='fake-money',
        min_trade_interval=0.5,  # Allow rapid trading for testing
        trade_amount_pct=0.1,    # Trade 10% of available crypto
        max_exchanges=4
    )

    # Get initial price from first update
    initial_price = None

    # Write log header
    with open(log_filename, 'w', encoding='utf-8') as f:
        f.write(f"Multi-Directional Arbitrage Test\n")
        f.write(f"Started: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n")
        f.write(f"Duration: {TEST_DURATION_SECONDS} seconds\n")
        f.write(f"Symbol: {SYMBOL}\n")
        f.write(f"Exchanges: {', '.join(EXCHANGES)}\n")
        f.write(f"Initial Balance: ${INITIAL_BALANCE_USDT:,.2f}\n")

    stats['start_time'] = time.time()

    try:
        async with websockets.connect(WEBSOCKET_URL) as websocket:
            print("[OK] Connected to WebSocket\n")

            while True:
                # Check if test duration exceeded
                elapsed = time.time() - stats['start_time']
                if elapsed >= TEST_DURATION_SECONDS:
                    print(f"\n[OK] Test completed after {elapsed:.1f} seconds")
                    break

                try:
                    message = await asyncio.wait_for(websocket.recv(), timeout=1.0)

                    # Parse columnar data format from C# server
                    data = parse_spread_data(message)

                    stats['updates'] += 1

                    # Get initial price from first update
                    if initial_price is None and data:
                        for record in data:
                            if record.get('symbol') == SYMBOL and record.get('bestAsk'):
                                initial_price = record['bestAsk']
                                print(f"Initial price: ${initial_price:.6f}")

                                # Start trading pair with real spread data
                                success = trader.start_trading_pair(SYMBOL, EXCHANGES, data)
                                if success:
                                    print(f"[OK] Trading started on {len(EXCHANGES)} exchanges\n")

                                    # Show initial balances
                                    status = trader.get_balance_status(SYMBOL)
                                    print("Initial balances:")
                                    for exchange in EXCHANGES:
                                        b = status['balances'][exchange]
                                        print(f"  {exchange:10s}: ${b['usdt']:>10.2f} USDT + {b['crypto']:>12.8f} ZBT")
                                    print(f"\nCan trade in {len(status['can_trade_directions'])} directions:")
                                    for direction in status['can_trade_directions'][:6]:  # Show first 6
                                        print(f"  - {direction}")
                                    if len(status['can_trade_directions']) > 6:
                                        print(f"  ... and {len(status['can_trade_directions']) - 6} more\n")
                                    else:
                                        print()
                                else:
                                    print("[ERROR] Failed to start trading")
                                    return
                                break

                    # Process opportunity if trading started
                    if initial_price is not None:
                        # Filter data for our symbol
                        symbol_data = [r for r in data if r.get('symbol') == SYMBOL]

                        if symbol_data:
                            # Get balances before trade
                            balances_before = balance_manager.get_pair_balances(SYMBOL)

                            # Process opportunity
                            result = trader.process_opportunity(SYMBOL, symbol_data)

                            if result and result.get('success'):
                                stats['trades'] += 1
                                stats['total_profit'] += result['profit']

                                # Track direction
                                direction = result['direction']
                                stats['direction_counts'][direction] = stats['direction_counts'].get(direction, 0) + 1

                                # Get balances after trade
                                balances_after = balance_manager.get_pair_balances(SYMBOL)

                                # Log trade
                                log_trade(stats['trades'], result, balances_before, balances_after)

                                # Print progress
                                print(f"Trade #{stats['trades']:4d} | {direction:20s} | "
                                      f"Profit: ${result['profit']:>7.4f} | "
                                      f"Total: ${stats['total_profit']:>8.2f}")

                    # Report progress every 60 seconds
                    if elapsed - stats['last_balance_report'] >= 60:
                        stats['last_balance_report'] = elapsed
                        print(f"\n--- Progress Report ({int(elapsed)}s / {TEST_DURATION_SECONDS}s) ---")
                        print(f"Updates: {stats['updates']} | Trades: {stats['trades']} | Profit: ${stats['total_profit']:.2f}")

                        status = trader.get_balance_status(SYMBOL)
                        print(f"Can still trade in {len(status['can_trade_directions'])} directions")

                        # Show top 3 directions by trade count
                        if stats['direction_counts']:
                            print("Most used directions:")
                            top_directions = sorted(stats['direction_counts'].items(), key=lambda x: x[1], reverse=True)[:3]
                            for direction, count in top_directions:
                                print(f"  {direction}: {count} trades")
                        print()

                except asyncio.TimeoutError:
                    # Check if we should continue
                    if time.time() - stats['start_time'] >= TEST_DURATION_SECONDS:
                        break
                    continue
                except Exception as parse_error:
                    # Skip parse errors
                    continue

    except Exception as e:
        print(f"\n[ERROR] Error: {e}")
        import traceback
        traceback.print_exc()

    finally:
        # Print final summary
        print(f"\n{'='*80}")
        print(f"TEST RESULTS")
        print(f"{'='*80}")
        print(f"Duration: {TEST_DURATION_SECONDS} seconds")
        print(f"Total Updates: {stats['updates']}")
        print(f"Total Trades: {stats['trades']}")
        print(f"Total Profit: ${stats['total_profit']:.2f}")

        if stats['trades'] > 0:
            print(f"Average Profit per Trade: ${stats['total_profit'] / stats['trades']:.4f}")

        print(f"\nTrades by Direction:")
        for direction, count in sorted(stats['direction_counts'].items(), key=lambda x: x[1], reverse=True):
            pct = count / stats['trades'] * 100 if stats['trades'] > 0 else 0
            print(f"  {direction:20s}: {count:4d} trades ({pct:5.1f}%)")

        # Show final balances
        print(f"\nFinal Balances:")
        try:
            final_balances = balance_manager.get_pair_balances(SYMBOL)
            for exchange in EXCHANGES:
                b = final_balances.get(exchange, {'usdt': 0, 'crypto': 0})
                print(f"  {exchange:10s}: ${b['usdt']:>10.2f} USDT + {b['crypto']:>12.8f} ZBT")
        except Exception as e:
            print(f"  Error: {e}")

        # Final statistics
        final_stats = trader.get_statistics()
        print(f"\nTrading Session Statistics:")
        for symbol, session in final_stats['sessions'].items():
            print(f"  {symbol}: {session['trades']} trades, ${session['profit']:.2f} profit")

        print(f"\nDetailed log saved to: {log_filename}")

        # Write summary to log
        log_summary()

if __name__ == "__main__":
    print("Multi-Directional Arbitrage Test")
    print("Make sure SpreadAggregator is running on ws://127.0.0.1:8181\n")

    asyncio.run(process_websocket())
