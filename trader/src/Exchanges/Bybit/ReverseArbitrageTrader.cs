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
        private readonly IExchange _bybitExchange;
        private readonly IExchange _gateIoExchange;
        private readonly BybitTrailingTrader _bybitTrailingTrader;
        private readonly SemaphoreSlim _sellLock = new SemaphoreSlim(1, 1);
        private long? _pendingSellOrderId;
        private bool _sellConfirmed;
        private readonly TaskCompletionSource<decimal> _arbitrageCycleTcs = new TaskCompletionSource<decimal>();
        private string? _symbol;
        private string? _baseAsset;
        private DateTime? _buyFilledServerTime;
        private DateTime? _buyFilledLocalTime;
        private int _sellBasePrecision;
        private decimal _lastExecutedSellQuantity = 0;
        private decimal _amount;
        private ArbitrageCycleState _state;
        private TaskCompletionSource<decimal> _balanceTcs = new();
        private Timer? _balanceDebounceTimer;
        private decimal _lastReceivedBalance;

        public ReverseArbitrageTrader(IExchange bybitExchange, IExchange gateIoExchange)
        {
            _bybitExchange = bybitExchange ?? throw new ArgumentNullException(nameof(bybitExchange));
            _gateIoExchange = gateIoExchange ?? throw new ArgumentNullException(nameof(gateIoExchange));
            _bybitTrailingTrader = new BybitTrailingTrader(_bybitExchange);
        }

        public async Task<decimal> StartAsync(string symbol, decimal amount, int durationMinutes, ArbitrageCycleState state)
        {
            _symbol = symbol;
            _amount = amount;
            _state = state;
            _baseAsset = symbol.Split('_')[0];
            FileLogger.LogOther($"[Y1] --- Starting ReverseArbitrageTrader for {symbol} ---");
            FileLogger.LogOther($"[Y1] Buy on: Bybit (low-latency WS), Sell on: {_gateIoExchange.GetType().Name}");

            // Subscribe to order updates
            _gateIoExchange.SubscribeToOrderUpdatesAsync(HandleSellOrderUpdate);
            _bybitExchange.SubscribeToBalanceUpdatesAsync(HandleBuyBalanceUpdate);

            // Subscribe to fill events from BybitTrailingTrader
            _bybitTrailingTrader.OnOrderFilled += HandleBuyOrderFilled;

            _balanceDebounceTimer = new Timer(OnBalanceDebounceTimer, null, Timeout.Infinite, Timeout.Infinite);

            FileLogger.LogOther("[Y1] INIT complete.");

            // Y2 SETUP: Get symbol filters, cancel open orders
            FileLogger.LogOther("\n[Y2] --- SETUP Phase ---");

            // Convert symbol format: H_USDT -> HUSDT for Bybit
            var bybitSymbol = symbol.Replace("_", "");

            // Get symbol filters (hardcoded in BybitExchange for now)
            FileLogger.LogOther($"[Y2] Getting filters...");
            var (sellTickSize, sellBasePrecision) = await _gateIoExchange.GetSymbolFiltersAsync(symbol);
            _sellBasePrecision = (int)sellBasePrecision;
            FileLogger.LogOther($"[Y2] Filters: Gate.io Sell Precision={_sellBasePrecision}");

            // Note: Balance query not implemented yet in BybitLowLatencyWs
            FileLogger.LogOther("[Y2] Balance query skipped (not yet implemented via WS)");

            // Cancel all open orders
            FileLogger.LogOther($"[Y2] Cancelling all open orders for {bybitSymbol}...");
            await _bybitExchange.CancelAllOrdersAsync(bybitSymbol);
            FileLogger.LogOther("[Y2] All open orders cancelled.");
            FileLogger.LogOther("[Y2] SETUP complete.\n");

            // Y3 TRAIL: Start Bybit trailing with bid following + depth
            FileLogger.LogOther("[Y3] --- TRAIL Phase ---");
            FileLogger.LogOther($"[Y3] Starting Bybit trailing for {bybitSymbol}...");

            var dollarDepth = 25m; // $25 depth like Gate.io for faster fills in testing
            _bybitTrailingTrader.StartAsync(bybitSymbol, amount, dollarDepth);

            FileLogger.LogOther("[Y3] Bybit trailing started. Waiting for fill event...");

            // Don't complete immediately - wait for TaskCompletionSource to be set by fill event
            return await _arbitrageCycleTcs.Task;
        }

        private async Task CleanupAndSignalCompletionAsync()
        {
            if (_symbol == null) return;

            FileLogger.LogOther("[Y7] --- CLEANUP Phase ---");
            FileLogger.LogOther("[Y7] Cleanup started...");

            // TODO: Stop Bybit trailing
            var bybitSymbol = _symbol.Replace("_", "");
            await _bybitTrailingTrader.StopAsync(bybitSymbol);

            // Unsubscribe from Gate.io
            await _gateIoExchange.UnsubscribeAsync();
            _balanceDebounceTimer?.Dispose();
            await _bybitExchange.UnsubscribeAsync();

            FileLogger.LogOther("[Y7] Cleanup finished.");
            FileLogger.LogOther("[Y7] CLEANUP complete.\n");

            _arbitrageCycleTcs.TrySetResult(_lastExecutedSellQuantity);
        }

        private async void HandleBuyOrderFilled(IOrder filledOrder)
        {
            var t0 = DateTime.UtcNow;
            await _sellLock.WaitAsync();
            var t1 = DateTime.UtcNow;

            try
            {
                // Y4 FILL: Detect Bybit fill event
                FileLogger.LogOther("\n[Y4] --- FILL Phase ---");

                var buyFillServerTime = filledOrder.UpdateTime ?? filledOrder.CreateTime;
                var buyFillServerTimeStr = buyFillServerTime?.ToString("HH:mm:ss.fff") ?? "N/A";

                _buyFilledServerTime = buyFillServerTime;
                _buyFilledLocalTime = t0;

                FileLogger.LogOther($"[Y4] Buy order {filledOrder.OrderId} filled on Bybit!");
                FileLogger.LogOther($"[Y4] Buy fill server time: {buyFillServerTimeStr}, Handler entered: {t0:HH:mm:ss.fff}");

                if (buyFillServerTime.HasValue)
                {
                    var wsLatency = (t0 - buyFillServerTime.Value).TotalMilliseconds;
                    FileLogger.LogOther($"[Latency] WS propagation delay (Bybit fill -> handler): {wsLatency:F0}ms");
                }

                var lockWait = (t1 - t0).TotalMilliseconds;
                if (lockWait > 1)
                {
                    FileLogger.LogOther($"[Latency] Lock wait time: {lockWait:F0}ms");
                }

                FileLogger.LogOther("[Y4] FILL complete. Waiting for balance stabilization...\n");

                // --- NEW LOGIC: Wait for the actual balance update ---
                FileLogger.LogOther($"[Y4] Waiting for balance update for asset '{_baseAsset}'...");
                var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var actualQuantity = await _balanceTcs.Task.WaitAsync(cancellationTokenSource.Token);
                _balanceTcs = new TaskCompletionSource<decimal>(); // Re-create for next cycle
                FileLogger.LogOther($"[Y4] Balance update received. Actual available quantity: {actualQuantity}");
                // --- END NEW LOGIC ---

                // Y5 MARKET: Place market SELL on Gate.io
                FileLogger.LogOther("[Y5] --- MARKET Phase ---");
                FileLogger.LogOther($"[Y5] Immediately selling on {_gateIoExchange.GetType().Name}.");

                // Gate.io uses underscore format (HUSDT -> H_USDT)
                var sellSymbol = filledOrder.Symbol.Contains("USDT") && !filledOrder.Symbol.Contains("_")
                    ? filledOrder.Symbol.Replace("USDT", "_USDT")
                    : filledOrder.Symbol;

                decimal factor = (decimal)Math.Pow(10, _sellBasePrecision);
                var sellQuantity = Math.Truncate(actualQuantity * factor) / factor;
                FileLogger.LogOther($"[Y5] Immediately selling {sellQuantity} on GateIoExchange (original from Bybit Leg 2: {actualQuantity}).");

                var t2 = DateTime.UtcNow;
                var sellOrderId = await _gateIoExchange.PlaceOrderAsync(
                    sellSymbol,
                    OrderSide.Sell,
                    NewOrderType.Market,
                    quantity: sellQuantity);
                var t3 = DateTime.UtcNow;

                var placeOrderLatency = (t3 - t2).TotalMilliseconds;
                FileLogger.LogOther($"[Latency] PlaceOrderAsync (Gate.io) execution time: {placeOrderLatency:F0}ms");

                if (sellOrderId.HasValue)
                {
                    _pendingSellOrderId = sellOrderId.Value;
                    _sellConfirmed = false;
                    FileLogger.LogOther($"[Y5] Sell order {sellOrderId.Value} placed on {_gateIoExchange.GetType().Name} at {t3:HH:mm:ss.fff}");
                    FileLogger.LogOther($"[Y5] Total time from handler entry to sell placed: {(t3 - t0).TotalMilliseconds:F0}ms");
                    FileLogger.LogOther("[Y5] MARKET complete. Waiting for WS confirmation...\n");
                }
                else
                {
                    FileLogger.LogOther($"[Y5] FAILED to place market sell order on {_gateIoExchange.GetType().Name}.");
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogOther($"[Y5] CRITICAL FAILURE placing sell order on {_gateIoExchange.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _sellLock.Release();
            }
        }

        private void HandleBuyBalanceUpdate(IBalance balance)
        {
            if (balance.Asset == _baseAsset)
            {
                FileLogger.LogOther($"[Balance Update] Asset: {balance.Asset}, Available: {balance.Available}");
                _lastReceivedBalance = balance.Available;
                // Reset the timer every time an update comes in
                _balanceDebounceTimer?.Change(150, Timeout.Infinite);
            }
        }

        private void OnBalanceDebounceTimer(object? state)
        {
            _balanceTcs.TrySetResult(_lastReceivedBalance);
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
                FileLogger.LogOther("\n[Y6] --- CONFIRM Phase ---");

                var sellFillServerTime = order.UpdateTime ?? order.CreateTime;
                var sellFillServerTimeStr = sellFillServerTime?.ToString("HH:mm:ss.fff") ?? "N/A";

                FileLogger.LogOther($"[Y6] Sell order update: OrderId={order.OrderId}, Status={order.Status}, FinishType={order.FinishType}");
                FileLogger.LogOther($"[Y6] Sell fill server time: {sellFillServerTimeStr}, Update received: {t0:HH:mm:ss.fff}");

                if (sellFillServerTime.HasValue)
                {
                    var wsLatency = (t0 - sellFillServerTime.Value).TotalMilliseconds;
                    FileLogger.LogOther($"[Latency] WS propagation delay (Gate.io fill -> handler): {wsLatency:F0}ms");
                }

                // Gate.io returns Status=Finish + FinishType=Filled
                if (order.Status == "Finish" && order.FinishType == "Filled")
                {
                    // Store the actual executed quantity from the sell order
                    _lastExecutedSellQuantity = order.Quantity;

                    FileLogger.LogOther($"[Y6] Sell order {order.OrderId} CONFIRMED filled on {_gateIoExchange.GetType().Name}!");

                    // Calculate end-to-end latency
                    if (_buyFilledServerTime.HasValue && sellFillServerTime.HasValue)
                    {
                        var serverToServerLatency = (sellFillServerTime.Value - _buyFilledServerTime.Value).TotalMilliseconds;
                        FileLogger.LogOther($"[Latency] END-TO-END (Bybit fill -> Gate.io fill) SERVER TIME: {serverToServerLatency:F0}ms");
                    }

                    if (_buyFilledLocalTime.HasValue)
                    {
                        var localEndToEnd = (t0 - _buyFilledLocalTime.Value).TotalMilliseconds;
                        FileLogger.LogOther($"[Latency] END-TO-END (Bybit handler -> Gate.io confirmation) LOCAL TIME: {localEndToEnd:F0}ms");
                    }

                    _sellConfirmed = true;
                    _pendingSellOrderId = null;

                    FileLogger.LogOther("[Y6] CONFIRM complete. Cycle complete.\n");

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
