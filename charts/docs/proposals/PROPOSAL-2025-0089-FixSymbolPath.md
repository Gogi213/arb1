# PROPOSAL-2025-0089: Исправление формирования пути к данным символа

## Диагностика
Логи сервера `charts` показывают множество предупреждений `Symbol path not found`. Это происходит из-за несоответствия в логике формирования путей между `analyzer` и `charts`.

-   `analyzer` при создании директорий заменяет символ `/` в имени пары на `#` (например, `BTC/USDT` становится `BTC#USDT`).
-   `charts` при попытке прочитать эти данные **не выполняет** такую замену и ищет неверный путь (например, `.../symbol=BTC/USDT` вместо `.../symbol=BTC#USDT`), что приводит к ошибке.

## Предлагаемое изменение
Предлагается добавить замену символа `/` на `#` в функции `load_data` в файле `charts/server.py` при формировании пути к директории символа.

### Изменения в `charts/server.py`
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

## Обоснование
Это изменение синхронизирует логику формирования путей в `charts` с логикой в `analyzer`, что позволит корректно находить и загружать данные для всех символов.

## Оценка рисков
Риск отсутствует. Это прямое исправление ошибки.

## План тестирования
1.  Перезапустить сервер `charts`.
2.  Открыть `charts/index.html`.
3.  Убедиться, что в логах сервера больше не появляются предупреждения `Symbol path not found`.
4.  Убедиться, что графики для всех пар (включая те, что содержат `/`) успешно загружаются.

## План отката
1.  Отменить изменения в файле `charts/server.py`.