using TraderBot.Core;

namespace TraderBot.Exchanges.Bybit.Adapters
{
    public class BybitOrderAdapter : IOrder
    {
        private readonly BybitOrderUpdate _order;

        public BybitOrderAdapter(BybitOrderUpdate order)
        {
            _order = order;
        }

        public string Symbol => _order.Symbol;
        public long OrderId => _order.OrderId;
        public decimal Price => _order.Price;
        public decimal Quantity => _order.Quantity;
        public decimal CumulativeQuantityFilled => _order.CumulativeQuantityFilled;
        public decimal QuoteQuantity => _order.QuoteQuantity;
        public decimal CumulativeQuoteQuantity => _order.CumulativeExecutedValue;
        public string Status => _order.Status;
        public string? FinishType => _order.FinishType;
        public System.DateTime? CreateTime => _order.CreateTime;
        public System.DateTime? UpdateTime => _order.UpdateTime;
    }

    public class BybitBalanceAdapter : IBalance
    {
        private readonly BybitBalanceUpdate _balance;

        public BybitBalanceAdapter(BybitBalanceUpdate balance)
        {
            _balance = balance;
        }

        public string Asset => _balance.Asset;
        public decimal Available => _balance.Available;
        public decimal Total => _balance.Total;
    }
}
