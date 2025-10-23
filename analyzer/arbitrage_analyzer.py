#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Vectorized Multi-Exchange Arbitrage Engine using Polars.
"""

import polars as pl
from typing import List, Dict, Optional
import logging

class MultiExchangeArbitrageAnalyzer:
    """
    Analyzes arbitrage opportunities across multiple exchanges using vectorized operations.
    """

    def __init__(self, logger: Optional[logging.Logger] = None):
        self.logger = logger if logger else logging.getLogger(__name__)

    def find_maker_taker_opportunities(
        self,
        spread_data: pl.DataFrame,
        commission_pct: float
    ) -> pl.DataFrame:
        """
        Finds Maker-Taker arbitrage opportunities using a vectorized approach.
        This strategy involves placing a limit buy order (Maker) on the exchange
        with the lower bid, and simultaneously placing a market sell order (Taker)
        on the exchange with the higher bid.

        Args:
            spread_data: A Polars DataFrame with market data for a single symbol.
            commission_pct: The commission percentage for the Taker side of the trade.
                          The Maker side is assumed to have 0% commission.

        Returns:
            A Polars DataFrame containing all profitable opportunities.
        """
        if spread_data.is_empty() or spread_data.height < 2:
            self.logger.debug("Spread data is empty or has less than 2 rows, cannot find opportunities.")
            return pl.DataFrame()

        # Ensure required columns exist
        required_cols = {'timestamp', 'bestBid', 'exchange', 'symbol'}
        if not required_cols.issubset(spread_data.columns):
            self.logger.warning(f"Spread data is missing required columns. Got: {spread_data.columns}")
            return pl.DataFrame()

        # Create all possible pairs of exchanges for each timestamp
        df_a = spread_data.rename({col: col + "_a" for col in spread_data.columns if col != 'timestamp'})
        df_b = spread_data.rename({col: col + "_b" for col in spread_data.columns if col != 'timestamp'})

        opportunities_df = df_a.join(
            df_b,
            on="timestamp",
            how="inner"
        ).filter(
            pl.col("exchange_a") != pl.col("exchange_b")
        )

        if opportunities_df.is_empty():
            self.logger.debug("No overlapping timestamps found between different exchanges to create pairs.")
            return pl.DataFrame()

        # Pre-filter to ensure prices are valid
        opportunities_df = opportunities_df.filter(
            (pl.col("bestBid_a").is_not_null()) & (pl.col("bestBid_a") > 0) &
            (pl.col("bestBid_b").is_not_null()) & (pl.col("bestBid_b") > 0)
        )

        if opportunities_df.is_empty():
            self.logger.debug("No valid price pairs found after filtering for nulls and zero prices.")
            return pl.DataFrame()

        # --- Scenario 1: Buy on A (Maker), Sell on B (Taker) ---
        opps_a_to_b = opportunities_df.filter(
            pl.col("bestBid_b") > pl.col("bestBid_a")
        ).with_columns(
            (
                ((pl.col("bestBid_b") - pl.col("bestBid_a")) / pl.col("bestBid_a")) * 100 - commission_pct
            ).alias("profit_pct"),
            pl.col("exchange_a").alias("buy_exchange"),
            pl.col("bestBid_a").alias("buy_price"),
            pl.col("exchange_b").alias("sell_exchange"),
            pl.col("bestBid_b").alias("sell_price"),
        )

        # --- Scenario 2: Buy on B (Maker), Sell on A (Taker) ---
        opps_b_to_a = opportunities_df.filter(
            pl.col("bestBid_a") > pl.col("bestBid_b")
        ).with_columns(
            (
                ((pl.col("bestBid_a") - pl.col("bestBid_b")) / pl.col("bestBid_b")) * 100 - commission_pct
            ).alias("profit_pct"),
            pl.col("exchange_b").alias("buy_exchange"),
            pl.col("bestBid_b").alias("buy_price"),
            pl.col("exchange_a").alias("sell_exchange"),
            pl.col("bestBid_a").alias("sell_price"),
        )

        # Combine and filter for profitable opportunities
        all_opps = pl.concat([opps_a_to_b, opps_b_to_a])

        if all_opps.is_empty():
            self.logger.debug("No potential opportunities were generated from price comparisons.")
            return pl.DataFrame()

        profitable_opportunities = all_opps.filter(pl.col("profit_pct") > 0)

        if profitable_opportunities.is_empty():
            self.logger.debug("No opportunities were found to be profitable after accounting for commission.")
            return pl.DataFrame()

        # Select and standardize columns for the final output
        final_df = profitable_opportunities.select([
            pl.col("timestamp"),
            pl.col("symbol_a").alias("symbol"),
            pl.col("buy_exchange"),
            pl.col("buy_price"),
            pl.col("sell_exchange"),
            pl.col("sell_price"),
            pl.col("profit_pct")
        ])

        return final_df
