using OKX.Net.Clients;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

public class OkxExchangeClient : IExchangeClient
{
    public string ExchangeName => "OKX";
    private readonly OKXRestClient _restClient;
    private readonly List<ManagedConnection> _connections = new List<ManagedConnection>();
    private Action<SpreadData> _onData;

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
        _onData = onData;

        foreach (var connection in _connections)
        {
            await connection.StopAsync();
        }
        _connections.Clear();

        var symbolsList = symbols.ToList();
        // OKX official limit is 100 symbols per connection. We use 20% of that.
        const int chunkSize = 20;

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
        private readonly OKXSocketClient _socketClient;
        private readonly SemaphoreSlim _resubscribeLock = new SemaphoreSlim(1, 1);

        public ManagedConnection(List<string> symbols, Action<SpreadData> onData)
        {
            _symbols = symbols;
            _onData = onData;
            _socketClient = new OKXSocketClient();
        }

        public async Task StartAsync()
        {
            await SubscribeInternalAsync();
        }

        public async Task StopAsync()
        {
            await _socketClient.UnifiedApi.UnsubscribeAllAsync();
            _socketClient.Dispose();
        }

        private async Task SubscribeInternalAsync()
        {
            Console.WriteLine($"[OkxExchangeClient] Subscribing to a chunk of {_symbols.Count} symbols.");

            await _socketClient.UnifiedApi.UnsubscribeAllAsync();

            var result = await _socketClient.UnifiedApi.ExchangeData.SubscribeToTickerUpdatesAsync(_symbols, data =>
            {
                var ticker = data.Data;
                if (ticker.BestBidPrice.HasValue && ticker.BestAskPrice.HasValue)
                {
                    _onData(new SpreadData
                    {
                        Exchange = "OKX",
                        Symbol = ticker.Symbol,
                        BestBid = ticker.BestBidPrice.Value,
                        BestAsk = ticker.BestAskPrice.Value
                    });
                }
            });

            if (!result.Success)
            {
                Console.WriteLine($"[ERROR] [OKX] Failed to subscribe to chunk starting with {_symbols.FirstOrDefault()}: {result.Error}");
            }
            else
            {
                Console.WriteLine($"[OKX] Successfully subscribed to chunk starting with {_symbols.FirstOrDefault()}.");
                result.Data.ConnectionLost += HandleConnectionLost;
                result.Data.ConnectionRestored += (t) => Console.WriteLine($"[OKX] Connection restored for chunk after {t}.");
            }
        }

        private async void HandleConnectionLost()
        {
            await _resubscribeLock.WaitAsync();
            try
            {
                Console.WriteLine($"[OKX] Connection lost for chunk starting with {_symbols.FirstOrDefault()}. Attempting to resubscribe...");
                await Task.Delay(1000);
                await SubscribeInternalAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] [OKX] Failed to resubscribe for chunk: {ex.Message}");
            }
            finally
            {
                _resubscribeLock.Release();
            }
        }
    }
}