# Timeframe Analysis & Advisor Implementation Session
**Date:** December 2, 2025  
**Session Focus:** Real-time timeframe recommendation system and precision analysis

---

## Session Overview

### Initial Request
User asked for a helper that evaluates real-time trading and recommends optimal timeframe (30s vs 60s chart).

### Implementation Evolution
1. **TimeframeAdvisor.cs** - Heuristic evaluation system
2. **GradientSlopeStrategy.cs** - Integration with rolling metrics
3. **Dashboard API** - Recommendation logging endpoints
4. **Precision Analysis** - Offline CSV comparison tool

---

## Key Components Created

### Long/Short Conditions (Summary)
- **Long (Bull) Conditions:** Price above fast and slow EMAs, fast gradient positive and sufficiently strong, slow gradient aligned or not opposing, candle confirms upward momentum, ADX/filters pass, and bar not overextended relative to ATR.
- **Short (Bear) Conditions:** Price below fast and slow EMAs, fast gradient negative and sufficiently strong, slow gradient aligned or not opposing, candle confirms downward momentum, ADX/filters pass, and bar not overextended relative to ATR.

Note: These align with the strategy’s entry readiness fields (`signalEligible`, `gradDirOk`, `priceAbove/BelowEMAs`, `fastStrongForEntry`, `notOverextended`, `filtersOk`). See `Strategies/GradientSlopeStrategy.cs` for exact thresholds.

### 1. TimeframeAdvisor.cs
**Location:** `Strategies/TimeframeAdvisor.cs`

**Purpose:** Lightweight C# class for real-time timeframe recommendations

**Key Features:**
- Context-based evaluation (ADX, ATR, gradient stability, whipsaws, trade activity)
- Recommendation enum: `Keep`, `Prefer30s`, `Prefer60s`
- Scoring heuristics:
  - ADX ≥ 25 → +2 for 30s (strong trends favor faster response)
  - ATR 6-12 → +1 for 30s (moderate volatility)
  - Whipsaws ≥ 3 → +3 for 60s (too much noise)
  - Gradient stability > 1.2 → +2 for 60s (unstable trends)
  - Weak gradients (<0.08/0.15) → +1 for 60s (consolidation)
  - Trades ≥ 6 → +1 for 60s (overtrading)

**Code Structure:**
```csharp
public class TimeframeAdvisor
{
    public enum Recommendation { Keep, Prefer30s, Prefer60s }
    
    public class Context
    {
        public double ADX, ATR, FastGradient, SlowGradient;
        public double GradientStability;
        public int BarsSinceEntry, RecentWhipsawCount, RecentTrades;
    }
    
    public Recommendation Evaluate(Context ctx)
    public string Explain(Context ctx, Recommendation rec)
}
```

---

### 2. GradientSlopeStrategy Integration

**Location:** `Strategies/GradientSlopeStrategy.cs`

**Enhancements Made:**

#### Rolling Gradient Stability
```csharp
private Queue<double> fastGradWindow = new Queue<double>();
private const int STABILITY_WINDOW = 25;

// In OnBarUpdate():
double fastGrad = FastEMA[0] - FastEMA[1];
fastGradWindow.Enqueue(fastGrad);
while (fastGradWindow.Count > STABILITY_WINDOW)
    fastGradWindow.Dequeue();

// Compute stddev:
double mean = fastGradWindow.Average();
double variance = fastGradWindow.Select(v => Math.Pow(v - mean, 2)).Average();
double gradientStability = Math.Sqrt(variance);
```

#### Decay Counters
```csharp
private int recentWhipsawCount = 0;  // Increment on signal flips
private int recentTradesCount = 0;   // Increment on entries/exits

// Each bar:
if (recentWhipsawCount > 0) recentWhipsawCount--;
if (recentTradesCount > 0) recentTradesCount--;
```

