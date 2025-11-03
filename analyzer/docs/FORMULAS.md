# Formula Documentation - Ratio Analyzer

Comprehensive documentation of all metrics and formulas used in the arbitrage analysis.
**Version**: 3.0 (Event-Peak Analysis)

---

## Core Metrics

### 1. Ratio
`ratio = bestBid_exchange1 / bestBid_exchange2`
Primary metric showing relative price between exchanges.

### 2. Deviation (%)
`deviation_pct = ((ratio - mean(ratio)) / mean(ratio)) * 100`
Measures how far the current ratio is from the mean.

---

## Mean Reversion Metrics

### 3. Zero Crossings
`zero_crossings = sum(np.diff(np.sign(deviation)) != 0)`
Counts how many times deviation crosses zero (returns to mean).

### 4. Zero Crossings Per Minute
`zero_crossings_per_minute = zero_crossings / duration_hours / 60`
Normalized frequency of mean reversion.

---

## Event-Based Opportunity Analysis

This methodology focuses on analyzing discrete **Opportunity Events** rather than individual data points. An event is a full cycle of the spread widening beyond a threshold and then returning.

### 5. Opportunity Events
`opportunity_events = (is_above & ~is_above.shift(1)).sum()`
Counts the number of times the spread **enters** an opportunity state. This measures the **frequency** of new opportunities.

### 6. Opportunity Data Points (Ticks)
`total_opportunity_ticks = is_opportunity_tick.sum()`
Counts the total number of data points where the spread was wide. This is a measure of **total duration**, not frequency.

### 7. Average Opportunity Duration
`avg_opportunity_duration_ticks = total_opportunity_ticks / opportunity_events`
Calculates the average length of a single trading opportunity in ticks.

### 8. Event Peak Analysis (NEW)
This is the core of the new methodology. For each individual Opportunity Event, we find the maximum deviation reached during that event.

**Algorithm**:
1.  Identify contiguous blocks of `True` in the `is_above` boolean series. Each block is an event.
2.  For each block, find the maximum value of `abs(deviation_pct)` within its boundaries.
3.  Collect all these maximum values into a list called `event_peaks`.

**Example**:
`deviation: [0.2, 0.4, 0.8, 0.5, 0.1, 0.4, 0.6, 0.2]`
`threshold: 0.3%`
-   **Event 1**: `[0.4, 0.8, 0.5]` -> Peak is **0.8%**
-   **Event 2**: `[0.4, 0.6]` -> Peak is **0.6%**
`event_peaks = [0.8, 0.6]`

### 9. Event Peak Metrics (NEW)

#### 9.1. Average Event Peak
`avg_event_peak_pct = mean(event_peaks)`
The average of all recorded peak values. Answers: "How strong is a typical opportunity?"

#### 9.2. Max Event Peak
`max_event_peak_pct = max(event_peaks)`
The single highest peak recorded across all events.

#### 9.3. Event Peak Distribution
This is a histogram of the `event_peaks` list, categorized into buckets.
-   `peaks_03_05_pct`: Count of events with peak in [0.3%, 0.5%)
-   `peaks_05_10_pct`: Count of events with peak in [0.5%, 1.0%)
-   `peaks_10_plus_pct`: Count of events with peak >= 1.0%

**Purpose**: To understand the **quality** and **character** of opportunities. A pair with many events but all in the `0.3-0.5%` bucket is less interesting than a pair with fewer events that mostly fall into the `>1.0%` bucket.

---

## Other Metrics

### 10. Percent Time Above Threshold
`pct_time_above = (total_opportunity_ticks / data_points) * 100`
Percentage of time when spread exceeds threshold.

### 11. Data Points & Duration Hours
`data_points = len(timestamps)`
`duration_hours = (max_timestamp - min_timestamp).total_seconds() / 3600`

---

## Key Formulas Summary Table

| Metric | Good Value | Bad Value | Purpose |
|--------|------------|-----------|---------|
| zero_crossings_per_minute | > 5 | < 0.5 | Frequency of mean reversion |
| opportunity_events_per_hour | > 100 | < 10 | Frequency of new opportunities |
| avg_opportunity_duration_ticks | < 100 | > 1000 | Speed of spread closure |
| **avg_event_peak_pct** | > 0.5% | < 0.35% | Average strength of an opportunity |
| **pct_of_peaks_gt_05** | > 20% | < 5% | Share of high-quality opportunities |
| pct_time_above_03pct | 5-20% | > 50% | Indicates if spread ever closes |

---

## Filtering Rules for Good Pairs

### Rule 1: Mean Reversion Check
`if zero_crossings_per_minute < 1.0: verdict = "TRENDING - AVOID"`

### Rule 2: Spread Persistence Check
`if pct_time_above_03pct > 50%: verdict = "SPREAD DOESN'T CLOSE - AVOID"`

### Rule 3: Opportunity Quality Check (NEW)
`if avg_event_peak_pct < 0.4 and pct_of_peaks_gt_05 < 10%: verdict = "WEAK OPPORTUNITIES - AVOID"`

### Rule 4: Ideal Pair Criteria (NEW)
```python
if (zero_crossings_per_minute > 5.0 and
    opportunity_events_per_hour > 100 and
    avg_event_peak_pct > 0.5 and
    pct_of_peaks_gt_05 > 20%):
    verdict = "EXCELLENT: Frequent & Strong Opportunities"
```

---

## Example Analysis: ZKUSDT (Binance vs MEXC)

**Raw Metrics**:
```
zero_crossings_per_minute: 397.95
opportunity_events_per_hour: 2,799
avg_opportunity_duration_ticks: 2.0
pct_time_above_03pct: 1.64%
avg_event_peak_pct: 0.45% (hypothetical)
event_peak_distribution:
  0.3-0.5%: 80%
  0.5-1.0%: 18%
  >1.0%: 2%
```

**Interpretation**:
1. ✅ **Extreme Mean Reversion** (397 crossings/min).
2. ✅ **Very Frequent Events** (2,799/hr).
3. ✅ **Instant Spread Closure** (avg duration 2.0 ticks).
4. ✅ **Good Peak Quality**: Average peak is 0.45%, and **20%** of all events (`18% + 2%`) are strong opportunities (>0.5%).

**Verdict**: EXCELLENT - Frequent, strong, and fast-closing opportunities.

---

## Example Analysis: MINAUSDT (Binance vs Bybit) - BAD PAIR

**Raw Metrics**:
```
zero_crossings_per_minute: 0.29
opportunity_events_per_hour: 6.8
avg_opportunity_duration_ticks: 4398.9
pct_time_above_03pct: 97.44%
avg_event_peak_pct: 0.31% (hypothetical)
event_peak_distribution:
  0.3-0.5%: 99%
  0.5-1.0%: 1%
  >1.0%: 0%
```

**Interpretation**:
1. ❌ **Trending** (0.29 crossings/min).
2. ❌ **Almost No Events** (6.8/hr).
3. ❌ **Spread Never Closes** (avg duration ~4400 ticks, 97% time above).
4. ❌ **Extremely Weak Peaks**: Average peak is barely above the threshold, and almost no events are >0.5%.

**Verdict**: AVOID - Trending pair with rare and weak opportunities.

---

**Last Updated**: 2025-11-03
**Version**: 3.0
**Author**: Claude with Sequential Thinking Verification
