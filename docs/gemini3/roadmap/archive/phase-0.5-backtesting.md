# Phase 0.5: Backtesting Infrastructure

**Status:** ⚪ Not Started  
**Goal:** Validate signal logic on historical data before risking capital.  
**Duration:** ~1 week

---

## Overview

Before porting Python signal logic to C# (Phase 1), we need proof it works correctly. This phase creates infrastructure to:
1. Replay historical market data from Parquet files
2. Compare C# signals with Python analyzer output  
3. Track hypothetical P&L in paper trading mode

**Success Criteria:** Signal accuracy >95% vs Python baseline on 30 days of historical data.

---

## Task 0.5.1: Parquet Replay Service

**Purpose:** Inject historical data into RollingWindowService for testing.

**Target File:** `collections/src/SpreadAggregator.Testing/ParquetReplayService.cs` (New)

**Implementation:**
```csharp
public class ParquetReplayService
{
    private readonly string _dataPath;
    private readonly Channel<MarketData> _outputChannel;

    public ParquetReplayService(string dataPath, Channel<MarketData> outputChannel)
    {
        _dataPath = dataPath;
        _outputChannel = outputChannel;
    }

    public async Task ReplayAsync(DateTime startDate, DateTime endDate, int speedMultiplier = 1)
    {
        var files = Directory.GetFiles(_dataPath, "*.parquet", SearchOption.AllDirectories)
            .Where(f => IsInDateRange(f, startDate, endDate))
            .OrderBy(f => f);

        foreach (var file in files)
        {
            using var reader = await ParquetReader.CreateAsync(file);
            var table = await reader.ReadAsTableAsync();

            foreach (var row in table.Rows)
            {
                var data = MapRowToMarketData(row);
                await _outputChannel.Writer.WriteAsync(data);

                // Throttle to simulate real-time (optional)
                if (speedMultiplier < 100)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100 / speedMultiplier));
                }
            }
        }
        
        _outputChannel.Writer.Complete();
    }

    private bool IsInDateRange(string filePath, DateTime start, DateTime end)
    {
        // Extract date from path: .../date=2025-01-15/...
        var match = Regex.Match(filePath, @"date=(\d{4}-\d{2}-\d{2})");
        if (!match.Success) return false;

        var fileDate = DateTime.Parse(match.Groups[1].Value);
        return fileDate >= start && fileDate <= end;
    }

    private MarketData MapRowToMarketData(DataRow row)
    {
        // Map Parquet row to MarketData DTO
        return new SpreadData
        {
            Timestamp = (DateTime)row["Timestamp"],
            BestBid = (decimal)row["BestBid"],
            BestAsk = (decimal)row["BestAsk"],
            Exchange = (string)row["Exchange"],
            Symbol = (string)row["Symbol"],
            // ... other fields
        };
    }
}
```

**Usage:**
```csharp
// In test setup
var channel = Channel.CreateUnbounded<MarketData>();
var replayService = new ParquetReplayService("/data/market_data", channel);

// Inject into RollingWindowService
var rollingWindow = new RollingWindowService(channel.Reader, ...);

// Start replay at 10x speed
await replayService.ReplayAsync(
    startDate: DateTime.Parse("2025-01-01"),
    endDate: DateTime.Parse("2025-01-30"),
    speedMultiplier: 10
);
```

**Test Target:** `RollingWindowService` and `SignalDetectionService` (Phase 1).

---

## Task 0.5.2: Signal Validation Tool

**Purpose:** Compare C# signals with Python analyzer output to verify correctness.

**Target File:** `analyzer/validate_signals.py` (New utility script)

**Implementation:**
```python
import polars as pl
import requests
from datetime import datetime

def validate_signals(test_date: str):
    """
    Compare C# API signals with Python analyzer CSV output.
    Returns precision and recall metrics.
    """
    
    # 1. Load Python analyzer results (ground truth)
    python_signals = pl.read_csv(f"output/analysis_{test_date}.csv")
    
    # 2. Fetch C# signals from API
    response = requests.get(
        f"http://localhost:5000/api/signals/history",
        params={"date": test_date}
    )
    csharp_signals = pl.DataFrame(response.json())
    
    # 3. Join on symbol + timestamp (allow 1s tolerance)
    matches = python_signals.join(
        csharp_signals, 
        on=["symbol"], 
        how="inner"
    ).filter(
        (pl.col("timestamp") - pl.col("timestamp_right")).abs() < pl.duration(seconds=1)
    )
    
    # 4. Calculate metrics
    precision = len(matches) / len(csharp_signals) if len(csharp_signals) > 0 else 0
    recall = len(matches) / len(python_signals) if len(python_signals) > 0 else 0
    f1_score = 2 * (precision * recall) / (precision + recall) if (precision + recall) > 0 else 0
    
    print(f"[{test_date}] Precision: {precision:.2%}, Recall: {recall:.2%}, F1: {f1_score:.2%}")
    
    # 5. Assert minimum quality
    assert precision > 0.95, f"Precision too low: {precision:.2%}"
    assert recall > 0.95, f"Recall too low: {recall:.2%}"
    
    return {"precision": precision, "recall": recall, "f1": f1_score}

# Run validation for 30 days
if __name__ == "__main__":
    results = []
    start_date = datetime(2025, 1, 1)
    
    for day_offset in range(30):
        test_date = (start_date + timedelta(days=day_offset)).strftime("%Y-%m-%d")
        try:
            metrics = validate_signals(test_date)
            results.append({"date": test_date, **metrics})
        except AssertionError as e:
            print(f"❌ FAILED: {e}")
            break
    
    # Summary report
    df = pl.DataFrame(results)
    print("\n=== 30-Day Validation Report ===")
    print(df.describe())
    print(f"Average Precision: {df['precision'].mean():.2%}")
    print(f"Average Recall: {df['recall'].mean():.2%}")
```

