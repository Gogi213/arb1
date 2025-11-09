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
        var subscriptions = new Dictionary<string, EventHandler<Application.Services.WindowDataUpdatedEventArgs>>();

        try
        {
            var opportunities = _opportunityFilter.GetFilteredOpportunities();
            _logger.LogInformation($"Starting event-driven streaming for {opportunities.Count} opportunities");

            // Subscribe to window updates for each opportunity
            foreach (var opp in opportunities)
            {
                var key = $"{opp.Symbol}_{opp.Exchange1}_{opp.Exchange2}";

                EventHandler<Application.Services.WindowDataUpdatedEventArgs> handler = async (sender, e) =>
                {
                    // Only process if this event is relevant to our opportunity
                    if ((e.Exchange == opp.Exchange1 || e.Exchange == opp.Exchange2) && e.Symbol == opp.Symbol)
                    {
                        try
                        {
                            var chartData = _rollingWindow.JoinRealtimeWindows(
                                opp.Symbol, opp.Exchange1, opp.Exchange2);

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

                                        _logger.LogDebug($"Event-driven update sent for {opp.Symbol} ({opp.Exchange1}/{opp.Exchange2})");
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
                    }
                };

                subscriptions[key] = handler;
                _rollingWindow.WindowDataUpdated += handler;
                _logger.LogDebug($"Subscribed to {opp.Symbol} ({opp.Exchange1}/{opp.Exchange2})");
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
            foreach (var subscription in subscriptions.Values)
            {
                _rollingWindow.WindowDataUpdated -= subscription;
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
