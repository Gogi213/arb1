using Microsoft.Extensions.Configuration;
using TraderBot.Exchanges.Bybit;
using TraderBot.Host;

namespace BybitWsTest;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("--- Bybit Low Latency WebSocket Test ---");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var bybitConfig = configuration.GetSection("ExchangeConfigs").Get<List<ExchangeConfig>>()
            .First(c => c.ExchangeName == "Bybit");

        if (string.IsNullOrEmpty(bybitConfig.ApiKey) || string.IsNullOrEmpty(bybitConfig.ApiSecret))
        {
            Console.WriteLine("API Key or Secret not found in appsettings.json");
            return;
        }

        Console.WriteLine("Configuration loaded. Initializing WebSocket client...");

        var wsClient = new BybitLowLatencyWs(bybitConfig.ApiKey, bybitConfig.ApiSecret);

        await wsClient.ConnectAsync();

        Console.WriteLine("Test client running. Press Enter to exit.");
        Console.ReadLine();
        
        wsClient.Dispose();
    }
}
