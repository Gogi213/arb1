using Binance.Net.Clients;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

public class BinanceExchangeClient : IExchangeClient
{
    public string ExchangeName => "Binance";
    private readonly BinanceRestClient _restClient;
    private readonly List<ManagedConnection> _connections = new List<ManagedConnection>();
    private Action<SpreadData> _onData;

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
        _onData = onData;
        
        foreach (var connection in _connections)
        {
            await connection.StopAsync();
        }
        _connections.Clear();

        var symbolsList = symbols.ToList();
        // Binance API limit for book ticker subscription is 100 symbols per request.
        // We shard connections by 20% of that limit.
        const int chunkSize = 20; // 20% of 100

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
        private readonly BinanceSocketClient _socketClient;
        private readonly SemaphoreSlim _resubscribeLock = new SemaphoreSlim(1, 1);

        public ManagedConnection(List<string> symbols, Action<SpreadData> onData)
        {
            _symbols = symbols;
            _onData = onData;
            _socketClient = new BinanceSocketClient();
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
            Console.WriteLine($"[BinanceExchangeClient] Subscribing to a chunk of {_symbols.Count} symbols.");
            
            await _socketClient.SpotApi.UnsubscribeAllAsync();

            var result = await _socketClient.SpotApi.ExchangeData.SubscribeToBookTickerUpdatesAsync(_symbols, data =>
            {
                _onData(new SpreadData
                {
                    Exchange = "Binance",
                    Symbol = data.Data.Symbol,
                    BestBid = data.Data.BestBidPrice,
                    BestAsk = data.Data.BestAskPrice
                });
            });

            if (!result.Success)
            {
                Console.WriteLine($"[ERROR] [Binance] Failed to subscribe to chunk starting with {_symbols.FirstOrDefault()}: {result.Error}");
            }
            else
            {
                Console.WriteLine($"[Binance] Successfully subscribed to chunk starting with {_symbols.FirstOrDefault()}.");
                result.Data.ConnectionLost += HandleConnectionLost;
                result.Data.ConnectionRestored += (t) => Console.WriteLine($"[Binance] Connection restored for chunk after {t}.");
            }
        }

        private async void HandleConnectionLost()
        {
            await _resubscribeLock.WaitAsync();
            try
            {
                Console.WriteLine($"[Binance] Connection lost for chunk starting with {_symbols.FirstOrDefault()}. Attempting to resubscribe...");
                await Task.Delay(1000); 
                await SubscribeInternalAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] [Binance] Failed to resubscribe for chunk: {ex.Message}");
            }
            finally
            {
                _resubscribeLock.Release();
            }
        }
    }
}