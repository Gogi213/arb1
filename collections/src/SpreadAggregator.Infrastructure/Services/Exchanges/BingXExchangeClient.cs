using BingX.Net.Clients;
using BingX.Net.Interfaces.Clients.SpotApi;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Infrastructure.Services.Exchanges.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

public class BingXExchangeClient : ExchangeClientBase<BingXRestClient, BingXSocketClient>
{
    public override string ExchangeName => "BingX";
    protected override int ChunkSize => 100;
    protected override bool SupportsTradesStream => false;
    protected override bool SupportsMultipleSymbols => false; // BingX doesn't support multiple symbols in one subscription

    protected override BingXRestClient CreateRestClient() => new();
    protected override BingXSocketClient CreateSocketClient() => new();

    protected override IExchangeSocketApi CreateSocketApi(BingXSocketClient client)
    {
        return new BingXSocketApiAdapter(client.SpotApi);
    }

    public override async Task<IEnumerable<SymbolInfo>> GetSymbolsAsync()
    {
        var symbolsData = await _restClient.SpotApi.ExchangeData.GetSymbolsAsync();
        if (!symbolsData.Success)
        {
            return Enumerable.Empty<SymbolInfo>();
        }

        return symbolsData.Data.Select(s => new SymbolInfo
        {
            Exchange = ExchangeName,
            Name = s.Name,
            PriceStep = s.TickSize,
            QuantityStep = s.StepSize,
            MinNotional = s.MinNotional
        });
    }

    public override async Task<IEnumerable<TickerData>> GetTickersAsync()
    {
        var tickers = await _restClient.SpotApi.ExchangeData.GetTickersAsync();
        return tickers.Data.Select(t => new TickerData
        {
            Symbol = t.Symbol,
            QuoteVolume = t.QuoteVolume
        });
    }

    private class BingXSocketApiAdapter : IExchangeSocketApi
    {
        private readonly IBingXSocketClientSpotApi _spotApi;

        public BingXSocketApiAdapter(IBingXSocketClientSpotApi spotApi)
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
            // BingX only supports single symbol subscription, so subscribe to first symbol
            // Base class handles SupportsMultipleSymbols = false and subscribes one by one
            var symbol = symbols.First();
            var result = await _spotApi.SubscribeToBookPriceUpdatesAsync(
                symbol,
                data =>
                {
                    onData(new SpreadData
                    {
                        Exchange = "BingX",
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
            throw new NotImplementedException("BingX does not support trade stream yet");
        }
    }
}
