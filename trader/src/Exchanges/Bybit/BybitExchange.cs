using System;
using System.Linq;
using System.Threading.Tasks;
using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.SymbolOrderBooks;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects.Sockets;
using TraderBot.Core;

namespace TraderBot.Exchanges.Bybit
{
    public class BybitExchange : IExchange
    {
        private IBybitRestClient? _restClient;
        private IBybitSocketClient? _socketClient;
        private readonly List<UpdateSubscription> _subscriptions = new();
        private decimal _tickSize;

        public Task InitializeAsync(string apiKey, string apiSecret)
        {
            _restClient = new BybitRestClient();
            _socketClient = new BybitSocketClient();

            _restClient.SetApiCredentials(new ApiCredentials(apiKey, apiSecret));
            _socketClient.SetApiCredentials(new ApiCredentials(apiKey, apiSecret));

            return Task.CompletedTask;
        }

        public async Task<decimal> GetBalanceAsync(string asset)
        {
            if (_restClient == null) throw new InvalidOperationException("Client not initialized");
            var balanceResult = await _restClient.V5Api.Account.GetBalancesAsync(AccountType.Unified, asset);
            if (!balanceResult.Success)
            {
                Console.WriteLine($"Error getting balance: {balanceResult.Error}");
                return 0;
            }

            var coinBalance = balanceResult.Data.List.FirstOrDefault()?.Assets.FirstOrDefault(c => c.Asset == asset);
            return coinBalance?.WalletBalance ?? 0;
        }

        public async Task<(decimal tickSize, decimal basePrecision)> GetSymbolFiltersAsync(string symbol)
        {
            if (_restClient == null) throw new InvalidOperationException("Client not initialized");
            var symbolInfoResult = await _restClient.V5Api.ExchangeData.GetSpotSymbolsAsync(symbol);
            if (!symbolInfoResult.Success)
            {
                throw new Exception($"Symbol {symbol} not found on Bybit. Error: {symbolInfoResult.Error}");
            }

            if (symbolInfoResult.Data == null || !symbolInfoResult.Data.List.Any())
            {
                throw new Exception($"Symbol {symbol} data not found in Bybit response.");
            }
            var symbolData = symbolInfoResult.Data.List.First();
            _tickSize = symbolData.PriceFilter.TickSize;
            
            if (symbolData.LotSizeFilter == null)
            {
                throw new Exception($"LotSizeFilter for {symbol} is null.");
            }
            var basePrecision = symbolData.LotSizeFilter.BasePrecision;

            return (symbolData.PriceFilter.TickSize, basePrecision);
        }

        public async Task CancelAllOrdersAsync(string symbol)
        {
            if (_restClient == null) throw new InvalidOperationException("Client not initialized");
            var result = await _restClient.V5Api.Trading.CancelAllOrderAsync(Category.Spot, symbol);
            // Bybit returns an error if there are no orders to cancel, so we only log errors that are not this specific case.
            if (!result.Success && result.Error?.Code != 170130)
            {
                Console.WriteLine($"Error cancelling all orders: {result.Error}");
            }
        }

        public async Task SubscribeToOrderBookUpdatesAsync(string symbol, Action<IOrderBook> onOrderBookUpdate)
        {
            if (_socketClient == null) throw new InvalidOperationException("Client not initialized");

            var subscriptionResult = await _socketClient.V5SpotApi.SubscribeToOrderbookUpdatesAsync(
                symbol,
                50,
                data =>
                {
                    onOrderBookUpdate(new BybitOrderBookAdapter(data.Data, symbol));
                });

            if (subscriptionResult.Success)
            {
                _subscriptions.Add(subscriptionResult.Data);
            }
            else
            {
                Console.WriteLine($"Failed to subscribe to orderbook updates: {subscriptionResult.Error}");
            }
        }

        public async Task<long?> PlaceOrderAsync(string symbol, Core.OrderSide side, Core.NewOrderType type, decimal? quantity = null, decimal? price = null, decimal? quoteQuantity = null)
        {
            if (_socketClient == null) throw new InvalidOperationException("Client not initialized");
            var orderQuantity = type == Core.NewOrderType.Market ? quoteQuantity : quantity;
            if (orderQuantity == null)
            {
                throw new ArgumentNullException(nameof(quantity), "Order quantity must be provided.");
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await _socketClient.V5PrivateApi.PlaceOrderAsync(
                Category.Spot,
                symbol,
                (global::Bybit.Net.Enums.OrderSide)side,
                (global::Bybit.Net.Enums.NewOrderType)type,
                quantity: orderQuantity.Value,
                price: price,
                marketUnit: type == Core.NewOrderType.Market ? global::Bybit.Net.Enums.MarketUnit.QuoteAsset : null,
                ct: cts.Token);

            if (!result.Success)
            {
                Console.WriteLine($"Error placing order: {result.Error}");
                return null;
            }

            return long.Parse(result.Data.OrderId);
        }

        public async Task<bool> ModifyOrderAsync(string symbol, long orderId, decimal newPrice, decimal newQuantity)
        {
            if (_restClient == null) throw new InvalidOperationException("Client not initialized");
            var result = await _restClient.V5Api.Trading.EditOrderAsync(
                Category.Spot,
                symbol,
                orderId.ToString(),
                quantity: newQuantity,
                price: newPrice);

            if (!result.Success)
            {
                Console.WriteLine($"Error modifying order: {result.Error}");
            }

            return result.Success;
        }

        public async Task SubscribeToOrderUpdatesAsync(Action<IOrder> onOrderUpdate)
        {
            if (_socketClient == null) throw new InvalidOperationException("Client not initialized");

            var subscriptionResult = await _socketClient.V5PrivateApi.SubscribeToOrderUpdatesAsync(data =>
            {
                foreach (var order in data.Data)
                {
                    onOrderUpdate(new BybitOrderAdapter(order));
                }
            });

            if (subscriptionResult.Success)
            {
                _subscriptions.Add(subscriptionResult.Data);
            }
            else
            {
                Console.WriteLine($"Failed to subscribe to order updates: {subscriptionResult.Error}");
            }
        }

        public async Task SubscribeToBalanceUpdatesAsync(Action<IBalance> onBalanceUpdate)
        {
            if (_socketClient == null) throw new InvalidOperationException("Client not initialized");

            var subscriptionResult = await _socketClient.V5PrivateApi.SubscribeToWalletUpdatesAsync(data =>
            {
                foreach (var balance in data.Data)
                {
                    foreach (var coin in balance.Assets)
                    {
                        onBalanceUpdate(new BybitBalanceAdapter(coin));
                    }
                }
            });

            if (subscriptionResult.Success)
            {
                _subscriptions.Add(subscriptionResult.Data);
            }
            else
            {
                Console.WriteLine($"Failed to subscribe to balance updates: {subscriptionResult.Error}");
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
                var result = await _restClient.V5Api.Trading.CancelOrderAsync(Category.Spot, symbol, orderId.ToString());
                if (!result.Success)
                {
                    Console.WriteLine($"Error cancelling order: {result.Error}");
                }
            }
        }
    }
}