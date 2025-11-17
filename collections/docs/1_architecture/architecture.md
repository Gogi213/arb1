# Detailed Architecture of the Collections Project (SpreadAggregator)
**Version:** 1.4 (Validated on 2025-11-16)

## 1. Overview

The `SpreadAggregator` project (part of the `collections` initiative) is a .NET application developed using Clean Architecture and event-driven principles. Its primary function is to aggregate real-time market data from multiple cryptocurrency exchanges, calculate arbitrage spreads, broadcast them via a WebSocket server, and save all raw data to Parquet files for later analysis.

## 2. Architectural Layers (Clean Architecture)

The project strictly adheres to Clean Architecture principles, ensuring separation of concerns and loose coupling. Dependencies are directed inwards—from outer layers to inner layers.

```
┌─────────────────────────────────────────┐
│        Presentation Layer               │
│  ┌─────────────────────────────────┐    │
│  │   Controllers (API)             │    │
│  │   Hosted Services (Entry Point) │    │
│  └─────────────────────────────────┘    │
├─────────────────────────────────────────┤
│        Application Layer                │
│  ┌─────────────────────────────────┐    │
│  │   Services (Orchestration, etc) │    │
│  │   Interfaces (IExchangeClient)  │    │
│  └─────────────────────────────────┘    │
├─────────────────────────────────────────┤
│        Domain Layer                     │
│  ┌─────────────────────────────────┐    │
│  │   Entities (MarketData, Spread) │    │
│  │   Services (SpreadCalculator)   │    │
│  └─────────────────────────────────┘    │
├─────────────────────────────────────────┤
│        Infrastructure Layer             │
│  ┌─────────────────────────────────┐    │
│  │   Exchange Clients (Binance.Net)│    │
│  │   Data Persistence (Parquet)    │    │
│  │   WebSocket Server (Fleck)      │    │
│  └─────────────────────────────────┘    │
└─────────────────────────────────────────┘
```

### 2.1. `Domain`
*   **Responsibility:** The core of the application. Contains business entities (`MarketData`, `Spread`) and key business logic (`SpreadCalculator`) that is independent of implementation details.

### 2.2. `Application`
*   **Responsibility:** Orchestrates the domain logic. Defines *what* the application should do. Contains services (`OrchestrationService`, `DataCollectorService`) and interfaces for external dependencies (`IDataWriter`, `IExchangeClient`).

### 2.3. `Infrastructure`
*   **Responsibility:** Implements interfaces from the `Application` layer. Contains code for interacting with the outside world: clients for exchange APIs, a service for writing Parquet files (`ParquetDataWriter`), and a WebSocket server based on `Fleck`.

### 2.4. `Presentation`
*   **Responsibility:** The application's entry point and host. It's an ASP.NET Core application that configures Dependency Injection, settings, and runs background services (`IHostedService`) in `Program.cs`. It also exposes external interfaces (API and WebSockets).

## 3. Event-Driven Data Flow

Instead of inefficient polling, the system is built on events, ensuring immediate reaction to changes and low resource consumption.

### 3.1. Key Flow Components

*   **Exchange Clients (`Infrastructure`):** Subscribe to the WebSocket streams of exchanges. As soon as a new message (data tick) arrives, they parse it into the `MarketData` domain model and immediately write it to a channel.
*   **`System.Threading.Channels`:** The core of asynchronous communication. A single `Channel.CreateBounded<MarketData>` with the `DropOldest` option is used as a central data bus.
*   **`OrchestrationService` (`Application`):** The primary producer of data. It receives raw data from exchange clients, uses `SpreadCalculator` to compute spreads, and then publishes the data to consumers.
*   **Consumers:**
    *   **`DataCollectorService` & `RollingWindowService`:** These background services act as **competing consumers** on the central channel. Each service reads from the channel in a loop, effectively taking messages from the other.
    *   **`WebSocketServer` (`Infrastructure`):** Receives calculated spread data *directly* from the `OrchestrationService` for immediate broadcast to all connected clients.