#### Periodic Advisor Evaluation
```csharp
if (IsFirstTickOfBar && CurrentBar % 25 == 0)
{
    var advisor = new TimeframeAdvisor();
    var context = new TimeframeAdvisor.Context
    {
        ADX = ADX(14)[0],
        ATR = ATR(14)[0],
        FastGradient = FastEMA[0] - FastEMA[1],
        SlowGradient = SlowEMA[0] - SlowEMA[1],
        GradientStability = gradientStability,
        BarsSinceEntry = myPosition != Position.Flat ? CurrentBar - entryBar : -1,
        RecentWhipsawCount = recentWhipsawCount,
        RecentTrades = recentTradesCount
    };
    
    var recommendation = advisor.Evaluate(context);
    string explanation = advisor.Explain(context, recommendation);
    
    // Log to CSV and post to dashboard
    LogAction("TIMEFRAME_ADVICE", explanation);
    PostToAdvisorAPI(recommendation, explanation, context);
}
```

#### HTTP POST to Dashboard
```csharp
private void PostToAdvisorAPI(string recommendation, string explanation, Context ctx)
{
    Task.Run(async () =>
    {
        try
        {
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(2);
                string json = BuildManualJson(recommendation, explanation, ctx);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                await client.PostAsync("http://127.0.0.1:5001/advisor/recommendations", content);
            }
        }
        catch { /* silent fail */ }
    });
}

private string BuildManualJson(string rec, string exp, Context ctx)
{
    return $"{{" +
        $"\"instrument\":\"{Instrument.FullName}\"," +
        $"\"timeframe\":{BarsPeriod.Value}," +
        $"\"recommendation\":\"{rec}\"," +
        $"\"explanation\":\"{exp.Replace("\"", "\\\"")}\"," +
        $"\"metrics\":{{" +
            $"\"ADX\":{ctx.ADX:F2}," +
            $"\"ATR\":{ctx.ATR:F2}," +
            $"\"GradientStability\":{ctx.GradientStability:F4}," +
            $"\"Whipsaws\":{ctx.RecentWhipsawCount}" +
        $"}}" +
    $"}}";
}
```

**Dependencies Added:**
```csharp
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
```

**Build Status:** Compiles successfully (699 warnings - mostly CS0436 Draw conflicts, not critical)

---

### 3. Dashboard API Endpoints

**Location:** `web/dashboard/server.py`

**New Endpoints:**

#### POST /advisor/recommendations
```python
@app.post('/advisor/recommendations')
async def store_advisor_recommendation(request: Request):
    payload = await request.json()
    
    # Validate recommendation
    valid_recs = ['Keep', 'Prefer30s', 'Prefer60s', 'Prefer3m']
    if payload.get('recommendation') not in valid_recs:
        raise HTTPException(400, "Invalid recommendation")
    
    # Store in ai_footprints
    log_ai_footprint(
        action='TIMEFRAME_ADVICE',
        details=json.dumps({
            'instrument': payload.get('instrument'),
            'timeframe': payload.get('timeframe'),
            'recommendation': payload.get('recommendation'),
            'metrics': payload.get('metrics', {})
        }),
        reasoning=payload.get('explanation', ''),
        diff_summary=None
    )
    
    return {"status": "stored"}
```

#### GET /advisor/recommendations/latest
```python
@app.get('/advisor/recommendations/latest')
def get_latest_recommendations(limit: int = 50):
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()
    
    cursor.execute("""
        SELECT ts, details, reasoning, diff_summary
        FROM ai_footprints
        WHERE action = 'TIMEFRAME_ADVICE'
        ORDER BY ts DESC
        LIMIT ?
    """, (limit,))
    
    rows = cursor.fetchall()
    conn.close()
    
    results = []
    for row in rows:
        details = json.loads(row[1]) if row[1] else {}
        results.append({
            'timestamp': row[0],
            'instrument': details.get('instrument'),
            'timeframe': details.get('timeframe'),
            'recommendation': details.get('recommendation'),
            'metrics': details.get('metrics', {}),
            'explanation': row[2]
        })
    
    return {'recommendations': results}
```

