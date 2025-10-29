using Bybit.Net.Objects.Models.V5;
using Bybit.Net.Enums;
using System.Collections.Generic;
using System.Linq;
using TraderBot.Core;

namespace TraderBot.Exchanges.Bybit
{
    public class BybitOrderAdapter : IOrder
    {
        // Используем наш внутренний класс, а не библиотечный
        private readonly BybitOrderUpdate _order;

        public BybitOrderAdapter(BybitOrderUpdate order)
        {
            _order = order;
        }

        public string Symbol => _order.Symbol;
        public long OrderId => _order.OrderId;
        public decimal Price => _order.Price;
        // Теперь используем правильное поле для исполненного количества
        public decimal Quantity => _order.CumulativeQuantityFilled > 0 ? _order.CumulativeQuantityFilled : _order.Quantity;
        public decimal QuoteQuantity => _order.QuoteQuantity;
        public string Status => _order.Status.ToString();
        public string? FinishType => _order.Status == "Filled" ? "Filled" :
                                      _order.Status == "Cancelled" ? "Cancelled" : null;
        public DateTime? CreateTime => _order.CreateTime;
        public DateTime? UpdateTime => _order.UpdateTime;
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
