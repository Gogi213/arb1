"""Main entry point for arbitrage finder."""
import asyncio
import yaml
import threading
import http.server
import socketserver
import json
import os
from pathlib import Path
from urllib.parse import urlparse
from core.data_receiver import DataReceiver
from core.market_state import MarketState
from core.arbitrage_finder import ArbitrageFinder
from core.output import OutputHandler
from core.types import MarketData


class DashboardHandler(http.server.SimpleHTTPRequestHandler):
    """HTTP handler for dashboard and API."""

    def do_GET(self):
        parsed_path = urlparse(self.path)

        if parsed_path.path == '/':
            self.path = '/dashboard.html'
            return http.server.SimpleHTTPRequestHandler.do_GET(self)

        elif parsed_path.path == '/api/opportunities':
            self.send_response(200)
            self.send_header('Content-type', 'application/json')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.end_headers()

            opportunities = self.read_opportunities()
            self.wfile.write(json.dumps(opportunities).encode())

        else:
            return http.server.SimpleHTTPRequestHandler.do_GET(self)

    def read_opportunities(self, max_records=100):
        """Read last N opportunities from situations.jsonl."""
        jsonl_path = Path('situations.jsonl')

        if not jsonl_path.exists():
            return []

        opportunities = []
        try:
            with open(jsonl_path, 'rb') as f:
                lines = []
                f.seek(0, os.SEEK_END)
                file_size = f.tell()

                buffer_size = 8192
                position = file_size

                while position > 0 and len(lines) < max_records:
                    read_size = min(buffer_size, position)
                    position -= read_size
                    f.seek(position)
                    chunk = f.read(read_size)
                    lines = chunk.split(b'\n') + lines

                for line in lines[-max_records:]:
                    if line.strip():
                        try:
                            opp = json.loads(line)
                            opportunities.append(opp)
                        except json.JSONDecodeError:
                            continue

        except Exception as e:
            print(f"[WEB] Error reading opportunities: {e}")

        return opportunities

    def log_message(self, format, *args):
        """Suppress log messages."""
        pass


def start_web_server(port=8080):
    """Start HTTP server in background thread."""
    try:
        with socketserver.TCPServer(("", port), DashboardHandler) as httpd:
            print(f"[WEB] Dashboard server started on http://localhost:{port}")
            httpd.serve_forever()
    except Exception as e:
        print(f"[WEB] Failed to start server: {e}")


class ArbitrageApp:
    """Main application orchestrator."""

    def __init__(self, config: dict):
        self.config = config

        # Initialize components
        self.market_state = MarketState(
            max_data_age_sec=config['arbitrage']['max_data_age_sec']
        )
        self.arbitrage_finder = ArbitrageFinder(
            min_profit_pct=config['arbitrage']['min_profit_pct']
        )
        self.output_handler = OutputHandler(
            output_file=config['output']['file'],
            console_output=config['output']['console']
        )
        self.receiver = DataReceiver(
            url=config['websocket']['url'],
            reconnect_delay=config['websocket']['reconnect_delay']
        )

        # Register callback
        self.receiver.on_data(self.on_market_data)

        # Stats
        self.data_count = 0
        self.opportunities_found = 0

    async def on_market_data(self, data: MarketData) -> None:
        """Callback for incoming market data - update state."""
        self.market_state.update(data)
        self.data_count += 1

    async def find_arbitrage_loop(self) -> None:
        """Periodically search for arbitrage opportunities."""
        while True:
            await asyncio.sleep(1)  # Check every second

            # Find opportunities
            opportunities = self.arbitrage_finder.find_opportunities(self.market_state)

            if opportunities:
                self.opportunities_found += len(opportunities)
                self.output_handler.write(opportunities)

    async def stats_loop(self) -> None:
        """Print statistics periodically."""
        while True:
            await asyncio.sleep(10)  # Every 10 seconds
            print(f"\n[STATS] Data received: {self.data_count} | "
                  f"Market state size: {self.market_state.size()} | "
                  f"Opportunities found: {self.opportunities_found}")

    async def start(self) -> None:
        """Start all tasks."""
        await asyncio.gather(
            self.receiver.start(),
            self.find_arbitrage_loop(),
            self.stats_loop()
        )


async def main():
    """Main application loop."""
    # Load config
    config_path = Path(__file__).parent / "config.yaml"
    with open(config_path) as f:
        config = yaml.safe_load(f)

    # Start web server in background thread
    web_port = 8080
    web_thread = threading.Thread(
        target=start_web_server,
        args=(web_port,),
        daemon=True
    )
    web_thread.start()

    # Print startup info
    print("=" * 80)
    print("ARBITRAGE FINDER")
    print("=" * 80)
    print(f"Config file:      {config_path}")
    print(f"WebSocket:        {config['websocket']['url']}")
    print(f"Min profit:       {config['arbitrage']['min_profit_pct']}%")
    print(f"Max data age:     {config['arbitrage']['max_data_age_sec']}s")
    print(f"Output file:      {config['output']['file']}")
    print(f"Console output:   {config['output']['console']}")
    print(f"Web dashboard:    http://localhost:{web_port}")
    print("=" * 80)
    print("\nStarting...\n")

    # Create and start app
    app = ArbitrageApp(config)
    await app.start()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\n\nStopped by user")
