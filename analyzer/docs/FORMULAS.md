# Formula Documentation - Ultra-Fast Ratio Analyzer

**Version**: 4.0 (Batch Optimized)
**Last Updated**: 2025-11-03

This document outlines the metrics calculated by the `run_all_ultra.py` script. The methodology is based on batch processing of fixed deviation thresholds.

---

## 1. Core Concepts

### 1.1. Ratio
The fundamental metric representing the price relationship between two exchanges.

`ratio = bestBid_exchange1 / bestBid_exchange2`

### 1.2. Deviation (%)
Measures the percentage difference of the current `ratio` from its historical mean. This is the primary indicator for arbitrage opportunities.

`deviation_pct = ((ratio - mean(ratio)) / mean(ratio)) * 100`

---

## 2. Key Performance Metrics

### 2.1. Zero Crossings Per Minute
Measures the frequency of mean reversion. A high value indicates that the `deviation` frequently returns to its average, which is a key characteristic of a stable, arbitragable pair.

**Formula**:
`zero_crossings = sum(sign(deviation) != sign(deviation.shift(1)))`
`zero_crossings_per_minute = zero_crossings / duration_hours / 60`

- **Good Value**: > 1.0
- **Bad Value**: < 0.5 (Indicates a trending pair, not mean-reverting)

### 2.2. Opportunity Cycles
An "opportunity cycle" is counted each time the absolute `deviation` surpasses a specific threshold. This metric measures the **frequency of arbitrage opportunities** at different levels of profitability.

**Thresholds Analyzed**:
- 0.3% (30bp)
- 0.5% (50bp)
- 0.4% (40bp)

**Formula**:
`cycles_XXXbp = sum( (abs(deviation) > threshold) & (abs(deviation.shift(1)) <= threshold) )`
`cycles_XXXbp_per_hour = cycles_XXXbp / duration_hours`

### 2.3. Percent Time Above Threshold
Measures the percentage of total time that the absolute `deviation` remains above a given threshold. This helps identify pairs where the spread is permanently wide and never returns to the mean.

**Formula**:
`pct_time_above_XXXbp = mean(abs(deviation) > threshold) * 100`

- **Good Value**: < 20%
- **Bad Value**: > 50% (Indicates the spread rarely closes)

### 2.4. Average Cycle Duration (seconds)
Calculates the average time (in seconds) that an opportunity lasts, from the moment it crosses the threshold until it (theoretically) returns.

**Formula**:
`total_time_above_sec = duration_hours * 3600 * (pct_time_above_XXXbp / 100)`
`avg_cycle_duration_XXXbp_sec = total_time_above_sec / cycles_XXXbp`

- **Good Value**: Low (e.g., < 300 seconds). Indicates fast-closing opportunities.
- **Bad Value**: High. Indicates slow-closing or stuck spreads.

### 2.5. Pattern Break
A boolean flag that is `True` if the analysis period ends while the `deviation` is still above a threshold. This can indicate that the historical mean-reverting pattern is currently broken.

**Formula**:
`pattern_break_XXXbp = abs(last_deviation) > threshold`

---

## 3. Summary of Metrics in Output

The final output CSV and console summary will contain the following key columns:

| Metric | Description |
|--------|-------------|
| `symbol` | The trading symbol (e.g., BTC/USDT). |
| `exchange1`, `exchange2` | The pair of exchanges being compared. |
| `max_deviation_pct` | The maximum positive deviation observed. |
| `min_deviation_pct` | The maximum negative deviation observed. |
| `deviation_asymmetry` | Indicates directional bias. `0` is perfectly symmetric. |
| `zero_crossings_per_minute` | Core metric for mean-reversion frequency. |
| `cycles_030bp_per_hour` | Frequency of opportunities at the 0.3% level. |
| `cycles_040bp_per_hour` | Frequency of opportunities at the 0.4% level. |
| `cycles_050bp_per_hour` | Frequency of opportunities at the 0.5% level. |
| `pct_time_above_030bp` | Percent of time the spread was wider than 0.3%. |
| `avg_cycle_duration_030bp_sec` | Average duration of a 0.3% opportunity. |
| `pattern_break_030bp` | Flag indicating if the pair is currently in a >0.3% deviation state. |
| `duration_hours` | Total duration of the data analyzed. |
