using System;
using System.Linq;
using System.Threading.Tasks;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Sockets;
using GateIo.Net.Clients;
using GateIo.Net.Enums;
using GateIo.Net.Objects.Models;
using TraderBot.Core;

namespace TraderBot.Exchanges.GateIo
{
    public class GateIoExchange : IExchange
    {
        private GateIoRestClient? _restClient;
        private GateIoSocketClient? _socketClient;
        private readonly List<CryptoExchange.Net.Objects.Sockets.UpdateSubscription> _subscriptions = new();

        public async Task InitializeAsync(string apiKey, string apiSecret)
        {
            _restClient = new GateIoRestClient(options =>
            {
                options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
            });
            _socketClient = new GateIoSocketClient(options =>
            {
                options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
            });
            await Task.CompletedTask;
        }

        public async Task<decimal> GetBalanceAsync(string asset)
        {
            if (_restClient == null) throw new InvalidOperationException("Client not initialized");
            var balances = await _restClient.SpotApi.Account.GetBalancesAsync();
            if (!balances.Success) return 0m;
            var balance = balances.Data.FirstOrDefault(b => b.Asset == asset);
            return balance?.Available ?? 0m;
        }

        public async Task<(decimal tickSize, decimal basePrecision)> GetSymbolFiltersAsync(string symbol)
        {
            if (_restClient == null) throw new InvalidOperationException("Client not initialized");
            var symbolDataResult = await _restClient.SpotApi.ExchangeData.GetSymbolAsync(symbol);
            if (!symbolDataResult.Success)
            {
                throw new Exception($"Symbol {symbol} not found on Gate.io. Error: {symbolDataResult.Error}");
            }

            var pair = symbolDataResult.Data;

            // precision - это количество знаков после запятой, поэтому tickSize = 10^(-precision)
            decimal tickSize = (decimal)Math.Pow(10, -pair.PricePrecision);
            
            // amount_precision - это и есть basePrecision
            decimal basePrecision = pair.QuantityPrecision;

            return (tickSize, basePrecision);
        }

        public async Task CancelAllOrdersAsync(string symbol)
        {
            if (_restClient == null) throw new InvalidOperationException("Client not initialized");
            await _restClient.SpotApi.Trading.CancelAllOrdersAsync(symbol);
        }

        public async Task<long?> PlaceOrderAsync(string symbol, Core.OrderSide side, Core.NewOrderType type, decimal? quantity = null, decimal? price = null, decimal? quoteQuantity = null)
        {
            if (_socketClient == null) throw new InvalidOperationException("Client not initialized");

            // Gate.io market buy order uses quantity in quote asset
            var orderQuantity = type == Core.NewOrderType.Market && side == Core.OrderSide.Buy ? quoteQuantity : quantity;

            if (orderQuantity == null)
            {
                throw new ArgumentNullException(nameof(quantity), "Order quantity must be provided.");
            }

            var t0 = DateTime.UtcNow;
            var result = await _socketClient.SpotApi.PlaceOrderAsync(
                symbol,
                (global::GateIo.Net.Enums.OrderSide)side,
                (global::GateIo.Net.Enums.NewOrderType)type,
                orderQuantity.Value,
                price: price,
                timeInForce: type == Core.NewOrderType.Market ? TimeInForce.ImmediateOrCancel : (TimeInForce?)null);
            var t1 = DateTime.UtcNow;

            var latency = (t1 - t0).TotalMilliseconds;
            FileLogger.LogWebsocket($"[Gate API] PlaceOrderAsync WS execution time: {latency:F0}ms");

            if (result.Success)
            {
                return result.Data.Id;
            }
            else
            {
                FileLogger.LogWebsocket($"[Gate API ERROR] Failed to place order. Error: {result.Error}");
                return null;
            }
        }

        public decimal RoundQuantity(string symbol, decimal quantity)
        {
            // TODO: This should use fetched precision for the specific symbol
            return Math.Round(quantity, 2);
        }

