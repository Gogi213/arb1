# PROPOSAL-2025-0074: Исправление ошибки сериализации JSON

## Диагностика
После внедрения расчета скользящих процентилей на клиенте возникает ошибка `Error: Unexpected end of JSON input`. Это свидетельствует о том, что браузер получает невалидный или неполный JSON-ответ от сервера.

Наиболее вероятная причина — наличие в данных несовместимых с JSON значений, таких как `Infinity` или `NaN`. Эти значения могут появиться в результате математических операций (например, деление на ноль при расчете спреда) и не могут быть корректно сериализованы в стандартный JSON, что приводит к ошибке парсинга на клиенте.

## Предлагаемое изменение
Предлагается добавить шаг очистки данных в `charts/server.py` в функции `load_and_process_pair` непосредственно перед их преобразованием в списки для отправки. Этот шаг будет заменять все вхождения `Infinity`, `-Infinity` и `NaN` на `null`.

### Изменения в `charts/server.py`
```python
<<<<<<< SEARCH
:start_line:93
-------
        pl.col('spread').rolling_quantile(quantile=0.90, window_size=window_size, min_periods=1).alias('upper_band'),
        pl.col('spread').rolling_quantile(quantile=0.10, window_size=window_size, min_periods=1).alias('lower_band')
    ])

    # Convert to epoch seconds for uPlot
    timestamps = (result_df.get_column("timestamp").dt.epoch(time_unit="ms") / 1000).to_list()
=======
        pl.col('spread').rolling_quantile(quantile=0.90, window_size=window_size, min_periods=1).alias('upper_band'),
        pl.col('spread').rolling_quantile(quantile=0.10, window_size=window_size, min_periods=1).alias('lower_band')
    ])

    # Заменяем невалидные числовые значения на null перед сериализацией
    result_df = result_df.with_columns([
        pl.col('spread').fill_nan(None).fill_inf(None),
        pl.col('upper_band').fill_nan(None).fill_inf(None),
        pl.col('lower_band').fill_nan(None).fill_inf(None)
    ])

    # Convert to epoch seconds for uPlot
    timestamps = (result_df.get_column("timestamp").dt.epoch(time_unit="ms") / 1000).to_list()
>>>>>>> REPLACE
```

## Обоснование
Это изменение гарантирует, что ответ API всегда будет содержать валидный JSON. Значения `null` корректно обрабатываются как на стороне сервера (сериализуются), так и на стороне клиента (парсятся и игнорируются `uPlot` при отрисовке). Это напрямую устраняет основную причину сообщенной ошибки.

## Оценка рисков
Риск минимален. Изменение не затрагивает основную логику вычислений, а лишь обеспечивает корректную сериализацию данных.

## План тестирования
1.  Перезапустить сервер `charts`: `uvicorn charts.server:app --reload`.
2.  Открыть `charts/index.html` в браузере и очистить кэш.
3.  Убедиться, что ошибка `Unexpected end of JSON input` больше не появляется в консоли разработчика.
4.  Убедиться, что графики по-прежнему корректно отображаются.

## План отката
1.  Отменить изменения в файле `charts/server.py` с помощью `git restore charts/server.py`.