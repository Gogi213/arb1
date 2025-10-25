using System;
using System.Threading;
using System.Threading.Tasks;
using Bybit.Net.Clients;
using Bybit.Net.Enums;
using CryptoExchange.Net.Authentication;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Bybit WebSocket PlaceOrder Test ===");
        Console.WriteLine();

        // API ключи
        var apiKey = "UVSbRqLBEY30RnPaiH";
        var apiSecret = "Fg45sn0nH4FhqZaxctj54Nf9cO03Qf9s0zds";

        Console.WriteLine($"[1] API Key: {apiKey.Substring(0, 5)}...");
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

        // Подписываемся на order updates
        Console.WriteLine("[4] Subscribing to order updates...");
        var subscriptionResult = await socketClient.V5PrivateApi.SubscribeToOrderUpdatesAsync(data =>
        {
            Console.WriteLine($"[ORDER UPDATE] Received {data.Data.Length} order(s)");
            foreach (var order in data.Data)
            {
                Console.WriteLine($"  - OrderId: {order.OrderId}, Symbol: {order.Symbol}, Status: {order.Status}, CreateTime: {order.CreateTime}, UpdateTime: {order.UpdateTime}");
            }
        });

        if (subscriptionResult.Success)
        {
            Console.WriteLine("[4] ✓ Successfully subscribed to order updates.");
        }
        else
        {
            Console.WriteLine($"[4] ✗ FAILED to subscribe: {subscriptionResult.Error}");
            return;
        }
        Console.WriteLine();

        // Ждем стабилизации WS
        Console.WriteLine("[5] Waiting 2 seconds for WS...");
        await Task.Delay(2000);
        Console.WriteLine("[5] Done.");
        Console.WriteLine();

        // Параметры ордера
        var symbol = "ZKJUSDT";
        var side = OrderSide.Sell;
        var orderType = NewOrderType.Market;
        var quoteQuantity = 5m; // Changed to 5 USD as requested

        Console.WriteLine($"[6] Order Parameters:");
        Console.WriteLine($"    Symbol: {symbol}");
        Console.WriteLine($"    Side: {side}");
        Console.WriteLine($"    Type: {orderType}");
        Console.WriteLine($"    QuoteQty: ${quoteQuantity}");
        Console.WriteLine();

        Console.WriteLine("[7] >>> Calling PlaceOrderAsync via WebSocket...");
        var startTime = DateTime.UtcNow;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            Console.WriteLine("[7.1] Creating PlaceOrderAsync task...");
            var placeOrderTask = socketClient.V5PrivateApi.PlaceOrderAsync(
                Category.Spot,
                symbol,
                side,
                orderType,
                quantity: quoteQuantity,
                price: null,
                marketUnit: MarketUnit.QuoteAsset,
                ct: cts.Token);

            Console.WriteLine("[7.2] Task created. Awaiting result...");

            var result = await placeOrderTask;
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

            Console.WriteLine();
            Console.WriteLine($"[8] <<< PlaceOrderAsync RETURNED after {elapsed:F0}ms");
            Console.WriteLine($"    Success: {result.Success}");

            if (result.Success)
            {
                Console.WriteLine($"    OrderId: {result.Data.OrderId}");
                Console.WriteLine("    ✓ ✓ ✓ ORDER PLACED SUCCESSFULLY!");
            }
            else
            {
                Console.WriteLine($"    ✗ Error: {result.Error}");
                if (result.Error != null)
                {
                   Console.WriteLine($"    --- DETAILED ERROR ---");
                   Console.WriteLine($"    Code: {result.Error.Code}");
                   Console.WriteLine($"    Message: {result.Error.Message}");
                   Console.WriteLine($"    ----------------------");
                }
            }
        }
        catch (OperationCanceledException)
        {
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            Console.WriteLine();
            Console.WriteLine($"[ERROR] ✗ ✗ ✗ TIMEOUT after {elapsed:F0}ms");
        }
        catch (Exception ex)
        {
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            Console.WriteLine();
            Console.WriteLine($"[ERROR] ✗ ✗ ✗ EXCEPTION after {elapsed:F0}ms:");
            Console.WriteLine($"    Type: {ex.GetType().Name}");
            Console.WriteLine($"    Message: {ex.Message}");
            Console.WriteLine($"    Stack: {ex.StackTrace}");
        }

        Console.WriteLine();
        Console.WriteLine("[9] Waiting 5 seconds for WS updates...");
        await Task.Delay(5000);

        Console.WriteLine();
        Console.WriteLine("[10] Unsubscribing...");
        await socketClient.UnsubscribeAllAsync();

        Console.WriteLine();
        Console.WriteLine("=== TEST COMPLETE ===");
    }
}
