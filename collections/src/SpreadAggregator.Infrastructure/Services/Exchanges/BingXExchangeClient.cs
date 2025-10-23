using BingX.Net.Clients;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

public class BingXExchangeClient : IExchangeClient
{
    public string ExchangeName => "BingX";
    private readonly BingXRestClient _restClient;
    private readonly List<ManagedConnection> _connections = new List<ManagedConnection>();
    private Action<SpreadData> _onData;

    public BingXExchangeClient()
    {
        _restClient = new BingXRestClient();
    }

    public async Task<IEnumerable<string>> GetSymbolsAsync()
    {
        var symbols = await _restClient.SpotApi.ExchangeData.GetSymbolsAsync();
        return symbols.Data.Select(s => s.Name);
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
        _onData = onData;

        foreach (var connection in _connections)
        {
            await connection.StopAsync();
        }
        _connections.Clear();

        var symbolsList = symbols.ToList();
        // The API subscribes one symbol at a time. We'll be conservative and assume a max of 100 subscriptions per connection.
        // We shard by 20% of that limit.
        const int chunkSize = 100;

        for (int i = 0; i < symbolsList.Count; i += chunkSize)
        {
            var chunk = symbolsList.Skip(i).Take(chunkSize).ToList();
            if (chunk.Any())
            {
                var connection = new ManagedConnection(chunk, _onData);
                _connections.Add(connection);
            }
        }

        await Task.WhenAll(_connections.Select(c => c.StartAsync()));
    }

    private class ManagedConnection
    {
        private readonly List<string> _symbols;
        private readonly Action<SpreadData> _onData;
        private readonly BingXSocketClient _socketClient;
        private readonly SemaphoreSlim _resubscribeLock = new SemaphoreSlim(1, 1);

        public ManagedConnection(List<string> symbols, Action<SpreadData> onData)
        {
            _symbols = symbols;
            _onData = onData;
            _socketClient = new BingXSocketClient();
        }

        public async Task StartAsync()
        {
            await SubscribeInternalAsync();
        }

        public async Task StopAsync()
        {
            await _socketClient.SpotApi.UnsubscribeAllAsync();
            _socketClient.Dispose();
        }

        private async Task SubscribeInternalAsync()
        {
            Console.WriteLine($"[BingXExchangeClient] Subscribing to a chunk of {_symbols.Count} symbols.");

            await _socketClient.SpotApi.UnsubscribeAllAsync();

            foreach (var symbol in _symbols)
            {
                var result = await _socketClient.SpotApi.SubscribeToBookPriceUpdatesAsync(symbol, data =>
                {
                    _onData(new SpreadData
                    {
                        Exchange = "BingX",
                        Symbol = data.Data.Symbol,
                        BestBid = data.Data.BestBidPrice,
                        BestAsk = data.Data.BestAskPrice
                    });
                });

                if (!result.Success)
                {
                    Console.WriteLine($"[ERROR] [BingX] Failed to subscribe to {symbol}: {result.Error}");
                }
                else
                {
                    // Attach the handler only to the first successful subscription in the chunk
                    if (_symbols.IndexOf(symbol) == 0)
                    {
                         Console.WriteLine($"[BingX] Successfully subscribed to chunk starting with {_symbols.FirstOrDefault()}.");
                        result.Data.ConnectionLost += HandleConnectionLost;
                        result.Data.ConnectionRestored += (t) => Console.WriteLine($"[BingX] Connection restored for chunk after {t}.");
                    }
                }
            }
        }

        private async void HandleConnectionLost()
        {
            await _resubscribeLock.WaitAsync();
            try
            {
                Console.WriteLine($"[BingX] Connection lost for chunk starting with {_symbols.FirstOrDefault()}. Attempting to resubscribe...");
                await Task.Delay(1000);
                await SubscribeInternalAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] [BingX] Failed to resubscribe for chunk: {ex.Message}");
            }
            finally
            {
                _resubscribeLock.Release();
            }
        }
    }
}