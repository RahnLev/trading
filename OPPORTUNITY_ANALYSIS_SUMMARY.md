# Opportunity Analysis System - Quick Reference

## What Was Created

### 1. Interactive Web Dashboard (`opportunity_analysis.html`)
- **URL**: http://localhost:51888/opportunity_analysis.html
- **Features**:
  - Select opportunity log files from dropdown
  - Adjust parameters dynamically (streak length, gradient thresholds, etc.)
  - Real-time analysis with visual results
  - Color-coded table (green=caught, red=missed, yellow=partial)
  - Statistics cards showing catch rates and missed points

### 2. Python Analysis Script (`analyze_opportunity_streaks.py`)
- **Standalone script** for command-line analysis
- Generates detailed console reports
- Exports results to CSV
- Shows top missed opportunities
- Verbose mode for detailed streak breakdown

### 3. Comparison Script (`compare_opportunity_logs.py`)
- **Side-by-side comparison** of two logs (e.g., 30s vs 2min)
- Compares: total streaks, catch rates, block reasons
- Shows which timeframe performs better
- Lists top missed opportunities from each

### 4. Server API Endpoints (added to `server.py`)
- `/opportunity_analysis` - Serve the web page
- `/api/opportunity-files` - List available logs
- `/api/analyze-streaks` - Perform analysis (called by web UI)

## Quick Start

### Web Dashboard (Recommended for Interactive Analysis)

```bash
# 1. Start server
cd "c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard"
python server.py

# 2. Open browser
http://localhost:51888/opportunity_analysis.html

# 3. Select file and click "Analyze Streaks"
```

### Python Script (For Batch Analysis/Exports)

```bash
cd "c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom"

# Basic analysis
python analyze_opportunity_streaks.py "strategy_logs\BarsOnTheFlow_Opportunities_*.csv"

# With custom parameters and export
python analyze_opportunity_streaks.py "strategy_logs\BarsOnTheFlow_Opportunities_*.csv" \
    --min-streak 5 \
    --max-streak 8 \
    --min-movement 5.0 \
    --verbose \
    --output results.csv
```

### Compare Two Logs

```bash
# Compare 30-second vs 2-minute bars
python compare_opportunity_logs.py \
    "strategy_logs\BarsOnTheFlow_Opportunities_MNQ 12-25_2025-12-15_10-29-44-580.csv" \
    "strategy_logs\BarsOnTheFlow_Opportunities_MNQ 12-25_2025-12-15_10-31-21-297.csv" \
    --label1 "30-Second Bars" \
    --label2 "2-Minute Bars"
```

## What It Does

### Streak Detection
Finds sequences of bars with:
- **Consistent direction** - Net price movement in one direction
- **Minimum movement** - Significant change (default: 5+ points)
- **Allowed counter-trend bars** - Some opposite bars OK (default: 20% = 1 in 5)
- **Gradient threshold** - Average EMA slope meets requirements

### Entry Status Classification
- **Caught** ✅ - Entered at start of streak (optimal)
- **Missed** ❌ - Valid streak but blocked by filters
- **Partial** ⚠️ - Entered mid-streak (late entry)

### Key Metrics
- **Catch Rate** - % of streaks successfully entered
- **Avg Missed Points** - Profit lost on missed opportunities
- **Block Reasons** - Why entries were skipped (gradient, candle avoidance, etc.)

## Parameters Explained

| Parameter | Default | Description |
|-----------|---------|-------------|
| Min Streak | 5 | Minimum consecutive bars |
| Max Streak | 8 | Maximum consecutive bars |
| Long Gradient | 7.0° | Min gradient for longs |
| Short Gradient | -7.0° | Max gradient for shorts |
| Min Movement | 5.0 pts | Min net price change |
| Counter Ratio | 0.2 | Max ratio of opposite bars (0.2 = 1 in 5) |

## Use Cases

### 1. Find Best Timeframe
- Run strategy on 30s, 1m, 2m bars with `EnableOpportunityLog=true`
- Compare catch rates and missed opportunities
- Choose timeframe with best performance

