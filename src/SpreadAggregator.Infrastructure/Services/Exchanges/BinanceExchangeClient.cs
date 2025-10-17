using Binance.Net.Clients;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

public class BinanceExchangeClient : IExchangeClient
{
    public string ExchangeName => "Binance";
    private readonly BinanceRestClient _restClient;

    public BinanceExchangeClient()
    {
        _restClient = new BinanceRestClient();
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
        const int batchSize = 50; // Reduced from 100 to fit within message size limits
        const int delayBetweenSubscriptions = 500; // 0.5 second

        for (int i = 0; i < symbolsList.Count; i += batchSize)
        {
            var batch = symbolsList.Skip(i).Take(batchSize).ToList();
            var batchNumber = i / batchSize + 1;
            Console.WriteLine($"[BinanceExchangeClient] Subscribing to batch {batchNumber}, containing {batch.Count} symbols.");

            var socketClient = new BinanceSocketClient();
            var result = await socketClient.SpotApi.ExchangeData.SubscribeToBookTickerUpdatesAsync(batch, data =>
            {
                onData(new SpreadData
                {
                    Exchange = "Binance",
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
                Console.WriteLine($"[BinanceExchangeClient] Successfully subscribed to batch {batchNumber}.");
                result.Data.ConnectionLost += () => Console.WriteLine($"[BinanceExchangeClient] Connection lost for batch {batchNumber}.");
                result.Data.ConnectionRestored += (t) => Console.WriteLine($"[BinanceExchangeClient] Connection restored for batch {batchNumber} after {t}.");
            }

            await Task.Delay(delayBetweenSubscriptions);
        }
    }

}