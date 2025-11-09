using Microsoft.Extensions.Logging;
using SpreadAggregator.Application.Abstractions;
using System;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services;

/// <summary>
/// Dedicated logger for bid/bid arbitrage data (chart data)
/// Logs joined bid1/bid2/spread that goes to the chart
/// </summary>
public class BidBidLogger : IBidBidLogger, IDisposable
{
    private readonly StreamWriter _icpWriter;
    private readonly Channel<(string symbol, string exchange1, string exchange2, DateTime timestamp, decimal bid1, decimal bid2, double spread)> _logChannel;
    private readonly ILogger<BidBidLogger> _logger;
    private readonly Task _backgroundTask;
    private bool _disposed;

    public BidBidLogger(ILogger<BidBidLogger> logger, string logDirectory = "logs")
    {
        _logger = logger;

        // Create logs directory if it doesn't exist
        if (!Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

        // Create ICPUSDT bid/bid log file (chart data)
        var icpBidBidFileName = $"bidbid_ICPUSDT_{timestamp}.log";
        var icpBidBidFilePath = Path.Combine(logDirectory, icpBidBidFileName);
        _icpWriter = new StreamWriter(icpBidBidFilePath, append: true)
        {
            AutoFlush = true
        };
        _icpWriter.WriteLine("Timestamp,Exchange1,Exchange2,Symbol,Bid1,Bid2,Spread");
        _logger.LogInformation($"BidBid ICPUSDT chart logger started. Writing to: {icpBidBidFilePath}");

        // Create bounded channel to avoid memory buildup
        _logChannel = Channel.CreateBounded<(string, string, string, DateTime, decimal, decimal, double)>(
            new BoundedChannelOptions(10000)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });

        // Start background writer task
        _backgroundTask = Task.Run(ProcessLogQueueAsync);
    }

    /// <summary>
    /// Logs bid/bid arbitrage data point.
    /// Non-blocking - writes to channel for background processing.
    /// </summary>
    public Task LogAsync(string symbol, string exchange1, string exchange2,
                         DateTime timestamp, decimal bid1, decimal bid2, double spread)
    {
        if (_disposed) return Task.CompletedTask;

        // Only log ICPUSDT from Bybit/GateIo
        if (symbol.Equals("ICPUSDT", StringComparison.OrdinalIgnoreCase))
        {
            // Non-blocking write to channel
            _logChannel.Writer.TryWrite((symbol, exchange1, exchange2, timestamp, bid1, bid2, spread));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Background task that processes log queue.
    /// </summary>
    private async Task ProcessLogQueueAsync()
    {
        await foreach (var (symbol, exchange1, exchange2, timestamp, bid1, bid2, spread) in _logChannel.Reader.ReadAllAsync())
        {
            try
            {
                var timestampStr = timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);

                var logLine = $"{timestampStr},{exchange1},{exchange2},{symbol}," +
                             $"{bid1.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                             $"{bid2.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                             $"{spread.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}";

                await _icpWriter.WriteLineAsync(logLine);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing to bidbid log");
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

        _icpWriter?.Dispose();

        GC.SuppressFinalize(this);
    }
}
