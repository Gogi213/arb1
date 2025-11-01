# PROPOSAL-2025-0006: Единый канал данных для сохранения в "даталейк"

## 1. Краткая диагностика
В текущей реализации данные о сделках (`TradeData`) отправляются только в WebSocket, но не сохраняются в Parquet-файлы ("даталейк"). Это происходит потому, что канал данных, ведущий к сервису записи (`DataCollectorService`), настроен на прием только одного типа данных — `SpreadData`. Это делает систему неполноценной и нерасширяемой.

## 2. Предлагаемое изменение
Предлагается провести рефакторинг системы каналов данных, чтобы она могла обрабатывать несколько типов рыночных данных.

### Шаг 1: Создать базовый класс `MarketData`
Создадим общий базовый класс для всех типов рыночных данных.

**Новый файл:** `src/SpreadAggregator.Domain/Entities/MarketData.cs`
```csharp
namespace SpreadAggregator.Domain.Entities;

public abstract class MarketData
{
    public required string Exchange { get; init; }
    public required string Symbol { get; init; }
    public DateTime Timestamp { get; set; }
}
```

### Шаг 2: Унаследовать `SpreadData` и `TradeData`
Модифицируем существующие сущности, чтобы они наследовали от `MarketData`.

**Файл для изменения:** `src/SpreadAggregator.Domain/Entities/SpreadData.cs`
```diff
--- a/src/SpreadAggregator.Domain/Entities/SpreadData.cs
+++ b/src/SpreadAggregator.Domain/Entities/SpreadData.cs
@@ -3,32 +3,19 @@
 /// <summary>
 /// Represents spread data for a trading pair.
 /// </summary>
-public class SpreadData
+public class SpreadData : MarketData
 {
-    /// <summary>
-    /// Name of the exchange.
-    /// </summary>
-    public required string Exchange { get; init; }
-
-    /// <summary>
-    /// Trading symbol (e.g., BTC/USDT).
-    /// </summary>
-    public required string Symbol { get; init; }
-
     /// <summary>
     /// The highest price a buyer is willing to pay.
     /// </summary>
     public decimal BestBid { get; init; }
-
     /// <summary>
     /// The lowest price a seller is willing to accept.
     /// </summary>
     public decimal BestAsk { get; init; }
-
     /// <summary>
     /// The calculated bid-ask spread in percentage.
     /// </summary>
     public decimal SpreadPercentage { get; set; }
     public decimal MinVolume { get; set; }
     public decimal MaxVolume { get; set; }
-    public DateTime Timestamp { get; set; }
 }
```

**Файл для изменения:** `src/SpreadAggregator.Domain/Entities/TradeData.cs`
```diff
--- a/src/SpreadAggregator.Domain/Entities/TradeData.cs
+++ b/src/SpreadAggregator.Domain/Entities/TradeData.cs
@@ -3,13 +3,9 @@
 /// <summary>
 /// Represents a single trade event from an exchange.
 /// </summary>
-public class TradeData
+public class TradeData : MarketData
 {
-    public required string Exchange { get; init; }
-    public required string Symbol { get; init; }
     public decimal Price { get; init; }
     public decimal Quantity { get; init; }
     public string Side { get; init; } // "Buy" or "Sell"
-    public DateTime Timestamp { get; init; }
 }
```

### Шаг 3: Изменить тип канала в `Program.cs`
Обновим регистрацию сервиса `Channel`, чтобы он работал с базовым типом `MarketData`.

**Файл для изменения:** `src/SpreadAggregator.Presentation/Program.cs`
```diff
--- a/src/SpreadAggregator.Presentation/Program.cs
+++ b/src/SpreadAggregator.Presentation/Program.cs
@@ -57,8 +57,8 @@
  
                  services.AddSingleton<SpreadCalculator>();
                  services.AddSingleton<VolumeFilter>();
-                 services.AddSingleton(Channel.CreateUnbounded<SpreadData>());
-                 services.AddSingleton(sp => sp.GetRequiredService<Channel<SpreadData>>().Reader);
+                 services.AddSingleton(Channel.CreateUnbounded<MarketData>());
+                 services.AddSingleton(sp => sp.GetRequiredService<Channel<MarketData>>().Reader);
  
                  // Register all exchange clients
                  services.AddSingleton<IExchangeClient, BinanceExchangeClient>();
```

