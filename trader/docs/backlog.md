# Backlog Проекта "Trader"

## Текущий Статус Проекта (Октябрь 2025)

**Состояние:** Фаза активной разработки завершена. Обе арбитражные "ноги" (`Leg 1` и `Leg 2`) полностью реализованы и функциональны.

*   **Leg 1 (Gate.io -> Bybit):** Полностью завершен и протестирован.
*   **Leg 2 (Bybit -> Gate.io):** Механика полностью завершена. Ключевые проблемы, включая подписку на стакан Bybit и корректную обработку ордеров на продажу в Gate.io, были успешно решены.

**Текущая работа:** Проект находится на стадии оптимизации. Основное внимание уделяется настройке и корректировке торговых объемов для максимизации эффективности.

---

### Технический долг и улучшения

| ID | Задача | Компонент | Приоритет | Статус |
|----|--------|-----------|----------|--------|
| TD-001 | Использовать реальное кол-во из ордера на покупке для ордера на продажу | Core | High | Done |
| TD-011 | Реализовать корректное ожидание завершения для `Leg 2` | Core | High | Done |
| TD-002 | Вынести "магические числа" (размер ордера, глубина стакана) в конфигурацию | Core, Bybit | Medium | To Do |
| TD-003 | Реализовать `CancelAllOrdersAsync` в `BybitLowLatencyWs` | Bybit | Medium | To Do |
| TD-004 | Реализовать `CancelOrderAsync` в `BybitLowLatencyWs` | Bybit | Medium | To Do |
| TD-005 | Загружать фильтры символов (`tickSize`, `basePrecision`) с биржи, а не хардкодить | Bybit | Medium | To Do |
| TD-006 | Заменить `Task.Delay` в `AuthenticateAsync` на ожидание реального ответа | Bybit | Low | To Do |
| TD-007 | Реализовать получение баланса через WebSocket в `BybitLowLatencyWs` | Bybit | Low | To Do |
| TD-008 | Реализовать подписку на обновления баланса в `BybitExchange` | Bybit | Low | To Do |
| TD-009 | Добавить обработку ошибок для `ModifyOrderAsync` | Core, Bybit | Medium | To Do |
| TD-010 | Реализовать поддержание локального стакана (snapshot + delta) | Bybit | High | Done |

### Новые фичи

| ID | Фича | Описание | Приоритет | Статус |
|----|-------|------------|----------|--------|
| FEAT-001 | Динамический выбор размера ордера | Рассчитывать размер ордера на основе доступного баланса и риск-параметров. | Medium | To Do |
| FEAT-002 | Реализация "Leg 3" | Добавить поддержку третьей биржи (например, OKX) для расширения арбитражных возможностей. | Low | To Do |
| FEAT-003 | Улучшенный мониторинг и UI | Создать простой веб-интерфейс для отслеживания статуса ботов, текущих PnL и логов в реальном времени. | Low | To Do |


---

### Предложения по изменениям

| ID | Описание | Статус |
|----|-----------|--------|
| [PROPOSAL-2025-0008](proposals/PROPOSAL-2025-0008.md) | Исправление жизненного цикла Leg 2 | Done |
| [PROPOSAL-2025-0009](proposals/PROPOSAL-2025-0009.md) | Увеличение суммы ордера для Leg 2 | Done |
| [PROPOSAL-2025-0010](proposals/PROPOSAL-2025-0010.md) | Использование реального количества для продажи в Leg 2 | Done |
| [PROPOSAL-2025-0011](proposals/PROPOSAL-2025-0011.md) | Улучшение логирования ошибок в GateIoExchange | Done |
| [PROPOSAL-2025-0012](proposals/PROPOSAL-2025-0012.md) | Исправление адаптера Bybit для использования `cumExecQty` | Done |
| [PROPOSAL-2025-0013](proposals/PROPOSAL-2025-0013.md) | Тестирование `quoteQuantity` для рыночной продажи на Gate.io | Done |
| [PROPOSAL-2025-0014](proposals/PROPOSAL-2025-0014.md) | Добавление отладочного логирования перед продажей на Gate.io | Done |
| [PROPOSAL-2025-0015](proposals/PROPOSAL-2025-0015.md) | Финальное исправление: парсинг и использование `cumExecQty` | Done |
| [PROPOSAL-2025-0016](proposals/PROPOSAL-2025-0016.md) | Исправление уровня доступа для `BybitOrderUpdate` | Done |
| [PROPOSAL-2025-0017](proposals/PROPOSAL-2025-0017.md) | Увеличение суммы ордера для Leg 2 | Done |
| [PROPOSAL-2025-0028](proposals/PROPOSAL-2025-0028.md) | Спринт 1: Декаплинг Leg 1 и Leg 2 | Done |
| [PROPOSAL-2025-0029](proposals/PROPOSAL-2025-0029.md) | Спринт 2: Реализация "идеального свопа" Gate.io | Done |


---

## Sprint 3: Code Refactoring and Simplification (Planned)

**Status:** Not Started

**Goal:** A comprehensive review and refactoring of the existing codebase to eliminate unnecessary layers of abstraction, remove redundant code, and improve overall code clarity and maintainability.

**Details:** See [Sprint 3 Plan](sprint3_refactoring_plan.md).


---

## Sprint 4: Dynamic Leg Switching (Planned)

**Status:** Not Started

**Goal:** Implement a "supervisor" component that dynamically decides which arbitrage leg to execute based on real-time market conditions.

**Details:** See [Sprint 4 Plan](sprint4_dynamic_leg_switching.md).
