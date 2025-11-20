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
        app.UseRouting(); // Enable routing
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

        // PROPOSAL-2025-0093: Create TWO independent channels instead of one shared
        // This fixes competing consumers bug where DataCollectorService and RollingWindowService
        // were reading from the same channel, each getting only ~50% of data
        var rawDataChannel = Channel.CreateBounded<MarketData>(channelOptions);
        var rollingWindowChannel = Channel.CreateBounded<MarketData>(channelOptions);

        services.AddSingleton<RawDataChannel>(new RawDataChannel(rawDataChannel));
        services.AddSingleton<RollingWindowChannel>(new RollingWindowChannel(rollingWindowChannel));
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

        // Task 0.5: Register ExchangeHealthMonitor
        services.AddSingleton<IExchangeHealthMonitor>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ExchangeHealthMonitor>>();
            return new ExchangeHealthMonitor(logger);
        });

        // Phase 1, Task 1.1: Register DeviationCalculator
        services.AddSingleton<DeviationCalculator>(sp =>
        {
            var minThreshold = configuration.GetValue<decimal>("Arbitrage:MinDeviationThreshold", 0.10m);
            return new DeviationCalculator(minThreshold);
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
                sp.GetRequiredService<IBidAskLogger>(),
                sp.GetRequiredService<IExchangeHealthMonitor>(), // Task 0.5
                sp.GetRequiredService<DeviationCalculator>() // Phase 1, Task 1.1
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
    private readonly RawDataChannel _rawDataChannel;
    private readonly ILogger<OrchestrationServiceHost> _logger;

    public OrchestrationServiceHost(
        OrchestrationService orchestrationService,
        RawDataChannel rawDataChannel,
        ILogger<OrchestrationServiceHost> logger)
    {
        _orchestrationService = orchestrationService;
        _rawDataChannel = rawDataChannel;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[OrchestrationHost] Starting orchestration service...");
        _ = _orchestrationService.StartAsync(cancellationToken);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // PROPOSAL-2025-0095: Graceful shutdown
        _logger.LogInformation("[OrchestrationHost] Stopping orchestration service gracefully...");

        // Stop orchestration (stops exchange subscriptions)
        await _orchestrationService.StopAsync(cancellationToken);

        // Complete raw data channel (signals consumers to stop)
        _rawDataChannel.Channel.Writer.Complete();

        _logger.LogInformation("[OrchestrationHost] Orchestration service stopped, channels completed");
    }
}

public class RollingWindowServiceHost : IHostedService
{
    private readonly RollingWindowService _rollingWindowService;
    private readonly RollingWindowChannel _rollingWindowChannel;
    private readonly ILogger<RollingWindowServiceHost> _logger;
    private Task? _runningTask;
    private CancellationTokenSource? _cts;

    public RollingWindowServiceHost(
        RollingWindowService rollingWindowService,
        RollingWindowChannel rollingWindowChannel,
        ILogger<RollingWindowServiceHost> logger)
    {
        _rollingWindowService = rollingWindowService;
        _rollingWindowChannel = rollingWindowChannel;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[RollingWindowHost] Starting rolling window service...");
        _cts = new CancellationTokenSource();
        _runningTask = _rollingWindowService.StartAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // PROPOSAL-2025-0095: Graceful shutdown
        _logger.LogInformation("[RollingWindowHost] Stopping rolling window service gracefully...");

        // Signal cancellation
        _cts?.Cancel();

        // Complete the channel to stop processing
        _rollingWindowChannel.Channel.Writer.Complete();

        // Wait for task to finish (with timeout)
        if (_runningTask != null)
        {
            var timeout = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            var completed = await Task.WhenAny(_runningTask, timeout);

            if (completed == timeout)
            {
                _logger.LogWarning("[RollingWindowHost] Rolling window service did not stop within 5 seconds");
            }
            else
            {
                _logger.LogInformation("[RollingWindowHost] Rolling window service stopped");
            }
        }

        _cts?.Dispose();
    }
}
