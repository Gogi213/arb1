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
        private readonly TaskCompletionSource<decimal> _arbitrageCycleTcs = new TaskCompletionSource<decimal>();
        private string? _symbol;
        private DateTime? _buyFilledServerTime;
        private DateTime? _buyFilledLocalTime;
        private int _sellBasePrecision;
        private decimal _lastExecutedSellQuantity = 0;
        private ArbitrageCycleState _state;

        public ArbitrageTrader(IExchange buyExchange, IExchange sellExchange)
        {
            _buyExchange = buyExchange ?? throw new ArgumentNullException(nameof(buyExchange));
            _sellExchange = sellExchange ?? throw new ArgumentNullException(nameof(sellExchange));
            _trailingTrader = new TrailingTrader(_buyExchange);
        }

        public async Task<decimal> StartAsync(string symbol, decimal amount, int durationMinutes, ArbitrageCycleState state)
        {
            _symbol = symbol;
            _state = state;
            FileLogger.LogOther($"--- Starting ArbitrageTrader for {symbol} ---");
            FileLogger.LogOther($"Buy on: {_buyExchange.GetType().Name}, Sell on: {_sellExchange.GetType().Name}");

            // Get precision for the SELL exchange to round quantities correctly
            var sellSymbol = symbol.Replace("_", "");
            var (_, basePrecision) = await _sellExchange.GetSymbolFiltersAsync(sellSymbol);
            _sellBasePrecision = (int)basePrecision;
            FileLogger.LogOther($"[Setup] Sell exchange precision: {_sellBasePrecision} decimal places.");

            // Subscribe to sell exchange order updates for confirmation
            _sellExchange.SubscribeToOrderUpdatesAsync(HandleSellOrderUpdate);

            // Subscribe to fill events from the TrailingTrader
            _trailingTrader.OnOrderFilled += HandleBuyOrderFilled;

            // Start the buying process on the buyExchange (don't await it here)
            _trailingTrader.StartAsync(symbol, amount, durationMinutes);

            return await _arbitrageCycleTcs.Task;
        }

        private async Task CleanupAndSignalCompletionAsync()
        {
            if (_symbol == null) return;
            FileLogger.LogOther("[Arbitrage] Cleanup started...");
            await _trailingTrader.StopAsync(_symbol);
            await _sellExchange.UnsubscribeAsync();
            FileLogger.LogOther("[Arbitrage] Cleanup finished.");
            _arbitrageCycleTcs.TrySetResult(_lastExecutedSellQuantity); // Signal that the entire cycle is complete
        }

        private async void HandleBuyOrderFilled(IOrder filledOrder)
        {
            var t0 = DateTime.UtcNow;
            await _sellLock.WaitAsync();
            var t1 = DateTime.UtcNow;

            try
            {
                var buyFillServerTime = filledOrder.UpdateTime ?? filledOrder.CreateTime;
                var buyFillServerTimeStr = buyFillServerTime?.ToString("HH:mm:ss.fff") ?? "N/A";

                // Save timestamps for end-to-end calculation
                _buyFilledServerTime = buyFillServerTime;
                _buyFilledLocalTime = t0;

                FileLogger.LogOther($"[Arbitrage] Buy order {filledOrder.OrderId} filled on {_buyExchange.GetType().Name}!");
                _state.GateIoLeg1BuyQuantity = filledOrder.Quantity;
                FileLogger.LogOther($"[Arbitrage] Buy fill server time: {buyFillServerTimeStr}, Handler entered: {t0:HH:mm:ss.fff}");

                if (buyFillServerTime.HasValue)
                {
                    var wsLatency = (t0 - buyFillServerTime.Value).TotalMilliseconds;
                    FileLogger.LogOther($"[Latency] WS propagation delay (Gate fill -> handler): {wsLatency:F0}ms");
                }

                var lockWait = (t1 - t0).TotalMilliseconds;
                if (lockWait > 1)
                {
                    FileLogger.LogOther($"[Latency] Lock wait time: {lockWait:F0}ms");
                }

                FileLogger.LogOther($"[Arbitrage] Immediately selling on {_sellExchange.GetType().Name}.");

                // Bybit uses a different symbol format (without underscore)
                var sellSymbol = filledOrder.Symbol.Replace("_", "");

                // Round the quantity according to the sell exchange's rules
                var sellQuantity = Math.Round(filledOrder.Quantity, _sellBasePrecision);
                FileLogger.LogOther($"[Arbitrage] Original buy quantity: {filledOrder.Quantity}, rounded sell quantity: {sellQuantity}");

                var t2 = DateTime.UtcNow;
                var sellOrderId = await _sellExchange.PlaceOrderAsync(
                    sellSymbol,
                    OrderSide.Sell,
                    NewOrderType.Market,
                    quantity: sellQuantity);
                var t3 = DateTime.UtcNow;

                var placeOrderLatency = (t3 - t2).TotalMilliseconds;
                FileLogger.LogOther($"[Latency] PlaceOrderAsync (Bybit) execution time: {placeOrderLatency:F0}ms");

                if (sellOrderId.HasValue)
                {
                    _pendingSellOrderId = sellOrderId.Value;
                    _sellConfirmed = false;
                    FileLogger.LogOther($"[Arbitrage] Sell order {sellOrderId.Value} placed on {_sellExchange.GetType().Name} at {t3:HH:mm:ss.fff}");
                    FileLogger.LogOther($"[Arbitrage] Total time from handler entry to sell placed: {(t3 - t0).TotalMilliseconds:F0}ms");
                    FileLogger.LogOther($"[Arbitrage] Waiting for WS confirmation...");
                }
                else
                {
                    FileLogger.LogOther($"[Arbitrage] FAILED to place market sell order on {_sellExchange.GetType().Name}.");
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogOther($"[Arbitrage] CRITICAL FAILURE placing sell order on {_sellExchange.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _sellLock.Release();
            }
        }

        private async void HandleSellOrderUpdate(IOrder order)
        {
            var t0 = DateTime.UtcNow;
            await _sellLock.WaitAsync();
            try
            {
                if (_pendingSellOrderId == null || order.OrderId != _pendingSellOrderId.Value || _sellConfirmed)
                    return;

                var sellFillServerTime = order.UpdateTime ?? order.CreateTime;
                var sellFillServerTimeStr = sellFillServerTime?.ToString("HH:mm:ss.fff") ?? "N/A";

                FileLogger.LogOther($"[Arbitrage] Sell order update: OrderId={order.OrderId}, Status={order.Status}, FinishType={order.FinishType}");
                FileLogger.LogOther($"[Arbitrage] Sell fill server time: {sellFillServerTimeStr}, Update received: {t0:HH:mm:ss.fff}");

                if (sellFillServerTime.HasValue)
                {
                    var wsLatency = (t0 - sellFillServerTime.Value).TotalMilliseconds;
                    FileLogger.LogOther($"[Latency] WS propagation delay (Bybit fill -> handler): {wsLatency:F0}ms");
                }

                // Check if filled (Bybit returns Status=Filled for market orders)
                if (order.Status == "Filled" || order.FinishType == "Filled")
                {
                    // Store the actual executed quantity from the sell order
                    _lastExecutedSellQuantity = order.Quantity;

                    FileLogger.LogOther($"[Arbitrage] Sell order {order.OrderId} CONFIRMED filled on {_sellExchange.GetType().Name}!");

                    // Calculate end-to-end latency
                    if (_buyFilledServerTime.HasValue && sellFillServerTime.HasValue)
                    {
                        var serverToServerLatency = (sellFillServerTime.Value - _buyFilledServerTime.Value).TotalMilliseconds;
                        FileLogger.LogOther($"[Latency] END-TO-END (Gate fill -> Bybit fill) SERVER TIME: {serverToServerLatency:F0}ms");
                    }

                    if (_buyFilledLocalTime.HasValue)
                    {
                        var localEndToEnd = (t0 - _buyFilledLocalTime.Value).TotalMilliseconds;
                        FileLogger.LogOther($"[Latency] END-TO-END (Gate handler -> Bybit confirmation) LOCAL TIME: {localEndToEnd:F0}ms");
                    }

                    _sellConfirmed = true;
                    _pendingSellOrderId = null;

                    FileLogger.LogOther("[Arbitrage] Cycle complete.");
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
