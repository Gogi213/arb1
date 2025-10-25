using System;
using System.Threading;
using System.Threading.Tasks;

namespace TraderBot.Core
{
    public class ArbitrageTrader : ITrader
    {
        private readonly IExchange _buyExchange;
        private readonly IExchange _sellExchange;
        private readonly TrailingTrader _trailingTrader;
        private readonly SemaphoreSlim _sellLock = new SemaphoreSlim(1, 1);
        private long? _pendingSellOrderId;
        private bool _sellConfirmed;
        private readonly TaskCompletionSource<bool> _arbitrageCycleTcs = new TaskCompletionSource<bool>();
        private string? _symbol;

        public ArbitrageTrader(IExchange buyExchange, IExchange sellExchange)
        {
            _buyExchange = buyExchange ?? throw new ArgumentNullException(nameof(buyExchange));
            _sellExchange = sellExchange ?? throw new ArgumentNullException(nameof(sellExchange));
            _trailingTrader = new TrailingTrader(_buyExchange);
        }

        public Task StartAsync(string symbol, decimal amount, int durationMinutes)
        {
            _symbol = symbol;
            Console.WriteLine($"--- Starting ArbitrageTrader for {symbol} ---");
            Console.WriteLine($"Buy on: {_buyExchange.GetType().Name}, Sell on: {_sellExchange.GetType().Name}");

            // Subscribe to sell exchange order updates for confirmation
            _sellExchange.SubscribeToOrderUpdatesAsync(HandleSellOrderUpdate);

            // Subscribe to fill events from the TrailingTrader
            _trailingTrader.OnOrderFilled += HandleBuyOrderFilled;

            // Start the buying process on the buyExchange (don't await it here)
            _trailingTrader.StartAsync(symbol, amount, durationMinutes);

            return _arbitrageCycleTcs.Task;
        }

        private async Task CleanupAndSignalCompletionAsync()
        {
            if (_symbol == null) return;
            Console.WriteLine("[Arbitrage] Cleanup started...");
            await _trailingTrader.StopAsync(_symbol);
            await _sellExchange.UnsubscribeAsync();
            Console.WriteLine("[Arbitrage] Cleanup finished.");
            _arbitrageCycleTcs.TrySetResult(true); // Signal that the entire cycle is complete
        }

        private async void HandleBuyOrderFilled(IOrder filledOrder)
        {
            await _sellLock.WaitAsync();
            try
            {
                Console.WriteLine($"[Arbitrage] Buy order {filledOrder.OrderId} filled on {_buyExchange.GetType().Name}!");
                Console.WriteLine($"[Arbitrage] Immediately selling $5 on {_sellExchange.GetType().Name}.");

                // Bybit uses a different symbol format (without underscore)
                var sellSymbol = filledOrder.Symbol.Replace("_", "");
                var sellAmountUsd = 5m; // Minimum for Bybit spot market order

                var sellOrderId = await _sellExchange.PlaceOrderAsync(
                    sellSymbol,
                    OrderSide.Sell,
                    NewOrderType.Market,
                    quoteQuantity: sellAmountUsd);

                if (sellOrderId.HasValue)
                {
                    _pendingSellOrderId = sellOrderId.Value;
                    _sellConfirmed = false;
                    Console.WriteLine($"[Arbitrage] Sell order {sellOrderId.Value} placed on {_sellExchange.GetType().Name}. Waiting for WS confirmation...");
                }
                else
                {
                    Console.WriteLine($"[Arbitrage] FAILED to place market sell order on {_sellExchange.GetType().Name}.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Arbitrage] CRITICAL FAILURE placing sell order on {_sellExchange.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _sellLock.Release();
            }
        }

        private async void HandleSellOrderUpdate(IOrder order)
        {
            await _sellLock.WaitAsync();
            try
            {
                if (_pendingSellOrderId == null || order.OrderId != _pendingSellOrderId.Value || _sellConfirmed)
                    return;

                Console.WriteLine($"[Arbitrage] Sell order update: OrderId={order.OrderId}, Status={order.Status}, FinishType={order.FinishType}");

                // Check if filled (Bybit returns Status=Filled for market orders)
                if (order.Status == "Filled" || order.FinishType == "Filled")
                {
                    Console.WriteLine($"[Arbitrage] Sell order {order.OrderId} CONFIRMED filled on {_sellExchange.GetType().Name}!");
                    _sellConfirmed = true;
                    _pendingSellOrderId = null;

                    Console.WriteLine("[Arbitrage] Cycle complete.");
                    await CleanupAndSignalCompletionAsync();
                }
            }
            finally
            {
                _sellLock.Release();
            }
        }
    }
}
