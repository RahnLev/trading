# Parameter JSON Feature - Implementation Summary

## Overview
Strategy parameters are now automatically written to a JSON file alongside the CSV log file. The `botf_filter_analysis.html` dashboard has been updated to detect and display these parameters.

## What Changed

### 1. BarsOnTheFlow.cs
**New Method: `WriteParametersJsonFile()`** (lines ~989-1077)
- Writes all strategy parameters to a JSON file
- File naming: `BarsOnTheFlow_{Instrument}_{timestamp}_params.json` (alongside the CSV)
- File location: Same `strategy_logs` directory as the CSV log file
- Called automatically when CSV log is initialized

**Updated: `InitializeLog()`** (line ~1102)
- Now calls `WriteParametersJsonFile()` immediately after CSV header is written
- Ensures parameters are captured at strategy start

**Parameters Captured:**
- Contracts
- ExitOnTrendBreak, ExitOnRetrace, TrendRetraceFraction
- EnableTrendOverlay, EnableShorts
- AvoidShortsOnGoodCandle, AvoidLongsOnBadCandle
- FastEmaPeriod, FastGradLookbackBars, UseChartScaledFastGradDeg
- GradientFilterEnabled, SkipShortsAboveGradient, SkipLongsBelowGradient
- MinConsecutiveBars, ReverseOnTrendBreak, ExitIfEntryBarOpposite, StopLossPoints
- AllowMidBarGradientEntry, AllowMidBarGradientExit
- ShowBarIndexLabels, ShowFastGradLabels, EnableFastGradDebug, EnableDashboardDiagnostics, DashboardBaseUrl
- Instrument name and run start time

### 2. botf_filter_analysis.html
**UI Updates:**
- File input now accepts both `.csv` and `.json` files with `multiple` attribute
- Updated helper text: "Upload CSV log file. Optional: also upload the matching _params.json file to see strategy parameters."
- New parameters card (initially hidden) that displays when JSON is loaded:
  - Shows all parameters in a grid layout
  - Toggle button to collapse/expand the card
  - Color-coded display: parameter name in blue, value in light gray

**JavaScript Updates:**
- Added `currentParams` and `paramsCollapsed` state variables
- Added `paramsCard`, `paramsContent`, `paramsGrid`, `toggleParamsBtn` DOM element references
- Enhanced `fileInput` event listener to:
  - Support multiple file selection
  - Detect both CSV and JSON files
  - Process JSON file and call `displayParameters()` when found
- New `loadParametersJson()` function (for future enhancements)
- New `displayParameters(params)` function:
  - Validates parameters exist
  - Sorts parameters alphabetically
  - Renders each parameter in a card with key and value
  - Handles booleans with checkmark/X symbols
  - Formats numbers to 4 decimal places
- Added toggle button click handler for collapse/expand

## How to Use

### During Strategy Execution
1. Run BarsOnTheFlow strategy
2. Both files are created automatically:
   - `BarsOnTheFlow_MNQZ24_2024-12-13_12-30-45-123.csv`
   - `BarsOnTheFlow_MNQZ24_2024-12-13_12-30-45-123_params.json`

### In the Dashboard
1. Open `botf_filter_analysis.html` in browser
2. Click "Load CSV (BarsOnTheFlow log)"
3. Either:
   - **Option A:** Select just the CSV file → only trade data shown
   - **Option B:** Select both CSV and `_params.json` files → trade data + parameters displayed
4. Parameters card appears at the top (below filters) showing all active parameters
5. Use "Collapse" button to hide/show parameters

## File Format Example

```json
{
  "Contracts": 1,
  "ExitOnTrendBreak": true,
  "ExitOnRetrace": false,
  "TrendRetraceFraction": 0.5,
  "EnableTrendOverlay": true,
  "EnableShorts": true,
  "AvoidShortsOnGoodCandle": false,
  "AvoidLongsOnBadCandle": false,
  "FastEmaPeriod": 10,
  "FastGradLookbackBars": 20,
  "UseChartScaledFastGradDeg": false,
  "GradientFilterEnabled": true,
  "SkipShortsAboveGradient": 0.5,
  "SkipLongsBelowGradient": -0.5,
  "MinConsecutiveBars": 3,
  "ReverseOnTrendBreak": false,
  "ExitIfEntryBarOpposite": false,
  "StopLossPoints": 10,
  "AllowMidBarGradientEntry": false,
  "AllowMidBarGradientExit": false,
  "ShowBarIndexLabels": true,
  "ShowFastGradLabels": false,
  "EnableFastGradDebug": false,
  "EnableDashboardDiagnostics": false,
  "DashboardBaseUrl": "http://127.0.0.1:51888",
  "Instrument": "MNQZ24",
  "StartTime": "2024-12-13 12:30:45.123"
}
```

## Benefits

✓ **No CSV parsing needed** - Parameters in separate, easy-to-read JSON file
✓ **Downstream compatibility** - Analysis tools can now read strategy config alongside trade data
✓ **Self-documenting runs** - Each log set includes its parameter snapshot
✓ **Dashboard integration** - Parameters visible immediately when loading trades
✓ **Flexible file handling** - Load CSV alone or with parameters

## Build Status
✓ Build succeeded with 720 warnings, 0 errors
✓ No breaking changes to existing functionality
✓ CSS already included for parameter display cards
