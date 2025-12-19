# Opportunity Streak Analysis Guide

## Overview

This system analyzes BarsOnTheFlow opportunity logs to find directional "streaks" (consistent price movements similar to the 5-bar patterns) and identifies which opportunities were caught vs missed.

## Files Created

1. **`opportunity_analysis.html`** - Interactive web dashboard
2. **`analyze_opportunity_streaks.py`** - Standalone Python analysis script
3. **Server API endpoints** in `server.py`:
   - `/opportunity_analysis` - Serve the HTML page
   - `/api/opportunity-files` - List available opportunity logs
   - `/api/analyze-streaks` - Perform streak analysis

## Quick Start - Web Dashboard

### 1. Start the Server

```bash
cd "c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard"
python server.py
```

Server will run on: http://localhost:51888

### 2. Open the Dashboard

Navigate to: **http://localhost:51888/opportunity_analysis.html**

### 3. Use the Interface

1. **Select a log file** - Choose from available `BarsOnTheFlow_Opportunities_*.csv` files
2. **Configure parameters**:
   - **Min/Max Streak Length**: Range of consecutive bars to scan (default: 5-8)
   - **Gradient Thresholds**: Long (7°) and Short (-7°) gradient requirements
   - **Min Net Movement**: Minimum price movement in points (default: 5.0)
   - **Counter-Trend Ratio**: Allowed ratio of opposite-direction bars (default: 0.2 = 1 in 5)
   - **Streak Type**: Analyze longs, shorts, or both

3. **Click "Analyze Streaks"** to run analysis

4. **View Results**:
   - Top stats cards show: Total streaks, caught, missed, partial entries, catch rate
   - Table shows each streak with: bars, direction, length, entry status, missed points, block reasons

## Quick Start - Python Script

### 1. Run Analysis

```bash
cd "c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom"
python analyze_opportunity_streaks.py "strategy_logs\BarsOnTheFlow_Opportunities_MNQ 12-25_2025-12-15_10-31-21-297.csv"
```

### 2. With Custom Parameters

```bash
python analyze_opportunity_streaks.py "strategy_logs\BarsOnTheFlow_Opportunities_*.csv" \
    --min-streak 5 \
    --max-streak 8 \
    --min-movement 5.0 \
    --long-grad 7.0 \
    --short-grad -7.0 \
    --counter-ratio 0.2 \
    --type both \
    --verbose \
    --output results.csv
```

### 3. Command Line Options

- `--min-streak <int>` - Minimum streak length (default: 5)
- `--max-streak <int>` - Maximum streak length (default: 8)
- `--min-movement <float>` - Minimum net price movement in points (default: 5.0)
- `--long-grad <float>` - Long gradient threshold in degrees (default: 7.0)
- `--short-grad <float>` - Short gradient threshold in degrees (default: -7.0)
- `--counter-ratio <float>` - Max counter-trend bar ratio 0-1 (default: 0.2)
- `--type <long|short|both>` - Analyze longs, shorts, or both (default: both)
- `--output <file>` - Save results to CSV file
- `--verbose` - Show detailed streak information

## Understanding the Analysis

### What is a Streak?

A "streak" is a sequence of bars with:
1. **Consistent overall direction** - Net price movement in one direction
2. **Minimum net movement** - Significant price change (default: 5+ points)
3. **Allowable counter-trend bars** - Some opposite bars allowed based on ratio
4. **Optional gradient filter** - Average EMA gradient meets threshold

This mimics BarsOnTheFlow's logic of looking for 5-bar patterns with consistent direction.

### Entry Status Categories

- **Caught** ✅ - Strategy entered at start of streak
- **Missed** ❌ - Valid streak but no entry (blocked by filters)
- **Partial** ⚠️ - Entered mid-streak (late entry)

### Key Metrics

- **Catch Rate** - Percentage of streaks successfully entered
- **Avg Missed Points** - Average profit lost on missed opportunities
- **Block Reasons** - Why entries were blocked (gradient filter, candle avoidance, etc.)

## Comparing Different Timeframes

### Example: 30-Second vs 2-Minute Bars

1. Run strategy on both timeframes with `EnableOpportunityLog=true`
2. Use the web dashboard or script to analyze each log
3. Compare:
   - Number of streaks found
   - Catch rates
   - Average streak lengths
   - Most common block reasons
   - Missed points per streak

### Analysis Questions

- Do shorter timeframes show more streaks but noisier signals?
- Which timeframe has better catch rates?
- Are gradient thresholds appropriate for each timeframe?
- Which block reasons are most common for missed opportunities?

## Workflow Recommendations

### 1. Initial Analysis (Web Dashboard)

1. Load both 30-second and 2-minute logs
2. Run with default parameters
3. Compare catch rates and missed opportunities
4. Identify patterns in block reasons

### 2. Parameter Optimization

