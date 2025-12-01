================================================================================
DEBUGGING SETUP INSTRUCTIONS FOR NINJATRADER
================================================================================

⚠️ IMPORTANT: The 'clr' debugger type requires the C# extension for VS Code/Cursor.

STEP 1: Install C# Extension (REQUIRED)
---------------------------------------
1. Open Extensions in Cursor:
   - Windows/Linux: Ctrl+Shift+X
   - Mac: Cmd+Shift+X

2. Search for "C#" by Microsoft

3. Install ONE of these:
   - "C# Dev Kit" (recommended - includes full debugging support)
   - OR "C#" (basic extension)

4. Restart Cursor after installation

5. The launch.json configuration will now work!

STEP 2: Attach Debugger
------------------------
1. Compile your code in NinjaTrader (F5 or Build menu)

2. Start NinjaTrader and load your strategy

3. In Cursor:
   - Press F5 or go to Run and Debug (Ctrl+Shift+D / Cmd+Shift+D)
   - Select "Attach to NinjaTrader" from dropdown
   - Click the green play button

4. The debugger will attach and pause at System.Diagnostics.Debugger.Break() calls

STEP 3: Debug Breakpoints
--------------------------
The code has automatic breakpoints at these locations:

- Breakpoint #0: State.Active (line ~555)
  → Check EnableLogging, LogFolder, logInitialized

- Breakpoint #2: OnBarUpdate before InitLogger (line ~714)
  → Check EnableLogging, logInitialized, CurrentBar

- Breakpoint #3: InitLogger start (line ~1960)
  → Check EnableLogging, LogFolder, Globals.UserDataDir

- Breakpoint #6: FlushCachedBar (line ~2291)
  → Check logInitialized, hasCachedBar, cachedBarIndex

- Breakpoint #7: SafeWriteLine (line ~2887, first 5 bars only)
  → Check logWriter, logPath, line content

ALTERNATIVE: If Extension Installation Fails
---------------------------------------------
The code has extensive Print() statements that output to NinjaTrader's
Output window. Check the Output window for diagnostic messages about:
- Path normalization
- File creation
- Write operations
- Error messages

You can also check the log path file:
  C:\Mac\Home\Documents\NinjaTrader 8\tmp\CBASTestingIndicator3_LogPath.txt

================================================================================
