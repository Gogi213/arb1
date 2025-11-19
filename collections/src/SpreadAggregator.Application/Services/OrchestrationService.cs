using Microsoft.Extensions.Configuration;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Domain.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.IO;

namespace SpreadAggregator.Application.Services;

public class OrchestrationService
{
    private readonly IWebSocketServer _webSocketServer;
    private readonly SpreadCalculator _spreadCalculator;
    private readonly VolumeFilter _volumeFilter;
    private readonly IConfiguration _configuration;
    private readonly IEnumerable<IExchangeClient> _exchangeClients;
    private readonly Channel<MarketData> _rawDataChannel;
    private readonly Channel<MarketData> _rollingWindowChannel;
    private readonly IDataWriter? _dataWriter;
    private readonly IBidAskLogger? _bidAskLogger;

    // PROPOSAL-2025-0095: Track symbols and tasks for cleanup
    private readonly List<SymbolInfo> _allSymbolInfo = new();
    private readonly List<Task> _exchangeTasks = new();
    private readonly object _symbolLock = new();
    private readonly object _taskLock = new();
    private CancellationTokenSource? _cancellationTokenSource;

    public IEnumerable<SymbolInfo> AllSymbolInfo
    {
        get
        {
            lock (_symbolLock)
            {
                return _allSymbolInfo.ToList(); // Return snapshot
            }
        }
    }

    public ChannelReader<MarketData> RawDataChannelReader => _rawDataChannel.Reader;
    public ChannelReader<MarketData> RollingWindowChannelReader => _rollingWindowChannel.Reader;

    /// <summary>
    /// PROPOSAL-2025-0095: Get exchange health status for monitoring
    /// </summary>
    public Dictionary<string, string> GetExchangeHealth()
    {
        lock (_taskLock)
        {
            var exchangeNames = _configuration.GetSection("ExchangeSettings:Exchanges").GetChildren().Select(x => x.Key);
            var health = new Dictionary<string, string>();

            foreach (var exchangeName in exchangeNames)
            {
                var task = _exchangeTasks.FirstOrDefault();
                if (task == null)
                {
                    health[exchangeName] = "not_started";
                }
                else if (task.IsFaulted)
                {
                    health[exchangeName] = "failed";
                }
                else if (task.IsCompleted)
                {
                    health[exchangeName] = "stopped";
                }
                else
                {
                    health[exchangeName] = "running";
                }
            }

            return health;
        }
    }

