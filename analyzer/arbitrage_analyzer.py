#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Vectorized Multi-Exchange Arbitrage Engine using Polars.
This version uses a robust pair-wise `asof_join` for analysis.
"""

import polars as pl
from typing import Optional, List
import logging
from itertools import combinations

class MultiExchangeArbitrageAnalyzer:
    """
    Analyzes arbitrage opportunities by performing pair-wise comparisons
    across multiple exchanges.
    """

    def __init__(self, logger: Optional[logging.Logger] = None):
        self.logger = logger if logger else logging.getLogger(__name__)

    def find_maker_taker_opportunities(
        self,
        spread_data: pl.DataFrame,
        commission_pct: float
    ) -> pl.DataFrame:
        """
        Finds arbitrage opportunities by iterating through all unique pairs of
        exchanges for a given symbol and performing a time-series join.
        """
        if spread_data.is_empty():
            return pl.DataFrame()

        unique_exchanges = spread_data['exchange'].unique().to_list()
        if len(unique_exchanges) < 2:
            self.logger.debug(f"Symbol {spread_data['symbol'][0]} is on less than 2 exchanges. Skipping.")
            return pl.DataFrame()

        all_opportunities = []

        # Iterate through all unique pairs of exchanges (e.g., (Binance, OKX), (Binance, GateIo), etc.)
        for ex_a, ex_b in combinations(unique_exchanges, 2):
            df_a = spread_data.filter(pl.col('exchange') == ex_a).sort('timestamp')
            df_b = spread_data.filter(pl.col('exchange') == ex_b).sort('timestamp')

            if df_a.is_empty() or df_b.is_empty():
                continue

            # --- Join A to B: For each tick on A, find the latest tick on B ---
            opps_a = self._find_pair_opportunities(df_a, df_b, ex_a, ex_b, commission_pct)
            if not opps_a.is_empty():
                all_opportunities.append(opps_a)

            # --- Join B to A: For each tick on B, find the latest tick on A ---
            opps_b = self._find_pair_opportunities(df_b, df_a, ex_b, ex_a, commission_pct)
            if not opps_b.is_empty():
                all_opportunities.append(opps_b)

        if not all_opportunities:
            return pl.DataFrame()

        # Combine all found opportunities and return
        return pl.concat(all_opportunities)

    def _find_pair_opportunities(
        self,
        df_left: pl.DataFrame,
        df_right: pl.DataFrame,
        exchange_left: str,
        exchange_right: str,
        commission_pct: float
    ) -> pl.DataFrame:
        """
        Helper function to find opportunities between a single pair of exchanges.
        Uses maker-taker strategy: buy with limit order (at bid price), sell with market order (at bid price).
        """
        # Rename columns to avoid conflicts after join, but keep the join key ('timestamp') the same
        cols_to_rename = [c for c in df_right.columns if c != 'timestamp']
        df_right_renamed = df_right.rename({col: f"{col}_right" for col in cols_to_rename})

        # For each row in left, find the latest row in right
        joined_df = df_left.join_asof(df_right_renamed, on='timestamp')

        # Filter for valid prices and calculate profit
        # Maker-Taker Strategy:
        # - Buy on Right with LIMIT order at bid price (maker, lower commission)
        # - Sell on Left with MARKET order at bid price (taker)
        # Profitable when: bid_left > bid_right (after commission)
        opportunities = joined_df.filter(
            (pl.col("bestBid") > 0) & (pl.col("bestBid_right") > 0) &
            (pl.col("bestBid") > pl.col("bestBid_right"))
        ).with_columns(
            (
                ((pl.col("bestBid") - pl.col("bestBid_right")) / pl.col("bestBid_right")) * 100 - commission_pct
            ).alias("profit_pct"),
            pl.lit(exchange_right).alias("buy_exchange"),
            pl.col("bestBid_right").alias("buy_price"),
            pl.lit(exchange_left).alias("sell_exchange"),
            pl.col("bestBid").alias("sell_price"),
        )

        profitable_opps = opportunities.filter(pl.col("profit_pct") > 0)

        if profitable_opps.is_empty():
            return pl.DataFrame()

        return profitable_opps.select([
            "timestamp", "symbol", "buy_exchange", "buy_price",
            "sell_exchange", "sell_price", "profit_pct"
        ])
