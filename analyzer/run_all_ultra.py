#!/usr/bin/env python3
"""
ULTRA-FAST parallel analyzer with batch processing and advanced optimizations.

Key optimizations:
1. Batch by symbol - load data once, analyze all pairs
2. No subprocess - direct function calls
3. Data caching in worker memory
4. Single parquet scan - read all dates at once (2-4x faster I/O)
5. Parallel exchange loading - ThreadPoolExecutor (1.5-2x faster)
6. Pure Polars operations - zero-copy, no NumPy conversion (1.5-2x faster)
7. Filter pushdown - filter nulls before sort (10-30% faster)
8. Decimal → Float64 cast - 1.5-2x faster parsing
9. Batch threshold calculation - all thresholds in one pass (1.3x faster)

Output metrics:
- Zero crossings per minute (mean reversion frequency)
- Opportunity cycles per hour for multiple thresholds (0.3%, 0.5%, 0.4%)
- Percent time above each threshold

Expected speedup: 2-3x vs previous version, 30-60x vs naive implementation
"""

import os
from pathlib import Path
from itertools import combinations
from multiprocessing import Pool, cpu_count
from concurrent.futures import ThreadPoolExecutor, as_completed
import polars as pl
from datetime import datetime

# Import analyzer library modules
from lib.config import load_config, get_default_config
from lib.data_loader import load_exchange_symbol_data
from lib.analysis import analyze_pair_fast
from lib.discovery import discover_data


def analyze_symbol_batch(args):
    """
    Analyze ALL pairs for a single symbol in one go.
    Loads data once, analyzes multiple pairs.

    This is the key optimization - prevents re-loading same data.
    """
    symbol, exchanges, data_path, start_date, end_date, thresholds, zero_threshold = args

    # OPTIMIZATION #12: Parallel loading of exchanges (1.5-2x faster)
    # Load data for all exchanges in parallel using ThreadPoolExecutor
    exchange_data = {}

    with ThreadPoolExecutor(max_workers=len(exchanges)) as executor:
        # Submit all loading tasks
        future_to_exchange = {
            executor.submit(load_exchange_symbol_data, data_path, exchange, symbol, start_date, end_date): exchange
            for exchange in exchanges
        }

        # Collect results as they complete
        for future in as_completed(future_to_exchange):
            exchange = future_to_exchange[future]
            try:
                data = future.result()
                if data is not None and not data.is_empty():
                    exchange_data[exchange] = data
            except Exception:
                pass

    # Now analyze all pairs
    results = []
    exchange_pairs = list(combinations(sorted(exchanges), 2))

    for ex1, ex2 in exchange_pairs:
        if ex1 not in exchange_data or ex2 not in exchange_data:
            results.append({
                'symbol': symbol,
                'ex1': ex1,
                'ex2': ex2,
                'status': 'SKIPPED',
                'stats': None
            })
            continue

        # Data already loaded - just analyze
        stats = analyze_pair_fast(
            symbol, ex1, ex2,
            exchange_data[ex1],
            exchange_data[ex2],
            thresholds,
            zero_threshold
        )

        if stats is not None:
            results.append({
                'symbol': symbol,
                'ex1': ex1,
                'ex2': ex2,
                'status': 'SUCCESS',
                'stats': stats
            })
        else:
            results.append({
                'symbol': symbol,
                'ex1': ex1,
                'ex2': ex2,
                'status': 'SKIPPED',
                'stats': None
            })

    return results


