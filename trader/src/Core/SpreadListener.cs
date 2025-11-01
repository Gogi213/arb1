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

        private decimal? _lastGateBid;
        private decimal? _lastBybitBid;
        private string? _symbol;
        private const decimal SpreadThreshold = 0.25m;

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
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cancellationToken);
                    FileLogger.LogOther("[SpreadListener] Connection closed by server.");
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
            }
            finally
            {
                _lock.Release();
            }
        }

        private void CalculateAndLogSpreads()
        {
            if (!_lastGateBid.HasValue || !_lastBybitBid.HasValue || _lastGateBid.Value == 0 || _lastBybitBid.Value == 0)
                return;

            // 1 - гейт -> байбит
            var gateToBybitSpread = (_lastBybitBid.Value / _lastGateBid.Value - 1) * 100;
            if (gateToBybitSpread >= SpreadThreshold)
            {
                var direction = "GateIo_To_Bybit";
                FileLogger.LogSpread($"{_symbol}/{direction}: {gateToBybitSpread:F2}%");
                OnProfitableSpreadDetected?.Invoke(direction, gateToBybitSpread);
            }

            // 2 - байбит -> гейт
            var bybitToGateSpread = (_lastGateBid.Value / _lastBybitBid.Value - 1) * 100;
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