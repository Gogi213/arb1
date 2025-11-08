# Sprint 2 Plan: Real-time Updates via WebSocket

## Goal
To replace the one-time HTTP stream for the "Real-time Window" mode with a persistent WebSocket connection, enabling charts to update automatically as new data becomes available on the server.

## Key Tasks

1.  **[In Progress] Backend: WebSocket Endpoint (`charts/server.py`):**
    *   Create a new WebSocket endpoint at `/ws/realtime_charts`.
    *   When a client connects, start a background task for that client.
    *   This task will run in a loop (e.g., every 2 seconds). In each iteration, it will execute the logic from the `rolling_window_data_streamer` (refactored into a helper function) to get the latest chart data.
    *   The results will be sent as a single JSON message (a list of all chart objects) to the connected client.

2.  **[ToDo] Frontend: WebSocket Client (`charts/index.html`):**
    *   Modify the "Load Real-time Window" button's `onclick` event to call a new `connectWebSocket()` function.
    *   The `connectWebSocket()` function will:
        *   Establish a connection to the `/ws/realtime_charts` endpoint.
        *   On receiving a message, it will parse the JSON array of chart data.
        *   It will intelligently update the dashboard: create new charts for new symbols and update existing charts using `uPlot`'s `setData()` method for maximum performance. A map of `uPlot` instances will be maintained for this.

3.  **[ToDo] Testing (`charts/tests/regression_test.py`):**
    *   Create a new regression test file.
    *   The test will use FastAPI's `TestClient` to establish a WebSocket connection to the new endpoint.
    *   It will use the `/api/test/load_mock_data` endpoint to seed the server with data.
    *   It will then receive a message from the WebSocket and assert that the data is structured correctly and contains the expected mock symbols.
    *   The test will have a strict timeout.

## Guiding Principles
*   **KISS/YAGNI:** A simple loop sending full state updates is sufficient. No complex diffing logic on the client or server yet.
*   **Zero Code:** Reuse the data generation logic from Sprint 1.