**Expected Output:**
```
[2025-01-01] Precision: 96.5%, Recall: 97.2%, F1: 96.8%
[2025-01-02] Precision: 95.8%, Recall: 96.1%, F1: 95.9%
...
=== 30-Day Validation Report ===
Average Precision: 96.2%
Average Recall: 96.5%
✅ PASS - Signal detection matches Python analyzer
```

---

## Task 0.5.3: Paper Trading Mode

**Purpose:** Run ConvergentTrader without real exchange calls to track hypothetical P&L.

**Target File:** `trader/src/Core/PaperTradingExchange.cs` (New)

**Implementation:**
```csharp
public class PaperTradingExchange : IExchange
{
    private decimal _fakeBalance = 1000m; // Start with $1000 USDT
    private readonly Dictionary<string, decimal> _fakePrices = new();
    
    public Task<OrderResult> PlaceMarketOrderAsync(string symbol, OrderSide side, decimal quantity)
    {
        // Simulate market order at current fake price
        var price = _fakePrices.GetValueOrDefault(symbol, 1.0m);
        var cost = price * quantity;
        
        if (side == OrderSide.Buy)
        {
            _fakeBalance -= cost;
            Console.WriteLine($"[PAPER] BUY {quantity} {symbol} @ ${price} (balance: ${_fakeBalance})");
        }
        else
        {
            _fakeBalance += cost;
            Console.WriteLine($"[PAPER] SELL {quantity} {symbol} @ ${price} (balance: ${_fakeBalance})");
        }
        
        return Task.FromResult(new OrderResult 
        { 
            OrderId = Guid.NewGuid().ToString(),
            FilledQuantity = quantity,
            AveragePrice = price,
            Status = OrderStatus.Filled
        });
    }
    
    // Update fake price from replay data
    public void UpdatePrice(string symbol, decimal price)
    {
        _fakePrices[symbol] = price;
    }
}
```

**Usage:**
```csharp
// In test/backtest setup
var paperExchange = new PaperTradingExchange();
var trader = new ConvergentTrader(paperExchange, ...);

// Subscribe to replay data to update prices
replayService.OnMarketData += (data) => {
    paperExchange.UpdatePrice(data.Symbol, data.BestAsk);
};

// Run trading cycle
await trader.ExecuteCycleAsync("VIRTUAL_USDT");

// Check hypothetical P&L
var finalBalance = paperExchange.GetBalance();
Console.WriteLine($"30-day P&L: ${finalBalance - 1000}");
```

---

## Deliverables

**1. Backtesting Report:**
```markdown
# Backtesting Report: 2025-01-01 to 2025-01-30

## Signal Accuracy
- Precision: 96.2%
- Recall: 96.5%
- F1 Score: 96.3%
- Total Signals Detected: 1,247
- False Positives: 48
- False Negatives: 44

## Paper Trading P&L
- Starting Balance: $1,000.00
- Ending Balance: $1,142.35
- Total Return: +14.2%
- Win Rate: 68%
- Max Drawdown: -5.2%
- Sharpe Ratio: 1.8

## Performance
- Avg Signal Detection Latency: 12ms
- Replay Throughput: 50,000 ticks/sec (10x speed)

✅ PASS - Ready for Phase 1 (production signal detection)
```

**2. Test Suite:**
- Integration test: `ParquetReplayServiceTests.cs`
- Validation script: `validate_signals.py`
- P&L tracker: `PaperTradingTests.cs`

---

## Acceptance Criteria

- [ ] Parquet replay works at 1x, 10x, 100x speeds
- [ ] Signal validation shows >95% precision and recall
- [ ] Paper trading tracks P&L correctly
- [ ] Backtesting report generated automatically
- [ ] All integration tests passing

---

[← Prev: Foundation](phase-0-foundation.md) | [Back to Roadmap](README.md) | [Next: Brain →](phase-1-brain.md)
