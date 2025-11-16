# Debugging Guide for CBASTestingIndicator3 Logging Issue

## Setup

1. **Compile the code** in NinjaTrader (F5 or Build menu)
2. **Start NinjaTrader** and load your strategy
3. **In Cursor/VS Code**, go to Run and Debug (F5 or Ctrl+Shift+D)
4. **Select "Attach to NinjaTrader"** from the dropdown
5. **Click the green play button** or press F5 to attach

## Breakpoints Added

The code now has `System.Diagnostics.Debugger.Break()` calls at critical points. When the debugger hits these, execution will pause and you can inspect variables.

### Breakpoint #0: State.Active (Line ~555)
**When it hits:** When the indicator enters Active state (after properties are set by strategy)

**Check these variables:**
- `EnableLogging` - should be `true`
- `LogFolder` - should be set to `/Users/mm/Documents/NinjaTrader 8/Indicator_logs` (or your custom path)
- `logInitialized` - should be `false` at this point
- `Globals.UserDataDir` - check what path it contains

**Action:** Step into `InitLogger()` (F11)

---

### Breakpoint #2: OnBarUpdate - Before InitLogger (Line ~714)
**When it hits:** When OnBarUpdate tries to initialize logging

**Check these variables:**
- `EnableLogging` - should be `true`
- `logInitialized` - should be `false`
- `CurrentBar` - current bar index

**Action:** Step into `InitLogger()` (F11)

---

### Breakpoint #3: InitLogger Start (Line ~1960)
**When it hits:** First line of InitLogger method

**Check these variables:**
- `EnableLogging` - should be `true`
- `logInitialized` - should be `false`
- `LogFolder` - check the path value
- `Globals.UserDataDir` - check the path value

**Action:** Step through the method (F10) and watch:
- Path normalization logic
- Directory creation
- File creation
- StreamWriter initialization

**Key things to verify:**
1. After path normalization, `folder` should be `/Users/mm/Documents/NinjaTrader 8/Indicator_logs` (not `C:\Users\...`)
2. `Directory.CreateDirectory(folder)` should succeed
3. `logPath` should be a valid macOS path
4. `logWriter` should not be null after creation
5. `logInitialized` should be `true` at the end

---

### Breakpoint #6: FlushCachedBar (Line ~2291)
**When it hits:** When trying to write cached bar data to log

**Check these variables:**
- `EnableLogging` - should be `true`
- `logInitialized` - should be `true`
- `hasCachedBar` - should be `true`
- `cachedBarIndex` - bar index being flushed
- `LogSignalsOnly` - if `true`, only logs bars with signals

**Action:** Step through to see if it calls `MaybeLogRow()` and eventually `SafeWriteLine()`

---

### Breakpoint #7: SafeWriteLine (Line ~2887)
**When it hits:** When actually writing a line to the log file (only on first 5 bars)

**Check these variables:**
- `line` - the CSV line being written (should not be empty)
- `logInitialized` - should be `true`
- `logWriter` - should not be null
- `logPath` - should be the correct macOS path

**Action:** Step over `logWriter.WriteLine(line)` and `logWriter.Flush()` to see if they execute without errors

**After writing, check:**
- Use the debugger's Immediate Window or Watch window to check: `System.IO.File.Exists(logPath)`
- Check file size: `new System.IO.FileInfo(logPath).Length`

---

## What to Look For

### If Breakpoint #0 or #2 never hits:
- Logging initialization is not being attempted
- Check if `EnableLogging` is actually `true` when the indicator is created

### If Breakpoint #3 hits but path is wrong:
- Path normalization is failing
- Check `Globals.UserDataDir` value
- Verify the `isMacOS` detection logic

### If Breakpoint #3 completes but `logInitialized` is still false:
- An exception occurred in InitLogger
- Check the Output window for error messages
- Check the Exception object in the catch block

### If Breakpoint #6 never hits:
- `FlushCachedBar()` is not being called
- Check if `hasCachedBar` is being set to `true`
- Check if `LogSignalsOnly` is preventing logging

### If Breakpoint #7 hits but file is empty:
- `logWriter.WriteLine()` might be failing silently
- Check for exceptions in the catch block
- Verify file permissions on the log directory

---

## Quick Debugging Commands

In the debugger's **Immediate Window** (Ctrl+Alt+I), you can run:

```csharp
// Check if file exists
System.IO.File.Exists(logPath)

// Get file size
new System.IO.FileInfo(logPath).Length

// Check directory exists
System.IO.Directory.Exists(folder)

// Get current directory
System.IO.Directory.GetCurrentDirectory()

// Check OS
System.Environment.OSVersion.Platform
```

---

## After Debugging

**IMPORTANT:** Remove all `System.Diagnostics.Debugger.Break()` calls before deploying to production!

Search for: `System.Diagnostics.Debugger.Break()` and remove those lines.





