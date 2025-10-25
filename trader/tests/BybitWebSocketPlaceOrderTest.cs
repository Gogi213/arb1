using System;
using System.Threading;
using System.Threading.Tasks;
using Bybit.Net.Clients;
using Bybit.Net.Enums;
using CryptoExchange.Net.Authentication;

namespace TraderBot.Tests
{
    class BybitWebSocketPlaceOrderTest
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Bybit WebSocket PlaceOrder Test ===");
            Console.WriteLine();

            // Читаем API ключи из переменных окружения
            var apiKey = Environment.GetEnvironmentVariable("BYBIT_API_KEY");
            var apiSecret = Environment.GetEnvironmentVariable("BYBIT_API_SECRET");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
            {
                Console.WriteLine("ERROR: Set BYBIT_API_KEY and BYBIT_API_SECRET environment variables");
                return;
            }

            Console.WriteLine($"[1] API Key loaded: {apiKey.Substring(0, 5)}...");
            Console.WriteLine();

            // Создаем WebSocket клиента
            Console.WriteLine("[2] Creating BybitSocketClient...");
            var socketClient = new BybitSocketClient();
            Console.WriteLine("[2] BybitSocketClient created.");
            Console.WriteLine();

            // Устанавливаем credentials
            Console.WriteLine("[3] Setting API credentials...");
            socketClient.SetApiCredentials(new ApiCredentials(apiKey, apiSecret));
            Console.WriteLine("[3] API credentials set.");
            Console.WriteLine();

            // Подписываемся на order updates для подтверждения
            Console.WriteLine("[4] Subscribing to order updates...");
            var subscriptionResult = await socketClient.V5PrivateApi.SubscribeToOrderUpdatesAsync(data =>
            {
                Console.WriteLine($"[ORDER UPDATE] Received {data.Data.Length} order(s)");
                foreach (var order in data.Data)
                {
                    Console.WriteLine($"  - OrderId: {order.OrderId}, Symbol: {order.Symbol}, Status: {order.Status}, Side: {order.Side}");
                }
            });

            if (subscriptionResult.Success)
            {
                Console.WriteLine("[4] Successfully subscribed to order updates.");
            }
            else
            {
                Console.WriteLine($"[4] FAILED to subscribe to order updates: {subscriptionResult.Error}");
                return;
            }
            Console.WriteLine();

            // Даем время на подключение WS
            Console.WriteLine("[5] Waiting 2 seconds for WS to stabilize...");
            await Task.Delay(2000);
            Console.WriteLine("[5] Done waiting.");
            Console.WriteLine();

            // Размещаем MARKET ордер на продажу $2 ZKJUSDT
            var symbol = "ZKJUSDT";
            var side = OrderSide.Sell;
            var orderType = NewOrderType.Market;
            var quoteQuantity = 2m;

            Console.WriteLine($"[6] Placing Market Order:");
            Console.WriteLine($"    Symbol: {symbol}");
            Console.WriteLine($"    Side: {side}");
            Console.WriteLine($"    OrderType: {orderType}");
            Console.WriteLine($"    QuoteQuantity: {quoteQuantity}");
            Console.WriteLine();

            Console.WriteLine("[7] Calling V5PrivateApi.PlaceOrderAsync...");
            var startTime = DateTime.UtcNow;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                var placeOrderTask = socketClient.V5PrivateApi.PlaceOrderAsync(
                    Category.Spot,
                    symbol,
                    side,
                    orderType,
                    quantity: quoteQuantity,
                    price: null,
                    marketUnit: MarketUnit.QuoteAsset,
                    ct: cts.Token);

                Console.WriteLine("[7] PlaceOrderAsync task started. Waiting for result...");

                var result = await placeOrderTask;
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

                Console.WriteLine();
                Console.WriteLine($"[8] PlaceOrderAsync returned after {elapsed:F0}ms");
                Console.WriteLine($"    Success: {result.Success}");

                if (result.Success)
                {
                    Console.WriteLine($"    OrderId: {result.Data.OrderId}");
                    Console.WriteLine("    ✓ Order placed successfully!");
                }
                else
                {
                    Console.WriteLine($"    Error: {result.Error}");
                    Console.WriteLine($"    Error Code: {result.Error?.Code}");
                    Console.WriteLine($"    Error Message: {result.Error?.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                Console.WriteLine();
                Console.WriteLine($"[ERROR] PlaceOrderAsync TIMEOUT after {elapsed:F0}ms");
            }
            catch (Exception ex)
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                Console.WriteLine();
                Console.WriteLine($"[ERROR] PlaceOrderAsync threw exception after {elapsed:F0}ms:");
                Console.WriteLine($"    Type: {ex.GetType().Name}");
                Console.WriteLine($"    Message: {ex.Message}");
                Console.WriteLine($"    Stack: {ex.StackTrace}");
            }

            Console.WriteLine();
            Console.WriteLine("[9] Waiting 5 seconds for potential WS updates...");
            await Task.Delay(5000);

            Console.WriteLine();
            Console.WriteLine("[10] Unsubscribing from order updates...");
            await socketClient.UnsubscribeAllAsync();
            Console.WriteLine("[10] Unsubscribed.");

            Console.WriteLine();
            Console.WriteLine("=== Test Complete ===");
        }
    }
}
