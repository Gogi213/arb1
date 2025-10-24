#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Collects and stores statistics from the live test simulation.
Optimized to use Polars DataFrame throughout for better performance.
"""

from typing import Dict
import polars as pl

class StatisticsCollector:
    """
    Aggregates and processes all successful simulated trades during a test run.
    Uses Polars DataFrames for efficient bulk operations.
    """
    def __init__(self):
        self.trades_df: pl.DataFrame = pl.DataFrame()

    def record_trade(
        self,
        profit_threshold: float,
        symbol: str,
        buy_exchange: str,
        sell_exchange: str,
        net_profit_pct: float,
        amount_usdt: float
    ):
        """Records a single successful arbitrage event."""
        profit_usdt = amount_usdt * (net_profit_pct / 100)

        trade_df = pl.DataFrame({
            "profit_threshold": [profit_threshold],
            "symbol": [symbol],
            "buy_exchange": [buy_exchange],
            "sell_exchange": [sell_exchange],
            "net_profit_pct": [net_profit_pct],
            "amount_usdt": [amount_usdt],
            "profit_usdt": [profit_usdt]
        })

        if self.trades_df.is_empty():
            self.trades_df = trade_df
        else:
            self.trades_df = pl.concat([self.trades_df, trade_df])

    def record_trades_bulk(self, profit_threshold: float, symbol: str, trades_df: pl.DataFrame):
        """
        Records a batch of trades from a DataFrame in a vectorized manner.
        """
        if trades_df.is_empty():
            return

        # Use trade_size_usdt if available, otherwise calculate from trade_amount_crypto
        if "trade_size_usdt" in trades_df.columns:
            report_df = trades_df.select([
                pl.lit(profit_threshold).alias("profit_threshold"),
                pl.lit(symbol).alias("symbol"),
                pl.col("buy_exchange"),
                pl.col("sell_exchange"),
                pl.col("profit_pct").alias("net_profit_pct"),
                pl.col("trade_size_usdt").alias("amount_usdt"),
                (pl.col("profit_pct") / 100 * pl.col("trade_size_usdt")).alias("profit_usdt")
            ])
        else:
            report_df = trades_df.select([
                pl.lit(profit_threshold).alias("profit_threshold"),
                pl.lit(symbol).alias("symbol"),
                pl.col("buy_exchange"),
                pl.col("sell_exchange"),
                pl.col("profit_pct").alias("net_profit_pct"),
                (pl.col("trade_amount_crypto") * pl.col("buy_price")).alias("amount_usdt"),
                (pl.col("profit_pct") / 100 * (pl.col("trade_amount_crypto") * pl.col("buy_price"))).alias("profit_usdt")
            ])

        if self.trades_df.is_empty():
            self.trades_df = report_df
        else:
            self.trades_df = pl.concat([self.trades_df, report_df])

    def get_results(self) -> Dict:
        """
        Processes the collected trades and returns a structured dictionary,
        grouped by symbol and profit threshold. Uses vectorized Polars operations.
        """
        if self.trades_df.is_empty():
            return {}

        # Calculate totals using Polars aggregations
        total_trades = len(self.trades_df)
        total_profit = self.trades_df.select(pl.col("profit_usdt").sum()).item()

        results = {
            "total_trades": total_trades,
            "total_profit": total_profit,
            "by_symbol": {}
        }

        # Group by symbol
        for symbol in self.trades_df["symbol"].unique().to_list():
            symbol_df = self.trades_df.filter(pl.col("symbol") == symbol)

            symbol_stats = {
                "total_profit": symbol_df.select(pl.col("profit_usdt").sum()).item(),
                "total_trades": len(symbol_df),
                "by_threshold": {}
            }

            # Group by threshold within each symbol
            for threshold in symbol_df["profit_threshold"].unique().to_list():
                threshold_df = symbol_df.filter(pl.col("profit_threshold") == threshold)

                # Create exchange pairs and count them
                pairs_df = threshold_df.select([
                    pl.col("buy_exchange"),
                    pl.col("sell_exchange")
                ]).with_columns([
                    pl.min_horizontal(pl.col("buy_exchange"), pl.col("sell_exchange")).alias("ex1"),
                    pl.max_horizontal(pl.col("buy_exchange"), pl.col("sell_exchange")).alias("ex2")
                ]).group_by(["ex1", "ex2"]).agg(pl.len().alias("count"))

                pair_counts = {
                    (row["ex1"], row["ex2"]): row["count"]
                    for row in pairs_df.to_dicts()
                }

                symbol_stats["by_threshold"][threshold] = {
                    "profit": threshold_df.select(pl.col("profit_usdt").sum()).item(),
                    "trades": len(threshold_df),
                    "pair_counts": pair_counts
                }

            results["by_symbol"][symbol] = symbol_stats

        return results