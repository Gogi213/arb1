# PROPOSAL 002: Externalize Configuration

**Severity**: HIGH
**Component**: Trader (Core)
**Status**: Proposed

## Problem
Key trading parameters are hardcoded in the C# source code:
*   `SpreadThreshold = 0.25m` (SpreadListener.cs)
*   `dollarOffset = 25` (TrailingTrader.cs)

Changing these requires recompiling and redeploying the bot. This prevents dynamic adjustment based on market volatility.

## Proposed Solution
1.  Create a `TradingStrategyConfig` class.
2.  Bind it to `appsettings.json`.
3.  Inject `IOptionsMonitor<TradingStrategyConfig>` into components to allow hot-reloading.

## Diff

### `trader/src/Core/TradingStrategyConfig.cs` (New File)

```csharp
public class TradingStrategyConfig
{
    public decimal MinSpreadPercentage { get; set; } = 0.25m;
    public decimal TrailingBuyOffsetUsd { get; set; } = 25.0m;
    public int MaxTradeDurationMinutes { get; set; } = 60;
}
```

### `trader/src/Host/appsettings.json`

```json
<<<<<<< CURRENT
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "AllowedHosts": "*",
  "SpreadListenerUrl": "ws://localhost:5000/ws"
}
=======
{
  "Logging": { ... },
  "SpreadListenerUrl": "ws://localhost:5000/ws",
  "TradingStrategy": {
    "MinSpreadPercentage": 0.3,
    "TrailingBuyOffsetUsd": 15.0,
    "MaxTradeDurationMinutes": 30
  }
}
>>>>>>> PROPOSED
```

### `trader/src/Core/SpreadListener.cs`

```csharp
<<<<<<< CURRENT
        private const decimal SpreadThreshold = 0.25m;

        public SpreadListener(string serverUrl)
        {
            _ws = new ClientWebSocket();
            // ...
        }
=======
        private readonly TradingStrategyConfig _config;

        public SpreadListener(string serverUrl, TradingStrategyConfig config)
        {
            _config = config;
            _ws = new ClientWebSocket();
            // ...
        }
        
        // In CalculateAndLogSpreads:
        if (gateToBybitSpread >= _config.MinSpreadPercentage)
>>>>>>> PROPOSED
```

### `trader/src/Core/TrailingTrader.cs`

```csharp
<<<<<<< CURRENT
                    newTargetPrice = CalculateTargetPrice(orderBook, 25); // $25 offset for faster fills in testing
=======
                    newTargetPrice = CalculateTargetPrice(orderBook, _config.TrailingBuyOffsetUsd);
>>>>>>> PROPOSED
```
