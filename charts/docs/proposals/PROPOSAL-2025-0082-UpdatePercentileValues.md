# PROPOSAL-2025-0082: Изменение значений процентилей на 95/5

## Диагностика
Текущие границы для канала волатильности спреда установлены на 10-й и 90-й процентили. Поступил запрос на их изменение для расширения канала, чтобы он охватывал 90% всех значений спреда, отсекая только по 5% с каждой стороны.

## Предлагаемое изменение
Предлагается изменить значения квантилей в `charts/server.py` с `0.90` и `0.10` на `0.95` и `0.05` соответственно.

### Изменения в `charts/server.py`
```python
<<<<<<< SEARCH
:start_line:108
-------
    result_df = result_df.with_columns([
        pl.col('spread').rolling_quantile(quantile=0.90, window_size=window_size, min_periods=1).alias('upper_band'),
        pl.col('spread').rolling_quantile(quantile=0.10, window_size=window_size, min_periods=1).alias('lower_band')
    ])
=======
    result_df = result_df.with_columns([
        pl.col('spread').rolling_quantile(quantile=0.95, window_size=window_size, min_periods=1).alias('upper_band'),
        pl.col('spread').rolling_quantile(quantile=0.05, window_size=window_size, min_periods=1).alias('lower_band')
    ])
>>>>>>> REPLACE
```

## Обоснование
Это изменение позволит анализировать более широкий, 90%-й доверительный интервал для спреда, что сделает идентификацию действительно аномальных отклонений более точной.

## Оценка рисков
Риск отсутствует. Изменение затрагивает только два числовых литерала в коде.

## План тестирования
1.  Перезапустить сервер `charts`.
2.  Открыть `charts/index.html` в браузере.
3.  Визуально убедиться, что пунктирные линии на графиках (границы канала) стали шире.

## План отката
1.  Вернуть предыдущие значения `0.90` и `0.10` в файле `charts/server.py`.