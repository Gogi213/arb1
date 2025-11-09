using Binance.Net.Clients;
using Binance.Net.Interfaces.Clients.SpotApi;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Sockets;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Infrastructure.Services.Exchanges.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

/// <summary>
/// Binance exchange client implementation.
/// Reduced from 185 lines to ~110 lines using ExchangeClientBase.
/// </summary>
public class BinanceExchangeClient : ExchangeClientBase<BinanceRestClient, BinanceSocketClient>
{
    public override string ExchangeName => "Binance";
    protected override int ChunkSize => 20;
    protected override bool SupportsTradesStream => true;

    protected override BinanceRestClient CreateRestClient() => new();
    protected override BinanceSocketClient CreateSocketClient() => new();

    protected override IExchangeSocketApi CreateSocketApi(BinanceSocketClient client)
    {
        return new BinanceSocketApiAdapter(client.SpotApi);
    }

    public override async Task<IEnumerable<SymbolInfo>> GetSymbolsAsync()
    {
        var exchangeInfo = await _restClient.SpotApi.ExchangeData.GetExchangeInfoAsync();
        if (!exchangeInfo.Success)
        {
            return Enumerable.Empty<SymbolInfo>();
        }

        return exchangeInfo.Data.Symbols.Select(s => new SymbolInfo
        {
            Exchange = ExchangeName,
            Name = s.Name,
            PriceStep = s.PriceFilter?.TickSize ?? 0,
            QuantityStep = s.LotSizeFilter?.StepSize ?? 0,
            MinNotional = s.MinNotionalFilter?.MinNotional ?? 0
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
    /// Adapter that wraps Binance SpotApi to implement IExchangeSocketApi.
    /// This eliminates the need for reflection or dynamic typing.
    /// </summary>
    private class BinanceSocketApiAdapter : IExchangeSocketApi
    {
        private readonly IBinanceSocketClientSpotApi _spotApi;

        public BinanceSocketApiAdapter(IBinanceSocketClientSpotApi spotApi)
        {
            _spotApi = spotApi;
        }

        public Task UnsubscribeAllAsync()
        {
            return _spotApi.UnsubscribeAllAsync();
        }

        public async Task<object> SubscribeToTickerUpdatesAsync(
            IEnumerable<string> symbols,
            Func<SpreadData, Task> onData)
        {
            var result = await _spotApi.ExchangeData.SubscribeToBookTickerUpdatesAsync(
                symbols,
                async data =>
                {
                    await onData(new SpreadData
                    {
                        Exchange = "Binance",
                        Symbol = data.Data.Symbol,
                        BestBid = data.Data.BestBidPrice,
                        BestAsk = data.Data.BestAskPrice
                        // ServerTimestamp not available in BookTickerUpdate
                    });
                });

            return result;
        }

        public async Task<object> SubscribeToTradeUpdatesAsync(
            IEnumerable<string> symbols,
            Func<TradeData, Task> onData)
        {
            var result = await _spotApi.ExchangeData.SubscribeToTradeUpdatesAsync(
                symbols,
                async data =>
                {
                    await onData(new TradeData
                    {
                        Exchange = "Binance",
                        Symbol = data.Data.Symbol,
                        Price = data.Data.Price,
                        Quantity = data.Data.Quantity,
                        Side = data.Data.BuyerIsMaker ? "Sell" : "Buy",
                        Timestamp = data.Data.TradeTime
                    });
                });

            return result;
        }
    }
}
