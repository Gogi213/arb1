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
        private CryptoExchange.Net.Objects.Sockets.UpdateSubscription? _subscription;

        public async Task InitializeAsync(string apiKey, string apiSecret)
        {
            _restClient = new GateIoRestClient(options =>
            {
                options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
            });
            _socketClient = new GateIoSocketClient();
            await Task.CompletedTask;
        }

        public async Task<decimal> GetBalanceAsync(string asset)
        {
            var balances = await _restClient.SpotApi.Account.GetBalancesAsync();
            if (!balances.Success) return 0m;
            var balance = balances.Data.FirstOrDefault(b => b.Asset == asset);
            return balance?.Available ?? 0m;
        }

        public Task<(decimal tickSize, decimal basePrecision)> GetSymbolFiltersAsync(string symbol)
        {
            // Gate.io implementation is not needed for now.
            return Task.FromResult((tickSize: 0.0001m, basePrecision: 4m));
        }

        public async Task CancelAllOrdersAsync(string symbol)
        {
            await _restClient.SpotApi.Trading.CancelAllOrdersAsync(symbol);
        }

        public async Task<long?> PlaceOrderAsync(string symbol, decimal quantity, decimal price)
        {
            var result = await _restClient.SpotApi.Trading.PlaceOrderAsync(
                symbol, OrderSide.Buy, NewOrderType.Limit, quantity, price: price);
            return result.Success ? result.Data.Id : null;
        }

        public async Task<bool> ModifyOrderAsync(string symbol, long orderId, decimal newPrice, decimal quantity)
        {
            var editRequest = new GateIoBatchEditRequest
            {
                OrderId = orderId.ToString(),
                Symbol = symbol,
                Price = newPrice,
                Quantity = quantity
            };
            var result = await _restClient.SpotApi.Trading.EditMultipleOrderAsync(new[] { editRequest });
            return result.Success && result.Data.All(o => o.Succeeded);
        }

        public async Task SubscribeToPriceUpdatesAsync(string symbol, Action<decimal> onPriceUpdate)
        {
            var subscriptionResult = await _socketClient.SpotApi.SubscribeToBookTickerUpdatesAsync(symbol, data =>
            {
                onPriceUpdate(data.Data.BestBidPrice);
            });

            if (subscriptionResult.Success)
            {
                _subscription = subscriptionResult.Data;
            }
        }

        public async Task UnsubscribeAsync()
        {
            if (_subscription != null)
            {
                await _socketClient.UnsubscribeAsync(_subscription);
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