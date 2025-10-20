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
    """Represents a single simulated arbitrage trade for a specific threshold."""
    profit_threshold: float
    buy_exchange: str
    sell_exchange: str
    symbol: str
    net_profit_pct: float
    amount_usdt: float
    profit_usdt: float

class StatisticsCollector:
    """
    Aggregates and processes all successful simulated trades during a test run.
    """
    def __init__(self):
        self.trades: List[ArbitrageTrade] = []

    def record_trade(
        self,
        profit_threshold: float,
        buy_exchange: str,
        sell_exchange: str,
        symbol: str,
        net_profit_pct: float,
        amount_usdt: float
    ):
        """Records a single successful arbitrage event."""
        profit_usdt = amount_usdt * (net_profit_pct / 100)
        trade = ArbitrageTrade(
            profit_threshold=profit_threshold,
            buy_exchange=buy_exchange,
            sell_exchange=sell_exchange,
            symbol=symbol,
            net_profit_pct=net_profit_pct,
            amount_usdt=amount_usdt,
            profit_usdt=profit_usdt
        )
        self.trades.append(trade)

    def get_results(self) -> Dict:
        """
        Processes the collected trades and returns a structured dictionary.
        """
        if not self.trades:
            return {}

        # --- Overall Stats (aggregated across all thresholds) ---
        exchange_pair_counts = defaultdict(int)
        coin_counts = defaultdict(int)
        for trade in self.trades:
            pair = tuple(sorted((trade.buy_exchange, trade.sell_exchange)))
            exchange_pair_counts[pair] += 1
            coin_counts[trade.symbol] += 1

        top_3_combo = self._find_top_exchange_combo()
        round_trips = self._find_round_trip_opportunities()

        # --- Per-Threshold Stats ---
        earnings_per_threshold = defaultdict(float)
        trades_per_threshold = defaultdict(int)
        for trade in self.trades:
            earnings_per_threshold[trade.profit_threshold] += trade.profit_usdt
            trades_per_threshold[trade.profit_threshold] += 1

        return {
            "total_simulated_trades": len(self.trades),
            "exchange_pair_ranking": sorted(exchange_pair_counts.items(), key=lambda item: item[1], reverse=True),
            "coin_ranking": sorted(coin_counts.items(), key=lambda item: item[1], reverse=True),
            "estimated_earnings": dict(sorted(earnings_per_threshold.items())),
            "trades_per_threshold": dict(sorted(trades_per_threshold.items())),
            "top_3_combo": top_3_combo,
            "round_trip_opportunities": round_trips,
        }

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