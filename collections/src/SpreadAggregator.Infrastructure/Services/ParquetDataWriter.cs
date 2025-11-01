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
    private static readonly ParquetSchema _spreadSchema = new ParquetSchema(
        new DataField("Timestamp", typeof(DateTime)),
        new DecimalDataField("BestBid", 28, 10),
        new DecimalDataField("BestAsk", 28, 10),
        new DecimalDataField("SpreadPercentage", 28, 10),
        new DecimalDataField("MinVolume", 28, 10),
        new DecimalDataField("MaxVolume", 28, 10),
        new DataField("Exchange", typeof(string)),
        new DataField("Symbol", typeof(string))
    );

    private static readonly ParquetSchema _tradeSchema = new ParquetSchema(
        new DataField("Timestamp", typeof(DateTime)),
        new DecimalDataField("Price", 28, 10),
        new DecimalDataField("Quantity", 28, 10),
        new DataField("Side", typeof(string)),
        new DataField("Exchange", typeof(string)),
        new DataField("Symbol", typeof(string))
    );
    
    private readonly ChannelReader<MarketData> _channelReader;
    private readonly IConfiguration _configuration;

    public ParquetDataWriter(Channel<MarketData> channel, IConfiguration configuration)
    {
        _channelReader = channel.Reader;
        _configuration = configuration;
    }

    public Task WriteAsync(string filePath, IReadOnlyCollection<SpreadData> data)
    {
        return WriteSpreadsAsync(filePath, data);
    }

    private async Task WriteSpreadsAsync(string filePath, IReadOnlyCollection<SpreadData> data)
    {
        if (data == null || !data.Any())
            return;

        var columns = CreateSpreadDataColumns(data);

        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        await WriteData(columns, _spreadSchema, fileStream);
    }

    private async Task WriteTradesAsync(string filePath, IReadOnlyCollection<TradeData> data)
    {
        if (data == null || !data.Any())
            return;

        var columns = CreateTradeDataColumns(data);

        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        await WriteData(columns, _tradeSchema, fileStream);
    }

    private DataColumn[] CreateSpreadDataColumns(IReadOnlyCollection<SpreadData> data)
    {
        return new[]
        {
            new DataColumn(_spreadSchema.DataFields[0], data.Select(d => d.Timestamp).ToArray()),
            new DataColumn(_spreadSchema.DataFields[1], data.Select(d => d.BestBid).ToArray()),
            new DataColumn(_spreadSchema.DataFields[2], data.Select(d => d.BestAsk).ToArray()),
            new DataColumn(_spreadSchema.DataFields[3], data.Select(d => d.SpreadPercentage).ToArray()),
            new DataColumn(_spreadSchema.DataFields[4], data.Select(d => d.MinVolume).ToArray()),
            new DataColumn(_spreadSchema.DataFields[5], data.Select(d => d.MaxVolume).ToArray()),
            new DataColumn(_spreadSchema.DataFields[6], data.Select(d => d.Exchange).ToArray()),
            new DataColumn(_spreadSchema.DataFields[7], data.Select(d => d.Symbol).ToArray())
        };
    }

    private DataColumn[] CreateTradeDataColumns(IReadOnlyCollection<TradeData> data)
    {
        return new[]
        {
            new DataColumn(_tradeSchema.DataFields[0], data.Select(d => d.Timestamp).ToArray()),
            new DataColumn(_tradeSchema.DataFields[1], data.Select(d => d.Price).ToArray()),
            new DataColumn(_tradeSchema.DataFields[2], data.Select(d => d.Quantity).ToArray()),
            new DataColumn(_tradeSchema.DataFields[3], data.Select(d => d.Side).ToArray()),
            new DataColumn(_tradeSchema.DataFields[4], data.Select(d => d.Exchange).ToArray()),
            new DataColumn(_tradeSchema.DataFields[5], data.Select(d => d.Symbol).ToArray())
        };
    }

    private async Task WriteData(DataColumn[] columns, ParquetSchema schema, Stream stream)
    {
        using var parquetWriter = await ParquetWriter.CreateAsync(schema, stream);
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

            var timestampCol = await groupReader.ReadColumnAsync(_spreadSchema.DataFields[0]);
            var bestBidCol = await groupReader.ReadColumnAsync(_spreadSchema.DataFields[1]);
            var bestAskCol = await groupReader.ReadColumnAsync(_spreadSchema.DataFields[2]);
            var spreadPercentageCol = await groupReader.ReadColumnAsync(_spreadSchema.DataFields[3]);
            var minVolumeCol = await groupReader.ReadColumnAsync(_spreadSchema.DataFields[4]);
            var maxVolumeCol = await groupReader.ReadColumnAsync(_spreadSchema.DataFields[5]);
            var exchangeCol = await groupReader.ReadColumnAsync(_spreadSchema.DataFields[6]);
            var symbolCol = await groupReader.ReadColumnAsync(_spreadSchema.DataFields[7]);

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

        var spreadBuffers = new Dictionary<string, List<SpreadData>>();
        var tradeBuffers = new Dictionary<string, List<TradeData>>();
        var batchSize = _configuration.GetValue<int>("Recording:BatchSize", 1000);

        try
        {
            await foreach (var data in _channelReader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    var hourlyPartitionDir = Path.Combine(dataRoot,
                        $"exchange={data.Exchange}",
                        $"symbol={data.Symbol}",
                        $"date={data.Timestamp:yyyy-MM-dd}",
                        $"hour={data.Timestamp.Hour:D2}");

                    if (data is SpreadData spreadData)
                    {
                        if (!spreadBuffers.TryGetValue(hourlyPartitionDir, out var buffer))
                        {
                            buffer = new List<SpreadData>();
                            spreadBuffers[hourlyPartitionDir] = buffer;
                        }
                        buffer.Add(spreadData);

                        if (buffer.Count >= batchSize)
                        {
                            Directory.CreateDirectory(hourlyPartitionDir);
                            var filePath = Path.Combine(hourlyPartitionDir, $"spreads-{data.Timestamp:mm-ss.fffffff}.parquet");
                            await FlushSpreadBufferAsync(filePath, buffer);
                        }
                    }
                    else if (data is TradeData tradeData)
                    {
                        if (!tradeBuffers.TryGetValue(hourlyPartitionDir, out var buffer))
                        {
                            buffer = new List<TradeData>();
                            tradeBuffers[hourlyPartitionDir] = buffer;
                        }
                        buffer.Add(tradeData);

                        if (buffer.Count >= batchSize)
                        {
                            Directory.CreateDirectory(hourlyPartitionDir);
                            var filePath = Path.Combine(hourlyPartitionDir, $"trades-{data.Timestamp:mm-ss.fffffff}.parquet");
                            await FlushTradeBufferAsync(filePath, buffer);
                        }
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
            await FlushAllBuffersAsync(spreadBuffers, tradeBuffers);
        }
    }

    private async Task FlushSpreadBufferAsync(string filePath, List<SpreadData> buffer)
    {
        if (!buffer.Any()) return;
        await WriteSpreadsAsync(filePath, buffer);
        Console.WriteLine($"[DataCollector] Wrote {buffer.Count} spread records to {filePath}.");
        buffer.Clear();
    }

    private async Task FlushTradeBufferAsync(string filePath, List<TradeData> buffer)
    {
        if (!buffer.Any()) return;
        await WriteTradesAsync(filePath, buffer);
        Console.WriteLine($"[DataCollector] Wrote {buffer.Count} trade records to {filePath}.");
        buffer.Clear();
    }

    private async Task FlushAllBuffersAsync(Dictionary<string, List<SpreadData>> spreadBuffers, Dictionary<string, List<TradeData>> tradeBuffers)
    {
        foreach (var (hourlyDir, buffer) in spreadBuffers)
        {
            if (buffer.Any())
            {
                Directory.CreateDirectory(hourlyDir);
                var filePath = Path.Combine(hourlyDir, $"spreads-{DateTime.Now:mm-ss.fffffff}.parquet");
                await FlushSpreadBufferAsync(filePath, buffer);
            }
        }
        foreach (var (hourlyDir, buffer) in tradeBuffers)
        {
            if (buffer.Any())
            {
                Directory.CreateDirectory(hourlyDir);
                var filePath = Path.Combine(hourlyDir, $"trades-{DateTime.Now:mm-ss.fffffff}.parquet");
                await FlushTradeBufferAsync(filePath, buffer);
            }
        }
    }
}