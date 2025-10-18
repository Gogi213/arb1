#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Barbotine Multi-Pair Arbitrage TUI
Real-time trading dashboard with auto-trading capabilities
"""

import asyncio
from textual.app import App, ComposeResult
from textual.widgets import Header, Footer, Static, DataTable, Button, Input, Label
from textual.containers import Horizontal, Vertical, Container
from textual.reactive import reactive

from ws_client import SpreadAggregatorClient
from arbitrage_analyzer import ArbitrageAnalyzer
from filter_engine import FilterEngine, FilterConfig
from balance_manager import BalanceManager
from multi_pair_trader import MultiPairTrader


class FilterPanel(Static):
    """Panel for filter configuration"""

    def compose(self) -> ComposeResult:
        yield Label("Filter Settings", classes="panel-title")
        yield Horizontal(
            Label("Min Exchanges:", classes="input-label"),
            Input(value="3", placeholder="3", id="min_exchanges"),
            classes="filter-row"
        )
        yield Horizontal(
            Label("Min Profit %:", classes="input-label"),
            Input(value="0.5", placeholder="0.5", id="min_profit"),
            classes="filter-row"
        )
        yield Button("Apply Filters", id="apply_filters", variant="primary")
        yield Label("")  # Spacer
        yield Button("Start Trading", id="toggle_trading", variant="success")


class StatsPanel(Static):
    """Panel for displaying statistics"""

    total_pairs = reactive(0)
    total_trades = reactive(0)
    total_profit = reactive(0.0)
    data_updates = reactive(0)

    def compose(self) -> ComposeResult:
        yield Label("Statistics", classes="panel-title")
        yield Label(id="stat_pairs")
        yield Label(id="stat_trades")
        yield Label(id="stat_profit")
        yield Label(id="stat_updates")

    def watch_total_pairs(self, value: int) -> None:
        self.query_one("#stat_pairs", Label).update(f"Active Pairs: {value}")

    def watch_total_trades(self, value: int) -> None:
        self.query_one("#stat_trades", Label).update(f"Total Trades: {value}")

    def watch_total_profit(self, value: float) -> None:
        self.query_one("#stat_profit", Label).update(f"Total Profit: ${value:.2f}")

    def watch_data_updates(self, value: int) -> None:
        self.query_one("#stat_updates", Label).update(f"Updates: {value}")


class BarbotineTUI(App):
    """Barbotine Multi-Pair Arbitrage TUI Application"""

    CSS = """
    Screen {
        background: $surface;
    }

    Header {
        background: $primary;
    }

    .panel-title {
        background: $accent;
        padding: 0 1;
        text-style: bold;
    }

    .filter-row {
        height: 3;
        padding: 0 1;
    }

    .input-label {
        width: 15;
        content-align: right middle;
        padding-right: 1;
    }

    Input {
        width: 12;
    }

    Button {
        margin: 1;
        width: 100%;
    }

    DataTable {
        height: 100%;
    }

    #left_panel {
        width: 30;
        background: $panel;
        padding: 1;
    }

    #right_panel {
        width: 1fr;
        background: $background;
        padding: 1;
    }

    #stats_panel {
        height: auto;
        padding: 1;
        margin-bottom: 1;
    }
    """

    TITLE = "Barbotine Multi-Pair Arbitrage Bot"
    SUB_TITLE = "Real-time Trading Dashboard"

    BINDINGS = [
        ("q", "quit", "Quit"),
        ("r", "refresh", "Refresh"),
    ]

    def __init__(self, initial_balance: float = 10000):
        super().__init__()

        # Initialize components
        self.balance_manager = BalanceManager(total_usdt=initial_balance)
        self.trader = MultiPairTrader(self.balance_manager, mode='fake-money', min_trade_interval=1.0)
        self.analyzer = ArbitrageAnalyzer()
        self.filter_config = FilterConfig(min_exchanges=3, min_profit_pct=0.5)
        self.filter_engine = FilterEngine(self.filter_config)
        self.ws_client = SpreadAggregatorClient()

        # State
        self.data_update_count = 0
        self.listening = False
        self.auto_trading_enabled = False
        self.max_trading_pairs = 3  # Max number of pairs to trade simultaneously

    def compose(self) -> ComposeResult:
        """Create child widgets"""
        yield Header()
        yield Container(
            Horizontal(
                Vertical(
                    FilterPanel(id="filter_panel"),
                    id="left_panel"
                ),
                Vertical(
                    StatsPanel(id="stats_panel"),
                    DataTable(id="pairs_table"),
                    id="right_panel"
                ),
            ),
        )
        yield Footer()

    async def on_mount(self) -> None:
        """Called when app starts"""
        self.notify("Connecting to SpreadAggregator...")

        try:
            # Connect to WebSocket
            await self.ws_client.connect("ws://127.0.0.1:8181")
            self.notify("Connected! Starting data stream...", severity="information")
            self.listening = True

            # Start listening in background
            asyncio.create_task(self.listen_to_data())

            # Auto-start trading after 2 seconds
            await asyncio.sleep(2)
            self.auto_trading_enabled = True
            button = self.query_one("#toggle_trading", Button)
            button.label = "Stop Trading"
            button.variant = "error"
            self.notify("Auto-trading ENABLED - will start trading top pairs", severity="information")

        except Exception as e:
            self.notify(f"Error connecting: {e}", severity="error")

    async def listen_to_data(self):
        """Listen to WebSocket data stream"""
        def on_data(parsed_data):
            """Callback for WebSocket data"""
            if not parsed_data or not self.listening:
                return

            try:
                # Update counter
                self.data_update_count += 1

                # Analyze opportunities
                opportunities = self.analyzer.analyze(parsed_data)

                # Apply filters
                filtered = self.filter_engine.filter(opportunities)

                # Update table
                self.update_pairs_table(filtered[:20])  # Top 20

                # Update stats
                stats = self.trader.get_statistics()
                stats_panel = self.query_one("#stats_panel", StatsPanel)
                stats_panel.total_pairs = stats['total_pairs']
                stats_panel.total_trades = stats['total_trades']
                stats_panel.total_profit = stats['total_profit']
                stats_panel.data_updates = self.data_update_count

                # Auto-trading logic
                if self.auto_trading_enabled:
                    # Start trading for top pairs if we have capacity
                    active_count = len(self.trader.active_sessions)
                    if active_count < self.max_trading_pairs:
                        # Find pairs not yet trading
                        for opp in filtered[:self.max_trading_pairs]:
                            if opp.symbol not in self.trader.active_sessions:
                                if self.trader.start_trading_pair(opp):
                                    self.notify(f"Started trading {opp.symbol} ({opp.profit_pct:.2f}% profit)", severity="information")
                                    break  # Add one pair at a time

                    # Process opportunities for active pairs
                    for opp in filtered[:10]:  # Top 10 most profitable
                        if opp.symbol in self.trader.active_sessions:
                            self.trader.process_opportunity(opp)

            except Exception as e:
                self.notify(f"Error processing data: {e}", severity="warning")

        try:
            await self.ws_client.listen(on_data)
        except Exception as e:
            self.notify(f"WebSocket error: {e}", severity="error")
            self.listening = False

    def update_pairs_table(self, opportunities):
        """Update the pairs table with opportunities"""
        table = self.query_one("#pairs_table", DataTable)
        table.clear()

        # Add columns if not exists
        if not table.columns:
            table.add_columns("Symbol", "Profit%", "Buy From", "Sell To", "Exchanges", "Trades")

        for opp in opportunities:
            # Check if trading this pair
            is_trading = opp.symbol in self.trader.active_sessions
            trade_count = 0
            if is_trading:
                session = self.trader.active_sessions[opp.symbol]
                trade_count = session.trade_count

            table.add_row(
                opp.symbol,
                f"{opp.profit_pct:+.3f}%",
                opp.min_ask_exchange,
                opp.max_bid_exchange,
                str(opp.exchange_count),
                str(trade_count) if is_trading else "-"
            )

    def on_button_pressed(self, event: Button.Pressed) -> None:
        """Handle button presses"""
        if event.button.id == "apply_filters":
            self.apply_filters()
        elif event.button.id == "toggle_trading":
            self.toggle_trading()

    def apply_filters(self) -> None:
        """Apply updated filter settings"""
        try:
            min_exchanges = int(self.query_one("#min_exchanges", Input).value)
            min_profit = float(self.query_one("#min_profit", Input).value)

            self.filter_engine.update_config(
                min_exchanges=min_exchanges,
                min_profit_pct=min_profit
            )

            self.notify(f"Filters updated: >= {min_exchanges} exchanges, >= {min_profit}% profit", severity="information")

        except ValueError as e:
            self.notify(f"Invalid filter values: {e}", severity="error")

    def toggle_trading(self) -> None:
        """Toggle auto-trading on/off"""
        self.auto_trading_enabled = not self.auto_trading_enabled

        button = self.query_one("#toggle_trading", Button)
        if self.auto_trading_enabled:
            button.label = "Stop Trading"
            button.variant = "error"
            self.notify("Auto-trading ENABLED - will start trading top pairs", severity="information")
        else:
            button.label = "Start Trading"
            button.variant = "success"
            self.notify("Auto-trading DISABLED", severity="warning")

    def action_quit(self) -> None:
        """Quit the application"""
        self.listening = False
        self.exit()

    def action_refresh(self) -> None:
        """Refresh the display"""
        self.notify("Refreshing...")


def main():
    """Run the TUI application"""
    app = BarbotineTUI(initial_balance=10000)
    app.run()


if __name__ == "__main__":
    main()
