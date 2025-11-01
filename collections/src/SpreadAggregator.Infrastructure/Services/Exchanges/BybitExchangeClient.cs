using Bybit.Net.Clients;
using Bybit.Net.Interfaces.Clients;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Infrastructure.Services.Exchanges.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

/// <summary>
/// Bybit exchange client implementation.
/// Reduced from 154 lines to ~115 lines using ExchangeClientBase.
/// Note: Bybit uses V5SpotApi with Orderbook depth=1 instead of standard BookTicker API.
/// </summary>
public class BybitExchangeClient : ExchangeClientBase<IBybitRestClient, BybitSocketClient>
{
    public override string ExchangeName => "Bybit";
    protected override int ChunkSize => 10;
    protected override bool SupportsTradesStream => false;

    private readonly IBybitRestClient _injectedRestClient;

    public BybitExchangeClient(IBybitRestClient restClient)
    {
        _injectedRestClient = restClient;
    }

    protected override IBybitRestClient CreateRestClient() => _injectedRestClient;
    protected override BybitSocketClient CreateSocketClient() => new();

    protected override IExchangeSocketApi CreateSocketApi(BybitSocketClient client)
    {
        return new BybitSocketApiAdapter(client);
    }

    public override async Task<IEnumerable<string>> GetSymbolsAsync()
    {
        var symbols = await _restClient.V5Api.ExchangeData.GetSpotSymbolsAsync();
        return symbols.Data.List.Select(s => s.Name);
    }

    public override async Task<IEnumerable<TickerData>> GetTickersAsync()
    {
        var tickers = await _restClient.V5Api.ExchangeData.GetSpotTickersAsync();
        return tickers.Data.List.Select(t => new TickerData
        {
            Symbol = t.Symbol,
            QuoteVolume = t.Turnover24h
        });
    }

    /// <summary>
    /// Adapter for Bybit V5SpotApi.
    /// Uses concrete BybitSocketClient type to access V5SpotApi directly.
    /// </summary>
    private class BybitSocketApiAdapter : IExchangeSocketApi
    {
        private readonly BybitSocketClient _socketClient;

        public BybitSocketApiAdapter(BybitSocketClient socketClient)
        {
            _socketClient = socketClient;
        }

        public Task UnsubscribeAllAsync()
        {
            return _socketClient.V5SpotApi.UnsubscribeAllAsync();
        }

        public async Task<object> SubscribeToTickerUpdatesAsync(
            IEnumerable<string> symbols,
            Action<SpreadData> onData)
        {
            // Bybit uses orderbook depth=1 which is equivalent to BookTicker
            var result = await _socketClient.V5SpotApi.SubscribeToOrderbookUpdatesAsync(
                symbols,
                1,
                data =>
                {
                    var bestBid = data.Data.Bids.FirstOrDefault();
                    var bestAsk = data.Data.Asks.FirstOrDefault();

                    if (bestBid != null && bestAsk != null)
                    {
                        onData(new SpreadData
                        {
                            Exchange = "Bybit",
                            Symbol = data.Data.Symbol,
                            BestBid = bestBid.Price,
                            BestAsk = bestAsk.Price
                        });
                    }
                });

            return result;
        }

        public Task<object> SubscribeToTradeUpdatesAsync(
            IEnumerable<string> symbols,
            Action<TradeData> onData)
        {
            throw new NotImplementedException("Bybit does not support trade stream yet");
        }
    }
}
