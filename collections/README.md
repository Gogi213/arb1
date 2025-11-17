# Collections Project: SpreadAggregator

## 1. Overview

`SpreadAggregator` is a .NET application designed to collect, process, and distribute real-time cryptocurrency market data. It is built following Clean Architecture principles to ensure a high degree of maintainability and separation of concerns.

The project's core responsibilities include:
- **Connecting** to multiple cryptocurrency exchanges via their WebSocket APIs.
- **Subscribing** to real-time data streams (order books and trades).
- **Calculating** arbitrage spreads between different exchanges.
- **Broadcasting** the calculated spread data to clients via a WebSocket server.
- **Persisting** all raw market data to Parquet files for offline analysis by the `analyzer` project.

## 2. Architecture

The application is divided into four main layers:
- **Domain:** Contains the core business logic and entities.
- **Application:** Orchestrates the domain logic and defines interfaces for external services.
- **Infrastructure:** Implements the interfaces, providing concrete clients for exchanges, data writers, and the WebSocket server.
- **Presentation:** The entry point of the application, an ASP.NET Core project responsible for configuration, dependency injection, and hosting the background services.

## 3. How to Run

### Prerequisites
- .NET SDK (version specified in the project files)

### Configuration
1.  Copy `appsettings.example.json` to `appsettings.json`.
2.  Fill in the required configuration values, such as the WebSocket server connection string and any necessary API keys for the exchange clients.
3.  Configure the desired exchanges and volume filters under the `ExchangeSettings` section.

### Execution
Navigate to the Presentation layer directory and run the application:
```bash
cd src/SpreadAggregator.Presentation
dotnet run
```
The application will start, connect to the configured exchanges, and begin broadcasting data.

## 4. APIs and Interfaces

- **WebSocket Endpoint:** `ws://localhost:5000/ws/realtime_charts` (configurable in `appsettings.json`)
- **HTTP API:** The application also exposes several HTTP endpoints for health checks and serving a simple HTML dashboard.