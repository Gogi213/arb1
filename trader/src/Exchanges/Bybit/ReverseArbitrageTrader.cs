using System;
using System.Threading;
using System.Threading.Tasks;
using TraderBot.Core;

namespace TraderBot.Exchanges.Bybit
{
    /// <summary>
    /// Reverse arbitrage: Bybit (BUY limit trailing) -> Gate.io (SELL market)
    /// </summary>
    public class ReverseArbitrageTrader : ITrader
    {
        private readonly BybitLowLatencyWs _bybitWs;
        private readonly IExchange _gateIoExchange;
        private readonly BybitTrailingTrader _bybitTrailingTrader;
        private readonly SemaphoreSlim _sellLock = new SemaphoreSlim(1, 1);
        private long? _pendingSellOrderId;
        private bool _sellConfirmed;
        private readonly TaskCompletionSource<bool> _arbitrageCycleTcs = new TaskCompletionSource<bool>();
        private string? _symbol;
        private DateTime? _buyFilledServerTime;
        private DateTime? _buyFilledLocalTime;
        private decimal _tickSize;
        private int _basePrecision;

        public ReverseArbitrageTrader(BybitLowLatencyWs bybitWs, IExchange gateIoExchange)
        {
            _bybitWs = bybitWs ?? throw new ArgumentNullException(nameof(bybitWs));
            _gateIoExchange = gateIoExchange ?? throw new ArgumentNullException(nameof(gateIoExchange));
            _bybitTrailingTrader = new BybitTrailingTrader(_bybitWs);
        }

        public Task StartAsync(string symbol, decimal amount, int durationMinutes)
        {
            _symbol = symbol;
            Console.WriteLine($"[Y1] --- Starting ReverseArbitrageTrader for {symbol} ---");
            Console.WriteLine($"[Y1] Buy on: Bybit (low-latency WS), Sell on: {_gateIoExchange.GetType().Name}");

            // Subscribe to Gate.io order updates for sell confirmation
            _gateIoExchange.SubscribeToOrderUpdatesAsync(HandleSellOrderUpdate);

            // Subscribe to fill events from BybitTrailingTrader
            _bybitTrailingTrader.OnOrderFilled += HandleBuyOrderFilled;

            Console.WriteLine("[Y1] INIT complete.");

            // Y2 SETUP: Get symbol filters, cancel open orders
            Console.WriteLine("\n[Y2] --- SETUP Phase ---");

            // Convert symbol format: H_USDT -> HUSDT for Bybit
            var bybitSymbol = symbol.Replace("_", "");

            // Get symbol filters (hardcoded in BybitExchange for now)
            Console.WriteLine($"[Y2] Getting filters for {bybitSymbol}...");
            _tickSize = 0.00001m; // HUSDT typical tick size
            _basePrecision = 0; // HUSDT base precision (H token)
            Console.WriteLine($"[Y2] Filters: TickSize={_tickSize}, BasePrecision={_basePrecision}");

            // Note: Balance query not implemented yet in BybitLowLatencyWs
            Console.WriteLine("[Y2] Balance query skipped (not yet implemented via WS)");

            // Cancel all open orders
            Console.WriteLine($"[Y2] Cancelling all open orders for {bybitSymbol}...");
            _bybitWs.CancelAllOrdersAsync(bybitSymbol);
            Console.WriteLine("[Y2] All open orders cancelled.");
            Console.WriteLine("[Y2] SETUP complete.\n");

            // Y3 TRAIL: Start Bybit trailing with bid following + depth
            Console.WriteLine("[Y3] --- TRAIL Phase ---");
            Console.WriteLine($"[Y3] Starting Bybit trailing for {bybitSymbol}...");

            var dollarDepth = 25m; // $25 depth like Gate.io for faster fills in testing
            _bybitTrailingTrader.StartAsync(bybitSymbol, amount, dollarDepth, _tickSize, _basePrecision);

            Console.WriteLine("[Y3] Bybit trailing started. Waiting for fill event...");

            // Don't complete immediately - wait for TaskCompletionSource to be set by fill event
            return _arbitrageCycleTcs.Task;
        }

        private async Task CleanupAndSignalCompletionAsync()
        {
            if (_symbol == null) return;

            Console.WriteLine("[Y7] --- CLEANUP Phase ---");
            Console.WriteLine("[Y7] Cleanup started...");

            // TODO: Stop Bybit trailing
            var bybitSymbol = _symbol.Replace("_", "");
            await _bybitTrailingTrader.StopAsync(bybitSymbol);

            // Unsubscribe from Gate.io
            await _gateIoExchange.UnsubscribeAsync();

            Console.WriteLine("[Y7] Cleanup finished.");
            Console.WriteLine("[Y7] CLEANUP complete.\n");

            _arbitrageCycleTcs.TrySetResult(true);
        }

