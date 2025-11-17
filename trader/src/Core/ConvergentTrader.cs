using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TraderBot.Core
{
    public class ConvergentTrader : ITrader
    {
        private readonly IExchange _exchange;
        private readonly TrailingTrader _trailingTrader;
        private string? _symbol;
        private string? _baseAsset;
        private int _basePrecision;
        private decimal _amount;
        private readonly TaskCompletionSource<decimal> _cycleTcs = new();
        private ArbitrageCycleState _state; // Keep for compatibility, even if minimal

        // Balance handling like-before
        private TaskCompletionSource<decimal> _baseAssetBalanceTcs = new();
        private Timer? _baseAssetDebounceTimer;
        private decimal _lastReceivedBaseAssetBalance;

        public ConvergentTrader(IExchange exchange)
        {
            _exchange = exchange;
            _trailingTrader = new TrailingTrader(_exchange);
        }

        public async Task<decimal> StartAsync(string symbol, decimal amount, int durationMinutes, ArbitrageCycleState state)
        {
            _symbol = symbol;
            _baseAsset = symbol.Split('_')[0];
            _amount = amount;
            _state = state;

            FileLogger.LogOther($"--- Starting ConvergentTrader for {symbol} ---");
            FileLogger.LogOther($"Exchange: {_exchange.GetType().Name}");

            var filters = await _exchange.GetSymbolFiltersAsync(symbol);
            _basePrecision = (int)filters.basePrecision;

            // Cancel all open orders
            await _exchange.CancelAllOrdersAsync(symbol);
            FileLogger.LogOther("All open orders cancelled.");

            // Subscribe to updates
            _exchange.SubscribeToBalanceUpdatesAsync(HandleBaseAssetBalanceUpdate);
            _trailingTrader.OnOrderFilled += HandleBuyFilled;

            // Initialize debounce timer
            _baseAssetDebounceTimer = new Timer(OnBaseAssetBalanceDebounceTimer, null, Timeout.Infinite, Timeout.Infinite);

            // Start trailing buy
            await _trailingTrader.StartAsync(symbol, amount, durationMinutes);

            return await _cycleTcs.Task;
        }

        private async void HandleBuyFilled(IOrder filledOrder)
        {
            // Save bought quantity (for later sell)
            _state.Leg1GateBuyFilledQuantity = filledOrder.CumulativeQuantityFilled;
            FileLogger.LogOther($"[Convergent] Buy filled: {filledOrder.CumulativeQuantityFilled} {_baseAsset}");

            // Wait for balance update
            FileLogger.LogOther($"[Convergent] Waiting for balance update...");
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var actualAvailable = await _baseAssetBalanceTcs.Task.WaitAsync(cts.Token);
            _baseAssetBalanceTcs = new TaskCompletionSource<decimal>();

            FileLogger.LogOther($"[Convergent] Balance updated. Available: {actualAvailable}");

            // Wait 5 seconds as requested
            FileLogger.LogOther($"[Convergent] Waiting 5 seconds before market sell...");
            await Task.Delay(5000);

            // Place market sell
            FileLogger.LogOther($"[Convergent] Placing market sell for {actualAvailable} on {filledOrder.Symbol}");
            var sellQuantity = actualAvailable;

            try
            {
                var sellOrderId = await _exchange.PlaceOrderAsync(
                    symbol: filledOrder.Symbol,
                    side: OrderSide.Sell,
                    type: NewOrderType.Market,
                    quantity: sellQuantity
                );

                FileLogger.LogOther($"[Convergent] PlaceOrderAsync returned: {(sellOrderId.HasValue ? sellOrderId.Value.ToString() : "NULL")}");

                if (sellOrderId.HasValue)
                {
                    FileLogger.LogOther($"[Convergent] Market sell placed: {sellOrderId.Value}");
                    // In this simple version, assume market sell fills immediately
                    // For real implementation, would need order update subscription
                    await Task.Delay(1000); // Assume filled
                    var proceeds = sellQuantity * 0.98m; // Rough estimate (arbitrary for now)
                    _cycleTcs.TrySetResult(proceeds);
                }
                else
                {
                    FileLogger.LogOther($"[Convergent] FAILED to place sell order! (returned NULL)");
                    _cycleTcs.TrySetException(new Exception("Sell failed: OrderId is null"));
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogOther($"[Convergent] EXCEPTION placing sell order: {ex.Message}");
                _cycleTcs.TrySetException(new Exception($"Sell failed with exception: {ex.Message}", ex));
            }
        }

        private void HandleBaseAssetBalanceUpdate(IBalance balance)
        {
            FileLogger.LogOther($"[Convergent-Balance] Received update for {balance.Asset}: {balance.Available} (looking for {_baseAsset})");
            if (balance.Asset == _baseAsset)
            {
                FileLogger.LogOther($"[Convergent-Balance] MATCH! Setting debounce timer for {_baseAsset}");
                _lastReceivedBaseAssetBalance = balance.Available;
                _baseAssetDebounceTimer?.Change(150, Timeout.Infinite);
            }
        }

        private void OnBaseAssetBalanceDebounceTimer(object? state)
        {
            FileLogger.LogOther($"[Convergent-Balance] Debounce timer fired! Setting TCS result: {_lastReceivedBaseAssetBalance}");
            _baseAssetBalanceTcs.TrySetResult(_lastReceivedBaseAssetBalance);
        }
    }
}
