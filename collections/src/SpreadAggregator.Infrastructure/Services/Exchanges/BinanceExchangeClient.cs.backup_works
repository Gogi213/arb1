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
    private Action<SpreadData>? _onTickerData;
    private Action<TradeData>? _onTradeData;

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
        const int chunkSize = 20;

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
        private readonly BinanceSocketClient _socketClient;
        private readonly SemaphoreSlim _resubscribeLock = new SemaphoreSlim(1, 1);

        public ManagedConnection(List<string> symbols, Action<SpreadData>? onTickerData, Action<TradeData>? onTradeData)
        {
            _symbols = symbols;
            _onTickerData = onTickerData;
            _onTradeData = onTradeData;
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

            if (_onTickerData != null)
            {
                var tickerSubscription = await _socketClient.SpotApi.ExchangeData.SubscribeToBookTickerUpdatesAsync(_symbols, data =>
                {
                    _onTickerData?.Invoke(new SpreadData
                    {
                        Exchange = "Binance",
                        Symbol = data.Data.Symbol,
                        BestBid = data.Data.BestBidPrice,
                        BestAsk = data.Data.BestAskPrice
                    });
                });

                if (!tickerSubscription.Success)
                {
                    Console.WriteLine($"[ERROR] [Binance] Failed to subscribe to ticker chunk starting with {_symbols.FirstOrDefault()}: {tickerSubscription.Error}");
                }
                else
                {
                    Console.WriteLine($"[Binance] Successfully subscribed to ticker chunk starting with {_symbols.FirstOrDefault()}.");
                    tickerSubscription.Data.ConnectionLost += HandleConnectionLost;
                    tickerSubscription.Data.ConnectionRestored += (t) => Console.WriteLine($"[Binance] Ticker connection restored for chunk after {t}.");
                }
            }

            if (_onTradeData != null)
            {
                var tradeSubscription = await _socketClient.SpotApi.ExchangeData.SubscribeToTradeUpdatesAsync(_symbols, data =>
                {
                    _onTradeData?.Invoke(new TradeData
                    {
                        Exchange = "Binance",
                        Symbol = data.Data.Symbol,
                        Price = data.Data.Price,
                        Quantity = data.Data.Quantity,
                        Side = data.Data.BuyerIsMaker ? "Sell" : "Buy",
                        Timestamp = data.Data.TradeTime
                    });
                });

                if (!tradeSubscription.Success)
                {
                    Console.WriteLine($"[ERROR] [Binance] Failed to subscribe to trade chunk starting with {_symbols.FirstOrDefault()}: {tradeSubscription.Error}");
                }
                else
                {
                    Console.WriteLine($"[Binance] Successfully subscribed to trade chunk starting with {_symbols.FirstOrDefault()}.");
                    tradeSubscription.Data.ConnectionLost += HandleConnectionLost;
                    tradeSubscription.Data.ConnectionRestored += (t) => Console.WriteLine($"[Binance] Trade connection restored for chunk after {t}.");
                }
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