### Шаг 4: Обновить `OrchestrationService`
Сервис должен теперь работать с каналом `Channel<MarketData>`.

**Файл для изменения:** `src/SpreadAggregator.Application/Services/OrchestrationService.cs`
```diff
--- a/src/SpreadAggregator.Application/Services/OrchestrationService.cs
+++ b/src/SpreadAggregator.Application/Services/OrchestrationService.cs
@@ -21,16 +21,16 @@
      private readonly VolumeFilter _volumeFilter;
      private readonly IConfiguration _configuration;
      private readonly IEnumerable<IExchangeClient> _exchangeClients;
-     private readonly Channel<SpreadData> _rawDataChannel;
+     private readonly Channel<MarketData> _rawDataChannel;
      private readonly IDataWriter _dataWriter;
  
-     public ChannelReader<SpreadData> RawDataChannelReader => _rawDataChannel.Reader;
+     public ChannelReader<MarketData> RawDataChannelReader => _rawDataChannel.Reader;
  
      public OrchestrationService(
          IWebSocketServer webSocketServer,
          SpreadCalculator spreadCalculator,
          IConfiguration configuration,
          VolumeFilter volumeFilter,
          IEnumerable<IExchangeClient> exchangeClients,
-         Channel<SpreadData> rawDataChannel,
+         Channel<MarketData> rawDataChannel,
          IDataWriter? dataWriter = null)
      {
          _webSocketServer = webSocketServer;
@@ -115,6 +115,7 @@
         {
             tasks.Add(exchangeClient.SubscribeToTradesAsync(filteredSymbols, async tradeData =>
             {
+                await _rawDataChannel.Writer.WriteAsync(tradeData);
                 var message = JsonSerializer.Serialize(tradeData);
                 await _webSocketServer.BroadcastRealtimeAsync(message);
             }));
```

### Шаг 5: Обновить `ParquetDataWriter`
Это ключевой шаг. Сервис записи должен будет определять тип данных и сохранять их в разные файлы.

**Файл для изменения:** `src/SpreadAggregator.Infrastructure/Services/ParquetDataWriter.cs`
*(Это концептуальный diff, реальная реализация будет сложнее)*
```diff
// ...
public class ParquetDataWriter : IDataWriter
{
    // ...
    public async Task InitializeCollectorAsync(CancellationToken stoppingToken)
    {
        await foreach (var data in _channelReader.ReadAllAsync(stoppingToken))
        {
            if (data is SpreadData spreadData)
            {
                // Логика сохранения SpreadData в spread.parquet
            }
            else if (data is TradeData tradeData)
            {
                // Логика сохранения TradeData в trades.parquet
            }
        }
    }
}
```

## 3. Обоснование
Этот рефакторинг является критически важным для масштабируемости системы. Использование общего базового класса и полиморфного канала данных — это стандартный и надежный паттерн, который позволит в будущем легко добавлять новые типы рыночных данных (например, ликвидации, данные о финансировании) без необходимости переделывать всю систему.

## 4. Оценка рисков
- **Риск:** Средний. Изменения затрагивают ядро системы обработки данных. Основной риск — неправильная реализация логики в `ParquetDataWriter`, которая может привести к потере или повреждению данных.
- **Снижение риска:** Изменения будут вноситься пошагово. После каждого шага будет проводиться проверка. Особое внимание будет уделено тестированию `ParquetDataWriter`.

## 5. План тестирования
1. Одобрить и применить все изменения.
2. Запустить приложение с включенными потоками тикеров и сделок.
3. **Ожидаемый результат:**
    * Приложение работает без ошибок.
    * В "даталейке" (папке с Parquet-файлами) создаются/обновляются два файла: один для спредов, другой для сделок.
    * WebSocket-клиент продолжает получать оба типа данных.

## 6. План отката
1. Откатить все изменения в затронутых файлах.
2. Удалить новый файл `src/SpreadAggregator.Domain/Entities/MarketData.cs`.