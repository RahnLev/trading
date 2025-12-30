@echo off
echo Starting Dashboard Server (Python) and AI Proxy Service (.NET)...
echo.

REM Start Dashboard Server (Python) in a new window
start "Dashboard Server (Port 51888)" cmd /k "cd /d \"C:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard\" && python server.py"

REM Wait a moment for Python server to start
timeout /t 2 /nobreak >nul

REM Start AI Proxy Service (.NET) in a new window
start "AI Proxy Service (Port 5265)" cmd /k "cd /d \"C:\Users\mm\source\repos\AiProxyService\" && dotnet run --launch-profile http"

echo.
echo Both services are starting in separate windows:
echo - Dashboard Server: http://127.0.0.1:51888
echo - AI Proxy Service: http://localhost:5265
echo.
echo Press any key to exit this window (services will continue running)...
pause >nul

