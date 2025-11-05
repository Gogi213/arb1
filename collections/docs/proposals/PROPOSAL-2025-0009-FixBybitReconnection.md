# PROPOSAL-2025-0009: Устранение конфликта переподключения WebSocket для Bybit

## Диагностика

В ходе анализа логов (`logs/websocket.log`) была выявлена значительная нестабильность WebSocket-соединений с биржей Bybit (20 разрывов за наблюдаемый период). Глубокий анализ кода, в частности файла `src/SpreadAggregator.Infrastructure/Services/Exchanges/Base/ExchangeClientBase.cs`, показал наличие потенциального состояния гонки (race condition).

Проблема заключается в том, что базовая библиотека `CryptoExchange.Net` (используемая `Bybit.Net`) имеет собственный встроенный механизм автоматического переподключения. Однако в нашем коде, в методе `HandleConnectionLost`, реализована собственная принудительная логика переподключения, которая активируется при потере соединения.

Этот двойной подход, когда и библиотека, и наше приложение одновременно пытаются восстановить соединение, приводит к конфликту, лишним разрывам и общей нестабильности.

## Предлагаемое изменение

Предлагается удалить нашу кастомную логику переподключения из метода `HandleConnectionLost` в файле `src/SpreadAggregator.Infrastructure/Services/Exchanges/Base/ExchangeClientBase.cs` и полностью доверить процесс восстановления соединения встроенному механизму библиотеки `CryptoExchange.Net`.

**Файл:** `src/SpreadAggregator.Infrastructure/Services/Exchanges/Base/ExchangeClientBase.cs`

```diff
<<<<<<< SEARCH
:start_line:213
-------
        private async void HandleConnectionLost()
        {
            await _resubscribeLock.WaitAsync();
            try
            {
                WebSocketLogger.Log($"[{_parent.ExchangeName}] Connection lost for chunk starting with {_symbols.FirstOrDefault()}. Attempting to resubscribe...");
                await Task.Delay(1000);
                await SubscribeInternalAsync();
            }
            catch (Exception ex)
            {
                WebSocketLogger.Log($"[ERROR] [{_parent.ExchangeName}] Failed to resubscribe for chunk: {ex.Message}");
            }
            finally
            {
                _resubscribeLock.Release();
            }
        }
=======
        private void HandleConnectionLost()
        {
            // Логируем разрыв, но не вмешиваемся.
            // Библиотека CryptoExchange.Net обрабатывает переподключение автоматически.
            WebSocketLogger.Log($"[{_parent.ExchangeName}] Connection lost for chunk starting with {_symbols.FirstOrDefault()}. Library will handle reconnection.");
        }
>>>>>>> REPLACE
```

## Обоснование

1.  **Устранение состояния гонки:** Удаление дублирующей логики исключит конфликт между нашим кодом и библиотекой.
2.  **Следование лучшим практикам:** Документация и здравый смысл подсказывают, что следует использовать встроенные механизмы фреймворков и библиотек, а не изобретать собственные, если в этом нет острой необходимости.
3.  **Упрощение кода:** Код станет проще, чище и будет содержать меньше потенциальных точек отказа. Метод `HandleConnectionLost` станет синхронным, так как ему больше не нужно управлять асинхронными операциями переподписки.

## Оценка рисков

**Низкий.** Единственный риск заключается в том, что автоматическое переподключение библиотеки по какой-то причине не сработает. Однако, судя по логам (`ticker connection restored`), оно работает. Предлагаемое изменение фактически приводит код в соответствие с тем, как библиотека и должна использоваться.

## План тестирования

1.  Применить предложенный `diff`.
2.  Собрать и запустить приложение.
3.  Наблюдать за лог-файлом `logs/websocket.log` в течение продолжительного времени (несколько часов).
4.  Убедиться, что количество сообщений `Connection lost` для Bybit значительно уменьшилось или исчезло совсем.
5.  Проверить, что после редких `Connection lost` соединение успешно восстанавливается (появляется сообщение `connection restored`), и поток данных возобновляется без нашего вмешательства.

## План отката

1.  Вернуть исходный код метода `HandleConnectionLost` с помощью `git restore` или вручную.
2.  Пересобрать и перезапустить приложение.