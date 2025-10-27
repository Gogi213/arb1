using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TraderBot.Core;

namespace TraderBot.Exchanges.Bybit
{
    /// <summary>
    /// Low-latency WebSocket client for Bybit HFT trading
    /// </summary>
    public class BybitLowLatencyWs : IDisposable
    {
        private readonly ClientWebSocket _ws; // Trade WebSocket (authenticated)
        private readonly ClientWebSocket _wsPublic; // Public WebSocket (for orderbook)
        private readonly string _apiKey;
        private readonly string _apiSecret;
        private readonly ArrayPool<byte> _bufferPool;
        private bool _isAuthenticated;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _sendLockPublic = new SemaphoreSlim(1, 1);

        // Callbacks for subscriptions
        private Action<IOrderBook>? _orderBookCallback;
        private Action<IOrder>? _orderUpdateCallback;

        // Mapping reqId -> real OrderId from Bybit response
        private readonly Dictionary<string, TaskCompletionSource<string>> _orderIdMapping = new();
        private readonly SemaphoreSlim _mappingLock = new SemaphoreSlim(1, 1);

        public BybitLowLatencyWs(string apiKey, string apiSecret)
        {
            _ws = new ClientWebSocket();
            _wsPublic = new ClientWebSocket();
            _apiKey = apiKey;
            _apiSecret = apiSecret;
            _bufferPool = ArrayPool<byte>.Shared;
        }

        public async Task ConnectAsync()
        {
            // Connect trade WebSocket (authenticated)
            var uriTrade = new Uri("wss://stream.bybit.com/v5/trade");
            await _ws.ConnectAsync(uriTrade, CancellationToken.None);
            Console.WriteLine("[Bybit LowLatency WS] Connected to wss://stream.bybit.com/v5/trade");

            // Connect public WebSocket (for orderbook)
            var uriPublic = new Uri("wss://stream.bybit.com/v5/public/spot");
            await _wsPublic.ConnectAsync(uriPublic, CancellationToken.None);
            Console.WriteLine("[Bybit LowLatency WS] Connected to wss://stream.bybit.com/v5/public/spot");

            // Start receive loops in background
            _ = Task.Run(ReceiveLoop);
            _ = Task.Run(ReceiveLoopPublic);

            // Authenticate trade WebSocket
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

            // Create TaskCompletionSource for waiting real OrderId
            var tcs = new TaskCompletionSource<string>();
            await _mappingLock.WaitAsync();
            try
            {
                _orderIdMapping[reqId] = tcs;
            }
            finally
            {
                _mappingLock.Release();
            }

            // Format quantity without trailing zeros - use InvariantCulture for dot separator
            var qtyStr = quoteQuantity.ToString("0.########", CultureInfo.InvariantCulture);

            // Build JSON with proper Bybit v5 format
            var orderMessage = $@"{{""reqId"":""{reqId}"",""header"":{{""X-BAPI-TIMESTAMP"":""{timestamp}"",""X-BAPI-RECV-WINDOW"":""5000""}},""op"":""order.create"",""args"":[{{""category"":""spot"",""symbol"":""{symbol}"",""side"":""{side}"",""orderType"":""Market"",""qty"":""{qtyStr}"",""marketUnit"":""quoteCoin""}}]}}";

            var t0 = DateTime.UtcNow;
            await SendMessageAsync(orderMessage);
            var t1 = DateTime.UtcNow;

            var sendLatency = (t1 - t0).TotalMilliseconds;
            Console.WriteLine($"[Bybit LowLatency WS] Market order sent in {sendLatency:F2}ms");

            // Wait for real OrderId from WS response (timeout 5 seconds)
            var realOrderIdTask = tcs.Task;
            if (await Task.WhenAny(realOrderIdTask, Task.Delay(5000)) == realOrderIdTask)
            {
                return await realOrderIdTask;
            }

            Console.WriteLine($"[Bybit LowLatency WS] Timeout waiting for OrderId, returning reqId");
            return reqId; // Fallback to reqId if timeout
        }

