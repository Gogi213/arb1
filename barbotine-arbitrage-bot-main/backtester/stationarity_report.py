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

from .stationarity_analyzer import calculate_stationarity_metrics, calculate_stationarity_score, calculate_cointegration_test

class StationarityReport:
    """
    Analyzes all possible symbol-exchange pairs for cointegration and stationarity,
    then generates a ranked CSV report.
    """
    def __init__(self, full_data: pl.DataFrame, exchanges: List[str], log_dir: str, logger: Optional[logging.Logger] = None):
        self.full_data = full_data
        self.exchanges = exchanges
        self.log_dir = log_dir
        self.logger = logger if logger else logging.getLogger(__name__)
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

                all_results.append({
                    "symbol": symbol,
                    "exchange_1": ex1,
                    "exchange_2": ex2,
                    "is_cointegrated": is_cointegrated,
                    "coint_p_value": coint_p,
                    "hedge_ratio": hedge_ratio,
                    "stationarity_score": score,
                    "adf_p_value": metrics.get("adf_p_value"),
                    "kpss_p_value": metrics.get("kpss_p_value"),
                    "hurst_exponent": metrics.get("hurst_exponent"),
                    "data_points": len(aligned_df)
                })

        if not all_results:
            self.logger.warning("No pairs with sufficient data found to generate a cointegration report.")
            return

        # Create and save the report
        report_df = pl.DataFrame(all_results)
        # Sort by cointegration first, then by score
        report_df = report_df.sort(["is_cointegrated", "stationarity_score"], descending=[True, True])
        
        report_df.write_csv(self.report_path)
        self.logger.info(f"Cointegration report saved to: {self.report_path}")
