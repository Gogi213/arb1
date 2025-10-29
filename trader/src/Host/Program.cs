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
                FileLogger.LogOther("Please provide at least two exchange configurations in appsettings.json");
                return;
            }

            var gateIoConfig = configs.First(c => c.ExchangeName == "GateIo");
            var bybitConfig = configs.First(c => c.ExchangeName == "Bybit");

            var gateIoExchange = new GateIoExchange();
            await gateIoExchange.InitializeAsync(gateIoConfig.ApiKey, gateIoConfig.ApiSecret);

            var bybitExchange = new BybitExchange();
            await bybitExchange.InitializeAsync(bybitConfig.ApiKey, bybitConfig.ApiSecret);

            // LEG 1: Gate.io (BUY limit trailing) -> Bybit (SELL market)
            var arbitrageTrader = new ArbitrageTrader(gateIoExchange, bybitExchange);
            await arbitrageTrader.StartAsync(gateIoConfig.Symbol, gateIoConfig.Amount, gateIoConfig.DurationMinutes);

            FileLogger.LogOther("\n[X7] --- LEG 1 (X1-X7) cycle finished ---");

            // LEG 2 (Y1-Y7): Bybit (BUY limit trailing) -> Gate.io (SELL market)
            FileLogger.LogOther("\n[Y1] --- Starting LEG 2 (Y1-Y7) ---");
            var bybitLowLatencyWs = new BybitLowLatencyWs(bybitConfig.ApiKey, bybitConfig.ApiSecret);
            await bybitLowLatencyWs.ConnectAsync();

            var reverseArbitrageTrader = new ReverseArbitrageTrader(bybitLowLatencyWs, gateIoExchange);
            await reverseArbitrageTrader.StartAsync(bybitConfig.Symbol, bybitConfig.Amount, bybitConfig.DurationMinutes);

            FileLogger.LogOther("\n--- Full process (X1-X7 + Y1-Y7) finished. Program exiting. ---");
        }
    }
}
