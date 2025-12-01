@echo off
echo Checking CSV logging integrity...
echo.

cd /d "c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom"

echo Looking for CSV files...
dir "indicators_log\*.csv" /O-D /B 2>nul

echo.
echo Running Python verification...
python verify_csv_logging.py

pause
