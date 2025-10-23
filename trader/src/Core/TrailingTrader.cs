using System;
using System.Threading;
using System.Threading.Tasks;

namespace TraderBot.Core
{
    public class TrailingTrader : ITrader
    {
        private readonly IExchange _exchange;
        private long? _orderId;
        private decimal? _currentOrderPrice;
        private decimal _quantity;
        private readonly object _lock = new object();
        private bool _isPlacingOrder = false;
        private bool _isModifyingOrder = false;
        private decimal _tickSize;
        private int _basePrecision;
        private decimal _lastPlacedPrice;

        public TrailingTrader(IExchange exchange)
        {
            _exchange = exchange;
        }

        public async Task StartAsync(string symbol, decimal amount, int durationMinutes)
        {
            Console.WriteLine("--- Initial Setup ---");
            var balance = await _exchange.GetBalanceAsync("USDT");
            Console.WriteLine($"USDT Balance: {balance}");

            var filters = await _exchange.GetSymbolFiltersAsync(symbol);
            _tickSize = filters.tickSize;
            _basePrecision = (int)filters.basePrecision;
            Console.WriteLine($"Filters for {symbol}: TickSize={_tickSize}, BasePrecision={_basePrecision}");

            Console.WriteLine($"Cancelling all open orders for {symbol}...");
            await _exchange.CancelAllOrdersAsync(symbol);
            Console.WriteLine("All open orders cancelled.");
            Console.WriteLine("---------------------\n");

            await _exchange.SubscribeToPriceUpdatesAsync(symbol, async bestBidPrice =>
            {
                if (bestBidPrice == 0) return;
                var newTargetPrice = Math.Round(bestBidPrice * 0.99m / _tickSize) * _tickSize;
                
                if (_lastPlacedPrice > 0)
                    Console.WriteLine($"[TT] bid={bestBidPrice}  tgt={newTargetPrice}  last={_lastPlacedPrice}  diff%={((newTargetPrice - _lastPlacedPrice) / _lastPlacedPrice * 100):F3}");

                if (Math.Abs(newTargetPrice - _lastPlacedPrice) < _tickSize)
                {
                    Console.WriteLine("[TT] skip modify – same level");
                    return;
                }

                lock (_lock)
                {
                    if (_orderId == null)
                    {
                        if (_isPlacingOrder) return;
                        _isPlacingOrder = true;
                    }
                    else
                    {
                        if (_isModifyingOrder || newTargetPrice == _currentOrderPrice) return;
                        _isModifyingOrder = true;
                    }
                }

                if (_orderId == null)
                {
                    Console.WriteLine($"First price update. Best Bid: {bestBidPrice}. Placing order at {newTargetPrice}");
                    _quantity = Math.Round(amount / newTargetPrice, _basePrecision);
                    var placedOrderId = await _exchange.PlaceOrderAsync(symbol, _quantity, newTargetPrice);
                    if (placedOrderId.HasValue)
                    {
                        _orderId = placedOrderId;
                        _currentOrderPrice = newTargetPrice;
                        _lastPlacedPrice = newTargetPrice;
                        Console.WriteLine($"  > Successfully placed order {_orderId} at price {_currentOrderPrice}");
                    }
                    else
                    {
                        Console.WriteLine("  > Failed to place order.");
                        _isPlacingOrder = false;
                    }
                }
                else
                {
                    Console.WriteLine($"Price changed. Best Bid: {bestBidPrice}. Moving order to {newTargetPrice}");
                    var success = await _exchange.ModifyOrderAsync(symbol, _orderId.Value, newTargetPrice, _quantity);
                    if (success)
                    {
                        _currentOrderPrice = newTargetPrice;
                        _lastPlacedPrice = newTargetPrice;
                        Console.WriteLine($"  > Successfully modified order {_orderId} to price {newTargetPrice}");
                    }
                    else
                    {
                        Console.WriteLine("  > Failed to modify order.");
                    }
                    _isModifyingOrder = false;
                }
            });

            Console.WriteLine($"Listening for price changes for {durationMinutes} minutes...");
            await Task.Delay(TimeSpan.FromMinutes(durationMinutes));

            Console.WriteLine("\n--- Test Finished ---");
            Console.WriteLine("Unsubscribing and cancelling final order...");
            await _exchange.UnsubscribeAsync();
            await _exchange.CancelOrderAsync(symbol, _orderId);
            Console.WriteLine($"Final order {_orderId} cancelled.");
            Console.WriteLine("---------------------\n");
        }
    }
}