using SpreadAggregator.Domain.Entities;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SpreadAggregator.Application.Abstractions;

public interface IDataWriter
{
    Task WriteAsync(string filePath, IReadOnlyCollection<SpreadData> data);
    Task<List<SpreadData>> ReadAsync(string filePath);
    Task InitializeCollectorAsync(CancellationToken cancellationToken);
}