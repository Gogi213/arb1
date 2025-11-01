using Bybit.Net.Clients;
using Bybit.Net.Interfaces.Clients;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

public class BybitExchangeClient : IExchangeClient
{
    public string ExchangeName => "Bybit";
    private readonly IBybitRestClient _restClient;
    private readonly List<ManagedConnection> _connections = new List<ManagedConnection>();
    private Action<SpreadData>? _onData;

    public BybitExchangeClient(IBybitRestClient restClient)
    {
        _restClient = restClient;
    }

    public async Task<IEnumerable<string>> GetSymbolsAsync()
    {
        var symbols = await _restClient.V5Api.ExchangeData.GetSpotSymbolsAsync();
        return symbols.Data.List.Select(s => s.Name);
    }

    public async Task<IEnumerable<TickerData>> GetTickersAsync()
    {
        var tickers = await _restClient.V5Api.ExchangeData.GetSpotTickersAsync();
        return tickers.Data.List.Select(t => new TickerData
        {
            Symbol = t.Symbol,
            QuoteVolume = t.Turnover24h
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
        // Original code used a batch size of 10. We'll use 20% of that.
        // Original code used a batch size of 10. We'll stick to that as it's a documented limit for subscriptions per connection.
        const int chunkSize = 10;

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
        private readonly BybitSocketClient _socketClient;
        private readonly SemaphoreSlim _resubscribeLock = new SemaphoreSlim(1, 1);

        public ManagedConnection(List<string> symbols, Action<SpreadData> onData)
        {
            _symbols = symbols;
            _onData = onData;
            _socketClient = new BybitSocketClient();
        }

        public async Task StartAsync()
        {
            await SubscribeInternalAsync();
        }

        public async Task StopAsync()
        {
            await _socketClient.V5SpotApi.UnsubscribeAllAsync();
            _socketClient.Dispose();
        }

        private async Task SubscribeInternalAsync()
        {
            Console.WriteLine($"[BybitExchangeClient] Subscribing to a chunk of {_symbols.Count} symbols.");

            await _socketClient.V5SpotApi.UnsubscribeAllAsync();

            var result = await _socketClient.V5SpotApi.SubscribeToOrderbookUpdatesAsync(_symbols, 1, data =>
            {
                var bestBid = data.Data.Bids.FirstOrDefault();
                var bestAsk = data.Data.Asks.FirstOrDefault();

                if (bestBid != null && bestAsk != null)
                {
                    _onData(new SpreadData
                    {
                        Exchange = "Bybit",
                        Symbol = data.Data.Symbol,
                        BestBid = bestBid.Price,
                        BestAsk = bestAsk.Price
                    });
                }
            });

            if (!result.Success)
            {
                Console.WriteLine($"[ERROR] [Bybit] Failed to subscribe to chunk starting with {_symbols.FirstOrDefault()}: {result.Error}");
            }
            else
            {
                Console.WriteLine($"[Bybit] Successfully subscribed to chunk starting with {_symbols.FirstOrDefault()}.");
                result.Data.ConnectionLost += HandleConnectionLost;
                result.Data.ConnectionRestored += (t) => Console.WriteLine($"[Bybit] Connection restored for chunk after {t}.");
            }
        }

        private async void HandleConnectionLost()
        {
            await _resubscribeLock.WaitAsync();
            try
            {
                Console.WriteLine($"[Bybit] Connection lost for chunk starting with {_symbols.FirstOrDefault()}. Attempting to resubscribe...");
                await Task.Delay(1000);
                await SubscribeInternalAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] [Bybit] Failed to resubscribe for chunk: {ex.Message}");
            }
            finally
            {
                _resubscribeLock.Release();
            }
        }
    }
}