using Microsoft.AspNetCore.Mvc;
using SpreadAggregator.Application.Services;
using SpreadAggregator.Infrastructure.Services.Charts;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace SpreadAggregator.Presentation.Controllers;

/// <summary>
/// Real-time chart data WebSocket controller
/// Replaces Python charts/server.py /ws/realtime_charts endpoint
/// </summary>
[ApiController]
[Route("ws")]
public class RealTimeController : ControllerBase
{
    private readonly ILogger<RealTimeController> _logger;
    private readonly RollingWindowService _rollingWindow;
    private readonly OpportunityFilterService _opportunityFilter;

    public RealTimeController(
        ILogger<RealTimeController> logger,
        RollingWindowService rollingWindow,
        OpportunityFilterService opportunityFilter)
    {
        _logger = logger;
        _rollingWindow = rollingWindow;
        _opportunityFilter = opportunityFilter;
    }

    /// <summary>
    /// WebSocket endpoint for streaming real-time chart data
    /// Event-driven architecture: subscribes to RollingWindowService.WindowDataUpdated
    /// Each opportunity sends update when new data arrives
    /// True asynchronous updates - no polling, no artificial delays
    /// </summary>
    [HttpGet("realtime_charts")]
    public async Task HandleWebSocket()
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        _logger.LogInformation("WebSocket connection established");

        await StreamRealtimeData(webSocket);
    }

    private async Task StreamRealtimeData(WebSocket webSocket)
    {
        var sendLock = new SemaphoreSlim(1, 1);
        var cts = new CancellationTokenSource();
        // Store full details needed for unsubscription: (Symbol, Ex1, Ex2, Handler)
        var subscriptions = new List<(string Symbol, string Ex1, string Ex2, EventHandler<Application.Services.WindowDataUpdatedEventArgs> Handler)>();

        try
        {
            var opportunities = _opportunityFilter.GetFilteredOpportunities().Take(20).ToList();
            _logger.LogInformation($"Starting event-driven streaming for {opportunities.Count} opportunities");

            // Log first 10 opportunities for debugging
            for (int i = 0; i < Math.Min(10, opportunities.Count); i++)
            {
                var opp = opportunities[i];
                _logger.LogInformation($"Opportunity {i+1}: {opp.Symbol} ({opp.Exchange1}/{opp.Exchange2}) cycles={opp.OpportunityCycles}");
            }
            if (opportunities.Count > 10)
            {
                _logger.LogInformation($"... and {opportunities.Count - 10} more opportunities");
            }

            // Subscribe to window updates for each opportunity
            foreach (var opp in opportunities)
            {
                // opp.Symbol is already normalized by OpportunityFilterService
                var key = $"{opp.Symbol}_{opp.Exchange1}_{opp.Exchange2}";
                
                // THROTTLING: Track last update time to prevent CPU saturation
                // Only update chart max 4 times per second (250ms)
                var lastUpdateTime = DateTime.MinValue;
                var throttleInterval = TimeSpan.FromMilliseconds(250);

                EventHandler<Application.Services.WindowDataUpdatedEventArgs> handler = async (sender, e) =>
                {
                    // TARGETED EVENTS: No filter needed! Event only fires if relevant to this window
                    var now = DateTime.UtcNow;
                    if (now - lastUpdateTime < throttleInterval)
                        return;

                    lastUpdateTime = now;

                        try
                        {
                            // This calculation is heavy (Sort + Quantiles), so throttling is CRITICAL
                            // PERFORMANCE FIX: Offload to background thread to avoid blocking the event loop
                            var chartData = await Task.Run(() => _rollingWindow.JoinRealtimeWindows(
                                opp.Symbol, opp.Exchange1, opp.Exchange2));

                            if (chartData != null)
                            {
                                var chartUpdate = new
                                {
                                    symbol = chartData.Symbol,
                                    exchange1 = chartData.Exchange1,
                                    exchange2 = chartData.Exchange2,
                                    timestamps = chartData.Timestamps,
                                    spreads = chartData.Spreads,
                                    upperBand = chartData.UpperBand,
                                    lowerBand = chartData.LowerBand
                                };

                                var json = JsonSerializer.Serialize(chartUpdate, new JsonSerializerOptions
                                {
                                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                                });
                                var bytes = Encoding.UTF8.GetBytes(json);

                                // Thread-safe send
                                await sendLock.WaitAsync();
                                try
                                {
                                    if (webSocket.State == WebSocketState.Open)
                                    {
                                        await webSocket.SendAsync(
                                            new ArraySegment<byte>(bytes),
                                            WebSocketMessageType.Text,
                                            endOfMessage: true,
                                            CancellationToken.None);

                                        // _logger.LogDebug($"Event-driven update sent for {opp.Symbol} ({opp.Exchange1}/{opp.Exchange2})");
                                    }
                                }
                                finally
                                {
                                    sendLock.Release();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                $"Error sending event-driven update for {opp.Symbol} ({opp.Exchange1}/{opp.Exchange2})");
                        }
                };

                subscriptions.Add((opp.Symbol, opp.Exchange1, opp.Exchange2, handler));
                // TARGETED EVENTS: Subscribe to specific window instead of global broadcast
                _rollingWindow.SubscribeToWindow(opp.Symbol, opp.Exchange1, opp.Exchange2, handler);
                _logger.LogInformation($"[RealTime] Subscribed to window: {opp.Symbol} ({opp.Exchange1}/{opp.Exchange2})");

                // Test if RollingWindow has data for this pair
                var testData = _rollingWindow.JoinRealtimeWindows(opp.Symbol, opp.Exchange1, opp.Exchange2);
                if (testData != null)
                {
                    _logger.LogInformation($"[RealTime] RollingWindow has data for {opp.Symbol} ({opp.Exchange1}/{opp.Exchange2}) - {testData.Timestamps.Count} points");
                }
                else
                {
                    _logger.LogWarning($"[RealTime] RollingWindow has NO data for {opp.Symbol} ({opp.Exchange1}/{opp.Exchange2})");
                }
            }

            // Keep connection alive until WebSocket closes
            while (webSocket.State == WebSocketState.Open && !cts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000, cts.Token);
            }
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "WebSocket connection error");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in real-time streaming");
        }
        finally
        {
            // Unsubscribe from all events
            // Unsubscribe from all events
            foreach (var sub in subscriptions)
            {
                _rollingWindow.UnsubscribeFromWindow(sub.Symbol, sub.Ex1, sub.Ex2, sub.Handler);
            }
            _logger.LogInformation($"Unsubscribed from {subscriptions.Count} opportunities");

            cts.Cancel();
            cts.Dispose();
            sendLock.Dispose();

            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Connection closed",
                    CancellationToken.None);
            }

            _logger.LogInformation("WebSocket connection closed");
        }
    }
}
