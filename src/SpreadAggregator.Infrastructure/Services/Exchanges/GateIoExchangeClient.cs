using GateIo.Net.Clients;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

public class GateIoExchangeClient : IExchangeClient
{
    public string ExchangeName => "GateIo";
    private readonly GateIoRestClient _restClient;

    public GateIoExchangeClient()
    {
        _restClient = new GateIoRestClient();
    }

    public async Task<IEnumerable<string>> GetSymbolsAsync()
    {
        var tickers = await _restClient.SpotApi.ExchangeData.GetTickersAsync();
        return tickers.Data.Select(t => t.Symbol);
    }

    public async Task<IEnumerable<TickerData>> GetTickersAsync()
    {
        var tickers = await _restClient.SpotApi.ExchangeData.GetTickersAsync();
        return tickers.Data.Select(t => new TickerData
        {
            Symbol = t.Symbol,
            QuoteVolume = t.QuoteVolume
        });
    }

    public async Task SubscribeToTickersAsync(IEnumerable<string> symbols, Action<SpreadData> onData)
    {
        var symbolsList = symbols.ToList();
        // Gate.io might have subscription limits, so we'll use a conservative batching approach.
        const int batchSize = 10;
        const int delayBetweenSubscriptions = 1000; // 1 second

        for (int i = 0; i < symbolsList.Count; i += batchSize)
        {
            var batch = symbolsList.Skip(i).Take(batchSize).ToList();
            var batchNumber = i / batchSize + 1;
            Console.WriteLine($"[GateIoExchangeClient] Subscribing to batch {batchNumber}, containing {batch.Count} symbols.");

            // Note: Gate.io uses SubscribeToBookTickerUpdatesAsync for book ticker data.
            var socketClient = new GateIoSocketClient();
            var result = await socketClient.SpotApi.SubscribeToBookTickerUpdatesAsync(batch, data =>
            {
                onData(new SpreadData
                {
                    Exchange = "GateIo",
                    Symbol = data.Data.Symbol,
                    BestBid = data.Data.BestBidPrice,
                    BestAsk = data.Data.BestAskPrice
                });
            });

            if (!result.Success)
            {
                Console.WriteLine($"[ERROR] Failed to subscribe to batch {batchNumber}: {result.Error}");
            }
            else
            {
                Console.WriteLine($"[GateIoExchangeClient] Successfully subscribed to batch {batchNumber}.");
                result.Data.ConnectionLost += () => Console.WriteLine($"[GateIoExchangeClient] Connection lost for batch {batchNumber}.");
                result.Data.ConnectionRestored += (t) => Console.WriteLine($"[GateIoExchangeClient] Connection restored for batch {batchNumber} after {t}.");
            }

            await Task.Delay(delayBetweenSubscriptions);
        }
    }
}