@echo off
setlocal
set PORT=51888
set HOST=127.0.0.1
cd /d "%~dp0"

:: Ensure dependencies (FastAPI + Uvicorn)
python -c "import fastapi, uvicorn" 2>nul
if errorlevel 1 (
  echo Installing Python dependencies (fastapi, uvicorn)...
  python -m pip install -q fastapi "uvicorn[standard]"
)

echo Starting dashboard at http://%HOST%:%PORT%
echo Press Ctrl+C to stop.
python -m uvicorn server:app --host %HOST% --port %PORT%
endlocal
