using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using TraderBot.Exchanges.Bybit;

namespace TraderBot.ManualTests
{
    class BybitTrailingTest
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Bybit Trailing Test (Order Book + Modify) ===\n");

            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            var apiKey = configuration["Bybit:ApiKey"];
            var apiSecret = configuration["Bybit:ApiSecret"];

            Console.WriteLine($"API Key: {apiKey?.Substring(0, 10)}...\n");

            var ws = new BybitLowLatencyWs(apiKey, apiSecret);

            Console.WriteLine("Connecting to Bybit...");
            await ws.ConnectAsync();
            Console.WriteLine("Connected and authenticated!\n");

            // Subscribe to order updates
            Console.WriteLine("Subscribing to order updates...");
            await ws.SubscribeToOrderUpdatesAsync(order =>
            {
                Console.WriteLine($"[Order Update] OrderId={order.OrderId}, Status={order.Status}, Price={order.Price}");
            });

            // Test parameters
            var symbol = "HUSDT"; // Bybit format (no underscore)
            var tickSize = 0.00001m; // From Bybit symbol filters for H
            var dollarDepth = 25m; // $25 depth into the order book (like Gate.io)

            string? currentOrderId = null;
            decimal lastPlacedPrice = 0;

            Console.WriteLine($"=== Starting Trailing Test for {symbol} (BUY) ===");
            Console.WriteLine($"Dollar depth: ${dollarDepth}");
            Console.WriteLine($"TickSize: {tickSize}\n");

            // Subscribe to order book
            await ws.SubscribeToOrderBookAsync(symbol, async orderBook =>
            {
                try
                {
                    var bestBid = orderBook.Bids.FirstOrDefault()?.Price ?? 0;
                    if (bestBid == 0) return;

                    var targetPrice = BybitLowLatencyWs.CalculateTargetPriceForBuy(orderBook, dollarDepth, tickSize);

                    if (lastPlacedPrice > 0)
                    {
                        var diffPercent = ((targetPrice - lastPlacedPrice) / lastPlacedPrice * 100);
                        Console.WriteLine($"[Trail] bid={bestBid:F5}  tgt={targetPrice:F5}  last={lastPlacedPrice:F5}  diff%={diffPercent:F3}");
                    }

                    // Check if price changed enough
                    if (Math.Abs(targetPrice - lastPlacedPrice) < tickSize)
                    {
                        return;
                    }

                    if (currentOrderId == null)
                    {
                        // Place initial BUY limit order
                        Console.WriteLine($"\nBest Bid: {bestBid:F5}. Placing buy order at {targetPrice:F5}");

                        var testQuantity = 5m; // 5 H (minimum for Bybit spot)
                        currentOrderId = await ws.PlaceLimitOrderAsync(symbol, "Buy", testQuantity, targetPrice);

                        if (currentOrderId != null)
                        {
                            lastPlacedPrice = targetPrice;
                            Console.WriteLine($"  > Successfully placed order {currentOrderId} at price {targetPrice:F5}\n");
                        }
                        else
                        {
                            Console.WriteLine("  > Failed to place order.\n");
                        }
                    }
                    else
                    {
                        if (targetPrice == lastPlacedPrice) return;

                        // Modify existing order
                        Console.WriteLine($"\nPrice changed. Best Bid: {bestBid:F5}. Moving order to {targetPrice:F5}");

                        var modifyStart = DateTime.UtcNow;
                        var testQuantity = 5m; // 5 H
                        var success = await ws.ModifyOrderAsync(symbol, currentOrderId, targetPrice, testQuantity);
                        var modifyEnd = DateTime.UtcNow;
                        var modifyLatency = (modifyEnd - modifyStart).TotalMilliseconds;

                        if (success)
                        {
                            lastPlacedPrice = targetPrice;
                            Console.WriteLine($"  > Successfully modified order {currentOrderId} to price {targetPrice:F5}");
                            Console.WriteLine($"[Latency] ModifyOrderAsync execution time: {modifyLatency:F0}ms\n");
                        }
                        else
                        {
                            Console.WriteLine($"  > Failed to modify order. Time spent: {modifyLatency:F0}ms\n");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] {ex.Message}");
                }
            });

            Console.WriteLine("Listening for orderbook updates and trailing the spread...");
            Console.WriteLine("Press Ctrl+C to stop.\n");

            // Keep running
            await Task.Delay(Timeout.Infinite);
        }
    }
}
