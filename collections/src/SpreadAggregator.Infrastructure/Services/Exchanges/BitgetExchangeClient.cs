using Bitget.Net.Clients;
using Bitget.Net.Interfaces.Clients.SpotApiV2;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Infrastructure.Services.Exchanges.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

public class BitgetExchangeClient : ExchangeClientBase<BitgetRestClient, BitgetSocketClient>
{
    public override string ExchangeName => "Bitget";
    protected override int ChunkSize => 100;
    protected override bool SupportsTradesStream => false;

    protected override BitgetRestClient CreateRestClient() => new();
    protected override BitgetSocketClient CreateSocketClient() => new();

    protected override IExchangeSocketApi CreateSocketApi(BitgetSocketClient client)
    {
        return new BitgetSocketApiAdapter(client.SpotApiV2);
    }

    public override async Task<IEnumerable<string>> GetSymbolsAsync()
    {
        var symbols = await _restClient.SpotApiV2.ExchangeData.GetSymbolsAsync();
        return symbols.Data.Select(s => s.Symbol);
    }

    public override async Task<IEnumerable<TickerData>> GetTickersAsync()
    {
        var tickers = await _restClient.SpotApiV2.ExchangeData.GetTickersAsync();
        return tickers.Data.Select(t => new TickerData
        {
            Symbol = t.Symbol,
            QuoteVolume = t.QuoteVolume
        });
    }

    private class BitgetSocketApiAdapter : IExchangeSocketApi
    {
        private readonly IBitgetSocketClientSpotApi _spotApi;

        public BitgetSocketApiAdapter(IBitgetSocketClientSpotApi spotApi)
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
            var result = await _spotApi.SubscribeToTickerUpdatesAsync(
                symbols,
                data =>
                {
                    onData(new SpreadData
                    {
                        Exchange = "Bitget",
                        Symbol = data.Data.Symbol,
                        BestBid = data.Data.BestBidPrice,
                        BestAsk = data.Data.BestAskPrice
                    });
                });

            return result;
        }

        public Task<object> SubscribeToTradeUpdatesAsync(
            IEnumerable<string> symbols,
            Action<TradeData> onData)
        {
            throw new NotImplementedException("Bitget does not support trade stream yet");
        }
    }
}
