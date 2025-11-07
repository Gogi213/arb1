# PROPOSAL-2025-0088: Комплексное исправление загрузки данных в Charts

## Диагностика
После серии тестов были выявлены две корневые проблемы, мешающие работе `charts`:

1.  **Неверное формирование пути:** Логика в `charts/server.py` не заменяет символ `/` на `#` в именах пар (например, `BTC/USDT`), из-за чего не может найти соответствующие директории с данными, созданные `analyzer`. Это приводит к ошибкам `Symbol path not found`.
2.  **Слишком строгий фильтр:** Фильтр `opportunity_cycles_040bp > 50` отсеивает большинство или все доступные пары, так как они не достигают такого высокого показателя.

## Предлагаемое изменение
Предлагается внести два изменения в `charts/server.py` для решения обеих проблем:

1.  **Исправить путь:** Добавить замену `/` на `#` при формировании пути к данным символа.
2.  **Ослабить фильтр:** Снизить порог фильтрации с `50` до `0`, чтобы отображать все пары, у которых есть хотя бы один торговый цикл.

### Изменения в `charts/server.py`

1.  **Исправление пути в `load_data`:**
    ```python
<<<<<<< SEARCH
:start_line:50
-------
    async def load_data(exchange):
        symbol_path = os.path.join(DATA_LAKE_PATH, f"exchange={exchange}", f"symbol={symbol}")
        if not os.path.exists(symbol_path):
=======
    async def load_data(exchange):
        symbol_path_str = symbol.replace('/', '#')
        symbol_path = os.path.join(DATA_LAKE_PATH, f"exchange={exchange}", f"symbol={symbol_path_str}")
        if not os.path.exists(symbol_path):
>>>>>>> REPLACE
    ```

2.  **Корректировка фильтра в `get_dashboard_data`:**
    ```python
<<<<<<< SEARCH
:start_line:184
-------
        df_filtered = df_opps.filter(pl.col('opportunity_cycles_040bp') > 50)
=======
        df_filtered = df_opps.filter(pl.col('opportunity_cycles_040bp') > 0)
>>>>>>> REPLACE
    ```

## Обоснование
Эти два изменения в совокупности обеспечат корректную работу приложения: данные будут правильно находиться на диске, а фильтр не будет отсеивать все доступные для анализа пары.

## Оценка рисков
Риск минимален. Это исправление двух явных ошибок.

## План тестирования
1.  Перезапустить сервер `charts`.
2.  Открыть `charts/index.html`.
3.  Убедиться, что в логах сервера отсутствуют ошибки `Symbol path not found`.
4.  Убедиться, что на странице отображаются графики.

## План отката
1.  Отменить оба изменения в файле `charts/server.py`.