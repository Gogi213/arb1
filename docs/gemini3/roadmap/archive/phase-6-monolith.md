# Phase 6: The Monolith (HFT Latency)

**Status:** ⚪ Not Started  
**Goal:** Merge all services into single process for sub-millisecond execution.

---

## Task 6.1: Process Consolidation

**Target:** `Arb1.TradingHost/Program.cs` (New unified project)

```csharp
var builder = Host.CreateApplicationBuilder(args);

// Shared in-memory channel
var signalChannel = Channel.CreateUnbounded<Signal>();
builder.Services.AddSingleton(signalChannel);

// Collections services
builder.Services.AddSingleton<OrchestrationService>();
builder.Services.AddSingleton<SignalDetectionService>();

// Trader services
builder.Services.AddSingleton<ConvergentTrader>();
builder.Services.AddSingleton<AutoPilotService>();

await builder.Build().RunAsync();
```

---

## Task 6.2: In-Memory Channels

Replace WebSocket IPC with `System.Threading.Channels`.

**Before:** `ws://localhost:5000/ws/signals` (network overhead)  
**After:** `channel.Writer.WriteAsync(signal)` (<1μs)

---

## Task 6.3: Hot Path Optimization

Profile with BenchmarkDotNet, optimize:
- Signal detection latency
- Order placement pipeline
- Memory allocations

**Target:** Signal → Exchange order <5ms

---

[← Prev: Web UI](phase-5-webui.md) | [Back to Roadmap](README.md)
