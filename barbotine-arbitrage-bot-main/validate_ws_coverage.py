import asyncio
import websockets
import json
import ccxt.async_support as ccxt
from rich.console import Console
from rich.table import Table
import time

# --- Configuration ---
WEBSOCKET_URI = "ws://127.0.0.1:8181"
COLLECTION_DURATION_SECONDS = 60  # How long to listen for WebSocket messages
VOLUME_THRESHOLD_USD = 2_000_000  # Volume filter MUST match the C# application

# List of exchanges supported by the C# application
SUPPORTED_EXCHANGES = [
    'binance',
    'bybit',
    'okx',
    'kucoin',
    'gateio',
    'mexc',
    'bingx',
    'bitget',
]

console = Console()

async def collect_ws_data():
    """Collects unique pairs from the WebSocket stream for a set duration."""
    console.print(f"[yellow]Connecting to WebSocket at {WEBSOCKET_URI}...")
    console.print(f"Listening for {COLLECTION_DURATION_SECONDS} seconds to collect WebSocket data...")
    
    ws_pairs = {exchange: set() for exchange in SUPPORTED_EXCHANGES}
    start_time = time.time()

    try:
        async with websockets.connect(WEBSOCKET_URI) as websocket:
            while time.time() - start_time < COLLECTION_DURATION_SECONDS:
                try:
                    message = await asyncio.wait_for(websocket.recv(), timeout=1.0)
                    data = json.loads(message)
                    
                    exchange_name = data.get("Exchange", "").lower()
                    symbol = data.get("Symbol")

                    if exchange_name in ws_pairs and symbol:
                        ws_pairs[exchange_name].add(symbol)

                except asyncio.TimeoutError:
                    continue  # No message received, continue listening
                except json.JSONDecodeError:
                    console.print(f"[red]Error decoding JSON from WebSocket message: {message}[/red]")
                except Exception as e:
                    console.print(f"[red]An error occurred in WebSocket client: {e}[/red]")
                    break
    except websockets.exceptions.ConnectionClosedError as e:
        console.print(f"[bold red]WebSocket connection failed: {e}[/bold red]")
        console.print("Please ensure the C# SpreadAggregator application is running.")
        return None
    except Exception as e:
        console.print(f"[bold red]An unexpected error occurred: {e}[/bold red]")
        return None

    console.print("[green]Finished collecting WebSocket data.[/green]")
    return {exchange: len(pairs) for exchange, pairs in ws_pairs.items()}

async def fetch_rest_data(exchange_id):
    """Fetches and filters pairs from a single exchange via REST API."""
    exchange = None
    try:
        exchange_class = getattr(ccxt, exchange_id)
        
        # Specific options for exchanges that fail by default
        options = {}
        if exchange_id == 'bybit':
            options = {'options': {'defaultType': 'spot'}}
        elif exchange_id == 'gateio':
            options = {'options': {'defaultType': 'spot'}}

        exchange = exchange_class(options)
        
        # Explicitly load markets for Gate.io to prevent issues
        if exchange_id == 'gateio':
            await exchange.load_markets()

        # For Bybit, we need to specify the market type for fetch_tickers
        if exchange_id == 'bybit':
            tickers = await exchange.fetch_tickers(params={'type': 'spot'})
        else:
            tickers = await exchange.fetch_tickers()

        filtered_count = 0
        for symbol, ticker in tickers.items():
            if symbol.endswith('/USDT') and ticker.get('quoteVolume') is not None:
                if ticker['quoteVolume'] >= VOLUME_THRESHOLD_USD:
                    filtered_count += 1
        
        return filtered_count
    except (ccxt.NetworkError, ccxt.ExchangeError) as e:
        console.print(f"[red]Error fetching data for {exchange_id}: {e}[/red]")
        return 0
    except Exception as e:
        console.print(f"[red]An unexpected error occurred for {exchange_id}: {e}[/red]")
        return 0
    finally:
        if exchange:
            await exchange.close()

async def collect_rest_data():
    """Collects filtered pair counts from all exchanges via REST API concurrently."""
    console.print(f"\n[yellow]Fetching REST data from exchanges with volume > ${VOLUME_THRESHOLD_USD:,}...[/yellow]")
    
    tasks = [fetch_rest_data(exchange_id) for exchange_id in SUPPORTED_EXCHANGES]
    results = await asyncio.gather(*tasks)
    
    rest_counts = dict(zip(SUPPORTED_EXCHANGES, results))
    
    console.print("[green]Finished fetching REST data.[/green]")
    return rest_counts

def display_results(ws_counts, rest_counts):
    """Displays the comparison table."""
    if ws_counts is None:
        return

    table = Table(title="WebSocket vs. REST API Coverage Comparison")
    table.add_column("Exchange", justify="left", style="cyan", no_wrap=True)
    table.add_column("WS Pairs", justify="right", style="magenta")
    table.add_column("REST Pairs (Filtered)", justify="right", style="green")
    table.add_column("Delta", justify="right", style="yellow")

    total_ws = 0
    total_rest = 0

    for exchange in SUPPORTED_EXCHANGES:
        ws_count = ws_counts.get(exchange, 0)
        rest_count = rest_counts.get(exchange, 0)
        delta = ws_count - rest_count
        
        delta_str = f"[bold red]{delta}[/bold red]" if delta < 0 else f"[bold green]+{delta}[/bold green]" if delta > 0 else str(delta)
        
        table.add_row(
            exchange.capitalize(),
            str(ws_count),
            str(rest_count),
            delta_str
        )
        total_ws += ws_count
        total_rest += rest_count

    # Add a footer with totals
    table.add_section()
    total_delta = total_ws - total_rest
    total_delta_str = f"[bold red]{total_delta}[/bold red]" if total_delta < 0 else f"[bold green]+{total_delta}[/bold green]" if total_delta > 0 else str(total_delta)
    table.add_row("Total", f"[bold]{total_ws}[/bold]", f"[bold]{total_rest}[/bold]", total_delta_str)

    console.print(table)

async def main():
    """Main function to run the validation."""
    ws_counts = await collect_ws_data()
    
    if ws_counts:
        rest_counts = await collect_rest_data()
        display_results(ws_counts, rest_counts)

if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        console.print("\n[bold]Validation script terminated by user.[/bold]")
