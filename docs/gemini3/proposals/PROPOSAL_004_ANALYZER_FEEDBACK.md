# PROPOSAL 004: Analyzer Feedback Loop

**Severity**: MEDIUM
**Component**: Ecosystem (Analyzer -> Trader)
**Status**: Proposed

## Problem
The `Analyzer` calculates valuable metrics (e.g., "Pair X has 90% mean reversion probability"), but this data is dead. The `Trader` blindly trades whatever it is hardcoded to trade or whatever the user manually starts.

## Proposed Solution
Establish a file-based contract where `Analyzer` writes recommended settings, and `Trader` reads them.

## Workflow
1.  **Analyzer**: Runs `run_all_ultra.py`.
2.  **Analyzer**: Generates `trading_recommendations.json` in a shared folder.
3.  **Trader**: `DecisionMaker` reads this file to decide *which* pairs to accept signals for.

## Diff

### Shared File Structure
`c:\visual projects\arb1\data\config\trading_recommendations.json`

```json
{
  "generated_at": "2025-11-19T20:00:00Z",
  "pairs": {
    "VIRTUAL_USDT": {
      "status": "active",
      "min_spread": 0.35,  // Analyzer suggests higher spread due to volatility
      "buy_exchange": "GateIo",
      "sell_exchange": "Bybit"
    },
    "AI16Z_USDT": {
      "status": "blacklisted", // Analyzer detected low liquidity or high risk
      "reason": "low_volume"
    }
  }
}
```

### `trader/src/Core/DecisionMaker.cs`

```csharp
<<<<<<< CURRENT
        private void HandleProfitableSpread(string direction, decimal spread)
        {
            if (_isCycleInProgress)
            {
                // ...
                return;
            }

            _isCycleInProgress = true;
            // ...
        }
=======
        private void HandleProfitableSpread(string direction, decimal spread)
        {
            // 1. Check if pair is allowed by Analyzer
            var recommendations = _configLoader.GetRecommendations();
            if (!recommendations.IsPairAllowed(_symbol, direction))
            {
                FileLogger.LogOther($"[DecisionMaker] Spread {spread}% detected, but Analyzer blacklisted {_symbol}. Ignoring.");
                return;
            }

            // 2. Check dynamic threshold
            var minSpread = recommendations.GetMinSpread(_symbol) ?? _defaultMinSpread;
            if (spread < minSpread)
            {
                 FileLogger.LogOther($"[DecisionMaker] Spread {spread}% < Analyzer recommended {minSpread}%. Ignoring.");
                 return;
            }

            if (_isCycleInProgress)
            {
                // ...
                return;
            }
            
            // ...
        }
>>>>>>> PROPOSED
```
