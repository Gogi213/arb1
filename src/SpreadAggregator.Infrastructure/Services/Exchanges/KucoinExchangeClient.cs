using Kucoin.Net.Clients;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

public class KucoinExchangeClient : IExchangeClient
{
    public string ExchangeName => "Kucoin";
    private readonly KucoinRestClient _restClient;

    public KucoinExchangeClient()
    {
        _restClient = new KucoinRestClient();
    }

    public async Task<IEnumerable<string>> GetSymbolsAsync()
    {
        var markets = await _restClient.SpotApi.ExchangeData.GetSymbolsAsync();
        return markets.Data.Select(m => m.Symbol);
    }

    public async Task<IEnumerable<TickerData>> GetTickersAsync()
    {
        var tickers = await _restClient.SpotApi.ExchangeData.GetTickersAsync();
        return tickers.Data.Data.Select(t => new TickerData
        {
            Symbol = t.Symbol,
            QuoteVolume = t.QuoteVolume ?? 0
        });
    }

    public async Task SubscribeToTickersAsync(IEnumerable<string> symbols, Action<SpreadData> onData)
    {
        var symbolsList = symbols.ToList();
        const int batchSize = 100; // Kucoin official limit
        const int delayBetweenSubscriptions = 500; // 0.5 second delay

        for (int i = 0; i < symbolsList.Count; i += batchSize)
        {
            var batch = symbolsList.Skip(i).Take(batchSize).ToList();
            var batchNumber = i / batchSize + 1;
            Console.WriteLine($"[KucoinExchangeClient] Subscribing to batch {batchNumber}, containing {batch.Count} symbols.");

            var socketClient = new KucoinSocketClient();
            var result = await socketClient.SpotApi.SubscribeToBookTickerUpdatesAsync(batch, data =>
            {
                if (data.Data?.BestBid != null && data.Data?.BestAsk != null && data.Symbol != null)
                {
                    onData(new SpreadData
                    {
                        Exchange = ExchangeName,
                        Symbol = data.Symbol,
                        BestBid = data.Data.BestBid.Price,
                        BestAsk = data.Data.BestAsk.Price
                    });
                }
            });

            if (!result.Success)
            {
                Console.WriteLine($"[ERROR] [KucoinExchangeClient] Failed to subscribe to batch {batchNumber}: {result.Error}");
            }
            else
            {
                Console.WriteLine($"[KucoinExchangeClient] Successfully subscribed to batch {batchNumber}.");
                result.Data.ConnectionLost += () => Console.WriteLine($"[KucoinExchangeClient] Connection lost for batch {batchNumber}.");
                result.Data.ConnectionRestored += (t) => Console.WriteLine($"[KucoinExchangeClient] Connection restored for batch {batchNumber} after {t}.");
            }

            await Task.Delay(delayBetweenSubscriptions);
        }
    }
}