# Бэклог проекта Trader

Этот документ отслеживает технический долг, идеи по улучшению и новые функции для проекта `Trader`.

## Текущий Статус Проекта (Ноябрь 2025)

**Состояние:** Проект находится на стадии **обнаружения возможностей**. Полный арбитражный цикл (Leg 1 и Leg 2) **НЕ выполняется**, так как логика запуска торгов в `DecisionMaker` не реализована.

*   **Обнаружение спредов:** Работает.
*   **Запуск торгов:** **НЕ РАБОТАЕТ** (см. `TODO` в `DecisionMaker.cs`).

**Приоритетная задача:** Реализация логики запуска торгов.

---

## Эпики и Ключевые Задачи

| ID | Задача | Компонент | Приоритет | Статус | Описание |
|----|--------|-----------|----------|--------|----------|
| EPIC-001 | Реализация логики запуска торгов | `DecisionMaker` | Critical | To Do | Реализовать вызов `ArbitrageTrader` или `ReverseArbitrageTrader` из `DecisionMaker.HandleProfitableSpread` на основе `direction`. Включить управление флагом `_isCycleInProgress`. |

## Технический долг

*Примечание: Многие из перечисленных ниже задач могут быть актуальны только после реализации основной торговой логики.*

| ID | Задача | Компонент | Приоритет | Статус | Описание |
|----|--------|-----------|----------|--------|----------|
| TD-001 | Использовать реальное кол-во из ордера на покупке для ордера на продажу | `Core` | High | Blocked | Актуально после реализации торговой логики. |
| TD-002 | Вынести "магические числа" (размер ордера, глубина стакана) в конфигурацию | `Core`, `Bybit` | Medium | To Do |
| TD-003 | Реализовать `CancelAllOrdersAsync` в `BybitLowLatencyWs` | `Bybit` | Medium | To Do |
| TD-004 | Реализовать `CancelOrderAsync` в `BybitLowLatencyWs` | `Bybit` | Medium | To Do |
| TD-005 | Загружать фильтры символов (`tickSize`, `basePrecision`) с биржи, а не хардкодить | `Bybit` | Medium | To Do |
| TD-006 | Заменить `Task.Delay` в `AuthenticateAsync` на ожидание реального ответа | `Bybit` | Low | To Do |
| TD-007 | Реализовать получение баланса через WebSocket в `BybitLowLatencyWs` | `Bybit` | Low | To Do |
| TD-008 | Реализовать подписку на обновления баланса в `BybitExchange` | `Bybit` | Low | To Do |
| TD-009 | Добавить обработку ошибок для `ModifyOrderAsync` | `Core`, `Bybit` | Medium | To Do |
| TD-010 | Реализовать поддержание локального стакана (snapshot + delta) | `Bybit` | High | To Do |
| TD-011 | Реализовать корректное ожидание завершения для `Leg 2` | `Core` | High | Blocked | Актуально после реализации торговой логики. |
| TD-012 | Надежное управление балансом в `BybitTrailingTrader` | `Bybit`, `Core` | High | Blocked | Актуально после реализации торговой логики. |
| TD-013 | Заменить `Math.Round` на `Math.Truncate` для расчетов количества | `Core` | Medium | To Do |

## Новые функции и улучшения

*Примечание: Многие из перечисленных ниже функций могут быть реализованы только после реализации основной торговой логики.*

| ID | Фича | Описание | Приоритет | Статус |
|----|-------|------------|----------|--------|
| FEAT-001 | Динамический выбор размера ордера | Рассчитывать размер ордера на основе доступного баланса и риск-параметров. | Medium | Blocked |
| FEAT-002 | Реализация "Leg 3" | Добавить поддержку третьей биржи (например, OKX) для расширения арбитражных возможностей. | Low | Blocked |
| FEAT-003 | Улучшенный мониторинг и UI | Создать простой веб-интерфейс для отслеживания статуса ботов, текущих PnL и логов в реальном времени. | Low | To Do |

---

## Предложения по изменениям (Исторические)

*Примечание: Эти предложения относятся к предыдущим итерациям проекта и могут быть неактуальны в текущем контексте.*

| ID | Описание | Статус |
|----|-----------|--------|
| [PROPOSAL-2025-0056](proposals/PROPOSAL-2025-0056.md) | Использование точного объема покупки для продажи в Leg 1 | Rejected |
| [PROPOSAL-2025-0055](proposals/PROPOSAL-2025-0055.md) | Исправление ошибки компиляции после PROPOSAL-2025-0054 | Done |
| [PROPOSAL-2025-0054](proposals/PROPOSAL-2025-0054.md) | Исправление передачи объема для продажи в Leg 2 | Done |
| [PROPOSAL-2025-0041](proposals/PROPOSAL-2025-0041.md) | (Временное) Хардкод суммы для разблокировки тестирования Leg 2 | Obsolete |
| [PROPOSAL-2025-0040](proposals/PROPOSAL-2025-0040.md) | Реализация целевой логики полного цикла | Obsolete |
| [PROPOSAL-2025-0039](proposals/PROPOSAL-2025-0039.md) | Исправление оркестратора и уточнение логики Leg 2 | Obsolete |
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
| [PROPOSAL-2025-0030](proposals/PROPOSAL-2025-0030.md) | Исправление `BALANCE_NOT_ENOUGH` (ожидание баланса) | Done |
| [PROPOSAL-2025-0038](proposals/PROPOSAL-2025-0038.md) | Использование единого экземпляра BybitExchange | To Do |
| [PROPOSAL-2025-0037](proposals/PROPOSAL-2025-0037.md) | Реализация полностью замкнутого балансового цикла | To Do |
| [PROPOSAL-2025-0036](proposals/PROPOSAL-2025-0036.md) | Изменение структуры файлов Proposal | Done |
| [PROPOSAL-2025-0031](proposals/PROPOSAL-2025-0031.md) | Исправление округления в `ReverseArbitrageTrader` | Done |
| [PROPOSAL-2025-0032](proposals/PROPOSAL-2025-0032.md) | Внедрение "debouncing" для стабилизации баланса | Done |


---

## Sprint 3: Code Refactoring and Simplification (Planned)

**Status:** Not Started

**Goal:** A comprehensive review and refactoring of the existing codebase to eliminate unnecessary layers of abstraction, remove redundant code, and improve overall code clarity and maintainability.


---

## Sprint 4: Dynamic Leg Switching (Planned)

**Status:** Not Started

**Goal:** Implement a "supervisor" component that dynamically decides which arbitrage leg to execute based on real-time market conditions.