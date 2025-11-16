# VS Code Debugging Guide for NinjaTrader

## Quick Start

1. **Set Breakpoint**: Click left margin next to line 518 in `CBASTestingIndicator3.cs`
2. **Start Debugging**: Press `F5` → Select "Attach to NinjaTrader (Auto-find)"
3. **Trigger Code**: In NinjaTrader, press `F5` to compile, then `Ctrl+R` to reload indicator
4. **Debug**: When paused at breakpoint, inspect variables and step through code

## Debugging Controls

- **F5**: Continue execution
- **F10**: Step Over (execute current line, don't go into functions)
- **F11**: Step Into (go into function calls)
- **Shift+F11**: Step Out (exit current function)
- **Ctrl+Shift+F5**: Restart debugging session
- **Shift+F5**: Stop debugging

## Key Breakpoint Locations

### State.DataLoaded
- **Line 518**: Output window logger initialization entry point
- **Line 521**: `InitOutputWindowLogger()` call
- **Line 524**: Success confirmation

### InitOutputWindowLogger()
- **Line 2078**: Method entry (with real-time debug)
- **Line 2085**: Timestamp setting
- **Line 2100**: Folder path determination
- **Line 2110**: Directory creation
- **Line 2120**: File creation
- **Line 2135**: Logger initialization

### OnBarUpdate
- **Line 769**: Fallback output logger init
- **Line 850**: "Waiting for bars" diagnostic

## Debug Console Commands

When paused at a breakpoint, you can type these in the Debug Console:

```
CurrentBar                    // See current bar number
State                         // Check current state
outputWindowLogInitialized    // Check logger flag
outputWindowLogPath           // See log file path
EnableLogging                 // Check if logging enabled
```

## Common Issues

### Breakpoint Not Hit
- Ensure NinjaTrader is running (check Task Manager)
- Verify you pressed F5 in NinjaTrader to compile
- Make sure you pressed Ctrl+R to reload the indicator
- Check that debugger is attached (Debug toolbar visible at top)

### Symbols Not Loaded
- Make sure project is built in **Debug** configuration
- Check that `<DebugType>full</DebugType>` is in .csproj
- Verify PDB files exist in `bin\Debug\` folder

### Can't Attach
- Run VS Code as Administrator
- Check NinjaTrader.exe process ID matches launch.json
- Try "Attach to NinjaTrader (Pick Process)" option

## Current Configuration

- **NinjaTrader PID**: 16896
- **Build Config**: Debug
- **Debug Type**: Full
- **Launch Config**: `.vscode/launch.json`

## Watch Variables

Add these to Watch panel for monitoring:

- `CurrentBar`
- `outputWindowLogInitialized`
- `outputWindowLogPath`
- `State`
- `EnableLogging`
- `LogDrawnSignals`
- `logInitialized`
- `signalLogInitialized`

## Rebuild and Restart

If you make code changes:
1. Stop debugger (Shift+F5)
2. Build: `dotnet build NinjaTrader.Custom.csproj -c Debug`
3. Reload indicator in NinjaTrader (Ctrl+R)
4. Restart debugger (F5)
