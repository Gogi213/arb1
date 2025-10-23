using Mexc.Net.Clients;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

public class MexcExchangeClient : IExchangeClient
{
    public string ExchangeName => "MEXC";
    private readonly MexcRestClient _restClient;
    private readonly List<ManagedConnection> _connections = new List<ManagedConnection>();
    private Action<SpreadData> _onData;

    public MexcExchangeClient()
    {
        _restClient = new MexcRestClient();
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
        // MEXC has a limit of 30 subscriptions per connection. We use 20% of that.
        // MEXC has a limit on the message size for subscriptions.
        // A chunkSize of 30 was too large and exceeded the 1024 byte limit.
        // Reducing to 6 to keep message size down.
        const int chunkSize = 6;

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
        private readonly MexcSocketClient _socketClient;
        private readonly SemaphoreSlim _resubscribeLock = new SemaphoreSlim(1, 1);

        public ManagedConnection(List<string> symbols, Action<SpreadData> onData)
        {
            _symbols = symbols;
            _onData = onData;
            _socketClient = new MexcSocketClient();
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
            Console.WriteLine($"[MexcExchangeClient] Subscribing to a chunk of {_symbols.Count} symbols.");

            await _socketClient.SpotApi.UnsubscribeAllAsync();

            var result = await _socketClient.SpotApi.SubscribeToBookTickerUpdatesAsync(_symbols, data =>
            {
                if (data.Data != null && data.Symbol != null)
                {
                    _onData(new SpreadData
                    {
                        Exchange = "MEXC",
                        Symbol = data.Symbol,
                        BestBid = data.Data.BestBidPrice,
                        BestAsk = data.Data.BestAskPrice
                    });
                }
            });

            if (!result.Success)
            {
                Console.WriteLine($"[ERROR] [MEXC] Failed to subscribe to chunk starting with {_symbols.FirstOrDefault()}: {result.Error}");
            }
            else
            {
                Console.WriteLine($"[MEXC] Successfully subscribed to chunk starting with {_symbols.FirstOrDefault()}.");
                result.Data.ConnectionLost += HandleConnectionLost;
                result.Data.ConnectionRestored += (t) => Console.WriteLine($"[MEXC] Connection restored for chunk after {t}.");
            }
        }

        private async void HandleConnectionLost()
        {
            await _resubscribeLock.WaitAsync();
            try
            {
                Console.WriteLine($"[MEXC] Connection lost for chunk starting with {_symbols.FirstOrDefault()}. Attempting to resubscribe...");
                await Task.Delay(1000);
                await SubscribeInternalAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] [MEXC] Failed to resubscribe for chunk: {ex.Message}");
            }
            finally
            {
                _resubscribeLock.Release();
            }
        }
    }
}