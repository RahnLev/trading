@echo off
REM Dashboard Server Starter (port 51888)
REM Run this from the web\dashboard folder, or double-click the .bat file.

cd /d "%~dp0"

echo ============================================
echo  Dashboard Server - Port 51888
echo ============================================
echo.

REM Check Python
python --version >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Python not found. Install Python and add it to PATH.
    echo         https://www.python.org/downloads/
    pause
    exit /b 1
)

REM Optional: install/upgrade dependencies (uncomment next 2 lines if you get import errors)
REM echo Installing dependencies...
REM pip install -r requirements.txt -q

echo Starting server...
echo.
echo  Open in browser: http://127.0.0.1:51888/
echo  To stop: close this window or press Ctrl+C
echo ============================================
echo.

python server.py
if errorlevel 1 (
    echo.
    echo [ERROR] Server exited with an error.
    echo  Common fixes:
    echo   - Port 51888 in use: close other app using it, or set PORT=51889 and run again
    echo   - Missing module: run  pip install -r requirements.txt
    pause
    exit /b 1
)
pause
