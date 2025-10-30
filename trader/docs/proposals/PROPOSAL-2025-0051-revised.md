# PROPOSAL-2025-0051-revised: Add Maximum Diagnostic Logging to BybitOrderUpdate

## 1. Диагностика

Проблема остается: `CumulativeQuoteQuantity` равно нулю. Предыдущие попытки исправления были слепыми. По вашему указанию, я меняю подход на максимальный сбор диагностических данных.

## 2. Предлагаемое изменение

Это изменение добавляет исчерпывающее логирование в конструктор `BybitOrderUpdate`, чтобы отследить парсинг каждого ключевого поля из JSON.

### `trader/src/Exchanges/Bybit/BybitLowLatencyWs.cs`

```diff
<<<<<<< SEARCH
:start_line:729
-------
        public BybitOrderUpdate(JsonElement data)
        {
            Symbol = data.TryGetProperty("symbol", out var sym) ? sym.GetString() ?? "" : "";
            OrderId = data.TryGetProperty("orderId", out var oid) ? long.Parse(oid.GetString() ?? "0") : 0;
            Price = data.TryGetProperty("price", out var pr) && decimal.TryParse(pr.GetString(), out var price) ? price : 0;
            Quantity = data.TryGetProperty("qty", out var q) && decimal.TryParse(q.GetString(), out var qty) ? qty : 0;
            CumulativeQuantityFilled = data.TryGetProperty("cumExecQty", out var cq) && decimal.TryParse(cq.GetString(), out var cqty) ? cqty : 0;
            CumulativeExecutedValue = data.TryGetProperty("cumExecValue", out var cev) && decimal.TryParse(cev.GetString(), out var ceValue) ? ceValue : 0;
            QuoteQuantity = CumulativeExecutedValue;
            CumulativeQuoteQuantity = CumulativeExecutedValue;
            Status = data.TryGetProperty("orderStatus", out var st) ? st.GetString() ?? "" : "";
            FinishType = Status == "Filled" ? "Filled" : Status == "Cancelled" ? "Cancelled" : null;

            if (data.TryGetProperty("createdTime", out var ct) && long.TryParse(ct.GetString(), out var createMs))
            {
                CreateTime = DateTimeOffset.FromUnixTimeMilliseconds(createMs).UtcDateTime;
            }

            if (data.TryGetProperty("updatedTime", out var ut) && long.TryParse(ut.GetString(), out var updateMs))
            {
                UpdateTime = DateTimeOffset.FromUnixTimeMilliseconds(updateMs).UtcDateTime;
            }
        }
=======
        public BybitOrderUpdate(JsonElement data)
        {
            FileLogger.LogOther($"[DEBUG_PARSE] --- Start Parsing BybitOrderUpdate ---");
            FileLogger.LogOther($"[DEBUG_PARSE] Raw JSON: {data.ToString()}");

            Symbol = data.TryGetProperty("symbol", out var sym) ? sym.GetString() ?? "" : "";
            OrderId = data.TryGetProperty("orderId", out var oid) ? long.Parse(oid.GetString() ?? "0") : 0;
            Price = data.TryGetProperty("price", out var pr) && decimal.TryParse(pr.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var price) ? price : 0;
            Quantity = data.TryGetProperty("qty", out var q) && decimal.TryParse(q.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var qty) ? qty : 0;
            CumulativeQuantityFilled = data.TryGetProperty("cumExecQty", out var cq) && decimal.TryParse(cq.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var cqty) ? cqty : 0;
            
            var rawCumExecValue = "NOT_FOUND";
            if (data.TryGetProperty("cumExecValue", out var cev))
            {
                rawCumExecValue = cev.GetString();
            }
            var didParse = decimal.TryParse(rawCumExecValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var ceValue);
            FileLogger.LogOther($"[DEBUG_PARSE] Field 'cumExecValue': Raw='{rawCumExecValue}', ParsedOK={didParse}, Value={ceValue}");
            CumulativeExecutedValue = didParse ? ceValue : 0;

            QuoteQuantity = CumulativeExecutedValue;
            CumulativeQuoteQuantity = CumulativeExecutedValue;
            Status = data.TryGetProperty("orderStatus", out var st) ? st.GetString() ?? "" : "";
            FinishType = Status == "Filled" ? "Filled" : Status == "Cancelled" ? "Cancelled" : null;

            FileLogger.LogOther($"[DEBUG_PARSE] Parsed Status: {Status}");
            FileLogger.LogOther($"[DEBUG_PARSE] --- End Parsing ---");

            if (data.TryGetProperty("createdTime", out var ct) && long.TryParse(ct.GetString(), out var createMs))
            {
                CreateTime = DateTimeOffset.FromUnixTimeMilliseconds(createMs).UtcDateTime;
            }

            if (data.TryGetProperty("updatedTime", out var ut) && long.TryParse(ut.GetString(), out var updateMs))
            {
                UpdateTime = DateTimeOffset.FromUnixTimeMilliseconds(updateMs).UtcDateTime;
            }
        }
>>>>>>> REPLACE
```

## 3. Обоснование

Этот `diff` добавляет максимальное логирование:
1.  Выводит **весь сырой JSON** объекта ордера.
2.  Детально логирует процесс парсинга `cumExecValue`, включая сырое значение и результат `TryParse`.
3.  Явно указывает `CultureInfo.InvariantCulture` для всех `decimal.TryParse`, чтобы исключить проблемы с локалью.
4.  Логирует и другие ключевые поля, например `Status`.

Это даст нам исчерпывающую картину происходящего внутри конструктора.

## 4. Оценка рисков

-   **Риск:** Нулевой.

## 5. План тестирования

1.  Утвердить и применить изменение.
2.  Запустить сборку и выполнение.
3.  Проанализировать лог `other.txt` на наличие блока `[DEBUG_PARSE]`.

## 6. План отката

-   Удалить добавленные строки логирования.