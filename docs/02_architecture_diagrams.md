# Архитектура Системы (Визуализация)

**Дата:** 2025-11-10
**Навигация:** [← Текущее Состояние](01_current_state.md) | [Проблемы →](03_integration_problems.md) | [Roadmap →](04_implementation_roadmap.md)

---

## 1. Общая Архитектура Компонентов

```mermaid
graph TB
    subgraph "Exchanges"
        E1[Binance]
        E2[Bybit]
        E3[GateIo]
        E4[OKX/Kucoin/etc]
    end

    subgraph "Collections (.NET)"
        ORC[OrchestrationService]
        WS[FleckWebSocketServer<br/>ws://127.0.0.1:8181]
        RW[RollingWindowService<br/>15-min window]
        PW[ParquetDataWriter]
        DASH[Dashboard<br/>localhost:5000]
    end

    subgraph "Data Lake"
        PARQUET[(Parquet Files<br/>data/market_data/)]
    end

    subgraph "Analyzer (Python)"
        AN[run_all_ultra.py]
        JSON[analyzer_output.json]
    end

    subgraph "Trader (.NET)"
        SL[SpreadListener]
        DM[DecisionMaker]
        AT[ArbitrageTrader]
        RT[ReverseArbitrageTrader]
    end

    E1 & E2 & E3 & E4 -->|bid/ask| ORC
    ORC -->|real-time spreads| WS
    ORC -->|buffer| PW
    PW -->|persist| PARQUET
    ORC -->|15-min data| RW
    RW -->|chart data| DASH

    PARQUET -.->|read historical| AN
    AN -.->|export| JSON
    JSON -.->|manual copy| DM

    WS -->|WebSocket stream| SL
    SL -->|OnProfitableSpreadDetected| DM
    DM -->|LEG1| AT
    DM -->|LEG2| RT
    AT & RT -->|place orders| E2 & E3

    style ORC fill:#90EE90
    style DM fill:#FFB6C1
    style WS fill:#87CEEB
    style PARQUET fill:#DDA0DD
```

---

## 2. Поток Данных (Real-time)

```mermaid
sequenceDiagram
    participant Binance
    participant Collections
    participant WebSocket
    participant Trader
    participant GateIo
    participant Bybit

    Note over Binance,Collections: Сбор данных от бирж
    Binance->>Collections: BTC/USDT: bid=43250.5, ask=43251.0

    Note over Collections: Нормализация символа
    Collections->>Collections: BTCUSDT (BTC_USDT после fix)
    Collections->>Collections: Calculate spread %

    Note over Collections,WebSocket: Трансляция
    Collections->>WebSocket: {"MessageType": "Spread", "Payload": {...}}
    WebSocket->>Trader: SpreadListener получает

    Note over Trader: Анализ спреда
    Trader->>Trader: GateIo bid: 1.234<br/>Bybit bid: 1.239<br/>Spread: 0.40%

    alt Spread >= 0.4% (LEG1 threshold)
        Trader->>GateIo: Place limit BUY order
        GateIo-->>Trader: Order filled
        Trader->>Bybit: Place market SELL order
        Bybit-->>Trader: Order filled
        Note over Trader: LEG1 done, waiting for spread ~0%
    end

    alt Spread ~= 0% (LEG2 threshold)
        Trader->>Bybit: Place limit BUY order
        Bybit-->>Trader: Order filled
        Trader->>GateIo: Place market SELL order
        GateIo-->>Trader: Order filled
        Note over Trader: LEG2 done, cycle complete
    end
```

---

## 3. DecisionMaker State Machine

