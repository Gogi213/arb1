# Phase 4: The "Auto-Pilot"

**Status:** ⚪ Not Started  
**Goal:** Fully automated trading loop.

---

## Task 4.1: Auto-Pilot Service

**Target:** `trader/src/Services/Au toPilotService.cs` (New)

```csharp
public class AutoPilotService : IHostedService
{
    private readonly SignalDetectionService _signalService;
    private readonly ConvergentTrader _trader;
    private readonly HashSet<string> _cooldownPairs = new();
    private const int CooldownMinutes = 30;

    private async Task MonitorSignalsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var signals = _signalService.GetActiveSignals()
                .Where(s => !_cooldownPairs.Contains(s.PairKey))
                .OrderByDescending(s => s.Strength)
                .Take(1);

            foreach (var signal in signals)
            {
                _logger.LogInformation("🤖 Auto-pilot executing on {Pair}", signal.PairKey);
                await _trader.ExecuteCycleAsync(signal);
                
                // Cooldown to prevent loops
                _cooldownPairs.Add(signal.PairKey);
                _ = Task.Run(async () => {
                    await Task.Delay(TimeSpan.FromMinutes(CooldownMinutes));
                    _cooldownPairs.Remove(signal.PairKey);
                });
            }

            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
}
```

---

## Task 4.2: Position Manager

Track open positions, prevent double-entry.

---

## Task 4.3: Risk Limits

Max loss per day, max open positions.

---

[← Prev: Sight](phase-3-sight.md) | [Back to Roadmap](README.md) | [Next: Web UI →](phase-5-webui.md)
