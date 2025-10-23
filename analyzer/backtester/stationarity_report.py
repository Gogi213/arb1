#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Generates a comprehensive stationarity report for all symbol pairs.
"""

import polars as pl
import itertools
from typing import List, Dict, Optional
import logging
import os

from .stationarity_analyzer import (
    calculate_stationarity_metrics,
    calculate_stationarity_score,
    calculate_cointegration_test,
    calculate_half_life,
    calculate_zero_crossings
)
from arbitrage_analyzer import MultiExchangeArbitrageAnalyzer


class StationarityReport:
    """
    Analyzes all possible symbol-exchange pairs for cointegration and stationarity,
    then generates a ranked CSV report.
    """
    def __init__(self, full_data: pl.DataFrame, exchanges: List[str], log_dir: str, logger: Optional[logging.Logger], analyzer: MultiExchangeArbitrageAnalyzer, commission_pct: float):
        self.full_data = full_data
        self.exchanges = exchanges
        self.log_dir = log_dir
        self.logger = logger if logger else logging.getLogger(__name__)
        self.analyzer = analyzer
        self.commission_pct = commission_pct
        self.report_path = os.path.join(self.log_dir, "cointegration_report.csv")

    def generate_report(self):
        """
        Orchestrates the generation of the cointegration and stationarity report.
        """
        self.logger.info("Starting generation of the cointegration report for all pairs...")
        
        all_symbols = self.full_data.select('symbol').unique().to_series().to_list()
        exchange_pairs = list(itertools.combinations(self.exchanges, 2))
        
        all_results = []

        total_pairs = len(all_symbols) * len(exchange_pairs)
        self.logger.info(f"Found {len(all_symbols)} symbols and {len(exchange_pairs)} exchange pairs. Analyzing {total_pairs} total combinations.")

        count = 0
        for symbol in all_symbols:
            symbol_data = self.full_data.filter(pl.col('symbol') == symbol)
            for ex1, ex2 in exchange_pairs:
                count += 1
                if count % 100 == 0:
                    self.logger.info(f"  ...analyzed {count}/{total_pairs} pairs...")

                # Create aligned time series for both exchanges
                df1 = symbol_data.filter(pl.col('exchange') == ex1).select(["timestamp", "bestBid"]).rename({"bestBid": "price1"})
                df2 = symbol_data.filter(pl.col('exchange') == ex2).select(["timestamp", "bestBid"]).rename({"bestBid": "price2"})
                
                df1 = symbol_data.filter(pl.col('exchange') == ex1).select(["timestamp", "bestBid", "MinVolume"]).rename({"bestBid": "price1", "MinVolume": "volume1"})
                df2 = symbol_data.filter(pl.col('exchange') == ex2).select(["timestamp", "bestBid", "MinVolume"]).rename({"bestBid": "price2", "MinVolume": "volume2"})
                
                aligned_df = df1.join(df2, on="timestamp", how="inner").sort("timestamp")
                
                if aligned_df.is_empty() or aligned_df.height < 50:
                    continue
                
                series1 = aligned_df.get_column("price1")
                series2 = aligned_df.get_column("price2")

                # Perform Cointegration Test
                coint_p, hedge_ratio = calculate_cointegration_test(series1, series2)
                
                if coint_p is None:
                    continue

                is_cointegrated = coint_p < 0.05

                # Calculate stationarity metrics on the spread for scoring
                spread_series = series1 - (hedge_ratio * series2 if hedge_ratio is not None else series2)
                metrics = calculate_stationarity_metrics(spread_series)
                score = calculate_stationarity_score(metrics)
                
                # --- New Metrics Calculation ---
                spread_std_dev = spread_series.std()
                mean_price = (series1.mean() + series2.mean()) / 2
                relative_spread_std_dev = (spread_std_dev / mean_price) * 100 if mean_price else 0
                
                half_life = calculate_half_life(spread_series)
                zero_crossings = calculate_zero_crossings(spread_series)
                
                # Calculate tradeability score
                tradeability_score = (zero_crossings / half_life) if half_life and half_life > 0 else 0
                
                # Calculate average volume
                avg_volume = (aligned_df.get_column("volume1").mean() + aligned_df.get_column("volume2").mean()) / 2

                # --- Calculate Opportunity Frequency ---
                pair_data = symbol_data.filter(pl.col('exchange').is_in([ex1, ex2]))
                opportunities_df = self.analyzer.find_maker_taker_opportunities(
                    spread_data=pair_data,
                    commission_pct=self.commission_pct
                )
                
                if opportunities_df.is_empty():
                    num_opportunities = 0
                else:
                    profitable_opps = opportunities_df.filter(pl.col('profit_pct') >= 0.25)
                    num_opportunities = profitable_opps.height
                
                duration_hours = (aligned_df.select(pl.max("timestamp"))[0,0] - aligned_df.select(pl.min("timestamp"))[0,0]).total_seconds() / 3600
                opportunities_per_hour = num_opportunities / duration_hours if duration_hours > 0 else 0

                all_results.append({
                    "symbol": symbol,
                    "exchange_1": ex1,
                    "exchange_2": ex2,
                    "is_cointegrated": is_cointegrated,
                    "opportunities_per_hour": opportunities_per_hour,
                    "relative_spread_std_dev": relative_spread_std_dev,
                    "tradeability_score": tradeability_score,
                    "half_life": half_life,
                    "zero_crossings": zero_crossings,
                    "average_volume": avg_volume,
                    "stationarity_score": score,
                    "coint_p_value": coint_p,
                    "hedge_ratio": hedge_ratio,
                    "spread_std_dev": spread_std_dev, # Keep for reference
                    "data_points": len(aligned_df)
                })

        if not all_results:
            self.logger.warning("No pairs with sufficient data found to generate a cointegration report.")
            return

        # Create and save the report
        report_df = pl.DataFrame(all_results)
        # Sort by cointegration, then by spread volatility, then by score
        report_df = report_df.sort(
            by=["is_cointegrated", "opportunities_per_hour", "tradeability_score", "relative_spread_std_dev"],
            descending=[True, True, True, True]
        )
        
        report_df.write_csv(self.report_path)
        self.logger.info(f"Cointegration report saved to: {self.report_path}")
