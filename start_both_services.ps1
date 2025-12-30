# Start both Dashboard Server and AI Proxy Service
Write-Host "Starting Dashboard Server (Python) and AI Proxy Service (.NET)..." -ForegroundColor Green
Write-Host ""

# Start Dashboard Server (Python) in a new window
$dashboardPath = "C:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard"
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$dashboardPath'; python server.py" -WindowStyle Normal

# Wait a moment for Python server to start
Start-Sleep -Seconds 2

# Start AI Proxy Service (.NET) in a new window
$aiProxyPath = "C:\Users\mm\source\repos\AiProxyService"
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$aiProxyPath'; dotnet run --launch-profile http" -WindowStyle Normal

Write-Host ""
Write-Host "Both services are starting in separate windows:" -ForegroundColor Cyan
Write-Host "  - Dashboard Server: http://127.0.0.1:51888" -ForegroundColor Yellow
Write-Host "  - AI Proxy Service: http://localhost:5265" -ForegroundColor Yellow
Write-Host ""
Write-Host "Press any key to exit this window (services will continue running)..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

