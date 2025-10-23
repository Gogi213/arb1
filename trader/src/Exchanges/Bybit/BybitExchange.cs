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
        private BybitSymbolOrderBook? _orderBook;

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
            var balanceResult = await _restClient!.V5Api.Account.GetBalancesAsync(AccountType.Unified, asset);
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
            var symbolInfo = await _restClient!.V5Api.ExchangeData.GetSpotSymbolsAsync(symbol);
            if (!symbolInfo.Success)
            {
                Console.WriteLine($"Error getting symbol info: {symbolInfo.Error}");
                return (0, 0);
            }

            var symbolData = symbolInfo.Data.List.First();
            return (symbolData.PriceFilter.TickSize, symbolData.LotSizeFilter.BasePrecision);
        }

        public async Task CancelAllOrdersAsync(string symbol)
        {
            var result = await _restClient!.V5Api.Trading.CancelAllOrderAsync(Category.Spot, symbol);
            // Bybit returns an error if there are no orders to cancel, so we only log errors that are not this specific case.
            if (!result.Success && result.Error?.Code != 170130)
            {
                Console.WriteLine($"Error cancelling all orders: {result.Error}");
            }
        }

        public async Task SubscribeToPriceUpdatesAsync(string symbol, Action<decimal> onPriceUpdate)
        {
            // Исправлена инициализация: _socketClient передается напрямую в конструктор.
            _orderBook = new BybitSymbolOrderBook(symbol, Category.Spot, null, null, _socketClient);

            _orderBook.OnOrderBookUpdate += (bookData) =>
            {
                // Исправлен доступ к данным: получаем цену из первого бида.
                var bestBid = bookData.Bids.FirstOrDefault();
                if (bestBid != null)
                {
                    onPriceUpdate(bestBid.Price);
                }
            };

            var startResult = await _orderBook.StartAsync();
            if (!startResult.Success)
            {
                Console.WriteLine($"Error starting order book: {startResult.Error}");
            }
        }

        public async Task<long?> PlaceOrderAsync(string symbol, decimal quantity, decimal price)
        {
            var result = await _restClient!.V5Api.Trading.PlaceOrderAsync(
                Category.Spot,
                symbol,
                OrderSide.Buy,
                NewOrderType.Limit,
                quantity,
                price);

            if (!result.Success)
            {
                Console.WriteLine($"Error placing order: {result.Error}");
                return null;
            }

            return long.Parse(result.Data.OrderId);
        }

        public async Task<bool> ModifyOrderAsync(string symbol, long orderId, decimal newPrice, decimal newQuantity)
        {
            var result = await _restClient!.V5Api.Trading.EditOrderAsync(
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

        public async Task UnsubscribeAsync()
        {
            if (_orderBook != null)
            {
                await _orderBook.StopAsync();
            }
        }

        public async Task CancelOrderAsync(string symbol, long? orderId)
        {
            if (orderId.HasValue)
            {
                var result = await _restClient!.V5Api.Trading.CancelOrderAsync(Category.Spot, symbol, orderId.ToString());
                if (!result.Success)
                {
                    Console.WriteLine($"Error cancelling order: {result.Error}");
                }
            }
        }
    }
}