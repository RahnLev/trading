@echo off
echo Starting Dashboard Server...
start "Dashboard Server" cmd /c "cd /d ""C:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard"" && start-dashboard.cmd"
timeout /t 2 /nobreak >nul
echo Starting AI Proxy Service...
start "AI Proxy Service" cmd /c "cd /d ""C:\Users\mm\source\repos\AiProxyService"" && start-ai-proxy.cmd"
echo Both services started.

