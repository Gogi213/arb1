#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Vectorized Multi-Exchange Arbitrage Engine using Polars.
"""

import polars as pl
from typing import List, Dict, Optional

class MultiExchangeArbitrageAnalyzer:
    """
    Analyzes arbitrage opportunities across multiple exchanges using vectorized operations.
    """

    def __init__(self):
        pass

    def find_maker_taker_opportunities(
        self,
        spread_data: pl.DataFrame,
        commission_pct: float
    ) -> pl.DataFrame:
        """
        Finds Maker/Taker opportunities using a vectorized approach with Polars.
        This is the realistic scenario: Buy at Bid (as a Maker), Sell at Bid (as a Taker).
        
        Args:
            spread_data: A Polars DataFrame with market data for a single symbol.
            commission_pct: The commission percentage for trades.

        Returns:
            A Polars DataFrame containing all profitable opportunities.
        """
        if spread_data.is_empty() or spread_data.height < 2:
            return pl.DataFrame()

        # Create all possible pairs of exchanges for each timestamp
        df_a = spread_data.rename({col: col + "_a" for col in spread_data.columns if col != 'timestamp'})
        df_b = spread_data.rename({col: col + "_b" for col in spread_data.columns if col != 'timestamp'})

        opportunities_df = df_a.join(
            df_b, 
            on="timestamp", 
            how="inner"
        )

        # Filter out pairs from the same exchange
        opportunities_df = opportunities_df.filter(
            pl.col("exchange_a") != pl.col("exchange_b")
        )

        if opportunities_df.is_empty():
            return pl.DataFrame()

        # Pre-filter to ensure prices are valid before any calculation
        opportunities_df = opportunities_df.filter(
            (pl.col("bestBid_a").is_not_null()) & (pl.col("bestBid_a") > 0) &
            (pl.col("bestBid_b").is_not_null()) & (pl.col("bestBid_b") > 0)
        )

        if opportunities_df.is_empty():
            return pl.DataFrame()

        # Vectorized calculation of profit for Maker/Taker model
        # Buy at Bid price on exchange A (Maker), Sell at Bid price on exchange B (Taker)
        # Maker fee is 0, Taker fee is commission_pct
        opportunities_df = opportunities_df.with_columns([
            pl.lit(commission_pct).alias("total_commission"),
            (
                ((pl.col("bestBid_b") - pl.col("bestBid_a")) / pl.col("bestBid_a")) * 100
            ).alias("gross_profit_pct")
        ]).with_columns(
            (pl.col("gross_profit_pct") - pl.col("total_commission")).alias("net_profit_pct")
        )

        # Filter for profitable opportunities
        profitable_opportunities = opportunities_df.filter(pl.col("net_profit_pct") > 0)

        # Rename columns for final output
        final_df = profitable_opportunities.select([
            pl.col("timestamp"),
            pl.col("symbol_a").alias("symbol"),
            pl.col("exchange_a").alias("buy_exchange"),
            pl.col("bestBid_a").alias("buy_price"),
            pl.col("exchange_b").alias("sell_exchange"),
            pl.col("bestBid_b").alias("sell_price"),
            pl.col("net_profit_pct").alias("profit_pct")
        ])

        return final_df
