# Detailed Process Flow of the Collections Project (SpreadAggregator)

This document describes the step-by-step data flow and service logic within the `SpreadAggregator` application, based on an analysis of the `OrchestrationService`.

## 1. Startup and Initialization (`StartAsync`)

1.  **Start WebSocket Server:** Upon startup, the `OrchestrationService` immediately starts the `_webSocketServer` to be ready for client connections.
2.  **Read Configuration:** The service reads the list of active exchanges from the configuration (`ExchangeSettings:Exchanges`).
3.  **Start Exchange Handlers:** For each active exchange, a `ProcessExchange` method is launched in a separate asynchronous task. These are long-running, background tasks.

## 2. Processing a Single Exchange (`ProcessExchange`)

This method encapsulates all logic specific to a single exchange.

1.  **Load Configuration:** Volume filter parameters (`MinUsdVolume`, `MaxUsdVolume`) for the given exchange are loaded from `IConfiguration`.
2.  **Get Symbol and Ticker Information:** Asynchronous calls are made to get the full list of trading instruments and 24-hour trading volume data.
3.  **Filter Symbols:** A multi-step filtering process is applied to select only liquid and relevant instruments (pairs against `USDT`/`USDC` within the specified volume range).
4.  **Subscribe to Data Streams:** The service subscribes to the `Tickers` (order book) and/or `Trades` (trade feed) streams for the filtered list of symbols. An asynchronous callback method is defined for each subscription.

## 3. The Hot Path: Processing a Data Tick (Subscription Callback)

This is the most performance-critical section of the code, executed on every price update.

1.  **Data Reception:** A `MarketData` object (e.g., `SpreadData` or `TradeData`) arrives from an exchange client into the callback method within the `OrchestrationService`.
2.  **Normalization and Enrichment:**
    *   The symbol name is normalized to a standard format like `BTC_USDT`.
    *   If it's spread data, the `SpreadPercentage` is calculated using the `_spreadCalculator`.
    *   The data is timestamped.
3.  **Data Distribution (Current Flawed Implementation):** The enriched `MarketData` object is sent to two places:
    *   **Direct Broadcast via WebSocket:** The data is wrapped in a `WebSocketMessage`, serialized to JSON, and sent **directly** to the `_webSocketServer` for immediate broadcast to all clients. This ensures minimal latency for real-time data.
    *   **Write to Shared Channel (Twice):** The data is written **twice** to the same shared `Channel<MarketData>` instance. This channel is consumed by both `DataCollectorService` and `RollingWindowService` in a **competing consumer** pattern, meaning each service only receives a fraction of the duplicated data.

This detailed flow demonstrates how the system separates tasks, but also highlights a critical implementation flaw in its real-time data processing pipeline.
