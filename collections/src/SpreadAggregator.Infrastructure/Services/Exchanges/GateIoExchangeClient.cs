using GateIo.Net.Clients;
using GateIo.Net.Interfaces.Clients.SpotApi;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Infrastructure.Services.Exchanges.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

/// <summary>
/// Gate.io exchange client implementation.
/// Reduced from 185 lines to ~120 lines using ExchangeClientBase.
/// </summary>
public class GateIoExchangeClient : ExchangeClientBase<GateIoRestClient, GateIoSocketClient>
{
    public override string ExchangeName => "GateIo";
    protected override int ChunkSize => 30;
    protected override bool SupportsTradesStream => true;

    protected override GateIoRestClient CreateRestClient() => new();
    protected override GateIoSocketClient CreateSocketClient() => new();

    protected override IExchangeSocketApi CreateSocketApi(GateIoSocketClient client)
    {
        return new GateIoSocketApiAdapter(client.SpotApi);
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
            PriceStep = (decimal)Math.Pow(10, -s.PricePrecision),
            QuantityStep = (decimal)Math.Pow(10, -s.QuantityPrecision),
            MinNotional = s.MinQuoteQuantity
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

    /// <summary>
    /// Adapter that wraps GateIo SpotApi to implement IExchangeSocketApi.
    /// </summary>
    private class GateIoSocketApiAdapter : IExchangeSocketApi
    {
        private readonly IGateIoSocketClientSpotApi _spotApi;

        public GateIoSocketApiAdapter(IGateIoSocketClientSpotApi spotApi)
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
                    onData(new SpreadData
                    {
                        Exchange = "GateIo",
                        Symbol = data.Data.Symbol,
                        BestBid = data.Data.BestBidPrice,
                        BestAsk = data.Data.BestAskPrice
                    });
                });

            return result;
        }

        public async Task<object> SubscribeToTradeUpdatesAsync(
            IEnumerable<string> symbols,
            Action<TradeData> onData)
        {
            var result = await _spotApi.SubscribeToTradeUpdatesAsync(
                symbols,
                data =>
                {
                    onData(new TradeData
                    {
                        Exchange = "GateIo",
                        Symbol = data.Data.Symbol,
                        Price = data.Data.Price,
                        Quantity = data.Data.Quantity,
                        Side = data.Data.Side.ToString(),
                        Timestamp = data.Data.CreateTime
                    });
                });

            return result;
        }
    }
}
