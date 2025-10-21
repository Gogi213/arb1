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
        new DataField("SpreadPercentage", typeof(double)),
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

        var timestampColumn = new DataColumn(_schema.DataFields[0], data.Select(d => d.Timestamp).ToArray());
        var bestBidColumn = new DataColumn(_schema.DataFields[1], data.Select(d => d.BestBid).ToArray());
        var bestAskColumn = new DataColumn(_schema.DataFields[2], data.Select(d => d.BestAsk).ToArray());
        var spreadPercentageColumn = new DataColumn(_schema.DataFields[3], data.Select(d => d.SpreadPercentage).ToArray());
        var minVolumeColumn = new DataColumn(_schema.DataFields[4], data.Select(d => d.MinVolume).ToArray());
        var maxVolumeColumn = new DataColumn(_schema.DataFields[5], data.Select(d => d.MaxVolume).ToArray());
        var exchangeColumn = new DataColumn(_schema.DataFields[6], data.Select(d => d.Exchange).ToArray());
        var symbolColumn = new DataColumn(_schema.DataFields[7], data.Select(d => d.Symbol).ToArray());

        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var parquetWriter = await ParquetWriter.CreateAsync(_schema, fileStream);
        using var rowGroupWriter = parquetWriter.CreateRowGroup();

        await rowGroupWriter.WriteColumnAsync(timestampColumn);
        await rowGroupWriter.WriteColumnAsync(bestBidColumn);
        await rowGroupWriter.WriteColumnAsync(bestAskColumn);
        await rowGroupWriter.WriteColumnAsync(spreadPercentageColumn);
        await rowGroupWriter.WriteColumnAsync(minVolumeColumn);
        await rowGroupWriter.WriteColumnAsync(maxVolumeColumn);
        await rowGroupWriter.WriteColumnAsync(exchangeColumn);
        await rowGroupWriter.WriteColumnAsync(symbolColumn);
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
        var sessionDirectory = Path.Combine("data", "market_data", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        Directory.CreateDirectory(sessionDirectory);
        Console.WriteLine($"[DataCollector] Starting to record data into: {sessionDirectory} (Parquet)");

        var dataBuffers = new Dictionary<string, List<SpreadData>>();
        var batchSize = _configuration.GetValue<int>("Recording:BatchSize", 100);

        try
        {
            await foreach (var data in _channelReader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    var exchangeDir = Path.Combine(sessionDirectory, data.Exchange);
                    var symbolDir = Path.Combine(exchangeDir, data.Symbol);
                    Directory.CreateDirectory(symbolDir);
                    var filePath = Path.Combine(symbolDir, "order_book_updates.parquet");

                    var bufferKey = filePath;
                    if (!dataBuffers.TryGetValue(bufferKey, out var buffer))
                    {
                        buffer = new List<SpreadData>();
                        dataBuffers[bufferKey] = buffer;
                    }

                    buffer.Add(data);

                    if (buffer.Count >= batchSize)
                    {
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
            await FlushAllBuffersAsync(dataBuffers);
        }
    }

    private async Task FlushBufferAsync(string filePath, List<SpreadData> buffer)
    {
        if (!buffer.Any()) return;

        var existingData = await ReadAsync(filePath);
        existingData.AddRange(buffer);
        await WriteAsync(filePath, existingData);
        
        Console.WriteLine($"[DataCollector] Wrote {buffer.Count} records to {filePath}. Total records: {existingData.Count}");
        buffer.Clear();
    }

    private async Task FlushAllBuffersAsync(Dictionary<string, List<SpreadData>> dataBuffers)
    {
        foreach (var (filePath, buffer) in dataBuffers)
        {
            await FlushBufferAsync(filePath, buffer);
        }
    }
}