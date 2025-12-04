@echo off
cd /d "%~dp0"
echo Starting dashboard at http://127.0.0.1:51888
echo Press Ctrl+C to stop.
python -m uvicorn server:app --host 127.0.0.1 --port 51888
