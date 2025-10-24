using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Application.Services;
using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Domain.Services;
using SpreadAggregator.Infrastructure.Services;
using SpreadAggregator.Infrastructure.Services.Exchanges;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using BingX.Net.Interfaces.Clients;
using BingX.Net.Clients;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Clients;

namespace SpreadAggregator.Presentation;

class Program
{
    static async Task Main(string[] args)
    {
        await CreateHostBuilder(args).Build().RunAsync();
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, configuration) =>
            {
                configuration.Sources.Clear();
                var env = hostingContext.HostingEnvironment;
                configuration
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{env.EnvironmentName}.json", true, true);
            })
            .ConfigureLogging(logging =>
            {
                logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
                logging.AddFilter("BingX", LogLevel.Warning);
                logging.AddFilter("Bybit", LogLevel.Debug);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<IWebSocketServer>(sp =>
                {
                    var connectionString = context.Configuration.GetSection("ConnectionStrings")?["WebSocket"];
                    if (string.IsNullOrEmpty(connectionString))
                    {
                        throw new InvalidOperationException("WebSocket connection string is not configured.");
                    }
                    return new FleckWebSocketServer(connectionString);
                });

                services.AddSingleton<SpreadCalculator>();
                services.AddSingleton<VolumeFilter>();
                services.AddSingleton(Channel.CreateUnbounded<SpreadData>());
                services.AddSingleton(sp => sp.GetRequiredService<Channel<SpreadData>>().Reader);

                // Register all exchange clients
                services.AddSingleton<IExchangeClient, BinanceExchangeClient>();
                services.AddSingleton<IExchangeClient, MexcExchangeClient>();
                services.AddSingleton<IExchangeClient, GateIoExchangeClient>();
                services.AddSingleton<IExchangeClient, KucoinExchangeClient>();
                services.AddSingleton<IExchangeClient, OkxExchangeClient>();
                services.AddSingleton<IExchangeClient, BitgetExchangeClient>();
                services.AddSingleton<IExchangeClient, BingXExchangeClient>();
                services.AddSingleton<IExchangeClient, BybitExchangeClient>();

                services.AddBybit();

                // Регистрация IDataWriter
                services.AddSingleton<IDataWriter, ParquetDataWriter>();
                
                services.AddSingleton<OrchestrationService>();
                services.AddHostedService<OrchestrationServiceHost>();
                
                // Запускаем сборщик данных как отдельный, долгоживущий сервис
                services.AddHostedService<DataCollectorService>();
            });
}

public class OrchestrationServiceHost : IHostedService
{
    private readonly OrchestrationService _orchestrationService;

    public OrchestrationServiceHost(OrchestrationService orchestrationService)
    {
        _orchestrationService = orchestrationService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Не блокируем запуск, OrchestrationService сам управляет фоновыми задачами
        _ = _orchestrationService.StartAsync(cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Здесь можно добавить логику для грациозной остановки, если потребуется
        return Task.CompletedTask;
    }
}
