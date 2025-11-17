# Overall System Data Flow
**Version:** 2.0 (Validated on 2025-11-17)

This document describes the end-to-end data flow between the three main system components: `collections`, `analyzer`, and `trader`.

## 1. Real-Time Flow (Arbitrage Detection)

This flow is designed for detecting and executing arbitrage trades with minimal latency.

1.  **Data Ingestion:** `collections` (`SpreadAggregator`) connects to the WebSocket streams of multiple exchanges.
2.  **Spread Calculation:** `collections` continuously calculates arbitrage spreads.
3.  **Spread Broadcasting:** `collections` immediately broadcasts each calculated spread via its own WebSocket server.
4.  **Signal Reception:** `trader` (`SpreadListener`) connects to the `collections` WebSocket server and receives the stream of spread data.
5.  **Decision:** `trader` (`DecisionMaker`) analyzes the incoming spread. If it exceeds a configured threshold, it logs the opportunity.
6.  **Process Halts:** The `trader`'s `DecisionMaker` **does not execute a trade**. The logic is a `//TODO` placeholder, and the process for this real-time signal stops here.

## 2. Historical/Batch Flow (Strategy & Analysis)

This flow is designed for offline analysis to find and validate trading strategies.

1.  **Data Collection and Persistence:** `collections` (`ParquetDataWriter`) asynchronously saves all raw market data into Parquet files.
    *   **IMPORTANT:** Due to a known architectural flaw (competing consumers), the `ParquetDataWriter` currently receives only a **fraction** of the total data stream, making the persisted data incomplete.
2.  **Reading and Analysis:** `analyzer` (`run_all_ultra.py`) is run as a separate batch process. It scans the directory of Parquet files.
3.  **Parallel Processing:** `analyzer` uses multiple processes and threads to analyze the historical data, calculating statistical metrics like mean-reversion frequency.
4.  **Report Generation:** `analyzer` creates a CSV report ranking the most promising trading pairs for arbitrage.
5.  **Strategic Decision:** Developers and strategists use this report to decide which trading pairs to enable in the `trader`'s configuration.

## Summary Diagram

*   **`collections`** -> (WebSocket) -> **`trader`** (Real-time Execution)
*   **`collections`** -> (Incomplete Parquet files due to bug) -> **`analyzer`** (Historical Analysis)
*   **`analyzer`** -> (CSV Report) -> **Human** -> (Configuration) -> **`trader`** (Strategic Management)