# Bar Flow Report - Usage Guide

## Overview
The Bar Flow Report is an interactive web page that displays all entry, exit, and staying conditions for any bar in your BarsOnTheFlow strategy execution. Conditions that are met are highlighted in GREEN ✅, conditions that are not met are highlighted in RED ❌.

## Access the Report

1. Start the dashboard server (if not already running):
   ```
   cd web\dashboard
   start-dashboard.cmd
   ```

2. Open in your browser:
   ```
   http://localhost:8000/barFlowReport.html
   ```

3. Or with a specific bar:
   ```
   http://localhost:8000/barFlowReport.html?bar=156
   ```

## How to Use

1. **Enter a Bar Number**: Type any bar index from your strategy run
2. **Click "Load Bar Report"**: The page will load all conditions for that bar
3. **Review Conditions**: All conditions are color-coded:
   - ✅ **GREEN** = Condition MET
   - ❌ **RED** = Condition NOT MET
   - ⚠️ **YELLOW** = Neutral/Informational

## What You'll See

### Bar Information
- Bar Index, Timestamp
- Position (LONG/SHORT/FLAT)
- Action (ENTRY/EXIT/BAR)
- OHLC prices
- Candle Type (good/bad)
- Fast EMA value and gradient

### Condition Sections

#### **Trend State**
- Trend Up: 4 good bars OR 3 good + positive PnL
- Trend Down: 4 bad bars OR 3 bad + negative PnL

#### **Entry Filters**
- Allow Long This Bar: Not blocked by AvoidLongsOnBadCandle
- Allow Short This Bar: Not blocked by AvoidShortsOnGoodCandle
- Previous Bar Type: Good (up-close) or Bad (down-close)

#### **Gradient Filter** (if enabled)
- Current EMA gradient in degrees
- Long Gradient OK: >= 7.0° threshold
- Short Gradient OK: <= -7.0° threshold

#### **Pending Deferred Entries** (if any)
- Pending Short From Good: Short was deferred because trigger bar was good
- Pending Long From Bad: Long was deferred because trigger bar was bad

#### **Marginal Trend Postponed Exits** (new feature)
- When trend is 2 good/2 bad but PnL still supports position
- Exit is postponed one bar to confirm reversal

### Bar-Specific Sections

#### If Bar is an **ENTRY**:
Shows all conditions that caused the entry:
- Trend condition met (Trend Up for long, Trend Down for short)
- Entry allowed (not blocked by candle type filters)
- Was flat (ready for entry)
- Gradient filter passed (if enabled)

#### If Bar is an **EXIT**:
Shows the specific exit reason and conditions:
- **TrendBreak**: Trend no longer met, ExitOnTrendBreak enabled
- **Retrace**: Profit gave back >= 66% of MFE
- **EntryBarOpposite**: Entry bar closed opposite to position

#### If Bar is **IN-TRADE**:
Shows position status:
- Position active (entry bar reference)
- Trend still valid

#### If Bar is **FLAT** (no position):
Shows why no entry occurred:
- Which trends are active/inactive
- Which entries are allowed/blocked

## Example Use Cases

### 1. "Why didn't I enter on bar 156?"
Load bar 156 and check the FLAT section:
- Is Trend Up or Trend Down red? → Need 4 bars or 3+PnL
- Is Allow Long/Short red? → Blocked by candle type filter
- Is gradient filter red? → EMA slope not steep enough

### 2. "Why did I exit on bar 243?"
Load bar 243 and check the EXIT section:
- See the exact reason (TrendBreak/Retrace/EntryBarOpposite)
- See which conditions triggered it

### 3. "Why did I enter on bar 189?"
Load bar 189 and check the ENTRY section:
- All green conditions show what made the entry valid
- See the previous bar state that triggered it

### 4. "Why am I still holding at bar 305?"
Load bar 305 and check the IN-TRADE section:
- See if trend is still valid
- Check if any exit conditions are approaching

## Color Coding Summary

| Color | Icon | Meaning |
|-------|------|---------|
| 🟢 Green | ✅ | Condition is MET |
| 🔴 Red | ❌ | Condition is NOT MET |
| 🟡 Yellow | ⚠️ | Neutral/Informational |

## Technical Details

### Data Source
The report reads from the most recent CSV log file in:
```
c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\strategy_logs\
```

### API Endpoints
- `GET /api/latest-log` - Returns path to most recent log file
- `GET /api/bar-data?bar=N` - Returns all data for bar N

### Refresh Data
The page automatically finds the latest log file. If you run a new strategy backtest:
1. The page will automatically use the newest log file
2. Or refresh the browser page to be sure

## Troubleshooting

**"No log file found"**
- Make sure you've run the BarsOnTheFlow strategy at least once
- Check that strategy_logs directory exists
- Ensure the dashboard server is running

**"Bar not found"**
- Bar number might be outside the range of your backtest
- Check the strategy log file to see available bar numbers

**Page not loading**
- Ensure dashboard server is running on port 8000
- Check browser console for errors
- Try accessing http://localhost:8000/ping to verify server

## Integration with Strategy

The barFlowReport.html reads the same CSV log files that BarsOnTheFlow.cs creates. The columns include:
- `allowLongThisBar`, `allowShortThisBar` - Entry filters
- `trendUpAtDecision`, `trendDownAtDecision` - Trend states
- `pendingShortFromGood`, `pendingLongFromBad` - Deferred entries
- `prevOpen`, `prevClose`, `prevCandleType` - Previous bar info
- `fastEmaGradDeg` - EMA gradient for filtering
- `reason` - Entry/exit reason

## Future Enhancements

Potential additions:
- Previous/Next bar navigation buttons
- Visual timeline of bars with color-coded actions
- Export specific bar analysis as PDF
- Compare multiple bars side-by-side
- Filter bars by condition (e.g., show all entries with gradient > 10°)
