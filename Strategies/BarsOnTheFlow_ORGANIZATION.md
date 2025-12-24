# BarsOnTheFlow Strategy Organization Document

## Overview
This document tracks all features, properties, and logic in the BarsOnTheFlow strategy to identify what's actively used vs. potentially redundant.

---

## 📊 STRATEGY PROPERTIES (36 Total)

### Core Trading Parameters (ACTIVE - Essential)
| # | Property | Default | Status | Notes |
|---|----------|---------|--------|-------|
| 1 | `Contracts` | 1 | ✅ ACTIVE | Position size |
| 2 | `ExitOnTrendBreak` | true | ✅ ACTIVE | Exit when trend signal lost |
| 3 | `ExitOnRetrace` | true | ✅ ACTIVE | Exit when MFE gives back TrendRetraceFraction |
| 4 | `TrendRetraceFraction` | 0.66 | ✅ ACTIVE | Fraction of MFE to give back before exit |
| 6 | `EnableShorts` | true | ✅ ACTIVE | Allow short trades |
| 7 | `AvoidShortsOnGoodCandle` | true | ✅ ACTIVE | Block shorts on up-close bars |
| 8 | `AvoidLongsOnBadCandle` | true | ✅ ACTIVE | Block longs on down-close bars |

### Trend Detection Parameters (ACTIVE)
| # | Property | Default | Status | Notes |
|---|----------|---------|--------|-------|
| 25 | `TrendLookbackBars` | 5 | ✅ ACTIVE | Window for trend analysis |
| 26 | `MinConsecutiveBars` | 3 | ✅ ACTIVE | Min good/bad bars for trend |
| 27 | `UsePnLTiebreaker` | false | ✅ ACTIVE | Allow PnL tiebreaker for marginal patterns |
| 24 | `ReverseOnTrendBreak` | false | ✅ ACTIVE | Reverse instead of just exit |

### EMA & Gradient Parameters (ACTIVE)
| # | Property | Default | Status | Notes |
|---|----------|---------|--------|-------|
| 16 | `FastEmaPeriod` | 10 | ✅ ACTIVE | EMA period |
| 17 | `FastGradLookbackBars` | 2 | ✅ ACTIVE | Gradient calculation lookback |
| 11 | `UseChartScaledFastGradDeg` | true | ✅ ACTIVE | Use pixel-based degrees |
| 21 | `GradientFilterEnabled` | false | ✅ ACTIVE | Enable gradient filtering |
| 22 | `SkipShortsAboveGradient` | -7.0 | ✅ ACTIVE | Gradient threshold for shorts |
| 23 | `SkipLongsBelowGradient` | 7.0 | ✅ ACTIVE | Gradient threshold for longs |

### Mid-Bar Entry/Exit (ACTIVE but rarely used)
| # | Property | Default | Status | Notes |
|---|----------|---------|--------|-------|
| 28 | `AllowMidBarGradientEntry` | false | ⚠️ OPTIONAL | Mid-bar entry on gradient cross |
| 29 | `AllowMidBarGradientExit` | false | ⚠️ OPTIONAL | Mid-bar exit on gradient cross |
| 18 | `ExitIfEntryBarOpposite` | true | ✅ ACTIVE | Exit if entry bar closes opposite |

### Stop Loss Parameters (ACTIVE)
| # | Property | Default | Status | Notes |
|---|----------|---------|--------|-------|
| 30 | `StopLossPoints` | 20 | ✅ ACTIVE | Fixed stop loss in points |
| 31 | `UseTrailingStop` | false | ✅ ACTIVE | Trailing vs static stop |
| 32 | `UseDynamicStopLoss` | false | ✅ ACTIVE | Calculate stop from volatility |
| 33 | `DynamicStopLookback` | 5 | ✅ ACTIVE | Bars for dynamic stop calc |
| 34 | `DynamicStopMultiplier` | 1.0 | ✅ ACTIVE | Multiplier for avg range |
| 35 | `UseVolumeAwareStop` | true | ✅ ACTIVE | Query API for vol-aware stops |

### Visual/Debug Parameters (OPTIONAL - can disable for production)
| # | Property | Default | Status | Notes |
|---|----------|---------|--------|-------|
| 5 | `EnableTrendOverlay` | true | ⚠️ OPTIONAL | Draw trend rectangles |
| 9 | `ShowBarIndexLabels` | true | ⚠️ OPTIONAL | Bar index labels on chart |
| 10 | `ShowFastGradLabels` | true | ⚠️ OPTIONAL | Gradient labels on chart |
| 12 | `EnableFastGradDebug` | false | ⚠️ DEBUG | Verbose gradient logging |
| 13 | `FastGradDebugStart` | 0 | ⚠️ DEBUG | Debug range start |
| 14 | `FastGradDebugEnd` | 0 | ⚠️ DEBUG | Debug range end |
| 15 | `FastGradDebugLogToCsv` | false | ⚠️ DEBUG | Log gradients to CSV |

### Dashboard/Logging Parameters (OPTIONAL)
| # | Property | Default | Status | Notes |
|---|----------|---------|--------|-------|
| 19 | `EnableDashboardDiagnostics` | false | ⚠️ OPTIONAL | Stream to dashboard |
| 20 | `DashboardBaseUrl` | localhost:51888 | ⚠️ OPTIONAL | Dashboard URL |
| 36 | `EnableOpportunityLog` | true | ⚠️ OPTIONAL | Log opportunity analysis |

