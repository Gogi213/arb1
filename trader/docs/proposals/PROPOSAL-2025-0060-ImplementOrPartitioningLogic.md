# PROPOSAL-2025-0060: Реализовать логику партиционирования "ИЛИ"

## Диагностика
Текущая логика записи данных в `ParquetDataWriter` создает новые файлы только при заполнении буфера до `batchSize` (1000 записей). Смена часа приводит к созданию нового буфера, но не вызывает запись неполного буфера за предыдущий час. Это приводит к потере данных, если приложение будет остановлено некорректно, и не соответствует требуемой бизнес-логике.

## Предлагаемое изменение
Модифицировать метод `InitializeCollectorAsync` в `ParquetDataWriter.cs`. При поступлении новой записи данных нужно проверять, отличается ли ее час от часа предыдущей записи. Если да, то необходимо принудительно записать все неполные буферы, относящиеся к старому часу, на диск.

**Файл для изменения:** `collections/src/SpreadAggregator.Infrastructure/Services/ParquetDataWriter.cs`

```diff
<<<<<<< SEARCH
:start_line:167
-------
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
=======
        int? lastHour = null;

        try
        {
            await foreach (var data in _channelReader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    var currentHour = data.Timestamp.Hour;
                    if (lastHour.HasValue && lastHour != currentHour)
                    {
                        // Hour has changed, flush all buffers
                        await FlushAllBuffersAsync(spreadBuffers, tradeBuffers);
                    }
                    lastHour = currentHour;

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
>>>>>>> REPLACE
```

## Обоснование
Это изменение вводит явную проверку на смену часа. При смене часа вызывается `FlushAllBuffersAsync`, который записывает все накопленные данные из всех буферов. Это гарантирует, что данные за предыдущий час не будут потеряны и будут сохранены в своих партициях, реализуя требуемую логику "ИЛИ".

## Оценка рисков
Низкий. Изменение добавляет необходимую бизнес-логику, не затрагивая основной механизм записи. Возможна небольшая задержка в момент смены часа из-за записи всех буферов, но это ожидаемое поведение.

## План тестирования
1.  Применить изменения.
2.  Запустить `SpreadAggregator.Presentation`.
3.  Оставить работать на время, пересекающее границу часа (например, с 10:59 до 11:01).
4.  Проверить, что в папке `data/market_data` для предыдущего часа (e.g., `hour=10`) появились parquet-файлы, даже если количество записей в них было меньше 1000.

## План отката
Отменить изменения в файле `ParquetDataWriter.cs`.