#!/usr/bin/env python3
"""
Compare backtest results with stationarity analysis to find correlations.
"""

import os
import re
import polars as pl
from pathlib import Path

def parse_backtest_summary(log_file):
    """Parse backtest summary log and extract profit per symbol."""
    results = {}

    with open(log_file, 'r', encoding='utf-8') as f:
        content = f.read()

    # Extract symbol sections
    symbol_pattern = r'Symbol: (\w+)\n.*?Threshold >= 0\.25%: ([\d.]+) USDT from (\d+) trades'
    matches = re.findall(symbol_pattern, content, re.DOTALL)

    for symbol, profit, trades in matches:
        results[symbol] = {
            'profit_025': float(profit),
            'trades_025': int(trades)
        }

    # Also extract best threshold profits
    best_pattern = r'Symbol: (\w+)\n.*?Threshold >= ([\d.]+)%: ([\d.]+) USDT from (\d+) trades \(Best\)'
    best_matches = re.findall(best_pattern, content, re.DOTALL)

    for symbol, threshold, profit, trades in best_matches:
        if symbol in results:
            results[symbol]['best_profit'] = float(profit)
            results[symbol]['best_threshold'] = float(threshold)
            results[symbol]['best_trades'] = int(trades)

    return results

def load_stationarity_csv(csv_file):
    """Load stationarity analysis CSV."""
    df = pl.read_csv(csv_file)

    # Filter only Bybit-GateIo pairs
    df = df.filter(
        ((pl.col('exchange_1') == 'Bybit') & (pl.col('exchange_2') == 'GateIo')) |
        ((pl.col('exchange_1') == 'GateIo') & (pl.col('exchange_2') == 'Bybit'))
    )

    return df

def compare_results(backtest_log, stationarity_csv):
    """Compare backtest and stationarity results."""

    # Parse backtest
    backtest_data = parse_backtest_summary(backtest_log)

    # Load stationarity
    stationarity_df = load_stationarity_csv(stationarity_csv)

    # Create comparison dataframe
    comparison = []

    for symbol, bt_data in backtest_data.items():
        # Find matching stationarity data
        stat_row = stationarity_df.filter(pl.col('symbol') == symbol)

        if len(stat_row) == 0:
            print(f"Warning: No stationarity data for {symbol}")
            continue

        stat_row = stat_row[0]

        comparison.append({
            'symbol': symbol,
            'backtest_profit': bt_data.get('best_profit', bt_data['profit_025']),
            'backtest_trades': bt_data.get('best_trades', bt_data['trades_025']),
            'best_threshold': bt_data.get('best_threshold', 0.25),
            'stationarity_score': stat_row['stationarity_score'],
            'adf_p_value': stat_row['adf_p_value'],
            'hurst_exponent': stat_row['hurst_exponent'],
            'half_life': stat_row['half_life'],
            'count_above_025_alternating': stat_row['count_above_0.25_alternating'],
            'pct_above_025_alternating': stat_row['pct_above_0.25_alternating'],
            'alternation_efficiency_025': stat_row['alternation_efficiency_0.25'],
            'opps_per_hour_025_alternating': stat_row['opps_per_hour_0.25_alternating'],
            'median_profit_pct': stat_row['median_profit_pct'],
            'p95_profit_pct': stat_row['p95_profit_pct'],
        })

    comparison_df = pl.DataFrame(comparison)

    # Convert any inf values to None for CSV compatibility
    for col in comparison_df.columns:
        if comparison_df[col].dtype in [pl.Float64, pl.Float32]:
            comparison_df = comparison_df.with_columns(
                pl.when(pl.col(col).is_infinite())
                .then(None)
                .otherwise(pl.col(col))
                .alias(col)
            )

    # Sort by backtest profit descending
    comparison_df = comparison_df.sort('backtest_profit', descending=True)

    return comparison_df

