# PROPOSAL-2025-0010: Интеграция информации о шаге цены (Price Step)

## Диагностика

В настоящее время система агрегирует и транслирует только данные о ценах (`SpreadData`), но не предоставляет статической информации об инструментах, такой как шаг цены (`tickSize`), шаг лота (`lotSize`) и т.д. Эта информация критически важна для внешних торговых систем, чтобы они могли выставлять корректные ордера и фильтровать неподходящие инструменты.

Анализ API бирж (на примере Bybit) и соответствующих библиотек (`Bybit.Net`) показал, что эндпоинт, возвращающий список символов (`GetSpotSymbolsAsync`), также предоставляет полную информацию об инструменте, включая фильтры цен и лотов. Мы просто не использовали эти данные.

## Предлагаемое изменение

Предлагается провести рефакторинг для получения, хранения и предоставления полной информации об инструменте.

### 1. Создание новой сущности `SymbolInfo`

Создать новый класс в доменном слое для хранения полной информации о символе.

**Файл:** `src/SpreadAggregator.Domain/Entities/SymbolInfo.cs` (новый)
```csharp
namespace SpreadAggregator.Domain.Entities;

/// <summary>
/// Represents detailed information about a trading symbol on an exchange.
/// </summary>
public class SymbolInfo
{
    /// <summary>
    /// The name of the exchange.
    /// </summary>
    public string Exchange { get; set; } = string.Empty;

    /// <summary>
    /// The symbol name (e.g., BTCUSDT).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The minimum price change for the symbol.
    /// </summary>
    public decimal PriceStep { get; set; }

    /// <summary>
    /// The minimum quantity change for the symbol.
    /// </summary>
    public decimal QuantityStep { get; set; }

    /// <summary>
    /// The minimum notional value for an order.
    /// </summary>
    public decimal MinNotional { get; set; }
}
```

### 2. Изменение интерфейса `IExchangeClient`

Изменить метод `GetSymbolsAsync` в интерфейсе `IExchangeClient`, чтобы он возвращал `IEnumerable<SymbolInfo>` вместо `IEnumerable<string>`.

**Файл:** `src/SpreadAggregator.Application/Abstractions/IExchangeClient.cs`
```diff
<<<<<<< SEARCH
:start_line:10
-------
    /// <summary>
    /// Gets the list of available symbols for the exchange.
    /// </summary>
    Task<IEnumerable<string>> GetSymbolsAsync();
=======
    /// <summary>
    /// Gets detailed information for all available symbols on the exchange.
    /// </summary>
    Task<IEnumerable<SymbolInfo>> GetSymbolsAsync();
>>>>>>> REPLACE
```

### 3. Обновление `BybitExchangeClient`

Обновить реализацию `GetSymbolsAsync` в `BybitExchangeClient` для извлечения `PriceStep` (`tickSize`) и других параметров.

**Файл:** `src/SpreadAggregator.Infrastructure/Services/Exchanges/BybitExchangeClient.cs`
```diff
<<<<<<< SEARCH
:start_line:39
-------
    public override async Task<IEnumerable<string>> GetSymbolsAsync()
    {
        var symbols = await _restClient.V5Api.ExchangeData.GetSpotSymbolsAsync();
        return symbols.Data.List.Select(s => s.Name);
    }
=======
    public override async Task<IEnumerable<SymbolInfo>> GetSymbolsAsync()
    {
        var symbolsData = await _restClient.V5Api.ExchangeData.GetSpotSymbolsAsync();
        if (!symbolsData.Success)
        {
            // Handle error appropriately
            return Enumerable.Empty<SymbolInfo>();
        }

        return symbolsData.Data.List.Select(s => new SymbolInfo
        {
            Exchange = ExchangeName,
            Name = s.Name,
            PriceStep = s.PriceFilter?.TickSize ?? 0,
            QuantityStep = s.LotSizeFilter?.BasePrecision ?? 0,
            MinNotional = s.LotSizeFilter?.MinOrderValue ?? 0
        });
    }
>>>>>>> REPLACE
```

### 4. Обновление `OrchestrationService`

Обновить `OrchestrationService` для работы с новым типом данных. Это потребует изменения способа фильтрации и хранения символов.

*(Конкретный diff для `OrchestrationService` будет большим, но основная идея — заменить `List<string> allSymbols` на `List<SymbolInfo> allSymbolInfo` и соответствующим образом обновить логику.)*

## Обоснование

1.  **Обогащение данных:** Система начнет предоставлять критически важные статические данные, что значительно повысит ее ценность для внешних систем.
2.  **Централизация информации:** Вместо того чтобы заставлять каждый клиентский сервис самостоятельно запрашивать информацию об инструментах, наш агрегатор будет делать это централизованно.
3.  **Повышение эффективности:** Уменьшается количество запросов к API бирж со стороны конечных пользователей.

## Оценка рисков

**Средний.** Это значительное изменение, затрагивающее основной интерфейс `IExchangeClient` и все его реализации.
- **Риск:** Необходимо будет обновить **все 8** классов-клиентов бирж (`GateIoExchangeClient`, `MexcExchangeClient` и т.д.), чтобы они соответствовали новому интерфейсу. Для каждой биржи придется найти соответствующее поле в ее модели данных.
- **Смягчение:** Изменения будут вноситься поэтапно, начиная с Bybit. Для остальных бирж можно временно возвращать "пустые" `SymbolInfo` с нулями, чтобы не блокировать сборку проекта, и реализовывать их по одной.

## План тестирования

1.  Применить изменения для `SymbolInfo`, `IExchangeClient` и `BybitExchangeClient`.
2.  Временно закомментировать или адаптировать код в `OrchestrationService`, который перестал компилироваться.
3.  Запустить приложение в режиме отладки и убедиться, что вызов `GetSymbolsAsync` для Bybit возвращает корректные данные, включая `PriceStep`.
4.  Последовательно реализовать `GetSymbolsAsync` для остальных 7 бирж.
5.  Полностью адаптировать `OrchestrationService` для работы с `SymbolInfo`.
6.  Проверить, что итоговые данные корректно передаются конечному потребителю.

## План отката

1.  С помощью `git` отменить все внесенные изменения.
2.  Вернуть интерфейс `IExchangeClient` и его реализации к исходному состоянию.