        public async Task<bool> ModifyOrderAsync(string symbol, long orderId, decimal newPrice, decimal quantity)
        {
            if (_socketClient == null) throw new InvalidOperationException("Client not initialized");

            var t0 = DateTime.UtcNow;
            var result = await _socketClient.SpotApi.EditOrderAsync(symbol, orderId, price: newPrice, quantity: quantity);
            var t1 = DateTime.UtcNow;

            var latency = (t1 - t0).TotalMilliseconds;
            if (latency > 100)
            {
                FileLogger.LogWebsocket($"[Gate API] ModifyOrderAsync WS took: {latency:F0}ms (orderId: {orderId})");
            }

            return result.Success;
        }

        public async Task SubscribeToOrderBookUpdatesAsync(string symbol, Action<IOrderBook> onOrderBookUpdate)
        {
            if (_socketClient == null) throw new InvalidOperationException("Client not initialized");
            var subscriptionResult = await _socketClient.SpotApi.SubscribeToPartialOrderBookUpdatesAsync(symbol, 20, 100, data =>
            {
                onOrderBookUpdate(new GateIoOrderBookAdapter(data.Data));
            });

            if (subscriptionResult.Success)
            {
                _subscriptions.Add(subscriptionResult.Data);
            }
        }

        public async Task SubscribeToOrderUpdatesAsync(Action<IOrder> onOrderUpdate)
        {
            if (_socketClient == null) throw new InvalidOperationException("Client not initialized");
            var subscriptionResult = await _socketClient.SpotApi.SubscribeToOrderUpdatesAsync(data =>
            {
                var wsReceiveTime = DateTime.UtcNow;
                FileLogger.LogWebsocket($"[Gate WS] Raw message received at: {wsReceiveTime:HH:mm:ss.fff}, Orders count: {data.Data.Count()}");

                foreach (var order in data.Data)
                {
                    var beforeAdapterTime = DateTime.UtcNow;
                    var adapter = new GateIoOrderAdapter(order);
                    var afterAdapterTime = DateTime.UtcNow;

                    var adapterDelay = (afterAdapterTime - beforeAdapterTime).TotalMilliseconds;
                    if (adapterDelay > 1)
                    {
                        FileLogger.LogWebsocket($"[Gate WS] Adapter creation took: {adapterDelay:F0}ms");
                    }

                    var beforeCallbackTime = DateTime.UtcNow;
                    onOrderUpdate(adapter);
                    var afterCallbackTime = DateTime.UtcNow;

                    var callbackDelay = (afterCallbackTime - beforeCallbackTime).TotalMilliseconds;
                    FileLogger.LogWebsocket($"[Gate WS] Callback execution took: {callbackDelay:F0}ms");

                    var totalDelay = (afterCallbackTime - wsReceiveTime).TotalMilliseconds;
                    FileLogger.LogWebsocket($"[Gate WS] Total processing time (receive -> callback done): {totalDelay:F0}ms");
                }
            });

            if (subscriptionResult.Success)
            {
                _subscriptions.Add(subscriptionResult.Data);
            }
        }

        public async Task SubscribeToBalanceUpdatesAsync(Action<IBalance> onBalanceUpdate)
        {
            if (_socketClient == null) throw new InvalidOperationException("Client not initialized");
            var subscriptionResult = await _socketClient.SpotApi.SubscribeToBalanceUpdatesAsync(data =>
            {
                foreach (var balance in data.Data)
                {
                    onBalanceUpdate(new GateIoBalanceAdapter(balance));
                }
            });

            if (subscriptionResult.Success)
            {
                _subscriptions.Add(subscriptionResult.Data);
            }
        }

        public async Task UnsubscribeAsync()
        {
            if (_socketClient == null) return;
            foreach (var sub in _subscriptions)
            {
                await _socketClient.UnsubscribeAsync(sub);
            }
            _subscriptions.Clear();
        }

        public async Task CancelOrderAsync(string symbol, long? orderId)
        {
            if (orderId.HasValue)
            {
                if (_restClient == null) throw new InvalidOperationException("Client not initialized");
                await _restClient.SpotApi.Trading.CancelOrderAsync(symbol, orderId.Value);
            }
        }
    }
}