# Analyzer Metrics and Algorithms

This document provides a detailed explanation of the key metrics calculated by the `analyzer` and the algorithms used within the `analyze_pair_fast` function.

## 1. Core Concepts: Synchronization and Deviation

The analysis starts by synchronizing time-series data and calculating the fundamental price deviation.

1.  **Synchronization (`join_asof`):**
    *   Data from two exchanges (`data1`, `data2`) is synchronized by time. For each row in `data1`, the nearest preceding row from `data2` is found. This creates a unified `DataFrame` containing `bid_ex1`, `ask_ex1`, `bid_ex2`, and `ask_ex2`.

2.  **Ratio Calculation:**
    *   A `ratio` column is computed as `bid_ex1 / bid_ex2`. This shows how the price on one exchange differs from the other.

3.  **Deviation Calculation:**
    *   **Formula:** `((ratio - 1.0) / 1.0) * 100`
    *   **Rationale:** This is a critical step. Deviation is measured from `1.0` (price parity), not from the mean of the ratio. This ensures that a `deviation` of `0` signifies that prices are equal, representing a break-even point for an arbitrage trade. The result is expressed as a percentage.

## 2. Key Performance Metrics

These metrics are designed to identify pairs with strong mean-reverting characteristics and frequent, tradeable opportunities.

| Metric | Formula & Explanation | Why It's Important |
|---|---|---|
| **Deviation (%)** | `((ratio - 1.0) / 1.0) * 100` <br> Measures deviation from price parity (1.0). A value of 0 means prices are equal. | This is the core indicator of an arbitrage opportunity. It directly shows the potential profit margin before fees. |
| **Complete Opportunity Cycles** | A cycle is counted only when the deviation: <br> 1. Was above a threshold (e.g., 0.4%) <br> 2. And then **returned to the neutral zone** (`abs(deviation) < 0.05%`). <br><br> **Algorithm:** The `count_complete_cycles` function iterates through the data with a `was_above` flag. A cycle is counted when the `deviation` enters the neutral zone *after* the flag was set, preventing false positives from spreads that never close. | This is the most critical metric for traders as it counts **real, closeable opportunities**, filtering out periods where the price spread gets stuck. |
| **Zero Crossings per Minute** | `(sign(dev) * sign(dev.shift(1)) < 0).sum() / duration_minutes` <br> Counts how often the deviation crosses the 0% mark, indicating a true sign flip. | A high value signifies strong, symmetric mean-reversion, which is the ideal characteristic of a stable arbitrage pair. |
| **Deviation Asymmetry** | `mean(deviation)` <br> The average deviation over the period. | A value near 0 indicates symmetric oscillation. A high positive or negative value reveals a **directional bias**, making it risky to trade as the price may not return to zero. |
| **`cycles_..._per_hour`** | `opportunity_cycles / duration_hours` <br> Normalizes the cycle count over time. | Allows for fair comparison of opportunity frequency between pairs, regardless of the analysis duration. |
| **`pct_time_above_...`** | `mean(abs(deviation) > threshold) * 100` <br> Percentage of time the deviation was wider than the threshold. | Must be analyzed **together with cycle count**. High `pct_time` with low `cycles` indicates a stuck, untradeable spread. |
| **`avg_cycle_duration_..._sec`** | `(total_time_above_threshold_sec) / opportunity_cycles` <br> Average duration of a single opportunity in seconds. | Helps estimate how quickly a position needs to be opened and closed. Short durations (<60s) are for bots; longer durations (1-5min) can be handled manually. |
| **`pattern_break_...`** | A boolean flag that is `True` if the data series ends while the deviation is still above the threshold. | This can indicate a "regime change" or pattern breakdown, suggesting that the historical mean-reverting behavior may no longer hold. |