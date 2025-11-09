using Microsoft.Extensions.Configuration;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Domain.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.IO;

namespace SpreadAggregator.Application.Services;

public class OrchestrationService
{
    private readonly IWebSocketServer _webSocketServer;
    private readonly SpreadCalculator _spreadCalculator;
    private readonly VolumeFilter _volumeFilter;
    private readonly IConfiguration _configuration;
    private readonly IEnumerable<IExchangeClient> _exchangeClients;
    private readonly Channel<MarketData> _rawDataChannel;
    private readonly Channel<MarketData> _rollingWindowChannel;
    private readonly IDataWriter? _dataWriter;
    private readonly IBidAskLogger? _bidAskLogger;

    private readonly List<SymbolInfo> _allSymbolInfo = new();
    private readonly List<Task> _exchangeTasks = new();
    public IEnumerable<SymbolInfo> AllSymbolInfo => _allSymbolInfo;

    public ChannelReader<MarketData> RawDataChannelReader => _rawDataChannel.Reader;
    public ChannelReader<MarketData> RollingWindowChannelReader => _rollingWindowChannel.Reader;

    public OrchestrationService(
        IWebSocketServer webSocketServer,
        SpreadCalculator spreadCalculator,
        IConfiguration configuration,
        VolumeFilter volumeFilter,
        IEnumerable<IExchangeClient> exchangeClients,
        Channel<MarketData> rawDataChannel,
        Channel<MarketData> rollingWindowChannel,
        IDataWriter? dataWriter = null,
        IBidAskLogger? bidAskLogger = null)
    {
        _webSocketServer = webSocketServer;
        _spreadCalculator = spreadCalculator;
        _configuration = configuration;
        _volumeFilter = volumeFilter;
        _exchangeClients = exchangeClients;
        _rawDataChannel = rawDataChannel;
        _rollingWindowChannel = rollingWindowChannel;
        _dataWriter = dataWriter;
        _bidAskLogger = bidAskLogger;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _webSocketServer.Start();


        var exchangeNames = _configuration.GetSection("ExchangeSettings:Exchanges").GetChildren().Select(x => x.Key);
        var tasks = new List<Task>();

        foreach (var exchangeName in exchangeNames)
        {
            var exchangeClient = _exchangeClients.FirstOrDefault(c => c.ExchangeName.Equals(exchangeName, StringComparison.OrdinalIgnoreCase));
            if (exchangeClient == null)
            {
                Console.WriteLine($"[ERROR] No client found for exchange: {exchangeName}");
                continue;
            }

            var task = ProcessExchange(exchangeClient, exchangeName);
            tasks.Add(task);
            _exchangeTasks.Add(task); // Store for monitoring/cleanup
        }

        // Store tasks but don't await - they are long-running background subscriptions
        Console.WriteLine($"[Orchestration] Started {_exchangeTasks.Count} exchange subscription tasks");
    }

    private async Task ProcessExchange(IExchangeClient exchangeClient, string exchangeName)
    {
        var exchangeConfig = _configuration.GetSection($"ExchangeSettings:Exchanges:{exchangeName}:VolumeFilter");
        var minVolume = exchangeConfig.GetValue<decimal?>("MinUsdVolume") ?? 0;
        var maxVolume = exchangeConfig.GetValue<decimal?>("MaxUsdVolume") ?? decimal.MaxValue;

        var allSymbols = (await exchangeClient.GetSymbolsAsync()).ToList();
        var existingSymbols = new HashSet<string>(_allSymbolInfo.Select(s => s.Name));
        var newSymbols = allSymbols.Where(s => !existingSymbols.Contains(s.Name)).ToList();
        _allSymbolInfo.AddRange(newSymbols);
        
        var tickers = (await exchangeClient.GetTickersAsync()).ToList();
        Console.WriteLine($"[{exchangeName}] Received {tickers.Count} tickers and {allSymbols.Count} symbol info objects.");

        var tickerLookup = tickers.ToDictionary(t => t.Symbol, t => t.QuoteVolume);

        var filteredSymbolInfo = allSymbols
            .Where(s => tickerLookup.ContainsKey(s.Name) &&
                        (s.Name.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) || s.Name.EndsWith("USDC", StringComparison.OrdinalIgnoreCase)) &&
                        _volumeFilter.IsVolumeSufficient(tickerLookup[s.Name], minVolume, maxVolume))
            .ToList();
        
