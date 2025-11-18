# PROPOSAL-2025-0100: Специализация документа с определением роли для проекта Trader

## Диагностика

Документы, определяющие роль (`role_definition.md`) для проектов `trader` и `collections`, на данный момент являются точными копиями друг друга. Это приводит к следующим проблемам:
1.  **Избыточность**: Требуется двойная работа для поддержания документов в актуальном состоянии.
2.  **Отсутствие специфики**: Общий характер документа не отражает уникальные компоненты, проблемы и технические фокусы проекта `trader`.
3.  **Снижение полезности**: Документ менее полезен в качестве руководства для разработчиков, работающих именно над `trader`, так как в нем отсутствуют прямые ссылки на ключевые файлы и проблемы проекта.

## Предлагаемое изменение

Я предлагаю добавить новый раздел "7. Project-Specific Context" в файл `trader/docs/role_definition.md`. Этот раздел будет содержать информацию, относящуюся исключительно к проекту `trader`.

```
<<<<<<< SEARCH
:start_line:70
-------
- **Exchange APIs:**
    - **Bybit:** Intimate knowledge of the custom `BybitLowLatencyWs` client.
    - **Gate.io:** Proficiency with the `jkorf/GateIo.Net` library, particularly its WebSocket client.
=======
- **Exchange APIs:**
    - **Bybit:** Intimate knowledge of the custom `BybitLowLatencyWs` client.
    - **Gate.io:** Proficiency with the `jkorf/GateIo.Net` library, particularly its WebSocket client.

## 7. Project-Specific Context

While sharing a common foundation with other HFT projects, the `trader` project has its own unique components and challenges. This role requires specific knowledge of the following:

- **Core Components:**
    - `TraderBot.Core.csproj`: The heart of the trading logic.
    - `TrailingTrader.cs`: Implementation of the core trailing stop strategy.
    - `SpreadListener.cs`: Listens for spread data and triggers trading decisions.

- **Exchange Implementations:**
    - `BybitExchange.cs` & `BybitTrailingTrader.cs`: Specific logic for the Bybit exchange, including handling of order parameters and WebSocket communication.
    - `GateIoExchange.cs`: Implementation for the Gate.io exchange.

- **Known Issues & Focus Areas:**
    - **Order Value Calculation:** Deep understanding of issues like `ISSUE-001-bybit-order-value.md`, related to correct order value calculation on Bybit.
    - **Market Sell Failures:** Analysis of problems such as `ISSUE-002-gateio-market-sell-fail.md` on Gate.io.
    - **Balance Synchronization:** Ensuring accurate tracking and synchronization of asset balances across exchanges is a critical ongoing task.
>>>>>>> REPLACE
```

## Обоснование

1.  **Повышение релевантности**: Документ станет более релевантным и полезным для разработчиков, работающих над проектом `trader`.
2.  **Улучшение адаптации**: Новые члены команды смогут быстрее понять ключевые компоненты и текущие проблемы проекта.
3.  **Дифференциация проектов**: Это создаст четкое различие между ролями в `trader` и `collections`, даже если их базовые принципы совпадают.
4.  **Соответствие принципам**: Изменение соответствует принципу "Evidence Over Speculation", так как оно основано на анализе существующей структуры файлов и задокументированных проблем.

## Оценка рисков

- **Риск**: Минимальный. Изменение затрагивает только документацию и не влияет на код.
- **Смягчение**: Не требуется.

## План тестирования

1.  Проверить, что файл `trader/docs/role_definition.md` был успешно обновлен.
2.  Убедиться, что форматирование Markdown не нарушено.

## План отката

- Восстановить предыдущую версию файла `trader/docs/role_definition.md` из системы контроля версий.