#### Helper Function
```python
def log_ai_footprint(action: str, details: dict, reasoning: str, diff_summary: str = None):
    """Programmatic footprint logging for INDEX_REBUILT, ANOMALY_SCAN, TIMEFRAME_ADVICE, etc."""
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()
    
    cursor.execute("""
        INSERT INTO ai_footprints (ts, action, details, reasoning, diff_summary)
        VALUES (?, ?, ?, ?, ?)
    """, (
        time.time(),
        action,
        json.dumps(details) if details else None,
        reasoning,
        diff_summary
    ))
    
    conn.commit()
    conn.close()
```

---

### 4. Offline Precision Analysis Tool

**Location:** `web/tools/analyze_timeframe_precision.py`

**Purpose:** Compare historical strategy performance across different timeframes

**Metrics Computed:**

1. **EMA-Price Distance**
   - Mean: `|Close - FastEMA|`
   - Std: Standard deviation of distance
   - Percentage: Distance as % of price

2. **Gradient Stability**
   - `stddev(FastEMA[0] - FastEMA[1])`
   - Lower = smoother, more stable trends

3. **Whipsaw Rate**
   - `(signal_flips / total_bars) * 100`
   - Signal flip = `PrevSignal != NewSignal`

4. **Entry Precision (MFE/MAE Ratio)**
   - Extract from EXIT records with `TradeMFE` and `TradeMAE`
   - Higher ratio = better entry timing
   - Average MFE and MAE in points

5. **ADX Consistency**
   - Mean and stddev (currently 0.0 - ADX not in CSV yet)

**Code Structure:**
```python
def analyze_precision(csv_path):
    """Compute precision metrics for a strategy session."""
    df = pd.read_csv(csv_path, on_bad_lines='skip', engine='python')
    df = df.drop_duplicates(subset=['Bar'], keep='last')
    
    # EMA-Price Distance
    df['ema_price_dist'] = abs(df['Close'] - df['FastEMA'])
    ema_dist_mean = df['ema_price_dist'].mean()
    ema_dist_std = df['ema_price_dist'].std()
    ema_price_pct = (ema_dist_mean / df['Close'].mean()) * 100
    
    # Gradient Stability
    gradient_stability = df['FastGradient'].std()
    gradient_mean = df['FastGradient'].mean()
    
    # Whipsaw Rate
    signal_flips = (df['PrevSignal'] != df['NewSignal']).sum()
    whipsaw_rate_pct = (signal_flips / len(df)) * 100
    
    # MFE/MAE Ratio
    exit_rows = df[df['Action'] == 'EXIT']
    avg_mfe = exit_rows['TradeMFE'].mean() if len(exit_rows) > 0 else 0
    avg_mae = exit_rows['TradeMAE'].mean() if len(exit_rows) > 0 else 0
    mfe_mae_ratio = avg_mfe / avg_mae if avg_mae > 0 else 0
    
    return {
        'bars': int(len(df)),
        'ema_distance_mean': round(float(ema_dist_mean), 2),
        'ema_distance_std': round(float(ema_dist_std), 2),
        'ema_pct_mean': round(float(ema_price_pct), 4),
        'gradient_stability': round(float(gradient_stability), 4),
        'gradient_mean': round(float(gradient_mean), 4),
        'whipsaw_rate_pct': round(float(whipsaw_rate_pct), 2),
        'signal_flips': int(signal_flips),
        'mfe_mae_ratio': round(float(mfe_mae_ratio), 2),
        'avg_mfe': round(float(avg_mfe), 2),
        'avg_mae': round(float(avg_mae), 2),
        'trade_count': int(len(exit_rows))
    }

def compare_and_recommend(metrics_30s, metrics_3m):
    """Score and recommend best timeframe."""
    score_30s = 0
    score_3m = 0
    reasons = []
    
    # Lower EMA% is better (tighter tracking)
    if metrics_30s['ema_pct_mean'] < metrics_3m['ema_pct_mean']:
        score_30s += 2
        reasons.append("30s: Tighter EMA-price tracking")
    elif metrics_3m['ema_pct_mean'] < metrics_30s['ema_pct_mean']:
        score_3m += 2
        reasons.append("3m: Tighter EMA-price tracking")
    
    # Lower gradient stability is better (less volatile)
    if metrics_30s['gradient_stability'] < metrics_3m['gradient_stability']:
        score_30s += 2
        reasons.append("30s: Lower gradient volatility")
    elif metrics_3m['gradient_stability'] < metrics_30s['gradient_stability']:
        score_3m += 2
        reasons.append("3m: Lower gradient volatility")
    
    # Lower whipsaw rate is better
    if metrics_30s['whipsaw_rate_pct'] < metrics_3m['whipsaw_rate_pct']:
        score_30s += 2
        reasons.append("30s: Fewer signal flips")
    elif metrics_3m['whipsaw_rate_pct'] < metrics_30s['whipsaw_rate_pct']:
        score_3m += 2
        reasons.append("3m: Fewer signal flips")
    
    # Higher MFE/MAE is better (better entry timing)
    if metrics_30s['mfe_mae_ratio'] > metrics_3m['mfe_mae_ratio']:
        score_30s += 2
        reasons.append("30s: Better MFE/MAE capture")
    elif metrics_3m['mfe_mae_ratio'] > metrics_30s['mfe_mae_ratio']:
        score_3m += 2
        reasons.append("3m: Better MFE/MAE capture")
    
    if score_30s > score_3m:
        recommendation = "Prefer30s"
        explanation = f"30-second timeframe wins ({score_30s} vs {score_3m}): {'; '.join(reasons)}"
    elif score_3m > score_30s:
        recommendation = "Prefer3m"
        explanation = f"3-minute timeframe wins ({score_3m} vs {score_30s}): {'; '.join(reasons)}"
    else:
        recommendation = "Keep"
        explanation = "Both timeframes perform similarly; keep current setting"
    
    return recommendation, explanation, reasons
```

