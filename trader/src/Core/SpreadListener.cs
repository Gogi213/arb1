using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TraderBot.Core
{
    public class SpreadListener
    {
        private readonly ClientWebSocket _ws;
        private readonly Uri _serverUri;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        // PROPOSAL-001: Store price AND timestamp to detect stale data
        private (decimal Price, DateTime Timestamp)? _lastGateBid;
        private (decimal Price, DateTime Timestamp)? _lastBybitBid;
        
        private string? _symbol;
        private const decimal SpreadThreshold = 0.25m;
        
        // PROPOSAL-001: Maximum allowed age for price data (7 seconds)
        // Tolerant enough for illiquid coins, but protects against stale data
        private static readonly TimeSpan MaxDataAge = TimeSpan.FromSeconds(7);

        public event Action<string, decimal>? OnProfitableSpreadDetected;

        public SpreadListener(string serverUrl)
        {
            _ws = new ClientWebSocket();
            _serverUri = new Uri(serverUrl);
            _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                FileLogger.LogOther($"[SpreadListener] Connecting to {_serverUri}...");
                await _ws.ConnectAsync(_serverUri, cancellationToken);
                FileLogger.LogOther($"[SpreadListener] Connection established.");

                await ReceiveLoop(cancellationToken);
            }
            catch (Exception ex)
            {
                FileLogger.LogOther($"[SpreadListener-ERROR] Connection failed: {ex.Message}");
            }
        }

        private async Task ReceiveLoop(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            while (_ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    // PROPOSAL-001: Invalidate all data on disconnect
                    _lastGateBid = null;
                    _lastBybitBid = null;
                    
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cancellationToken);
                    FileLogger.LogOther("[SpreadListener] Connection closed by server. All price data invalidated.");
                }
                else
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    try
                    {
                        var genericMessage = JsonSerializer.Deserialize<WebSocketMessage>(message, _jsonOptions);

                        if (string.Equals(genericMessage?.MessageType, "Spread", StringComparison.OrdinalIgnoreCase))
                        {
                            var spreadData = JsonSerializer.Deserialize<SpreadData>(genericMessage.Payload.GetRawText(), _jsonOptions);
                            if (spreadData != null)
                            {
                                await ProcessSpreadDataAsync(spreadData);
                            }
                        }
                        else
                        {
                            FileLogger.LogOther($"[SpreadListener] Received message of type '{genericMessage?.MessageType}'. Ignoring.");
                        }
                    }
                    catch (JsonException ex)
                    {
                        FileLogger.LogOther($"[SpreadListener-ERROR] Failed to deserialize message: {ex.Message}. Message: {message}");
                    }
                }
            }
        }

        private async Task ProcessSpreadDataAsync(SpreadData data)
        {
            await _lock.WaitAsync();
            try
            {
                _symbol = data.Symbol;

                // PROPOSAL-001: Store timestamp along with price
                // Use data's timestamp if available, otherwise use current time
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
            }
            finally
            {
                _lock.Release();
            }
        }

        private void CalculateAndLogSpreads()
        {
            // PROPOSAL-001: Check WebSocket connection state FIRST
            if (_ws.State != WebSocketState.Open)
            {
                FileLogger.LogOther($"[SpreadListener-ERROR] WebSocket disconnected (State: {_ws.State}). Invalidating all data.");
                _lastGateBid = null;
                _lastBybitBid = null;
                return;
            }

            if (!_lastGateBid.HasValue || !_lastBybitBid.HasValue)
                return;

            // PROPOSAL-001: Extract price and timestamp from tuples
            var (gatePrice, gateTime) = _lastGateBid.Value;
            var (bybitPrice, bybitTime) = _lastBybitBid.Value;

            if (gatePrice == 0 || bybitPrice == 0)
                return;

            // PROPOSAL-001: CRITICAL - Validate data freshness
            // If either exchange's data is stale, do NOT calculate spread
            // This prevents trading on phantom arbitrage opportunities
            var now = DateTime.UtcNow;
            var gateAge = now - gateTime;
            var bybitAge = now - bybitTime;

            if (gateAge > MaxDataAge || bybitAge > MaxDataAge)
            {
                // Optional: Log warning if data is stale (helps debugging connection issues)
                if (gateAge > MaxDataAge)
                {
                    FileLogger.LogOther($"[SpreadListener-WARN] GateIo data is stale ({gateAge.TotalSeconds:F1}s old). Skipping spread calculation.");
                }
                if (bybitAge > MaxDataAge)
                {
                    FileLogger.LogOther($"[SpreadListener-WARN] Bybit data is stale ({bybitAge.TotalSeconds:F1}s old). Skipping spread calculation.");
                }
                return;
            }

            // 1 - гейт -> байбит
            var gateToBybitSpread = (bybitPrice / gatePrice - 1) * 100;
            if (gateToBybitSpread >= SpreadThreshold)
            {
                var direction = "GateIo_To_Bybit";
                FileLogger.LogSpread($"{_symbol}/{direction}: {gateToBybitSpread:F2}%");
                OnProfitableSpreadDetected?.Invoke(direction, gateToBybitSpread);
            }

            // 2 - байбит -> гейт
            var bybitToGateSpread = (gatePrice / bybitPrice - 1) * 100;
            if (bybitToGateSpread >= SpreadThreshold)
            {
                var direction = "Bybit_To_GateIo";
                FileLogger.LogSpread($"{_symbol}/{direction}: {bybitToGateSpread:F2}%");
                OnProfitableSpreadDetected?.Invoke(direction, bybitToGateSpread);
            }
        }

        // Helper class for deserializing the message wrapper
        private class WebSocketMessage
        {
            public string MessageType { get; set; }
            public JsonElement Payload { get; set; }
        }
    }
}