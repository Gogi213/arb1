# PROPOSAL 003: Circuit Breaker & Risk Management

**Severity**: HIGH
**Component**: Trader (ArbitrageTrader)
**Status**: Proposed

## Problem
The bot currently has no "stop loss" mechanism for the account as a whole. If a bug or market anomaly causes it to make 10 losing trades in a row, it will continue until the wallet is empty.

## Proposed Solution
Implement a `CircuitBreaker` that tracks:
1.  **Consecutive Failures**: If > 3 trades fail (or result in loss), stop.
2.  **Daily Drawdown**: If balance drops by > X%, stop.

## Diff

### `trader/src/Core/ArbitrageTrader.cs`

```csharp
<<<<<<< CURRENT
    public class ArbitrageTrader : ITrader
    {
        private readonly IExchange _buyExchange;
        private readonly IExchange _sellExchange;
        // ...
        
        public async Task<decimal> StartAsync(string symbol, decimal amount, int durationMinutes, ArbitrageCycleState state)
        {
            // ... logic ...
            return await _arbitrageCycleTcs.Task;
        }
=======
    public class ArbitrageTrader : ITrader
    {
        private readonly IExchange _buyExchange;
        // ...
        
        // Risk State
        private static int _consecutiveFailures = 0;
        private const int MaxConsecutiveFailures = 3;

        public async Task<decimal> StartAsync(string symbol, decimal amount, int durationMinutes, ArbitrageCycleState state)
        {
            if (_consecutiveFailures >= MaxConsecutiveFailures)
            {
                FileLogger.LogOther($"[RISK] Circuit Breaker Tripped! {MaxConsecutiveFailures} consecutive failures. Trading halted.");
                throw new InvalidOperationException("Circuit Breaker Tripped");
            }

            try 
            {
                // ... existing logic ...
                
                var result = await _arbitrageCycleTcs.Task;
                
                if (result > 0) _consecutiveFailures = 0; // Reset on success
                else _consecutiveFailures++;
                
                return result;
            }
            catch (Exception)
            {
                _consecutiveFailures++;
                throw;
            }
        }
>>>>>>> PROPOSED
```
