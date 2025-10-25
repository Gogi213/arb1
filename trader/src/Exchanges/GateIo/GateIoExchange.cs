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
        private GateIoRestClient _restClient;
        private GateIoSocketClient _socketClient;
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
            var balances = await _restClient.SpotApi.Account.GetBalancesAsync();
            if (!balances.Success) return 0m;
            var balance = balances.Data.FirstOrDefault(b => b.Asset == asset);
            return balance?.Available ?? 0m;
        }

        public async Task<(decimal tickSize, decimal basePrecision)> GetSymbolFiltersAsync(string symbol)
        {
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
            await _restClient.SpotApi.Trading.CancelAllOrdersAsync(symbol);
        }

        public async Task<long?> PlaceOrderAsync(string symbol, decimal quantity, decimal price)
        {
            var result = await _socketClient.SpotApi.PlaceOrderAsync(
                symbol,
                OrderSide.Buy,
                NewOrderType.Limit,
                quantity,
                price: price);
            return result.Success ? result.Data.Id : null;
        }

        public async Task<bool> ModifyOrderAsync(string symbol, long orderId, decimal newPrice, decimal quantity)
        {
            var result = await _socketClient.SpotApi.EditOrderAsync(symbol, orderId, price: newPrice, quantity: quantity);
            return result.Success;
        }

        public async Task SubscribeToOrderBookUpdatesAsync(string symbol, Action<IOrderBook> onOrderBookUpdate)
        {
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
            var subscriptionResult = await _socketClient.SpotApi.SubscribeToOrderUpdatesAsync(data =>
            {
                foreach (var order in data.Data)
                {
                    onOrderUpdate(new GateIoOrderAdapter(order));
                }
            });

            if (subscriptionResult.Success)
            {
                _subscriptions.Add(subscriptionResult.Data);
            }
        }

        public async Task SubscribeToBalanceUpdatesAsync(Action<IBalance> onBalanceUpdate)
        {
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
            foreach (var sub in _subscriptions)
            {
                await _socketClient.UnsubscribeAsync(sub);
            }
        }

        public async Task CancelOrderAsync(string symbol, long? orderId)
        {
            if (orderId.HasValue)
            {
                await _restClient.SpotApi.Trading.CancelOrderAsync(symbol, orderId.Value);
            }
        }
    }
}