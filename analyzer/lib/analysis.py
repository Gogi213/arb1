"""
Core analysis functions for arbitrage opportunity detection.

Implements mean-reversion analysis for price ratio deviations between exchanges.
"""

import polars as pl
from typing import Optional, Dict, Any, List


def count_complete_cycles(above_threshold_series, in_neutral_series) -> int:
    """
    Count complete arbitrage cycles.

    A cycle completes when:
    1. We were above threshold at some point
    2. We returned to neutral zone (can close position)

    This prevents counting false opportunities that never return to zero.

    Args:
        above_threshold_series: Polars Series of bool - True when deviation > threshold
        in_neutral_series: Polars Series of bool - True when deviation in neutral zone

    Returns:
        Number of complete cycles
    """
    above = above_threshold_series.to_numpy()
    neutral = in_neutral_series.to_numpy()

    cycles = 0
    was_above = False

    for i in range(len(above)):
        if above[i]:
            was_above = True
        elif neutral[i] and was_above:
            # Completed a cycle: was above threshold, now returned to neutral
            cycles += 1
            was_above = False

    return cycles


def analyze_pair_fast(
    symbol: str,
    ex1: str,
    ex2: str,
    data1: pl.DataFrame,
    data2: pl.DataFrame,
    thresholds: Optional[List[float]] = None,
    zero_threshold: float = 0.05
) -> Optional[Dict[str, Any]]:
    """
    Fast pair analysis - OPTIMIZED with Polars operations.

    Analyzes the ratio deviation between two exchanges for mean-reversion patterns.

    Args:
        symbol: Symbol name (e.g., "BTC/USDT")
        ex1: First exchange name
        ex2: Second exchange name
        data1: DataFrame for first exchange (columns: timestamp, bestBid, bestAsk)
        data2: DataFrame for second exchange (columns: timestamp, bestBid, bestAsk)
        thresholds: List of profitability thresholds in % (default: [0.3, 0.5, 0.4])
        zero_threshold: Neutral zone threshold in % (default: 0.05)

    Returns:
        Dictionary with analysis metrics or None if analysis fails

    Metrics returned:
        - max_deviation_pct: Maximum deviation from price parity
        - min_deviation_pct: Minimum deviation from price parity
        - deviation_asymmetry: Average deviation (directional bias indicator)
        - zero_crossings: Number of times deviation crosses zero
        - zero_crossings_per_hour: Zero crossings normalized per hour
        - zero_crossings_per_minute: Zero crossings normalized per minute
        - opportunity_cycles_XXXbp: Number of complete cycles for each threshold
        - cycles_XXXbp_per_hour: Cycles per hour for each threshold
        - pct_time_above_XXXbp: % of time deviation > threshold
        - avg_cycle_duration_XXXbp_sec: Average cycle duration in seconds
        - pattern_break_XXXbp: True if last deviation > threshold (pattern breaking)
        - data_points: Number of data points analyzed
        - duration_hours: Analysis duration in hours
    """
    # Synchronize data using join_asof (backward strategy - no look-ahead bias)
    try:
        joined = data1.rename({
            'bestBid': 'bid_ex1',
            'bestAsk': 'ask_ex1'
        }).join_asof(
            data2.rename({
                'bestBid': 'bid_ex2',
                'bestAsk': 'ask_ex2'
            }),
            on='timestamp'
        )

        if joined.is_empty():
            return None

        # OPTIMIZATION #4: Pure Polars operations (1.5-2x faster, zero-copy)
        # Calculate ratio and statistics in Polars
        joined = joined.with_columns([
            (pl.col('bid_ex1') / pl.col('bid_ex2')).alias('ratio')
        ])

        # CRITICAL FIX: Calculate deviation from 1.0, NOT from mean!
        # For arbitrage, we need to know deviation from PRICE EQUALITY, not from average
        # deviation = 0 means prices are equal → can close position at break-even
        # If we used mean_ratio, deviation = 0 would NOT guarantee break-even close!
        joined = joined.with_columns([
            ((pl.col('ratio') - 1.0) / 1.0 * 100).alias('deviation')
        ])

        # All aggregations in pure Polars (no NumPy conversion)
        max_deviation_pct = float(joined['deviation'].max())
        min_deviation_pct = float(joined['deviation'].min())
        mean_deviation_pct = float(joined['deviation'].mean())

        # Calculate asymmetry (directional bias indicator)
        # Now using deviation from 1.0 (price equality)
        # asymmetry = average deviation from zero
        # For symmetric oscillation around parity: asymmetry ≈ 0
        # For persistent bias (e.g., always +0.3%): |asymmetry| > 0.2
        asymmetry = mean_deviation_pct

        # Zero crossings in pure Polars
        # FIXED: Use multiplication to detect true sign flips (+1 to -1 or vice versa)
        # This prevents counting transitions through exactly 0.0 as two separate events
        # sign[i] * sign[i-1] < 0 only when crossing from positive to negative (or vice versa)
        deviation_sign = pl.col('deviation').sign()
        zero_crossings = int(
            joined.with_columns([
                (deviation_sign * deviation_sign.shift(1) < 0).alias('crossed')
            ])['crossed'].sum()
        )

        # Time range
        min_timestamp = joined['timestamp'].min()
        max_timestamp = joined['timestamp'].max()
        duration_hours = (max_timestamp - min_timestamp).total_seconds() / 3600

        # Calculate zero crossings per time
        zero_crossings_per_hour = zero_crossings / duration_hours if duration_hours > 0 else 0
        zero_crossings_per_minute = zero_crossings_per_hour / 60 if duration_hours > 0 else 0

        # BATCH OPTIMIZATION: Calculate all thresholds in one pass
        # CORRECTED LOGIC: Count only COMPLETE cycles that return to ZERO
        #
        # A cycle = movement from ~zero → above threshold → back to ~zero
        # This ensures we only count tradeable opportunities (can close position at break-even)

        # Use provided thresholds or defaults
        if thresholds is None:
            thresholds = [0.3, 0.5, 0.4]

        joined_with_thresholds = joined.with_columns([
            # Above threshold flags
            (pl.col('deviation').abs() > thresholds[0]).alias('above_030bp'),
            (pl.col('deviation').abs() > thresholds[1]).alias('above_050bp'),
            (pl.col('deviation').abs() > thresholds[2]).alias('above_040bp'),
            # In neutral zone flag
            (pl.col('deviation').abs() < zero_threshold).alias('in_neutral')
        ])

        # Count COMPLETE cycles using correct logic
        # Cycle = return to neutral AFTER being above threshold
        cycles_030bp = count_complete_cycles(
            joined_with_thresholds['above_030bp'],
            joined_with_thresholds['in_neutral']
        )
        cycles_050bp = count_complete_cycles(
            joined_with_thresholds['above_050bp'],
            joined_with_thresholds['in_neutral']
        )
        cycles_040bp = count_complete_cycles(
            joined_with_thresholds['above_040bp'],
            joined_with_thresholds['in_neutral']
        )

        # Calculate percentage of time above thresholds
        threshold_metrics = joined_with_thresholds.select([
            (pl.col('above_030bp').mean() * 100).alias('pct_030bp'),
            (pl.col('above_050bp').mean() * 100).alias('pct_050bp'),
            (pl.col('above_040bp').mean() * 100).alias('pct_040bp')
        ])

        # Extract results
        metrics = threshold_metrics.row(0, named=True)

        pct_030bp = float(metrics['pct_030bp'])
        pct_050bp = float(metrics['pct_050bp'])
        pct_040bp = float(metrics['pct_040bp'])

        # Average duration per cycle (in seconds)
        avg_duration_030bp_sec = (duration_hours * pct_030bp / 100 * 3600) / cycles_030bp if cycles_030bp > 0 else 0
        avg_duration_050bp_sec = (duration_hours * pct_050bp / 100 * 3600) / cycles_050bp if cycles_050bp > 0 else 0
        avg_duration_040bp_sec = (duration_hours * pct_040bp / 100 * 3600) / cycles_040bp if cycles_040bp > 0 else 0

        # Pattern break detection: check if last cycle is incomplete (didn't return below threshold)
        # If deviation ends above threshold, pattern may be breaking
        last_deviation = float(joined['deviation'][-1])
        pattern_break_030bp = abs(last_deviation) > thresholds[0]
        pattern_break_050bp = abs(last_deviation) > thresholds[1]
        pattern_break_040bp = abs(last_deviation) > thresholds[2]

        threshold_stats = {
            'opportunity_cycles_030bp': cycles_030bp,
            'cycles_030bp_per_hour': cycles_030bp / duration_hours if duration_hours > 0 else 0,
            'pct_time_above_030bp': pct_030bp,
            'avg_cycle_duration_030bp_sec': avg_duration_030bp_sec,
            'pattern_break_030bp': pattern_break_030bp,

            'opportunity_cycles_050bp': cycles_050bp,
            'cycles_050bp_per_hour': cycles_050bp / duration_hours if duration_hours > 0 else 0,
            'pct_time_above_050bp': pct_050bp,
            'avg_cycle_duration_050bp_sec': avg_duration_050bp_sec,
            'pattern_break_050bp': pattern_break_050bp,

            'opportunity_cycles_040bp': cycles_040bp,
            'cycles_040bp_per_hour': cycles_040bp / duration_hours if duration_hours > 0 else 0,
            'pct_time_above_040bp': pct_040bp,
            'avg_cycle_duration_040bp_sec': avg_duration_040bp_sec,
            'pattern_break_040bp': pattern_break_040bp
        }

        return {
            'max_deviation_pct': max_deviation_pct,
            'min_deviation_pct': min_deviation_pct,
            'deviation_asymmetry': asymmetry,
            'zero_crossings': zero_crossings,
            'zero_crossings_per_hour': zero_crossings_per_hour,
            'zero_crossings_per_minute': zero_crossings_per_minute,
            **threshold_stats,
            'data_points': len(joined),
            'duration_hours': duration_hours
        }
    except Exception as e:
        print(f"Error in analyze_pair_fast: {e}")
        import traceback
        traceback.print_exc()
        return None
