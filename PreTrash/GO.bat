@echo off
cls
echo ================================================================================
echo RUNNING EMA GRADIENT ANALYSIS...
echo ================================================================================
echo.
cd /d "c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom"
python simple_ema_analysis.py
if errorlevel 1 (
    echo.
    echo ================================================================================
    echo ERROR: Script failed with exit code %errorlevel%
    echo ================================================================================
)
echo.
echo ================================================================================
echo DONE - Press any key to exit
echo ================================================================================
pause >nul
