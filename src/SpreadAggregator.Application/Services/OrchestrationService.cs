using Microsoft.Extensions.Configuration;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Domain.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SpreadAggregator.Application.Services;

public class OrchestrationService
{
    private readonly IWebSocketServer _webSocketServer;
    private readonly SpreadCalculator _spreadCalculator;
    private readonly VolumeFilter _volumeFilter;
    private readonly IConfiguration _configuration;
    private readonly IEnumerable<IExchangeClient> _exchangeClients;
    private readonly SpreadDataCache _spreadDataCache;
    private Timer _broadcastTimer;

    public OrchestrationService(
        IWebSocketServer webSocketServer,
        SpreadCalculator spreadCalculator,
        IConfiguration configuration,
        VolumeFilter volumeFilter,
        IEnumerable<IExchangeClient> exchangeClients,
        SpreadDataCache spreadDataCache)
    {
        _webSocketServer = webSocketServer;
        _spreadCalculator = spreadCalculator;
        _configuration = configuration;
        _volumeFilter = volumeFilter;
        _exchangeClients = exchangeClients;
        _spreadDataCache = spreadDataCache;
    }

    public async Task StartAsync()
    {
        _webSocketServer.Start();

        _broadcastTimer = new Timer(BroadcastData, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

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

        await Task.WhenAll(tasks);
    }

    private async Task ProcessExchange(IExchangeClient exchangeClient, string exchangeName)
    {
        var minVolume = 1500000m;
        var maxVolume = 100000000000m;

        var tickers = (await exchangeClient.GetTickersAsync()).ToList();
        Console.WriteLine($"[{exchangeName}] Received {tickers.Count} tickers.");

        var filteredSymbols = tickers
            .Where(t => t.Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) && _volumeFilter.IsVolumeSufficient(t.QuoteVolume, minVolume, maxVolume))
            .Select(t => t.Symbol)
            .ToList();
        Console.WriteLine($"[{exchangeName}] {filteredSymbols.Count} symbols passed the volume filter.");

        if (!filteredSymbols.Any())
        {
            Console.WriteLine($"[{exchangeName}] No symbols to subscribe to after filtering.");
            return;
        }

        await exchangeClient.SubscribeToTickersAsync(filteredSymbols, async spreadData =>
        {
            if (spreadData.BestAsk == 0) return;

            spreadData.SpreadPercentage = _spreadCalculator.Calculate(spreadData.BestBid, spreadData.BestAsk);
            spreadData.MinVolume = minVolume;
            spreadData.MaxVolume = maxVolume;

            _spreadDataCache.Update(spreadData);
        });
    }

    private void BroadcastData(object state)
    {
        var allData = _spreadDataCache.GetAll();
        if (!allData.Any()) return;

        var package = new SpreadDataPackage();
        foreach (var data in allData)
        {
            package.Data.Add(new List<object>
            {
                data.Exchange,
                data.Symbol,
                data.BestBid,
                data.BestAsk,
                data.SpreadPercentage,
                data.MinVolume,
                data.MaxVolume
            });
        }

        var message = JsonSerializer.Serialize(package);
        _webSocketServer.BroadcastAsync(message).GetAwaiter().GetResult();
    }
}