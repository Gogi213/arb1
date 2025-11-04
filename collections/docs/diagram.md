graph TD
    subgraph Presentation
        A[SpreadAggregator.Presentation]
        A_Program["Program.cs (Main)"]
        A_OrchestrationServiceHost["OrchestrationServiceHost (IHostedService)"]

        A_Program --> A_OrchestrationServiceHost
        A_Program --> B_DataCollectorService
        A_OrchestrationServiceHost --> B_OrchestrationService
    end

    subgraph Application
        B[SpreadAggregator.Application]
        B_IDataWriter["IDataWriter"]
        B_IExchangeClient["IExchangeClient"]
        B_IWebSocketServer["IWebSocketServer"]
        B_TickerData["TickerData (DTO)"]
        B_DataCollectorService["DataCollectorService"]
        B_OrchestrationService["OrchestrationService"]

        B_OrchestrationService --> B_IExchangeClient
        B_OrchestrationService --> B_IWebSocketServer
        B_OrchestrationService --> B_IDataWriter
        B_OrchestrationService --> C_SpreadCalculator
        B_OrchestrationService --> C_VolumeFilter
        B_DataCollectorService --> B_IDataWriter
    end

    subgraph Domain
        C[SpreadAggregator.Domain]
        C_MarketData["MarketData (abstract)"]
        C_SpreadData["SpreadData"]
        C_TradeData["TradeData"]
        C_WebSocketMessage["WebSocketMessage"]
        C_SpreadCalculator["SpreadCalculator"]
        C_VolumeFilter["VolumeFilter"]

        C_SpreadData --> C_MarketData
        C_TradeData --> C_MarketData
    end

    subgraph Infrastructure
        D[SpreadAggregator.Infrastructure]
        D_FleckWebSocketServer["FleckWebSocketServer"]
        D_ParquetDataWriter["ParquetDataWriter"]
        D_ExchangeClientBase["ExchangeClientBase (abstract)"]
        D_BinanceExchangeClient["BinanceExchangeClient"]
        D_BybitExchangeClient["BybitExchangeClient"]
        D_GateIoExchangeClient["GateIoExchangeClient"]
        D_KucoinExchangeClient["KucoinExchangeClient"]
        D_MexcExchangeClient["MexcExchangeClient"]
        D_OkxExchangeClient["OKXExchangeClient"]
        D_BingXExchangeClient["BingXExchangeClient"]
        D_BitgetExchangeClient["BitgetExchangeClient"]

        D_FleckWebSocketServer --> B_IWebSocketServer
        D_ParquetDataWriter --> B_IDataWriter
        D_ExchangeClientBase --> B_IExchangeClient
        D_BinanceExchangeClient --> D_ExchangeClientBase
        D_BybitExchangeClient --> D_ExchangeClientBase
        D_GateIoExchangeClient --> D_ExchangeClientBase
        D_KucoinExchangeClient --> D_ExchangeClientBase
        D_MexcExchangeClient --> D_ExchangeClientBase
        D_OkxExchangeClient --> D_ExchangeClientBase
        D_BingXExchangeClient --> D_ExchangeClientBase
        D_BitgetExchangeClient --> D_ExchangeClientBase
    end

    A --> B
    A --> D
    B --> C
    D --> B
