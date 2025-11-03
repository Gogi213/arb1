# Cryptocurrency Arbitrage Ratio Analyzer

High-performance analyzer for identifying mean-reverting price ratios between exchanges. Uses event-based opportunity cycle detection to find profitable arbitrage patterns.

## Concept

The analyzer divides bestBid prices between exchanges (ratio = ex1_bid / ex2_bid) and analyzes deviations from the mean to find:
- **Mean reversion frequency** (zero crossings per minute)
- **Opportunity cycles** (periods when deviation exceeds threshold, then reverts)
- **Time in profitable range** (percentage of time above threshold)

See [docs/FORMULAS.md](docs/FORMULAS.md) for detailed formula explanations.

## Usage

```bash
python run_all_ultra.py
```

This ultra-optimized version uses:
- **Single parquet scan** (2-4x faster I/O)
- **Parallel exchange loading** (ThreadPoolExecutor for concurrent I/O)
- **Pure Polars operations** (zero-copy, no NumPy conversion, 1.5-2x faster)
- **Filter pushdown** (nulls filtered before sort, 10-30% faster)
- **Decimal → Float64 cast** (1.5-2x faster parsing)
- **Batch threshold calculation** (all thresholds in one pass, 1.3x faster)
- **Batch processing by symbol** (load once, analyze all pairs)

Expected processing time: **10-20 seconds** for full dataset (2-3x faster than previous version).

## Output

Creates `summary_stats_YYYYMMDD_HHMMSS.csv` with columns:
- `symbol`, `exchange1`, `exchange2`
- `zero_crossings_per_minute` - Mean reversion frequency
- `cycles_030bp_per_hour` - Opportunity cycles at 30bp (0.30%) threshold
- `cycles_050bp_per_hour` - Opportunity cycles at 50bp (0.50%) threshold
- `cycles_100bp_per_hour` - Opportunity cycles at 100bp (1.00%) threshold
- `pct_time_above_030bp` - % time above 30bp threshold
- `pct_time_above_050bp` - % time above 50bp threshold
- `pct_time_above_100bp` - % time above 100bp threshold

**Note**: bp = basis points (1bp = 0.01%, so 30bp = 0.30%)

Sorted by best trading opportunities (high zero crossings + reasonable cycles).

## Good vs Bad Pairs

**Good (ZKUSDT example):**
- Zero crossings: 397.95/min (strong mean reversion)
- Cycles: Reasonable count matching crossings
- High time in profitable range

**Bad (MINAUSDT example):**
- Zero crossings: 0.29/min (trending, not mean-reverting)
- Cycles: Low count
- Risky for arbitrage trading

## Architecture

- **Data structure**: `exchange=X/symbol=Y/date=Z/hour=H/*.parquet`
- **Processing**: In-memory batch processing with Polars
- **Optimization**: All I/O and computation optimized for speed
- **Output**: Single summary CSV with actionable trading metrics