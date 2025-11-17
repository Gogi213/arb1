using GateIo.Net.Objects.Models;
using System.Collections.Generic;
using System.Linq;
using TraderBot.Core;

namespace TraderBot.Exchanges.GateIo
{
    public class GateIoOrderAdapter : IOrder
    {
        private readonly GateIoOrderUpdate _order;

        public GateIoOrderAdapter(GateIoOrderUpdate order)
        {
            _order = order;
        }

        public string Symbol => _order.Symbol;
        public long OrderId => long.Parse(_order.Id);
        public decimal Price => _order.AveragePrice ?? _order.Price ?? 0m;
        public decimal Quantity => _order.QuantityFilled;
        public decimal CumulativeQuantityFilled => _order.QuantityFilled; // Gate.io provides filled quantity directly
        public decimal QuoteQuantity => _order.QuoteQuantityFilled;
        public decimal CumulativeQuoteQuantity => _order.QuoteQuantityFilled; // Gate.io provides this directly
        public string Status => _order.Event.ToString();
        public string? FinishType => _order.FinishType?.ToString();
        public DateTime? CreateTime => _order.CreateTime;
        public DateTime? UpdateTime => _order.UpdateTime;
    }

    public class GateIoBalanceAdapter : IBalance
    {
        private readonly GateIoBalanceUpdate _balance;

        public GateIoBalanceAdapter(GateIoBalanceUpdate balance)
        {
            _balance = balance;
        }

        public string Asset => _balance.Asset;
        public decimal Available => _balance.Available;
    }

    public class GateIoOrderBookEntryAdapter : IOrderBookEntry
    {
        private readonly GateIoOrderBookEntry _entry;

        public GateIoOrderBookEntryAdapter(GateIoOrderBookEntry entry)
        {
            _entry = entry;
        }

        public decimal Price => _entry.Price;
        public decimal Quantity => _entry.Quantity;
    }

    public class GateIoOrderBookAdapter : IOrderBook
    {
        private readonly GateIoPartialOrderBookUpdate _orderBook;

        public GateIoOrderBookAdapter(GateIoPartialOrderBookUpdate orderBook)
        {
            _orderBook = orderBook;
        }

        public string Symbol => _orderBook.Symbol;
        public IEnumerable<IOrderBookEntry> Bids => _orderBook.Bids.Select(b => new GateIoOrderBookEntryAdapter(b));
        public IEnumerable<IOrderBookEntry> Asks => _orderBook.Asks.Select(a => new GateIoOrderBookEntryAdapter(a));
    }
}