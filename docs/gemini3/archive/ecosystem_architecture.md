# Ecosystem Architecture: Future State

```mermaid
graph TD
    subgraph "Collections (SpreadAggregator)"
        Exchanges[Exchange Clients] -->|WS/REST| Orchestrator
        Orchestrator -->|Hot Path (WS)| WebSocketServer
        Orchestrator -->|Cold Path| RawChannel
        Orchestrator -->|Cold Path| RollingChannel
        RawChannel --> ParquetWriter[Parquet Data Writer]
        RollingChannel --> RollingWindow[Rolling Window Stats]
    end

    subgraph "Trader (TraderBot)"
        Listener[SpreadListener] -->|WS| WebSocketServer
        Listener -->|SpreadData| DecisionMaker
        DecisionMaker -->|Signal| StrategyEngine
        StrategyEngine -->|Execute| ArbitrageTrader
        ArbitrageTrader -->|Order| Exchanges
        
        ConfigLoader[Config Loader] --> StrategyEngine
    end

    subgraph "Analyzer"
        ParquetFiles[(Parquet Data)] -->|Read| PolarsEngine
        PolarsEngine -->|Analyze| StatsGenerator
        StatsGenerator -->|Write| StrategyConfig[strategy_settings.json]
    end

    ParquetWriter --> ParquetFiles
    StrategyConfig --> ConfigLoader
```

## Key Data Flows

1.  **Real-time Data**: `Exchanges` -> `Collections` -> `Trader`. Latency: < 10ms internal, + network.
2.  **Historical Data**: `Collections` -> `Parquet` -> `Analyzer`.
3.  **Feedback Loop**: `Analyzer` -> `Config` -> `Trader`.

## Technology Choices
*   **Communication**: WebSocket (Fleck) for low latency.
*   **Data Storage**: Parquet for high compression and fast read speeds (Polars friendly).
*   **Configuration**: JSON (simple, human-readable, easy to generate).
