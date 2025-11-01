# PROPOSAL-2025-0004: Добавление потока данных о сделках (пилот для Bybit)

## 1. Краткая диагностика
Сейчас приложение обрабатывает только данные из стакана (цены бид/аск). Пользователь попросил добавить поток данных о недавних сделках (trades), чтобы получать более полную картину рынка. Это значительное добавление функционала, которое требует создания нового, параллельного конвейера обработки данных.

## 2. Предлагаемое изменение
Это предложение описывает пилотную реализацию для биржи **Bybit**. Мы введем новую сущность `TradeData` и создадим необходимую инфраструктуру для подписки, обработки и трансляции событий о сделках.

### Шаг 1: Создать сущность `TradeData`
Будет создана новая сущность для представления одного события сделки.

**Новый файл:** `src/SpreadAggregator.Domain/Entities/TradeData.cs`
```csharp
namespace SpreadAggregator.Domain.Entities;

/// <summary>
/// Представляет одно событие сделки с биржи.
/// </summary>
public class TradeData
{
    public required string Exchange { get; init; }
    public required string Symbol { get; init; }
    public decimal Price { get; init; }
    public decimal Quantity { get; init; }
    public string Side { get; init; } // "Buy" или "Sell"
    public DateTime Timestamp { get; init; }
}
```

### Шаг 2: Обновить интерфейс `IExchangeClient`
В интерфейс клиента будет добавлен новый метод для подписки на сделки.

**Файл для изменения:** `src/SpreadAggregator.Application/Abstractions/IExchangeClient.cs`
```diff
--- a/src/SpreadAggregator.Application/Abstractions/IExchangeClient.cs
+++ b/src/SpreadAggregator.Application/Abstractions/IExchangeClient.cs
@@ -33,4 +33,11 @@
      /// <param name="symbols">Символы для подписки.</param>
      /// <param name="onData">Действие, выполняемое при поступлении новых данных.</param>
      Task SubscribeToTickersAsync(IEnumerable<string> symbols, Action<SpreadData> onData);
+
+    /// <summary>
+    /// Подписывается на обновления сделок для списка символов.
+    /// </summary>
+    /// <param name="symbols">Символы для подписки.</param>
+    /// <param name="onData">Действие, выполняемое при поступлении новых данных о сделке.</param>
+    Task SubscribeToTradesAsync(IEnumerable<string> symbols, Action<TradeData> onData);
 }
```

### Шаг 3: Реализовать для `BybitExchangeClient`
`BybitExchangeClient` будет обновлен для реализации нового метода. Это потребует создания новой подписки на поток сделок Bybit.

**Файл для изменения:** `src/SpreadAggregator.Infrastructure/Services/Exchanges/BybitExchangeClient.cs`
*(Это концептуальный diff, реальная реализация будет сложнее и, вероятно, потребует изменения класса `ManagedConnection` для обработки нескольких типов подписок).*
```diff
--- a/src/SpreadAggregator.Infrastructure/Services/Exchanges/BybitExchangeClient.cs
+++ b/src/SpreadAggregator.Infrastructure/Services/Exchanges/BybitExchangeClient.cs
@@ -68,6 +68,12 @@
          await Task.WhenAll(_connections.Select(c => c.StartAsync()));
      }
  
+     public Task SubscribeToTradesAsync(IEnumerable<string> symbols, Action<TradeData> onData)
+     {
+         // ПРИМЕЧАНИЕ: Этот метод будет реализован. Он потребует значительных изменений в классе ManagedConnection,
+         // чтобы обрабатывать второй тип подписки (сделки) наряду со стаканами.
+         return Task.CompletedTask;
+     }
+
      private class ManagedConnection
      // ...
```

### Шаг 4: Обновить `OrchestrationService`
Сервис-оркестратор будет изменен, чтобы инициировать подписку на сделки и транслировать данные.

**Файл для изменения:** `src/SpreadAggregator.Application/Services/OrchestrationService.cs`
```diff
--- a/src/SpreadAggregator.Application/Services/OrchestrationService.cs
+++ b/src/SpreadAggregator.Application/Services/OrchestrationService.cs
@@ -89,6 +89,14 @@
              return;
          }
  
+         // Подписка на сделки
+         await exchangeClient.SubscribeToTradesAsync(filteredSymbols, tradeData =>
+         {
+             // Пока что просто сериализуем и отправляем
+             var message = JsonSerializer.Serialize(tradeData);
+             _webSocketServer.BroadcastRealtimeAsync(message);
+         });
+
          await exchangeClient.SubscribeToTickersAsync(filteredSymbols, async spreadData =>
          {
              if (spreadData.BestAsk == 0) return;

```

## 3. Обоснование
Создание отдельной сущности `TradeData` и параллельного конвейера подписки — это правильный архитектурный подход. Он обеспечивает чистое разделение ответственности, гарантирует целостность данных и создает масштабируемую основу для добавления новых типов данных в будущем. Пилотная реализация для Bybit позволит проверить дизайн перед его внедрением для всех остальных бирж.

## 4. Оценка рисков
- **Риск:** Средний. Это значительное архитектурное изменение. Основной риск заключается в правильном управлении жизненным циклом двух отдельных WebSocket-подписок (стакан и сделки) внутри каждого `ExchangeClient`, особенно в части управления соединениями и логики переподключения.
- **Снижение риска:** Мы снижаем этот риск, сначала реализуя функционал для одной биржи (`Bybit`). Это позволит нам решить сложности в малом масштабе перед полным развертыванием. Критически важно провести тщательное тестирование реализации для Bybit.

## 5. План тестирования
1. Одобрить и применить изменения для пилотной реализации.
2. Запустить приложение.
3. Подключить WebSocket-клиент.
4. **Ожидаемый результат:** Клиент должен получать два типа сообщений: существующие сообщения `SpreadData` и новые сообщения `TradeData` для символов на бирже Bybit.
5. Убедиться, что сообщения `TradeData` содержат корректную информацию (цена, количество, сторона сделки).

## 6. План отката
1. Откатить все изменения в измененных файлах (`OrchestrationService.cs`, `IExchangeClient.cs`, `BybitExchangeClient.cs`).
2. Удалить новый файл `src/SpreadAggregator.Domain/Entities/TradeData.cs`.