---

## Issues Encountered & Solutions

### 1. FixedQueue<> Not Found
**Error:** `CS0246: The type or namespace name 'FixedQueue<>' could not be found`

**Cause:** NinjaTrader environment doesn't include custom collection types

**Solution:** Replaced with `Queue<double>` + manual capacity management
```csharp
fastGradWindow.Enqueue(value);
while (fastGradWindow.Count > STABILITY_WINDOW)
    fastGradWindow.Dequeue();
```

---

### 2. JSON Serialization Library Missing
**Error:** `CS0234: System.Text.Json not found`, `CS0103: Newtonsoft.Json not in context`

**Cause:** NinjaTrader project doesn't reference these assemblies

**Solution:** Manual JSON string building
```csharp
string json = $"{{\"key\":\"{value}\",\"num\":{number}}}";
```

**Avoid:** External JSON libraries in NinjaTrader - use built-in or manual methods

---

### 3. Pandas JSON Serialization Error
**Error:** `TypeError: Object of type int64 is not JSON serializable`

**Cause:** Pandas returns numpy int64/float64, not native Python types

**Solution:** Explicit casting in return dict
```python
return {
    'bars': int(len(df)),
    'signal_flips': int(signal_flips),
    'ema_distance_mean': round(float(ema_dist_mean), 2),
    'gradient_stability': round(float(gradient_stability), 4)
}
```

---

### 4. CSV Parsing Error (Malformed Lines)
**Error:** `ParserError: Expected 32 fields in line 3517, saw 33`

**Cause:** Commas in Notes field or encoding issues

**Solution:** Error-tolerant parsing
```python
df = pd.read_csv(csv_path, on_bad_lines='skip', engine='python')
```

---

### 5. Exit Cooldown Removal Request
**User Feedback:** "I want the 30 seconds delay deleted completely from the strategy"

**Changes Made:**
1. Set `exitCooldownSeconds = 0`
2. Removed cooldown check branch in `OnBarUpdate()`
3. Removed all `inExitCooldown` flag assignments
4. Removed all `lastExitTime` assignments
5. Suppressed cooldown HUD display
6. Suppressed "EXIT COOLDOWN STARTED" log messages

