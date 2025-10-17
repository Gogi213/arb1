# Arbitrage Finder

Cross-exchange arbitrage opportunity finder based on bid price decorrelation.

## Architecture

```
C# Project (SpreadAggregator) → WebSocket → Python Project (arb-finder)
                                ws://127.0.0.1:8181
```

## Strategy

**Decorrelation Arbitrage:**
- Buy on Exchange 1 at bid1 (limit order without fee)
- Sell on Exchange 2 at bid2 (market order) simultaneously
- Profit = (bid2 - bid1) / bid1 * 100%

**Example:**
- Exchange 1: BTC bid = $100
- Exchange 2: BTC bid = $102
- Profit = 2%

## Installation

### Option 1: Using Poetry (recommended)
```bash
cd arb-finder
poetry install
poetry run python main.py
```

### Option 2: Using pip
```bash
cd arb-finder
pip install websockets orjson pyyaml
python main.py
```

## Configuration

Edit `config.yaml`:

```yaml
websocket:
  url: "ws://127.0.0.1:8181"
  reconnect_delay: 5

arbitrage:
  min_profit_pct: 0.3  # Minimum profit threshold
  max_data_age_sec: 3  # Maximum age of market data

output:
  file: "situations.jsonl"
  console: true
```

## Usage

### Step 1: Start C# project
```bash
dotnet run --project ../src/SpreadAggregator.Presentation
```

### Step 2: Start Python arbitrage finder (with built-in web dashboard)
```bash
cd arb-finder
python main.py
```

### Step 3: Open Web Dashboard
Open in browser: **http://localhost:8080**

The dashboard shows:
- Real-time arbitrage opportunities
- Sortable table with time, symbol, exchanges, prices, profit
- Auto-refresh every 2 seconds
- Color-coded by profit (green > 0.5%, yellow > 0.3%)

**Note:** Web server starts automatically with main.py - no need for separate process!

## Output

**Console:**
```
================================================================================
[12:34:56] ARBITRAGE OPPORTUNITY: BTCUSDT
================================================================================
  BUY  on Binance   @ 95000.12345678 (spread: 0.05%)
  SELL on Bybit     @ 95500.87654321 (spread: 0.06%)
  PROFIT: 0.53%
================================================================================
```

**File (situations.jsonl):**
```json
{"timestamp_iso": "2025-10-17T12:34:56.789Z", "symbol": "BTCUSDT", "buy_exchange": "Binance", ...}
{"timestamp_iso": "2025-10-17T12:35:01.234Z", "symbol": "ETHUSDT", "buy_exchange": "OKX", ...}
```

**Web Dashboard:**
- Beautiful table interface at http://localhost:8080
- Real-time updates every 2 seconds
- Shows last 100 opportunities
- Filterable and sortable

## Project Structure

```
arb-finder/
├── config.yaml              # Configuration
├── main.py                  # Entry point (arbitrage finder)
├── web_server.py            # Web dashboard server
├── dashboard.html           # Web UI for viewing opportunities
└── core/
    ├── types.py             # Type definitions
    ├── data_receiver.py     # WebSocket client
    ├── market_state.py      # Market data storage
    ├── arbitrage_finder.py  # Arbitrage detection
    └── output.py            # Output handler
```

## Performance

- **Zero-copy**: In-place dict updates for market data
- **Fast JSON**: orjson library (5-10x faster than stdlib)
- **O(1) lookups**: Dict-based storage with (exchange, symbol) keys
- **Minimal allocations**: Reuses data structures

## Requirements

- Python 3.11+
- C# project running on ws://127.0.0.1:8181
- Libraries: websockets, orjson, pyyaml