        private async void HandleBuyOrderFilled(IOrder filledOrder)
        {
            var t0 = DateTime.UtcNow;
            await _sellLock.WaitAsync();
            var t1 = DateTime.UtcNow;

            try
            {
                // Y4 FILL: Detect Bybit fill event
                Console.WriteLine("\n[Y4] --- FILL Phase ---");

                var buyFillServerTime = filledOrder.UpdateTime ?? filledOrder.CreateTime;
                var buyFillServerTimeStr = buyFillServerTime?.ToString("HH:mm:ss.fff") ?? "N/A";

                _buyFilledServerTime = buyFillServerTime;
                _buyFilledLocalTime = t0;

                Console.WriteLine($"[Y4] Buy order {filledOrder.OrderId} filled on Bybit!");
                Console.WriteLine($"[Y4] Buy fill server time: {buyFillServerTimeStr}, Handler entered: {t0:HH:mm:ss.fff}");

                if (buyFillServerTime.HasValue)
                {
                    var wsLatency = (t0 - buyFillServerTime.Value).TotalMilliseconds;
                    Console.WriteLine($"[Latency] WS propagation delay (Bybit fill -> handler): {wsLatency:F0}ms");
                }

                var lockWait = (t1 - t0).TotalMilliseconds;
                if (lockWait > 1)
                {
                    Console.WriteLine($"[Latency] Lock wait time: {lockWait:F0}ms");
                }

                Console.WriteLine("[Y4] FILL complete. Trigger: Place market SELL on Gate.io\n");

                // Y5 MARKET: Place market SELL on Gate.io
                Console.WriteLine("[Y5] --- MARKET Phase ---");
                Console.WriteLine($"[Y5] Immediately selling on {_gateIoExchange.GetType().Name}.");

                // Gate.io uses underscore format (HUSDT -> H_USDT)
                var sellSymbol = filledOrder.Symbol.Contains("USDT") && !filledOrder.Symbol.Contains("_")
                    ? filledOrder.Symbol.Replace("USDT", "_USDT")
                    : filledOrder.Symbol;

                // TODO Y5: Determine sell quantity based on Bybit fill
                var sellQuantity = 5m; // Placeholder - will use actual filled quantity from Bybit

                var t2 = DateTime.UtcNow;
                var sellOrderId = await _gateIoExchange.PlaceOrderAsync(
                    sellSymbol,
                    OrderSide.Sell,
                    NewOrderType.Market,
                    quantity: sellQuantity);
                var t3 = DateTime.UtcNow;

                var placeOrderLatency = (t3 - t2).TotalMilliseconds;
                Console.WriteLine($"[Latency] PlaceOrderAsync (Gate.io) execution time: {placeOrderLatency:F0}ms");

                if (sellOrderId.HasValue)
                {
                    _pendingSellOrderId = sellOrderId.Value;
                    _sellConfirmed = false;
                    Console.WriteLine($"[Y5] Sell order {sellOrderId.Value} placed on {_gateIoExchange.GetType().Name} at {t3:HH:mm:ss.fff}");
                    Console.WriteLine($"[Y5] Total time from handler entry to sell placed: {(t3 - t0).TotalMilliseconds:F0}ms");
                    Console.WriteLine("[Y5] MARKET complete. Waiting for WS confirmation...\n");
                }
                else
                {
                    Console.WriteLine($"[Y5] FAILED to place market sell order on {_gateIoExchange.GetType().Name}.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Y5] CRITICAL FAILURE placing sell order on {_gateIoExchange.GetType().Name}: {ex.Message}");
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

                // Y6 CONFIRM: Gate.io fill confirmation
                Console.WriteLine("\n[Y6] --- CONFIRM Phase ---");

                var sellFillServerTime = order.UpdateTime ?? order.CreateTime;
                var sellFillServerTimeStr = sellFillServerTime?.ToString("HH:mm:ss.fff") ?? "N/A";

                Console.WriteLine($"[Y6] Sell order update: OrderId={order.OrderId}, Status={order.Status}, FinishType={order.FinishType}");
                Console.WriteLine($"[Y6] Sell fill server time: {sellFillServerTimeStr}, Update received: {t0:HH:mm:ss.fff}");

                if (sellFillServerTime.HasValue)
                {
                    var wsLatency = (t0 - sellFillServerTime.Value).TotalMilliseconds;
                    Console.WriteLine($"[Latency] WS propagation delay (Gate.io fill -> handler): {wsLatency:F0}ms");
                }

                // Gate.io returns Status=Finish + FinishType=Filled
                if (order.Status == "Finish" && order.FinishType == "Filled")
                {
                    Console.WriteLine($"[Y6] Sell order {order.OrderId} CONFIRMED filled on {_gateIoExchange.GetType().Name}!");

                    // Calculate end-to-end latency
                    if (_buyFilledServerTime.HasValue && sellFillServerTime.HasValue)
                    {
                        var serverToServerLatency = (sellFillServerTime.Value - _buyFilledServerTime.Value).TotalMilliseconds;
                        Console.WriteLine($"[Latency] END-TO-END (Bybit fill -> Gate.io fill) SERVER TIME: {serverToServerLatency:F0}ms");
                    }

                    if (_buyFilledLocalTime.HasValue)
                    {
                        var localEndToEnd = (t0 - _buyFilledLocalTime.Value).TotalMilliseconds;
                        Console.WriteLine($"[Latency] END-TO-END (Bybit handler -> Gate.io confirmation) LOCAL TIME: {localEndToEnd:F0}ms");
                    }

                    _sellConfirmed = true;
                    _pendingSellOrderId = null;

                    Console.WriteLine("[Y6] CONFIRM complete. Cycle complete.\n");

                    // Y7 CLEANUP
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