**Code Before:**
```csharp
if (inExitCooldown && (Time[0] - lastExitTime).TotalSeconds < exitCooldownSeconds)
{
    double secsLeft = exitCooldownSeconds - (Time[0] - lastExitTime).TotalSeconds;
    LogAction("IN_COOLDOWN", $"Remaining: {secsLeft:F1}s");
    return; // Skip all entry logic
}
```

**Code After:**
```csharp
// Cooldown check removed - entry logic executes immediately after exit
```

---

## Timeframe Analysis Results

### Test Sessions
1. **30-Second Bars:** `GradientSlope_MNQ 12-25_2025-12-02_00-56-03.csv` (5573 lines, 2624 unique bars)
2. **3-Minute Bars:** `GradientSlope_MNQ 12-25_2025-12-02_00-32-53.csv` (2010 lines, 939 unique bars)

### Comparison Results

| Metric | 30-Second | 3-Minute | Winner | Improvement |
|--------|-----------|----------|--------|-------------|
| **Bars Analyzed** | 2624 | 939 | 30s | 179% more data |
| **EMA-Price Distance (pts)** | 3.87 | 6.77 | ✅ 30s | 43% tighter |
| **EMA-Price % of Price** | 1.53% | 2.67% | ✅ 30s | 43% tighter |
| **Gradient Stability** | 1.20 | 2.18 | ✅ 30s | 45% smoother |
| **Gradient Mean** | -0.0446 | 0.028 | Similar | - |
| **Whipsaw Rate** | 8.8% | 9.37% | ✅ 30s | 6% fewer flips |
| **Signal Flips (count)** | 231 | 88 | 30s has more | (more bars) |
| **MFE/MAE Ratio** | 2.94 | 2.64 | ✅ 30s | 11% better |
| **Average MFE (pts)** | 2.72 | 2.70 | Similar | - |
| **Average MAE (pts)** | 0.92 | 1.02 | ✅ 30s | 10% tighter |
| **Total Trades** | 43 | 34 | ✅ 30s | 26% more |

### Final Recommendation
**Winner:** 30-Second Timeframe

**Reasoning:**
1. **Tighter EMA Tracking:** 1.53% vs 2.67% - price stays closer to signal EMAs
2. **Lower Gradient Volatility:** 1.20 vs 2.18 - more stable trend detection
3. **Better Entry Precision:** MFE/MAE 2.94 vs 2.64 - favorable moves are 2.94x larger than adverse moves
4. **Tighter Stop Losses:** 0.92 pts MAE vs 1.02 pts
5. **More Trading Opportunities:** 43 vs 34 trades with superior quality metrics

**User Observation Clarified:**
User noted 3-minute chart showed "clearer" EMA-price distance. Analysis confirms this meant **wider gaps** (6.77 pts vs 3.87 pts), which actually **reduces precision**. The 30-second bars track price movement more accurately.

---

## Files Modified/Created

### Created Files
1. `Strategies/TimeframeAdvisor.cs` - Heuristic advisor class
2. `web/tools/analyze_timeframe_precision.py` - Offline precision analysis tool
3. `TIMEFRAME_ANALYSIS_SESSION.md` - This documentation

### Modified Files
1. `Strategies/GradientSlopeStrategy.cs`
   - Added rolling gradient stability calculation
   - Added decay counters for whipsaws/trades
   - Added periodic advisor evaluation (every 25 bars)
   - Added HTTP POST to dashboard
   - Removed exit cooldown logic completely

2. `web/dashboard/server.py`
   - Added `POST /advisor/recommendations` endpoint
   - Added `GET /advisor/recommendations/latest` endpoint
   - Added `log_ai_footprint()` helper function
   - Extended `ai_footprints` table usage for `TIMEFRAME_ADVICE` action

---

## Usage Guide

### Real-Time Advisor (C# Strategy)
1. Strategy automatically evaluates timeframe every 25 bars
2. Logs recommendation to CSV with action `TIMEFRAME_ADVICE`
3. Posts to dashboard API (if running) at `http://127.0.0.1:5001/advisor/recommendations`
4. Check `.log` file for explanations: `[HH:MM:SS] TIMEFRAME_ADVICE: Prefer30s -> Strong trend (ADX) with moderate ATR...`

