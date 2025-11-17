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
        app.UseStaticFiles(); // Serve static files from wwwroot
        app.UseWebSockets();
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
            FullMode = BoundedChannelFullMode.DropOldest
        };
        var sharedChannel = Channel.CreateBounded<MarketData>(channelOptions);
        services.AddSingleton<RawDataChannel>(new RawDataChannel(sharedChannel));
        services.AddSingleton<RollingWindowChannel>(new RollingWindowChannel(sharedChannel));
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

        // BidAsk Logger
        services.AddSingleton<IBidAskLogger>(sp =>
        {
            var logDirectory = configuration.GetValue<string>("Logging:BidAskLogDirectory")
                ?? Path.Combine("..", "..", "logs");
            return new BidAskLogger(
                sp.GetRequiredService<ILogger<BidAskLogger>>(),
                logDirectory
            );
        });

        // BidBid Logger (chart data logger)
        services.AddSingleton<IBidBidLogger>(sp =>
        {
            var logDirectory = configuration.GetValue<string>("Logging:BidAskLogDirectory")
                ?? Path.Combine("..", "..", "logs");
            return new BidBidLogger(
                sp.GetRequiredService<ILogger<BidBidLogger>>(),
                logDirectory
            );
        });

        services.AddSingleton<RollingWindowService>(sp =>
        {
            var rollingChannel = sp.GetRequiredService<RollingWindowChannel>().Channel;
            var bidBidLogger = sp.GetRequiredService<IBidBidLogger>();
            var logger = sp.GetRequiredService<ILogger<RollingWindowService>>();
            return new RollingWindowService(rollingChannel, bidBidLogger, logger);
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
                sp.GetRequiredService<IDataWriter>(),
                sp.GetRequiredService<IBidAskLogger>()
            );
        });

        // Chart API Services
        services.AddSingleton<SpreadAggregator.Infrastructure.Services.Charts.ParquetReaderService>(sp =>
        {
            var dataLakePath = configuration.GetValue<string>("DataLake:Path")
                ?? @"data/market_data";
            return new SpreadAggregator.Infrastructure.Services.Charts.ParquetReaderService(
                dataLakePath,
                sp.GetRequiredService<ILogger<SpreadAggregator.Infrastructure.Services.Charts.ParquetReaderService>>()
            );
        });

        services.AddSingleton<SpreadAggregator.Infrastructure.Services.Charts.OpportunityFilterService>(sp =>
        {
            var analyzerStatsPath = configuration.GetValue<string>("Analyzer:StatsPath")
                ?? @"analyzer/summary_stats";
            return new SpreadAggregator.Infrastructure.Services.Charts.OpportunityFilterService(
                analyzerStatsPath,
                sp.GetRequiredService<ILogger<SpreadAggregator.Infrastructure.Services.Charts.OpportunityFilterService>>()
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
