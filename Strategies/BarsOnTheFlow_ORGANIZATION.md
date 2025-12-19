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
