@echo off
echo ========================================
echo   LATENCY ANALYSIS - FAST VERSION
echo ========================================
echo.

REM Проверка зависимостей
echo Checking dependencies...
pip show polars >nul 2>&1
if errorlevel 1 (
    echo Installing dependencies...
    pip install -r analysis\requirements.txt
)

echo.
echo Running analysis...
echo.

python analysis\latency_analysis_fast.py

echo.
echo ========================================
echo   DONE! Check results in:
echo   analysis/latency_opportunities.parquet
echo ========================================
pause