        var filteredSymbolNames = filteredSymbolInfo.Select(s => s.Name).ToList();
        
        Console.WriteLine($"[{exchangeName}] {filteredSymbolNames.Count} symbols passed the volume filter.");

        if (!filteredSymbolNames.Any())
        {
            Console.WriteLine($"[{exchangeName}] No symbols to subscribe to after filtering.");
            return;
        }

        var tasks = new List<Task>();
        var enableTickers = _configuration.GetValue<bool>("StreamSettings:EnableTickers", true);
        var enableTrades = _configuration.GetValue<bool>("StreamSettings:EnableTrades", true);

        if (enableTickers)
        {
            Console.WriteLine($"[{exchangeName}] Adding ticker subscription task...");
            tasks.Add(exchangeClient.SubscribeToTickersAsync(filteredSymbolNames, async spreadData =>
            {
                if (spreadData.BestAsk == 0) return;

                var localTimestamp = DateTime.UtcNow;
                var normalizedSymbol = spreadData.Symbol.Replace("/", "").Replace("-", "").Replace("_", "").Replace(" ", "");
                var normalizedSpreadData = new SpreadData
                {
                    Exchange = spreadData.Exchange,
                    Symbol = normalizedSymbol,
                    BestBid = spreadData.BestBid,
                    BestAsk = spreadData.BestAsk,
                    SpreadPercentage = _spreadCalculator.Calculate(spreadData.BestBid, spreadData.BestAsk),
                    MinVolume = minVolume,
                    MaxVolume = maxVolume,
                    Timestamp = localTimestamp,
                    ServerTimestamp = spreadData.ServerTimestamp
                };

                // Log bid/ask with both server and local timestamps (non-blocking)
                _bidAskLogger?.LogAsync(normalizedSpreadData, localTimestamp);

                await _rawDataChannel.Writer.WriteAsync(normalizedSpreadData);
                await _rollingWindowChannel.Writer.WriteAsync(normalizedSpreadData);
                var wrapper = new WebSocketMessage { MessageType = "Spread", Payload = normalizedSpreadData };
                var message = JsonSerializer.Serialize(wrapper);
                try
                {
                    await _webSocketServer.BroadcastRealtimeAsync(message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Orchestration] Failed to broadcast spread data: {ex.Message}");
                }
            }));
        }

        if (enableTrades)
        {
            Console.WriteLine($"[{exchangeName}] Adding trade subscription task...");
            tasks.Add(exchangeClient.SubscribeToTradesAsync(filteredSymbolNames, async tradeData =>
            {
                await _rawDataChannel.Writer.WriteAsync(tradeData);
                await _rollingWindowChannel.Writer.WriteAsync(tradeData);
                var wrapper = new WebSocketMessage { MessageType = "Trade", Payload = tradeData };
                var message = JsonSerializer.Serialize(wrapper);
                try
                {
                    await _webSocketServer.BroadcastRealtimeAsync(message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Orchestration] Failed to broadcast trade data: {ex.Message}");
                }
            }));
        }

        Console.WriteLine($"[{exchangeName}] Awaiting {tasks.Count} subscription tasks...");

        // These are long-running subscriptions - await them to handle errors properly
        await Task.WhenAll(tasks);

        Console.WriteLine($"[{exchangeName}] All subscription tasks completed");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[Orchestration] Stopping {_exchangeTasks.Count} exchange tasks...");

        // Give tasks a chance to complete gracefully
        var completedTask = await Task.WhenAny(Task.WhenAll(_exchangeTasks), Task.Delay(5000, cancellationToken));

        if (completedTask == Task.WhenAll(_exchangeTasks))
        {
            Console.WriteLine("[Orchestration] All tasks stopped gracefully");
        }
        else
        {
            Console.WriteLine("[Orchestration] Tasks did not complete in 5 seconds, forcing shutdown");
        }
    }

}
