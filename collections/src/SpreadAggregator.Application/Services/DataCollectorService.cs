using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpreadAggregator.Application.Abstractions;
using System.Threading;
using System.Threading.Tasks;

namespace SpreadAggregator.Application.Services;

public class DataCollectorService : BackgroundService
{
    private readonly IDataWriter _dataWriter;
    private readonly ILogger<DataCollectorService> _logger;

    public DataCollectorService(IDataWriter dataWriter, ILogger<DataCollectorService> logger)
    {
        _dataWriter = dataWriter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[DataCollector] Starting background data collection...");
        // This will run in the background for the entire lifetime of the application.
        await _dataWriter.InitializeCollectorAsync(stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // PROPOSAL-2025-0095: Graceful shutdown - flush all buffered data
        _logger.LogInformation("[DataCollector] Stopping gracefully, flushing buffers...");

        await _dataWriter.FlushAsync();

        _logger.LogInformation("[DataCollector] All data flushed successfully");

        await base.StopAsync(cancellationToken);
    }
}