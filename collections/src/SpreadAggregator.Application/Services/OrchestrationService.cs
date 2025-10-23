using Microsoft.Extensions.Configuration;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Application.Abstractions;
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
    private readonly Channel<SpreadData> _rawDataChannel;
    private readonly IDataWriter _dataWriter;

    public ChannelReader<SpreadData> RawDataChannelReader => _rawDataChannel.Reader;

    public OrchestrationService(
        IWebSocketServer webSocketServer,
        SpreadCalculator spreadCalculator,
        IConfiguration configuration,
        VolumeFilter volumeFilter,
        IEnumerable<IExchangeClient> exchangeClients,
        Channel<SpreadData> rawDataChannel,
        IDataWriter dataWriter = null)
    {
        _webSocketServer = webSocketServer;
        _spreadCalculator = spreadCalculator;
        _configuration = configuration;
        _volumeFilter = volumeFilter;
        _exchangeClients = exchangeClients;
        _rawDataChannel = rawDataChannel;
        _dataWriter = dataWriter;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _webSocketServer.Start();

        var recordingEnabled = _configuration.GetValue<bool>("Recording:Enabled");
        if (recordingEnabled && _dataWriter != null)
        {
            _ = _dataWriter.InitializeCollectorAsync(cancellationToken);
        }

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
        var minVolume = 2000000m;
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

            // Публикуем данные в канал
            await _rawDataChannel.Writer.WriteAsync(normalizedSpreadData);

            // Временная отправка сырых данных в существующий WebSocket для обратной совместимости с python-клиентом
            var message = JsonSerializer.Serialize(normalizedSpreadData);
            _webSocketServer.BroadcastRealtimeAsync(message);
        });
    }

}