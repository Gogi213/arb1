# PROPOSAL-2025-0077: Исправление ошибки нехватки памяти при обработке большого количества пар

## Диагностика
При увеличении количества анализируемых пар в `charts` на стороне клиента возникает ошибка `Error: Unexpected end of JSON input`. Это происходит потому, что сервер пытается обработать все пары одновременно с помощью `asyncio.gather`. Каждый такой процесс загружает в память большие объемы данных, что приводит к исчерпанию оперативной памяти (Out-of-Memory) и аварийному завершению серверного процесса. В результате клиент получает "обрезанный" JSON-ответ, который не может быть обработан.

## Предлагаемое изменение
Предлагается ограничить параллелизм, обрабатывая задачи не все сразу, а небольшими порциями (чанками). Я добавлю в `charts/server.py` вспомогательную функцию `process_in_chunks` и буду использовать ее в эндпоинте `get_dashboard_data` для обработки не более 4 пар одновременно, что предотвратит исчерпание памяти.

### Изменения в `charts/server.py`

1.  **Добавить новую вспомогательную функцию:**
    ```python
    # Вставить после блока импортов
    async def process_in_chunks(tasks, chunk_size: int):
        """Process a list of awaitable tasks in chunks."""
        results = []
        for i in range(0, len(tasks), chunk_size):
            chunk = tasks[i:i + chunk_size]
            results.extend(await asyncio.gather(*chunk))
        return results
    ```

2.  **Модифицировать эндпоинт `get_dashboard_data`:**
    ```python
<<<<<<< SEARCH
:start_line:128
-------
        tasks = [load_and_process_pair(opp, date_to_use) for opp in opportunities]
        results = await asyncio.gather(*tasks)

        # Filter out None results where data was missing or processing failed
=======
        tasks = [load_and_process_pair(opp, date_to_use) for opp in opportunities]
        # Process in chunks to avoid memory exhaustion
        results = await process_in_chunks(tasks, 4)

        # Filter out None results where data was missing or processing failed
>>>>>>> REPLACE
    ```

## Обоснование
Это изменение предотвратит исчерпание памяти, обеспечив стабильную работу сервера даже при большом количестве анализируемых пар. Обработка порциями — это стандартная и надежная практика для решения подобных проблем с памятью при асинхронной обработке.

## Оценка рисков
Риск минимален. Общее время выполнения может незначительно увеличиться из-за снижения степени параллелизма, но это необходимый компромисс для обеспечения стабильности работы приложения.

## План тестирования
1.  Перезапустить сервер `charts`: `uvicorn charts.server:app --reload`.
2.  Убедиться, что в `analyzer/summary_stats/` есть файл с большим количеством пар (>50).
3.  Открыть `charts/index.html` в браузере.
4.  Убедиться, что все графики загружаются, а в консоли разработчика отсутствует ошибка `Unexpected end of JSON input`.

## План отката
1.  Отменить изменения в файле `charts/server.py` с помощью `git restore charts/server.py`.