---

## 🔧 PRIVATE FIELDS

### Trend Tracking (ACTIVE)
```csharp
private readonly Queue<bool> recentGood     // Last N bar quality
private readonly Queue<double> recentPnl    // Last N bar PnL
```

### Position State (ACTIVE)
```csharp
private MarketPosition intendedPosition     // Track intended position for UniqueEntries
private int lastEntryBarIndex               // Bar of last entry
private MarketPosition lastEntryDirection   // Direction of last entry
```

### Pending/Deferred Signals (ACTIVE - Key logic)
```csharp
private bool pendingShortFromGood           // Deferred short (blocked by good candle)
private bool pendingLongFromBad             // Deferred long (blocked by bad candle)
private bool pendingExitLongOnGood          // Postponed long exit
private bool pendingExitShortOnBad          // Postponed short exit
```

### Mid-Bar Tracking (ACTIVE when enabled)
```csharp
private bool waitingForLongGradient         // Waiting for gradient to cross for long
private bool waitingForShortGradient        // Waiting for gradient to cross for short
private bool waitingToExitLongOnGradient    // Waiting to exit long
private bool waitingToExitShortOnGradient   // Waiting to exit short
```

### EMA/Gradient State (ACTIVE)
```csharp
private double lastFastEmaSlope             // Last computed slope
private double lastFastEmaGradDeg           // Last computed gradient in degrees
private EMA fastEma                         // EMA indicator
```

### Trend Visualization (OPTIONAL)
```csharp
private int trendStartBar                   // Start bar of current trend
private string trendRectTag                 // Tag for trend rectangle
private string trendLineTag                 // Tag for trend line
private Brush trendBrush                    // Brush for trend overlay
private MarketPosition trendSide            // Current trend direction
private double trendEntryPrice              // Entry price for retrace tracking
private double trendMaxProfit               // MFE for retrace tracking
```

### UI Controls (ACTIVE)
```csharp
private Grid barNavPanel                    // Navigation panel
private TextBox barNavTextBox               // Bar number input
private Button barNavButton                 // Go button
private TextBox stopLossTextBox             // Stop loss display
private Button stopLossPlusButton           // Increase stop
private Button stopLossMinusButton          // Decrease stop
```

### Logging (ACTIVE for debugging)
```csharp
private StreamWriter logWriter              // Main execution log
private StreamWriter opportunityLogWriter   // Opportunity analysis log
private StreamWriter outputLogWriter        // Output window mirror
private StreamWriter fastGradDebugWriter    // FastGrad debug CSV
private List<PendingLogEntry> pendingLogs   // Deferred log entries
```

### Decision Context Cache (ACTIVE)
```csharp
private double lastPrevOpen                 // Cached for execution logging
private double lastPrevClose
private string lastPrevCandleType
private bool lastAllowLongThisBar
private bool lastAllowShortThisBar
private bool lastTrendUp
private bool lastTrendDown
private int lastDecisionBar
private bool lastPendingShortFromGood
private bool lastPendingLongFromBad
private double lastFastGradDegForDecision
```

### API Caching (ACTIVE)
```csharp
private static HttpClient sharedClient      // Shared HTTP client
private int cachedVolumeAwareStopTicks      // Cached stop value
private int cachedVolumeAwareStopHour       // Cache hour
private DateTime cachedVolumeAwareStopTime  // Cache timestamp
private int lastRecordedBarSample           // Rate limit bar samples
```

---

## 📋 METHOD INVENTORY

### Core Strategy Methods (ACTIVE - Required)
| Method | Purpose | Status |
|--------|---------|--------|
| `OnStateChange()` | Initialize/cleanup | ✅ ACTIVE |
| `OnBarUpdate()` | Main trading logic | ✅ ACTIVE |
| `OnExecutionUpdate()` | Track fills | ✅ ACTIVE |

### Trend Detection (ACTIVE - Core Logic)
| Method | Purpose | Status |
|--------|---------|--------|
| `IsTrendUp()` | Detect uptrend | ✅ ACTIVE |
| `IsTrendDown()` | Detect downtrend | ✅ ACTIVE |
| `RecordCompletedBar()` | Update queues | ✅ ACTIVE |
| `GetBarSequencePattern()` | Pattern string | ✅ ACTIVE |

### Gradient Calculation (ACTIVE)
| Method | Purpose | Status |
|--------|---------|--------|
| `ComputeFastEmaGradient()` | Linear regression slope | ✅ ACTIVE |
| `ComputeChartScaledFastEmaDeg()` | Pixel-based degrees | ✅ ACTIVE |
| `ShouldSkipLongDueToGradient()` | Gradient filter | ✅ ACTIVE |
| `ShouldSkipShortDueToGradient()` | Gradient filter | ✅ ACTIVE |

### Mid-Bar Entry/Exit (OPTIONAL)
| Method | Purpose | Status |
|--------|---------|--------|
| `CheckMidBarGradientEntry()` | Mid-bar entry check | ⚠️ OPTIONAL |
| `CheckMidBarGradientExit()` | Mid-bar exit check | ⚠️ OPTIONAL |

