# PROPOSAL-2025-0083: Конфигурация списка бирж для сбора данных

## Диагностика
Проект `collections` (`SpreadAggregator`) спроектирован таким образом, что список активных для сбора данных бирж определяется наличием соответствующей конфигурационной секции в файле `appsettings.json` в разделе `ExchangeSettings.Exchanges`. Код C# уже содержит реализации клиентов для всех необходимых бирж.

Поступил запрос на ограничение сбора данных только биржами Binance, Gate.io и Bybit. Простое удаление секций нежелательно, так как их может потребоваться восстановить в будущем.

## Предлагаемое изменение
Поскольку формат JSON не поддерживает комментарии, предлагается "отключить" ненужные биржи путем переименования их ключей. Я добавлю префикс `_comment_` к ключам всех бирж, кроме `Binance`, `GateIo` и `Bybit`. Парсер конфигурации .NET проигнорирует эти переименованные секции, но они останутся в файле для будущего использования.

### Изменения в `appsettings.json`
```json
<<<<<<< SEARCH
:start_line:7
-------
      "Binance": {
        "VolumeFilter": {
          "MinUsdVolume": 1500000,
          "MaxUsdVolume": 1000000000
        }
      },
      "Mexc": {
        "VolumeFilter": {
          "MinUsdVolume": 1500000,
          "MaxUsdVolume": 999999999999
        }
      },
      "GateIo": {
        "VolumeFilter": {
          "MinUsdVolume": 1500000,
          "MaxUsdVolume": 1000000000
        }
      },
      "Kucoin": {
        "VolumeFilter": {
          "MinUsdVolume": 1500000,
          "MaxUsdVolume": 1000000000
        }
      },
      "OKX": {
        "VolumeFilter": {
          "MinUsdVolume": 1500000,
          "MaxUsdVolume": 1000000000
        }
      },
      "Bitget": {
        "VolumeFilter": {
          "MinUsdVolume": 1500000,
          "MaxUsdVolume": 1000000000
        }
      },
      "BingX": {
        "VolumeFilter": {
          "MinUsdVolume": 1500000,
          "MaxUsdVolume": 1000000000
        }
      },
      "Bybit": {
=======
      "Binance": {
        "VolumeFilter": {
          "MinUsdVolume": 1500000,
          "MaxUsdVolume": 1000000000
        }
      },
      "_comment_Mexc": {
        "VolumeFilter": {
          "MinUsdVolume": 1500000,
          "MaxUsdVolume": 999999999999
        }
      },
      "GateIo": {
        "VolumeFilter": {
          "MinUsdVolume": 1500000,
          "MaxUsdVolume": 1000000000
        }
      },
      "_comment_Kucoin": {
        "VolumeFilter": {
          "MinUsdVolume": 1500000,
          "MaxUsdVolume": 1000000000
        }
      },
      "_comment_OKX": {
        "VolumeFilter": {
          "MinUsdVolume": 1500000,
          "MaxUsdVolume": 1000000000
        }
      },
      "_comment_Bitget": {
        "VolumeFilter": {
          "MinUsdVolume": 1500000,
          "MaxUsdVolume": 1000000000
        }
      },
      "_comment_BingX": {
        "VolumeFilter": {
          "MinUsdVolume": 1500000,
          "MaxUsdVolume": 1000000000
        }
      },
      "Bybit": {
>>>>>>> REPLACE
```

## Обоснование
Этот подход позволяет безопасно и обратимо отключать конфигурационные секции в JSON-файлах, не нарушая их синтаксис.

## Оценка рисков
Риск отсутствует. Это безопасное изменение конфигурации.

## План тестирования
1.  Запустить проект `collections`.
2.  Проанализировать логи приложения и убедиться, что оно пытается установить соединение и собирать данные только для бирж Binance, Gate.io и Bybit.

## План отката
1.  Удалить префиксы `_comment_` у ключей соответствующих бирж в файле `appsettings.json`.