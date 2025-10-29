# Project Roadmap

## I. Core Development & Stabilization (Completed)

-   **[x] Sprint 1: Core Infrastructure & Leg 1 (Gate.io -> Bybit)**
    -   **Status:** Done
    -   **Outcome:** Basic arbitrage leg implemented and tested.

-   **[x] Sprint 2: Leg 2 Implementation & Debugging (Bybit -> Gate.io)**
    -   **Status:** Done
    -   **Outcome:** Full arbitrage cycle is functional. Critical `BALANCE_NOT_ENOUGH` bug resolved.

---

## II. Future Development (Planned)

-   **[ ] Stage 3: Stability & Precision Testing**
    -   Проверка передачи балансов и округлений в цикле `Bybit-Bybit`.
    -   Проведение стресс-теста с бесконечным циклом выполнения.

-   **[ ] Stage 4: Market Analysis & Spread Trading**
    -   Реализация механизма сравнения спредов между несколькими парами/биржами.
    -   Торговля на основе наиболее выгодного спреда.

-   **[ ] Stage 5: Data Automation & Analysis**
    -   Настройка сбора рыночных данных в реальном времени в Data Lake.
    -   Подключение к историческим данным для анализа и отбора наиболее перспективных торговых пар.