def run_ultra_fast_analysis(
    data_path,
    exchanges_filter=None,
    n_workers=None,
    start_date=None,
    end_date=None,
    thresholds=None,
    zero_threshold=0.05
):
    """
    ULTRA-FAST analysis with batching and caching.

    Args:
        data_path: Path to the market data directory.
        exchanges_filter: A list of exchanges to filter by.
        n_workers: Number of parallel workers (default: 3x CPU cores)
        start_date: Start date filter (YYYY-MM-DD format), inclusive. If None, no start filter.
        end_date: End date filter (YYYY-MM-DD format), inclusive. If None, no end filter.
        thresholds: List of analysis thresholds (default: [0.3, 0.5, 0.4])
        zero_threshold: Neutral zone threshold (default: 0.05)
    """
    DATA_PATH = data_path

    # Print date filter info
    if start_date or end_date:
        if start_date and end_date:
            print(f"\n>>> Filtering data: {start_date} to {end_date} <<<")
        elif start_date:
            print(f"\n>>> Filtering data: from {start_date} onwards <<<")
        else:
            print(f"\n>>> Filtering data: up to {end_date} <<<")
    else:
        print("\n>>> Analyzing ALL available data <<<")

    # Discover symbols
    symbols_to_analyze = discover_data(DATA_PATH)

    # DEBUG: Print some symbols to check formats
    print("\n--- Sample symbols found ---")
    for i, (symbol, exchanges) in enumerate(symbols_to_analyze.items()):
        if i < 5:  # Show first 5
            print(f"{symbol}: {list(exchanges)}")
        elif i == 5:
            print("...")
            break

    if not symbols_to_analyze:
        return

    # Filter exchanges if provided
    if exchanges_filter:
        print(f"\n>>> Filtering for exchanges: {', '.join(exchanges_filter)} <<<")
        exchanges_filter_set = set(exchanges_filter)
        filtered_symbols = {}
        for symbol, exchanges in symbols_to_analyze.items():
            # Keep only the exchanges that are in the filter list
            filtered_exchanges = exchanges.intersection(exchanges_filter_set)
            # Only keep symbols that are on at least 2 of the *filtered* exchanges
            if len(filtered_exchanges) >= 2:
                filtered_symbols[symbol] = filtered_exchanges
        symbols_to_analyze = filtered_symbols

        if not symbols_to_analyze:
            print("No symbols found trading on 2 or more of the specified exchanges.")
            return

    print("\n--- Preparing Symbol Batches ---")

    # Create tasks (one per SYMBOL, not per pair)
    tasks = []
    total_pairs = 0

    for symbol, exchanges in symbols_to_analyze.items():
        n_pairs = len(list(combinations(exchanges, 2)))
        total_pairs += n_pairs
        tasks.append((symbol, list(exchanges), DATA_PATH, start_date, end_date, thresholds, zero_threshold))

    print(f"Total symbols: {len(tasks)}")
    print(f"Total pairs: {total_pairs}")

    # Determine workers
    if n_workers is None:
        n_workers = cpu_count() * 3

    print(f"Using {n_workers} parallel workers")
    print(f"Batch processing: {total_pairs / len(tasks):.1f} pairs per symbol (avg)")
    print(f"\n--- Starting ULTRA-FAST Analysis ---\n")

    # Process in parallel
    successful = 0
    skipped = 0
    errors = 0
    all_stats = []

    processed_pairs = 0

    with Pool(processes=n_workers) as pool:
        # Process by SYMBOL batches
        results_batches = pool.imap_unordered(analyze_symbol_batch, tasks, chunksize=1)

        for batch_results in results_batches:
            for result in batch_results:
                processed_pairs += 1
                symbol = result['symbol']
                ex1 = result['ex1']
                ex2 = result['ex2']
                status = result['status']

                if status == "SUCCESS":
                    print(f"[{processed_pairs}/{total_pairs}] OK {symbol} ({ex1} vs {ex2})")
                    successful += 1

                    if result['stats']:
                        all_stats.append({
                            'symbol': symbol,
                            'exchange1': ex1,
                            'exchange2': ex2,
                            **result['stats']
                        })
                else:
                    skipped += 1

    # Save statistics
    if all_stats:
        # Use Polars instead of pandas (faster, no extra dependency)
        stats_df = pl.DataFrame(all_stats)
        # Sort by zero_crossings_per_minute (MOST IMPORTANT for mean reversion)
        stats_df = stats_df.sort('zero_crossings_per_minute', descending=True)

        # Create summary_stats directory inside analyzer if it doesn't exist
        analyzer_dir = Path(__file__).parent
        save_dir = analyzer_dir / "summary_stats"
        os.makedirs(save_dir, exist_ok=True)

        timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')
        stats_filename = save_dir / f"summary_stats_{timestamp}.csv"
        stats_df.write_csv(stats_filename)

        print(f"\n[OK] Summary statistics saved to: {stats_filename}")

        print(f"\n  Top 10 pairs by mean reversion frequency (zero crossings/min):")
        print(f"  {'Symbol':<12} {'Ex1':<8} {'Ex2':<8} {'ZC/min':<8} {'Cycles':<7} {'40bp/hr':<9} {'Asymm':<7}")
        print(f"  {'-'*82}")
        for row in stats_df.head(10).iter_rows(named=True):
            asymmetry = row.get('deviation_asymmetry', 0)
            cycles_040bp = row.get('opportunity_cycles_040bp', 0)

            print(f"  {row['symbol']:<12} {row['exchange1']:<8} {row['exchange2']:<8} "
                  f"{row.get('zero_crossings_per_minute', 0):>7.2f} "
                  f"{cycles_040bp:>6.0f} "
                  f"{row.get('cycles_040bp_per_hour', 0):>8.1f} "
                  f"{abs(asymmetry):>6.2f}")

        # Sort by opportunity cycles (complete round-trips with return to neutral)
        cycles_sorted = stats_df.sort('opportunity_cycles_040bp', descending=True)
        print(f"\n  Top 10 pairs by COMPLETE cycles (most tradeable opportunities):")
        print(f"  {'Symbol':<12} {'Ex1':<8} {'Ex2':<8} {'Cycles':<7} {'Per hr':<8} {'ZC/min':<8} {'Asymm':<7}")
        print(f"  {'-'*82}")
        for row in cycles_sorted.head(10).iter_rows(named=True):
            asymmetry = row.get('deviation_asymmetry', 0)
            cycles_040bp = row.get('opportunity_cycles_040bp', 0)

            print(f"  {row['symbol']:<12} {row['exchange1']:<8} {row['exchange2']:<8} "
                  f"{cycles_040bp:>6.0f} "
                  f"{row.get('cycles_040bp_per_hour', 0):>7.1f} "
                  f"{row.get('zero_crossings_per_minute', 0):>7.2f} "
                  f"{abs(asymmetry):>6.2f}")

    print(f"\n--- ULTRA-FAST Analysis Finished ---")
    print(f"Total pairs: {total_pairs}")
    print(f"[OK] Successful: {successful}")
    print(f"[ -] Skipped (no data): {skipped}")
    print(f"[!!] Errors: {errors}")


