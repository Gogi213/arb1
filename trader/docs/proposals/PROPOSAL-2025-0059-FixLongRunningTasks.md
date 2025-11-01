# PROPOSAL-2025-0059: Исправить обработку долгоживущих задач подписки

## Диагностика
`OrchestrationService` "зависает" при запуске, потому что он использует `await Task.WhenAll()` для ожидания завершения задач подписки на данные с бирж (`SubscribeToTickersAsync`, `SubscribeToTradesAsync`). Эти задачи являются долгоживущими и по своей природе никогда не завершаются, что блокирует дальнейшее выполнение и может приводить к непредсказуемому поведению, включая проблемы с подключением для `trader`.

## Предлагаемое изменение
Убрать `await Task.WhenAll()` из методов `StartAsync` и `ProcessExchange` в `OrchestrationService.cs`, чтобы задачи подписки запускались в фоновом режиме, не блокируя основной поток.

**Файл для изменения:** `collections/src/SpreadAggregator.Application/Services/OrchestrationService.cs`

```diff
<<<<<<< SEARCH
:start_line:66
-------
        await Task.WhenAll(tasks);
=======
        // Do not await long-running tasks, let them run in the background.
        // await Task.WhenAll(tasks);
>>>>>>> REPLACE
<<<<<<< SEARCH
:start_line:135
-------
            await Task.WhenAll(tasks);
=======
            // Do not await long-running tasks, let them run in the background.
            // await Task.WhenAll(tasks);
>>>>>>> REPLACE
```

## Обоснование
Это изменение позволит задачам подписки работать в фоновом режиме, как и предполагается, не блокируя `OrchestrationService`. Сервис сможет полностью инициализироваться, и WebSocket-сервер будет стабильно доступен для подключения `trader`.

## Оценка рисков
Низкий. Изменение исправляет явную логическую ошибку в обработке асинхронных операций.

## План тестирования
1.  Применить изменения.
2.  Запустить `SpreadAggregator.Presentation`.
3.  Запустить `trader`.
4.  Убедиться, что `trader` успешно подключается к `SpreadAggregator` (в логах `trader` нет ошибки `Connection failed`).
5.  Убедиться, что в консоли `SpreadAggregator` появляются сообщения о получении данных с бирж.

## План отката
Вернуть `await Task.WhenAll(tasks);` в `OrchestrationService.cs`.