### Stop Loss (ACTIVE)
| Method | Purpose | Status |
|--------|---------|--------|
| `ApplyStopLoss()` | Set stop loss on entry | ✅ ACTIVE |
| `CalculateStopLossTicks()` | Calculate stop amount | ✅ ACTIVE |
| `GetVolumeAwareStopTicks()` | Query API for stop | ✅ ACTIVE |
| `AdjustStopLoss()` | UI control handler | ✅ ACTIVE |

### Trend Visualization (OPTIONAL)
| Method | Purpose | Status |
|--------|---------|--------|
| `UpdateTrendLifecycle()` | Track trend state | ⚠️ OPTIONAL |
| `StartTrendTracking()` | Begin trend visual | ⚠️ OPTIONAL |
| `UpdateTrendProgress()` | Update MFE/retrace | ⚠️ OPTIONAL |
| `UpdateTrendOverlay()` | Draw rectangle | ⚠️ OPTIONAL |
| `ResetTrendVisuals()` | Clear visuals | ⚠️ OPTIONAL |
| `CreateTrendBrush()` | Create brush | ⚠️ OPTIONAL |

### UI (ACTIVE)
| Method | Purpose | Status |
|--------|---------|--------|
| `CreateBarNavPanel()` | Build UI panel | ✅ ACTIVE |
| `NavigateToBar()` | Chart navigation | ✅ ACTIVE |
| `FindVisualChild<T>()` | UI helper | ✅ ACTIVE |

### Logging (OPTIONAL but useful)
| Method | Purpose | Status |
|--------|---------|--------|
| `InitializeLog()` | Setup main log | ⚠️ OPTIONAL |
| `InitializeOpportunityLog()` | Setup opportunity log | ⚠️ OPTIONAL |
| `InitializeOutputLog()` | Setup output log | ⚠️ OPTIONAL |
| `LogLine()` | Write to log | ⚠️ OPTIONAL |
| `LogBarSnapshot()` | Log bar data | ⚠️ OPTIONAL |
| `LogOpportunityAnalysis()` | Log opportunities | ⚠️ OPTIONAL |
| `LogToOutput()` | Mirror to file | ⚠️ OPTIONAL |
| `PrintAndLog()` | Print + log | ⚠️ OPTIONAL |
| `LogStrategyParameters()` | Log params (unused?) | ❓ CHECK |
| `WriteParametersJsonFile()` | Write params JSON | ⚠️ OPTIONAL |
| `EnsureFastGradDebugWriter()` | Debug log setup | ⚠️ DEBUG |

### API/Dashboard (OPTIONAL)
| Method | Purpose | Status |
|--------|---------|--------|
| `SendDashboardDiag()` | Stream diagnostics | ⚠️ OPTIONAL |
| `RecordBarSample()` | Record to API | ⚠️ OPTIONAL |
| `EnsureHttpClient()` | HTTP client setup | ✅ ACTIVE |
| `ExportStrategyState()` | Export state JSON | ⚠️ OPTIONAL |
| `UpdateStrategyState()` | Update state file | ⚠️ OPTIONAL |

### Utility (ACTIVE)
| Method | Purpose | Status |
|--------|---------|--------|
| `GetCandleType()` | good/bad/doji | ✅ ACTIVE |
| `GetOrderReason()` | Order reason string | ✅ ACTIVE |
| `CaptureDecisionContext()` | Cache for logging | ⚠️ OPTIONAL |

---

## 🚨 POTENTIAL ISSUES & CLEANUP OPPORTUNITIES

### 1. Redundant Logic in OnBarUpdate
The `AvoidLongsOnBadCandle` and `AvoidShortsOnGoodCandle` checks are done multiple times:
- First at line ~429: `bool allowShortThisBar = !(AvoidShortsOnGoodCandle && prevGood);`
- Then repeated in the entry logic with additional `if (AvoidLongsOnBadCandle && prevBad)` checks

**Recommendation**: The double-checks can be simplified since `allowLongThisBar` already handles this.

### 2. Debug Print Statements
Excessive debug prints that could be controlled by a single debug flag:
- `[BAR_DATA_DEBUG]` - hardcoded bar range check (2653-2670)
- `[Trend Debug]` - only on bar 7
- Many `[EXIT_DEBUG]`, `[ENTRY]`, `[Reverse Debug]` prints

**Recommendation**: Add a single `EnableVerboseDebug` property to control all debug output.

### 3. Unused Method: `LogStrategyParameters()`
This method exists but is never called. `WriteParametersJsonFile()` is called instead.

**Recommendation**: Either call it in `InitializeLog()` or remove it.

### 4. Four-Bar PnL Logic Complexity
The marginal trend handling with `pendingExitLongOnGood` and `pendingExitShortOnBad` adds complexity:
```csharp
bool isMarginalTrend = (goodCount == 2 && badCount == 2);
```

This uses `recentGood.Count >= 4` but the queue size is configured by `TrendLookbackBars` (default 5).

**Recommendation**: Align the marginal trend check with `TrendLookbackBars` and `MinConsecutiveBars`.

### 5. HttpClient Timeout
`sharedClient.Timeout = TimeSpan.FromMilliseconds(300);` - but `GetVolumeAwareStopTicks()` uses `task.Wait(200)`.

**Recommendation**: Make timeout consistent or use a single timeout value.

### 6. Properties Grouping
All 36 properties are in the same `"BarsOnTheFlow"` GroupName.

