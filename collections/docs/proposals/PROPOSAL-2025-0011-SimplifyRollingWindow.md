## Диагностика

Текущая реализация RollingWindowService содержит ненужные агрегации (средний спред, мин/макс, объем), которые не требуются по задаче. Пользователь хочет только хранение данных bid/ask в скользящем окне за последний час.

## Предлагаемое изменение

Упростить RollingWindowData и RollingWindowService, убрав все агрегации и оставив только хранение списков SpreadData и TradeData в скользящем окне.

### Изменения в RollingWindowData.cs:
```csharp
public class RollingWindowData
{
    public required string Exchange { get; init; }
    public required string Symbol { get; init; }
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public List<SpreadData> Spreads { get; set; } = new();
    public List<TradeData> Trades { get; set; } = new();
    // Убрать: AverageSpreadPercentage, MinSpreadPercentage, MaxSpreadPercentage, TotalVolume
}
```

### Изменения в RollingWindowService.cs:
Убрать всю логику расчета агрегаций, оставить только управление окном (добавление/удаление данных).

## Обоснование

Задача требует только incremental rolling window для хранения данных bid/ask за последний час. Агрегации добавляют ненужную сложность и вычислительную нагрузку.

## Оценка рисков

- Низкий риск: упрощение кода, удаление функциональности
- Потенциально может сломать код, который использует удаленные свойства

## План тестирования

1. Запустить приложение
2. Проверить, что данные собираются в rolling window
3. Убедиться, что старые данные (>1 час) удаляются

## План отката

Восстановить удаленные свойства и методы из git history.
