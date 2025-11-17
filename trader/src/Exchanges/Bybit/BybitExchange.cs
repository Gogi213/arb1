using System;
using System.Linq;
using System.Threading.Tasks;
using Bybit.Net.Clients;
using Bybit.Net.Enums;
using CryptoExchange.Net.Authentication;
using TraderBot.Core;

namespace TraderBot.Exchanges.Bybit
{
    public class BybitExchange : IExchange
    {
        private BybitLowLatencyWs? _lowLatencyWs;
        private BybitRestClient? _restClient;
        private decimal _tickSize;
        private decimal _basePrecision;

        public async Task InitializeAsync(string apiKey, string apiSecret)
        {
            // Initialize REST client for balance queries and symbol filters
            _restClient = new BybitRestClient(options =>
            {
                options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
            });

            // Initialize low-latency WebSocket for all operations
            _lowLatencyWs = new BybitLowLatencyWs(apiKey, apiSecret);
            await _lowLatencyWs.ConnectAsync();
        }

        public async Task<decimal> GetBalanceAsync(string asset)
        {
            if (_lowLatencyWs == null) throw new InvalidOperationException("Client not initialized");

            // Try to get balance from WebSocket cache first (updated via wallet subscription)
            var wsBalance = await _lowLatencyWs.GetBalanceAsync(asset);

            // If cache is empty (no WebSocket update yet), fallback to REST API
            // Note: Bybit WebSocket does NOT send initial snapshot, only updates on changes
            if (wsBalance == 0m && _restClient != null)
            {
                FileLogger.LogOther($"[Bybit] Balance not in WS cache, querying via REST API for {asset}");

                var balancesResult = await _restClient.V5Api.Account.GetBalancesAsync(AccountType.Unified);

                if (!balancesResult.Success)
                {
                    FileLogger.LogOther($"[Bybit] Failed to get balance via REST: {balancesResult.Error?.Message}");
                    return 0m;
                }

                var accountData = balancesResult.Data.List.FirstOrDefault();
                if (accountData == null)
                {
                    FileLogger.LogOther($"[Bybit] No account data found via REST");
                    return 0m;
                }

                var balance = accountData.Assets.FirstOrDefault(a => a.Asset == asset);
                var availableBalance = balance?.WalletBalance ?? 0m;

                // Update WebSocket cache with REST result so subsequent calls are faster
                _lowLatencyWs.UpdateBalanceCache(asset, availableBalance);

                FileLogger.LogOther($"[Bybit] Balance from REST for {asset}: {availableBalance} (cache updated)");
                return availableBalance;
            }

            return wsBalance;
        }

        public async Task<(decimal tickSize, decimal basePrecision)> GetSymbolFiltersAsync(string symbol)
        {
            if (_restClient == null) throw new InvalidOperationException("Client not initialized");

            try
            {
                // Fetch symbol info from Bybit REST API - need to convert symbol format
                // ASTER_USDT -> ASTERUSDT (Bybit uses no separator)
                var bybitSymbol = symbol.Replace("_", "");

                var symbolResult = await _restClient.V5Api.ExchangeData.GetSpotSymbolsAsync(symbol: bybitSymbol);

                if (!symbolResult.Success || !symbolResult.Data.List.Any())
                {
                    FileLogger.LogOther($"[Bybit] Failed to get symbol filters for {bybitSymbol}: {symbolResult.Error?.Message}");
                    throw new Exception($"Failed to get symbol: {symbolResult.Error?.Message}");
                }

                var symbolInfo = symbolResult.Data.List.First();

                // TickSize - minimum price increment
                _tickSize = symbolInfo.PriceFilter.TickSize;

                // BasePrecision - number of decimal places for quantity
                // Bybit uses BasePrecision from LotSizeFilter
                _basePrecision = symbolInfo.LotSizeFilter.BasePrecision;

                FileLogger.LogOther($"[Bybit] Symbol {bybitSymbol} filters: tickSize={_tickSize}, basePrecision={_basePrecision}, minOrderQty={symbolInfo.LotSizeFilter.MinOrderQuantity}, minOrderValue={symbolInfo.LotSizeFilter.MinOrderValue}");

                return (_tickSize, _basePrecision);
            }
            catch (Exception ex)
            {
                FileLogger.LogOther($"[Bybit] Exception getting symbol filters: {ex.Message}");
                // Fallback to hardcoded values
                _tickSize = 0.0001m;
                _basePrecision = 0;
                FileLogger.LogOther($"[Bybit] Using fallback filters: tickSize={_tickSize}, basePrecision={_basePrecision}");
                return (_tickSize, _basePrecision);
            }
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

            // Bybit uses symbol format without underscore (XPLUSDT, not XPL_USDT)
            var bybitSymbol = symbol.Replace("_", "");

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
                FileLogger.LogOther($"[Bybit] Calling PlaceMarketOrderAsync: symbol={bybitSymbol}, side={sideStr}, qty={orderQuantity.Value}");
                orderIdStr = await _lowLatencyWs.PlaceMarketOrderAsync(bybitSymbol, sideStr, orderQuantity.Value);
                FileLogger.LogOther($"[Bybit] PlaceMarketOrderAsync returned: {orderIdStr ?? "NULL"}");
            }
            else
            {
                if (price == null)
                {
                    throw new ArgumentNullException(nameof(price), "Price must be provided for limit orders.");
                }
                orderIdStr = await _lowLatencyWs.PlaceLimitOrderAsync(bybitSymbol, sideStr, orderQuantity.Value, price.Value);
            }

            var t1 = DateTime.UtcNow;
            var apiLatency = (t1 - t0).TotalMilliseconds;
            FileLogger.LogWebsocket($"[Bybit] Low-latency WS PlaceOrder: {apiLatency:F0}ms (OrderId={orderIdStr})");

            // Parse real OrderId from Bybit
            if (orderIdStr != null && long.TryParse(orderIdStr, out var orderId))
            {
                FileLogger.LogOther($"[Bybit] Successfully parsed OrderId: {orderId}");
                return orderId;
            }
            else
            {
                FileLogger.LogOther($"[Bybit] Failed to parse OrderId from string: '{orderIdStr}'. Returning null.");
                return null;
            }
        }

        public decimal RoundQuantity(string symbol, decimal quantity)
        {
            // Bybit seems to handle rounding correctly, returning as is for now.
            return quantity;
        }

        public async Task<bool> ModifyOrderAsync(string symbol, long orderId, decimal newPrice, decimal newQuantity)
        {
            if (_lowLatencyWs == null) throw new InvalidOperationException("Client not initialized");

            // Bybit uses symbol format without underscore
            var bybitSymbol = symbol.Replace("_", "");
            return await _lowLatencyWs.ModifyOrderAsync(bybitSymbol, orderId.ToString(), newPrice, newQuantity);
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