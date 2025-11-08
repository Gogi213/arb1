# Sprint 1 Plan: Fix Real-time Spread Logic & Filtering

## Goal
To correct the real-time data processing logic in the `/api/rolling_window_data` endpoint. The endpoint must calculate real spreads between pairs of exchanges and only show opportunities that meet the filtering criteria from the analyzer's statistics.

## Key Tasks

1.  **[In Progress] Refactor `rolling_window_data_streamer` (`charts/server.py`):**
    *   Load the latest `summary_stats_...csv` file to identify high-opportunity symbols, similar to the `/api/dashboard_data` endpoint.
    *   Instead of iterating through all windows, iterate through the filtered opportunities.
    *   For each opportunity (e.g., `SYMBOL` on `ExchangeA` vs. `ExchangeB`), find the two corresponding `RollingWindowData` objects.
    *   Implement a `join_asof`-like logic to merge the two real-time spread series by timestamp, calculating the `bid/bid` spread.
    *   The output structure for each chart object must be `(symbol, exchange1, exchange2, timestamps, spreads, upper_band, lower_band)`.

2.  **[ToDo] Create Unit Test (`charts/tests/unit_test_realtime_spread.py`):**
    *   Create a new unit test file.
    *   The test will not require a running server. It will directly call the (refactored) `rolling_window_data_streamer` async generator function.
    *   It will mock the `pl.read_csv` call to provide predefined opportunity data.
    *   It will create mock `RollingWindowData` objects to simulate the data that would be in `rolling_window.windows`.
    *   The test will assert that the generator yields the correct number of charts and that the calculated spread values are correct for the mock data.

## Guiding Principles
*   **KISS/YAGNI:** Reuse the existing filtering logic from `/api/dashboard_data`.
*   **Correctness:** Prioritize the correct calculation of inter-exchange spreads over performance for this sprint.