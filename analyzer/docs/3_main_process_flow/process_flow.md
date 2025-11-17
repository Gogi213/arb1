# Detailed Process Flow of the Analyzer (`run_all_ultra.py`)

This document describes the step-by-step process flow of the `run_all_ultra.py` script, which is designed for analyzing arbitrage opportunities.

## Step 1: Initialization and Argument Parsing

The process begins in the `if __name__ == "__main__":` block.

1.  **`argparse` Parser:** An argument parser is created for command-line inputs. It defines the following parameters:
    *   `--data-path`: The path to the data directory.
    *   `--exchanges`: A list of exchanges to analyze.
    *   `--workers`: The number of parallel processes.
    *   `--date`, `--start-date`, `--end-date`, `--today`: Parameters for filtering data by date.
    *   `--thresholds`: Deviation thresholds for the analysis.

2.  **Date Handling:** The script determines the date range for analysis based on the `--today`, `--date`, `--start-date`, and `--end-date` arguments.

3.  **Main Function Call:** The primary function, `run_ultra_fast_analysis`, is called with the parsed parameters.

## Step 2: `run_ultra_fast_analysis` - Analysis Orchestration

This function serves as the main orchestrator for the entire process.

1.  **Data Discovery (`discover_data`):**
    *   **Input:** The data path (`data_path`).
    *   **Logic:** The function scans the specified directory, finding all `exchange=*` and `symbol=*` subfolders. It creates a `defaultdict(set)` where the key is the symbol name (e.g., `BTC/USDT`) and the value is a set of exchanges where it trades.
    *   **Output:** Returns a `symbols_to_analyze` dictionary containing only the symbols that trade on at least two exchanges.

2.  **Exchange Filtering:** If the `--exchanges` argument was provided, the `symbols_to_analyze` dictionary is filtered to keep only symbols that trade on at least two of the *specified* exchanges.

3.  **Task Formation:**
    *   The script iterates through the filtered `symbols_to_analyze` dictionary.
    *   For each symbol, a task is created as a tuple: `(symbol, list(exchanges), DATA_PATH, start_date, end_date, thresholds)`.
    *   **Key Optimization:** A single task is created per *symbol*, not per *exchange pair*. This avoids redundant data loading.

4.  **Parallel Execution (`multiprocessing.Pool`):**
    *   A pool of worker processes is created. The number of workers is either taken from the `--workers` argument or defaults to `cpu_count() * 3`.
    *   The tasks (a list of tuples) are passed to `pool.imap_unordered(analyze_symbol_batch, tasks)`. `imap_unordered` is used to get results as they are completed, improving responsiveness.

5.  **Result Processing and Aggregation:**
    *   The results from the workers are iterated over. Each result is a list of dictionaries, one for each exchange pair of a given symbol.
    *   Statistics from successful results (`status == "SUCCESS"`) are collected into a single `all_stats` list.
    *   Processing progress is printed to the console.

6.  **Saving and Displaying Statistics:**
    *   If `all_stats` is not empty, it is converted into a `polars.DataFrame`.
    *   The DataFrame is sorted by `zero_crossings_per_minute` (mean reversion frequency).
    *   The results are saved to a CSV file in the `analyzer/summary_stats/` directory with a unique, timestamp-based name.
    *   Two "Top 10" tables are printed to the console: one sorted by mean reversion frequency and the other by the number of complete arbitrage cycles.

## Step 3: `analyze_symbol_batch` - Processing a Single Symbol

This function runs in a separate process and is responsible for analyzing one symbol across all its available exchanges.

1.  **Parallel Data Loading (`ThreadPoolExecutor`):**
    *   **Input:** `symbol`, `exchanges`, `data_path`, `start_date`, `end_date`.
    *   **Logic:** For each exchange in the `exchanges` list, a `load_exchange_symbol_data` task is submitted to a `ThreadPoolExecutor`. This allows for the simultaneous loading of data from different exchanges for the same symbol.
    *   **Output:** An `exchange_data` dictionary where the key is the exchange name and the value is a `polars.DataFrame` of its data.

2.  **Pair Analysis:**
    *   A list of all possible two-exchange combinations is created using `itertools.combinations`.
    *   For each exchange pair (`ex1`, `ex2`), the `analyze_pair_fast` function is called.
    *   **Key Optimization:** The data (`exchange_data[ex1]`, `exchange_data[ex2]`) is passed directly. It is already loaded in memory and does not require re-reading from disk.

3.  **Result Formation:** The function returns a list of dictionaries, each containing `symbol`, `ex1`, `ex2`, `status`, and `stats` (the result from `analyze_pair_fast`).

## Step 4: `load_exchange_symbol_data` - Loading Data from Disk

1.  **Path Discovery:** The function constructs the path to the data, checking two symbol name formats (e.g., `BTC/USDT` and `BTCUSDT`).
2.  **Date-based File Filtering:** If a date range is specified, the function scans the `date=*` directories and selects only those within the range before reading any files.
3.  **Parquet Reading (`pl.scan_parquet`):**
    *   **Key Optimization:** It uses `scan_parquet` on all found files at once, which is significantly faster than reading them one by one.
    *   **Logic:** It selects only the required columns (`Timestamp`, `BestBid`, `BestAsk`), renames them, casts their types to `Float64`, and filters out `null` values.
    *   `.collect()` materializes the lazy DataFrame, and `.sort('timestamp')` orders the data.
4.  **Output:** Returns a sorted `polars.DataFrame` or `None` if no data is found or an error occurs.

## Step 5: `analyze_pair_fast` - Calculating Pair Metrics

This is the analysis core where all computations happen.

1.  **Data Synchronization (`join_asof`):** The data from two exchanges are joined on the `timestamp` column. This aligns quotes that were closest in time to each other.
2.  **Deviation Calculation:** `ratio` (`bid_ex1 / bid_ex2`) and `deviation` (`(ratio - 1.0) * 100`) are calculated. The deviation is critically calculated from `1.0` (price parity) to assess the possibility of a break-even trade.
3.  **Base Metric Calculation:** `max`, `min`, and `mean` for the `deviation` are calculated using `polars`.
4.  **`zero_crossings` Calculation:** The number of times the deviation crosses zero is determined. The logic `sign[i] * sign[i-1] < 0` is used to correctly count only true sign changes.
5.  **`opportunity_cycles` Calculation:**
    *   **Key Logic:** A cycle is considered "complete" only if the deviation first exceeds a `threshold` and then returns to a "neutral zone" (`abs(deviation) < ZERO_THRESHOLD`). This ensures that only opportunities where a trade could be opened and closed are counted.
    *   This logic is implemented in `count_complete_cycles`, which iterates over `numpy` arrays.
6.  **Dictionary Formation:** All calculated metrics (asymmetry, percentage of time above threshold, average cycle duration, etc.) are collected into a single dictionary and returned.