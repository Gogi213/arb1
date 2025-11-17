using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using TraderBot.Core;
using TraderBot.Exchanges.Bybit;
using TraderBot.Exchanges.GateIo;
using TraderBot.Core;

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

            if (args.Length > 0 && args[0] == "gate")
            {
                await RunManualConvergentTrader(configuration, "GateIo");
                return;
            }
            if (args.Length > 0 && args[0] == "bybit")
            {
                await RunManualConvergentTrader(configuration, "Bybit");
                return;
            }

            // --- Spread Listener Setup ---
            var spreadListenerUrl = configuration.GetValue<string>("SpreadListenerUrl");
            if (string.IsNullOrEmpty(spreadListenerUrl))
            {
                FileLogger.LogOther("SpreadListenerUrl is not configured in appsettings.json. Exiting.");
                return;
            }

            var listener = new SpreadListener(spreadListenerUrl);
            var decisionMaker = new DecisionMaker();
            decisionMaker.Subscribe(listener);

            FileLogger.LogOther("Starting spread listener. Waiting for profitable spread...");
            await listener.StartAsync(CancellationToken.None); // Now we await it, as it's the main loop

            FileLogger.LogOther("Spread listener finished. Program exiting.");
        }

        static async Task RunManualConvergentTrader(IConfiguration configuration, string exchangeName)
        {
            try
            {
                FileLogger.LogOther($"Initializing {exchangeName} trader...");

                var exchangeConfigs = configuration.GetSection("ExchangeConfigs").Get<List<ExchangeConfig>>();
                var config = exchangeConfigs.FirstOrDefault(c => c.ExchangeName == exchangeName);
                if (config == null)
                {
                    FileLogger.LogOther($"Exchange config for {exchangeName} not found.");
                    return;
                }

                IExchange exchange;
                if (exchangeName == "GateIo")
                {
                    FileLogger.LogOther("Creating GateIoExchange...");
                    exchange = new GateIoExchange();
                    FileLogger.LogOther("Initializing GateIoExchange...");
                    await exchange.InitializeAsync(config.ApiKey, config.ApiSecret);
                }
                else
                {
                    FileLogger.LogOther("Creating BybitExchange...");
                    exchange = new BybitExchange();
                    await ((BybitExchange)exchange).InitializeAsync(config.ApiKey, config.ApiSecret);
                }

                FileLogger.LogOther("Creating ConvergentTrader...");
                var trader = new ConvergentTrader(exchange);
                var state = new ArbitrageCycleState();

                FileLogger.LogOther($"Starting manual ConvergentTrader with amount: {config.Amount} for {config.Symbol}");
                var result = await trader.StartAsync(config.Symbol, config.Amount, config.DurationMinutes, state);
                FileLogger.LogOther($"ConvergentTrader completed with result: {result}");
            }
            catch (Exception ex)
            {
                var errorMsg = $"ERROR in RunManualConvergentTrader: {ex.Message}\n{ex.StackTrace}";
                FileLogger.LogOther(errorMsg);
                Console.WriteLine(errorMsg);
                throw;
            }
        }
    }
}