```mermaid
stateDiagram-v2
    [*] --> IDLE

    IDLE --> WAITING_LEG1: SpreadListener connected

    WAITING_LEG1 --> LEG1_IN_PROGRESS: spread >= leg1Threshold (0.4%)

    LEG1_IN_PROGRESS --> WAITING_LEG2: ArbitrageTrader completed
    LEG1_IN_PROGRESS --> ERROR: Trader exception

    WAITING_LEG2 --> LEG2_IN_PROGRESS: spread ~= 0% (±0.05%)

    LEG2_IN_PROGRESS --> CYCLE_COMPLETE: ReverseArbitrageTrader completed
    LEG2_IN_PROGRESS --> ERROR: Trader exception

    CYCLE_COMPLETE --> WAITING_LEG1: Reset state

    ERROR --> WAITING_LEG1: Log error, reset after timeout

    note right of WAITING_LEG1
        _isCycleInProgress = false
        _waitingForLeg2 = false
    end note

    note right of LEG1_IN_PROGRESS
        _isCycleInProgress = true
        _waitingForLeg2 = false
    end note

    note right of WAITING_LEG2
        _isCycleInProgress = true
        _waitingForLeg2 = true
    end note
```

---

## 4. Торговый Цикл (Детальная Последовательность)

```mermaid
sequenceDiagram
    participant Collections
    participant SpreadListener
    participant DecisionMaker
    participant ArbitrageTrader
    participant GateIo
    participant Bybit

    Note over Collections: Спред растет: -0.2% → 0.0% → 0.3% → 0.41%

    Collections->>SpreadListener: Spread: 0.41% (GateIo→Bybit)
    SpreadListener->>SpreadListener: CalculateSpreads()
    SpreadListener->>DecisionMaker: OnProfitableSpreadDetected("GateIo_To_Bybit", 0.41)

    Note over DecisionMaker: State: WAITING_LEG1
    DecisionMaker->>DecisionMaker: Check: spread >= 0.4% ✓<br/>_isCycleInProgress == false ✓
    DecisionMaker->>DecisionMaker: Set _isCycleInProgress = true<br/>Set _waitingForLeg2 = false

    Note over DecisionMaker: State: LEG1_IN_PROGRESS
    DecisionMaker->>ArbitrageTrader: StartAsync("VIRTUAL_USDT", 6, ...)

    Note over ArbitrageTrader,Bybit: LEG 1 Execution
    ArbitrageTrader->>GateIo: Subscribe to balance updates
    ArbitrageTrader->>Bybit: Subscribe to order updates
    ArbitrageTrader->>GateIo: TrailingTrader: Place limit BUY (follows orderbook)
    GateIo-->>ArbitrageTrader: Order filled (6.0 VIRTUAL)
    ArbitrageTrader->>ArbitrageTrader: Wait for balance update (debounce 150ms)
    GateIo-->>ArbitrageTrader: Balance update: 6.0 VIRTUAL available
    ArbitrageTrader->>Bybit: Place market SELL (6.0 VIRTUAL)
    Bybit-->>ArbitrageTrader: Order filled, got 7.4214 USDT
    ArbitrageTrader-->>DecisionMaker: Return 7.4214 USDT

    Note over DecisionMaker: State: WAITING_LEG2
    DecisionMaker->>DecisionMaker: Set _waitingForLeg2 = true
    DecisionMaker->>DecisionMaker: Log: "LEG1 done, waiting for spread ~0%"

    Note over Collections: Спред снижается: 0.41% → 0.25% → 0.10% → 0.02%

    Collections->>SpreadListener: Spread: 0.02% (near zero)
    SpreadListener->>DecisionMaker: OnProfitableSpreadDetected(...)
    DecisionMaker->>DecisionMaker: Check: _waitingForLeg2 == true ✓<br/>abs(spread) <= 0.05% ✓

    Note over DecisionMaker: State: LEG2_IN_PROGRESS
    DecisionMaker->>ArbitrageTrader: ReverseArbitrageTrader.StartAsync(...)

    Note over ArbitrageTrader,GateIo: LEG 2 Execution (Rebalance)
    ArbitrageTrader->>Bybit: TrailingTrader: Place limit BUY (7.4214 USDT)
    Bybit-->>ArbitrageTrader: Order filled (6.0 VIRTUAL bought)
    ArbitrageTrader->>GateIo: Place market SELL (6.0 VIRTUAL)
    GateIo-->>ArbitrageTrader: Order filled
    ArbitrageTrader-->>DecisionMaker: Return final USDT

    Note over DecisionMaker: State: CYCLE_COMPLETE
    DecisionMaker->>DecisionMaker: Set _isCycleInProgress = false<br/>Set _waitingForLeg2 = false
    DecisionMaker->>DecisionMaker: Log: "Full cycle complete. Profit: +$X"

    Note over DecisionMaker: State: WAITING_LEG1 (ready for next cycle)
```