### 2. Optimize Gradient Thresholds
- If catch rate is low: **Lower** gradient thresholds (e.g., 5° instead of 7°)
- If catching bad trades: **Raise** gradient thresholds (e.g., 10°)
- Use web dashboard to test different values quickly

### 3. Identify Problem Filters
- Review "Block Reasons" in results
- If `GradientFilter` is blocking good streaks → relax gradient
- If `AvoidLongsOnBadCandle` is common → consider disabling or tuning

### 4. Validate Strategy Changes
- Make parameter change in strategy
- Run on same data
- Compare before/after catch rates

## Example Workflows

### Interactive Tuning (Web Dashboard)
1. Open http://localhost:51888/opportunity_analysis.html
2. Select first log (30s bars)
3. Run with defaults → note catch rate
4. Adjust Long Gradient from 7° to 5°
5. Re-analyze → see if catch rate improves
6. Try different parameters until optimal
7. Repeat for second log (2min bars)
8. Compare which timeframe + parameters work best

### Batch Analysis (Python)
```bash
# Analyze all logs with same parameters
for log in strategy_logs/BarsOnTheFlow_Opportunities_*.csv; do
    python analyze_opportunity_streaks.py "$log" --output "${log%.csv}_analysis.csv"
done

# Compare results in Excel or pandas
```

### Full Comparison Report
```bash
# Generate detailed comparison
python compare_opportunity_logs.py \
    "logs/30sec.csv" \
    "logs/2min.csv" \
    --label1 "30s" \
    --label2 "2m" \
    --verbose > comparison_report.txt
```

## Output Examples

### Console Output (Python Script)
```
======================================================================
OPPORTUNITY STREAK ANALYSIS SUMMARY
======================================================================

Data Source: 2360 bars analyzed
Parameters:
  - Streak range: 5-8 bars
  - Min movement: 5.0 points
  - Long gradient threshold: 7.0°
  - Short gradient threshold: -7.0°

                              RESULTS                                
----------------------------------------------------------------------
Total Streaks Found:          23
  - Long streaks:             12
  - Short streaks:            11

Entry Status:
  - Caught (entered):          8  (34.8%)
  - Missed completely:        12  (52.2%)
  - Partial (late):            3  (13.0%)

Performance Metrics:
  - Catch rate:              34.8%
  - Avg streak length:        5.4 bars
  - Avg net movement:         7.23 points
  - Avg missed points:        6.45 points
======================================================================
```

### CSV Export Columns
- streak_num, direction, status
- start_bar, end_bar, length
- net_movement, missed_points
- avg_gradient, gradient_ok
- block_reasons, pattern

## Tips

### Low Catch Rate?
- Lower gradient thresholds
- Increase counter_ratio
- Reduce min_movement

### Too Many False Signals?
- Raise gradient thresholds
- Decrease counter_ratio
- Increase min_movement

### Different Timeframes?
- Shorter (30s): May need stricter filters
- Longer (2m): Can use more relaxed filters

## Files Location

```
c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\
├── web\dashboard\
│   ├── opportunity_analysis.html     (Web UI)
│   └── server.py                     (API endpoints added)
├── strategy_logs\
│   └── BarsOnTheFlow_Opportunities_*.csv  (Generated by strategy)
├── analyze_opportunity_streaks.py    (Standalone script)
├── compare_opportunity_logs.py       (Comparison script)
└── OPPORTUNITY_ANALYSIS_GUIDE.md     (Full documentation)
```

## Next Steps

1. ✅ System is ready to use
2. 📊 Run your strategy on different timeframes with `EnableOpportunityLog=true`
3. 🔍 Analyze logs using web dashboard or Python scripts
4. 📈 Compare 30s vs 2min to find optimal timeframe
5. ⚙️ Adjust strategy parameters based on findings
6. ♻️ Repeat and validate improvements

## Support

- Check `OPPORTUNITY_ANALYSIS_GUIDE.md` for detailed documentation
- Use `--verbose` flag in Python scripts for detailed output
- Review console errors if analysis fails
- Ensure opportunity logs exist in strategy_logs folder
