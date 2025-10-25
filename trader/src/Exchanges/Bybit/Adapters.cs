using Bybit.Net.Objects.Models.V5;
using Bybit.Net.Enums;
using System.Collections.Generic;
using System.Linq;
using TraderBot.Core;

namespace TraderBot.Exchanges.Bybit
{
    public class BybitOrderAdapter : IOrder
    {
        private readonly BybitOrder _order;

        public BybitOrderAdapter(BybitOrder order)
        {
            _order = order;
        }

        public string Symbol => _order.Symbol;
        public long OrderId => long.Parse(_order.OrderId);
        public decimal Price => _order.Price ?? 0m;
        public decimal Quantity => _order.Quantity;
        public string Status => _order.Status.ToString();
        public string? FinishType => _order.Status == OrderStatus.Filled ? "Filled" :
                                      _order.Status == OrderStatus.Cancelled ? "Cancelled" : null;
    }

    public class BybitBalanceAdapter : IBalance
    {
        private readonly BybitAssetBalance _balance;

        public BybitBalanceAdapter(BybitAssetBalance balance)
        {
            _balance = balance;
        }

        public string Asset => _balance.Asset;
        public decimal Available => _balance.Free ?? 0m;
    }

    public class BybitOrderBookEntryAdapter : IOrderBookEntry
    {
        private readonly BybitOrderbookEntry _entry;

        public BybitOrderBookEntryAdapter(BybitOrderbookEntry entry)
        {
            _entry = entry;
        }

        public decimal Price => _entry.Price;
        public decimal Quantity => _entry.Quantity;
    }

    public class BybitOrderBookAdapter : IOrderBook
    {
        private readonly BybitOrderbook _orderBook;
        private readonly string _symbol;

        public BybitOrderBookAdapter(BybitOrderbook orderBook, string symbol)
        {
            _orderBook = orderBook;
            _symbol = symbol;
        }

        public string Symbol => _symbol;
        public IEnumerable<IOrderBookEntry> Bids => _orderBook.Bids.Select(b => new BybitOrderBookEntryAdapter(b));
        public IEnumerable<IOrderBookEntry> Asks => _orderBook.Asks.Select(a => new BybitOrderBookEntryAdapter(a));
    }
}
