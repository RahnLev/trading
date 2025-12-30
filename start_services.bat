@echo off
echo Starting both services...
echo.

REM Start Dashboard Server (Python) in a new window
start "Dashboard Server" cmd /k "cd /d \"C:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard\" && python server.py"

REM Wait a moment
timeout /t 2 /nobreak >nul

REM Start AI Proxy Service (.NET) in a new window
start "AI Proxy Service" cmd /k "cd /d \"C:\Users\mm\source\repos\AiProxyService\" && dotnet run --launch-profile http"

echo.
echo Both services started in separate windows.
echo - Dashboard: http://127.0.0.1:51888
echo - AI Proxy: http://localhost:5265
echo.
pause

