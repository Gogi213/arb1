# PROPOSAL-2025-0005: Настраиваемые потоки данных

## 1. Краткая диагностика
Приложение в настоящее время подписывается на оба потока данных (стаканы и сделки) без возможности отключить один из них. Это неэффективно, если пользователю нужен только один тип данных, и приводит к ненужному потреблению ресурсов.

## 2. Предлагаемое изменение
Предлагается добавить возможность управлять активными потоками данных через конфигурационный файл.

### Шаг 1: Обновить `appsettings.json`
Добавить новую секцию `StreamSettings` для включения или отключения потоков.

**Файл для изменения:** `src/SpreadAggregator.Presentation/appsettings.json`
```diff
--- a/src/SpreadAggregator.Presentation/appsettings.json
+++ b/src/SpreadAggregator.Presentation/appsettings.json
@@ -58,4 +58,9 @@
   "Recording": {
     "Enabled": true
   }
+  ,
+  "StreamSettings": {
+    "EnableTickers": true,
+    "EnableTrades": true
+  }
 }
```

### Шаг 2: Обновить `OrchestrationService`
Изменить `OrchestrationService` так, чтобы он читал новую конфигурацию и запускал подписки только в том случае, если они включены.

**Файл для изменения:** `src/SpreadAggregator.Application/Services/OrchestrationService.cs`
```diff
--- a/src/SpreadAggregator.Application/Services/OrchestrationService.cs
+++ b/src/SpreadAggregator.Application/Services/OrchestrationService.cs
@@ -89,33 +89,40 @@
              return;
          }
  
-         var tickerTask = exchangeClient.SubscribeToTickersAsync(filteredSymbols, async spreadData =>
-         {
-             if (spreadData.BestAsk == 0) return;
- 
-             var normalizedSymbol = spreadData.Symbol.Replace("/", "").Replace("-", "").Replace("_", "").Replace(" ", "");
- 
-             var normalizedSpreadData = new SpreadData
-             {
-                 Exchange = spreadData.Exchange,
-                 Symbol = normalizedSymbol,
-                 BestBid = spreadData.BestBid,
-                 BestAsk = spreadData.BestAsk,
-                 SpreadPercentage = _spreadCalculator.Calculate(spreadData.BestBid, spreadData.BestAsk),
-                 MinVolume = minVolume,
-                 MaxVolume = maxVolume,
-                 Timestamp = DateTime.UtcNow
-             };
- 
-             await _rawDataChannel.Writer.WriteAsync(normalizedSpreadData);
- 
-             var message = JsonSerializer.Serialize(normalizedSpreadData);
-             await _webSocketServer.BroadcastRealtimeAsync(message);
-         });
- 
-         var tradeTask = exchangeClient.SubscribeToTradesAsync(filteredSymbols, async tradeData =>
-         {
-             var message = JsonSerializer.Serialize(tradeData);
-             await _webSocketServer.BroadcastRealtimeAsync(message);
-         });
- 
-         await Task.WhenAll(tickerTask, tradeTask);
+        var tasks = new List<Task>();
+        var enableTickers = _configuration.GetValue<bool>("StreamSettings:EnableTickers", true);
+        var enableTrades = _configuration.GetValue<bool>("StreamSettings:EnableTrades", true);
+
+        if (enableTickers)
+        {
+            tasks.Add(exchangeClient.SubscribeToTickersAsync(filteredSymbols, async spreadData =>
+            {
+                if (spreadData.BestAsk == 0) return;
+
+                var normalizedSymbol = spreadData.Symbol.Replace("/", "").Replace("-", "").Replace("_", "").Replace(" ", "");
+                var normalizedSpreadData = new SpreadData
+                {
+                    Exchange = spreadData.Exchange,
+                    Symbol = normalizedSymbol,
+                    BestBid = spreadData.BestBid,
+                    BestAsk = spreadData.BestAsk,
+                    SpreadPercentage = _spreadCalculator.Calculate(spreadData.BestBid, spreadData.BestAsk),
+                    MinVolume = minVolume,
+                    MaxVolume = maxVolume,
+                    Timestamp = DateTime.UtcNow
+                };
+                await _rawDataChannel.Writer.WriteAsync(normalizedSpreadData);
+                var message = JsonSerializer.Serialize(normalizedSpreadData);
+                await _webSocketServer.BroadcastRealtimeAsync(message);
+            }));
+        }
+
+        if (enableTrades)
+        {
+            tasks.Add(exchangeClient.SubscribeToTradesAsync(filteredSymbols, async tradeData =>
+            {
+                var message = JsonSerializer.Serialize(tradeData);
+                await _webSocketServer.BroadcastRealtimeAsync(message);
+            }));
+        }
+
+        await Task.WhenAll(tasks);
      }
```

## 3. Обоснование
Это изменение обеспечивает гибкость, позволяя пользователю выбирать только необходимые данные. Это снижает нагрузку на сеть, процессор и API бирж, что особенно важно при работе с большим количеством символов.

## 4. Оценка рисков
- **Риск:** Низкий. Изменение затрагивает только логику запуска подписок и полностью управляется конфигурацией.

## 5. План тестирования
1. Одобрить и применить изменения.
2. Запустить приложение с разными комбинациями `EnableTickers` и `EnableTrades` в `appsettings.json`.
3. **Ожидаемый результат:** WebSocket-клиент должен получать только те типы данных, которые включены в конфигурации.

## 6. План отката
1. Откатить изменения в файлах `appsettings.json` и `OrchestrationService.cs`.