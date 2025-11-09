using SpreadAggregator.Domain.Entities;
using System;
using System.Threading.Tasks;

namespace SpreadAggregator.Application.Abstractions;

/// <summary>
/// Interface for logging bid/ask data with both server and local timestamps.
/// </summary>
public interface IBidAskLogger
{
    /// <summary>
    /// Logs bid/ask data with both local and server timestamps.
    /// </summary>
    Task LogAsync(SpreadData spreadData, DateTime localTimestamp);
}
