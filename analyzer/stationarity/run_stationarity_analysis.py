#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Separate script to analyze stationarity of spreads between exchange pairs.
This runs independently from the backtester.
"""

import argparse
import logging
from datetime import datetime
import sys
import os

# Add parent directory to path for imports
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from backtester.logger import LoggerManager
from backtester.data_loader import stream_market_data
from stationarity_analyzer import (
    calculate_stationarity_metrics,
    calculate_cointegration_test,
    calculate_half_life,
    calculate_zero_crossings,
    calculate_stationarity_score
)
from itertools import combinations
import polars as pl
from multiprocessing import Pool, cpu_count
from tqdm import tqdm
import numpy as np

def compute_alternating_metrics_vectorized(joined_df: pl.DataFrame, thresholds: list) -> dict:
    """
    Fully vectorized computation of alternating metrics for all thresholds at once.
    No Python loops, pure Polars operations.

    Returns dict with all threshold metrics.
    """
    metrics = {}
    total_rows = len(joined_df)

    # Pre-compute all opportunity masks for all thresholds in one go
    for threshold in thresholds:
        # Create opportunity direction column (1 for ex1->ex2, -1 for ex2->ex1, 0 for none)
        opp_df = joined_df.select([
            pl.when(pl.col('profit_pct_ex1_to_ex2') >= threshold)
            .then(pl.lit(1))
            .when(pl.col('profit_pct_ex2_to_ex1') >= threshold)
            .then(pl.lit(-1))
            .otherwise(pl.lit(0))
            .alias('opp_direction')
        ])

        # Filter to only opportunities
        opps_only = opp_df.filter(pl.col('opp_direction') != 0)
        total_opps = len(opps_only)

        metrics[f'pct_above_{threshold}'] = (total_opps / total_rows) * 100
        metrics[f'count_above_{threshold}'] = total_opps

        if total_opps == 0:
            metrics[f'count_above_{threshold}_alternating'] = 0
            metrics[f'pct_above_{threshold}_alternating'] = 0.0
            metrics[f'alternation_efficiency_{threshold}'] = 0.0
            metrics[f'max_same_direction_streak_{threshold}'] = 0
            metrics[f'avg_same_direction_streak_{threshold}'] = 0.0
            continue

        # Vectorized alternating detection: direction != previous direction
        opps_array = opps_only['opp_direction'].to_numpy()

        # Shifted comparison (first element always counts)
        is_alternating = np.concatenate(([True], opps_array[1:] != opps_array[:-1]))
        alternating_count = int(is_alternating.sum())

        # Streak calculation using run-length encoding
        change_points = np.where(is_alternating)[0]
        if len(change_points) > 1:
            streaks = np.diff(change_points)
            max_streak = int(streaks.max()) if len(streaks) > 0 else 0
            avg_streak = float(streaks.mean()) if len(streaks) > 0 else 0.0
        else:
            max_streak = 0
            avg_streak = 0.0

        metrics[f'count_above_{threshold}_alternating'] = alternating_count
        metrics[f'pct_above_{threshold}_alternating'] = (alternating_count / total_rows) * 100
        metrics[f'alternation_efficiency_{threshold}'] = (alternating_count / total_opps) * 100
        metrics[f'max_same_direction_streak_{threshold}'] = max_streak
        metrics[f'avg_same_direction_streak_{threshold}'] = avg_streak

    return metrics

def analyze_symbol(args):
    """
    Analyze a single symbol across all exchange pairs.
    This function is called by multiprocessing pool.

    Args:
        args: Tuple of (symbol, data_path, start_date, end_date, exchanges_list, min_data_points, thresholds)

    Returns:
        List of result dictionaries for this symbol
    """
    symbol, data_path, start_date, end_date, exchanges_list, min_data_points, thresholds = args

    # Create a simple logger for this process
    import logging
    logger = logging.getLogger(f'stationarity_{symbol}')

    # Load data for this symbol only
    try:
        data_stream = stream_market_data(
            data_path=data_path,
            start_date=start_date,
            end_date=end_date,
            exchanges=exchanges_list if exchanges_list else None,
            symbols=[symbol],  # Only load this symbol
            logger=logger
        )

        # Collect data for this symbol
        symbol_data_parts = []
        for daily_data in data_stream:
            if not daily_data.is_empty():
                symbol_data_parts.append(daily_data)

        if not symbol_data_parts:
            return []

        symbol_data = pl.concat(symbol_data_parts)

    except Exception as e:
        logger.error(f"Error loading data for {symbol}: {e}")
        return []

    symbol_results = []

    # Analyze each pair of exchanges
    for ex1, ex2 in combinations(exchanges_list, 2):
        ex1_data = symbol_data.filter(pl.col('exchange') == ex1).sort('timestamp')
        ex2_data = symbol_data.filter(pl.col('exchange') == ex2).sort('timestamp')

        if ex1_data.is_empty() or ex2_data.is_empty():
            continue

        # Join data on timestamp to get synchronized prices
        joined = ex1_data.select(['timestamp', 'bestBid', 'bestAsk']).rename({
            'bestBid': 'bid_ex1',
            'bestAsk': 'ask_ex1'
        }).join_asof(
            ex2_data.select(['timestamp', 'bestBid', 'bestAsk']).rename({
                'bestBid': 'bid_ex2',
                'bestAsk': 'ask_ex2'
            }),
            on='timestamp'
        )

        # === DATA VALIDATION ===
        initial_count = len(joined)

        joined = joined.filter(
            pl.col('bid_ex1').is_not_null() & pl.col('ask_ex1').is_not_null() &
            pl.col('bid_ex2').is_not_null() & pl.col('ask_ex2').is_not_null()
        ).filter(
            pl.col('bid_ex1').is_finite() & pl.col('ask_ex1').is_finite() &
            pl.col('bid_ex2').is_finite() & pl.col('ask_ex2').is_finite()
        ).filter(
            (pl.col('bid_ex1') > 0) & (pl.col('ask_ex1') > 0) &
            (pl.col('bid_ex2') > 0) & (pl.col('ask_ex2') > 0)
        ).filter(
            (pl.col('bid_ex1') <= pl.col('ask_ex1')) &
            (pl.col('bid_ex2') <= pl.col('ask_ex2'))
        )

        if len(joined) < min_data_points:
            continue

        # Calculate spread and profit percentages
        joined = joined.with_columns([
            (pl.col('bid_ex1') - pl.col('bid_ex2')).alias('spread_raw'),
            (((pl.col('bid_ex1') - pl.col('bid_ex2')) / pl.col('bid_ex2')) * 100 - 0.2).alias('profit_pct_ex1_to_ex2'),
            (((pl.col('bid_ex2') - pl.col('bid_ex1')) / pl.col('bid_ex1')) * 100 - 0.2).alias('profit_pct_ex2_to_ex1')
        ])

        spread_series = joined['spread_raw']

        # Sample statistical tests ONLY if dataset is huge (>20k)
        if len(spread_series) > 20000:
            sample_indices = pl.Series(range(len(spread_series))).sample(n=15000, seed=42)
            spread_series_sample = spread_series[sample_indices]
            joined_sample = joined[sample_indices]
        else:
            spread_series_sample = spread_series
            joined_sample = joined

        # Calculate stationarity metrics on sampled data
        metrics = calculate_stationarity_metrics(spread_series_sample)
        half_life = calculate_half_life(spread_series_sample)
        zero_crossings = calculate_zero_crossings(spread_series_sample)

        # Calculate cointegration
        coint_p_value, hedge_ratio = calculate_cointegration_test(
            joined_sample['bid_ex1'],
            joined_sample['bid_ex2']
        )

        stationarity_score = calculate_stationarity_score(metrics)

        # === TRADING VIABILITY METRICS ===
        # Use fully vectorized function - NO Python loops!
        threshold_metrics = compute_alternating_metrics_vectorized(joined, thresholds)

        # Cache spread calculations for reuse across thresholds
        total_duration_hours = (joined['timestamp'].max() - joined['timestamp'].min()).total_seconds() / 3600

        # Direction change frequency - vectorized
        direction_series = np.where(
            joined['profit_pct_ex1_to_ex2'].to_numpy() >= joined['profit_pct_ex2_to_ex1'].to_numpy(),
            1, -1
        )
        direction_changes = int(np.sum(direction_series[1:] != direction_series[:-1]))

        # Timing metrics
        opportunities_per_hour = {}
        avg_time_between_opportunities = {}

        for threshold in thresholds:
            # Raw opportunities (without alternation filter)
            count = threshold_metrics[f'count_above_{threshold}']
            if count > 0 and total_duration_hours > 0:
                opportunities_per_hour[f'opps_per_hour_{threshold}'] = count / total_duration_hours
                avg_time_between_opportunities[f'avg_minutes_between_{threshold}'] = (total_duration_hours * 60) / count
            else:
                opportunities_per_hour[f'opps_per_hour_{threshold}'] = 0
                avg_time_between_opportunities[f'avg_minutes_between_{threshold}'] = float('inf')

            # Alternating opportunities (realistic tradeable)
            count_alt = threshold_metrics[f'count_above_{threshold}_alternating']
            if count_alt > 0 and total_duration_hours > 0:
                opportunities_per_hour[f'opps_per_hour_{threshold}_alternating'] = count_alt / total_duration_hours
                avg_time_between_opportunities[f'avg_minutes_between_{threshold}_alternating'] = (total_duration_hours * 60) / count_alt
            else:
                opportunities_per_hour[f'opps_per_hour_{threshold}_alternating'] = 0
                avg_time_between_opportunities[f'avg_minutes_between_{threshold}_alternating'] = float('inf')

        # Spread distribution metrics
        spread_pct_series = joined.select([
            pl.max_horizontal('profit_pct_ex1_to_ex2', 'profit_pct_ex2_to_ex1').alias('max_profit_pct')
        ])['max_profit_pct']

        # Calculate percentiles separately (Polars quantile takes single value)
        p25 = spread_pct_series.quantile(0.25)
        p50 = spread_pct_series.quantile(0.5)
        p75 = spread_pct_series.quantile(0.75)
        p95 = spread_pct_series.quantile(0.95)

        result = {
            'symbol': symbol,
            'exchange_1': ex1,
            'exchange_2': ex2,
            'data_points': len(joined),
            'duration_hours': total_duration_hours,

            # Stationarity metrics
            'adf_p_value': metrics.get('adf_p_value'),
            'kpss_p_value': metrics.get('kpss_p_value'),
            'hurst_exponent': metrics.get('hurst_exponent'),
            'half_life': half_life,
            'zero_crossings': zero_crossings,
            'cointegration_p_value': coint_p_value,
            'hedge_ratio': hedge_ratio,
            'stationarity_score': stationarity_score,

            # Spread distribution
            'mean_spread': float(spread_series.mean()),
            'std_spread': float(spread_series.std()),
            'median_profit_pct': float(p50) if p50 else 0,
            'p25_profit_pct': float(p25) if p25 else 0,
            'p75_profit_pct': float(p75) if p75 else 0,
            'p95_profit_pct': float(p95) if p95 else 0,
            'max_profit_pct': float(spread_pct_series.max()),
            'min_profit_pct': float(spread_pct_series.min()),

            # Threshold frequency metrics
            **threshold_metrics,

            # Direction change frequency
            'direction_changes': direction_changes,
            'direction_changes_per_hour': direction_changes / total_duration_hours if total_duration_hours > 0 else 0,

            # Timing metrics
            **opportunities_per_hour,
            **avg_time_between_opportunities
        }

        symbol_results.append(result)

    return symbol_results

def analyze_stationarity(
    data_path: str,
    start_date: datetime = None,
    end_date: datetime = None,
    exchanges: list = None,
    symbols: list = None,
    min_data_points: int = 100
):
    """
    Analyzes stationarity of spreads between exchange pairs for each symbol.
    Uses multiprocessing for parallel processing of symbols.

    Args:
        data_path: Path to market data
        start_date: Start date for analysis
        end_date: End date for analysis
        exchanges: List of exchanges to analyze
        symbols: List of symbols to analyze
        min_data_points: Minimum number of data points required for analysis
    """
    log_manager = LoggerManager(session_name="stationarity")
    system_log = log_manager.get_logger('system')
    report_log = log_manager.get_logger('stationarity_report')

    system_log.info("--- Starting Stationarity Analysis ---")

    # First, get list of symbols and exchanges by scanning the data directory
    if not symbols or not exchanges:
        # Quick scan to get available symbols and exchanges
        data_stream = stream_market_data(
            data_path=data_path,
            start_date=start_date,
            end_date=end_date,
            exchanges=exchanges,
            symbols=symbols,
            logger=system_log
        )

        sample_data = next(data_stream, None)
        if sample_data is None or sample_data.is_empty():
            system_log.error("No data loaded. Exiting.")
            return

        if not symbols:
            symbols = sample_data.select('symbol').unique().to_series().to_list()
        if not exchanges:
            exchanges = sample_data.select('exchange').unique().to_series().to_list()

    symbols_list = symbols if symbols else []
    exchanges_list = exchanges if exchanges else []

    system_log.info(f"Analyzing {len(symbols_list)} symbols across {len(exchanges_list)} exchanges")

    # Prepare arguments for multiprocessing
    thresholds = [0.25, 0.3, 0.35, 0.4]
    args_list = [
        (symbol, data_path, start_date, end_date, exchanges_list, min_data_points, thresholds)
        for symbol in symbols_list
    ]

    # Use multiprocessing to analyze symbols in parallel
    num_workers = max(1, cpu_count() - 1)  # Leave one CPU free
    system_log.info(f"Using {num_workers} worker processes")

    results = []
    with Pool(processes=num_workers) as pool:
        # Use tqdm for progress bar
        for symbol_results in tqdm(
            pool.imap(analyze_symbol, args_list),
            total=len(args_list),
            desc="Analyzing symbols"
        ):
            results.extend(symbol_results)

    # Convert results to DataFrame and save
    if results:
        results_df = pl.DataFrame(results)

        # Sort by stationarity score (best first)
        results_df = results_df.sort('stationarity_score', descending=True)

        # Save to CSV
        output_file = f"stationarity_analysis_{datetime.now().strftime('%Y%m%d_%H%M%S')}.csv"
        results_df.write_csv(output_file)
        system_log.info(f"Results saved to {output_file}")

        # Print summary report
        report_log.info("\n" + "="*80)
        report_log.info(" " * 25 + "Stationarity Analysis Report")
        report_log.info("="*80 + "\n")

        report_log.info(f"Total pairs analyzed: {len(results_df)}")
        report_log.info(f"Symbols analyzed: {len(symbols_list)}\n")

        # Top 10 most stationary pairs
        report_log.info("Top 10 Most Stationary Pairs:")
        report_log.info("-" * 80)

        for i, row in enumerate(results_df.head(10).to_dicts(), 1):
            report_log.info(f"{i}. {row['symbol']} ({row['exchange_1']}-{row['exchange_2']})")
            report_log.info(f"   Stationarity Score: {row['stationarity_score']:.3f}")
            report_log.info(f"   Duration: {row['duration_hours']:.1f} hours")
            report_log.info("")

            report_log.info("   Stationarity Metrics:")
            adf_str = f"{row['adf_p_value']:.4f}" if row['adf_p_value'] is not None else 'N/A'
            kpss_str = f"{row['kpss_p_value']:.4f}" if row['kpss_p_value'] is not None else 'N/A'
            hurst_str = f"{row['hurst_exponent']:.3f}" if row['hurst_exponent'] is not None else 'N/A'
            half_life_str = f"{row['half_life']:.2f}" if (row['half_life'] is not None and row['half_life'] != float('inf')) else 'N/A'

            report_log.info(f"     ADF p-value: {adf_str}")
            report_log.info(f"     KPSS p-value: {kpss_str}")
            report_log.info(f"     Hurst: {hurst_str}")
            report_log.info(f"     Half-life: {half_life_str} periods")
            report_log.info("")

            report_log.info("   Profit Distribution:")
            report_log.info(f"     Median: {row['median_profit_pct']:.3f}%")
            report_log.info(f"     P75: {row['p75_profit_pct']:.3f}%")
            report_log.info(f"     P95: {row['p95_profit_pct']:.3f}%")
            report_log.info(f"     Max: {row['max_profit_pct']:.3f}%")
            report_log.info("")

            report_log.info("   Opportunity Frequency (RAW - all opportunities):")
            report_log.info(f"     >= 0.25%: {row['pct_above_0.25']:.2f}% ({row['count_above_0.25']} times)")
            report_log.info(f"     >= 0.30%: {row['pct_above_0.3']:.2f}% ({row['count_above_0.3']} times)")
            report_log.info(f"     >= 0.35%: {row['pct_above_0.35']:.2f}% ({row['count_above_0.35']} times)")
            report_log.info(f"     >= 0.40%: {row['pct_above_0.4']:.2f}% ({row['count_above_0.4']} times)")
            report_log.info("")

            report_log.info("   Tradeable Opportunities (WITH alternation filter):")
            report_log.info(f"     >= 0.25%: {row['pct_above_0.25_alternating']:.2f}% ({row['count_above_0.25_alternating']} times) - Efficiency: {row['alternation_efficiency_0.25']:.0f}%")
            report_log.info(f"     >= 0.30%: {row['pct_above_0.3_alternating']:.2f}% ({row['count_above_0.3_alternating']} times) - Efficiency: {row['alternation_efficiency_0.3']:.0f}%")
            report_log.info(f"     >= 0.35%: {row['pct_above_0.35_alternating']:.2f}% ({row['count_above_0.35_alternating']} times) - Efficiency: {row['alternation_efficiency_0.35']:.0f}%")
            report_log.info(f"     >= 0.40%: {row['pct_above_0.4_alternating']:.2f}% ({row['count_above_0.4_alternating']} times) - Efficiency: {row['alternation_efficiency_0.4']:.0f}%")
            report_log.info("")

            report_log.info("   Trading Frequency (tradeable with alternation):")
            report_log.info(f"     Opportunities/hour @ 0.25%: {row['opps_per_hour_0.25_alternating']:.1f} (raw: {row['opps_per_hour_0.25']:.1f})")
            report_log.info(f"     Opportunities/hour @ 0.30%: {row['opps_per_hour_0.3_alternating']:.1f} (raw: {row['opps_per_hour_0.3']:.1f})")
            report_log.info(f"     Opportunities/hour @ 0.35%: {row['opps_per_hour_0.35_alternating']:.1f} (raw: {row['opps_per_hour_0.35']:.1f})")
            report_log.info(f"     Opportunities/hour @ 0.40%: {row['opps_per_hour_0.4_alternating']:.1f} (raw: {row['opps_per_hour_0.4']:.1f})")
            report_log.info("")

            report_log.info("   Direction Streak Statistics:")
            report_log.info(f"     Max same direction streak @ 0.25%: {row['max_same_direction_streak_0.25']}")
            report_log.info(f"     Avg same direction streak @ 0.25%: {row['avg_same_direction_streak_0.25']:.1f}")
            report_log.info("")

            report_log.info("   Direction Changes:")
            report_log.info(f"     Total: {row['direction_changes']}")
            report_log.info(f"     Per hour: {row['direction_changes_per_hour']:.1f}")
            report_log.info("")

        report_log.info("="*80)
        report_log.info(" " * 32 + "End of Report")
        report_log.info("="*80)
    else:
        system_log.warning("No results to report.")

    system_log.info("--- Stationarity Analysis Completed ---")

def main():
    """
    Main entry point for stationarity analysis.
    """
    parser = argparse.ArgumentParser(
        description="Analyze stationarity of spreads between exchange pairs."
    )
    parser.add_argument(
        "--data-path",
        type=str,
        default="data/market_data",
        help="Root path to the partitioned market data directory."
    )
    parser.add_argument(
        "--start-date",
        type=str,
        default=None,
        help="Start date in YYYY-MM-DD format. (Optional)"
    )
    parser.add_argument(
        "--end-date",
        type=str,
        default=None,
        help="End date in YYYY-MM-DD format. (Optional)"
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
    parser.add_argument(
        "--min-data-points",
        type=int,
        default=100,
        help="Minimum number of synchronized data points required for analysis."
    )

    args = parser.parse_args()

    # Parse arguments
    start_date = datetime.strptime(args.start_date, "%Y-%m-%d") if args.start_date else None
    end_date = datetime.strptime(args.end_date, "%Y-%m-%d") if args.end_date else None
    exchanges = [e.strip() for e in args.exchanges.split(',')] if args.exchanges else []
    symbols = [s.strip() for s in args.symbols.split(',')] if args.symbols else []

    analyze_stationarity(
        data_path=args.data_path,
        start_date=start_date,
        end_date=end_date,
        exchanges=exchanges,
        symbols=symbols,
        min_data_points=args.min_data_points
    )

if __name__ == "__main__":
    main()
