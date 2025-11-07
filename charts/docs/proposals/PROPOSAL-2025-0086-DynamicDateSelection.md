# PROPOSAL-2025-0086: Динамический выбор даты в Charts

## Диагностика
Приложение `charts` в настоящее время жестко запрограммировано на загрузку данных за вчерашний день (`datetime.now() - timedelta(days=1)`). Это приводит к путанице, так как пользователи ожидают увидеть самые свежие доступные данные, а не данные за конкретную, неявно заданную дату.

## Предлагаемое изменение
Предлагается убрать жестко заданную дату и вместо этого реализовать "умный" механизм, который будет автоматически определять самую последнюю дату, за которую доступны данные.

1.  **Извлечение даты из имени файла:** Мы будем использовать имя файла статистики (например, `summary_stats_20251106_165414.csv`) для определения даты, к которой относятся эти данные.
2.  **Модификация `charts/server.py`:** Обновить логику в `get_dashboard_data` для извлечения даты из имени файла и ее использования.

### Изменения в `charts/server.py`
```python
<<<<<<< SEARCH
:start_line:155
-------
        from datetime import timedelta
        date_to_use = (datetime.now() - timedelta(days=1)).strftime("%Y-%m-%d")

        df_opps = pl.read_csv(stats_file)
=======
        # --- DYNAMIC DATE LOGIC ---
        # Extract date from the filename like 'summary_stats_YYYYMMDD_HHMMSS.csv'
        try:
            date_str_from_filename = latest_file.split('_')[2] # Get the YYYYMMDD part
            date_obj = datetime.strptime(date_str_from_filename, '%Y%m%d')
            date_to_use = date_obj.strftime('%Y-%m-%d')
            logging.info(f"Dynamically determined date to use: {date_to_use}")
        except (IndexError, ValueError) as e:
            logging.error(f"Could not parse date from filename '{latest_file}'. Falling back to yesterday. Error: {e}")
            from datetime import timedelta
            date_to_use = (datetime.now() - timedelta(days=1)).strftime("%Y-%m-%d")

        df_opps = pl.read_csv(stats_file)
>>>>>>> REPLACE
```

## Обоснование
Это изменение сделает приложение `charts` более интуитивным и полезным. Оно всегда будет автоматически отображать данные для самого свежего анализа, избавляя пользователя от необходимости помнить, за какой день были сгенерированы последние статистические данные.

## Оценка рисков
Риск минимален. Изменение затрагивает только логику определения даты и включает в себя запасной вариант (fallback) на старое поведение в случае, если имя файла не соответствует ожидаемому формату.

## План тестирования
1.  Убедиться, что в директории `analyzer/summary_stats/` есть файлы, например, `summary_stats_20251106_... .csv`.
2.  Перезапустить сервер `charts`.
3.  Проверить логи сервера. Убедиться, что в логах появилась строка `Dynamically determined date to use: 2025-11-06`.
4.  Открыть `charts/index.html` и убедиться, что загружены данные за 6-е число.

## План отката
1.  Вернуть предыдущую логику с `timedelta(days=1)` в файле `charts/server.py`.