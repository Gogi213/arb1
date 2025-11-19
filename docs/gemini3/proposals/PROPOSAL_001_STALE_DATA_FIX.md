2.  In the calculation loop, validate that the data is fresh (e.g., < 1 second old).
3.  If data is stale, invalidate the state and do not trade.

## Diff

### `trader/src/Core/SpreadListener.cs`

```csharp
<<<<<<< CURRENT
        private decimal? _lastGateBid;
        private decimal? _lastBybitBid;
        private string? _symbol;
        private const decimal SpreadThreshold = 0.25m;
=======
        // Store price AND timestamp
        private (decimal Price, DateTime Timestamp)? _lastGateBid;
        private (decimal Price, DateTime Timestamp)? _lastBybitBid;
        
        private string? _symbol;
        
        // TODO: Move to config (See PROPOSAL 002)
        private const decimal SpreadThreshold = 0.25m; 
        
        // Maximum allowed age for price data
        private static readonly TimeSpan MaxDataAge = TimeSpan.FromSeconds(2);
>>>>>>> PROPOSED
```

```csharp
<<<<<<< CURRENT
                if (string.Equals(data.Exchange, "GateIo", StringComparison.OrdinalIgnoreCase))
                {
                    _lastGateBid = data.BestBid;
                }
                else if (string.Equals(data.Exchange, "Bybit", StringComparison.OrdinalIgnoreCase))
                {
                    _lastBybitBid = data.BestBid;
                }

                if (_lastGateBid.HasValue && _lastBybitBid.HasValue)
                {
                    CalculateAndLogSpreads();
                }
=======
                // Use server timestamp if available, otherwise local time
                var timestamp = data.Timestamp != default ? data.Timestamp : DateTime.UtcNow;

                if (string.Equals(data.Exchange, "GateIo", StringComparison.OrdinalIgnoreCase))
                {
                    _lastGateBid = (data.BestBid, timestamp);
                }
                else if (string.Equals(data.Exchange, "Bybit", StringComparison.OrdinalIgnoreCase))
                {
                    _lastBybitBid = (data.BestBid, timestamp);
                }

                if (_lastGateBid.HasValue && _lastBybitBid.HasValue)
                {
                    CalculateAndLogSpreads();
                }
>>>>>>> PROPOSED
```

```csharp
<<<<<<< CURRENT
        private void CalculateAndLogSpreads()
        {
            if (!_lastGateBid.HasValue || !_lastBybitBid.HasValue || _lastGateBid.Value == 0 || _lastBybitBid.Value == 0)
                return;

            // 1 - гейт -> байбит
            var gateToBybitSpread = (_lastBybitBid.Value / _lastGateBid.Value - 1) * 100;
=======
        private void CalculateAndLogSpreads()
        {
            if (!_lastGateBid.HasValue || !_lastBybitBid.HasValue) return;

            var (gatePrice, gateTime) = _lastGateBid.Value;
            var (bybitPrice, bybitTime) = _lastBybitBid.Value;

            if (gatePrice == 0 || bybitPrice == 0) return;

            // STALE DATA CHECK
            var now = DateTime.UtcNow;
            if (now - gateTime > MaxDataAge || now - bybitTime > MaxDataAge)
            {
                // Optional: Log warning if silence is prolonged
                return; 
            }

            // 1 - гейт -> байбит
            var gateToBybitSpread = (bybitPrice / gatePrice - 1) * 100;
>>>>>>> PROPOSED
```
