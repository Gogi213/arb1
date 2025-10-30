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
        private readonly TaskCompletionSource<decimal> _arbitrageCycleTcs = new();
        private string? _symbol;
        private string? _baseAsset;
        private string? _quoteAsset;
        private DateTime? _buyFilledServerTime;
        private DateTime? _buyFilledLocalTime;
        private int _sellBasePrecision;
        private ArbitrageCycleState _state;
        
        // For Base Asset (e.g., H)
        private TaskCompletionSource<decimal> _baseAssetBalanceTcs = new();
        private Timer? _baseAssetDebounceTimer;
        private decimal _lastReceivedBaseAssetBalance;

        // For Quote Asset (e.g., USDT)
        private TaskCompletionSource<decimal> _quoteAssetBalanceTcs = new();
        private Timer? _quoteAssetDebounceTimer;
        private decimal _lastReceivedQuoteAssetBalance;

        public ArbitrageTrader(IExchange buyExchange, IExchange sellExchange)
        {
            _buyExchange = buyExchange ?? throw new ArgumentNullException(nameof(buyExchange));
            _sellExchange = sellExchange ?? throw new ArgumentNullException(nameof(sellExchange));
            _trailingTrader = new TrailingTrader(_buyExchange);
        }

        public async Task<decimal> StartAsync(string symbol, decimal amount, int durationMinutes, ArbitrageCycleState state)
        {
            _symbol = symbol;
            _baseAsset = symbol.Split('_')[0];
            _quoteAsset = symbol.Split('_')[1];
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
            _buyExchange.SubscribeToBalanceUpdatesAsync(HandleBaseAssetBalanceUpdate);
            _sellExchange.SubscribeToBalanceUpdatesAsync(HandleQuoteAssetBalanceUpdate);

            // Subscribe to fill events from the TrailingTrader
            _trailingTrader.OnOrderFilled += HandleBuyOrderFilled;

            // Initialize timers here to avoid race conditions
            _baseAssetDebounceTimer = new Timer(OnBaseAssetBalanceDebounceTimer, null, Timeout.Infinite, Timeout.Infinite);
            _quoteAssetDebounceTimer = new Timer(OnQuoteAssetBalanceDebounceTimer, null, Timeout.Infinite, Timeout.Infinite);

            // Start the buying process on the buyExchange (don't await it here)
            _trailingTrader.StartAsync(symbol, amount, durationMinutes);

            return await _arbitrageCycleTcs.Task;
        }

        private async Task CleanupAndSignalCompletionAsync(decimal finalAmount)
        {
            if (_symbol == null) return;
            FileLogger.LogOther("[Arbitrage] Cleanup started...");
            await _trailingTrader.StopAsync(_symbol);
            await _sellExchange.UnsubscribeAsync();
            _baseAssetDebounceTimer?.Dispose();
            _quoteAssetDebounceTimer?.Dispose();
            await _buyExchange.UnsubscribeAsync();
            FileLogger.LogOther("[Arbitrage] Cleanup finished.");
            _arbitrageCycleTcs.TrySetResult(finalAmount); // Signal that the entire cycle is complete
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
                FileLogger.LogOther($"[Arbitrage] Buy fill server time: {buyFillServerTimeStr}, Handler entered: {t0:HH:mm:ss.fff}");
                
                // --- NEW LOGIC: Wait for the actual balance update ---

                FileLogger.LogOther($"[Arbitrage] Waiting for balance update for asset '{_baseAsset}'...");
                
                var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var actualQuantity = await _baseAssetBalanceTcs.Task.WaitAsync(cancellationTokenSource.Token);
                _baseAssetBalanceTcs = new TaskCompletionSource<decimal>(); // Re-create for next cycle
                
                FileLogger.LogOther($"[Arbitrage] Balance update received. Actual available quantity: {actualQuantity}");
                _state.GateIoLeg1BuyQuantity = actualQuantity;
                // --- END NEW LOGIC ---

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

                var sellSymbol = filledOrder.Symbol.Replace("_", "");

                // Round the quantity according to the sell exchange's rules
                // Use the precise quantity from the balance update
               var sellQuantity = Math.Round(actualQuantity, _sellBasePrecision);
               FileLogger.LogOther($"[Arbitrage] Original buy quantity from balance: {actualQuantity}, rounded sell quantity: {sellQuantity}");

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

       private void HandleBaseAssetBalanceUpdate(IBalance balance)
        {
            if (balance.Asset == _baseAsset)
            {
                FileLogger.LogOther($"[Balance Update] Asset: {balance.Asset}, Available: {balance.Available}");
               _lastReceivedBaseAssetBalance = balance.Available;
               // Reset the timer every time an update comes in
               _baseAssetDebounceTimer?.Change(150, Timeout.Infinite);
            }
        }

        private void OnBaseAssetBalanceDebounceTimer(object? state)
        {
            _baseAssetBalanceTcs.TrySetResult(_lastReceivedBaseAssetBalance);
        }

        private void HandleQuoteAssetBalanceUpdate(IBalance balance)
        {
            if (balance.Asset == _quoteAsset)
            {
                FileLogger.LogOther($"[Balance Update] Asset: {balance.Asset}, Available: {balance.Available}");
                _lastReceivedQuoteAssetBalance = balance.Available;
                // Reset the timer every time an update comes in
                _quoteAssetDebounceTimer?.Change(150, Timeout.Infinite);
            }
        }

        private void OnQuoteAssetBalanceDebounceTimer(object? state)
        {
            _quoteAssetBalanceTcs.TrySetResult(_lastReceivedQuoteAssetBalance);
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

                    var usdtProceeds = order.CumulativeQuoteQuantity;
                    FileLogger.LogOther($"[Arbitrage] Sale executed. USDT proceeds from order.CumulativeQuoteQuantity: {usdtProceeds}");

                    FileLogger.LogOther("[Arbitrage] Cycle complete.");
                    await CleanupAndSignalCompletionAsync(usdtProceeds);
                }
            }
            finally
            {
                _sellLock.Release();
            }
        }
    }
}