def analyze_correlations(df):
    """Analyze correlations between metrics."""
    print("\n" + "="*80)
    print("CORRELATION ANALYSIS: Stationarity Metrics vs Backtest Profit")
    print("="*80 + "\n")

    # Calculate correlations
    metrics = [
        ('stationarity_score', 'Stationarity Score'),
        ('adf_p_value', 'ADF p-value'),
        ('hurst_exponent', 'Hurst Exponent'),
        ('half_life', 'Half-life'),
        ('count_above_025_alternating', 'Alternating Opps Count @ 0.25%'),
        ('pct_above_025_alternating', 'Alternating Opps % @ 0.25%'),
        ('alternation_efficiency_025', 'Alternation Efficiency @ 0.25%'),
        ('opps_per_hour_025_alternating', 'Alternating Opps/Hour @ 0.25%'),
        ('median_profit_pct', 'Median Profit %'),
        ('p95_profit_pct', 'P95 Profit %'),
    ]

    correlations = []

    for metric_col, metric_name in metrics:
        # Filter out inf and null values
        valid_df = df.filter(
            pl.col('backtest_profit').is_finite() &
            pl.col(metric_col).is_finite() &
            pl.col('backtest_profit').is_not_null() &
            pl.col(metric_col).is_not_null()
        )

        if len(valid_df) < 2:
            continue

        # Calculate Pearson correlation
        corr = valid_df.select(
            pl.corr('backtest_profit', metric_col).alias('correlation')
        )['correlation'][0]

        correlations.append({
            'metric': metric_name,
            'correlation': corr
        })

    corr_df = pl.DataFrame(correlations).sort('correlation', descending=True)

    print("Correlations with Backtest Profit:")
    print("-" * 80)
    for row in corr_df.to_dicts():
        corr_val = row['correlation']
        stars = "***" if abs(corr_val) > 0.7 else "**" if abs(corr_val) > 0.5 else "*" if abs(corr_val) > 0.3 else ""
        print(f"  {row['metric']:50s}: {corr_val:+.4f} {stars}")

    print("\n*** Strong (>0.7)  ** Moderate (>0.5)  * Weak (>0.3)\n")

    return corr_df

def main():
    # Find latest files
    logs_dir = Path("logs")
    stationarity_dir = Path("stationarity")

    # Find latest backtest
    backtest_dirs = sorted(logs_dir.glob("backtest_*"), reverse=True)
    if not backtest_dirs:
        print("No backtest logs found!")
        return

    backtest_log = backtest_dirs[0] / "summary.log"
    print(f"Using backtest: {backtest_log}")

    # Find latest stationarity CSV
    stationarity_csvs = sorted(stationarity_dir.glob("stationarity_analysis_*.csv"), reverse=True)
    if not stationarity_csvs:
        print("No stationarity CSV found!")
        return

    stationarity_csv = stationarity_csvs[0]
    print(f"Using stationarity: {stationarity_csv}")

    # Compare
    comparison_df = compare_results(str(backtest_log), str(stationarity_csv))

    # Try to save comparison (skip if error)
    try:
        output_file = "comparison_backtest_stationarity.csv"
        comparison_df.write_csv(output_file)
        print(f"\nComparison saved to: {output_file}")
    except Exception as e:
        print(f"\nWarning: Could not save CSV: {e}")

    # Show top results
    print("\n" + "="*80)
    print("TOP 10 SYMBOLS BY BACKTEST PROFIT")
    print("="*80 + "\n")

    for i, row in enumerate(comparison_df.head(10).to_dicts(), 1):
        print(f"{i}. {row['symbol']:15s} - Profit: ${row['backtest_profit']:7.2f} USDT  "
              f"Trades: {row['backtest_trades']:3d}  "
              f"Stat.Score: {row['stationarity_score']:.3f}")
        print(f"   {'':15s}   AltOpps/hr: {row['opps_per_hour_025_alternating']:.2f}  "
              f"AltEff: {row['alternation_efficiency_025']:.1f}%  "
              f"Hurst: {row['hurst_exponent']:.3f}")
        print()

    # Analyze correlations
    analyze_correlations(comparison_df)

    print("\n" + "="*80)
    print("KEY INSIGHTS")
    print("="*80)
    print("\nLook for metrics with high correlation to backtest profit.")
    print("These are the best predictors of trading profitability!")

if __name__ == "__main__":
    main()
