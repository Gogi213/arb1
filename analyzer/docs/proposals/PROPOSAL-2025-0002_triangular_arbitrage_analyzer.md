# Proposal: PROPOSAL-2025-0002 - Triangular Arbitrage Analyzer

## 1. Compact Diagnostic
The user requires a tool to find triangular arbitrage opportunities on Binance, Bybit, and Gate.io. This functionality needs to be built "by analogy" with the existing pairwise arbitrage analyzer. The initial proposal (PROPOSAL-2025-0001) was incorrect as it misunderstood the core logic of the existing system and the nature of triangular arbitrage. This proposal corrects that mistake.

The existing system analyzes one symbol across two exchanges (e.g., `BTC/USDT` on Binance vs. Bybit). Triangular arbitrage involves three different symbols on a single exchange (e.g., `USDT -> BTC -> ETH -> USDT` on Binance).

## 2. Proposed Change
Create a new, separate analyzer script `run_triangular_analysis.py` that reuses the architectural patterns (parallel processing, Polars-based computation) of `run_all_ultra.py` but implements logic specific to triangular arbitrage.

**New Files to be Created:**

1.  **`run_triangular_analysis.py`**: The main executable script.
2.  **`triangular_analyzer/`**: A new directory to contain the core logic.
    *   `triangular_analyzer/__init__.py`
    *   `triangular_analyzer/discovery.py`: Functions to find valid triangular paths from available symbols on an exchange.
    *   `triangular_analyzer/analysis.py`: The core function to analyze a single triangle's profitability over time.

## 3. Rationale
This approach respects the "by analogy" requirement by:
-   **Separating Concerns**: Creating a new, dedicated script (`run_triangular_analysis.py`) avoids polluting the existing, highly-optimized pairwise analyzer.
-   **Reusing Architecture**: It will leverage the proven high-performance patterns of the existing system: multiprocessing for parallelism, Polars for efficient in-memory computation, and a similar data discovery and batch processing workflow.
-   **Adapting Logic**: It correctly implements the distinct mathematical logic required for triangular arbitrage, focusing on cycles within a single exchange rather than ratios between two.

This lays a clean, robust, and performant foundation for the new functionality.

## 4. Risk Assessment
-   **Risk:** None. This is an additive change. No existing files will be modified.
-   **Mitigation:** N/A.

## 5. Testing Plan
1.  Create the new files and directories as specified.
2.  Run the new script `run_triangular_analysis.py`.
3.  Verify that it correctly discovers potential triangles from the data directory.
4.  Verify that it produces a `triangular_summary_stats_...csv` file with plausible results.

## 6. Rollback Steps
-   Delete the newly created files and directory:
    ```bash
    rm run_triangular_analysis.py
    rm -rf triangular_analyzer/