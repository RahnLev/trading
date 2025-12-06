@echo off
REM Dashboard Server Starter
REM This script starts the dashboard server in a new window that stays open

cd /d "%~dp0"
start "Dashboard Server - Port 51888" cmd /k python server.py

echo Dashboard server started in a new window.
echo You can close this window - the server will keep running.
echo To stop the server, close the "Dashboard Server" window.
pause
