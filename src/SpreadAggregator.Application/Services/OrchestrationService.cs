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
using System.IO.Compression;

namespace SpreadAggregator.Application.Services;

public class OrchestrationService
{
    private readonly IWebSocketServer _webSocketServer;
    private readonly SpreadCalculator _spreadCalculator;
    private readonly VolumeFilter _volumeFilter;
    private readonly IConfiguration _configuration;
    private readonly IEnumerable<IExchangeClient> _exchangeClients;
    private readonly Channel<SpreadData> _rawDataChannel;

    public ChannelReader<SpreadData> RawDataChannelReader => _rawDataChannel.Reader;

    public OrchestrationService(
        IWebSocketServer webSocketServer,
        SpreadCalculator spreadCalculator,
        IConfiguration configuration,
        VolumeFilter volumeFilter,
        IEnumerable<IExchangeClient> exchangeClients,
        Channel<SpreadData> rawDataChannel)
    {
        _webSocketServer = webSocketServer;
        _spreadCalculator = spreadCalculator;
        _configuration = configuration;
        _volumeFilter = volumeFilter;
        _exchangeClients = exchangeClients;
        _rawDataChannel = rawDataChannel;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _webSocketServer.Start();

        var recordingEnabled = _configuration.GetValue<bool>("Recording:Enabled");
        if (recordingEnabled)
        {
            _ = StartDataCollectorAsync(cancellationToken);
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

            spreadData.SpreadPercentage = _spreadCalculator.Calculate(spreadData.BestBid, spreadData.BestAsk);
            spreadData.MinVolume = minVolume;
            spreadData.MaxVolume = maxVolume;

            spreadData.Timestamp = DateTime.UtcNow;

            // Публикуем данные в канал
            await _rawDataChannel.Writer.WriteAsync(spreadData);

            // Временная отправка сырых данных в существующий WebSocket для обратной совместимости с python-клиентом
            var message = JsonSerializer.Serialize(spreadData);
            _webSocketServer.BroadcastRealtimeAsync(message);
        });
    }

    private async Task StartDataCollectorAsync(CancellationToken cancellationToken)
    {
        var sessionDirectory = Path.Combine("data", "market_data", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        Directory.CreateDirectory(sessionDirectory);

        Console.WriteLine($"[DataCollector] Starting to record data into: {sessionDirectory}");
        await foreach (var data in _rawDataChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                var exchangeDir = Path.Combine(sessionDirectory, data.Exchange);
                var symbolDir = Path.Combine(exchangeDir, data.Symbol);
                Directory.CreateDirectory(symbolDir);

                var filePath = Path.Combine(symbolDir, "order_book_updates.csv.gz");
                var fileExists = File.Exists(filePath);

                // Используем GZipStream для сжатия "на лету"
                await using var fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None);
                await using var gzipStream = new GZipStream(fileStream, CompressionMode.Compress);
                await using var writer = new StreamWriter(gzipStream);

                // Если файл новый, записать заголовки
                if (!fileExists)
                {
                    await writer.WriteLineAsync("Timestamp,BestBid,BestAsk,SpreadPercentage,MinVolume,MaxVolume");
                }

                // Сформировать и записать строку CSV
                var csvLine = $"{data.Timestamp:o},{data.BestBid},{data.BestAsk},{data.SpreadPercentage},{data.MinVolume},{data.MaxVolume}";
                await writer.WriteLineAsync(csvLine);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataCollector] Error processing data: {ex.Message}");
            }
        }
    }
}