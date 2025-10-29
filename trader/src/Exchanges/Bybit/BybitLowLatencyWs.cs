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
        private readonly ClientWebSocket _privateWs; // For subscriptions (order, position)
        private readonly ClientWebSocket _tradeWs;   // For trade operations (create, amend)
        private readonly ClientWebSocket _publicWs;  // For public data (orderbook)
        private readonly string _apiKey;
        private readonly string _apiSecret;
        private readonly ArrayPool<byte> _bufferPool;
        private bool _isPrivateAuthenticated;
        private bool _isTradeAuthenticated;
        private readonly SemaphoreSlim _privateSendLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _tradeSendLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _publicSendLock = new SemaphoreSlim(1, 1);

        // Local order book state
        private readonly SortedDictionary<decimal, decimal> _bids = new(Comparer<decimal>.Create((a, b) => b.CompareTo(a))); // Bids are descending
        private readonly SortedDictionary<decimal, decimal> _asks = new(); // Asks are ascending
        private readonly object _bookLock = new object();

        // Callbacks for subscriptions
        private Action<IOrderBook>? _orderBookCallback;
        private Action<IOrder>? _orderUpdateCallback;

        // Mapping reqId -> real OrderId from Bybit response
        private readonly Dictionary<string, TaskCompletionSource<string>> _orderIdMapping = new();
        private readonly SemaphoreSlim _mappingLock = new SemaphoreSlim(1, 1);

        public BybitLowLatencyWs(string apiKey, string apiSecret)
        {
            _privateWs = new ClientWebSocket();
            _tradeWs = new ClientWebSocket();
            _publicWs = new ClientWebSocket();
            _apiKey = apiKey;
            _apiSecret = apiSecret;
            _bufferPool = ArrayPool<byte>.Shared;
        }

        public async Task ConnectAsync()
        {
            // Connect to /v5/private for subscriptions
            var privateUri = new Uri("wss://stream.bybit.com/v5/private");
            FileLogger.LogWebsocket($"[WS-PRIVATE] Connecting to {privateUri}...");
            await _privateWs.ConnectAsync(privateUri, CancellationToken.None);
            FileLogger.LogWebsocket($"[WS-PRIVATE] Connection successful. State: {_privateWs.State}");

            // Connect to /v5/trade for trading
            var tradeUri = new Uri("wss://stream.bybit.com/v5/trade");
            FileLogger.LogWebsocket($"[WS-TRADE] Connecting to {tradeUri}...");
            await _tradeWs.ConnectAsync(tradeUri, CancellationToken.None);
            FileLogger.LogWebsocket($"[WS-TRADE] Connection successful. State: {_tradeWs.State}");

            // Connect to /v5/public/spot for order book
            var publicUri = new Uri("wss://stream.bybit.com/v5/public/spot");
            FileLogger.LogWebsocket($"[WS-PUBLIC] Connecting to {publicUri}...");
            await _publicWs.ConnectAsync(publicUri, CancellationToken.None);
            FileLogger.LogWebsocket($"[WS-PUBLIC] Connection successful. State: {_publicWs.State}");

            // Start receive loops
            _ = Task.Run(() => ReceiveLoop(_privateWs, "PRIVATE"));
            _ = Task.Run(() => ReceiveLoop(_tradeWs, "TRADE"));
            _ = Task.Run(() => ReceiveLoop(_publicWs, "PUBLIC"));

            // Authenticate both private and trade sockets
            await AuthenticateAsync(_privateWs, "PRIVATE");
            await AuthenticateAsync(_tradeWs, "TRADE");
        }

        public Task<(decimal tickSize, decimal basePrecision)> GetSymbolFiltersAsync(string symbol)
        {
            // Hardcoded for now - will be fetched via REST/WS later
            decimal tickSize = 0.00001m;
            decimal basePrecision = 0; // H/USDT requires integer quantity

            FileLogger.LogOther($"[Bybit] Using hardcoded filters: tickSize={tickSize}, basePrecision={basePrecision}");
            return Task.FromResult((tickSize, basePrecision));
        }

        private async Task AuthenticateAsync(ClientWebSocket ws, string name)
        {
            FileLogger.LogWebsocket($"[WS-{name}] Attempting authentication...");
            var expires = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 10000;
            var signature = GenerateAuthSignature(expires);

            var authMessage = $@"{{""op"":""auth"",""args"":[""{_apiKey}"",""{expires}"",""{signature}""]}}";

            if (name == "PRIVATE")
                await SendMessageAsync(ws, _privateSendLock, authMessage, "PRIVATE");
            else if (name == "TRADE")
                await SendMessageAsync(ws, _tradeSendLock, authMessage, "TRADE");
            
            FileLogger.LogWebsocket($"[WS-{name}] Authentication request sent.");
            
            // In a real scenario, we'd wait for the auth response in ReceiveLoop
            await Task.Delay(1000);
        }

        private string GenerateAuthSignature(long expires)
        {
            var message = $"GET/realtime{expires}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_apiSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }
    
        private async Task PingAsync(ClientWebSocket ws, SemaphoreSlim sendLock, string name)
        {
            var pingMessage = $@"{{""op"":""ping"",""req_id"":""{Guid.NewGuid():N}""}}";
            await SendMessageAsync(ws, sendLock, pingMessage, name);
        }
    
        public async Task<string?> PlaceMarketOrderAsync(string symbol, string side, decimal quantity)
        {
            if (!_isTradeAuthenticated)
                throw new InvalidOperationException("Trade WebSocket is not authenticated");

            var reqId = Guid.NewGuid().ToString("N");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            // Create TaskCompletionSource for waiting real OrderId
            var tcs = new TaskCompletionSource<string>();
            await _mappingLock.WaitAsync();
            try
            {
                _orderIdMapping[reqId] = tcs;
                FileLogger.LogWebsocket($"[WS-PRIVATE] OrderId mapping created for reqId: {reqId}");
            }
            finally
            {
                _mappingLock.Release();
            }

            // Format quantity without trailing zeros - use InvariantCulture for dot separator
            var qtyStr = quantity.ToString("0.########", CultureInfo.InvariantCulture);

            // Build JSON with proper Bybit v5 format
            var orderMessage = $@"{{""reqId"":""{reqId}"",""header"":{{""X-BAPI-TIMESTAMP"":""{timestamp}"",""X-BAPI-RECV-WINDOW"":""5000""}},""op"":""order.create"",""args"":[{{""category"":""spot"",""symbol"":""{symbol}"",""side"":""{side}"",""orderType"":""Market"",""qty"":""{qtyStr}""}}]}}";

            var t0 = DateTime.UtcNow;
            await SendMessageAsync(_tradeWs, _tradeSendLock, orderMessage, "TRADE");
            var t1 = DateTime.UtcNow;

            var sendLatency = (t1 - t0).TotalMilliseconds;
            FileLogger.LogWebsocket($"[WS-PRIVATE] Market order sent in {sendLatency:F2}ms. ReqId: {reqId}");

            // Wait for real OrderId from WS response (timeout 5 seconds)
            var realOrderIdTask = tcs.Task;
            if (await Task.WhenAny(realOrderIdTask, Task.Delay(5000)) == realOrderIdTask)
            {
                var realId = await realOrderIdTask;
                FileLogger.LogWebsocket($"[WS-PRIVATE] Received real OrderId '{realId}' for reqId '{reqId}'.");
                return realId;
            }

            FileLogger.LogWebsocket($"[WS-PRIVATE-ERROR] Timeout waiting for OrderId for reqId '{reqId}'. Returning reqId as fallback.");
            await _mappingLock.WaitAsync();
            try
            {
                _orderIdMapping.Remove(reqId);
            }
            finally
            {
                _mappingLock.Release();
            }
            return reqId; // Fallback to reqId if timeout
        }

        public async Task<string?> PlaceLimitOrderAsync(string symbol, string side, decimal quantity, decimal price)
        {
            if (!_isTradeAuthenticated)
                throw new InvalidOperationException("Trade WebSocket is not authenticated");

            var reqId = Guid.NewGuid().ToString("N");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            // Create TaskCompletionSource for waiting real OrderId
            var tcs = new TaskCompletionSource<string>();
            await _mappingLock.WaitAsync();
            try
            {
                _orderIdMapping[reqId] = tcs;
                FileLogger.LogWebsocket($"[WS-PRIVATE] OrderId mapping created for reqId: {reqId}");
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
            await SendMessageAsync(_tradeWs, _tradeSendLock, orderMessage, "TRADE");
            var t1 = DateTime.UtcNow;

            var sendLatency = (t1 - t0).TotalMilliseconds;
            FileLogger.LogWebsocket($"[WS-PRIVATE] Limit order sent in {sendLatency:F2}ms. ReqId: {reqId}");

            // Wait for real OrderId from WS response (timeout 5 seconds)
            var realOrderIdTask = tcs.Task;
            if (await Task.WhenAny(realOrderIdTask, Task.Delay(5000)) == realOrderIdTask)
            {
                var realId = await realOrderIdTask;
                FileLogger.LogWebsocket($"[WS-PRIVATE] Received real OrderId '{realId}' for reqId '{reqId}'.");
                return realId;
            }

            FileLogger.LogWebsocket($"[WS-PRIVATE-ERROR] Timeout waiting for OrderId for reqId '{reqId}'. Returning reqId as fallback.");
            await _mappingLock.WaitAsync();
            try
            {
                _orderIdMapping.Remove(reqId);
            }
            finally
            {
                _mappingLock.Release();
            }
            return reqId; // Fallback to reqId if timeout
        }

        public async Task<bool> ModifyOrderAsync(string symbol, string orderId, decimal newPrice, decimal newQuantity)
        {
            if (!_isTradeAuthenticated)
                throw new InvalidOperationException("Trade WebSocket is not authenticated");

            var reqId = Guid.NewGuid().ToString("N");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            var qtyStr = newQuantity.ToString("0.########", CultureInfo.InvariantCulture);
            var priceStr = newPrice.ToString("0.########", CultureInfo.InvariantCulture);

            var amendMessage = $@"{{""reqId"":""{reqId}"",""header"":{{""X-BAPI-TIMESTAMP"":""{timestamp}"",""X-BAPI-RECV-WINDOW"":""5000""}},""op"":""order.amend"",""args"":[{{""category"":""spot"",""symbol"":""{symbol}"",""orderId"":""{orderId}"",""qty"":""{qtyStr}"",""price"":""{priceStr}""}}]}}";

            var t0 = DateTime.UtcNow;
            await SendMessageAsync(_tradeWs, _tradeSendLock, amendMessage, "TRADE");
            var t1 = DateTime.UtcNow;

            var sendLatency = (t1 - t0).TotalMilliseconds;
            FileLogger.LogWebsocket($"[WS-PRIVATE] Order amend sent in {sendLatency:F2}ms. ReqId: {reqId}");

            // Assuming amend is successful if no error is thrown.
            // A more robust implementation would wait for the amend confirmation message.
            return true;
        }

        public async Task SubscribeToOrderUpdatesAsync(Action<IOrder> onOrderUpdate)
        {
            _orderUpdateCallback = onOrderUpdate;

            var subscribeMessage = @"{""op"":""subscribe"",""args"":[""order""]}";
            await SendMessageAsync(_privateWs, _privateSendLock, subscribeMessage, "PRIVATE");
            FileLogger.LogWebsocket("[WS-PRIVATE] Sent subscription request for 'order' topic.");
        }

        public async Task SubscribeToOrderBookAsync(string symbol, Action<IOrderBook> onOrderBookUpdate)
        {
            _orderBookCallback = onOrderBookUpdate;

            // Subscribe to 50-level orderbook on public WebSocket
            var subscribeMessage = $@"{{""op"":""subscribe"",""args"":[""orderbook.50.{symbol}""]}}";
            await SendMessageAsync(_publicWs, _publicSendLock, subscribeMessage, "PUBLIC");
            FileLogger.LogWebsocket($"[WS-PUBLIC] Sent subscription request for 'orderbook.50.{symbol}' topic.");
    
            // Start a background task to send pings every 20 seconds to keep the connection alive
            _ = Task.Run(async () =>
            {
                while (_publicWs.State == WebSocketState.Open)
                {
                    await Task.Delay(20000);
                    if (_publicWs.State == WebSocketState.Open)
                    {
                        await PingAsync(_publicWs, _publicSendLock, "PUBLIC");
                    }
                }
            });
        }

        public async Task CancelAllOrdersAsync(string symbol)
        {
            // TODO: Implement in future sprint
            await Task.CompletedTask;
            FileLogger.LogOther("[Bybit LowLatency WS] CancelAllOrders not yet implemented");
        }

        public async Task CancelOrderAsync(string symbol, string orderId)
        {
            // TODO: Implement in future sprint
            await Task.CompletedTask;
            FileLogger.LogOther("[Bybit LowLatency WS] CancelOrder not yet implemented");
        }

        public async Task UnsubscribeAllAsync()
        {
            _orderBookCallback = null;
            _orderUpdateCallback = null;
            await Task.CompletedTask;
        }

        private async Task SendMessageAsync(ClientWebSocket ws, SemaphoreSlim sendLock, string message, string name)
        {
            await sendLock.WaitAsync();
            try
            {
                if (ws.State != WebSocketState.Open)
                {
                    FileLogger.LogWebsocket($"[WS-{name}-ERROR] Cannot send message, socket is not open. State: {ws.State}");
                    return;
                }
                FileLogger.LogWebsocket($"[WS-{name}-SEND] {message}");
                var buffer = _bufferPool.Rent(4096);
                try
                {
                    var bytesWritten = Encoding.UTF8.GetBytes(message, 0, message.Length, buffer, 0);
                    var segment = new ArraySegment<byte>(buffer, 0, bytesWritten);

                    await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                finally
                {
                    _bufferPool.Return(buffer);
                }
            }
            finally
            {
                sendLock.Release();
            }
        }

        private async Task ReceiveLoop(ClientWebSocket ws, string name)
        {
            var buffer = _bufferPool.Rent(16384); // Increased buffer for order book data
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    var receiveTime = DateTime.UtcNow;

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        FileLogger.LogWebsocket($"[WS-{name}-EVENT] Close message received. Status: {result.CloseStatus}, Description: {result.CloseStatusDescription}");
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client acknowledging close", CancellationToken.None);
                        FileLogger.LogWebsocket($"[WS-{name}-EVENT] WebSocket closed.");
                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    FileLogger.LogWebsocket($"[WS-{name}-RECV] @ {receiveTime:HH:mm:ss.fff}: {message}");

                    // Parse and route message
                    try
                    {
                        using var doc = JsonDocument.Parse(message);
                        var root = doc.RootElement;

                        // Handle auth response
                        if (root.TryGetProperty("op", out var op) && op.GetString() == "auth")
                        {
                            var success = (root.TryGetProperty("success", out var s) && s.GetBoolean()) ||
                                          (root.TryGetProperty("retCode", out var rc) && rc.TryGetInt32(out int rcValue) && rcValue == 0);

                            if (success)
                            {
                                if (name == "PRIVATE") _isPrivateAuthenticated = true;
                                if (name == "TRADE") _isTradeAuthenticated = true;
                                FileLogger.LogWebsocket($"[WS-{name}-EVENT] Authentication successful.");
                            }
                            else
                            {
                                if (name == "PRIVATE") _isPrivateAuthenticated = false;
                                if (name == "TRADE") _isTradeAuthenticated = false;
                                string errorDetail = "Unknown error";
                                if (root.TryGetProperty("retMsg", out var msg) && msg.ValueKind == JsonValueKind.String)
                                {
                                    errorDetail = msg.GetString() ?? "Unknown error";
                                }
                                FileLogger.LogWebsocket($"[WS-{name}-ERROR] Authentication failed: {errorDetail}");
                            }
                            continue;
                        }
                        
                        // Handle subscription confirmation
                        if (root.TryGetProperty("op", out op) && op.GetString() == "subscribe")
                        {
                            var success = root.TryGetProperty("success", out var s) && s.GetBoolean();
                            var retMsg = root.TryGetProperty("ret_msg", out var m) ? m.GetString() : "N/A";
                            var args = root.TryGetProperty("args", out var a) ? a.ToString() : "N/A";
                            FileLogger.LogWebsocket($"[WS-{name}-EVENT] Subscription response. Success: {success}, Msg: '{retMsg}', Args: {args}");
                            continue;
                        }

                        // Check for operation responses (order.create, order.amend)
                        if (root.TryGetProperty("reqId", out var reqIdProp) && reqIdProp.ValueKind == JsonValueKind.String)
                        {
                            var reqId = reqIdProp.GetString();
                            var retCode = root.TryGetProperty("retCode", out var rc) ? rc.GetInt32() : -1;
                            var retMsg = root.TryGetProperty("retMsg", out var rm) ? rm.GetString() : "N/A";
                            FileLogger.LogWebsocket($"[WS-{name}-EVENT] Operation response for reqId '{reqId}'. Code: {retCode}, Msg: '{retMsg}'");

                            // Extract real OrderId from successful order.create response
                            if (root.TryGetProperty("op", out var opType) && opType.GetString() == "order.create")
                            {
                                if (retCode == 0)
                                {
                                    if (root.TryGetProperty("data", out var data) && data.TryGetProperty("orderId", out var orderIdProp))
                                    {
                                        var realOrderId = orderIdProp.GetString();
                                        if (reqId != null && realOrderId != null)
                                        {
                                            await CompleteOrderIdMapping(reqId, realOrderId);
                                        }
                                    }
                                }
                                else
                                {
                                    FileLogger.LogWebsocket($"[WS-{name}-ERROR] Order creation failed for reqId '{reqId}'. Full response: {message}");
                                    if (reqId != null) await FailOrderIdMapping(reqId);
                                }
                            }
                            continue;
                        }

                        // Check for topic-based messages (order updates)
                        if (root.TryGetProperty("topic", out var topic))
                        {
                            var topicValue = topic.GetString();
                            FileLogger.LogWebsocket($"[WS-{name}-EVENT] '{topicValue}' topic message received. Routing to handler.");
                            if (topicValue == "order")
                            {
                                HandleOrderUpdate(root);
                            }
                            else if (topicValue?.StartsWith("orderbook") == true)
                            {
                                HandleOrderBookUpdate(root);
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        FileLogger.LogWebsocket($"[WS-{name}-ERROR] JSON parse error: {ex.Message}. Raw message: {message}");
                    }
                }
            }
            catch (WebSocketException ex)
            {
                FileLogger.LogWebsocket($"[WS-{name}-ERROR] WebSocket exception: {ex.Message} (ErrorCode: {ex.WebSocketErrorCode})");
            }
            catch (Exception ex)
            {
                FileLogger.LogWebsocket($"[WS-{name}-ERROR] Receive loop unexpected error: {ex.Message}");
            }
            finally
            {
                FileLogger.LogWebsocket($"[WS-{name}-EVENT] Receive loop finished.");
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
                        var orderUpdate = new BybitOrderUpdate(orderData);
                        var adapter = new BybitOrderAdapter(orderUpdate);
                        _orderUpdateCallback(adapter);
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogWebsocket($"[WS-PRIVATE-ERROR] Error in HandleOrderUpdate: {ex.Message}");
            }
        }

        private void HandleOrderBookUpdate(JsonElement root)
        {
            if (_orderBookCallback == null) return;

            try
            {
                var type = root.GetProperty("type").GetString();
                var data = root.GetProperty("data");
                var symbol = data.GetProperty("s").GetString() ?? "";

                lock (_bookLock)
                {
                    if (type == "snapshot")
                    {
                        _bids.Clear();
                        _asks.Clear();
                        ApplyChanges(_bids, data.GetProperty("b"));
                        ApplyChanges(_asks, data.GetProperty("a"));
                    }
                    else if (type == "delta")
                    {
                        ApplyChanges(_bids, data.GetProperty("b"));
                        ApplyChanges(_asks, data.GetProperty("a"));
                    }

                    // Pass a copy of the current state to the callback
                    var currentBook = new MaintainedOrderBook(symbol, _bids, _asks);
                    _orderBookCallback(currentBook);
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogWebsocket($"[WS-PUBLIC-ERROR] Error in HandleOrderBookUpdate: {ex.Message}. Payload: {root.ToString()}");
            }
        }

        private void ApplyChanges(SortedDictionary<decimal, decimal> bookSide, JsonElement changes)
        {
            foreach (var change in changes.EnumerateArray())
            {
                var price = decimal.Parse(change[0].GetString()!, CultureInfo.InvariantCulture);
                var qty = decimal.Parse(change[1].GetString()!, CultureInfo.InvariantCulture);

                if (qty == 0)
                {
                    bookSide.Remove(price);
                }
                else
                {
                    bookSide[price] = qty;
                }
            }
        }

        private async Task CompleteOrderIdMapping(string reqId, string realOrderId)
        {
            await _mappingLock.WaitAsync();
            try
            {
                if (_orderIdMapping.TryGetValue(reqId, out var tcs))
                {
                    FileLogger.LogWebsocket($"[WS-TRADE-EVENT] Completing mapping for reqId '{reqId}' with real OrderId '{realOrderId}'.");
                    tcs.TrySetResult(realOrderId);
                    _orderIdMapping.Remove(reqId);
                }
                else
                {
                    FileLogger.LogWebsocket($"[WS-TRADE-WARN] Received OrderId '{realOrderId}' for reqId '{reqId}', but no pending mapping was found.");
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
                    FileLogger.LogWebsocket($"[WS-TRADE-WARN] Failing mapping for reqId '{reqId}'. Completing with reqId as fallback.");
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
            _privateSendLock?.Dispose();
            _tradeSendLock?.Dispose();
            _publicSendLock?.Dispose();
            _mappingLock?.Dispose();
            _privateWs?.Dispose();
            _tradeWs?.Dispose();
            _publicWs?.Dispose();
        }
    }

    /// <summary>
    /// A concrete IOrderBook implementation representing the maintained local order book state.
    /// </summary>
    internal class MaintainedOrderBook : IOrderBook
    {
        public string Symbol { get; }
        public IEnumerable<IOrderBookEntry> Bids { get; }
        public IEnumerable<IOrderBookEntry> Asks { get; }

        public MaintainedOrderBook(string symbol, SortedDictionary<decimal, decimal> bids, SortedDictionary<decimal, decimal> asks)
        {
            Symbol = symbol;
            // Create a copy to ensure thread safety for the consumer
            Bids = bids.Select(kvp => (IOrderBookEntry)new OrderBookEntry { Price = kvp.Key, Quantity = kvp.Value }).ToList();
            Asks = asks.Select(kvp => (IOrderBookEntry)new OrderBookEntry { Price = kvp.Key, Quantity = kvp.Value }).ToList();
        }
    }

    // Lightweight models for parsing WebSocket messages
    public class BybitOrderUpdate : IOrder
    {
        public string Symbol { get; }
        public long OrderId { get; }
        public decimal Price { get; }
        public decimal Quantity { get; }
        public decimal CumulativeQuantityFilled { get; }
        public decimal QuoteQuantity { get; }
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
            CumulativeQuantityFilled = data.TryGetProperty("cumExecQty", out var cq) && decimal.TryParse(cq.GetString(), out var cqty) ? cqty : 0;
            QuoteQuantity = data.TryGetProperty("cumExecValue", out var cev) && decimal.TryParse(cev.GetString(), out var ceValue) ? ceValue : 0;
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
