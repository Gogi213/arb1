using Microsoft.Extensions.Hosting;
using SpreadAggregator.Application.Abstractions;
using System.Threading;
using System.Threading.Tasks;

namespace SpreadAggregator.Application.Services;

public class DataCollectorService : BackgroundService
{
    private readonly IDataWriter _dataWriter;

    public DataCollectorService(IDataWriter dataWriter)
    {
        _dataWriter = dataWriter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // This will run in the background for the entire lifetime of the application.
        await _dataWriter.InitializeCollectorAsync(stoppingToken);
    }
}