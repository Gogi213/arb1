#!/usr/bin/env python3
"""
Quick test to verify symbol normalization end-to-end.
"""

import sys
from pathlib import Path

# Add lib to path
sys.path.insert(0, str(Path(__file__).parent))

from lib.discovery import discover_data
from lib.data_loader import load_exchange_symbol_data

# Test discovery
print("=" * 60)
print("TEST 1: Discovery (reads from disk)")
print("=" * 60)
data_path = "C:/visual projects/arb1/data/market_data"
symbols = discover_data(data_path)

print("\nFirst 5 discovered symbols:")
for i, (symbol, exchanges) in enumerate(list(symbols.items())[:5]):
    print(f"  {symbol} -> {list(exchanges)}")
    if i >= 4:
        break

# Test loading
print("\n" + "=" * 60)
print("TEST 2: Data Loading (converts back to disk format)")
print("=" * 60)

if symbols:
    test_symbol = list(symbols.keys())[0]
    test_exchange = list(list(symbols.values())[0])[0]
    
    print(f"\nAttempting to load: {test_symbol} from {test_exchange}")
    print(f"Expected disk format: symbol={test_symbol.replace('/', '_')}")
    
    df = load_exchange_symbol_data(data_path, test_exchange, test_symbol)
    
    if df is not None:
        print(f"✅ SUCCESS! Loaded {len(df)} rows")
        print(f"   Columns: {df.columns}")
        print(f"   First timestamp: {df['timestamp'][0]}")
    else:
        print("❌ FAILED: Could not load data")
else:
    print("No symbols found!")

print("\n" + "=" * 60)
print("Normalization test complete")
print("=" * 60)
