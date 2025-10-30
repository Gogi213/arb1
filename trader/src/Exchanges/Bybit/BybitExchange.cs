using System;
using System.Threading.Tasks;
using TraderBot.Core;

namespace TraderBot.Exchanges.Bybit
{
    public class BybitExchange : IExchange
    {
        private BybitLowLatencyWs? _lowLatencyWs;
        private decimal _tickSize;
        private decimal _basePrecision;

        public async Task InitializeAsync(string apiKey, string apiSecret)
        {
            // Initialize low-latency WebSocket for all operations
            _lowLatencyWs = new BybitLowLatencyWs(apiKey, apiSecret);
            await _lowLatencyWs.ConnectAsync();
        }

        public async Task<decimal> GetBalanceAsync(string asset)
        {
            if (_lowLatencyWs == null) throw new InvalidOperationException("Client not initialized");
            // TODO: Implement via low-latency WS or keep as REST fallback
            throw new NotImplementedException("Balance query via low-latency WS not yet implemented");
        }

        public async Task<(decimal tickSize, decimal basePrecision)> GetSymbolFiltersAsync(string symbol)
        {
            if (_lowLatencyWs == null) throw new InvalidOperationException("Client not initialized");

            // Hardcoded for now - will be fetched via WS later
            // Bybit spot typical values
            _tickSize = 0.0001m;
            _basePrecision = 0; // H/USDT requires integer quantity

            FileLogger.LogOther($"[Bybit] Using hardcoded filters: tickSize={_tickSize}, basePrecision={_basePrecision}");
            return (_tickSize, _basePrecision);
        }

        public async Task CancelAllOrdersAsync(string symbol)
        {
            if (_lowLatencyWs == null) throw new InvalidOperationException("Client not initialized");
            await _lowLatencyWs.CancelAllOrdersAsync(symbol);
        }

        public async Task SubscribeToOrderBookUpdatesAsync(string symbol, Action<IOrderBook> onOrderBookUpdate)
        {
            if (_lowLatencyWs == null) throw new InvalidOperationException("Client not initialized");
            await _lowLatencyWs.SubscribeToOrderBookAsync(symbol, onOrderBookUpdate);
        }

        public async Task<long?> PlaceOrderAsync(string symbol, Core.OrderSide side, Core.NewOrderType type, decimal? quantity = null, decimal? price = null, decimal? quoteQuantity = null)
        {
            if (_lowLatencyWs == null) throw new InvalidOperationException("Client not initialized");

            // Standardize to always use base quantity. Quote quantity is no longer supported for market orders.
            var orderQuantity = quantity;
            if (orderQuantity == null)
            {
                throw new ArgumentNullException(nameof(quantity), "Order quantity must be provided.");
            }

            var t0 = DateTime.UtcNow;
            var sideStr = side == Core.OrderSide.Buy ? "Buy" : "Sell";

            string? orderIdStr;
            if (type == Core.NewOrderType.Market)
            {
                orderIdStr = await _lowLatencyWs.PlaceMarketOrderAsync(symbol, sideStr, orderQuantity.Value);
            }
            else
            {
                if (price == null)
                {
                    throw new ArgumentNullException(nameof(price), "Price must be provided for limit orders.");
                }
                orderIdStr = await _lowLatencyWs.PlaceLimitOrderAsync(symbol, sideStr, orderQuantity.Value, price.Value);
            }

            var t1 = DateTime.UtcNow;
            var apiLatency = (t1 - t0).TotalMilliseconds;
            FileLogger.LogWebsocket($"[Bybit] Low-latency WS PlaceOrder: {apiLatency:F0}ms (OrderId={orderIdStr})");

            // Parse real OrderId from Bybit
            return orderIdStr != null && long.TryParse(orderIdStr, out var orderId) ? orderId : null;
        }

        public decimal RoundQuantity(string symbol, decimal quantity)
        {
            // Bybit seems to handle rounding correctly, returning as is for now.
            return quantity;
        }

        public async Task<bool> ModifyOrderAsync(string symbol, long orderId, decimal newPrice, decimal newQuantity)
        {
            if (_lowLatencyWs == null) throw new InvalidOperationException("Client not initialized");
            return await _lowLatencyWs.ModifyOrderAsync(symbol, orderId.ToString(), newPrice, newQuantity);
        }

        public async Task SubscribeToOrderUpdatesAsync(Action<IOrder> onOrderUpdate)
        {
            if (_lowLatencyWs == null) throw new InvalidOperationException("Client not initialized");
            await _lowLatencyWs.SubscribeToOrderUpdatesAsync(onOrderUpdate);
        }

        public async Task SubscribeToBalanceUpdatesAsync(Action<IBalance> onBalanceUpdate)
        {
            if (_lowLatencyWs == null) throw new InvalidOperationException("Client not initialized");
            await _lowLatencyWs.SubscribeToBalanceUpdatesAsync(onBalanceUpdate);
        }

        public async Task UnsubscribeAsync()
        {
            if (_lowLatencyWs == null) return;
            await _lowLatencyWs.UnsubscribeAllAsync();
        }

        public async Task CancelOrderAsync(string symbol, long? orderId)
        {
            if (orderId.HasValue && _lowLatencyWs != null)
            {
                await _lowLatencyWs.CancelOrderAsync(symbol, orderId.Value.ToString());
            }
        }
    }
}