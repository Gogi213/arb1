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

    private readonly List<SymbolInfo> _allSymbolInfo = new();
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
        IDataWriter? dataWriter = null)
    {
        _webSocketServer = webSocketServer;
        _spreadCalculator = spreadCalculator;
        _configuration = configuration;
        _volumeFilter = volumeFilter;
        _exchangeClients = exchangeClients;
        _rawDataChannel = rawDataChannel;
        _rollingWindowChannel = rollingWindowChannel;
        _dataWriter = dataWriter;
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

            tasks.Add(ProcessExchange(exchangeClient, exchangeName));
        }

        // Do not await long-running tasks, let them run in the background.
        // await Task.WhenAll(tasks);
    }

    private async Task ProcessExchange(IExchangeClient exchangeClient, string exchangeName)
    {
        var exchangeConfig = _configuration.GetSection($"ExchangeSettings:Exchanges:{exchangeName}:VolumeFilter");
        var minVolume = exchangeConfig.GetValue<decimal?>("MinUsdVolume") ?? 0;
        var maxVolume = exchangeConfig.GetValue<decimal?>("MaxUsdVolume") ?? decimal.MaxValue;

        var allSymbols = (await exchangeClient.GetSymbolsAsync()).ToList();
        _allSymbolInfo.AddRange(allSymbols);
        
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
                    Timestamp = DateTime.UtcNow
                };
                await _rawDataChannel.Writer.WriteAsync(normalizedSpreadData);
                await _rollingWindowChannel.Writer.WriteAsync(normalizedSpreadData);
                var wrapper = new WebSocketMessage { MessageType = "Spread", Payload = normalizedSpreadData };
                var message = JsonSerializer.Serialize(wrapper);
                await _webSocketServer.BroadcastRealtimeAsync(message);
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
                await _webSocketServer.BroadcastRealtimeAsync(message);
            }));
        }

        Console.WriteLine($"[{exchangeName}] Awaiting {tasks.Count} subscription tasks...");
        try
        {
            // Do not await long-running tasks, let them run in the background.
            // await Task.WhenAll(tasks);
            Console.WriteLine($"[{exchangeName}] All subscription tasks completed successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] [{exchangeName}] Subscription failed: {ex}");
            throw;
        }
    }

}
