# Sprint 3 Plan: Refactoring and Stabilization

## Goal
To eliminate technical debt, improve code structure and maintainability, and ensure the stability of the features implemented in the previous sprints.

## Key Tasks

1.  **[In Progress] Fix Polars Deprecation Warning:**
    *   In `charts/server.py`, replace the deprecated `min_periods` argument with `min_samples` in all `rolling_quantile()` calls to align with the latest `polars` API and remove warnings.

2.  **[ToDo] Refactor Opportunity Loading (DRY):**
    *   The logic for reading and filtering the `summary_stats.csv` file is duplicated in `get_dashboard_data` and `_get_realtime_chart_data`.
    *   Create a single helper function, e.g., `_get_filtered_opportunities()`, that encapsulates this logic.
    *   Update both original functions to call this new helper function.

3.  **[ToDo] Final Regression Test:**
    *   Run `charts/tests/regression_test.py` to confirm that the refactoring has not introduced any regressions.

## Guiding Principles
*   **Maintainability:** Write clean, readable, and reusable code.
*   **Stability:** Ensure all existing functionality remains intact and robust.