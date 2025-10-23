using System;
using System.Threading.Tasks;

namespace TraderBot.Core
{
    public interface IExchange
    {
        Task InitializeAsync(string apiKey, string apiSecret);
        Task<decimal> GetBalanceAsync(string asset);
        Task<(decimal tickSize, decimal basePrecision)> GetSymbolFiltersAsync(string symbol);
        Task CancelAllOrdersAsync(string symbol);
        Task<long?> PlaceOrderAsync(string symbol, decimal quantity, decimal price);
        Task<bool> ModifyOrderAsync(string symbol, long orderId, decimal newPrice, decimal quantity);
        Task SubscribeToPriceUpdatesAsync(string symbol, Action<decimal> onPriceUpdate);
        Task UnsubscribeAsync();
        Task CancelOrderAsync(string symbol, long? orderId);
    }
}