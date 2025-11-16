# Get Debugger Working - Step by Step

## The Problem
Anysphere's C# extension doesn't support the 'clr' debugger type needed for .NET Framework.

## Solution: Install Microsoft's C# Extension

### Step 1: Install Microsoft C# Extension in Cursor

1. **Open Extensions**: `Ctrl+Shift+X` (or `Cmd+Shift+X` on Mac)

2. **Search for**: `ms-dotnettools.csharp`

3. **Install**: "C#" by Microsoft (ID: ms-dotnettools.csharp)

4. **OR Install**: "C# Dev Kit" by Microsoft (ID: ms-dotnettools.csdevkit) - This includes full debugging support

5. **Restart Cursor** after installation

### Step 2: Verify Extension Installed

1. Go to Extensions
2. Search for "C#"
3. You should see "C#" by Microsoft installed (not just Anysphere's)

### Step 3: Use the Debugger

1. **Compile** your code in NinjaTrader (F5)
2. **Start NinjaTrader** and load your strategy
3. **In Cursor**: Press F5 or go to Run and Debug
4. **Select**: "Attach to NinjaTrader"
5. **Click play** - Debugger should attach!

The `System.Diagnostics.Debugger.Break()` calls in the code will automatically pause execution.

## Alternative: Use Visual Studio (100% Guaranteed)

If the extension doesn't work in Cursor:

1. **Download Visual Studio Community** (free): https://visualstudio.microsoft.com/downloads/
2. **Install** with ".NET desktop development" workload
3. **Open** `NinjaTrader.Custom.sln` in Visual Studio
4. **Build** the solution (F6)
5. **Start NinjaTrader** and load strategy
6. **In Visual Studio**: Debug > Attach to Process (Ctrl+Alt+P)
7. **Select** "NinjaTrader" process
8. **Click Attach**
9. **Set breakpoints** or let `System.Diagnostics.Debugger.Break()` pause execution

## What You'll See When Debugger Works

- Execution pauses at breakpoints
- Variables visible in Locals window
- Can step through code (F10/F11)
- Can inspect `logPath`, `logWriter`, `logInitialized`, etc.
- Can see exact values of paths, file sizes, etc.

This will let us see exactly what's happening with the logging!




