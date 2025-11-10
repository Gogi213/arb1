# arb1 Documentation Index

**Последнее обновление:** 2025-11-10
**Версия:** 2.0

---

## Обзор Проекта

**arb1** - это ecosystem для арбитражной торговли криптовалютами, состоящий из трех взаимосвязанных компонентов:

- **Collections** (SpreadAggregator .NET) - Real-time сбор market data с 8 бирж
- **Analyzer** (Python) - Offline анализ исторических данных для поиска profitable пар
- **Trader** (TraderBot .NET) - Execution engine для 2-leg rebalancing циклов

---

## Документация

### 📊 [01. Текущее Состояние](01_current_state.md)
**Что работает и что не работает в каждом проекте**
- Статус Collections: WebSocket server, Parquet storage, Dashboard
- Статус Analyzer: Metrics calculation, CSV export
- Статус Trader: SpreadListener, ArbitrageTrader, DecisionMaker
- Список блокеров и missing functionality

---

### 🏗️ [02. Диаграммы Архитектуры](02_architecture_diagrams.md)
**6 Mermaid диаграмм для визуализации системы**
1. Overall Component Architecture (data flows между проектами)
2. Real-time Data Flow (sequence diagram)
3. DecisionMaker State Machine (5 состояний LEG1/LEG2)
4. Detailed Trading Cycle (все шаги с latencies)
5. Class Diagram (ключевые классы и dependencies)
6. Analyzer → Dashboard → Trader Integration (через OpportunityFilterService)

---

### 🐛 [03. Проблемы Интеграции](03_integration_problems.md)
**2 критические проблемы с решениями**
- **Problem 1:** Symbol Normalization (BLOCKER) - несовместимость форматов
- **Problem 2:** DecisionMaker Incomplete - не запускает трейдеры

Каждая проблема содержит:
- Description + Location (файл:строка)
- Current code + Solution
- Priority + Time Estimate

---

### 🚀 [04. Roadmap Имплементации](04_implementation_roadmap.md)
**2-фазный план до MVP (~4 часа)**
- **Фаза 0:** Исправление блокеров (45 мин)
  - Symbol normalization fix
  - SpreadListener testing
  - TradingConfig in appsettings.json
- **Фаза 1:** Базовая интеграция (3 часа)
  - Program.cs initialization
  - DecisionMaker completion

**Примечание:** Analyzer → Trader integration уже работает через Dashboard (OpportunityFilterService)

Includes: Acceptance Criteria, Risks & Mitigation

---

### 📘 [05. User Guide](05_user_guide.md)
**Руководство для ежедневной работы (после MVP)**
- Ежедневный workflow (5 шагов)
- Troubleshooting (Collections, SpreadListener, DecisionMaker, API errors)
- Долгосрочный roadmap (Фазы 3-6: REST API, Dynamic Thresholds, Dashboard, Orchestration)
- Acceptance criteria для MVP

---

## Quick Start

**1. Изучить текущее состояние:**
```bash
cat docs/01_current_state.md
```

**2. Понять архитектуру:**
```bash
cat docs/02_architecture_diagrams.md
```

**3. Определить что нужно исправить:**
```bash
cat docs/03_integration_problems.md
```

**4. Следовать roadmap:**
```bash
cat docs/04_implementation_roadmap.md
```

**5. После MVP - использовать User Guide:**
```bash
cat docs/05_user_guide.md
```

---

## Контакты и Ресурсы

- **Исходный integration plan:** `docs/architecture_integration_plan.md` (deprecated, заменен на 01-05)
- **Analyzer README:** `analyzer/README.md`
- **Collections docs:** `collections/docs/README.md`
- **Trader flows:** `trader/docs/flows/main_process_flow.md`
- **Trader audit:** `trader/docs/audit/audit_report.md`

---

**Следующий шаг:** Начать с [Текущего Состояния →](01_current_state.md)
