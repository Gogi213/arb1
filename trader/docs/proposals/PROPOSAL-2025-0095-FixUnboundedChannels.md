# PROPOSAL-2025-0095: Замена Unbounded Channels на Bounded

**Дата:** 2025-11-08
**Статус:** 🔵 Предложено

---

## 1. Диагностика

**Проблема:** В файле `collections/src/SpreadAggregator.Presentation/Program.cs` используются неограниченные каналы (`Channel.CreateUnbounded`).

**Риск:** Если обработчик (consumer) работает медленнее, чем поставщик (producer), очередь сообщений в канале будет расти бесконечно, что гарантированно приведет к ошибке `OutOfMemoryException` и падению сервиса. Это самая критическая проблема стабильности, выявленная в ходе аудита.

---

## 2. Предлагаемое изменение

Заменить `Channel.CreateUnbounded` на `Channel.CreateBounded` с разумным лимитом и политикой ожидания (`Wait`), чтобы создать обратное давление (back-pressure) на поставщика данных и предотвратить переполнение.

**Файл:** `collections/src/SpreadAggregator.Presentation/Program.cs`

```diff
--- a/collections/src/SpreadAggregator.Presentation/Program.cs
+++ b/collections/src/SpreadAggregator.Presentation/Program.cs
@@ -69,10 +69,15 @@
                 });
  
                 services.AddSingleton<SpreadCalculator>();
                 services.AddSingleton<VolumeFilter>();
-                services.AddSingleton<RawDataChannel>(new RawDataChannel(Channel.CreateUnbounded<MarketData>()));
-                services.AddSingleton<RollingWindowChannel>(new RollingWindowChannel(Channel.CreateUnbounded<MarketData>()));
+                var channelOptions = new BoundedChannelOptions(100_000)
+                {
+                    FullMode = BoundedChannelFullMode.Wait
+                };
+                services.AddSingleton<RawDataChannel>(new RawDataChannel(Channel.CreateBounded<MarketData>(channelOptions)));
+                services.AddSingleton<RollingWindowChannel>(new RollingWindowChannel(Channel.CreateBounded<MarketData>(channelOptions)));
                 services.AddSingleton(sp => sp.GetRequiredService<RawDataChannel>().Channel.Reader);
  
                 // Register all exchange clients
                 services.AddSingleton<IExchangeClient, BinanceExchangeClient>();

```

---

## 3. Обоснование

- **Предотвращение OOM:** `BoundedChannel` не позволит очереди расти бесконечно.
- **Back-Pressure:** Режим `Wait` заставит быстрый `OrchestrationService` дожидаться медленного `ParquetDataWriter`, что является правильным поведением для синхронизации потока данных и предотвращения их потери.
- **Лимит `100_000`:** Это достаточно большой буфер для сглаживания пиковых нагрузок, но достаточно малый, чтобы не потреблять гигабайты памяти.

---

## 4. План тестирования

1.  **Code Review:** Убедиться, что изменения применены корректно.
2.  **Нагрузочное тестирование (выполняется пользователем):**
    - Запустить сервис.
    - Подать высокую нагрузку (например, с помощью тестового генератора данных).
    - С помощью метрик или отладчика отслеживать `channel.Reader.Count`.
    - **Ожидаемый результат:** Счетчик не должен превышать лимит `100_000`. Потребление памяти должно стабилизироваться.

---

## 5. План отката

- Вернуть предыдущую версию файла `Program.cs` из системы контроля версий.
- Заменить `Channel.CreateBounded` обратно на `Channel.CreateUnbounded`.