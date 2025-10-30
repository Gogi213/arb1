using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TraderBot.Core
{
    public interface IOrderBookEntry
    {
        decimal Price { get; }
        decimal Quantity { get; }
    }

    public interface IOrderBook
    {
        string Symbol { get; }
        IEnumerable<IOrderBookEntry> Bids { get; }
        IEnumerable<IOrderBookEntry> Asks { get; }
    }

    public interface IOrder
    {
        string Symbol { get; }
        long OrderId { get; }
        decimal Price { get; }
        decimal Quantity { get; }
        decimal QuoteQuantity { get; }
        decimal CumulativeQuoteQuantity { get; }
        string Status { get; }
        string? FinishType { get; }
        DateTime? CreateTime { get; }
        DateTime? UpdateTime { get; }
    }

    public interface IBalance
    {
        string Asset { get; }
        decimal Available { get; }
    }

    public interface IExchange
    {
        Task InitializeAsync(string apiKey, string apiSecret);
        Task<decimal> GetBalanceAsync(string asset);
        Task<(decimal tickSize, decimal basePrecision)> GetSymbolFiltersAsync(string symbol);
        Task CancelAllOrdersAsync(string symbol);
        Task<long?> PlaceOrderAsync(string symbol, OrderSide side, NewOrderType type, decimal? quantity = null, decimal? price = null, decimal? quoteQuantity = null);
        decimal RoundQuantity(string symbol, decimal quantity);
        Task<bool> ModifyOrderAsync(string symbol, long orderId, decimal newPrice, decimal quantity);
        Task SubscribeToOrderBookUpdatesAsync(string symbol, Action<IOrderBook> onOrderBookUpdate);
        Task SubscribeToOrderUpdatesAsync(Action<IOrder> onOrderUpdate);
        Task SubscribeToBalanceUpdatesAsync(Action<IBalance> onBalanceUpdate);
        Task UnsubscribeAsync();
        Task CancelOrderAsync(string symbol, long? orderId);
    }
}