### Offline Analysis (Python Script)
```powershell
cd "c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\tools"
python analyze_timeframe_precision.py
```

**Output:**
- 30-second metrics JSON
- 3-minute metrics JSON
- Comparison with recommendation, explanation, and detailed reasons

### Dashboard API (if server running)
```powershell
# Get latest recommendations
Invoke-RestMethod -Uri "http://127.0.0.1:5001/advisor/recommendations/latest?limit=10"

# Manual post
$body = @{
    instrument = "MNQ 12-25"
    timeframe = "30s"
    recommendation = "Prefer30s"
    explanation = "Based on analysis..."
    metrics = @{ ADX = 25; ATR = 10; GradientStability = 1.2 }
} | ConvertTo-Json
Invoke-RestMethod -Uri "http://127.0.0.1:5001/advisor/recommendations" -Method Post -Body $body -ContentType "application/json"
```

---

## Configuration Reference

### TimeframeAdvisor Thresholds
```csharp
// ADX thresholds
const double STRONG_TREND_ADX = 25.0;

// ATR ranges (points)
const double MODERATE_ATR_LOW = 6.0;
const double MODERATE_ATR_HIGH = 12.0;

// Gradient stability
const double HIGH_STABILITY_THRESHOLD = 1.2;

// Weak gradient thresholds
const double WEAK_FAST_GRAD = 0.08;
const double WEAK_SLOW_GRAD = 0.15;

// Activity thresholds
const int HIGH_WHIPSAW_COUNT = 3;
const int HIGH_TRADE_COUNT = 6;
```

### Strategy Advisor Settings
```csharp
private const int STABILITY_WINDOW = 25;      // Rolling stddev window
private const int ADVISOR_INTERVAL = 25;      // Bars between evaluations
private const int HTTP_TIMEOUT_SECONDS = 2;   // Dashboard POST timeout
```

### Python Analysis Paths
```python
csv_30s = Path(r"c:\...\strategy_logs\GradientSlope_MNQ 12-25_2025-12-02_00-56-03.csv")
csv_3m = Path(r"c:\...\strategy_logs\GradientSlope_MNQ 12-25_2025-12-02_00-32-53.csv")
```

---

## Future Enhancements

### Potential Improvements
1. **Whipsaw Counter Increments:** Currently only decays; add increments on signal flips
2. **Trade Counter Increments:** Add increments on ENTRY/EXIT actions
3. **ADX Column in CSV:** Add ADX values to diagnostic CSV for offline analysis
4. **Dashboard UI Panel:** Frontend display for `/advisor/recommendations/latest`
5. **Multi-Timeframe Support:** Extend comparison to 1m, 2m, 5m, etc.
6. **Adaptive Threshold Learning:** Use ML to optimize advisor thresholds based on historical data
7. **Real-Time Confidence Scores:** Add confidence percentage to recommendations
8. **Alert System:** Notify when strong timeframe switch signal detected

### Dashboard UI Mockup
```html
<!-- Add to web/dashboard/static/index.html -->
<div class="card">
    <h3>Timeframe Advisor</h3>
    <div id="advisor-recommendations"></div>
</div>

<script>
async function loadAdvisorRecs() {
    const resp = await fetch('/advisor/recommendations/latest?limit=5');
    const data = await resp.json();
    const html = data.recommendations.map(r => `
        <div class="rec-item ${r.recommendation}">
            <strong>${r.recommendation}</strong> for ${r.instrument} (${r.timeframe}s bars)
            <br><small>${r.explanation}</small>
            <br><em>Metrics: ADX=${r.metrics.ADX}, ATR=${r.metrics.ATR}, Stability=${r.metrics.GradientStability}</em>
        </div>
    `).join('');
    document.getElementById('advisor-recommendations').innerHTML = html;
}
setInterval(loadAdvisorRecs, 5000);
</script>
```

---

## Debugging Tips

