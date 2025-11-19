using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TraderBot.Core;
using TraderBot.Core.Configuration;
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

            // Default: Show usage
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run gate   - Run ConvergentTrader on GateIo");
            Console.WriteLine("  dotnet run bybit  - Run ConvergentTrader on Bybit");
            Console.WriteLine();
            Console.WriteLine("Note: Legacy two-legged arbitrage (DecisionMaker) has been removed.");
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

                FileLogger.LogOther("Setting up DI for Configuration...");
                var services = new ServiceCollection();
                services.AddOptions();
                services.Configure<TradingSettings>(configuration.GetSection("TradingSettings"));
                var serviceProvider = services.BuildServiceProvider();
                var settings = serviceProvider.GetRequiredService<IOptionsMonitor<TradingSettings>>();

                FileLogger.LogOther("Creating ConvergentTrader...");
                var trader = new ConvergentTrader(exchange, settings);
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
