# Dashboard Server Manager
# This PowerShell script provides stable server management

param(
    [string]$Action = "start"
)

$ServerPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$ServerScript = Join-Path $ServerPath "server.py"
$PidFile = Join-Path $ServerPath ".server.pid"

function Start-DashboardServer {
    # Check if already running
    if (Test-Path $PidFile) {
        $pid = Get-Content $PidFile
        if (Get-Process -Id $pid -ErrorAction SilentlyContinue) {
            Write-Host "Server is already running (PID: $pid)"
            return
        }
    }
    
    Write-Host "Starting dashboard server..."
    
    # Start Python in a new process that won't be terminated
    $process = Start-Process -FilePath "python" `
                            -ArgumentList $ServerScript `
                            -WorkingDirectory $ServerPath `
                            -WindowStyle Normal `
                            -PassThru
    
    # Save PID
    $process.Id | Out-File -FilePath $PidFile -Encoding ASCII
    
    Write-Host "Dashboard server started (PID: $($process.Id))"
    Write-Host "Server running on http://127.0.0.1:51888"
    Write-Host ""
    Write-Host "Use 'stop_server.ps1' to stop the server"
}

function Stop-DashboardServer {
    if (-not (Test-Path $PidFile)) {
        Write-Host "No server PID file found. Server may not be running."
        return
    }
    
    $pid = Get-Content $PidFile
    
    try {
        $process = Get-Process -Id $pid -ErrorAction Stop
        Write-Host "Stopping server (PID: $pid)..."
        $process.Kill()
        Start-Sleep -Seconds 1
        Remove-Item $PidFile -Force
        Write-Host "Server stopped."
    }
    catch {
        Write-Host "Server process not found (PID: $pid)"
        Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
    }
}

function Get-ServerStatus {
    if (-not (Test-Path $PidFile)) {
        Write-Host "Server: NOT RUNNING"
        return
    }
    
    $pid = Get-Content $PidFile
    
    if (Get-Process -Id $pid -ErrorAction SilentlyContinue) {
        Write-Host "Server: RUNNING (PID: $pid)"
        Write-Host "URL: http://127.0.0.1:51888"
    }
    else {
        Write-Host "Server: NOT RUNNING (stale PID file)"
        Remove-Item $PidFile -Force
    }
}

# Main logic
switch ($Action.ToLower()) {
    "start" { Start-DashboardServer }
    "stop" { Stop-DashboardServer }
    "restart" {
        Stop-DashboardServer
        Start-Sleep -Seconds 2
        Start-DashboardServer
    }
    "status" { Get-ServerStatus }
    default {
        Write-Host "Usage: server_manager.ps1 [start|stop|restart|status]"
        Write-Host ""
        Write-Host "Examples:"
        Write-Host "  .\server_manager.ps1 start    - Start the server"
        Write-Host "  .\server_manager.ps1 stop     - Stop the server"
        Write-Host "  .\server_manager.ps1 restart  - Restart the server"
        Write-Host "  .\server_manager.ps1 status   - Check server status"
    }
}
