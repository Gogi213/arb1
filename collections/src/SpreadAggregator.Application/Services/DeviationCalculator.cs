using SpreadAggregator.Domain.Entities;
using System.Collections.Concurrent;

namespace SpreadAggregator.Application.Services;

/// <summary>
/// Phase 1, Task 1.1: Calculates cross-exchange price deviation for arbitrage opportunities.
/// Target latency: <10ms per update.
/// </summary>
public class DeviationCalculator
{
    // Latest spread data per exchange per symbol
    // Key: "Exchange:Symbol" (e.g., "Gate:BTC_USDT")
    private readonly ConcurrentDictionary<string, SpreadData> _latestSpreads = new();

    // Deviation threshold for filtering (configurable)
    private readonly decimal _minDeviationThreshold;

    /// <summary>
    /// Event fired when significant deviation detected.
    /// </summary>
    public event Action<DeviationData>? OnDeviationDetected;

    public DeviationCalculator(decimal minDeviationThreshold = 0.10m)
    {
        _minDeviationThreshold = minDeviationThreshold;
    }

    /// <summary>
    /// Process incoming spread data and calculate cross-exchange deviations.
    /// Call this from OrchestrationService when new spread arrives.
    /// </summary>
    public void ProcessSpread(SpreadData spread)
    {
        var key = $"{spread.Exchange}:{spread.Symbol}";
        
        // Update latest spread for this exchange:symbol
        _latestSpreads[key] = spread;

        // Try to find matching spread from other exchange
        TryCalculateDeviation(spread);
    }

    private void TryCalculateDeviation(SpreadData newSpread)
    {
        // Look for spreads from other exchanges for the same symbol
        var otherExchangeSpreads = _latestSpreads
            .Where(kvp => kvp.Key.EndsWith($":{newSpread.Symbol}") && 
                         !kvp.Key.StartsWith($"{newSpread.Exchange}:"))
            .Select(kvp => kvp.Value)
            .ToList();

        if (!otherExchangeSpreads.Any())
        {
            // No other exchange data yet for this symbol
            return;
        }

        // For each other exchange, calculate deviation
        foreach (var otherSpread in otherExchangeSpreads)
        {
            var deviation = CalculateDeviation(newSpread, otherSpread);
            
            if (deviation != null && Math.Abs(deviation.DeviationPercentage) >= _minDeviationThreshold)
            {
                // Significant deviation detected - emit event
                OnDeviationDetected?.Invoke(deviation);
            }
        }
    }

    private DeviationData? CalculateDeviation(SpreadData spread1, SpreadData spread2)
    {
        // User's strategy: bid/bid comparison
        // Buy limit at bid on cheap exchange, sell market (executes at bid) on expensive exchange
        var bid1 = spread1.BestBid;
        var bid2 = spread2.BestBid;

        if (bid1 == 0 || bid2 == 0)
        {
            // Invalid prices, skip
            return null;
        }

        // Determine which bid is cheaper/expensive
        decimal cheapBid, expensiveBid;
        string cheapExchange, expensiveExchange;
        DateTime? serverTimestamp;

        if (bid1 < bid2)
        {
            cheapBid = bid1;
            expensiveBid = bid2;
            cheapExchange = spread1.Exchange;
            expensiveExchange = spread2.Exchange;
            // Use newer timestamp
            serverTimestamp = spread1.ServerTimestamp ?? spread2.ServerTimestamp;
        }
        else
        {
            cheapBid = bid2;
            expensiveBid = bid1;
            cheapExchange = spread2.Exchange;
            expensiveExchange = spread1.Exchange;
            serverTimestamp = spread2.ServerTimestamp ?? spread1.ServerTimestamp;
        }

        // Calculate deviation percentage (bid vs bid)
        var deviationPercentage = (expensiveBid - cheapBid) / cheapBid * 100;

        return new DeviationData
        {
            Symbol = spread1.Symbol,
            DeviationPercentage = Math.Round(deviationPercentage, 2), // 0.01% precision
            CheapExchange = cheapExchange,
            ExpensiveExchange = expensiveExchange,
            CheapPrice = cheapBid,      // Now represents BestBid, not midpoint
            ExpensivePrice = expensiveBid, // Now represents BestBid, not midpoint
            Timestamp = DateTime.UtcNow,
            ServerTimestamp = serverTimestamp
        };
    }

    /// <summary>
    /// Get current deviation for a specific symbol (if available).
    /// Used by API endpoints.
    /// </summary>
    public DeviationData? GetCurrentDeviation(string symbol, string exchange1, string exchange2)
    {
        var key1 = $"{exchange1}:{symbol}";
        var key2 = $"{exchange2}:{symbol}";

        if (_latestSpreads.TryGetValue(key1, out var spread1) && 
            _latestSpreads.TryGetValue(key2, out var spread2))
        {
            return CalculateDeviation(spread1, spread2);
        }

        return null;
    }

    /// <summary>
    /// Get all current deviations across all symbols.
    /// Used by API endpoints.
    /// </summary>
    public List<DeviationData> GetAllDeviations()
    {
        var deviations = new List<DeviationData>();
        
        // Group by symbol
        var symbolGroups = _latestSpreads
            .GroupBy(kvp => kvp.Key.Split(':')[1]); // Extract symbol from "Exchange:Symbol"

        foreach (var group in symbolGroups)
        {
            var spreads = group.Select(kvp => kvp.Value).ToList();
            
            // Calculate deviations between all exchange pairs
            for (int i = 0; i < spreads.Count; i++)
            {
                for (int j = i + 1; j < spreads.Count; j++)
                {
                    var deviation = CalculateDeviation(spreads[i], spreads[j]);
                    if (deviation != null)
                    {
                        deviations.Add(deviation);
                    }
                }
            }
        }

        return deviations;
    }
}