        public async Task<string?> PlaceLimitOrderAsync(string symbol, string side, decimal quantity, decimal price)
        {
            if (!_isAuthenticated)
                throw new InvalidOperationException("Not authenticated");

            var reqId = Guid.NewGuid().ToString("N");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            // Create TaskCompletionSource for waiting real OrderId
            var tcs = new TaskCompletionSource<string>();
            await _mappingLock.WaitAsync();
            try
            {
                _orderIdMapping[reqId] = tcs;
            }
            finally
            {
                _mappingLock.Release();
            }

            var qtyStr = quantity.ToString("0.########", CultureInfo.InvariantCulture);
            var priceStr = price.ToString("0.########", CultureInfo.InvariantCulture);

            // Bybit requires timeInForce for limit orders (GTC = Good Till Cancel)
            var orderMessage = $@"{{""reqId"":""{reqId}"",""header"":{{""X-BAPI-TIMESTAMP"":""{timestamp}"",""X-BAPI-RECV-WINDOW"":""5000""}},""op"":""order.create"",""args"":[{{""category"":""spot"",""symbol"":""{symbol}"",""side"":""{side}"",""orderType"":""Limit"",""qty"":""{qtyStr}"",""price"":""{priceStr}"",""timeInForce"":""GTC""}}]}}";

            var t0 = DateTime.UtcNow;
            await SendMessageAsync(orderMessage);
            var t1 = DateTime.UtcNow;

            var sendLatency = (t1 - t0).TotalMilliseconds;
            Console.WriteLine($"[Bybit LowLatency WS] Limit order sent in {sendLatency:F2}ms");

            // Wait for real OrderId from WS response (timeout 5 seconds)
            var realOrderIdTask = tcs.Task;
            if (await Task.WhenAny(realOrderIdTask, Task.Delay(5000)) == realOrderIdTask)
            {
                return await realOrderIdTask;
            }

            Console.WriteLine($"[Bybit LowLatency WS] Timeout waiting for OrderId, returning reqId");
            return reqId; // Fallback to reqId if timeout
        }

        public async Task<bool> ModifyOrderAsync(string symbol, string orderId, decimal newPrice, decimal newQuantity)
        {
            if (!_isAuthenticated)
                throw new InvalidOperationException("Not authenticated");

            var reqId = Guid.NewGuid().ToString("N");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            var qtyStr = newQuantity.ToString("0.########", CultureInfo.InvariantCulture);
            var priceStr = newPrice.ToString("0.########", CultureInfo.InvariantCulture);

            var amendMessage = $@"{{""reqId"":""{reqId}"",""header"":{{""X-BAPI-TIMESTAMP"":""{timestamp}"",""X-BAPI-RECV-WINDOW"":""5000""}},""op"":""order.amend"",""args"":[{{""category"":""spot"",""symbol"":""{symbol}"",""orderId"":""{orderId}"",""qty"":""{qtyStr}"",""price"":""{priceStr}""}}]}}";

            var t0 = DateTime.UtcNow;
            await SendMessageAsync(amendMessage);
            var t1 = DateTime.UtcNow;

            var sendLatency = (t1 - t0).TotalMilliseconds;
            Console.WriteLine($"[Bybit LowLatency WS] Order amend sent in {sendLatency:F2}ms");

            return true;
        }

        public async Task SubscribeToOrderUpdatesAsync(Action<IOrder> onOrderUpdate)
        {
            _orderUpdateCallback = onOrderUpdate;

            var subscribeMessage = @"{""op"":""subscribe"",""args"":[""order""]}";
            await SendMessageAsync(subscribeMessage);
            Console.WriteLine("[Bybit LowLatency WS] Subscribed to order updates");
        }

        public async Task SubscribeToOrderBookAsync(string symbol, Action<IOrderBook> onOrderBookUpdate)
        {
            _orderBookCallback = onOrderBookUpdate;

            // Subscribe to 50-level orderbook on public WebSocket
            var subscribeMessage = $@"{{""op"":""subscribe"",""args"":[""orderbook.50.{symbol}""]}}";
            await SendMessagePublicAsync(subscribeMessage);
            Console.WriteLine($"[Bybit LowLatency WS] Subscribed to order book for {symbol}");
        }

