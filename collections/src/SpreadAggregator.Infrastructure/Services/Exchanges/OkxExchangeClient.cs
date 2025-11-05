using OKX.Net.Clients;
using OKX.Net.Interfaces.Clients.UnifiedApi;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Infrastructure.Services.Exchanges.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

/// <summary>
/// OKX exchange client implementation.
/// Reduced from 150 lines to ~120 lines using ExchangeClientBase.
/// </summary>
public class OkxExchangeClient : ExchangeClientBase<OKXRestClient, OKXSocketClient>
{
    public override string ExchangeName => "OKX";
    // OKX official limit is 100 symbols per connection. We use 20% of that.
    protected override int ChunkSize => 20;
    protected override bool SupportsTradesStream => false;

    protected override OKXRestClient CreateRestClient() => new();
    protected override OKXSocketClient CreateSocketClient() => new();

    protected override IExchangeSocketApi CreateSocketApi(OKXSocketClient client)
    {
        return new OkxSocketApiAdapter(client.UnifiedApi);
    }

    public override async Task<IEnumerable<SymbolInfo>> GetSymbolsAsync()
    {
        var symbolsData = await _restClient.UnifiedApi.ExchangeData.GetSymbolsAsync(OKX.Net.Enums.InstrumentType.Spot);
        if (!symbolsData.Success)
        {
            return Enumerable.Empty<SymbolInfo>();
        }

        return symbolsData.Data.Select(s => new SymbolInfo
        {
            Exchange = ExchangeName,
            Name = s.Symbol,
            PriceStep = s.TickSize ?? 0,
            QuantityStep = s.LotSize ?? 0,
            MinNotional = 0 // OKX provides MinSize in base currency, not notional value
        });
    }

    public override async Task<IEnumerable<TickerData>> GetTickersAsync()
    {
        var tickers = await _restClient.UnifiedApi.ExchangeData.GetTickersAsync(OKX.Net.Enums.InstrumentType.Spot);
        return tickers.Data.Select(t => new TickerData
        {
            Symbol = t.Symbol,
            QuoteVolume = t.QuoteVolume
        });
    }

    /// <summary>
    /// Adapter that wraps OKX UnifiedApi to implement IExchangeSocketApi.
    /// Note: OKX uses UnifiedApi instead of SpotApi.
    /// </summary>
    private class OkxSocketApiAdapter : IExchangeSocketApi
    {
        private readonly IOKXSocketClientUnifiedApi _unifiedApi;

        public OkxSocketApiAdapter(IOKXSocketClientUnifiedApi unifiedApi)
        {
            _unifiedApi = unifiedApi;
        }

        public Task UnsubscribeAllAsync()
        {
            return _unifiedApi.UnsubscribeAllAsync();
        }

        public async Task<object> SubscribeToTickerUpdatesAsync(
            IEnumerable<string> symbols,
            Action<SpreadData> onData)
        {
            var result = await _unifiedApi.ExchangeData.SubscribeToTickerUpdatesAsync(
                symbols,
                data =>
                {
                    var ticker = data.Data;
                    if (ticker.BestBidPrice.HasValue && ticker.BestAskPrice.HasValue)
                    {
                        onData(new SpreadData
                        {
                            Exchange = "OKX",
                            Symbol = ticker.Symbol,
                            BestBid = ticker.BestBidPrice.Value,
                            BestAsk = ticker.BestAskPrice.Value
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
            throw new NotImplementedException("OKX does not support trade stream yet");
        }
    }
}