**Recommendation**: Organize into logical groups:
- "Trading" (Contracts, EnableShorts, etc.)
- "Trend Detection" (TrendLookbackBars, MinConsecutiveBars, etc.)
- "Gradient Filter" (GradientFilterEnabled, thresholds, etc.)
- "Stop Loss" (StopLossPoints, UseTrailingStop, etc.)
- "Visualization" (EnableTrendOverlay, ShowBarIndexLabels, etc.)
- "Logging & Debug" (EnableDashboardDiagnostics, EnableOpportunityLog, etc.)

---

## ✅ RECOMMENDED CLEANUP ACTIONS

### Priority 1: Quick Wins
1. [ ] Remove or call `LogStrategyParameters()` method
2. [ ] Add `EnableVerboseDebug` property to control debug prints
3. [ ] Remove hardcoded bar range debug checks

### Priority 2: Code Organization
1. [ ] Group properties into logical categories
2. [ ] Extract trend detection logic into separate methods
3. [ ] Simplify redundant candle quality checks

### Priority 3: Feature Flags
Consider making these features toggleable:
1. [ ] All logging (single master switch)
2. [ ] UI controls (bar nav panel)
3. [ ] API integration (volume-aware stops, bar recording)

### Priority 4: Documentation
1. [ ] Add XML doc comments to public properties
2. [ ] Document the pending signal flow
3. [ ] Create a decision flowchart for entry/exit logic

---

## 📁 FILES GENERATED BY STRATEGY

| File Pattern | Purpose | Controlled By |
|--------------|---------|---------------|
| `strategy_logs/BarsOnTheFlow_{instrument}_{timestamp}.csv` | Execution log | Always created |
| `strategy_logs/BarsOnTheFlow_{instrument}_{timestamp}_params.json` | Parameters | Always created |
| `strategy_logs/BarsOnTheFlow_Opportunities_{instrument}_{timestamp}.csv` | Opportunity analysis | `EnableOpportunityLog` |
| `strategy_logs/BarsOnTheFlow_OutputWindow_{instrument}_{timestamp}.csv` | Print mirror | Always created |
| `strategy_logs/BarsOnTheFlow_FastGradDebug_{instrument}_{timestamp}.csv` | Gradient debug | `FastGradDebugLogToCsv` |
| `strategy_state/BarsOnTheFlow_state.json` | API state export | Always created |

---

*Last Updated: December 19, 2024*

---

## 🗄️ SQLite DATABASES

> **Full Schema:** See [DATABASE_SCHEMA.md](../web/dashboard/DATABASE_SCHEMA.md) for complete column definitions.