### 3.2. Critical Architectural Flaw: Duplicated Writes and Competing Consumers

The current implementation contains a significant architectural flaw:

1.  **Duplicated Writes:** Due to a misconfiguration in `Program.cs`, the `OrchestrationService` receives two references to the *same* channel instance (`_rawDataChannel` and `_rollingWindowChannel` are the same object). It then writes every incoming message to this channel **twice**.
2.  **Competing Consumers:** `DataCollectorService` (which triggers `ParquetDataWriter`) and `RollingWindowService` both read from this single channel. They are not fanning out; they are competing. This means that if `DataCollectorService` reads a message, `RollingWindowService` will not see it, and vice versa.

The result is that both the persisted Parquet data and the real-time window analysis are based on an incomplete and arbitrarily interleaved subset of the duplicated data stream.

### 3.3. Data Flow Diagram

```
                                         ┌──────────────────┐
                                    ┌───▶│ WebSocketServer  │───▶ WebSocket Clients
                                    │    └──────────────────┘
┌─────────────┐    ┌────────────────┐    │
│ Exchange    │───▶│ Channel        │───▶│ Orchestration    │
│ Clients     │    │ (Raw MarketData) │    │ Service          │
└─────────────┘    └────────────────┘    │ (Writes 2x)        │
      ▲                                  └──────────────────┘
      │
      │           (COMPETING CONSUMERS)
      └──────────────────┬──────────────────┘
                         │
                         ▼
        ┌────────────────┴────────────────┐
        │ DataCollectorSvc│ RollingWindowSvc│
        └─────────────────┴─────────────────┘
```

## 4. APIs and External Interfaces

### 4.1. WebSocket Endpoint

*   **Endpoint:** `ws://localhost:5000/ws/realtime_charts`
*   **Description:** The primary interface for receiving real-time spread data. A client connects to this endpoint and receives JSON messages upon each new spread calculation.
*   **Message Format:**
    ```json
    {
      "symbol": "ICPUSDT",
      "exchange1": "Bybit",
      "exchange2": "GateIo",
      "timestamps": [1731204612.345],
      "spreads": [-0.024966]
    }
    ```

### 4.2. HTTP Endpoints

*   `GET /api/health`: Checks the application's health. Returns `200 OK` with status "Healthy".
*   `GET /api/dashboard_data`: Delivers historical data for building charts in NDJSON (Newline Delimited JSON) format, which allows for streaming large datasets.
*   `GET /index.html`: Serves a static HTML file with a UI dashboard for data visualization.

## 5. Performance and Reliability

### 5.1. Performance
*   **Asynchronicity:** All I/O-bound operations (network, disk) are fully asynchronous (`async/await`), preventing thread blocking.
*   **Backpressure:** Bounded channels (`BoundedChannel`) with a `DropOldest` policy ensure the system won't crash due to memory overflow during abnormal market data spikes.
*   **Minimal Allocations:** The use of `System.Threading.Channels` and other modern .NET APIs minimizes memory allocations on the "hot path" of data processing.

### 5.2. Reliability
*   **Isolation:** Each exchange client and each WebSocket client runs in its own isolated task. An error in one does not affect the others.
*   **Graceful Shutdown:** Ensures proper stopping of all background services and closing of connections when the application terminates.
*   **Logging:** Structured logging is implemented for key events, errors, and performance metrics.
*   **Memory Leak Prevention:** The `RealTimeController` implements logic to unsubscribe from `WindowDataUpdated` events when a WebSocket client disconnects, which is critical for preventing memory leaks.

## 6. Development and Deployment

*   **Running:** The project is run as a standard ASP.NET Core application with the `dotnet run` command from the `SpreadAggregator.Presentation` directory.
*   **Testing:** Unit and integration tests are provided and can be run with `dotnet test`.
*   **Deployment:** The application can be published as a self-contained application (`dotnet publish`) and run directly, or it can be packaged into a Docker container.
