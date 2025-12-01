@echo off
setlocal enabledelayedexpansion
set LOGDIR=%USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\strategy_logs
set ARCHDIR=%LOGDIR%\archive
if not exist "%ARCHDIR%" mkdir "%ARCHDIR%"
for /f "tokens=1-4 delims=/ " %%a in ("%date%") do (
  set YYYY=%%d
  set MM=%%b
  set DD=%%c
)
set HH=%time:~0,2%
set MN=%time:~3,2%
set SS=%time:~6,2%
set HH=%HH: =0%
set TS=%YYYY%-%MM%-%DD%_%HH%-%MN%-%SS%
echo Archiving GradientSlope logs to %ARCHDIR% with timestamp %TS%
for %%F in ("%LOGDIR%\GradientSlope_*.*") do (
  echo Moving %%~nxF
  move "%%F" "%ARCHDIR%\%%~nF_%TS%%%~xF" >nul
)
echo Done.
endlocal
