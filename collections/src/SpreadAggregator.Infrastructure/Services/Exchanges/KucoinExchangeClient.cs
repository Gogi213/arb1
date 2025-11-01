using Kucoin.Net.Clients;
using Kucoin.Net.Interfaces.Clients.SpotApi;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Infrastructure.Services.Exchanges.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

/// <summary>
/// Kucoin exchange client implementation.
/// Reduced from 149 lines to ~120 lines using ExchangeClientBase.
/// </summary>
public class KucoinExchangeClient : ExchangeClientBase<KucoinRestClient, KucoinSocketClient>
{
    public override string ExchangeName => "Kucoin";
    // Kucoin official limit is 100 symbols per connection.
    protected override int ChunkSize => 100;
    protected override bool SupportsTradesStream => false;

    protected override KucoinRestClient CreateRestClient() => new();
    protected override KucoinSocketClient CreateSocketClient() => new();

    protected override IExchangeSocketApi CreateSocketApi(KucoinSocketClient client)
    {
        return new KucoinSocketApiAdapter(client.SpotApi);
    }

    public override async Task<IEnumerable<string>> GetSymbolsAsync()
    {
        var markets = await _restClient.SpotApi.ExchangeData.GetSymbolsAsync();
        return markets.Data.Select(m => m.Symbol);
    }

    public override async Task<IEnumerable<TickerData>> GetTickersAsync()
    {
        var tickers = await _restClient.SpotApi.ExchangeData.GetTickersAsync();
        return tickers.Data.Data.Select(t => new TickerData
        {
            Symbol = t.Symbol,
            QuoteVolume = t.QuoteVolume ?? 0
        });
    }

    /// <summary>
    /// Adapter that wraps Kucoin SpotApi to implement IExchangeSocketApi.
    /// </summary>
    private class KucoinSocketApiAdapter : IExchangeSocketApi
    {
        private readonly IKucoinSocketClientSpotApi _spotApi;

        public KucoinSocketApiAdapter(IKucoinSocketClientSpotApi spotApi)
        {
            _spotApi = spotApi;
        }

        public Task UnsubscribeAllAsync()
        {
            return _spotApi.UnsubscribeAllAsync();
        }

        public async Task<object> SubscribeToTickerUpdatesAsync(
            IEnumerable<string> symbols,
            Action<SpreadData> onData)
        {
            var result = await _spotApi.SubscribeToBookTickerUpdatesAsync(
                symbols,
                data =>
                {
                    if (data.Data?.BestBid != null && data.Data?.BestAsk != null && data.Symbol != null)
                    {
                        onData(new SpreadData
                        {
                            Exchange = "Kucoin",
                            Symbol = data.Symbol,
                            BestBid = data.Data.BestBid.Price,
                            BestAsk = data.Data.BestAsk.Price
                        });
                    }
                });

            return result;
        }

        public Task<object> SubscribeToTradeUpdatesAsync(
            IEnumerable<string> symbols,
            Action<TradeData> onData)
        {
            // Not implemented for this exchange yet
            throw new NotImplementedException("Kucoin does not support trade stream yet");
        }
    }
}
