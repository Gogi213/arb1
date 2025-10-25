using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TraderBot.Core;
using TraderBot.Exchanges.Bybit;
using TraderBot.Exchanges.GateIo;

namespace TraderBot.Host
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var configs = new List<ExchangeConfig>
            {
                new ExchangeConfig
                {
                    Exchange = new GateIoExchange(),
                    ApiKey = "db8399ac605fcb256463d7aa7e110748",
                    ApiSecret = "fd3e1bf1714fb2c8b1478169a829fcd9b275658c80cebc9a3b544e57e3fd3f25",
                    Symbol = "ZKJ_USDT",
                    Amount = 3.1m,
                    DurationMinutes = 60
                }
            };

            var tasks = new List<Task>();
            foreach (var config in configs)
            {
                tasks.Add(RunTraderAsync(config));
            }

            await Task.WhenAll(tasks);
        }

        static async Task RunTraderAsync(ExchangeConfig config)
        {
            Console.WriteLine($"--- Starting Trader for {config.Exchange.GetType().Name} on {config.Symbol} ---");
            await config.Exchange.InitializeAsync(config.ApiKey, config.ApiSecret);
            ITrader trader = new TrailingTrader(config.Exchange);
            await trader.StartAsync(config.Symbol, config.Amount, config.DurationMinutes);
        }
    }
}
