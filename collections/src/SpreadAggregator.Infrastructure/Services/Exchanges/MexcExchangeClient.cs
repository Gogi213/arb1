using Mexc.Net.Clients;
using Mexc.Net.Interfaces.Clients.SpotApi;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Infrastructure.Services.Exchanges.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

/// <summary>
/// MEXC exchange client implementation.
/// Reduced from 152 lines to ~115 lines using ExchangeClientBase.
/// </summary>
public class MexcExchangeClient : ExchangeClientBase<MexcRestClient, MexcSocketClient>
{
    public override string ExchangeName => "MEXC";
    // MEXC has a limit of 30 subscriptions per connection. We use 20% of that.
    // MEXC has a limit on the message size for subscriptions.
    // A chunkSize of 30 was too large and exceeded the 1024 byte limit.
    // Reducing to 6 to keep message size down.
    protected override int ChunkSize => 6;
    protected override bool SupportsTradesStream => false;

    protected override MexcRestClient CreateRestClient() => new();
    protected override MexcSocketClient CreateSocketClient() => new();

    protected override IExchangeSocketApi CreateSocketApi(MexcSocketClient client)
    {
        return new MexcSocketApiAdapter(client.SpotApi);
    }

    public override async Task<IEnumerable<SymbolInfo>> GetSymbolsAsync()
    {
        var symbolsData = await _restClient.SpotApi.ExchangeData.GetExchangeInfoAsync();
        if (!symbolsData.Success)
        {
            return Enumerable.Empty<SymbolInfo>();
        }

        return symbolsData.Data.Symbols.Select(s => new SymbolInfo
        {
            Exchange = ExchangeName,
            Name = s.Name,
            PriceStep = (decimal)Math.Pow(10, -s.QuoteAssetPrecision),
            QuantityStep = (decimal)Math.Pow(10, -s.BaseAssetPrecision),
            MinNotional = s.QuoteQuantityPrecision
        });
    }

    public override async Task<IEnumerable<TickerData>> GetTickersAsync()
    {
        var tickers = await _restClient.SpotApi.ExchangeData.GetTickersAsync();
        return tickers.Data.Select(t => new TickerData
        {
            Symbol = t.Symbol,
            QuoteVolume = t.QuoteVolume ?? 0
        });
    }

    /// <summary>
    /// Adapter that wraps MEXC SpotApi to implement IExchangeSocketApi.
    /// </summary>
    private class MexcSocketApiAdapter : IExchangeSocketApi
    {
        private readonly IMexcSocketClientSpotApi _spotApi;

        public MexcSocketApiAdapter(IMexcSocketClientSpotApi spotApi)
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
                    if (data.Data != null && data.Symbol != null)
                    {
                        onData(new SpreadData
                        {
                            Exchange = "MEXC",
                            Symbol = data.Symbol,
                            BestBid = data.Data.BestBidPrice,
                            BestAsk = data.Data.BestAskPrice
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
            throw new NotImplementedException("MEXC does not support trade stream yet");
        }
    }
}