        public async Task CancelAllOrdersAsync(string symbol)
        {
            // TODO: Implement in future sprint
            await Task.CompletedTask;
            Console.WriteLine("[Bybit LowLatency WS] CancelAllOrders not yet implemented");
        }

        public async Task CancelOrderAsync(string symbol, string orderId)
        {
            // TODO: Implement in future sprint
            await Task.CompletedTask;
            Console.WriteLine("[Bybit LowLatency WS] CancelOrder not yet implemented");
        }

        public async Task UnsubscribeAllAsync()
        {
            _orderBookCallback = null;
            _orderUpdateCallback = null;
            await Task.CompletedTask;
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

        private async Task SendMessagePublicAsync(string message)
        {
            await _sendLockPublic.WaitAsync();
            try
            {
                var buffer = _bufferPool.Rent(4096);
                try
                {
                    var bytesWritten = Encoding.UTF8.GetBytes(message, 0, message.Length, buffer, 0);
                    var segment = new ArraySegment<byte>(buffer, 0, bytesWritten);

                    await _wsPublic.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                finally
                {
                    _bufferPool.Return(buffer);
                }
            }
            finally
            {
                _sendLockPublic.Release();
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = _bufferPool.Rent(16384); // Increased buffer for order book data
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

                    // Parse and route message
                    try
                    {
                        using var doc = JsonDocument.Parse(message);
                        var root = doc.RootElement;

                        // Check for subscription confirmation
                        if (root.TryGetProperty("op", out var op))
                        {
                            var opValue = op.GetString();
                            if (opValue == "subscribe")
                            {
                                Console.WriteLine("[Bybit LowLatency WS] Subscription confirmed");
                                continue;
                            }
                        }

                        // Check for order.create/order.amend response
                        if (root.TryGetProperty("reqId", out var reqIdProp))
                        {
                            var reqId = reqIdProp.GetString();
                            if (root.TryGetProperty("retMsg", out var retMsg))
                            {
                                Console.WriteLine($"[Bybit LowLatency WS] Operation response: {retMsg.GetString()}");
                            }

                            // Extract real OrderId from successful order.create response
                            if (root.TryGetProperty("op", out var opType) && opType.GetString() == "order.create")
                            {
                                if (root.TryGetProperty("retCode", out var retCode) && retCode.GetInt32() == 0)
                                {
                                    // Success - extract orderId from data
                                    if (root.TryGetProperty("data", out var data) &&
                                        data.TryGetProperty("orderId", out var orderIdProp))
                                    {
                                        var realOrderId = orderIdProp.GetString();
                                        if (reqId != null && realOrderId != null)
                                        {
                                            await CompleteOrderIdMapping(reqId, realOrderId);
                                            Console.WriteLine($"[Bybit LowLatency WS] Order created: reqId={reqId}, OrderId={realOrderId}");
                                        }
                                    }
                                }
                                else
                                {
                                    // Failure - complete with error
                                    if (reqId != null)
                                    {
                                        await FailOrderIdMapping(reqId);
                                    }
                                }
                            }
                            continue;
                        }

                        // Check for topic-based messages (order updates, orderbook)
                        if (root.TryGetProperty("topic", out var topic))
                        {
                            var topicValue = topic.GetString();

                            if (topicValue == "order")
                            {
                                // Order update
                                HandleOrderUpdate(root);
                            }
                            else if (topicValue != null && topicValue.StartsWith("orderbook"))
                            {
                                // Order book update
                                HandleOrderBookUpdate(root);
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"[Bybit LowLatency WS] JSON parse error: {ex.Message}");
                    }
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

        private async Task ReceiveLoopPublic()
        {
            var buffer = _bufferPool.Rent(16384);
            try
            {
                while (_wsPublic.State == WebSocketState.Open)
                {
                    var result = await _wsPublic.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _wsPublic.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine($"[Bybit Public WS] Received: {message}");

                    try
                    {
                        using var doc = JsonDocument.Parse(message);
                        var root = doc.RootElement;

                        // Handle subscription confirmation
                        if (root.TryGetProperty("op", out var op) && op.GetString() == "subscribe")
                        {
                            if (root.TryGetProperty("success", out var success) && success.GetBoolean())
                            {
                                Console.WriteLine("[Bybit Public WS] Subscription confirmed");
                            }
                            continue;
                        }

                        // Handle orderbook messages (snapshot or delta)
                        if (root.TryGetProperty("topic", out var topic))
                        {
                            var topicValue = topic.GetString();
                            if (topicValue != null && topicValue.StartsWith("orderbook"))
                            {
                                HandleOrderBookUpdate(root);
                            }
                        }

                        // Handle ping-pong
                        if (root.TryGetProperty("op", out var opPing) && opPing.GetString() == "ping")
                        {
                            var pong = @"{""op"":""pong""}";
                            await SendMessagePublicAsync(pong);
                        }
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"[Bybit Public WS] JSON parse error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bybit Public WS] Receive error: {ex.Message}");
            }
            finally
            {
                _bufferPool.Return(buffer);
            }
        }

        private void HandleOrderUpdate(JsonElement root)
        {
            if (_orderUpdateCallback == null) return;

            try
            {
                if (root.TryGetProperty("data", out var dataArray))
                {
                    foreach (var orderData in dataArray.EnumerateArray())
                    {
                        var order = new BybitOrderUpdate(orderData);
                        _orderUpdateCallback(order);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bybit LowLatency WS] Error handling order update: {ex.Message}");
            }
        }

        private void HandleOrderBookUpdate(JsonElement root)
        {
            if (_orderBookCallback == null) return;

            try
            {
                if (root.TryGetProperty("data", out var data))
                {
                    var orderBook = new BybitOrderBookUpdate(data);
                    _orderBookCallback(orderBook);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bybit LowLatency WS] Error handling order book update: {ex.Message}");
            }
        }

        private async Task CompleteOrderIdMapping(string reqId, string realOrderId)
        {
            await _mappingLock.WaitAsync();
            try
            {
                if (_orderIdMapping.TryGetValue(reqId, out var tcs))
                {
                    tcs.TrySetResult(realOrderId);
                    _orderIdMapping.Remove(reqId);
                }
            }
            finally
            {
                _mappingLock.Release();
            }
        }

        private async Task FailOrderIdMapping(string reqId)
        {
            await _mappingLock.WaitAsync();
            try
            {
                if (_orderIdMapping.TryGetValue(reqId, out var tcs))
                {
                    tcs.TrySetResult(reqId); // Return reqId as fallback
                    _orderIdMapping.Remove(reqId);
                }
            }
            finally
            {
                _mappingLock.Release();
            }
        }

        /// <summary>
        /// Calculate target price based on order book depth (like Gate.io implementation)
        /// For buying: calculate price with dollarDepth from best bid
        /// </summary>
        public static decimal CalculateTargetPriceForBuy(IOrderBook orderBook, decimal dollarDepth, decimal tickSize)
        {
            var bestBid = orderBook.Bids.First().Price;
            decimal cumulativeVolume = 0;
            decimal targetPrice = bestBid;

            foreach (var bid in orderBook.Bids)
            {
                cumulativeVolume += bid.Price * bid.Quantity;
                if (cumulativeVolume >= dollarDepth)
                {
                    targetPrice = bid.Price;
                    break;
                }
            }

            return Math.Round(targetPrice / tickSize) * tickSize;
        }

        /// <summary>
        /// Calculate target price based on order book depth (like Gate.io implementation)
        /// For selling: calculate price with dollarDepth from best ask
        /// </summary>
        public static decimal CalculateTargetPriceForSell(IOrderBook orderBook, decimal dollarDepth, decimal tickSize)
        {
            var bestAsk = orderBook.Asks.First().Price;
            decimal cumulativeVolume = 0;
            decimal targetPrice = bestAsk;

            foreach (var ask in orderBook.Asks)
            {
                cumulativeVolume += ask.Price * ask.Quantity;
                if (cumulativeVolume >= dollarDepth)
                {
                    targetPrice = ask.Price;
                    break;
                }
            }

            return Math.Round(targetPrice / tickSize) * tickSize;
        }

        public void Dispose()
        {
            _sendLock?.Dispose();
            _sendLockPublic?.Dispose();
            _mappingLock?.Dispose();
            _ws?.Dispose();
            _wsPublic?.Dispose();
        }
    }

    // Lightweight models for parsing WebSocket messages
    internal class BybitOrderUpdate : IOrder
    {
        public string Symbol { get; }
        public long OrderId { get; }
        public decimal Price { get; }
        public decimal Quantity { get; }
        public string Status { get; }
        public string? FinishType { get; }
        public DateTime? CreateTime { get; }
        public DateTime? UpdateTime { get; }

        public BybitOrderUpdate(JsonElement data)
        {
            Symbol = data.TryGetProperty("symbol", out var sym) ? sym.GetString() ?? "" : "";
            OrderId = data.TryGetProperty("orderId", out var oid) ? long.Parse(oid.GetString() ?? "0") : 0;
            Price = data.TryGetProperty("price", out var pr) && decimal.TryParse(pr.GetString(), out var price) ? price : 0;
            Quantity = data.TryGetProperty("qty", out var q) && decimal.TryParse(q.GetString(), out var qty) ? qty : 0;
            Status = data.TryGetProperty("orderStatus", out var st) ? st.GetString() ?? "" : "";
            FinishType = Status == "Filled" ? "Filled" : Status == "Cancelled" ? "Cancelled" : null;

            if (data.TryGetProperty("createdTime", out var ct) && long.TryParse(ct.GetString(), out var createMs))
            {
                CreateTime = DateTimeOffset.FromUnixTimeMilliseconds(createMs).UtcDateTime;
            }

            if (data.TryGetProperty("updatedTime", out var ut) && long.TryParse(ut.GetString(), out var updateMs))
            {
                UpdateTime = DateTimeOffset.FromUnixTimeMilliseconds(updateMs).UtcDateTime;
            }
        }
    }

    internal class BybitOrderBookUpdate : IOrderBook
    {
        public string Symbol { get; }
        public IEnumerable<IOrderBookEntry> Bids => _bids;
        public IEnumerable<IOrderBookEntry> Asks => _asks;

        private readonly List<IOrderBookEntry> _bids = new();
        private readonly List<IOrderBookEntry> _asks = new();

        public BybitOrderBookUpdate(JsonElement data)
        {
            Symbol = data.TryGetProperty("s", out var sym) ? sym.GetString() ?? "" : "";

            if (data.TryGetProperty("b", out var bids))
            {
                foreach (var bid in bids.EnumerateArray())
                {
                    var priceStr = bid[0].GetString();
                    var qtyStr = bid[1].GetString();
                    if (priceStr != null && qtyStr != null &&
                        decimal.TryParse(priceStr, out var price) &&
                        decimal.TryParse(qtyStr, out var qty))
                    {
                        _bids.Add(new OrderBookEntry { Price = price, Quantity = qty });
                    }
                }
            }

            if (data.TryGetProperty("a", out var asks))
            {
                foreach (var ask in asks.EnumerateArray())
                {
                    var priceStr = ask[0].GetString();
                    var qtyStr = ask[1].GetString();
                    if (priceStr != null && qtyStr != null &&
                        decimal.TryParse(priceStr, out var price) &&
                        decimal.TryParse(qtyStr, out var qty))
                    {
                        _asks.Add(new OrderBookEntry { Price = price, Quantity = qty });
                    }
                }
            }
        }
    }

    internal class OrderBookEntry : IOrderBookEntry
    {
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
    }
}
