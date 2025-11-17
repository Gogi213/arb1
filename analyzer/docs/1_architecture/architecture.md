# Detailed Architecture of the Analyzer Project

## 1. Overview

The `analyzer` project is a Python console application designed for high-performance batch analysis of market data. Its architecture is built around the principle of maximum performance through parallel processing and efficient in-memory data manipulation. The application is stateless and is controlled exclusively through command-line arguments.

## 2. Architectural Layers and Components

The architecture can be conceptually divided into several logical layers, implemented as functions within the `run_all_ultra.py` script.

### 2.1. Orchestration Layer (`run_ultra_fast_analysis`)

This is the top-level component that manages the entire analysis process.

*   **Responsibilities:**
    *   Initiating data discovery.
    *   Filtering data based on the provided configuration.
    *   Creating and distributing tasks for parallel processing.
    *   Aggregating the results.
    *   Saving the final report and printing a summary to the console.
*   **Parallelism:** Uses `multiprocessing.Pool` to distribute tasks for analyzing *symbols* across different processes, allowing for full utilization of CPU cores.

### 2.2. Batch Processing Layer (`analyze_symbol_batch`)

This component handles a single large task: analyzing one trading symbol across all exchanges where it is listed.

*   **Responsibilities:**
    *   Loading data for a single symbol from all necessary exchanges.
    *   Organizing the analysis of all possible exchange pairs for that symbol.
    *   Returning the analysis results for all pairs.
*   **Parallelism:** Uses `concurrent.futures.ThreadPoolExecutor` to load data from disk for different exchanges in parallel. This is effective because data loading is an I/O-bound operation.
*   **Key Optimization (Batching):** Data for a single symbol (e.g., `BTC/USDT`) is loaded into memory once and then reused for analyzing all pairs (Bybit-Binance, Bybit-OKX, Binance-OKX, etc.). This eliminates redundant disk I/O operations.

### 2.3. Data Access Layer (`load_exchange_symbol_data`)

This component is responsible for reading and preprocessing data from the disk.

*   **Responsibilities:**
    *   Finding the correct path to a symbol's data, accounting for different naming formats.
    *   Efficiently filtering files by date *before* reading them.
    *   Reading all necessary Parquet files in a single pass (`pl.scan_parquet`).
    *   Performing basic data cleaning and transformation (selecting columns, renaming, casting types, filtering nulls).
*   **Technology:** Uses `polars.scan_parquet` for lazy reading, which allows the Polars engine to optimize the query.

### 2.4. Analysis and Computation Layer (`analyze_pair_fast`)

This is the core of the system, where the actual analysis for a single pair of exchanges occurs.

*   **Responsibilities:**
    *   Synchronizing the time-series data of two exchanges.
    *   Calculating metrics for deviation, mean reversion, and trading opportunities.
*   **Technology:** Exclusively uses `polars` for all computations. This avoids the overhead of zero-copy data transfers between libraries (like `pandas` and `numpy`) and leverages the highly optimized Polars core, written in Rust.
*   **Algorithms:**
    *   `join_asof`: For fast and efficient time-series synchronization.
    *   Vectorized `polars` operations for calculating `ratio` and `deviation`.
    *   A custom `count_complete_cycles` algorithm for counting realistic trading opportunities.

## 3. Data Flow

1.  **Source:** A set of Parquet files on disk, structured by `exchange/symbol/date/hour`.
2.  **Loading:** `load_exchange_symbol_data` reads the files and creates a `polars.DataFrame` in memory for each `(exchange, symbol)`.
3.  **Joining:** `analyze_pair_fast` joins two `DataFrame`s into one using `join_asof`.
4.  **Transformation:** New columns (`ratio`, `deviation`, threshold flags) are created in the joined `DataFrame`.
5.  **Aggregation:** Scalar values (statistics) are extracted from the `DataFrame` using methods like `.mean()`, `.max()`, and `.sum()`.
6.  **Result:** The statistics are collected into a dictionary, then a list of dictionaries, which is finally converted into a final `polars.DataFrame`.
7.  **Output:** The final `DataFrame` is saved to a CSV file.

## 4. Configuration

The application is fully configured via command-line arguments using `argparse`.

*   `--data-path`: Defines the root of the data storage.
*   `--workers`: Allows for performance tuning by controlling the number of processes.
*   `--date`, `--start-date`, `--end-date`: Provide flexibility in selecting the time slice for analysis.
*   `--thresholds`: Allow for tuning the key parameters of the opportunity-finding algorithm.

This architecture makes the application flexible, performant, and lightweight, as it does not require external databases, servers, or complex state management.
