# Phase 3: The "Sight" Restoration  

**Status:** ⚪ Not Started  
**Goal:** Enable Trader to react to market signals, not timers.

---

## Task 3.1: Signal Listener with WebSocket

**Target:** `trader/src/Core/SignalListener.cs` (New)

```csharp
public class SignalListener
{
    private ClientWebSocket _ws;
    
    public async Task ConnectAsync()
    {
        int retries = 0;
        while (retries < 5)
        {
            try
            {
                await _ws.ConnectAsync(
                    new Uri("ws://localhost:5000/ws/signals"), 
                    CancellationToken.None);
                return;
            }
            catch
            {
                retries++;
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retries))); // Exponential backoff
            }
        }
        throw new Exception("Failed to connect after 5 retries");
    }
    
    public async Task<ExitReason> WaitForExitAsync(
        string symbol, 
        decimal stopLoss, 
        decimal takeProfit, 
        TimeSpan timeout)
    {
        try
        {
            await foreach (var signal in ReadSignalsAsync(cts.Token))
            {
                if (signal.Deviation <= takeProfit) return ExitReason.TakeProfit;
                if (signal.Deviation <= stopLoss) return ExitReason.StopLoss;
            }
        }
        catch (OperationCanceledException)
        {
            return ExitReason.Timeout;
        }
        catch (WebSocketException)
        {
            return ExitReason.Disconnected;
        }
        return ExitReason.Unknown;
    }
}
```

---

## Task 3.2: Dynamic Exit Logic

Replace hardcoded 60s timer with signal-based exit. Fallback to timer if disconnected (graceful degradation).

---

## Task 3.3: Network Disconnect Fallback

Switch to timer-based mode if signal stream unavailable >10s.

---

[← Prev: Security](phase-2-security.md) | [Back to Roadmap](README.md) | [Next: Auto-Pilot →](phase-4-autopilot.md)
