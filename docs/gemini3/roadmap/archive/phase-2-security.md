# Phase 2: Security & Observability

**Status:** ⚪ Not Started  
**Goal:** Add authentication and structured logging before production.

---

## Task 2.1: API Key Authentication

**Target:** `collections/src/SpreadAggregator.Presentation/Middleware/ApiKeyAuthMiddleware.cs` (New)

```csharp
public class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _config;

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip auth for health/metrics
        if (context.Request.Path.StartsWithSegments("/api/health") || 
            context.Request.Path.StartsWithSegments("/api/metrics"))
        {
            await _next(context);
            return;
        }

        // Require API key for mutations
        if (context.Request.Method != "GET")
        {
            if (!context.Request.Headers.TryGetValue("X-API-Key", out var key))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("API Key required");
                return;
            }

            if (key != _config["ApiKey"])
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync("Invalid API Key");
                return;
            }
        }

        await _next(context);
    }
}
```

**Registration:** `app.UseMiddleware<ApiKeyAuthMiddleware>();`

---

## Task 2.2: Structured Logging (Serilog)

Replace all `Console.WriteLine` with `ILogger`.

**Configuration:**
```csharp
builder.Host.UseSeri log((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/app-.txt", rollingInterval: RollingInterval.Day));
```

---

## Task 2.3: Metrics Endpoint (Prometheus)

**Target:** `GET /api/metrics`

```csharp
private static readonly Counter SignalsDetected = 
    Metrics.CreateCounter("signals_detected_total", "Total signals", new[] { "type" });

// In SignalDetectionService
SignalsDetected.WithLabels("ZeroCrossing").Inc();
```

---

[← Prev: Brain](phase-1-brain.md) | [Back to Roadmap](README.md) | [Next: Sight →](phase-3-sight.md)
