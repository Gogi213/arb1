using OKX.Net.Clients;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

public class OkxExchangeClient : IExchangeClient
{
    public string ExchangeName => "OKX";
    private readonly OKXRestClient _restClient;

    public OkxExchangeClient()
    {
        _restClient = new OKXRestClient();
    }

    public async Task<IEnumerable<string>> GetSymbolsAsync()
    {
        var tickers = await _restClient.UnifiedApi.ExchangeData.GetTickersAsync(OKX.Net.Enums.InstrumentType.Spot);
        return tickers.Data.Select(t => t.Symbol);
    }

    public async Task<IEnumerable<TickerData>> GetTickersAsync()
    {
        var tickers = await _restClient.UnifiedApi.ExchangeData.GetTickersAsync(OKX.Net.Enums.InstrumentType.Spot);
        return tickers.Data.Select(t => new TickerData
        {
            Symbol = t.Symbol,
            QuoteVolume = t.QuoteVolume
        });
    }

    public async Task SubscribeToTickersAsync(IEnumerable<string> symbols, Action<SpreadData> onData)
    {
        var symbolsList = symbols.ToList();
        const int batchSize = 100; // OKX official limit
        const int delayBetweenSubscriptions = 500; // 0.5 second delay

        for (int i = 0; i < symbolsList.Count; i += batchSize)
        {
            var batch = symbolsList.Skip(i).Take(batchSize).ToList();
            var batchNumber = i / batchSize + 1;
            Console.WriteLine($"[OkxExchangeClient] Subscribing to batch {batchNumber}, containing {batch.Count} symbols.");

            var socketClient = new OKXSocketClient();
            var result = await socketClient.UnifiedApi.ExchangeData.SubscribeToTickerUpdatesAsync(batch, data =>
            {
                var ticker = data.Data;
                if (ticker.BestBidPrice.HasValue && ticker.BestAskPrice.HasValue)
                {
                    onData(new SpreadData
                    {
                        Exchange = ExchangeName,
                        Symbol = ticker.Symbol,
                        BestBid = ticker.BestBidPrice.Value,
                        BestAsk = ticker.BestAskPrice.Value
                    });
                }
            });

            if (!result.Success)
            {
                Console.WriteLine($"[ERROR] [OkxExchangeClient] Failed to subscribe to batch {batchNumber}: {result.Error}");
            }
            else
            {
                Console.WriteLine($"[OkxExchangeClient] Successfully subscribed to batch {batchNumber}.");
                result.Data.ConnectionLost += () => Console.WriteLine($"[OkxExchangeClient] Connection lost for batch {batchNumber}.");
                result.Data.ConnectionRestored += (t) => Console.WriteLine($"[OkxExchangeClient] Connection restored for batch {batchNumber} after {t}.");
            }

            await Task.Delay(delayBetweenSubscriptions);
        }
    }
}