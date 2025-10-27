using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using TraderBot.Exchanges.Bybit;
using TraderBot.Core;

namespace ManualTests
{
    class BybitLowLatencyTest
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Bybit Low-Latency WebSocket Test ===\n");

            // Load configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var apiKey = configuration["Bybit:ApiKey"];
            var apiSecret = configuration["Bybit:ApiSecret"];

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
            {
                Console.WriteLine("ERROR: Bybit API credentials not found in appsettings.json");
                return;
            }

            Console.WriteLine($"API Key: {apiKey[..8]}...");
            Console.WriteLine();

            // Create and connect
            var ws = new BybitLowLatencyWs(apiKey, apiSecret);

            Console.WriteLine("Connecting to Bybit...");
            await ws.ConnectAsync();
            Console.WriteLine("Connected and authenticated!\n");

            // Wait for authentication to complete
            await Task.Delay(1000);

            // Subscribe to order updates
            string? receivedOrderId = null;
            Console.WriteLine("Subscribing to order updates...");
            await ws.SubscribeToOrderUpdatesAsync(order =>
            {
                Console.WriteLine($"\n[ORDER UPDATE] OrderId={order.OrderId}, Symbol={order.Symbol}, Status={order.Status}, Price={order.Price}, Qty={order.Quantity}");
                receivedOrderId = order.OrderId.ToString();
            });

            await Task.Delay(2000);

            // Test parameters - using smaller alt coin for easier testing
            var symbol = "ETHUSDT";
            var testQuantity = 0.01m; // 0.01 ETH (minimum for Bybit)
            var currentPrice = 3500m; // Approximate ETH price
            var limitPrice = 1000m; // Way below market (won't fill)

            Console.WriteLine($"\n=== TEST 1: Place Limit Order ===");
            Console.WriteLine($"Symbol: {symbol}");
            Console.WriteLine($"Side: Buy");
            Console.WriteLine($"Quantity: {testQuantity} ETH");
            Console.WriteLine($"Price: ${limitPrice} (way below market, won't fill)");
            Console.WriteLine();

            var orderIdStr = await ws.PlaceLimitOrderAsync(symbol, "Buy", testQuantity, limitPrice);
            Console.WriteLine($"Order placed! Received OrderId: {orderIdStr}");

            // Wait for order update confirmation
            Console.WriteLine("\nWaiting for order update confirmation (5 seconds)...");
            await Task.Delay(5000);

            Console.WriteLine("\n=== TEST 2: Modify Order ===");

            if (!string.IsNullOrEmpty(orderIdStr))
            {
                var newPrice = limitPrice * 0.95m; // 5% lower
                Console.WriteLine($"Using OrderId: {orderIdStr}");
                Console.WriteLine($"Modifying order to new price: ${newPrice}");

                await ws.ModifyOrderAsync(symbol, orderIdStr, newPrice, testQuantity);
                Console.WriteLine("Modify request sent!");

                Console.WriteLine("\nWaiting for modify confirmation (5 seconds)...");
                await Task.Delay(5000);
            }
            else
            {
                Console.WriteLine("ERROR: No OrderId received from PlaceLimitOrderAsync");
            }

            Console.WriteLine("\n=== Test Complete ===");
            Console.WriteLine("Test will exit in 3 seconds...");
            await Task.Delay(3000);

            ws.Dispose();
        }
    }
}
