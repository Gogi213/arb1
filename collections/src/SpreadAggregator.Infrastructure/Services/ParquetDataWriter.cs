using Parquet;
using Parquet.Data;
using Parquet.Schema;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services;

using SpreadAggregator.Application.Abstractions;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;

public class ParquetDataWriter : IDataWriter
{
    private static readonly ParquetSchema _schema = new ParquetSchema(
        new DataField("Timestamp", typeof(DateTime)),
        new DecimalDataField("BestBid", 28, 10),
        new DecimalDataField("BestAsk", 28, 10),
        new DecimalDataField("SpreadPercentage", 28, 10),
        new DecimalDataField("MinVolume", 28, 10),
        new DecimalDataField("MaxVolume", 28, 10),
        new DataField("Exchange", typeof(string)),
        new DataField("Symbol", typeof(string))
    );
    
    private readonly ChannelReader<SpreadData> _channelReader;
    private readonly IConfiguration _configuration;

    public ParquetDataWriter(Channel<SpreadData> channel, IConfiguration configuration)
    {
        _channelReader = channel.Reader;
        _configuration = configuration;
    }

    public async Task WriteAsync(string filePath, IReadOnlyCollection<SpreadData> data)
    {
        if (data == null || !data.Any())
            return;

        var columns = CreateDataColumns(data);

        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        await WriteData(columns, fileStream);
    }


    private DataColumn[] CreateDataColumns(IReadOnlyCollection<SpreadData> data)
    {
        return new[]
        {
            new DataColumn(_schema.DataFields[0], data.Select(d => d.Timestamp).ToArray()),
            new DataColumn(_schema.DataFields[1], data.Select(d => d.BestBid).ToArray()),
            new DataColumn(_schema.DataFields[2], data.Select(d => d.BestAsk).ToArray()),
            new DataColumn(_schema.DataFields[3], data.Select(d => d.SpreadPercentage).ToArray()),
            new DataColumn(_schema.DataFields[4], data.Select(d => d.MinVolume).ToArray()),
            new DataColumn(_schema.DataFields[5], data.Select(d => d.MaxVolume).ToArray()),
            new DataColumn(_schema.DataFields[6], data.Select(d => d.Exchange).ToArray()),
            new DataColumn(_schema.DataFields[7], data.Select(d => d.Symbol).ToArray())
        };
    }

    private async Task WriteData(DataColumn[] columns, Stream stream)
    {
        using var parquetWriter = await ParquetWriter.CreateAsync(_schema, stream);
        using var rowGroupWriter = parquetWriter.CreateRowGroup();

        foreach (var column in columns)
        {
            await rowGroupWriter.WriteColumnAsync(column);
        }
    }

    public async Task<List<SpreadData>> ReadAsync(string filePath)
    {
        var allData = new List<SpreadData>();
        if (!File.Exists(filePath))
        {
            return allData;
        }

        using var reader = await ParquetReader.CreateAsync(filePath);
        if (reader.RowGroupCount == 0)
            return allData;

        for (int i = 0; i < reader.RowGroupCount; i++)
        {
            using var groupReader = reader.OpenRowGroupReader(i);

            var timestampCol = await groupReader.ReadColumnAsync(_schema.DataFields[0]);
            var bestBidCol = await groupReader.ReadColumnAsync(_schema.DataFields[1]);
            var bestAskCol = await groupReader.ReadColumnAsync(_schema.DataFields[2]);
            var spreadPercentageCol = await groupReader.ReadColumnAsync(_schema.DataFields[3]);
            var minVolumeCol = await groupReader.ReadColumnAsync(_schema.DataFields[4]);
            var maxVolumeCol = await groupReader.ReadColumnAsync(_schema.DataFields[5]);
            var exchangeCol = await groupReader.ReadColumnAsync(_schema.DataFields[6]);
            var symbolCol = await groupReader.ReadColumnAsync(_schema.DataFields[7]);

            for (int j = 0; j < timestampCol.Data.Length; j++)
            {
                allData.Add(new SpreadData
                {
                    Timestamp = ((DateTime[])timestampCol.Data)[j],
                    BestBid = ((decimal[])bestBidCol.Data)[j],
                    BestAsk = ((decimal[])bestAskCol.Data)[j],
                    SpreadPercentage = ((decimal[])spreadPercentageCol.Data)[j],
                    MinVolume = ((decimal[])minVolumeCol.Data)[j],
                    MaxVolume = ((decimal[])maxVolumeCol.Data)[j],
                    Exchange = ((string[])exchangeCol.Data)[j],
                    Symbol = ((string[])symbolCol.Data)[j],
                });
            }
        }
        return allData;
    }
    
    public async Task InitializeCollectorAsync(CancellationToken cancellationToken)
    {
        var dataRoot = Path.Combine("data", "market_data");
        Directory.CreateDirectory(dataRoot);
        Console.WriteLine($"[DataCollector] Starting to record data with hybrid partitioning into: {dataRoot}");

        // The key for the buffer is the hourly directory path.
        var dataBuffers = new Dictionary<string, List<SpreadData>>();
        var batchSize = _configuration.GetValue<int>("Recording:BatchSize", 1000);

        try
        {
            await foreach (var data in _channelReader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    // Hive-style partitioning including the hour.
                    var hourlyPartitionDir = Path.Combine(dataRoot,
                        $"exchange={data.Exchange}",
                        $"symbol={data.Symbol}",
                        $"date={data.Timestamp:yyyy-MM-dd}",
                        $"hour={data.Timestamp.Hour:D2}");
                    
                    if (!dataBuffers.TryGetValue(hourlyPartitionDir, out var buffer))
                    {
                        buffer = new List<SpreadData>();
                        dataBuffers[hourlyPartitionDir] = buffer;
                    }

                    buffer.Add(data);

                    if (buffer.Count >= batchSize)
                    {
                        // The directory is created just before writing.
                        Directory.CreateDirectory(hourlyPartitionDir);
                        // The filename is unique to prevent overwrites within the same hour.
                        var filePath = Path.Combine(hourlyPartitionDir, $"{data.Timestamp:mm-ss.fffffff}.parquet");
                        await FlushBufferAsync(filePath, buffer);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DataCollector] Error processing data: {ex}");
                }
            }
        }
        finally
        {
            // On shutdown, flush all remaining buffers to their respective unique files.
            await FlushAllBuffersAsync(dataBuffers);
        }
    }

    private async Task FlushBufferAsync(string filePath, List<SpreadData> buffer)
    {
        if (!buffer.Any()) return;

        // This is a simple, efficient write to a NEW file. No read or append needed.
        await WriteAsync(filePath, buffer);
        
        Console.WriteLine($"[DataCollector] Wrote {buffer.Count} records to {filePath}.");
        buffer.Clear();
    }

    private async Task FlushAllBuffersAsync(Dictionary<string, List<SpreadData>> dataBuffers)
    {
        foreach (var (hourlyDir, buffer) in dataBuffers)
        {
            if (buffer.Any())
            {
                Directory.CreateDirectory(hourlyDir);
                // Ensure the final file has a unique name.
                var filePath = Path.Combine(hourlyDir, $"{DateTime.Now:mm-ss.fffffff}.parquet");
                await FlushBufferAsync(filePath, buffer);
            }
        }
    }
}