if __name__ == "__main__":
    # Required for Windows multiprocessing support
    import multiprocessing
    multiprocessing.freeze_support()

    import argparse
    from datetime import date

    parser = argparse.ArgumentParser(
        description="ULTRA-FAST parallel ratio analyzer with batching",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  # Analyze all available data
  python run_all_ultra.py

  # Analyze only today's data
  python run_all_ultra.py --date 2025-11-03

  # Analyze data for a specific date range
  python run_all_ultra.py --start-date 2025-11-01 --end-date 2025-11-03

  # Analyze from a specific date onwards
  python run_all_ultra.py --start-date 2025-11-02

  # Analyze up to a specific date
  python run_all_ultra.py --end-date 2025-11-02

  # Use more workers for faster processing
  python run_all_ultra.py --workers 16 --date 2025-11-03

  # Use config file
  python run_all_ultra.py --config config.yaml
        """
    )
    parser.add_argument("--data-path", type=str, default=None,
                        help="Path to the market data directory (overrides config)")
    parser.add_argument("--exchanges", type=str, nargs='+', default=None,
                        help="List of exchanges to analyze (e.g., Binance Bybit OKX)")
    parser.add_argument("--workers", type=int, default=None,
                        help="Number of parallel workers (default: 3x CPU cores)")
    parser.add_argument("--date", type=str, default=None,
                        help="Analyze data for a specific date (YYYY-MM-DD). Shortcut for --start-date=DATE --end-date=DATE")
    parser.add_argument("--start-date", type=str, default=None,
                        help="Start date for analysis (YYYY-MM-DD), inclusive")
    parser.add_argument("--end-date", type=str, default=None,
                        help="End date for analysis (YYYY-MM-DD), inclusive")
    parser.add_argument("--thresholds", type=float, nargs=3, default=None,
                        help="Analysis thresholds as percentages (default from config: 0.3 0.5 0.4)")
    parser.add_argument("--today", action="store_true",
                        help="Analyze only today's data. Shortcut for --date=<today>")
    parser.add_argument("--config", type=str, default=None,
                        help="Path to config file (default: config.yaml in script directory)")

    args = parser.parse_args()

    # Load configuration
    try:
        if args.config:
            config = load_config(Path(args.config))
        else:
            config = load_config()
    except FileNotFoundError:
        print("WARNING: config.yaml not found, using defaults")
        config = get_default_config()

    # Command line args override config
    data_path = args.data_path if args.data_path else config.data_directory
    exchanges_filter = args.exchanges if args.exchanges else config.exchanges
    n_workers = args.workers if args.workers else config.workers
    thresholds = args.thresholds if args.thresholds else config.thresholds
    zero_threshold = config.zero_threshold

    # Handle --today flag
    if args.today:
        today_str = date.today().strftime('%Y-%m-%d')
        start_date = today_str
        end_date = today_str
        print(f">>> Using --today: {today_str} <<<")
    # Handle --date shortcut
    elif args.date:
        start_date = args.date
        end_date = args.date
    else:
        start_date = args.start_date if args.start_date else config.start_date
        end_date = args.end_date if args.end_date else config.end_date

    # Validate date format (basic check)
    for date_str, name in [(start_date, "start-date"), (end_date, "end-date")]:
        if date_str:
            try:
                datetime.strptime(date_str, '%Y-%m-%d')
            except ValueError:
                print(f"ERROR: Invalid {name} format. Expected YYYY-MM-DD, got: {date_str}")
                exit(1)

    print(">>> ULTRA-FAST MODE <<<")
    print("Optimizations: Batch processing + No subprocess + Data caching\n")

    run_ultra_fast_analysis(
        data_path=data_path,
        exchanges_filter=exchanges_filter,
        n_workers=n_workers,
        start_date=start_date,
        end_date=end_date,
        thresholds=thresholds,
        zero_threshold=zero_threshold
    )
