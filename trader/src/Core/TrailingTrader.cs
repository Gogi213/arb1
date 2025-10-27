using System;
using System.Linq;
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
        private readonly SemaphoreSlim _orderLock = new SemaphoreSlim(1, 1);
        private decimal _tickSize;
        private int _basePrecision;
        private decimal _lastPlacedPrice;
        private CancellationTokenSource? _cts;
        private bool _isStopped;
        private bool _isFilled;

        public event Action<IOrder>? OnOrderFilled;

        public TrailingTrader(IExchange exchange)
        {
            _exchange = exchange;
        }

        public async Task StartAsync(string symbol, decimal amount, int durationMinutes)
        {
            _cts = new CancellationTokenSource();

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

            await _exchange.SubscribeToOrderUpdatesAsync(HandleOrderUpdate);
            await _exchange.SubscribeToBalanceUpdatesAsync(HandleBalanceUpdate);

            await _exchange.SubscribeToOrderBookUpdatesAsync(symbol, async orderBook =>
            {
                if (_isStopped || _isFilled) return;

                // --- Part 1: Decide what to do (inside a lock) ---
                decimal newTargetPrice;
                bool shouldPlace = false;
                bool shouldModify = false;
                long? orderIdToModify = null;
                decimal quantityToUse = 0;
                var bestBidPrice = orderBook.Bids.FirstOrDefault()?.Price ?? 0;

                await _orderLock.WaitAsync();
                try
                {
                    if (_isStopped || _isFilled || bestBidPrice == 0) return;

                    newTargetPrice = CalculateTargetPrice(orderBook, 25); // $25 offset for faster fills in testing

                    if (_lastPlacedPrice > 0)
                        Console.WriteLine($"[TT] bid={bestBidPrice}  tgt={newTargetPrice}  last={_lastPlacedPrice}  diff%={((newTargetPrice - _lastPlacedPrice) / _lastPlacedPrice * 100):F3}");

                    if (Math.Abs(newTargetPrice - _lastPlacedPrice) < _tickSize)
                    {
                        return;
                    }

                    if (_orderId == null)
                    {
                        shouldPlace = true;
                        _orderId = -1; // Sentinel value to indicate placement is in progress
                        quantityToUse = Math.Round(amount / newTargetPrice, _basePrecision);
                    }
                    else
                    {
                        if (newTargetPrice == _currentOrderPrice) return;
                        shouldModify = true;
                        orderIdToModify = _orderId.Value;
                        quantityToUse = _quantity;
                    }
                    
                    _lastPlacedPrice = newTargetPrice;
                }
                finally
                {
                    _orderLock.Release();
                }

                // --- Part 2: Execute network call (outside the lock) ---
                if (shouldPlace)
                {
                    Console.WriteLine($"Best Bid: {bestBidPrice}. Placing order at {newTargetPrice}");
                    _quantity = quantityToUse;
                    var placedOrderId = await _exchange.PlaceOrderAsync(symbol, OrderSide.Buy, NewOrderType.Limit, quantity: _quantity, price: newTargetPrice);
                    
                    await _orderLock.WaitAsync();
                    try
                    {
                        if (placedOrderId.HasValue)
                        {
                            // If we successfully placed, update the ID from sentinel to the real one
                            _orderId = placedOrderId;
                            _currentOrderPrice = newTargetPrice;
                            Console.WriteLine($"  > Successfully placed order {_orderId} at price {_currentOrderPrice}");
                        }
                        else
                        {
                            // If placement failed, reset _orderId to null to allow another attempt
                            _orderId = null;
                            Console.WriteLine("  > Failed to place order.");
                        }
                    }
                    finally
                    {
                        _orderLock.Release();
                    }
                }
                else if (shouldModify && orderIdToModify.HasValue)
                {
                    Console.WriteLine($"Price changed. Best Bid: {bestBidPrice}. Moving order to {newTargetPrice}");
                    var modifyStart = DateTime.UtcNow;
                    var success = await _exchange.ModifyOrderAsync(symbol, orderIdToModify.Value, newTargetPrice, quantityToUse);
                    var modifyEnd = DateTime.UtcNow;
                    
                    await _orderLock.WaitAsync();
                    try
                    {
                        // Check if the order is still the one we intended to modify
                        if (_orderId != orderIdToModify)
                        {
                            Console.WriteLine($"  > Modify ignored, order {orderIdToModify.Value} was filled or cancelled in the meantime.");
                            return;
                        }

                        var modifyLatency = (modifyEnd - modifyStart).TotalMilliseconds;
                        if (success)
                        {
                            _currentOrderPrice = newTargetPrice;
                            Console.WriteLine($"  > Successfully modified order {_orderId} to price {newTargetPrice}");
                            Console.WriteLine($"[Latency] ModifyOrderAsync execution time: {modifyLatency:F0}ms");
                        }
                        else
                        {
                            Console.WriteLine($"  > Failed to modify order. Time spent: {modifyLatency:F0}ms");
                        }
                    }
                    finally
                    {
                        _orderLock.Release();
                    }
                }
            });

            Console.WriteLine($"Listening for price changes...");
        }

        public async Task StopAsync(string symbol)
        {
            _isStopped = true;
            _cts?.Cancel();
            Console.WriteLine("\n--- TrailingTrader Stopped ---");
            Console.WriteLine("Unsubscribing and cancelling final order...");
            await _exchange.UnsubscribeAsync();
            await _exchange.CancelOrderAsync(symbol, _orderId);
            Console.WriteLine($"Final order {_orderId} cancelled.");
            Console.WriteLine("---------------------\n");
        }

        private async void HandleOrderUpdate(IOrder order)
        {
            await _orderLock.WaitAsync();
            try
            {
                var now = DateTime.UtcNow;
                var createTimeStr = order.CreateTime?.ToString("HH:mm:ss.fff") ?? "N/A";
                var updateTimeStr = order.UpdateTime?.ToString("HH:mm:ss.fff") ?? "N/A";
                var nowStr = now.ToString("HH:mm:ss.fff");

                Console.WriteLine($"[Order Update] Symbol: {order.Symbol}, OrderId: {order.OrderId}, Status: {order.Status}, FinishType: {order.FinishType}");
                Console.WriteLine($"[Timestamps] Created: {createTimeStr}, Updated: {updateTimeStr}, LocalReceived: {nowStr}");

                if (order.UpdateTime.HasValue && order.CreateTime.HasValue)
                {
                    var fillLatency = (order.UpdateTime.Value - order.CreateTime.Value).TotalMilliseconds;
                    Console.WriteLine($"[Latency] Order fill time: {fillLatency:F0}ms");
                }

                if (order.OrderId == _orderId && order.Status == "Finish")
                {
                    if (order.FinishType == "Filled")
                    {
                        Console.WriteLine($"[!!!] Order {order.OrderId} was FILLED!");
                        _isFilled = true;
                        OnOrderFilled?.Invoke(order);
                        // Stop(); // Stop is now handled by ArbitrageTrader
                     }
                     else
                    {
                        Console.WriteLine($"[!!!] Order {order.OrderId} was finished ({order.FinishType})!");
                    }
                    _orderId = null;
                    _currentOrderPrice = null;
                }
            }
            finally
            {
                _orderLock.Release();
            }
        }

        private void HandleBalanceUpdate(IBalance balance)
        {
            Console.WriteLine($"[Balance Update] Asset: {balance.Asset}, Available: {balance.Available}");
        }
        
        private decimal CalculateTargetPrice(IOrderBook orderBook, decimal dollarOffset)
        {
            var bestBid = orderBook.Bids.First().Price;
            decimal cumulativeVolume = 0;
            decimal targetPrice = bestBid;

            foreach (var bid in orderBook.Bids)
            {
                cumulativeVolume += bid.Price * bid.Quantity;
                if (cumulativeVolume >= dollarOffset)
                {
                    targetPrice = bid.Price;
                    break;
                }
            }

            return Math.Round(targetPrice / _tickSize) * _tickSize;
        }
    }
}