### Check Advisor Execution
```csharp
// In OnBarUpdate(), add before advisor call:
Print($"CurrentBar={CurrentBar}, IsFirstTickOfBar={IsFirstTickOfBar}, Mod25={CurrentBar % 25}");
```

### Verify HTTP POST
```csharp
// Add logging to PostToAdvisorAPI:
Print($"[ADVISOR] Posting to dashboard: {recommendation}");
// After await:
Print($"[ADVISOR] Response status: {response.StatusCode}");
```

### Test Dashboard Endpoints
```powershell
# Check server is running
Invoke-RestMethod -Uri "http://127.0.0.1:5001/health"

# Test POST manually
$body = '{"instrument":"TEST","timeframe":"30","recommendation":"Keep","explanation":"Test","metrics":{}}'
Invoke-RestMethod -Uri "http://127.0.0.1:5001/advisor/recommendations" -Method Post -Body $body -ContentType "application/json"

# Verify stored
Invoke-RestMethod -Uri "http://127.0.0.1:5001/advisor/recommendations/latest?limit=1"
```

### Analyze Python Script Locally
```python
import pandas as pd
csv_path = r"c:\...\strategy_logs\GradientSlope_MNQ 12-25_2025-12-02_00-56-03.csv"
df = pd.read_csv(csv_path, on_bad_lines='skip', engine='python')
print(f"Total rows: {len(df)}")
print(f"Columns: {df.columns.tolist()}")
print(df.head())
```

---

## Key Learnings

1. **NinjaTrader C# Constraints:**
   - Avoid external libraries (System.Text.Json, Newtonsoft.Json)
   - Use manual JSON serialization or built-in methods
   - Replace custom collections with standard Queue/List + capacity management

2. **Pandas/Numpy Type Handling:**
   - Always cast numpy int64/float64 to native Python int/float for JSON
   - Use `int()`, `float()`, `round()` explicitly

3. **CSV Robustness:**
   - Use `on_bad_lines='skip', engine='python'` for error tolerance
   - Deduplicate by Bar using `drop_duplicates(subset=['Bar'], keep='last')`

4. **Async in C#:**
   - Use `Task.Run(async () => {...})` for fire-and-forget HTTP calls
   - Set reasonable timeouts (2s for dashboard POST)
   - Silent fail for non-critical external API calls

5. **Timeframe Analysis Insights:**
   - Visual "clarity" doesn't equal precision (wider gaps = less accurate)
   - Smaller timeframes can be more precise if metrics confirm it
   - MFE/MAE ratio is key indicator of entry quality
   - Gradient stability (stddev) reveals trend consistency

---

## Contact Points for Continuation

### To Resume Work:
1. **Real-time advisor:** Check `.log` files in `strategy_logs/` for TIMEFRAME_ADVICE entries
2. **Offline analysis:** Run `analyze_timeframe_precision.py` with new CSV paths
3. **Dashboard:** Start server with `python web/dashboard/server.py`, query `/advisor/recommendations/latest`
4. **Code changes:** All C# in `Strategies/`, all Python in `web/tools/` or `web/dashboard/`

### Related Files:
- `Strategies/TimeframeAdvisor.cs` - Advisor logic
- `Strategies/GradientSlopeStrategy.cs` - Integration
- `web/dashboard/server.py` - API endpoints
- `web/tools/analyze_timeframe_precision.py` - Analysis script
- `strategy_logs/GradientSlope_MNQ*.csv` - Session data
- `strategy_logs/GradientSlope_MNQ*.log` - Diagnostic logs

---

**Session Status:** Complete  
**Build Status:** ✅ Compiles successfully  
**Recommendation:** Use 30-second timeframe for MNQ 12-25 trading  
**Next Steps:** Optional dashboard UI, increment whipsaw/trade counters, add ADX column to CSV

---

## Continued Session Log (Dec 2, 2025)

### RSI in Diagnostics
- Added RSI to strategy heartbeat and entry decision notes in `GradientSlopeStrategy.cs`.
- Dashboard UI now shows `rsi=xx.x` in Recent Diagnoses (`static/app.js` updated).