---

## 5. Ключевые Классы и Зависимости

```mermaid
classDiagram
    class SpreadListener {
        -ClientWebSocket _ws
        -decimal? _lastGateBid
        -decimal? _lastBybitBid
        -string _symbol
        +event OnProfitableSpreadDetected
        +StartAsync(url)
        -ReceiveLoop()
        -ProcessSpreadDataAsync(data)
        -CalculateAndLogSpreads()
    }

    class DecisionMaker {
        -bool _isCycleInProgress
        -bool _waitingForLeg2
        -IConfiguration _config
        -ITrader _arbitrageTrader
        -ITrader _reverseArbitrageTrader
        +Subscribe(SpreadListener)
        -HandleProfitableSpread(direction, spread)
    }

    class ArbitrageTrader {
        -IExchange _buyExchange
        -IExchange _sellExchange
        -TrailingTrader _trailingTrader
        -ArbitrageCycleState _state
        +StartAsync(symbol, amount, duration, state)
        -HandleBuyOrderFilled(order)
        -HandleSellOrderUpdate(order)
    }

    class ReverseArbitrageTrader {
        -IExchange _buyExchange
        -IExchange _sellExchange
        -BybitTrailingTrader _trailingTrader
        -ArbitrageCycleState _state
        +StartAsync(symbol, amount, duration, state)
    }

    class ArbitrageCycleState {
        +decimal InitialGateIoBaseAssetBalance
        +decimal Leg1GateBuyFilledQuantity
        +decimal Leg1BybitSellResultUsdtAmount
    }

    class IExchange {
        <<interface>>
        +GetSymbolFiltersAsync(symbol)
        +PlaceOrderAsync(...)
        +SubscribeToOrderUpdatesAsync(handler)
        +SubscribeToBalanceUpdatesAsync(handler)
    }

    SpreadListener --> DecisionMaker : triggers
    DecisionMaker --> ArbitrageTrader : LEG1
    DecisionMaker --> ReverseArbitrageTrader : LEG2
    ArbitrageTrader --> IExchange : uses
    ReverseArbitrageTrader --> IExchange : uses
    ArbitrageTrader --> ArbitrageCycleState : shares
    ReverseArbitrageTrader --> ArbitrageCycleState : shares
```

---

## 6. Analyzer → Dashboard → Trader Integration Flow

```mermaid
graph TB
    subgraph "Offline Analysis (Hourly)"
        PARQUET[(Parquet<br/>historical data)]
        ANALYZER[Analyzer<br/>run_all_ultra.py]
        CSV[summary_stats_*.csv]

        PARQUET -->|read| ANALYZER
        ANALYZER -->|export metrics| CSV
    end

    subgraph "Collections Dashboard (Port 5000)"
        OPP_FILTER[OpportunityFilterService]
        DASHBOARD[Dashboard UI<br/>localhost:5000]

        CSV -->|read latest| OPP_FILTER
        OPP_FILTER -->|filter cycles > 40| DASHBOARD
    end

    subgraph "Manual Selection"
        USER[User Reviews<br/>Dashboard]
        CONFIG[trader/appsettings.json]

        DASHBOARD -->|view profitable pairs| USER
        USER -.->|copy pair config| CONFIG
    end

    subgraph "Real-time Trading"
        DM[DecisionMaker]
        CONFIG -->|reads on startup| DM
    end

    style USER fill:#FFD700
    style DASHBOARD fill:#90EE90
    style CONFIG fill:#FFB6C1
    style OPP_FILTER fill:#87CEEB
```

---

**Следующий шаг:** [Посмотреть критичные проблемы интеграции →](03_integration_problems.md)
