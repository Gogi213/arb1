using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TraderBot.Core;

namespace TraderBot.Exchanges.Bybit
{
    /// <summary>
    /// Trailing trader for Bybit using low-latency WebSocket
    /// Follows bid with dollar depth like Gate.io implementation
    /// </summary>
    public class BybitTrailingTrader
    {
        private readonly BybitLowLatencyWs _ws;
        private long? _orderId;
        private decimal? _currentOrderPrice;
        private decimal _quantity;
        private readonly SemaphoreSlim _orderLock = new SemaphoreSlim(1, 1);
        private decimal _tickSize;
        private int _basePrecision;
        private decimal _lastPlacedPrice;
        private bool _isStopped;
        private bool _isFilled;
        private decimal _dollarDepth;

        public event Action<IOrder>? OnOrderFilled;

        public BybitTrailingTrader(BybitLowLatencyWs ws)
        {
            _ws = ws ?? throw new ArgumentNullException(nameof(ws));
        }

        public async Task StartAsync(string symbol, decimal amount, decimal dollarDepth, decimal tickSize, int basePrecision)
        {
            _tickSize = tickSize;
            _basePrecision = basePrecision;
            _dollarDepth = dollarDepth;

            Console.WriteLine($"[BybitTrailing] Starting for {symbol}, amount=${amount}, depth=${dollarDepth}");

            // Subscribe to order updates to detect fills
            await _ws.SubscribeToOrderUpdatesAsync(HandleOrderUpdate);

            // Subscribe to order book for trailing
            await _ws.SubscribeToOrderBookAsync(symbol, async orderBook =>
            {
                if (_isStopped || _isFilled) return;

                await _orderLock.WaitAsync();
                try
                {
                    if (_isStopped || _isFilled) return;

                    var bestBid = orderBook.Bids.FirstOrDefault()?.Price ?? 0;
                    if (bestBid == 0) return;

                    var newTargetPrice = BybitLowLatencyWs.CalculateTargetPriceForBuy(orderBook, _dollarDepth, _tickSize);

                    if (_lastPlacedPrice > 0)
                    {
                        var diffPercent = ((newTargetPrice - _lastPlacedPrice) / _lastPlacedPrice * 100);
                        Console.WriteLine($"[BT] bid={bestBid:F5}  tgt={newTargetPrice:F5}  last={_lastPlacedPrice:F5}  diff%={diffPercent:F3}");
                    }

                    if (Math.Abs(newTargetPrice - _lastPlacedPrice) < _tickSize)
                    {
                        return;
                    }

                    if (_orderId == null)
                    {
                        Console.WriteLine($"[BybitTrailing] Best Bid: {bestBid:F5}. Placing BUY order at {newTargetPrice:F5}");
                        _quantity = Math.Round(amount / newTargetPrice, _basePrecision);

                        var placedOrderIdStr = await _ws.PlaceLimitOrderAsync(symbol, "Buy", _quantity, newTargetPrice);

                        if (placedOrderIdStr != null && long.TryParse(placedOrderIdStr, out var placedOrderId))
                        {
                            _orderId = placedOrderId;
                            _currentOrderPrice = newTargetPrice;
                            _lastPlacedPrice = newTargetPrice;
                            Console.WriteLine($"  > Successfully placed order {_orderId} at price {_currentOrderPrice}");
                        }
                        else
                        {
                            Console.WriteLine("  > Failed to place order.");
                        }
                    }
                    else
                    {
                        if (newTargetPrice == _currentOrderPrice) return;

                        Console.WriteLine($"[BybitTrailing] Price changed. Best Bid: {bestBid:F5}. Moving order to {newTargetPrice:F5}");
                        var modifyStart = DateTime.UtcNow;
                        var success = await _ws.ModifyOrderAsync(symbol, _orderId.Value.ToString(), newTargetPrice, _quantity);
                        var modifyEnd = DateTime.UtcNow;
                        var modifyLatency = (modifyEnd - modifyStart).TotalMilliseconds;

                        if (success)
                        {
                            _currentOrderPrice = newTargetPrice;
                            _lastPlacedPrice = newTargetPrice;
                            Console.WriteLine($"  > Successfully modified order {_orderId} to price {newTargetPrice}");
                            Console.WriteLine($"[Latency] ModifyOrderAsync execution time: {modifyLatency:F0}ms");
                        }
                        else
                        {
                            Console.WriteLine($"  > Failed to modify order. Time spent: {modifyLatency:F0}ms");
                        }
                    }
                }
                finally
                {
                    _orderLock.Release();
                }
            });

            Console.WriteLine($"[BybitTrailing] Listening for price changes...");
        }

        public async Task StopAsync(string symbol)
        {
            _isStopped = true;
            Console.WriteLine("\n[BybitTrailing] Stopped");
            // Note: Cancellation not implemented yet (in future sprint)
            await Task.CompletedTask;
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

                Console.WriteLine($"[Bybit Order Update] OrderId: {order.OrderId}, Status: {order.Status}, FinishType: {order.FinishType}");
                Console.WriteLine($"[Timestamps] Created: {createTimeStr}, Updated: {updateTimeStr}, LocalReceived: {nowStr}");

                if (order.UpdateTime.HasValue && order.CreateTime.HasValue)
                {
                    var fillLatency = (order.UpdateTime.Value - order.CreateTime.Value).TotalMilliseconds;
                    Console.WriteLine($"[Latency] Order fill time: {fillLatency:F0}ms");
                }

                if (order.OrderId == _orderId && order.Status == "Filled")
                {
                    Console.WriteLine($"[!!!] Bybit order {order.OrderId} was FILLED!");
                    _isFilled = true;
                    OnOrderFilled?.Invoke(order);
                }
            }
            finally
            {
                _orderLock.Release();
            }
        }
    }
}
