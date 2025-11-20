# Project Glossary

## General Terms

*   **Arbitrage:** The practice of taking advantage of a price difference between two or more markets.
*   **Spread:** The difference in price between two assets or markets. In this project, it often refers to the percentage difference between a buy price on one exchange and a sell price on another.
*   **Exchange:** A platform for trading cryptocurrencies (e.g., Binance, Bybit, Gate.io).
*   **Symbol:** A trading pair (e.g., `BTC_USDT`).
*   **Parquet:** A columnar storage file format used for efficient data storage and retrieval in the Data Lake.

## `collections` Project

*   **SpreadAggregator:** The name of the C# project responsible for collecting and aggregating market data.
*   **OrchestrationService:** The central service in `collections` that manages exchange connections, data processing, and distribution.
*   **DataCollectorService:** A background service responsible for persisting market data to disk (Parquet files).
*   **RawDataChannel:** A `System.Threading.Channel` used to decouple the ingestion of raw market data from its persistence, preventing backpressure on the hot path.
*   **RollingWindowChannel:** A separate channel used for real-time analysis (calculating spreads) to ensure it doesn't compete with the persistence layer.
*   **HFT Optimizations:** High-Frequency Trading optimizations, such as using synchronous `TryWrite` for channels and zero-allocation paths to minimize latency.

## `trader` Project

*   **Convergent Trader:** The active trading strategy that executes a buy-wait-sell cycle on a single exchange to capture price convergence.
*   **Spread Listener:** The legacy mode (now deprecated) that listened for arbitrage opportunities broadcasted by `collections`.
*   **TrailingTrader:** A component used by `ConvergentTrader` to execute buy orders that "trail" the market price for a better entry.
*   **DecisionMaker:** A removed component that was responsible for making trading decisions in the legacy architecture.
*   **Stale Data Protection:** Mechanisms (like timestamp checks and 7-second thresholds) to prevent trading on outdated market information.

## `analyzer` Project

*   **Ultra-Fast Analysis:** The batch analysis process (`run_all_ultra.py`) that uses `polars` and multiprocessing to rapidly scan historical data.
*   **Discovery:** The process of scanning the data directory to identify available symbols and exchanges (`lib/discovery.py`).
*   **Complete Cycle:** A metric used in analysis to count how many times a price deviation opened and then closed (returned to neutral), indicating a potential tradeable opportunity.
