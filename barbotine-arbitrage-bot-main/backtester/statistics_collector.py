#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Collects and stores statistics from the live test simulation.
"""

from dataclasses import dataclass
from typing import List, Dict, Tuple
from collections import defaultdict
from itertools import combinations

@dataclass
class ArbitrageTrade:
    """Represents a single simulated arbitrage trade."""
    profit_threshold: float
    symbol: str
    buy_exchange: str
    sell_exchange: str
    net_profit_pct: float
    amount_usdt: float
    profit_usdt: float

class StatisticsCollector:
    """
    Aggregates and processes all successful simulated trades during a test run.
    """
    def __init__(self):
        self.trades: List[ArbitrageTrade] = []
        self.stationarity_metrics: Dict[str, Dict] = {}

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
        trade = ArbitrageTrade(
            profit_threshold=profit_threshold,
            symbol=symbol,
            buy_exchange=buy_exchange,
            sell_exchange=sell_exchange,
            net_profit_pct=net_profit_pct,
            amount_usdt=amount_usdt,
            profit_usdt=profit_usdt
        )
        self.trades.append(trade)

    def record_stationarity_metrics(self, symbol: str, metrics: Dict):
        """Records the calculated stationarity metrics for a symbol."""
        self.stationarity_metrics[symbol] = metrics

    def get_results(self) -> Dict:
        """
        Processes the collected trades and returns a structured dictionary,
        grouped by symbol and profit threshold.
        """
        if not self.trades:
            return {}

        results = {
            "total_trades": len(self.trades),
            "total_profit": sum(t.profit_usdt for t in self.trades),
            "by_symbol": defaultdict(lambda: {
                "total_profit": 0,
                "total_trades": 0,
                "by_threshold": defaultdict(lambda: {"profit": 0, "trades": 0})
            })
        }

        for trade in self.trades:
            symbol_stats = results["by_symbol"][trade.symbol]
            threshold_stats = symbol_stats["by_threshold"][trade.profit_threshold]

            symbol_stats["total_profit"] += trade.profit_usdt
            symbol_stats["total_trades"] += 1
            threshold_stats["profit"] += trade.profit_usdt
            threshold_stats["trades"] += 1
        
        # Convert defaultdicts to regular dicts for cleaner output
        final_results = {
            "total_trades": results["total_trades"],
            "total_profit": results["total_profit"],
            "by_symbol": {
                s: {
                    "total_profit": d["total_profit"],
                    "total_trades": d["total_trades"],
                    "by_threshold": {
                        t: {
                            "profit": v["profit"],
                            "trades": v["trades"],
                            "pair_counts": dict(v.get("pair_counts", {}))
                        }
                        for t, v in sorted(d["by_threshold"].items())
                    }
                }
                for s, d in sorted(results["by_symbol"].items())
            },
            "stationarity_metrics": self.stationarity_metrics
        }
        return final_results

    def _find_top_exchange_combo(self) -> Tuple[Tuple[str, str, str], int]:
        """Finds the combination of 3 exchanges with the most trades between them."""
        exchanges = set()
        for trade in self.trades:
            exchanges.add(trade.buy_exchange)
            exchanges.add(trade.sell_exchange)

        if len(exchanges) < 3:
            return None, 0

        max_trades = 0
        best_combo = None

        for combo in combinations(exchanges, 3):
            combo_trades = 0
            for trade in self.trades:
                if trade.buy_exchange in combo and trade.sell_exchange in combo:
                    combo_trades += 1
            
            if combo_trades > max_trades:
                max_trades = combo_trades
                best_combo = combo
        
        return best_combo, max_trades

    def _find_round_trip_opportunities(self) -> Dict[str, Dict[str, float]]:
        """Finds pairs of exchanges where a coin could be traded back and forth."""
        round_trips = defaultdict(lambda: defaultdict(float))
        
        situations_map = defaultdict(list)
        for trade in self.trades:
            pair_key = tuple(sorted((trade.buy_exchange, trade.sell_exchange)))
            situations_map[(trade.symbol, pair_key)].append(trade)

        for (symbol, pair_key), trades in situations_map.items():
            ex1, ex2 = pair_key
            ex1_to_ex2_profit = sum(t.profit_usdt for t in trades if t.buy_exchange == ex1 and t.sell_exchange == ex2)
            ex2_to_ex1_profit = sum(t.profit_usdt for t in trades if t.buy_exchange == ex2 and t.sell_exchange == ex1)

            if ex1_to_ex2_profit > 0 and ex2_to_ex1_profit > 0:
                total_profit = ex1_to_ex2_profit + ex2_to_ex1_profit
                trip_key = f"{symbol} ({ex1} <-> {ex2})"
                round_trips[trip_key]["total_profit"] = total_profit
        
        return dict(sorted(round_trips.items(), key=lambda item: item[1]["total_profit"], reverse=True))