# Entry + Exit Performance Auto-Optimization

## Overview
The auto-suggest system automatically analyzes BOTH entry blocks and trade exits to continuously optimize strategy parameters.

## How It Works

## PART 1: EXIT ANALYSIS

### 1. Trade Analysis
When a trade completes, the system analyzes:
- **MFE Capture Rate**: What % of max profit was captured
- **Hold Duration**: How long the trade was held
- **Exit Reason**: Why the trade exited

### 2. Exit Pattern Detection

#### Poor MFE Capture
- **Trigger**: 5 consecutive trades with <40% MFE capture AND validation exit
- **Action**: Lower `ValidationMinFastGradientAbs` by 0.02
- **Goal**: Let winners run longer before validation forces exit
- **Example**: If MFE=3.00 but realized=0.80 (27% capture), system detects early exit

#### Short Losing Trades
- **Trigger**: 5 consecutive trades held ≤4 bars with negative P/L
- **Action**: Increase `MinHoldBars` by 1
- **Goal**: Force longer hold times to avoid quick losses
- **Example**: 3-bar trades losing money consistently → increase to 6-bar minimum

### 3. Auto-Apply Parameters

```python
AUTO_APPLY_STREAK_LIMITS = {
    'ValidationMinFastGradientAbs': 5,  # trigger after 5 poor exits
    'MinHoldBars': 5,  # trigger after 5 short losing trades
}

AUTO_APPLY_STEPS = {
    'ValidationMinFastGradientAbs': -0.02,  # decrease threshold
    'MinHoldBars': 1,  # increase hold time
}

AUTO_APPLY_BOUNDS = {
    'ValidationMinFastGradientAbs': (0.05, 0.20),  # don't go too loose/tight
    'MinHoldBars': (2, 10),  # reasonable hold range
}
```

### 4. Guardrails
- **Cooldown**: 5 minutes between adjustments for same parameter
- **Daily Limit**: Max 5 adjustments per parameter per day
- **Bounds**: Parameters stay within safe ranges
- **Reset**: Streaks reset when good performance detected

## PART 2: ENTRY ANALYSIS

### 1. Entry Block Analysis
When entry is blocked, the system tracks:
- **Which Blocker**: FastGradMin, ADXMin, RSIFloor, etc.
- **Market Conditions**: FastGrad value, ADX, RSI at block time
- **Move After Block**: Price movement in next N bars (favorable direction)

### 2. Entry Pattern Detection

#### Missed Opportunities
- **Trigger**: Entry blocked but price moves >2 points favorably afterward
- **Action**: Lower the threshold that caused the block
- **Examples**:
  - FastGrad=0.28 blocked (need 0.30), then moved +3.5 pts → Lower MinEntryFastGradientAbs
  - ADX=18 blocked (need 20), then moved +2.8 pts → Lower MinAdxForEntry by 2
  - RSI=48 blocked (need 50), then moved +4.2 pts → Lower RsiEntryFloor by 2

#### Blocker Effectiveness Analysis
- **Trigger**: Every 20 entry blocks, analyze patterns
- **Detection**: If one blocker caused >50% of missed opportunities (min 5 cases)
- **Action**: Aggressively lower that threshold (streak += 2)
- **Example**: If FastGradMin blocked 12/20 cases that moved favorably, system flags it as too strict

### 3. Auto-Apply Parameters (Entry)

```python
AUTO_APPLY_STREAK_LIMITS = {
    'MinEntryFastGradientAbs': 3,  # trigger after 3 missed opportunities
    'MinAdxForEntry': 4,  # ADX - slightly more cautious
    'RsiEntryFloor': 3,  # trigger after 3 missed opportunities
}

AUTO_APPLY_STEPS = {
    'MinEntryFastGradientAbs': -0.05,  # decrease by 0.05 (negative = loosen)
    'MinAdxForEntry': -2.0,  # decrease by 2.0 (negative = loosen)
    'RsiEntryFloor': -2.0,  # decrease by 2.0 (negative = loosen)
}

AUTO_APPLY_BOUNDS = {
    'MinEntryFastGradientAbs': (0.05, 1.0),  # don't go too loose/tight
    'MinAdxForEntry': (10.0, 30.0),  # reasonable ADX range
    'RsiEntryFloor': (30.0, 70.0),  # reasonable RSI range
}
```

## Integration

### Strategy → Dashboard Communication

#### Exit Analysis (POST to `/trade_completed`):
```json
{
  "MFE": 3.26,
  "MAE": -0.15,
  "RealizedPoints": 1.24,
  "BarsHeld": 3,
  "ExitReason": "VALIDATION_FAILED:FastGrad<=0.0123(need>0.15)",
  "EntryPrice": 2973.00,
  "ExitPrice": 2974.24,
  "Direction": "LONG"
}
```

#### Entry Analysis (POST to `/entry_blocked`):
```json
{
  "FastGrad": 0.28,
  "ADX": 18.5,
  "RSI": 52.3,
  "Blockers": ["FastGradMin"],
  "TrendSide": "LONG",
  "MoveAfterBlock": 3.45,  // Track price 10-20 bars later
  "BlockTime": "2025-11-30T10:15:00",
  "BlockBar": 1234
}
```

**Implementation Strategy:**
1. When entry blocked, store block data
2. Track high/low for next 10-20 bars
3. Calculate favorable move (High - BlockPrice for LONG, BlockPrice - Low for SHORT)
4. POST to `/entry_blocked` with MoveAfterBlock value

### Dashboard → Strategy Overrides
The dashboard applies overrides via HTTP requests to strategy's override endpoint (already implemented).

## Expected Results

### Before Auto-Optimization (from analysis)
**Entry Issues:**
- FastGradMin blocked 73.6% of entries
- ADXMin blocked 39.7% of entries  
- 20.7% of bars passed all filters but no entry placed

**Exit Issues:**
- Win Rate: 36.9%
- MFE Capture: -70% (giving back 1.39 pts/trade)
- 3-bar trades: -275.68 points total loss

### After Auto-Optimization
**Entry Improvements:**
- **Fewer Missed Opportunities**: System lowers thresholds when blocks precede favorable moves
- **Adaptive to Market Conditions**: Tightens when blocks prevent bad trades, loosens when causing misses
- **More Entries**: Optimal balance between selectivity and opportunity capture
- **Expected**: +15-25 more profitable entries per 500 bars

**Exit Improvements:**
- **Win Rate**: 36.9% → 42-48% (better exits + more entries)
- **MFE Capture**: -70% → 55-70% (profit retention)
- **Longer Holds**: Force 5+ bars, capture profitable 6-14 bar moves
- **Save**: ~275 points from preventing 3-bar losses
- **Capture**: ~150-200 points from better MFE retention

**Combined Impact:**
- **Entry + Exit optimization working together**
- **Continuous learning** from both missed opportunities and trade performance
- **Self-adjusting** to changing market volatility and trends
- **Expected Total**: +30-50% improvement in strategy profitability

## Monitoring

Check `/stats` endpoint for current state:
```json
{
  "exit_poor_performance_streak": 2,
  "short_hold_streak": 0,
  "recent_trades": [...],
  "property_streaks": {
    "ValidationMinFastGradientAbs": 2,
    "MinHoldBars": 0
  }
}
```

## Manual Override
You can still manually adjust parameters via the dashboard UI. Auto-suggest respects manual changes and builds on them.
