using Microsoft.Extensions.Logging;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services;

/// <summary>
/// Dedicated logger for bid/ask data with both server and local timestamps.
/// Writes to a separate bidask log file for analysis.
/// </summary>
public class BidAskLogger : IBidAskLogger, IDisposable
{
    private readonly StreamWriter _writer;
    private readonly StreamWriter _icpWriter;
    private readonly Channel<(SpreadData data, DateTime timestamp)> _logChannel;
    private readonly ILogger<BidAskLogger> _logger;
    private readonly Task _backgroundTask;
    private bool _disposed;

    public BidAskLogger(ILogger<BidAskLogger> logger, string logDirectory = "logs")
    {
        _logger = logger;

        // Create logs directory if it doesn't exist
        if (!Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

        // Create general log file
        var fileName = $"bidask_{timestamp}.log";
        var filePath = Path.Combine(logDirectory, fileName);
        _writer = new StreamWriter(filePath, append: true)
        {
            AutoFlush = true
        };
        _writer.WriteLine("LocalTimestamp,ServerTimestamp,Exchange,Symbol,BestBid,BestAsk,SpreadPercentage");
        _logger.LogInformation($"BidAsk logger started. Writing to: {filePath}");

        // Create ICPUSDT-specific log file (bid/ask)
        var icpFileName = $"bidask_ICPUSDT_{timestamp}.log";
        var icpFilePath = Path.Combine(logDirectory, icpFileName);
        _icpWriter = new StreamWriter(icpFilePath, append: true)
        {
            AutoFlush = true
        };
        _icpWriter.WriteLine("LocalTimestamp,ServerTimestamp,Exchange,Symbol,BestBid,BestAsk,SpreadPercentage");
        _logger.LogInformation($"BidAsk ICPUSDT logger started. Writing to: {icpFilePath}");

        // Create bounded channel to avoid memory buildup
        _logChannel = Channel.CreateBounded<(SpreadData, DateTime)>(new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        // Start background writer task
        _backgroundTask = Task.Run(ProcessLogQueueAsync);
    }

    /// <summary>
    /// Logs bid/ask data with both local and server timestamps.
    /// Non-blocking - writes to channel for background processing.
    /// </summary>
    public Task LogAsync(SpreadData spreadData, DateTime localTimestamp)
    {
        if (_disposed) return Task.CompletedTask;

        // Non-blocking write to channel
        _logChannel.Writer.TryWrite((spreadData, localTimestamp));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Background task that processes log queue.
    /// </summary>
    private async Task ProcessLogQueueAsync()
    {
        await foreach (var (spreadData, localTimestamp) in _logChannel.Reader.ReadAllAsync())
        {
            try
            {
                var serverTimestamp = spreadData.ServerTimestamp?.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) ?? "N/A";
                var localTimestampStr = localTimestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);

                var logLine = $"{localTimestampStr},{serverTimestamp},{spreadData.Exchange},{spreadData.Symbol}," +
                             $"{spreadData.BestBid.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                             $"{spreadData.BestAsk.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                             $"{spreadData.SpreadPercentage.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

                // Write to general log
                await _writer.WriteLineAsync(logLine);

                // Write to ICPUSDT log if it's ICPUSDT from Bybit or GateIo
                if (spreadData.Symbol.Equals("ICPUSDT", StringComparison.OrdinalIgnoreCase) &&
                    (spreadData.Exchange.Equals("Bybit", StringComparison.OrdinalIgnoreCase) ||
                     spreadData.Exchange.Equals("GateIo", StringComparison.OrdinalIgnoreCase)))
                {
                    await _icpWriter.WriteLineAsync(logLine);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing to bidask log");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        // Close channel and wait for background task to finish
        _logChannel.Writer.Complete();
        try
        {
            _backgroundTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error waiting for background task to complete");
        }

        _writer?.Dispose();
        _icpWriter?.Dispose();

        GC.SuppressFinalize(this);
    }
}
