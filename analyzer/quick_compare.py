#!/usr/bin/env python3
"""Quick correlation analysis between backtest and stationarity."""

import re
import polars as pl

# Find latest backtest
from pathlib import Path
latest_backtest = sorted(Path("logs").glob("backtest_*/summary.log"))[-1]
print(f"Using backtest: {latest_backtest}")

# Parse backtest
with open(latest_backtest, 'r') as f:
    content = f.read()

backtest = {}
pattern = r'Symbol: (\w+)\n.*?Threshold >= ([\d.]+)%: ([\d.]+) USDT from (\d+) trades \(Best\)'
for symbol, threshold, profit, trades in re.findall(pattern, content, re.DOTALL):
    backtest[symbol] = {'profit': float(profit), 'trades': int(trades)}

# Load stationarity
latest_stationarity = sorted(Path("stationarity").glob("stationarity_analysis_*.csv"))[-1]
print(f"Using stationarity: {latest_stationarity}\n")

df = pl.read_csv(str(latest_stationarity))
df = df.filter(
    ((pl.col('exchange_1') == 'Bybit') & (pl.col('exchange_2') == 'GateIo')) |
    ((pl.col('exchange_1') == 'GateIo') & (pl.col('exchange_2') == 'Bybit'))
)

# Match and print
print("\n" + "="*100)
print("CORRELATION ANALYSIS: Stationarity Metrics vs Backtest Profit (Bybit-GateIo)")
print("="*100 + "\n")

print(f"{'Symbol':<15} {'Profit':>10} {'Trades':>8} {'StatScore':>10} {'AltOpp/hr':>10} {'AltEff%':>10} {'Hurst':>8}")
print("-"*100)

symbols_sorted = sorted(backtest.items(), key=lambda x: x[1]['profit'], reverse=True)

for symbol, bt_data in symbols_sorted:
    stat = df.filter(pl.col('symbol') == symbol)
    if len(stat) == 0:
        continue

    row = stat.to_dicts()[0]
    print(f"{symbol:<15} ${bt_data['profit']:>9.2f} {bt_data['trades']:>8d} "
          f"{row['stationarity_score']:>10.3f} "
          f"{row['opps_per_hour_0.25_alternating']:>10.2f} "
          f"{row['alternation_efficiency_0.25']:>10.1f} "
          f"{row['hurst_exponent'] if row['hurst_exponent'] else 0.0:>8.3f}")

print("\n" + "="*100)
print("CORRELATION COEFFICIENTS")
print("="*100)

# Calculate correlations
comparison = []
for symbol, bt_data in backtest.items():
    stat = df.filter(pl.col('symbol') == symbol)
    if len(stat) == 0:
        continue
    row = stat.to_dicts()[0]
    comparison.append({
        'profit': bt_data['profit'],
        'trades': bt_data['trades'],
        'stat_score': row['stationarity_score'],
        'alt_opp_hr': row['opps_per_hour_0.25_alternating'],
        'alt_eff': row['alternation_efficiency_0.25'],
        'hurst': row['hurst_exponent'] if row['hurst_exponent'] else None,
        'half_life': row['half_life'] if (row['half_life'] and row['half_life'] != float('inf')) else None,
    })

comp_df = pl.DataFrame(comparison)

metrics = [
    ('stat_score', 'Stationarity Score'),
    ('alt_opp_hr', 'Alternating Opps/Hour @ 0.25%'),
    ('alt_eff', 'Alternation Efficiency @ 0.25%'),
    ('hurst', 'Hurst Exponent'),
    ('half_life', 'Half-life'),
    ('trades', 'Number of Trades'),
]

print(f"\n{'Metric':<45} {'Correlation':>12} {'Strength':>12}")
print("-"*70)

for col, name in metrics:
    valid = comp_df.filter(
        pl.col('profit').is_finite() &
        pl.col(col).is_finite() &
        pl.col('profit').is_not_null() &
        pl.col(col).is_not_null()
    )

    if len(valid) < 3:
        continue

    corr = valid.select(pl.corr('profit', col))['profit'][0]

    if abs(corr) > 0.7:
        strength = "STRONG ***"
    elif abs(corr) > 0.5:
        strength = "MODERATE **"
    elif abs(corr) > 0.3:
        strength = "WEAK *"
    else:
        strength = "NONE"

    print(f"{name:<45} {corr:>+12.4f} {strength:>12}")

print("\n" + "="*100)
print("KEY INSIGHTS")
print("="*100)
print("\n✓ Positive correlation = metric predicts higher profits")
print("✓ Negative correlation = metric predicts lower profits")
print("\nLook for STRONG correlations to prioritize trading pairs!\n")
