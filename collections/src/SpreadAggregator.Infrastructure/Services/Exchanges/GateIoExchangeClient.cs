using GateIo.Net.Clients;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

public class GateIoExchangeClient : IExchangeClient
{
    public string ExchangeName => "GateIo";
    private readonly GateIoRestClient _restClient;
    private readonly List<ManagedConnection> _connections = new List<ManagedConnection>();
    private Action<SpreadData>? _onTickerData;
    private Action<TradeData>? _onTradeData;

    public GateIoExchangeClient()
    {
        _restClient = new GateIoRestClient();
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
        _onTickerData = onData;
        await SetupConnections(symbols);
    }

    public async Task SubscribeToTradesAsync(IEnumerable<string> symbols, Action<TradeData> onData)
    {
        _onTradeData = onData;
        await SetupConnections(symbols);
    }

    private async Task SetupConnections(IEnumerable<string> symbols)
    {
        if (_connections.Any())
        {
            foreach (var connection in _connections)
            {
                await connection.StopAsync();
            }
            _connections.Clear();
        }

        var symbolsList = symbols.ToList();
        const int chunkSize = 30;

        for (int i = 0; i < symbolsList.Count; i += chunkSize)
        {
            var chunk = symbolsList.Skip(i).Take(chunkSize).ToList();
            if (chunk.Any())
            {
                var connection = new ManagedConnection(chunk, _onTickerData, _onTradeData);
                _connections.Add(connection);
            }
        }

        await Task.WhenAll(_connections.Select(c => c.StartAsync()));
    }

    private class ManagedConnection
    {
        private readonly List<string> _symbols;
        private readonly Action<SpreadData>? _onTickerData;
        private readonly Action<TradeData>? _onTradeData;
        private readonly GateIoSocketClient _socketClient;
        private readonly SemaphoreSlim _resubscribeLock = new SemaphoreSlim(1, 1);

        public ManagedConnection(List<string> symbols, Action<SpreadData>? onTickerData, Action<TradeData>? onTradeData)
        {
            _symbols = symbols;
            _onTickerData = onTickerData;
            _onTradeData = onTradeData;
            _socketClient = new GateIoSocketClient();
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
            Console.WriteLine($"[GateIoExchangeClient] Subscribing to a chunk of {_symbols.Count} symbols.");

            await _socketClient.SpotApi.UnsubscribeAllAsync();

            if (_onTickerData != null)
            {
                var tickerResult = await _socketClient.SpotApi.SubscribeToBookTickerUpdatesAsync(_symbols, data =>
                {
                    _onTickerData.Invoke(new SpreadData
                    {
                        Exchange = "GateIo",
                        Symbol = data.Data.Symbol,
                        BestBid = data.Data.BestBidPrice,
                        BestAsk = data.Data.BestAskPrice
                    });
                });

                if (!tickerResult.Success)
                {
                    Console.WriteLine($"[ERROR] [GateIo] Failed to subscribe to ticker chunk: {tickerResult.Error}");
                }
                else
                {
                    Console.WriteLine($"[GateIo] Successfully subscribed to ticker chunk starting with {_symbols.FirstOrDefault()}.");
                    tickerResult.Data.ConnectionLost += HandleConnectionLost;
                    tickerResult.Data.ConnectionRestored += (t) => Console.WriteLine($"[GateIo] Ticker connection restored after {t}.");
                }
            }

            if (_onTradeData != null)
            {
                var tradeResult = await _socketClient.SpotApi.SubscribeToTradeUpdatesAsync(_symbols, data =>
                {
                    _onTradeData.Invoke(new TradeData
                    {
                        Exchange = "GateIo",
                        Symbol = data.Data.Symbol,
                        Price = data.Data.Price,
                        Quantity = data.Data.Quantity,
                        Side = data.Data.Side.ToString(),
                        Timestamp = data.Data.CreateTime
                    });
                });

                if (!tradeResult.Success)
                {
                    Console.WriteLine($"[ERROR] [GateIo] Failed to subscribe to trade chunk: {tradeResult.Error}");
                }
                else
                {
                    Console.WriteLine($"[GateIo] Successfully subscribed to trade chunk starting with {_symbols.FirstOrDefault()}.");
                    tradeResult.Data.ConnectionLost += HandleConnectionLost;
                    tradeResult.Data.ConnectionRestored += (t) => Console.WriteLine($"[GateIo] Trade connection restored after {t}.");
                }
            }
        }

        private async void HandleConnectionLost()
        {
            await _resubscribeLock.WaitAsync();
            try
            {
                Console.WriteLine($"[GateIo] Connection lost for chunk starting with {_symbols.FirstOrDefault()}. Attempting to resubscribe...");
                await Task.Delay(1000);
                await SubscribeInternalAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] [GateIo] Failed to resubscribe for chunk: {ex.Message}");
            }
            finally
            {
                _resubscribeLock.Release();
            }
        }
    }
}