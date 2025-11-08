# Signal Plotting Logic in CBASTestingIndicator3

## Overview
This document explains **when** and **how** trading signals (bull/bear crosses) are detected and plotted on the chart based on the code and log analysis.

---

## When Signals Are Detected

### 1. Basic Cross Detection (Lines 406-410)
Signals are initially detected using NinjaTrader's built-in cross functions:

```csharp
bool bullCrossRaw = CrossAbove(Close, superTrend, 1);
bool bearCrossRaw = CrossBelow(Close, superTrend, 1);
```

**What this means:**
- **Bull Signal (`bullCrossRaw = true`)**: When `Close[0] > superTrend[0]` AND `Close[1] <= superTrend[1]`
  - Price crosses **above** the SuperTrend line from below
  - Indicates a potential bullish trend change

- **Bear Signal (`bearCrossRaw = true`)**: When `Close[0] < superTrend[0]` AND `Close[1] >= superTrend[1]`
  - Price crosses **below** the SuperTrend line from above
  - Indicates a potential bearish trend change

### 2. SuperTrend Calculation (Lines 347-381)
The SuperTrend line is calculated based on:
- **Upper Band**: `Close + Sensitivity * (2 * Range)`
- **Lower Band**: `Close - Sensitivity * (2 * Range)`
- **Range**: `High[0] - Low[0]` (current bar's price range)
- **SuperTrend Value**: 
  - Bullish regime: Uses `lowerBand`
  - Bearish regime: Uses `upperBand`

The SuperTrend flips direction when price crosses the bands.

---

## Signal Filtering (Optional)

### 3. Scoring Filter (Lines 412-429)
If `UseScoringFilter = true`, signals are filtered based on a quality score:

```csharp
if (UseScoringFilter)
{
    int bullScore = ComputeScore(bullCrossRaw, false, stNowLocal, vpmUse, out rbBull);
    int bearScore = ComputeScore(false, bearCrossRaw, stNowLocal, vpmUse, out rbBear);
    
    if (bull) bull = bullScore >= ScoreThreshold;  // Default threshold = 6
    if (bear) bear = bearScore >= ScoreThreshold;
}
```

**Score Components** (from `ComputeScore` method, lines 684-698):
- **Base cross signal**: +2 points (if cross detected)
- **EMA Stack alignment**: +2 points
  - Bull: `Close > EMA10 > EMA20`
  - Bear: `Close < EMA10 < EMA20`
- **Volume Per Minute (VPM)**: +1 point if `VPM > MinVpm` (default 300)
- **ADX strength**: +1 point if `ADX > MinAdx` (default 25)
- **Volume above average**: +1 point if `Volume > SMA(Volume, 50)`
- **Range break**: +2 points if price breaks out of range

**Maximum Score**: 10 points
**Default Threshold**: 6 points (configurable via `ScoreThreshold` parameter)

**Result**: Only signals with score ≥ threshold are plotted.

---

## How Signals Are Plotted

### 4. Visual Representation (Lines 431-440)

When a signal is confirmed (either raw cross or filtered):

**Bull Signal (Buy Triangle):**
```csharp
if (bull)
{
    double y1 = Low[0] - atr30[0] * 2.0;
    Draw.TriangleUp(this, $"Buy_{CurrentBar}", false, 0, y1, Brushes.LimeGreen);
}
```
- **Symbol**: Green upward triangle (▲)
- **Position**: `Low[0] - (2 * ATR30)` below the current bar's low
- **Color**: LimeGreen
- **Unique ID**: `Buy_{CurrentBar}` (prevents duplicate drawings)

**Bear Signal (Sell Triangle):**
```csharp
if (bear)
{
    double y2 = High[0] + atr30[0] * 2.0;
    Draw.TriangleDown(this, $"Sell_{CurrentBar}", false, 0, y2, Brushes.Red);
}
```
- **Symbol**: Red downward triangle (▼)
- **Position**: `High[0] + (2 * ATR30)` above the current bar's high
- **Color**: Red
- **Unique ID**: `Sell_{CurrentBar}` (prevents duplicate drawings)

---

## Log File Analysis

### 5. Signal Identification in Logs

In the CSV log files, signals are recorded in these columns:
- **`bull_cross`**: `1` = bull signal detected, `0` = no bull signal
- **`bear_cross`**: `1` = bear signal detected, `0` = no bear signal
- **`regime`**: `BULL`, `BEAR`, or `FLAT` (current market regime)

**Example from log:**
```
bar_index=10, close=25554.75, supertrend=25551.5, bull_cross=1, bear_cross=0, regime=BULL
```
This shows:
- Bar index 10
- Close price (25554.75) crossed above SuperTrend (25551.5)
- Bull signal triggered (`bull_cross=1`)
- A green triangle should be plotted on the chart

**Another example:**
```
bar_index=37, close=25548, supertrend=25552.55, bull_cross=0, bear_cross=1, regime=BEAR
```
This shows:
- Bar index 37
- Close price (25548) crossed below SuperTrend (25552.55)
- Bear signal triggered (`bear_cross=1`)
- A red triangle should be plotted on the chart

### 6. Signal Frequency

From the logs:
- Signals occur when price crosses the SuperTrend line
- Multiple signals can occur on the same bar (intrabar ticks)
- With `LogSignalsOnly = true`, only bars with signals are logged
- With scoring filter enabled, not all raw crosses become plotted signals

---

## Key Parameters Affecting Signal Detection

1. **`Sensitivity`** (default: 2.8): Controls band width → affects when SuperTrend flips
2. **`UseScoringFilter`** (default: false): Enables/disables signal filtering
3. **`ScoreThreshold`** (default: 6): Minimum score required to plot signal
4. **`MinVpm`** (default: 300): Minimum VPM for score contribution
5. **`MinAdx`** (default: 25): Minimum ADX for score contribution
6. **`UseSmoothedVpm`** (default: false): Use smoothed VPM in scoring
7. **`VpmEmaSpan`** (default: 5): EMA period for VPM smoothing

---

## Summary

**Signal Detection Flow:**
1. **Calculate SuperTrend** from price bands
2. **Detect cross**: Price crosses above/below SuperTrend
3. **Optional filtering**: Apply scoring system if enabled
4. **Plot signal**: Draw triangle if signal passes (raw or filtered)

**When signals appear:**
- Every time price crosses the SuperTrend line
- Only on bars where cross occurs (not every bar)
- Potentially filtered by scoring system if enabled
- Plotted at a fixed ATR distance from the bar

**What the logs show:**
- Exact bar index and timestamp of each signal
- Price and SuperTrend values at signal time
- Whether signal was bull or bear
- All supporting metrics (VPM, ADX, scores, etc.)

