# PROPOSAL-2025-0073: Отображение скользящих процентилей на графиках спреда

## Диагностика
Текущая реализация проекта `charts` отображает только график процентного отклонения спреда между двумя биржами. Этого недостаточно для оценки волатильности и определения статистически нормальных границ колебаний спреда. Для эффективного анализа необходимо визуализировать канал, в котором движется спред большую часть времени.

## Предлагаемое изменение
Предлагается добавить на график две дополнительные серии данных:
1.  **Верхняя граница:** 90-й процентиль спреда, рассчитанный по скользящему окну в 200 точек.
2.  **Нижняя граница:** 10-й процентиль спреда, рассчитанный по скользящему окну в 200 точек.

Это будет реализовано следующим образом:

1.  **Бэкенд (`charts/server.py`):** Модифицировать функцию `load_and_process_pair` для вычисления скользящих процентилей с использованием `polars.rolling_quantile`. Новые данные (`upper_band`, `lower_band`) будут добавлены в JSON-ответ API.
2.  **Фронтенд (`charts/index.html`):** Обновить JavaScript-код для приема и отображения трех серий данных в `uPlot`. Границы процентилей будут отрисованы как пунктирные линии.

### Изменения в `charts/server.py`
```python
<<<<<<< SEARCH
:start_line:79
-------
    result_df = merged_df.with_columns(
        (((pl.col('bid_a') / pl.col('bid_b')) - 1) * 100).alias('spread')
    )

    # Фильтруем строки, где спред не мог быть вычислен (из-за отсутствия данных от df_b)
    result_df = result_df.filter(pl.col('spread').is_not_null())

    # Convert to epoch seconds for uPlot
    timestamps = (result_df.get_column("timestamp").dt.epoch(time_unit="ms") / 1000).to_list()
    spreads = result_df.get_column("spread").to_list()

    return {
        "symbol": symbol,
        "exchange1": exchange_a,
        "exchange2": exchange_b,
        "timestamps": timestamps,
        "spreads": spreads
    }
=======
    result_df = merged_df.with_columns(
        (((pl.col('bid_a') / pl.col('bid_b')) - 1) * 100).alias('spread')
    )

    # Фильтруем строки, где спред не мог быть вычислен
    result_df = result_df.filter(pl.col('spread').is_not_null())

    # Рассчитываем скользящие процентили
    window_size = 200
    result_df = result_df.with_columns([
        pl.col('spread').rolling_quantile(quantile=0.90, window_size=window_size, min_periods=1).alias('upper_band'),
        pl.col('spread').rolling_quantile(quantile=0.10, window_size=window_size, min_periods=1).alias('lower_band')
    ])

    # Convert to epoch seconds for uPlot
    timestamps = (result_df.get_column("timestamp").dt.epoch(time_unit="ms") / 1000).to_list()
    spreads = result_df.get_column("spread").to_list()
    upper_bands = result_df.get_column("upper_band").to_list()
    lower_bands = result_df.get_column("lower_band").to_list()

    return {
        "symbol": symbol,
        "exchange1": exchange_a,
        "exchange2": exchange_b,
        "timestamps": timestamps,
        "spreads": spreads,
        "upper_band": upper_bands,
        "lower_band": lower_bands
    }
>>>>>>> REPLACE
```

### Изменения в `charts/index.html`
```html
<<<<<<< SEARCH
:start_line:59
-------
                    const dataForPlot = [
                        chartData.timestamps,
                        chartData.spreads
                    ];

                    renderChart(chartWrapper, dataForPlot);
=======
                    const dataForPlot = [
                        chartData.timestamps,
                        chartData.spreads,
                        chartData.upper_band,
                        chartData.lower_band
                    ];

                    renderChart(chartWrapper, dataForPlot);
>>>>>>> REPLACE
```
```html
<<<<<<< SEARCH
:start_line:78
-------
                series: [
                    {},
                    {
                        stroke: "rgba(75, 192, 192, 1)",
                        width: 1,
                        fill: "rgba(75, 192, 192, 0.1)",
                    }
                ],
=======
                series: [
                    {}, // For timestamps
                    { // Spread
                        stroke: "rgba(80, 150, 255, 1)",
                        width: 1,
                        fill: "rgba(80, 150, 255, 0.1)",
                        label: "Spread"
                    },
                    { // Upper Band
                        stroke: "rgba(255, 100, 100, 0.8)",
                        width: 1,
                        dash: [10, 5],
                        label: "90th Percentile"
                    },
                    { // Lower Band
                        stroke: "rgba(100, 255, 100, 0.8)",
                        width: 1,
                        dash: [10, 5],
                        label: "10th Percentile"
                    }
                ],
>>>>>>> REPLACE
```

## Обоснование
Это изменение позволит визуально оценивать волатильность спреда и быстро идентифицировать аномальные отклонения, выходящие за пределы 10-го и 90-го процентилей. Использование `polars` для вычислений на бэкенде является высокопроизводительным решением. Изменения на фронтенде минимальны и используют стандартные возможности `uPlot`.

## Оценка рисков
- **Производительность:** Вычисление скользящих процентилей незначительно увеличит нагрузку на сервер, однако `polars` оптимизирован для таких операций, и влияние на производительность должно быть минимальным.
- **Недостаточно данных:** Если в наборе данных меньше 200 точек, `rolling_quantile` сгенерирует `null` значения для начальных точек, что является ожидаемым поведением. Параметр `min_periods=1` гарантирует, что расчет начнется даже при неполном окне. `uPlot` корректно обрабатывает `null` значения, не отрисовывая их.

## План тестирования
1.  Запустить сервер `charts`: `uvicorn charts.server:app --reload`.
2.  Открыть `charts/index.html` в веб-браузере.
3.  Убедиться, что на каждом графике отображаются три линии:
    -   Основная линия спреда (синяя).
    -   Верхняя пунктирная линия (красная).
    -   Нижняя пунктирная линия (зеленая).
4.  Проверить консоль разработчика в браузере на наличие ошибок.
5.  Проверить, что легенда графика отображает названия всех трех серий.

## План отката
1.  Вернуть изменения в файлах `charts/server.py` и `charts/index.html` с помощью `git restore <file>`.