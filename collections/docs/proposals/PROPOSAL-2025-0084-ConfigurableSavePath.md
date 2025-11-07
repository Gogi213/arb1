# PROPOSAL-2025-0084: Настраиваемый путь сохранения данных

## Диагностика
В настоящее время путь для сохранения `.parquet` файлов жестко закодирован в классе `ParquetDataWriter` как `data/market_data` относительно текущей рабочей директории. Это негибко и может приводить к сохранению данных в непредназначенных для этого местах, в зависимости от того, откуда было запущено приложение.

## Предлагаемое изменение
Предлагается сделать путь сохранения данных настраиваемым через `appsettings.json`.

### 1. Изменения в `appsettings.json`
Добавить новую секцию `Recording` с ключом `DataRootPath`.

```json
{
  "ConnectionStrings": {
    "WebSocket": "ws://127.0.0.1:8181"
  },
  "ExchangeSettings": {
    // ...
  },
  "Recording": {
    "Enabled": true,
    "DataRootPath": "data/market_data" // Новый настраиваемый путь
  },
  "StreamSettings": {
    // ...
  }
}
```

### 2. Изменения в `ParquetDataWriter.cs`
Модифицировать метод `InitializeCollectorAsync`, чтобы он читал путь из конфигурации.

```csharp
<<<<<<< SEARCH
:start_line:159
-------
        var dataRoot = Path.Combine("data", "market_data");
        Directory.CreateDirectory(dataRoot);
        Console.WriteLine($"[DataCollector] Starting to record data with hybrid partitioning into: {dataRoot}");
=======
        var dataRoot = _configuration.GetValue<string>("Recording:DataRootPath", Path.Combine("data", "market_data"));
        Directory.CreateDirectory(dataRoot);
        Console.WriteLine($"[DataCollector] Starting to record data with hybrid partitioning into: {dataRoot}");
>>>>>>> REPLACE
```

## Обоснование
Это изменение сделает приложение более гибким и предсказуемым. Путь сохранения данных будет явно задан в конфигурации, что исключит путаницу и позволит легко его менять при необходимости.

## Оценка рисков
Риск отсутствует. Изменение улучшает конфигурацию и не затрагивает основную логику.

## План тестирования
1.  Запустить проект `collections`.
2.  Убедиться, что в логах отображается правильный путь сохранения, взятый из `appsettings.json`.
3.  Проверить, что файлы `.parquet` создаются по указанному пути.

## План отката
1.  Вернуть жестко закодированный путь в `ParquetDataWriter.cs`.
2.  Удалить ключ `DataRootPath` из `appsettings.json`.