# Collections Project: Metrics and Algorithms

This document details the key metrics and filters used within the `SpreadAggregator` project.

## 1. Core Business Metric: Spread Percentage

The primary real-time metric is the percentage spread between the best bid and best ask prices.

### 1.1. Calculation Algorithm (`SpreadCalculator.cs`)

*   **Class:** `SpreadAggregator.Domain.Services.SpreadCalculator`
*   **Method:** `public decimal Calculate(decimal bestBid, decimal bestAsk)`
*   **Inputs:**
    *   `bestBid`: The highest price a buyer is willing to pay.
    *   `bestAsk`: The lowest price a seller is willing to accept.
*   **Formula:**
    ```csharp
    (bestAsk - bestBid) / bestAsk * 100
    ```
*   **Description:** The formula calculates the difference between the ask and bid, then normalizes it by the ask price, expressing the result as a percentage. This shows the bid-ask spread as a percentage of the asset's price.
*   **Error Handling:** The method throws a `DivideByZeroException` if `bestAsk` is zero to prevent invalid calculations.

## 2. Pre-computation Filter: Volume Filter

Before subscribing to an instrument's price updates, the system filters it based on daily trading volume to discard illiquid or uninteresting markets.

### 2.1. Filtering Algorithm (`VolumeFilter.cs`)

*   **Class:** `SpreadAggregator.Domain.Services.VolumeFilter`
*   **Method:** `public bool IsVolumeSufficient(decimal volume, decimal minVolume, decimal maxVolume)`
*   **Inputs:**
    *   `volume`: The actual 24-hour trading volume in USD.
    *   `minVolume`: The minimum allowed volume (from configuration).
    *   `maxVolume`: The maximum allowed volume (from configuration).
*   **Logic:**
    ```csharp
    volume >= minVolume && volume <= maxVolume
    ```
*   **Description:** The filter only allows instruments whose daily volume falls within the configured `[minVolume, maxVolume]` range. This focuses resources on markets with sufficient liquidity.

## 3. System Performance and Health Metrics (Observed)

While not explicitly calculated as domain metrics, these are crucial indicators of the application's health and performance. They are essential for diagnosing issues like the one described in backlog item `TD-007`.

*   **Data Ingestion Rate:**
    *   **Description:** The number of messages processed from the `System.Threading.Channels` per unit of time.
    *   **Importance:** Measures market activity intensity and the load on the system. A sudden drop can indicate a disconnection from an exchange.

*   **Channel Queue Depth:**
    *   **Description:** The number of items currently buffered in the `_rawDataChannel`.
    *   **Importance:** This is a critical backpressure indicator. A consistently growing queue signifies that the consumers (`DataCollectorService`, `RollingWindowService`) cannot keep up with the producers (exchange clients), pointing to a bottleneck.

*   **End-to-End (E2E) Latency:**
    *   **Description:** The time difference between a message's server timestamp (`ServerTimestamp`) and the moment the calculated spread is broadcast over the WebSocket.
    *   **Importance:** This is the key performance metric for the entire pipeline. Low and stable E2E latency is the primary goal for a real-time data aggregation system.

*   **Message Duplication Factor (Flaw-related):**
    *   **Description:** The ratio of messages written to the channel versus messages received from exchanges.
    *   **Importance:** In the current flawed implementation (see `TD-007`), this value is expected to be `2.0`. After fixing the architecture, it should be `1.0`. Monitoring this helps validate the fix.

*   **Consumer Message Distribution (Flaw-related):**
    *   **Description:** The ratio of messages processed by `DataCollectorService` versus `RollingWindowService`.
    *   **Importance:** Due to the "competing consumer" flaw, this ratio will be arbitrary. After implementing a proper fan-out pattern, both services should process 100% of the incoming messages, making this metric obsolete.