| Database | Tables | Used By | Integration |
|----------|--------|---------|-------------|
| [volatility.db](../web/dashboard/DATABASE_SCHEMA.md#volatilitydb) | 3 | BarsOnTheFlow | `UseVolumeAwareStop` (default: true) |
| [dashboard.db](../web/dashboard/DATABASE_SCHEMA.md#dashboarddb) | 12 | BarsOnTheFlow | `EnableDashboardDiagnostics` (default: false) |
| [bars.db](../web/dashboard/DATABASE_SCHEMA.md#barsdb) | 0 | Reserved | — |

### Quick Reference

**volatility.db** - Volume-aware stop loss system
- `bar_samples` - Individual bar data → `RecordBarSample()`
- `volatility_stats` - Hourly aggregated stats
- `stop_loss_recommendations` - Pre-computed stops → `GetVolumeAwareStopTicks()`

**dashboard.db** - Live diagnostics & analytics
- `diags` - Bar-by-bar snapshots → `SendDashboardDiag()`
- `trades` - Completed trade records
- `entry_blocks` - Blocked entries for analysis
- `suggestions` - AI parameter tuning

---

## 📊 LIVE BAR DATA FEED (`/bars/latest`)

**Endpoint:** `GET /bars/latest?limit=50`  
**Storage:** In-memory cache only (NOT persisted to database)  
**Cache Size:** 1200 bars max (rolling deque - oldest bars auto-deleted)

### Data Fields Per Bar

The server's `_normalize_bar()` function accepts 50+ fields from multiple strategies. Fields are marked by source:
- 🟢 **BarsOnTheFlow** - Sent by BarsOnTheFlow
- 🔵 **GradientSlopeStrategy** - Sent by GradientSlopeStrategy (lines 3000-3104)
- ⚪ **Server** - Added by server

| Field | Source | Type | Description |
|-------|--------|------|-------------|
| **Bar Identification** | | | |
| `barIndex`    | 🟢🔵 | int | NinjaTrader bar index |
| `ts`          | ⚪ | float | Unix timestamp when received (server) |
| `localTime`   | 🟢🔵 | string | Strategy's local time (YYYY-MM-DD HH:mm:ss) |
| **OHLC Data** | | | |
| `open`        | 🟢🔵 | float | Bar open price |
| `high`        | 🟢🔵 | float | Bar high price |
| `low`         | 🟢🔵 | float | Bar low price |
| `close`       | 🟢🔵 | float | Bar close price |
| **EMA & Gradient** | | | |
| `fastEMA`     | 🟢🔵 | float | Fast EMA value |
| `fastGrad`    | 🟢🔵 | float | Fast EMA gradient (slope) |
| `fastGradDeg` | 🟢 | float | Fast EMA gradient in degrees |
| `slowEMA`     | 🔵 | float | Slow EMA value |
| `slowGrad`    | 🔵 | float | Slow EMA gradient (slope) |
| `accel`       | 🔵 | float | Gradient acceleration |
| `gradStab`    | 🔵 | float | Gradient stability metric |
| **Technical Indicators** | | | |
| `adx`         | 🔵 | float | ADX value |
| `atr`         | 🔵 | float | ATR value |
| `rsi`         | 🔵 | float | RSI value |
| `bandwidth`   | 🔵 | float | Bandwidth (EMA spread) |
| `unrealized`  | 🔵 | float | Unrealized P&L |
| **Trend & Position** | | | |
| `trendSide`   | 🔵 | string | "BULL" or "BEAR" |
| `signal`      | 🔵 | string | "LONG", "SHORT", or "FLAT" |
| `myPosition`  | 🔵 | string | Current position |
| `trendStartBar`       | 🔵 | int | Bar where trend started |
| `barsInSignal`        | 🔵 | int | Bars in current signal |
| **Entry Eligibility** | | | |
| `signalEligibleLong`  | 🔵 | bool | Long signal eligible |
| `signalEligibleShort` | 🔵 | bool | Short signal eligible |
| `streakLong`          | 🔵 | int | Consecutive long-favoring bars |
| `streakShort`         | 🔵 | int | Consecutive short-favoring bars |
| `entryLongReady`      | 🔵 | bool | All long filters passed |
| `entryShortReady`     | 🔵 | bool | All short filters passed |
| `entryDelayMet`       | 🔵 | bool | Entry delay requirement met |
| `canEnterLong`        | 🔵 | bool | Can enter long now |
| `canEnterShort`       | 🔵 | bool | Can enter short now |
| **Price vs EMA Filters** | | | |
| `priceAboveEMAs`      | 🔵 | bool | Price above both EMAs |
| `priceBelowEMAs`      | 🔵 | bool | Price below both EMAs |
| `gradDirLongOk`       | 🔵 | bool | Gradient direction OK for long |
| `gradDirShortOk`      | 🔵 | bool | Gradient direction OK for short |
| `fastStrongForEntryLong`  | 🔵 | bool | Fast gradient strong enough (long) |
| `fastStrongForEntryShort` | 🔵 | bool | Fast gradient strong enough (short) |
| **Filter Status** | | | |
| `notOverextended`         | 🔵 | bool | Not overextended filter |
| `adxOk`                   | 🔵 | bool | ADX filter passed |
| `gradStabOk`              | 🔵 | bool | Gradient stability OK |
| `bandwidthOk`             | 🔵 | bool | Bandwidth within range |
| `accelAlignOkLong`        | 🔵 | bool | Acceleration aligned (long) |
| `accelAlignOkShort`       | 🔵 | bool | Acceleration aligned (short) |
| `atrOk`                   | 🔵 | bool | ATR within limits |
| `rsiOk`                   | 🔵 | bool | RSI within limits |
| **Threshold Snapshots** | | | |
| `entryGradThrLong`        | 🔵 | float | Entry gradient threshold (long) |
| `entryGradThrShort`       | 🔵 | float | Entry gradient threshold (short) |
| `maxEntryFastGradAbs`     | 🔵 | float | Max allowed gradient for entry |
| `minAdxForEntry`          | 🔵 | float | Minimum ADX required |
| `maxGradientStabilityForEntry` | 🔵 | float | Max gradient stability allowed |
| `minBandwidthForEntry`    | 🔵 | float | Min bandwidth required |
| `maxBandwidthForEntry`    | 🔵 | float | Max bandwidth allowed |
| `maxATRForEntry`          | 🔵 | float | Max ATR allowed |
| `minRSIForEntry`          | 🔵 | float | Min RSI required |
| `maxRSIForEntry`          | 🔵 | float | Max RSI allowed |
| `entryBarDelay`           | 🔵 | int | Entry delay in bars |
| **BarsOnTheFlow Specific** | | | |
| `allowLongThisBar`        | 🟢 | bool | Whether long entry allowed this bar |
| `allowShortThisBar`       | 🟢 | bool | Whether short entry allowed this bar |
| **Blockers** | | | |
| `blockersLong`            | 🔵 | array | List of long entry blockers |
| `blockersShort`           | 🔵 | array | List of short entry blockers |
| **Classification (Joined)** | | | |
| `isBad`                   | ⚪ | int | 1 if bar classified as "bad" (from dashboard.db) |
| `badReason`               | ⚪ | string | Classification reason (from dashboard.db) |

### What Each Strategy Sends

**BarsOnTheFlow** (`SendDashboardDiag()` - lines 2585-2624):
- barIndex, time, OHLC (open/high/low/close)
- fastEMA, fastGrad, fastGradDeg
- allowLongThisBar, allowShortThisBar
- **Total: 11 fields**

**GradientSlopeStrategy** (`StreamCompactDiagnosis()` - lines 3000-3104):
- All BarsOnTheFlow fields PLUS:
- slowEMA, slowGrad, accel, gradStab
- adx, atr, rsi, bandwidth, signal, trendSide, trendStartBar
- Entry readiness: signalEligibleLong/Short, streakLong/Short, entryLongReady/ShortReady
- Filter status: 15+ boolean flags
- Threshold snapshots: 10+ current values
- blockersLong, blockersShort arrays
- **Total: 50+ fields**

### Storage Details

**In-Memory Cache:**
- Data structure: `deque[Dict[str, Any]]` (Python)
- Max size: 1200 bars
- Behavior: Oldest bars auto-deleted when limit reached
- Lifetime: Cleared when server restarts
- Purpose: Fast queries for live dashboards

**NOT Stored in Database:**
- ❌ bars.db is EMPTY (reserved for future use)
- ❌ No persistence across server restarts
- ✅ Only `bar_samples` in volatility.db (different data set)
- ✅ Only `diags` in dashboard.db (if `EnableDashboardDiagnostics = true`)

**Data Flow:**
```
┌─────────────────────────────────────────────────────────────────────┐
│                     Strategy → Server → Cache                        │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  BarsOnTheFlow (11 fields)                                          │
│     └─ SendDashboardDiag()                                          │
│        lines 2585-2624                                              │
│           │                                                          │
│           │ POST /diag                                              │
│           │                                                          │
│           ▼                                                          │
│     ┌─────────────────────┐                                         │
│     │ server.py           │                                         │
│     │ receive_diag()      │                                         │
│     │ lines 1319-1405     │                                         │
│     └─────────────────────┘                                         │
│           │                                                          │
│           ├─► diags.append(p)         [line 1348]                   │
│           │   └─ Raw diagnostic list (MAX_DIAGS = 10,000)           │
│           │                                                          │
│           └─► bar_cache.append(       [line 1350]                   │
│                 _normalize_bar(p))                                  │
│               └─ Normalized bar cache (BAR_CACHE_MAX = 1,200)       │
│                                                                      │
│  GradientSlopeStrategy (50+ fields)                                 │
│     └─ StreamCompactDiagnosis()                                     │
│        lines 3000-3104                                              │
│           │                                                          │
│           │ POST /diag (batched, 20 bars at a time)                │
│           │                                                          │
│           └──────────────────┘ (same flow as above)                 │
│                                                                      │
│                                                                      │
│  Frontend/API Queries:                                              │
│                                                                      │
│     GET /bars/latest?limit=50                                       │
│        └─ Returns last N bars from bar_cache                        │
│           [server.py lines 1677-1703]                               │
│                                                                      │
│     GET /diags?since=<timestamp>                                    │
│        └─ Returns raw diagnostics from diags list                   │
│           [server.py lines 1285-1316]                               │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

**Key Points:**
1. **Strategies do NOT post directly to `/bars/latest`** - they post to `/diag`
2. **Server's `/diag` endpoint populates TWO caches:**
   - `diags` list - Raw diagnostic data (10,000 max)
   - `bar_cache` deque - Normalized bar data (1,200 max) via `_normalize_bar()`
3. **`/bars/latest` reads from `bar_cache`** (line 1682 in server.py)
4. **No database persistence** - all in-memory, cleared on server restart
5. **GradientSlopeStrategy batches** - sends 20 bars at once, flushes every 1 second

**Server Code References:**
- `/diag` endpoint: [server.py](../web/dashboard/server.py#L1319-1405)
- `_normalize_bar()`: [server.py](../web/dashboard/server.py#L617-680)
- `/bars/latest` endpoint: [server.py](../web/dashboard/server.py#L1677-1703)
- `bar_cache` definition: [server.py](../web/dashboard/server.py#L574)

---

## 📁 STRATEGY STATE FILE (`BarsOnTheFlow_state.json`)

**Location:** `strategy_state/BarsOnTheFlow_state.json`  
**Update Frequency:** Every 10 bars  
**Purpose:** Persist strategy configuration, position state, and parameters  
**Code:** [BarsOnTheFlow.cs](BarsOnTheFlow.cs#L1391-1441) - `ExportStrategyState()` method

### State File Fields (24 Total)

All 24 fields in `BarsOnTheFlow_state.json` are **MISSING from `/bars/latest`** because they serve completely different purposes:
- **state.json** = Strategy-level configuration & position state
- **/bars/latest** = Bar-level diagnostics (OHLC, indicators, filters)

| Field | Type | Category | Description |
|-------|------|----------|-------------|
| **Strategy Metadata** | | | |
| `timestamp` | string | Metadata | Last export timestamp (ISO 8601) |
| `strategyName` | string | Metadata | Strategy name ("BarsOnTheFlow") |
| `isRunning` | bool | Status | Whether strategy is active |
| `currentBar` | int | Status | Current bar index |
| **Position State** | | | |
| `contracts` | int | Position | Contract size |
| `positionMarketPosition` | string | Position | Current position ("Flat", "Long", "Short") |
| `positionQuantity` | int | Position | Position quantity |
| `intendedPosition` | string | Position | Intended position (for unique entries) |
| **Stop Loss Configuration** | | | |
| `stopLossPoints` | float | Stop Loss | Fixed stop loss in points |
| `calculatedStopTicks` | int | Stop Loss | Dynamic stop in ticks (if enabled) |
| `calculatedStopPoints` | float | Stop Loss | Dynamic stop in points (if enabled) |
| `useTrailingStop` | bool | Stop Loss | Trailing stop enabled |
| `useDynamicStopLoss` | bool | Stop Loss | Dynamic stop enabled |
| `lookback` | int | Stop Loss | Dynamic stop lookback period |
| `multiplier` | float | Stop Loss | Dynamic stop multiplier |
| **Strategy Parameters** | | | |
| `enableShorts` | bool | Config | Short trades allowed |
| `avoidLongsOnBadCandle` | bool | Config | Block longs on down-close bars |
| `avoidShortsOnGoodCandle` | bool | Config | Block shorts on up-close bars |
| `exitOnTrendBreak` | bool | Config | Exit when trend breaks |
| `reverseOnTrendBreak` | bool | Config | Reverse on trend break |
| `fastEmaPeriod` | int | Config | Fast EMA period |
| `gradientThresholdSkipLongs` | float | Config | Min gradient for longs (SkipLongsBelowGradient) |
| `gradientThresholdSkipShorts` | float | Config | Max gradient for shorts (SkipShortsAboveGradient) |
| **Pending Signals** | | | |
| `pendingLongFromBad` | bool | Signals | Deferred long entry (blocked by bad candle) |
| `pendingShortFromGood` | bool | Signals | Deferred short entry (blocked by good candle) |

### Comparison: state.json vs /bars/latest

**Zero Field Overlap** - These are completely separate data sets:

```
┌─────────────────────────────────────────────────────────────────────┐
│                  BarsOnTheFlow_state.json (24 fields)                │
│                  ────────────────────────────────────                │
│  Strategy Configuration & Position State                            │
│  • Updates: Every 10 bars                                           │
│  • Persistence: File system (survives strategy restarts)            │
│  • Purpose: Resume strategy with same config                        │
│  • Fields: Metadata, position, stop loss, parameters, pending signals │
│                                                                      │
│  Examples:                                                           │
│  - positionMarketPosition: "Long"                                   │
│  - stopLossPoints: 20.0                                             │
│  - enableShorts: true                                               │
│  - pendingLongFromBad: false                                        │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                   /bars/latest (11-50+ fields)                       │
│                   ───────────────────────────                        │
│  Bar-Level Diagnostics & Technical Indicators                       │
│  • Updates: Every bar (real-time)                                   │
│  • Persistence: In-memory only (cleared on server restart)          │
│  • Purpose: Live monitoring, charting, analysis                     │
│  • Fields: OHLC, EMA/gradient, indicators, filters, entry signals   │
│                                                                      │
│  Examples:                                                           │
│  - close: 5123.50                                                   │
│  - fastEMA: 5122.75                                                 │
│  - allowLongThisBar: true                                           │
│  - fastGradDeg: 12.3                                                │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

**Why No Overlap:**
1. **state.json** tracks strategy-level state that persists between sessions
2. **/bars/latest** tracks bar-level diagnostics that change every bar
3. **state.json** is written by strategy to file system
4. **/bars/latest** is populated by server from POST /diag endpoint
5. **Different consumers**: state.json → strategy initialization, /bars/latest → dashboards/monitoring

**Code References:**
- State export: [BarsOnTheFlow.cs](BarsOnTheFlow.cs#L1391-1441) - `ExportStrategyState()`
- State import: [BarsOnTheFlow.cs](BarsOnTheFlow.cs#L264) - `UpdateStrategyState()`
- State streaming: [BarsOnTheFlow.cs](BarsOnTheFlow.cs#L1458-1577) - `SendStrategyState()` (NEW)
- Trigger: [BarsOnTheFlow.cs](BarsOnTheFlow.cs#L430) - Called every bar
- Server endpoint: [server.py](../web/dashboard/server.py#L1441-1507) - POST `/state`, GET `/strategy/state`
- Web page: [strategy_state.html](../web/dashboard/strategy_state.html) - Real-time state monitor

### NEW: Real-Time State Streaming (Replaces File Polling)

**Previous Behavior:**
- File written every 10 bars to `strategy_state/BarsOnTheFlow_state.json`
- External tools had to poll file system for updates
- ~10 bar delay for state changes

**New Behavior (as of this implementation):**
- State **streamed to server every bar** via POST `/state`
- Cached in server memory (`strategy_state_cache`)
- Web page polls GET `/strategy/state` every second
- **Previous bar's final OHLC** included in state payload
- File backup still written every 10 bars for persistence

**State Streaming Payload (sent every bar):**
```json
{
  "timestamp": "2025-12-21 10:30:45",
  "strategyName": "BarsOnTheFlow",
  "isRunning": true,
  
  "barIndex": 156,           // Previous bar (final data)
  "barTime": "2025-12-21T10:30:00Z",
  "open": 5120.25,
  "high": 5125.50,
  "low": 5119.75,
  "close": 5123.50,
  "volume": 1234,
  
  "currentBar": 157,         // Current bar being evaluated
  
  "positionMarketPosition": "Long",
  "positionQuantity": 1,
  "positionAveragePrice": 5115.00,
  "intendedPosition": "Long",
  
  "stopLossPoints": 20,
  "calculatedStopTicks": 16,
  "calculatedStopPoints": 4.0,
  "useTrailingStop": false,
  "useDynamicStopLoss": false,
  
  "enableShorts": true,
  "avoidLongsOnBadCandle": true,
  "avoidShortsOnGoodCandle": true,
  "exitOnTrendBreak": true,
  "reverseOnTrendBreak": false,
  "fastEmaPeriod": 10,
  "gradientThresholdSkipLongs": 7.0,
  "gradientThresholdSkipShorts": -7.0,
  "gradientFilterEnabled": false,
  
  "trendLookbackBars": 5,
  "minConsecutiveBars": 3,
  "usePnLTiebreaker": false,
  
  "pendingLongFromBad": false,
  "pendingShortFromGood": false,
  
  "lastEntryBarIndex": 150,
  "lastEntryDirection": "Long"
}
```

**Access Methods:**
1. **Web UI:** [http://localhost:51888/strategy_state.html](http://localhost:51888/strategy_state.html)
2. **API (Live):** `GET http://localhost:51888/strategy/state?strategy=BarsOnTheFlow`
3. **API (Historical):** `GET http://localhost:51888/api/bars/state-history?limit=100&strategy=BarsOnTheFlow`
4. **Database:** `bars.db` - `BarsOnTheFlowStateAndBar` table
5. **File (backup):** `strategy_state/BarsOnTheFlow_state.json` (updated every 10 bars)

**Persistence:**
- **In-Memory Cache:** Latest state per strategy (real-time access)
- **bars.db:** All state updates persisted to `BarsOnTheFlowStateAndBar` table every bar
  - 54 columns capturing complete state + OHLC
  - Indexed by barIndex, receivedTs, position, currentBar
  - Survives server restarts
  - Queryable via API for historical analysis
- **File Backup:** JSON file written every 10 bars (legacy support)

**Key Differences from /bars/latest:**
- `/bars/latest` = Bar diagnostics (OHLC, indicators, filters) for last 1200 bars
- `/strategy/state` = Strategy configuration + position state (single current snapshot)
- `bars.db` = Complete state+bar history (persistent, unlimited retention)
- State includes **previous bar's final OHLC** so external tools have complete bar data
- State updates **every bar** (not just on position changes)

---

## 🌐 WEB PAGES (Dashboard)

| File | URL | Purpose | Related Strategy |
|------|-----|---------|------------------|
| `web/dashboard/static/index.html` | `/` | Main dashboard home | BarsOnTheFlow |
| `web/dashboard/strategy_state.html` | `/strategy_state.html` | **Real-time strategy state monitor** (NEW) | BarsOnTheFlow |
| `web/dashboard/bar_report.html` | `/bar_report.html` | Bar-by-bar analysis report | BarsOnTheFlow |
| `web/dashboard/botf_filter_analysis.html` | `/botf_filter_analysis.html` | BarsOnTheFlow filter analysis | BarsOnTheFlow |
| `web/dashboard/opportunity_analysis.html` | `/opportunity_analysis.html` | Entry opportunity analysis | BarsOnTheFlow |
| `web/dashboard/filter_analysis.html` | `/filter_analysis.html` | General filter analysis | BarsOnTheFlow |
| `web/dashboard/candles.html` | `/candles.html` | Candle visualization (strategy-agnostic) | Any strategy |
| `web/dashboard/candle-base.html` | `/candle-base.html` | Candle base template | BarsOnTheFlow |
| `web/barFlowReport.html` | N/A (standalone) | Bar flow report viewer | BarsOnTheFlow |

---

## 🔗 DATA FLOW DIAGRAM

```
┌─────────────────────────────────────────────────────────────────────┐
│                      BarsOnTheFlow Strategy                          │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  ┌───────────────────┐     ┌───────────────────┐                   │
│  │ RecordBarSample() │────▶│ POST /api/        │                   │
│  │ (every bar)       │     │ volatility/       │                   │
│  └───────────────────┘     │ record-bar        │                   │
│                            └─────────┬─────────┘                   │
│  ┌───────────────────┐               │                             │
│  │ GetVolumeAware    │               ▼                             │
│  │ StopTicks()       │◀─────┌───────────────────┐                  │
│  └───────────────────┘      │  volatility.db    │                  │
│           │                 │  ├─bar_samples    │                  │
│           │                 │  ├─volatility_    │                  │
│           │                 │  │  stats         │                  │
│           │                 │  └─stop_loss_     │                  │
│           ▼                 │    recommendations│                  │
│  ┌───────────────────┐      └───────────────────┘                  │
│  │ GET /api/         │                                             │
│  │ volatility/       │                                             │
│  │ recommended-stop  │                                             │
│  └───────────────────┘                                             │
│                                                                      │
│  ┌───────────────────┐     ┌───────────────────┐                   │
│  │ SendDashboard     │────▶│ POST /api/diag    │                   │
│  │ Diag()            │     └─────────┬─────────┘                   │
│  └───────────────────┘               │                             │
│                                      ▼                             │
│                            ┌───────────────────┐                   │
│                            │  dashboard.db     │                   │
│                            │  ├─diags          │                   │
│                            │  ├─trades         │                   │
│                            │  ├─entry_blocks   │                   │
│                            │  └─suggestions    │                   │
│                            └───────────────────┘                   │
│                                      │                             │
│                                      ▼                             │
│                            ┌───────────────────┐                   │
│                            │  Web Dashboard    │                   │
│                            │  ├─bar_report     │                   │
│                            │  ├─filter_analysis│                   │
│                            │  └─opportunity_   │                   │
│                            │    analysis       │                   │
│                            └───────────────────┘                   │
│                                                                      │
│  ┌───────────────────┐     ┌───────────────────┐                   │
│  │ ExportStrategy    │────▶│ strategy_state/   │                   │
│  │ State() every     │     │ BarsOnTheFlow_    │                   │
│  │ 10 bars           │     │ state.json        │                   │
│  └───────────────────┘     └───────────────────┘                   │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

---

*Last Updated: December 19, 2025*
