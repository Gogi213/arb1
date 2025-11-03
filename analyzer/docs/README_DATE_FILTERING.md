# Date Filtering for Analyzer

## Overview
The `run_all_ultra.py` script now supports filtering data by date, allowing you to analyze specific time periods instead of processing all available data.

## Usage Examples

### 1. Analyze All Available Data (default behavior)
```bash
python run_all_ultra.py
```

### 2. Analyze Only Today's Data
```bash
# Using the --today flag (automatically uses current date)
python run_all_ultra.py --today

# Or specify today's date explicitly
python run_all_ultra.py --date 2025-11-03
```

### 3. Analyze a Specific Date
```bash
python run_all_ultra.py --date 2025-11-02
```

### 4. Analyze a Date Range
```bash
# Analyze data from Nov 1 to Nov 3, 2025
python run_all_ultra.py --start-date 2025-11-01 --end-date 2025-11-03
```

### 5. Analyze From a Date Onwards
```bash
# Analyze all data from Nov 2, 2025 onwards
python run_all_ultra.py --start-date 2025-11-02
```

### 6. Analyze Up to a Specific Date
```bash
# Analyze all data up to and including Nov 2, 2025
python run_all_ultra.py --end-date 2025-11-02
```

### 7. Combine with Worker Configuration
```bash
# Use 16 workers and analyze only today's data
python run_all_ultra.py --workers 16 --today

# Use custom worker count with date range
python run_all_ultra.py --workers 8 --start-date 2025-11-01 --end-date 2025-11-03
```

## Command-Line Arguments

| Argument | Type | Description |
|----------|------|-------------|
| `--workers` | integer | Number of parallel workers (default: 3x CPU cores) |
| `--today` | flag | Analyze only today's data |
| `--date` | YYYY-MM-DD | Analyze a specific date (shortcut for `--start-date=DATE --end-date=DATE`) |
| `--start-date` | YYYY-MM-DD | Start date for analysis (inclusive) |
| `--end-date` | YYYY-MM-DD | End date for analysis (inclusive) |

## Date Format

All dates must be in **YYYY-MM-DD** format (e.g., `2025-11-03`).

## How It Works

1. **Date Filtering at Data Loading**: The script filters data at the directory level before reading parquet files, making it efficient.

2. **Inclusive Date Range**: Both `start_date` and `end_date` are inclusive. For example, `--start-date 2025-11-01 --end-date 2025-11-03` will include data from all three days.

3. **Performance**: Filtering by date reduces the amount of data loaded and processed, significantly speeding up analysis for recent data.

## Use Cases

- **Daily Analysis**: Run the analyzer every day with `--today` to only process fresh data
- **Backtesting**: Test strategies on specific historical periods using date ranges
- **Incremental Updates**: Process only new data by specifying `--start-date` as the last processed date
- **Quick Checks**: Analyze recent data quickly without processing the entire history

## Example Workflow

```bash
# Morning routine: analyze yesterday's data
python run_all_ultra.py --date 2025-11-02

# Real-time monitoring: analyze only today
python run_all_ultra.py --today

# Weekly review: analyze the past week
python run_all_ultra.py --start-date 2025-10-27 --end-date 2025-11-03

# Full historical analysis (all available data)
python run_all_ultra.py
```

## Notes

- If no date parameters are specified, the script processes **all available data** in the data directory
- Invalid date formats will show an error message
- If no data exists for the specified date range, the script will report no results
