# Running Both Services Together

This guide explains how to run both the **Dashboard Server** (Python/FastAPI) and the **AI Proxy Service** (.NET/ASP.NET Core) on the same machine.

## Services Overview

1. **Dashboard Server** (Python FastAPI)
   - Port: `51888`
   - URL: `http://127.0.0.1:51888`
   - Location: `C:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard`
   - Purpose: Trading strategy dashboard, data visualization, trade analysis

2. **AI Proxy Service** (.NET ASP.NET Core)
   - Port: `5265` (HTTP) or `7046` (HTTPS)
   - URL: `http://localhost:5265`
   - Location: `C:\Users\mm\source\repos\AiProxyService`
   - Purpose: OpenAI API proxy with chat interface

## Quick Start

### Option 1: Batch File (Windows)
Double-click `start_both_services.bat` in the NinjaTrader Custom directory.

### Option 2: PowerShell Script
Run in PowerShell:
```powershell
.\start_both_services.ps1
```

### Option 3: Manual Start

**Terminal 1 - Dashboard Server:**
```cmd
cd "C:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard"
python server.py
```

**Terminal 2 - AI Proxy Service:**
```cmd
cd "C:\Users\mm\source\repos\AiProxyService"
dotnet run
```

## Verifying Services Are Running

1. **Dashboard Server**: Open `http://127.0.0.1:51888` in your browser
2. **AI Proxy Service**: Open `http://localhost:5265/health` or `http://localhost:5265/ui`

## Environment Variables for AI Proxy Service

The AI Proxy Service requires:
- `OPENAI_API_KEY` - Your OpenAI API key
- `OPENAI_BASE_URL` - (Optional) Defaults to `https://api.openai.com/v1`

These can be set in:
- `Properties/launchSettings.json` (for development)
- Environment variables
- `appsettings.json` (not recommended for API keys)

## Integration

The services run independently but can work together:
- Dashboard Server handles trading data and visualization
- AI Proxy Service provides AI chat capabilities
- Both can be accessed from the same browser

## Troubleshooting

### Port Already in Use
If you get a port conflict:
- **Dashboard (51888)**: Change `PORT` environment variable or edit `server.py`
- **AI Proxy (5265)**: Change port in `Properties/launchSettings.json`

### Services Won't Start
- **Python**: Ensure Python is installed and `server.py` dependencies are installed
- **.NET**: Ensure .NET 8.0 SDK is installed (`dotnet --version`)

### Check if Services Are Running
```cmd
netstat -ano | findstr "51888"
netstat -ano | findstr "5265"
```