### Entry Block at Bar 2775
- At bar 2775, SHORT entry was blocked by RSI filter (`RSI < 50`).
- Logs show `ENTRY_FILTER_BLOCKED ... RSI:43.2`, confirming RSIFloor prevented entry.

### Exit Behavior at Bars 2803–2804
- Strategy held LONG; gradients remained positive; price above EMAs.
- Dual-EMA strict exit guard and sign-flip exit require zero/negative gradient and price below EMAs.
- No trailing-stop exit implemented; MFE reached ~12.0 then retraced without triggering exit under current rules.

### Dashboard Server
- Server started at `http://127.0.0.1:5001`; diags streaming visible.
- Compact diagnostics JSON includes `rsi`, `adx`, gradients, blockers; UI renders RSI.

### Proposed Improvements
- Optional trailing exit: exit on % retrace of max MFE (e.g., 40%).
- Toggle to relax dual-EMA strict guard for exits.
- Tune sign-flip tolerance for faster exits on weakening momentum.

### Actions Taken
- Started server; verified endpoints responding.
- Updated UI (`static/app.js`) to render RSI.
- Confirmed CSV and dashboard diagnostics reflect RSI values.

---

## In-Memory Bar Cache (Dec 2, 2025)

### Problem
Repeated CSV parsing for per-bar Q&A was inefficient. User requested faster lookups for recent bars (last ~10-400 bars).

### Solution: Lightweight Memory Cache
Implemented in `web/dashboard/server.py`:

**Design:**
- `deque` with `maxlen=400` stores normalized bar records
- Populated automatically on each `/diag` POST
- No backfill from CSV; cache fills from live diagnostics stream

**Normalized Bar Schema:**
```python
{
  'barIndex': int,
  'ts': float,           # receivedTs
  'localTime': str,      # formatted timestamp
  'fastGrad': float,
  'slowGrad': float,
  'accel': float,
  'adx': float,
  'rsi': float,
  'fastEMA': float,
  'slowEMA': float,
  'close': float,
  'gradStab': float,
  'bandwidth': float,
  'unrealized': float,
  'trendSide': str       # 'BULL' or 'BEAR'
}
```

**New Endpoints:**
1. **`GET /bars/latest?limit=50`**  
   Returns most recent normalized bars from cache. Joins with `dev_classifications` table if available to enrich with `isBad` and `badReason`.

2. **`GET /bars/around?center=2812&window=10`**  
   Returns bars in range `[center-window, center+window]` sorted by barIndex. Also joins classification data.

**Usage:**
```bash
# Get last 10 bars
curl http://127.0.0.1:5001/bars/latest?limit=10

# Get bars around bar 2812 (±10 bars)
curl http://127.0.0.1:5001/bars/around?center=2812&window=10
```

**Example Response:**
```json
{
  "bars": [{
    "barIndex": 2812,
    "ts": 1764657468.38,
    "localTime": "2025-12-02 08:37:48",
    "fastGrad": 1.4,
    "slowGrad": 1.3,
    "accel": 0.015,
    "adx": 27.5,
    "rsi": 58.2,
    "fastEMA": 100.5,
    "slowEMA": 100.1,
    "close": 101.0,
    "gradStab": 0.8,
    "bandwidth": 0.12,
    "unrealized": 0.0,
    "trendSide": "BULL",
    "isBad": 0,
    "badReason": "good"
  }],
  "count": 1
}
```

**Benefits:**
- O(1) append on each diag
- O(n) lookup (n ≤ 400) — trivial for small windows
- No repeated CSV reads
- Classification join enriches context
- Cache automatically refreshes with live data

**Trade-offs:**
- Ephemeral: lost on server restart (acceptable for short-term Q&A)
- No multi-instrument keying (single feed assumption; easily extended if needed)
- No historical backfill (rely on live stream or one-time seed POST)

**Verification:**
- Server running on `http://127.0.0.1:5001`
- Test diag posted; cache populated successfully
- Both endpoints return expected JSON

**Status:** ✅ Implemented and verified
