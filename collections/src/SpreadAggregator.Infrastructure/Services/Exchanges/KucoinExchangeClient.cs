using Kucoin.Net.Clients;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

public class KucoinExchangeClient : IExchangeClient
{
    public string ExchangeName => "Kucoin";
    private readonly KucoinRestClient _restClient;
    private readonly List<ManagedConnection> _connections = new List<ManagedConnection>();
    private Action<SpreadData>? _onData;

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
        _onData = onData;

        foreach (var connection in _connections)
        {
            await connection.StopAsync();
        }
        _connections.Clear();

        var symbolsList = symbols.ToList();
        // Kucoin official limit is 100 symbols per connection. We use 20% of that.
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

    public Task SubscribeToTradesAsync(IEnumerable<string> symbols, Action<TradeData> onData)
    {
        // Not implemented for this exchange yet.
        return Task.CompletedTask;
    }

    private class ManagedConnection
    {
        private readonly List<string> _symbols;
        private readonly Action<SpreadData> _onData;
        private readonly KucoinSocketClient _socketClient;
        private readonly SemaphoreSlim _resubscribeLock = new SemaphoreSlim(1, 1);

        public ManagedConnection(List<string> symbols, Action<SpreadData> onData)
        {
            _symbols = symbols;
            _onData = onData;
            _socketClient = new KucoinSocketClient();
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
            Console.WriteLine($"[KucoinExchangeClient] Subscribing to a chunk of {_symbols.Count} symbols.");

            await _socketClient.SpotApi.UnsubscribeAllAsync();

            var result = await _socketClient.SpotApi.SubscribeToBookTickerUpdatesAsync(_symbols, data =>
            {
                if (data.Data?.BestBid != null && data.Data?.BestAsk != null && data.Symbol != null)
                {
                    _onData(new SpreadData
                    {
                        Exchange = "Kucoin",
                        Symbol = data.Symbol,
                        BestBid = data.Data.BestBid.Price,
                        BestAsk = data.Data.BestAsk.Price
                    });
                }
            });

            if (!result.Success)
            {
                Console.WriteLine($"[ERROR] [Kucoin] Failed to subscribe to chunk starting with {_symbols.FirstOrDefault()}: {result.Error}");
            }
            else
            {
                Console.WriteLine($"[Kucoin] Successfully subscribed to chunk starting with {_symbols.FirstOrDefault()}.");
                result.Data.ConnectionLost += HandleConnectionLost;
                result.Data.ConnectionRestored += (t) => Console.WriteLine($"[Kucoin] Connection restored for chunk after {t}.");
            }
        }

        private async void HandleConnectionLost()
        {
            await _resubscribeLock.WaitAsync();
            try
            {
                Console.WriteLine($"[Kucoin] Connection lost for chunk starting with {_symbols.FirstOrDefault()}. Attempting to resubscribe...");
                await Task.Delay(1000);
                await SubscribeInternalAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] [Kucoin] Failed to resubscribe for chunk: {ex.Message}");
            }
            finally
            {
                _resubscribeLock.Release();
            }
        }
    }
}