# Backlog

## 2025-10-21 (Optimization)

- **Optimization:** Reduced log file verbosity by changing the file handler's log level from `DEBUG` to `INFO` in `backtester/logger.py`.
- **Justification:** This change dramatically reduces the size of log files by eliminating high-frequency debug messages from the core simulation loop, making logs cleaner and more focused on essential information.

---

# Backlog

## 2025-10-21

- **Refactor:** Updated the backtester's data loader (`barbotine-arbitrage-bot-main/backtester/data_loader.py`) to support the Parquet format instead of gzipped CSVs.
- **Feature:** Adapted the data loader to a new, unified data structure from Parquet files. This includes parsing string-based numeric fields, converting timestamps, and renaming columns to maintain compatibility with the arbitrage analyzer.
- **Justification:** The change from CSV to Parquet provides significant performance improvements in I/O and allows for a more standardized data schema, simplifying the data loading pipeline.

---

## 2025-10-21 (Refactoring)

- **Refactor:** Removed all files and directories related to live trading and live testing to isolate the backtesting functionality.
- **Justification:** This simplifies the project structure, reduces complexity, and focuses the codebase exclusively on backtesting, aligning with the current development goals. The deleted components were redundant for a pure backtesting environment.

---

## 2025-10-21 (Bugfix)

- **Fix:** Corrected a critical bug in the arbitrage detection logic within `arbitrage_analyzer.py`.
- **Details:** The previous logic incorrectly compared `bestBid` to `bestBid` across exchanges. The logic has been rewritten to correctly model arbitrage by comparing one exchange's `bestAsk` (buy price) with another's `bestBid` (sell price).
- **Justification:** This was a critical flaw that invalidated all previous backtest results. The fix ensures that the backtester now identifies realistic and actionable arbitrage opportunities, making its output reliable.

---

## 2025-10-21 (Strategy Update)

- **Refactor:** Updated the arbitrage detection logic in `arbitrage_analyzer.py` to a **Maker-Taker** model, as per user clarification.
- **Details:** The strategy now simulates placing a limit buy order (Maker) at the `bestBid` on the cheaper exchange and a simultaneous market sell order (Taker) at the `bestBid` on the more expensive exchange. This is validated in both directions.
- **Justification:** This change aligns the backtester's core logic with the intended trading strategy, providing a more accurate simulation. The previous Taker-Taker model was replaced to reflect the goal of capturing spreads by acting as a liquidity provider on one side of the trade.

---

## 2025-10-21 (Optimization)

- **Optimization:** Refactored the `_analyze_initial_data` method in `backtester.py` to prevent memory overflow errors.
- **Details:** The method now processes data symbol-by-symbol in a loop instead of performing a single, large join operation on the entire initial dataset.
- **Justification:** This change significantly reduces peak memory consumption during the initial analysis phase, allowing the backtester to handle large data sessions without crashing.
## 2025-10-22: Refactor Logging System

- **Change:** Refactored the logging system to be instance-based instead of module-based.
- **Reason:** The previous implementation initialized loggers on module import, causing all backtest runs within the same Python session to write to the same timestamped log directory. This change ensures each `Backtester` instance creates its own isolated set of logs.
- **Implementation:**
    - Created a `LoggerManager` class in `backtester/logger.py` to handle the creation and configuration of loggers for a single session.
    - The `Backtester` class now creates an instance of `LoggerManager` upon initialization.
    - Loggers are now passed to dependent components (`ArbitrageAnalyzer`, `ReportGenerator`, `BalanceManager`) via dependency injection instead of being imported directly.
    - Added debug logging to `ArbitrageAnalyzer` to provide more insight into the opportunity-finding process.
## 2025-10-22: Implement Stationarity Scoring Report

- **Change:** Implemented a new, comprehensive stationarity analysis report.
- **Reason:** The previous method for calculating stationarity was flawed (lookahead bias, sample bias) and did not produce an actionable or readable report. The new system provides a clear, ranked list of the most promising mean-reverting pairs for trading.
- **Implementation:**
    - Created a new `StationarityReport` class in `backtester/stationarity_report.py` to encapsulate the analysis logic.
    - The backtester now generates a `stationarity_report.csv` at the beginning of each run.
    - This report analyzes **all** possible symbol-exchange pairs, not just those that produced trades.
    - Introduced a `calculate_stationarity_score` function that uses a hybrid model to rank pairs based on a weighted score of ADF, KPSS, and Hurst exponent metrics.
    - The final CSV is sorted by this score, placing the most promising candidates at the top.
    - The old, flawed `_calculate_stationarity` method in `backtester.py` was removed.
## 2025-10-22: Upgraded to Cointegration Analysis

- **Change:** Replaced the basic stationarity check with a proper Engle-Granger two-step cointegration test.
- **Reason:** The previous "stationarity score" was a good heuristic, but a formal cointegration test provides a statistically rigorous method for identifying pairs suitable for mean-reversion trading (pairs trading). This is the correct approach for the user's stated goal.
- **Implementation:**
    - Added a `calculate_cointegration_test` function to `stationarity_analyzer.py` which performs the Engle-Granger test and returns the p-value and hedge ratio.
    - The `stationarity_report.py` module was updated to use this new function.
    - The final report is now named `cointegration_report.csv` and includes new critical columns: `is_cointegrated` (True/False) and `hedge_ratio`.
    - The report is now sorted primarily by `is_cointegrated` (True values first), then by the stationarity score, to bring the most actionable pairs to the top.