    public OrchestrationService(
        IWebSocketServer webSocketServer,
        SpreadCalculator spreadCalculator,
        IConfiguration configuration,
        VolumeFilter volumeFilter,
        IEnumerable<IExchangeClient> exchangeClients,
        Channel<MarketData> rawDataChannel,
        Channel<MarketData> rollingWindowChannel,
        IDataWriter? dataWriter = null,
        IBidAskLogger? bidAskLogger = null)
    {
        _webSocketServer = webSocketServer;
        _spreadCalculator = spreadCalculator;
        _configuration = configuration;
        _volumeFilter = volumeFilter;
        _exchangeClients = exchangeClients;
        _rawDataChannel = rawDataChannel;
        _rollingWindowChannel = rollingWindowChannel;
        _dataWriter = dataWriter;
        _bidAskLogger = bidAskLogger;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _webSocketServer.Start();

        // Create cancellation token source for stopping all exchanges
        _cancellationTokenSource = new CancellationTokenSource();

        var exchangeNames = _configuration.GetSection("ExchangeSettings:Exchanges").GetChildren().Select(x => x.Key);
        var tasks = new List<Task>();

        foreach (var exchangeName in exchangeNames)
        {
            var exchangeClient = _exchangeClients.FirstOrDefault(c => c.ExchangeName.Equals(exchangeName, StringComparison.OrdinalIgnoreCase));
            if (exchangeClient == null)
            {
                Console.WriteLine($"[ERROR] No client found for exchange: {exchangeName}");
                continue;
            }

            // PROPOSAL-2025-0095: Error boundary - one exchange failure doesn't crash entire system
            var task = Task.Run(async () =>
            {
                try
                {
                    await ProcessExchange(exchangeClient, exchangeName, _cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine($"[{exchangeName}] Exchange stopped gracefully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FATAL] [{exchangeName}] Exchange failed with error: {ex.Message}");
                    Console.WriteLine($"[INFO] [{exchangeName}] Other exchanges continue running");
                    // Exchange died, but system continues
                }
            }, _cancellationTokenSource.Token);

            tasks.Add(task);

            // PROPOSAL-2025-0095: Thread-safe task tracking
            lock (_taskLock)
            {
                _exchangeTasks.Add(task); // Store for monitoring/cleanup
            }
        }

        // Store tasks but don't await - they are long-running background subscriptions
        Console.WriteLine($"[Orchestration] Started {_exchangeTasks.Count} exchange subscription tasks");
    }

    private async Task ProcessExchange(IExchangeClient exchangeClient, string exchangeName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var exchangeConfig = _configuration.GetSection($"ExchangeSettings:Exchanges:{exchangeName}:VolumeFilter");
        var minVolume = exchangeConfig.GetValue<decimal?>("MinUsdVolume") ?? 0;
        var maxVolume = exchangeConfig.GetValue<decimal?>("MaxUsdVolume") ?? decimal.MaxValue;

        var allSymbols = (await exchangeClient.GetSymbolsAsync()).ToList();

        // PROPOSAL-2025-0095: Thread-safe symbol addition
        lock (_symbolLock)
        {
            var existingSymbols = new HashSet<string>(_allSymbolInfo.Select(s => s.Name));
            var newSymbols = allSymbols.Where(s => !existingSymbols.Contains(s.Name)).ToList();
            _allSymbolInfo.AddRange(newSymbols);
        }
        
        var tickers = (await exchangeClient.GetTickersAsync()).ToList();
        Console.WriteLine($"[{exchangeName}] Received {tickers.Count} tickers and {allSymbols.Count} symbol info objects.");

        var tickerLookup = tickers.ToDictionary(t => t.Symbol, t => t.QuoteVolume);

        var filteredSymbolInfo = allSymbols
            .Where(s => tickerLookup.ContainsKey(s.Name) &&
                        (s.Name.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) || s.Name.EndsWith("USDC", StringComparison.OrdinalIgnoreCase)) &&
                        _volumeFilter.IsVolumeSufficient(tickerLookup[s.Name], minVolume, maxVolume))
            .ToList();
        
        var filteredSymbolNames = filteredSymbolInfo.Select(s => s.Name).ToList();
        
        Console.WriteLine($"[{exchangeName}] {filteredSymbolNames.Count} symbols passed the volume filter.");

        if (!filteredSymbolNames.Any())
        {
            Console.WriteLine($"[{exchangeName}] No symbols to subscribe to after filtering.");
            return;
        }

        var tasks = new List<Task>();
        var enableTickers = _configuration.GetValue<bool>("StreamSettings:EnableTickers", true);
        var enableTrades = _configuration.GetValue<bool>("StreamSettings:EnableTrades", true);

        if (enableTickers)
        {
            Console.WriteLine($"[{exchangeName}] Adding ticker subscription task for {filteredSymbolNames.Count} symbols...");
            tasks.Add(exchangeClient.SubscribeToTickersAsync(filteredSymbolNames, async spreadData =>
            {
                if (spreadData.BestAsk == 0) return;

                var localTimestamp = DateTime.UtcNow;

                // HFT: Use server timestamp from exchange (more accurate for cross-exchange timing)
                // Fallback to local timestamp only if exchange doesn't provide it
                var timestamp = spreadData.ServerTimestamp ?? localTimestamp;

                // Унифицированная нормализация: удаляем все разделители и преобразуем к формату SYMBOL_QUOTE
                var normalizedSymbol = spreadData.Symbol
                    .Replace("/", "")
                    .Replace("-", "")
                    .Replace("_", "")
                    .Replace(" ", "");

                // Добавляем подчеркивание перед USDT/USDC для единообразия
                if (normalizedSymbol.EndsWith("USDT"))
                {
                    normalizedSymbol = normalizedSymbol.Substring(0, normalizedSymbol.Length - 4) + "_USDT";
                }
                else if (normalizedSymbol.EndsWith("USDC"))
                {
                    normalizedSymbol = normalizedSymbol.Substring(0, normalizedSymbol.Length - 4) + "_USDC";
                }

                var normalizedSpreadData = new SpreadData
                {
                    Exchange = spreadData.Exchange,
                    Symbol = normalizedSymbol,
                    BestBid = spreadData.BestBid,
                    BestAsk = spreadData.BestAsk,
                    SpreadPercentage = _spreadCalculator.Calculate(spreadData.BestBid, spreadData.BestAsk),
                    MinVolume = minVolume,
                    MaxVolume = maxVolume,
                    Timestamp = timestamp,  // Use server timestamp for HFT accuracy
                    ServerTimestamp = spreadData.ServerTimestamp
                };

                // Log bid/ask with both server and local timestamps (non-blocking)
                // DISABLED: Bid/Ask logging disabled to save disk space
                // _bidAskLogger?.LogAsync(normalizedSpreadData, localTimestamp);

                // PROPOSAL-2025-0093: HFT hot path optimization
                // HOT PATH: WebSocket broadcast FIRST (critical for <1μs latency)
                var wrapper = new WebSocketMessage { MessageType = "Spread", Payload = normalizedSpreadData };
                var message = JsonSerializer.Serialize(wrapper);
                _ = _webSocketServer.BroadcastRealtimeAsync(message); // fire-and-forget

                // COLD PATH: TryWrite (synchronous, 0 allocations, ~50-100ns each)
                // Preferred over WriteAsync for HFT - 20-100x faster, no blocking
                if (!_rawDataChannel.Writer.TryWrite(normalizedSpreadData))
                {
                    Console.WriteLine($"[Orchestration-WARN] Raw data channel full (system overload), dropping spread data");
                }

                if (!_rollingWindowChannel.Writer.TryWrite(normalizedSpreadData))
                {
                    Console.WriteLine($"[Orchestration-WARN] Rolling window channel full (system overload), dropping spread data");
                }
            }));
        }

        if (enableTrades)
        {
            Console.WriteLine($"[{exchangeName}] Adding trade subscription task...");
            tasks.Add(exchangeClient.SubscribeToTradesAsync(filteredSymbolNames, async tradeData =>
            {
                // PROPOSAL-2025-0093: HFT hot path optimization (same as for spreads)
                // HOT PATH: WebSocket broadcast FIRST
                var wrapper = new WebSocketMessage { MessageType = "Trade", Payload = tradeData };
                var message = JsonSerializer.Serialize(wrapper);
                _ = _webSocketServer.BroadcastRealtimeAsync(message); // fire-and-forget

                // COLD PATH: TryWrite for minimal latency
                if (!_rawDataChannel.Writer.TryWrite(tradeData))
                {
                    Console.WriteLine($"[Orchestration-WARN] Raw data channel full (system overload), dropping trade data");
                }

                if (!_rollingWindowChannel.Writer.TryWrite(tradeData))
                {
                    Console.WriteLine($"[Orchestration-WARN] Rolling window channel full (system overload), dropping trade data");
                }
            }));
        }

        Console.WriteLine($"[{exchangeName}] Awaiting {tasks.Count} subscription tasks...");

        // These are long-running subscriptions - await them to handle errors properly
        await Task.WhenAll(tasks);

        Console.WriteLine($"[{exchangeName}] All subscription tasks completed");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[Orchestration] Stopping {_exchangeTasks.Count} exchange tasks...");

        // Step 1: Cancel all operations
        if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
        {
            Console.WriteLine("[Orchestration] Cancelling all operations...");
            _cancellationTokenSource.Cancel();
        }

        // Step 2: Stop all exchange client connections explicitly
        Console.WriteLine("[Orchestration] Stopping all exchange client connections...");
        var stopClientTasks = _exchangeClients.Select(client =>
        {
            try
            {
                return client.StopAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Orchestration] Error stopping {client.ExchangeName}: {ex.Message}");
                return Task.CompletedTask;
            }
        }).ToArray();

        await Task.WhenAll(stopClientTasks);
        Console.WriteLine("[Orchestration] All exchange clients stopped");

        // Step 3: Wait for tasks to complete
        Task[] tasksSnapshot;
        lock (_taskLock)
        {
            // PROPOSAL-2025-0095: Cleanup completed tasks before shutdown
            _exchangeTasks.RemoveAll(t => t.IsCompleted);
            tasksSnapshot = _exchangeTasks.ToArray();
        }

        if (tasksSnapshot.Length == 0)
        {
            Console.WriteLine("[Orchestration] No active tasks to stop");
            return;
        }

        // Give tasks a chance to complete gracefully
        var completedTask = await Task.WhenAny(Task.WhenAll(tasksSnapshot), Task.Delay(2000, cancellationToken));

        if (completedTask == Task.WhenAll(tasksSnapshot))
        {
            Console.WriteLine("[Orchestration] All tasks stopped gracefully");
        }
        else
        {
            Console.WriteLine("[Orchestration] Tasks did not complete in 2 seconds, forcing shutdown");
        }

        // Dispose cancellation token source
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        // PROPOSAL-2025-0095: Clear task list
        lock (_taskLock)
        {
            _exchangeTasks.Clear();
        }
    }

}
