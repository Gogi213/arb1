# PROPOSAL-2025-0075: Исправление ошибки `AttributeError` при очистке данных

## Диагностика
После применения `PROPOSAL-2025-0074` сервер падает с ошибкой `AttributeError: 'Expr' object has no attribute 'fill_inf'`. Это указывает на то, что метод `.fill_inf()` не поддерживается для объектов выражений (`Expr`) в используемой версии библиотеки `polars`.

## Предлагаемое изменение
Предлагается заменить вызов `.fill_inf(None)` на более универсальную конструкцию `when/then/otherwise` для достижения той же цели — замены бесконечных значений на `null`.

### Изменения в `charts/server.py`
```python
<<<<<<< SEARCH
:start_line:98
-------
    # Заменяем невалидные числовые значения на null перед сериализацией
    result_df = result_df.with_columns([
        pl.col('spread').fill_nan(None).fill_inf(None),
        pl.col('upper_band').fill_nan(None).fill_inf(None),
        pl.col('lower_band').fill_nan(None).fill_inf(None)
    ])
=======
    # Заменяем невалидные числовые значения на null перед сериализацией
    result_df = result_df.with_columns([
        pl.when(pl.col('spread').is_infinite()).then(None).otherwise(pl.col('spread')).fill_nan(None).alias('spread'),
        pl.when(pl.col('upper_band').is_infinite()).then(None).otherwise(pl.col('upper_band')).fill_nan(None).alias('upper_band'),
        pl.when(pl.col('lower_band').is_infinite()).then(None).otherwise(pl.col('lower_band')).fill_nan(None).alias('lower_band')
    ])
>>>>>>> REPLACE
```

## Обоснование
Этот подход совместим с более широким диапазоном версий `polars` и является идиоматичным способом условной замены значений в выражениях. Он решает проблему `AttributeError`, сохраняя при этом необходимую логику очистки данных для корректной сериализации в JSON.

## Оценка рисков
Риск отсутствует. Это изменение является исправлением технической ошибки и не меняет бизнес-логику.

## План тестирования
1.  Перезапустить сервер `charts`: `uvicorn charts.server:app --reload`.
2.  Открыть `charts/index.html` в браузере.
3.  Убедиться, что внутренняя ошибка сервера (`Internal server error`) больше не возникает.
4.  Убедиться, что графики отображаются корректно, без ошибок в консоли браузера.

## План отката
1.  Отменить изменения в файле `charts/server.py` с помощью `git restore charts/server.py`.