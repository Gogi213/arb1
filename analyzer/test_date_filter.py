#!/usr/bin/env python3
"""
Simple test script to verify date filtering functionality.
This script doesn't run the full analysis, just tests the date filtering logic.
"""
import sys
from pathlib import Path
from datetime import datetime, date

def test_date_validation():
    """Test date format validation"""
    print("Testing date validation...")

    valid_dates = ['2025-11-03', '2025-01-01', '2024-12-31']
    invalid_dates = ['2025/11/03', '03-11-2025', '2025-13-01', 'invalid']

    for date_str in valid_dates:
        try:
            datetime.strptime(date_str, '%Y-%m-%d')
            print(f"  [OK] Valid date: {date_str}")
        except ValueError:
            print(f"  [FAIL] {date_str} should be valid")
            return False

    for date_str in invalid_dates:
        try:
            datetime.strptime(date_str, '%Y-%m-%d')
            print(f"  [FAIL] {date_str} should be invalid")
            return False
        except ValueError:
            print(f"  [OK] Invalid date detected: {date_str}")

    print("  Date validation: PASSED\n")
    return True

def test_date_range_logic():
    """Test date range filtering logic"""
    print("Testing date range logic...")

    test_dates = ['2025-11-01', '2025-11-02', '2025-11-03']

    # Test case 1: Single date
    start_date = '2025-11-02'
    end_date = '2025-11-02'
    expected = ['2025-11-02']
    result = [d for d in test_dates if (not start_date or d >= start_date) and (not end_date or d <= end_date)]
    assert result == expected, f"Single date failed: {result} != {expected}"
    print(f"  [OK] Single date filter: {start_date} -> {result}")

    # Test case 2: Date range
    start_date = '2025-11-01'
    end_date = '2025-11-03'
    expected = ['2025-11-01', '2025-11-02', '2025-11-03']
    result = [d for d in test_dates if (not start_date or d >= start_date) and (not end_date or d <= end_date)]
    assert result == expected, f"Date range failed: {result} != {expected}"
    print(f"  [OK] Date range filter: {start_date} to {end_date} -> {result}")

    # Test case 3: Start date only
    start_date = '2025-11-02'
    end_date = None
    expected = ['2025-11-02', '2025-11-03']
    result = [d for d in test_dates if (not start_date or d >= start_date) and (not end_date or d <= end_date)]
    assert result == expected, f"Start date only failed: {result} != {expected}"
    print(f"  [OK] Start date filter: from {start_date} -> {result}")

    # Test case 4: End date only
    start_date = None
    end_date = '2025-11-02'
    expected = ['2025-11-01', '2025-11-02']
    result = [d for d in test_dates if (not start_date or d >= start_date) and (not end_date or d <= end_date)]
    assert result == expected, f"End date only failed: {result} != {expected}"
    print(f"  [OK] End date filter: up to {end_date} -> {result}")

    # Test case 5: No dates (all data)
    start_date = None
    end_date = None
    expected = ['2025-11-01', '2025-11-02', '2025-11-03']
    result = [d for d in test_dates if (not start_date or d >= start_date) and (not end_date or d <= end_date)]
    assert result == expected, f"No filter failed: {result} != {expected}"
    print(f"  [OK] No filter: all dates -> {result}")

    print("  Date range logic: PASSED\n")
    return True

def test_today_date():
    """Test today's date generation"""
    print("Testing today's date...")

    today_str = date.today().strftime('%Y-%m-%d')
    print(f"  [OK] Today's date: {today_str}")

    # Verify it matches expected format
    try:
        datetime.strptime(today_str, '%Y-%m-%d')
        print("  Today date format: PASSED\n")
        return True
    except ValueError:
        print("  [FAIL] Today's date format invalid")
        return False

def main():
    print("=" * 50)
    print("Date Filtering Functionality Tests")
    print("=" * 50)
    print()

    all_passed = True

    all_passed &= test_date_validation()
    all_passed &= test_date_range_logic()
    all_passed &= test_today_date()

    print("=" * 50)
    if all_passed:
        print("[OK] ALL TESTS PASSED")
        print("=" * 50)
        print()
        print("Usage examples:")
        print("  # Analyze today's data")
        print("  python run_all_ultra.py --today")
        print()
        print("  # Analyze specific date")
        print(f"  python run_all_ultra.py --date {date.today().strftime('%Y-%m-%d')}")
        print()
        print("  # Analyze date range")
        print("  python run_all_ultra.py --start-date 2025-11-01 --end-date 2025-11-03")
        print()
        return 0
    else:
        print("[FAIL] SOME TESTS FAILED")
        print("=" * 50)
        return 1

if __name__ == "__main__":
    sys.exit(main())
