using CryptoExchange.Net.Interfaces;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Sockets;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges.Base;

/// <summary>
/// Abstraction over different JKorf exchange socket APIs to unify access patterns.
/// Each exchange has different API paths (SpotApi, V5SpotApi, UnifiedApi, etc.)
/// This interface provides a unified way to interact with them.
/// </summary>
public interface IExchangeSocketApi
{
    /// <summary>
    /// Unsubscribe from all active subscriptions.
    /// </summary>
    Task UnsubscribeAllAsync();

    /// <summary>
    /// Subscribe to ticker (book ticker) updates for multiple symbols.
    /// </summary>
    /// <param name="symbols">The symbols to subscribe to.</param>
    /// <param name="onData">Callback when ticker data arrives.</param>
    /// <returns>CallResult with subscription if successful.</returns>
    Task<object> SubscribeToTickerUpdatesAsync(
        IEnumerable<string> symbols,
        Action<SpreadData> onData);

    /// <summary>
    /// Subscribe to trade updates for multiple symbols.
    /// </summary>
    /// <param name="symbols">The symbols to subscribe to.</param>
    /// <param name="onData">Callback when trade data arrives.</param>
    /// <returns>CallResult with subscription if successful.</returns>
    Task<object> SubscribeToTradeUpdatesAsync(
        IEnumerable<string> symbols,
        Action<TradeData> onData);
}
