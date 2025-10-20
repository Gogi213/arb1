using Mexc.Net.Clients;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

public class MexcExchangeClient : IExchangeClient
{
    public string ExchangeName => "MEXC";
    private readonly MexcRestClient _restClient;

    public MexcExchangeClient()
    {
        _restClient = new MexcRestClient();
    }

    public async Task<IEnumerable<string>> GetSymbolsAsync()
    {
        var tickers = await _restClient.SpotApi.ExchangeData.GetTickersAsync();
        if (tickers == null || !tickers.Success || tickers.Data == null)
        {
            Console.WriteLine($"[ERROR] Failed to retrieve tickers from {ExchangeName}: {tickers?.Error?.Message ?? "No data returned"}");
            return Enumerable.Empty<string>();
        }
        return tickers.Data.Select(t => t.Symbol);
    }

    public async Task<IEnumerable<TickerData>> GetTickersAsync()
    {
        var tickers = await _restClient.SpotApi.ExchangeData.GetTickersAsync();
        if (tickers == null || !tickers.Success || tickers.Data == null)
        {
            Console.WriteLine($"[ERROR] Failed to retrieve tickers from {ExchangeName}: {tickers?.Error?.Message ?? "No data returned"}");
            return Enumerable.Empty<TickerData>();
        }
        return tickers.Data.Select(t => new TickerData
        {
            Symbol = t.Symbol,
            QuoteVolume = t.QuoteVolume ?? 0
        });
    }

    public async Task SubscribeToTickersAsync(IEnumerable<string> symbols, Action<SpreadData> onData)
    {
        // MEXC has a strict limit on the total number of active subscriptions per connection.
        // To work around this, we create a new socket client when the limit is approached.
        const int maxSubscriptionsPerConnection = 30; // A safe limit.
        const int batchSize = 10; // Symbols per subscription message.
        const int delayBetweenSubscriptions = 1000; // 1 second delay.

        var symbolsList = symbols.ToList();
        var activeSocketClient = new MexcSocketClient(); // Start with a fresh client.
        var subscriptionsOnCurrentClient = 0;

        for (int i = 0; i < symbolsList.Count; i += batchSize)
        {
            var batch = symbolsList.Skip(i).Take(batchSize).ToList();

            // Check if we need a new client for this batch
            if (subscriptionsOnCurrentClient > 0 && subscriptionsOnCurrentClient + batch.Count >= maxSubscriptionsPerConnection)
            {
                Console.WriteLine($"[MexcExchangeClient] Subscription limit for current client reached. Creating a new client.");
                activeSocketClient = new MexcSocketClient();
                subscriptionsOnCurrentClient = 0;
            }

            var batchNumber = i / batchSize + 1;
            Console.WriteLine($"[MexcExchangeClient] Subscribing to batch {batchNumber}, containing {batch.Count} symbols.");

            var result = await activeSocketClient.SpotApi.SubscribeToBookTickerUpdatesAsync(batch, data =>
            {
                if (data.Data != null && data.Symbol != null)
                {
                    onData(new SpreadData
                    {
                        Exchange = ExchangeName,
                        Symbol = data.Symbol,
                        BestBid = data.Data.BestBidPrice,
                        BestAsk = data.Data.BestAskPrice
                    });
                }
            });

            if (!result.Success)
            {
                Console.WriteLine($"[ERROR] Failed to subscribe to batch {batchNumber}: {result.Error}");
            }
            else
            {
                subscriptionsOnCurrentClient += batch.Count;
                Console.WriteLine($"[MexcExchangeClient] Successfully subscribed to batch {batchNumber}. Current subscriptions on this client: {subscriptionsOnCurrentClient}.");
                result.Data.ConnectionLost += () => Console.WriteLine($"[MexcExchangeClient] Connection lost for batch {batchNumber}.");
                result.Data.ConnectionRestored += (t) => Console.WriteLine($"[MexcExchangeClient] Connection restored for batch {batchNumber} after {t}.");
            }

            // Wait before sending the next subscription request.
            await Task.Delay(delayBetweenSubscriptions);
        }
    }
}