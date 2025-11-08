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

public class RawDataChannel
{
    public Channel<MarketData> Channel { get; }
    public RawDataChannel(Channel<MarketData> channel) => Channel = channel;
}

public class RollingWindowChannel
{
    public Channel<MarketData> Channel { get; }
    public RollingWindowChannel(Channel<MarketData> channel) => Channel = channel;
}

class Program
{
    static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure logging
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        builder.Logging.AddFilter("BingX", LogLevel.Warning);
        builder.Logging.AddFilter("Bybit", LogLevel.Debug);

        // Configure application services
        ConfigureServices(builder.Services, builder.Configuration);

        // Add ASP.NET Core services for Charts API
        builder.Services.AddControllers();
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        var app = builder.Build();

        // Configure middleware
        app.UseCors();
        app.MapControllers();

        // Start background services
        await app.RunAsync();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IWebSocketServer>(sp =>
        {
            var connectionString = configuration.GetSection("ConnectionStrings")?["WebSocket"];
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("WebSocket connection string is not configured.");
            }
            return new FleckWebSocketServer(connectionString, () => sp.GetRequiredService<OrchestrationService>());
        });

        services.AddSingleton<SpreadCalculator>();
        services.AddSingleton<VolumeFilter>();

        var channelOptions = new BoundedChannelOptions(100_000)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        services.AddSingleton<RawDataChannel>(new RawDataChannel(Channel.CreateBounded<MarketData>(channelOptions)));
        services.AddSingleton<RollingWindowChannel>(new RollingWindowChannel(Channel.CreateBounded<MarketData>(channelOptions)));
        services.AddSingleton(sp => sp.GetRequiredService<RawDataChannel>().Channel.Reader);

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
        services.AddSingleton<IDataWriter>(sp =>
        {
            var rawChannel = sp.GetRequiredService<RawDataChannel>().Channel;
            var config = sp.GetRequiredService<IConfiguration>();
            return new ParquetDataWriter(rawChannel, config);
        });

        services.AddSingleton<RollingWindowService>(sp =>
        {
            var rollingChannel = sp.GetRequiredService<RollingWindowChannel>().Channel;
            return new RollingWindowService(rollingChannel);
        });

        services.AddSingleton<OrchestrationService>(sp =>
        {
            var rawChannel = sp.GetRequiredService<RawDataChannel>().Channel;
            var rollingChannel = sp.GetRequiredService<RollingWindowChannel>().Channel;
            return new OrchestrationService(
                sp.GetRequiredService<IWebSocketServer>(),
                sp.GetRequiredService<SpreadCalculator>(),
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<VolumeFilter>(),
                sp.GetRequiredService<IEnumerable<IExchangeClient>>(),
                rawChannel,
                rollingChannel,
                sp.GetRequiredService<IDataWriter>()
            );
        });

        services.AddHostedService<OrchestrationServiceHost>();
        services.AddHostedService<DataCollectorService>();
        services.AddHostedService<RollingWindowServiceHost>();
    }
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
        _ = _orchestrationService.StartAsync(cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public class RollingWindowServiceHost : IHostedService
{
    private readonly RollingWindowService _rollingWindowService;

    public RollingWindowServiceHost(RollingWindowService rollingWindowService)
    {
        _rollingWindowService = rollingWindowService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = _rollingWindowService.StartAsync(cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