Adjust parameters dynamically in the web dashboard:
- If catch rate is low: **relax** gradient thresholds
- If too many false signals: **tighten** min movement or counter-ratio
- If missing short trends: check if short gradient threshold is appropriate

### 3. Export Detailed Results (Python Script)

```bash
# 30-second bars analysis
python analyze_opportunity_streaks.py logs/30sec.csv --output analysis_30sec.csv --verbose

# 2-minute bars analysis
python analyze_opportunity_streaks.py logs/2min.csv --output analysis_2min.csv --verbose
```

### 4. Compare Results in Excel

Open both CSV outputs to compare:
- Sort by `missed_points` to find biggest missed opportunities
- Filter by `block_reasons` to see which filters are blocking good trades
- Compare `counter_ratio` to see how "clean" the streaks are

## Advanced Use Cases

### Find Longest Missed Streaks

```bash
python analyze_opportunity_streaks.py log.csv --min-streak 8 --max-streak 15 --verbose
```

### Long-Only Analysis with Strict Gradient

```bash
python analyze_opportunity_streaks.py log.csv --type long --long-grad 10.0 --output long_strict.csv
```

### Short-Only with Relaxed Requirements

```bash
python analyze_opportunity_streaks.py log.csv --type short --short-grad -5.0 --min-movement 3.0
```

### Very Strict: Pure Directional Runs

```bash
python analyze_opportunity_streaks.py log.csv --counter-ratio 0.0 --min-streak 5 --verbose
```

## Interpreting Block Reasons

Common block reasons and what they mean:

- **`GradientFilter (-0.23° > -7.00°)`** - EMA gradient too weak/wrong direction
- **`AvoidLongsOnBadCandle`** - Long signal but previous bar was down
- **`AvoidShortsOnGoodCandle`** - Short signal but previous bar was up
- **`Trend requirements not met`** - Not enough consecutive bars yet
- **`Position already open`** - Already in a trade

## Tips for Optimization

1. **If missing lots of good streaks**:
   - Lower gradient thresholds (e.g., 5° instead of 7°)
   - Increase counter-ratio (allow more counter-trend bars)
   - Reduce min movement requirement

2. **If catching bad streaks (false signals)**:
   - Raise gradient thresholds
   - Decrease counter-ratio (require cleaner trends)
   - Increase min movement requirement

3. **For different timeframes**:
   - Shorter timeframes (30s): May need stricter gradient filters
   - Longer timeframes (2m): Can use more relaxed parameters

## Output Files

### Web Dashboard

Results display in interactive table with:
- Color-coded rows (green=caught, red=missed, yellow=partial)
- Sortable columns
- Real-time statistics
- Multiple file comparison

### Python Script CSV Output

Columns include:
- `streak_num`, `direction`, `status`
- `start_bar`, `end_bar`, `length`
- `start_time`, `end_time`
- `start_price`, `end_price`, `net_movement`
- `good_bars`, `bad_bars`, `counter_ratio`
- `avg_gradient`, `gradient_ok`
- `entry_bar`, `missed_points`
- `block_reasons`, `pattern`

## Troubleshooting

### No Streaks Found

- Check if `min_movement` is too high for your instrument
- Verify gradient thresholds are reasonable
- Try widening streak length range
- Use `--verbose` to see what's being filtered out

### Server Won't Start

```bash
# Install dependencies
pip install fastapi uvicorn

# Check if port is in use
netstat -ano | findstr :51888
```

### No Opportunity Files Listed

1. Verify strategy has `EnableOpportunityLog=true`
2. Run strategy in NinjaTrader to generate logs
3. Check logs are in: `Documents\NinjaTrader 8\bin\Custom\strategy_logs\`
4. Files should match: `BarsOnTheFlow_Opportunities_*.csv`

## Examples

### Compare 30s vs 2m Timeframes

```bash
# Analyze 30-second bars
python analyze_opportunity_streaks.py "logs/Opportunities_30s.csv" --output results_30s.csv

# Analyze 2-minute bars  
python analyze_opportunity_streaks.py "logs/Opportunities_2m.csv" --output results_2m.csv

# Compare in Excel/Python pandas
```

### Find Most Profitable Missed Opportunities

Use the web dashboard or run:

```bash
python analyze_opportunity_streaks.py log.csv --verbose | grep "MISSED OPPORTUNITIES"
```

The script automatically shows top 10 missed opportunities sorted by missed points.

## Next Steps

1. **Run strategy on different timeframes** with `EnableOpportunityLog=true`
2. **Analyze logs** using web dashboard or Python script
3. **Identify patterns** in missed opportunities
4. **Adjust strategy parameters** based on findings
5. **Re-run and compare** to validate improvements

## Questions?

- Check console output for errors
- Use `--verbose` flag for detailed information
- Review block reasons to understand why entries were skipped
- Compare catch rates across different parameter settings
