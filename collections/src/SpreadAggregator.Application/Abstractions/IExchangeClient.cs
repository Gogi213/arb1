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
    /// Gets detailed information for all available symbols on the exchange.
    /// </summary>
    Task<IEnumerable<SymbolInfo>> GetSymbolsAsync();

    /// <summary>
    /// Gets tickers for all symbols.
    /// </summary>
    /// <returns>A list of tickers.</returns>
    Task<IEnumerable<TickerData>> GetTickersAsync();

    /// <summary>
    /// Subscribes to ticker (book ticker) updates for a list of symbols.
    /// </summary>
    /// <param name="symbols">The symbols to subscribe to.</param>
    /// <param name="onData">The action to perform when new ticker data arrives.</param>
    Task SubscribeToTickersAsync(IEnumerable<string> symbols, Func<SpreadData, Task> onData);

    /// <summary>
    /// Subscribes to trade updates for a list of symbols.
    /// </summary>
    /// <param name="symbols">The symbols to subscribe to.</param>
    /// <param name="onData">The action to perform when new trade data arrives.</param>
    Task SubscribeToTradesAsync(IEnumerable<string> symbols, Func<TradeData, Task> onData);

    /// <summary>
    /// Stops all active subscriptions and closes connections.
    /// </summary>
    Task StopAsync();
}