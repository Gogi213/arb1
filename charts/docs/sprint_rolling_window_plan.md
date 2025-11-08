# Sprint Plan: Real-time Rolling Window Visualization

## Goal
To add a new mode to the `charts` dashboard that visualizes the 1-hour real-time rolling window data provided by the `collections` service, while preserving the existing historical analysis functionality.

## Key Tasks

1.  **[In Progress] Frontend Implementation (`charts/index.html`):**
    *   Add a UI toggle to switch between "Historical Analysis" and "Real-time Rolling Window" views.
    *   Implement a JavaScript function to fetch data from the `/api/rolling_window_data` endpoint using the streaming API.
    *   Implement logic to clear the dashboard and render the new charts received from the real-time stream, reusing the existing `renderSingleChart` function.

2.  **[ToDo] Backend Review (`charts/server.py`):**
    *   Verify that the existing `/api/rolling_window_data` endpoint is sufficient for the new functionality. The current implementation calculates percentiles on-the-fly, which is acceptable for the first iteration under the KISS principle. No changes are planned for now.

3.  **[ToDo] Testing (`charts/tests/smoke_test.py`):**
    *   Create a smoke test that starts the server in a separate process.
    *   The test will connect to the `/api/rolling_window_data` endpoint and verify that a `200 OK` status is returned and the content type is correct.
    *   It will attempt to read from the stream for a short, fixed duration to confirm data is being sent.
    *   The test and the server process will have strict timeouts to prevent infinite execution.

## Guiding Principles
*   **KISS/YAGNI:** Reuse existing components (charting functions, streaming logic) wherever possible. Avoid premature backend optimizations.
*   **Zero Code/Zero Copy:** The new feature will leverage the existing in-memory `RollingWindow` object on the backend and stream data directly, minimizing data duplication.
*   **Iterative Development:** Deliver a working feature first, then consider performance enhancements in future sprints if necessary.
