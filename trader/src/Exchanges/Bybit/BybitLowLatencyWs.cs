using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TraderBot.Exchanges.Bybit
{
    /// <summary>
    /// Low-latency WebSocket client for Bybit HFT trading
    /// </summary>
    public class BybitLowLatencyWs : IDisposable
    {
        private readonly ClientWebSocket _ws;
        private readonly string _apiKey;
        private readonly string _apiSecret;
        private readonly ArrayPool<byte> _bufferPool;
        private bool _isAuthenticated;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        public BybitLowLatencyWs(string apiKey, string apiSecret)
        {
            _ws = new ClientWebSocket();
            _apiKey = apiKey;
            _apiSecret = apiSecret;
            _bufferPool = ArrayPool<byte>.Shared;
        }

        public async Task ConnectAsync()
        {
            var uri = new Uri("wss://stream.bybit.com/v5/trade");
            await _ws.ConnectAsync(uri, CancellationToken.None);
            Console.WriteLine("[Bybit LowLatency WS] Connected to wss://stream.bybit.com/v5/trade");

            // Start receive loop in background
            _ = Task.Run(ReceiveLoop);

            // Authenticate
            await AuthenticateAsync();
        }

        private async Task AuthenticateAsync()
        {
            var expires = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 10000;
            var signature = GenerateAuthSignature(expires);

            var authMessage = $@"{{""op"":""auth"",""args"":[""{_apiKey}"",""{expires}"",""{signature}""]}}";

            await SendMessageAsync(authMessage);

            // Wait for auth confirmation (simple wait, can be improved)
            await Task.Delay(500);
            _isAuthenticated = true;
            Console.WriteLine("[Bybit LowLatency WS] Authenticated");
        }

        private string GenerateAuthSignature(long expires)
        {
            var message = $"GET/realtime{expires}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_apiSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        public async Task<string?> PlaceMarketOrderAsync(string symbol, string side, decimal quoteQuantity)
        {
            if (!_isAuthenticated)
                throw new InvalidOperationException("Not authenticated");

            var reqId = Guid.NewGuid().ToString("N");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            // Format quantity without trailing zeros
            var qtyStr = quoteQuantity.ToString("0.########");

            // Build JSON with proper Bybit v5 format
            var orderMessage = $@"{{""reqId"":""{reqId}"",""header"":{{""X-BAPI-TIMESTAMP"":""{timestamp}"",""X-BAPI-RECV-WINDOW"":""5000""}},""op"":""order.create"",""args"":[{{""category"":""spot"",""symbol"":""{symbol}"",""side"":""{side}"",""orderType"":""Market"",""qty"":""{qtyStr}"",""marketUnit"":""quoteCoin""}}]}}";

            var t0 = DateTime.UtcNow;
            await SendMessageAsync(orderMessage);
            var t1 = DateTime.UtcNow;

            var sendLatency = (t1 - t0).TotalMilliseconds;
            Console.WriteLine($"[Bybit LowLatency WS] Message sent in {sendLatency:F2}ms");

            // Fire-and-forget - don't wait for response
            return reqId;
        }

        private async Task SendMessageAsync(string message)
        {
            await _sendLock.WaitAsync();
            try
            {
                var buffer = _bufferPool.Rent(4096);
                try
                {
                    var bytesWritten = Encoding.UTF8.GetBytes(message, 0, message.Length, buffer, 0);
                    var segment = new ArraySegment<byte>(buffer, 0, bytesWritten);

                    await _ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                finally
                {
                    _bufferPool.Return(buffer);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = _bufferPool.Rent(8192);
            try
            {
                while (_ws.State == WebSocketState.Open)
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine($"[Bybit LowLatency WS] Received: {message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bybit LowLatency WS] Receive error: {ex.Message}");
            }
            finally
            {
                _bufferPool.Return(buffer);
            }
        }

        public void Dispose()
        {
            _sendLock?.Dispose();
            _ws?.Dispose();
        }
    }
}
