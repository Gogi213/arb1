using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using TraderBot.Core;
using TraderBot.Exchanges.Bybit;
using TraderBot.Exchanges.GateIo;

namespace TraderBot.Host
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var configs = configuration.GetSection("ExchangeConfigs").Get<List<ExchangeConfig>>();
            if (configs == null || configs.Count < 2)
            {
                Console.WriteLine("Please provide at least two exchange configurations in appsettings.json");
                return;
            }

            var gateIoConfig = configs.First(c => c.ExchangeName == "GateIo");
            var bybitConfig = configs.First(c => c.ExchangeName == "Bybit");

            var gateIoExchange = new GateIoExchange();
            await gateIoExchange.InitializeAsync(gateIoConfig.ApiKey, gateIoConfig.ApiSecret);

            var bybitExchange = new BybitExchange();
            await bybitExchange.InitializeAsync(bybitConfig.ApiKey, bybitConfig.ApiSecret);

            // Gate.io for buying, Bybit for selling
            var arbitrageTrader = new ArbitrageTrader(gateIoExchange, bybitExchange);

            // We use the symbol and amount from the buying exchange config
            await arbitrageTrader.StartAsync(gateIoConfig.Symbol, gateIoConfig.Amount, gateIoConfig.DurationMinutes);
            
            Console.WriteLine("\n--- Arbitrage cycle finished. Program exiting. ---");
        }
    }
}
