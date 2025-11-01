using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpreadAggregator.Application.Abstractions;

/// <summary>
/// Defines the contract for an exchange client.
/// </summary>
public interface IExchangeClient
{
    /// <summary>
    /// Gets the name of the exchange.
    /// </summary>
    string ExchangeName { get; }

    /// <summary>
    /// Gets all symbols from the exchange.
    /// </summary>
    /// <returns>A list of symbols.</returns>
    Task<IEnumerable<string>> GetSymbolsAsync();

    /// <summary>
    /// Gets tickers for all symbols.
    /// </summary>
    /// <returns>A list of tickers.</returns>
    Task<IEnumerable<TickerData>> GetTickersAsync();

    /// <summary>
    /// Subscribes to ticker and trade updates for a list of symbols.
    /// </summary>
    /// <param name="symbols">The symbols to subscribe to.</param>
    /// <param name="onTickerData">The action to perform when new ticker data arrives.</param>
    /// <param name="onTradeData">The action to perform when new trade data arrives.</param>
    Task SubscribeAsync(IEnumerable<string> symbols, Action<SpreadData>? onTickerData, Action<TradeData>? onTradeData);
}