# PROPOSAL-2025-0091: Замена Unbounded Channels на Bounded

**Дата:** 2025-11-08
**Автор:** Kilo Code
**Статус:** Предложено

---

## 1. Компактный диагноз

**Проблема:** В файле `Program.cs` сервисы `RawDataChannel` и `RollingWindowChannel` регистрируются с использованием `Channel.CreateUnbounded<MarketData>()`. Это создает каналы без ограничения размера.

**Последствия:** Если производитель данных (например, `OrchestrationService`, получающий данные от бирж) работает быстрее, чем потребители (`ParquetDataWriter`, `RollingWindowService`), очередь сообщений в канале начинает неограниченно расти, что неизбежно приводит к `OutOfMemoryException` и аварийному завершению работы приложения. Аудит OOM показал, что это может произойти в течение нескольких минут при высокой нагрузке.

## 2. Предлагаемое изменение

Я предлагаю заменить `Channel.CreateUnbounded` на `Channel.CreateBounded` с разумно подобранной емкостью и стратегией `Wait`, чтобы обеспечить обратное давление (backpressure).

```diff
<<<<<<< SEARCH
:start_line:72
-------
                services.AddSingleton<RawDataChannel>(new RawDataChannel(Channel.CreateUnbounded<MarketData>()));
                services.AddSingleton<RollingWindowChannel>(new RollingWindowChannel(Channel.CreateUnbounded<MarketData>()));
=======
                var channelOptions = new BoundedChannelOptions(100_000)
                {
                    FullMode = BoundedChannelFullMode.Wait
                };
                services.AddSingleton<RawDataChannel>(new RawDataChannel(Channel.CreateBounded<MarketData>(channelOptions)));
                services.AddSingleton<RollingWindowChannel>(new RollingWindowChannel(Channel.CreateBounded<MarketData>(channelOptions)));
>>>>>>> REPLACE
```

## 3. Обоснование

- **Предотвращение OOM:** Ограничение размера канала — это самый надежный способ предотвратить переполнение памяти из-за дисбаланса скорости производителя и потребителя.
- **Обратное давление:** `BoundedChannelFullMode.Wait` заставит производителя (писателя в канал) дождаться, пока в канале появится свободное место. Это естественным образом замедлит весь конвейер обработки данных до скорости самого медленного компонента, предотвращая накопление данных.
- **Выбор емкости:** Емкость в `100,000` выбрана как компромисс. Она достаточно велика, чтобы сгладить кратковременные всплески активности, но достаточно мала, чтобы не занимать гигабайты памяти.

## 4. Оценка риска

**Низкий.** Это изменение является стандартной практикой при работе с `System.Threading.Channels` и напрямую устраняет известную критическую проблему. Риск негативных побочных эффектов минимален.

## 5. План тестирования

1.  **Сборка:** Убедиться, что проект `SpreadAggregator.Presentation` успешно компилируется.
2.  **Запуск:** Запустить приложение и убедиться, что оно стартует без ошибок.
3.  **Мониторинг (опционально):** Если есть возможность, подключить профайлер памяти и убедиться, что размер каналов не растет бесконтрольно под нагрузкой.

## 6. Шаги для отката

Вернуть изменения в файле `collections/src/SpreadAggregator.Presentation/Program.cs` к исходному состоянию, используя `Channel.CreateUnbounded<MarketData>()`.