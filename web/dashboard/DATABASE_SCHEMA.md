# Dashboard Database Schema Reference

Complete schema documentation for all SQLite databases used by BarsOnTheFlow strategy and dashboard.

---

## volatility.db

**Location:** `web/dashboard/volatility.db`  
**Used by:** BarsOnTheFlow (`UseVolumeAwareStop` feature)  
**Tables:** 3 (+ sqlite_sequence)

### bar_samples

Individual bar data recorded for volatility analysis. Strategy sends data via `RecordBarSample()`.

| Column | Type | Description |
|--------|------|-------------|
| `id` | INTEGER | Primary key (auto-increment) |
| `timestamp` | TEXT | ISO timestamp of bar |
| `bar_index` | INTEGER | NinjaTrader bar index |
| `symbol` | TEXT | Instrument symbol (e.g., "MNQ 03-25") |
| `hour_of_day` | INTEGER | Hour (0-23) |
| `day_of_week` | INTEGER | Day (0=Sunday, 6=Saturday) |
| `open_price` | REAL | Bar open price |
| `high_price` | REAL | Bar high price |
| `low_price` | REAL | Bar low price |
| `close_price` | REAL | Bar close price |
| `volume` | INTEGER | Bar volume |
| `bar_range` | REAL | High - Low |
| `body_size` | REAL | abs(Close - Open) |
| `upper_wick` | REAL | High - max(Open, Close) |
| `lower_wick` | REAL | min(Open, Close) - Low |
| `range_per_1k_volume` | REAL | bar_range / (volume / 1000) |
| `direction` | TEXT | "up" or "down" |
| `in_trade` | INTEGER | 1 if strategy was in trade |
| `trade_result_ticks` | REAL | Result if trade closed this bar |
| `created_at` | TEXT | Record creation timestamp |

**API:** `POST /api/volatility/record-bar`

---

### volatility_stats

Aggregated hourly volatility statistics computed from bar_samples.

| Column | Type | Description |
|--------|------|-------------|
| `id` | INTEGER | Primary key (auto-increment) |
| `hour_of_day` | INTEGER | Hour (0-23) |
| `day_of_week` | INTEGER | Day (0=Sunday, 6=Saturday) |
| `symbol` | TEXT | Instrument symbol |
| `avg_volume` | REAL | Average bar volume for this hour |
| `min_volume` | INTEGER | Minimum bar volume |
| `max_volume` | INTEGER | Maximum bar volume |
| `volume_stddev` | REAL | Volume standard deviation |
| `avg_bar_range` | REAL | Average bar range (ticks) |
| `min_bar_range` | REAL | Minimum bar range |
| `max_bar_range` | REAL | Maximum bar range |
| `bar_range_stddev` | REAL | Range standard deviation |
| `avg_range_per_1k_volume` | REAL | Normalized volatility metric |
| `sample_count` | INTEGER | Number of bars in sample |
| `first_sample_time` | TEXT | First sample timestamp |
| `last_sample_time` | TEXT | Most recent sample timestamp |
| `last_updated` | TEXT | Stats last recomputed |

**API:** `GET /api/volatility/stats`

---

### stop_loss_recommendations

Pre-computed stop loss recommendations by hour and volume condition.

| Column | Type | Description |
|--------|------|-------------|
| `id` | INTEGER | Primary key (auto-increment) |
| `hour_of_day` | INTEGER | Hour (0-23) |
| `day_of_week` | INTEGER | Day (0=Sunday, 6=Saturday) |
| `symbol` | TEXT | Instrument symbol |
| `recommended_stop_ticks` | INTEGER | Recommended stop in ticks |
| `min_stop_ticks` | INTEGER | Minimum stop (low volatility) |
| `max_stop_ticks` | INTEGER | Maximum stop (high volatility) |
| `avg_bar_range_ticks` | INTEGER | Average bar range for hour |
| `volume_condition` | TEXT | "low", "normal", "high" |
| `confidence_level` | TEXT | "low", "medium", "high" |
| `last_updated` | TEXT | Recommendation last updated |

**API:** `GET /api/volatility/recommended-stop?hour=N&volume=N&symbol=MNQ`

---

## dashboard.db

**Location:** `web/dashboard/dashboard.db`  
**Used by:** BarsOnTheFlow (`EnableDashboardDiagnostics` feature - disabled by default)  
**Tables:** 12 (+ sqlite_sequence)

### diags

Bar-by-bar diagnostic snapshots streamed from strategy.

| Column | Type | Description |
|--------|------|-------------|
| `id` | INTEGER | Primary key (auto-increment) |
| `ts` | REAL | Unix timestamp |
| `barIndex` | INTEGER | NinjaTrader bar index |
| `fastGrad` | REAL | Fast EMA gradient (degrees) |
| `rsi` | REAL | RSI value |
| `adx` | REAL | ADX value |
| `gradStab` | REAL | Gradient stability metric |
| `bandwidth` | REAL | Bandwidth indicator |
| `blockersLong` | TEXT | JSON list of long blockers |
| `blockersShort` | TEXT | JSON list of short blockers |
| `trendSide` | TEXT | "Long", "Short", or "Flat" |
| `volume` | REAL | Bar volume |
| `accel` | REAL | Gradient acceleration |
| `fastEMA` | REAL | Fast EMA value |
| `slowEMA` | REAL | Slow EMA value |
| `close` | REAL | Bar close price |

**API:** `POST /api/diag`

---

### trades

Completed trade records with performance metrics.

| Column | Type | Description |
|--------|------|-------------|
| `id` | INTEGER | Primary key (auto-increment) |
| `entry_time` | REAL | Entry Unix timestamp |
| `entry_bar` | INTEGER | Entry bar index |
| `direction` | TEXT | "Long" or "Short" |
| `entry_price` | REAL | Entry price |
| `exit_time` | REAL | Exit Unix timestamp |
| `exit_bar` | INTEGER | Exit bar index |
| `exit_price` | REAL | Exit price |
| `bars_held` | INTEGER | Number of bars in trade |
| `realized_points` | REAL | Profit/loss in points |
| `mfe` | REAL | Maximum Favorable Excursion |
| `mae` | REAL | Maximum Adverse Excursion |
| `exit_reason` | TEXT | Why trade was closed |
| `pending_used` | INTEGER | 1 if pending signal was used |
| `confirm_delta` | REAL | Confirmation delta value |
| `min_hold_bars` | INTEGER | Minimum hold setting |
| `min_entry_fast_grad` | REAL | Min gradient for entry |
| `validation_min_fast_grad` | REAL | Validation gradient |
| `exit_confirm_fast_ema_delta` | REAL | Exit confirmation delta |
| `fast_exit_thresh_long` | REAL | Fast exit threshold (long) |
| `fast_exit_thresh_short` | REAL | Fast exit threshold (short) |
| `strategy_version` | TEXT | Strategy version |
| `symbol` | TEXT | Instrument symbol |
| `imported_at` | REAL | Import timestamp |

**API:** `GET /api/trades`

---

### entry_blocks

Records of blocked entry attempts (for analysis).

| Column | Type | Description |
|--------|------|-------------|
| `id` | INTEGER | Primary key (auto-increment) |
| `block_time` | REAL | Unix timestamp |
| `block_bar` | INTEGER | Bar index where blocked |
| `fast_grad` | REAL | Gradient at block |
| `adx` | REAL | ADX at block |
| `rsi` | REAL | RSI at block |
| `blockers` | TEXT | JSON list of blocker reasons |
| `trend_side` | TEXT | Intended trade direction |
| `move_after_block` | REAL | Price move after block |
| `move_bars` | INTEGER | Bars tracked after |
| `strategy_version` | TEXT | Strategy version |
| `symbol` | TEXT | Instrument symbol |
| `created_at` | REAL | Record creation time |

**API:** `GET /api/entry-blocks`

---

### entry_cancellations

Cancelled entry records with streak information.

| Column | Type | Description |
|--------|------|-------------|
| `id` | INTEGER | Primary key (auto-increment) |
| `ts` | REAL | Unix timestamp |
| `barIndex` | INTEGER | Bar index |
| `fastGrad` | REAL | Fast gradient |
| `rsi` | REAL | RSI value |
| `adx` | REAL | ADX value |
| `gradStab` | REAL | Gradient stability |
| `bandwidth` | REAL | Bandwidth value |
| `blockersLong` | TEXT | Long blockers JSON |
| `blockersShort` | TEXT | Short blockers JSON |
| `trendSide` | TEXT | Trend direction |
| `effectiveMinGrad` | REAL | Effective min gradient |
| `effectiveRsiFloor` | REAL | Effective RSI floor |
| `weakGradStreak` | INTEGER | Weak gradient streak count |
| `rsiBelowStreak` | INTEGER | RSI below streak count |
| `volume` | REAL | Bar volume |

---

### suggestions

AI-generated parameter tuning suggestions.

| Column | Type | Description |
|--------|------|-------------|
| `id` | INTEGER | Primary key (auto-increment) |
| `ts` | REAL | Unix timestamp |
| `property` | TEXT | Property name to change |
| `recommend` | REAL | Recommended value |
| `reason` | TEXT | Explanation for suggestion |
| `autoTrigger` | INTEGER | 1 if auto-triggered |
| `applied` | INTEGER | 1 if applied |

---

### overrides_history

Log of parameter override changes.

| Column | Type | Description |
|--------|------|-------------|
| `id` | INTEGER | Primary key (auto-increment) |
| `ts` | REAL | Unix timestamp |
| `property` | TEXT | Property that changed |
| `oldValue` | REAL | Previous value |
| `newValue` | REAL | New value |
| `source` | TEXT | Change source (user/auto) |

---

### auto_apply_events

Records of automatically applied parameter changes.

| Column | Type | Description |
|--------|------|-------------|
| `id` | INTEGER | Primary key (auto-increment) |
| `ts` | REAL | Unix timestamp |
| `property` | TEXT | Property changed |
| `streakCount` | INTEGER | Streak that triggered |
| `recommend` | REAL | Applied value |
| `reason` | TEXT | Reason for change |

---

### strategy_snapshots

Point-in-time strategy state snapshots.

| Column | Type | Description |
|--------|------|-------------|
| `id` | INTEGER | Primary key (auto-increment) |
| `ts` | REAL | Unix timestamp |
| `version` | TEXT | Strategy version |
| `overrides_json` | TEXT | JSON of overrides |
| `streaks_json` | TEXT | JSON of streak data |
| `perf_json` | TEXT | JSON of performance |
| `component_hashes_json` | TEXT | JSON of component hashes |
| `notes` | TEXT | Snapshot notes |
| `created_at` | TEXT | Creation timestamp |

---

### ai_footprints

Audit log of AI/automation actions.

| Column | Type | Description |
|--------|------|-------------|
| `id` | INTEGER | Primary key (auto-increment) |
| `ts` | REAL | Unix timestamp |
| `action` | TEXT | Action taken |
| `details` | TEXT | Action details |
| `reasoning` | TEXT | Why action was taken |
| `diff_summary` | TEXT | Changes made |
| `created_at` | TEXT | Record timestamp |

---

### strategy_index

Strategy index metadata.

| Column | Type | Description |
|--------|------|-------------|
| `id` | INTEGER | Primary key (auto-increment) |
| `ts` | REAL | Unix timestamp |
| `version` | TEXT | Strategy version |
| `index_json` | TEXT | JSON index data |
| `created_at` | TEXT | Creation timestamp |

---

### dev_classifications

Development: Bar classification records.

| Column | Type | Description |
|--------|------|-------------|
| `id` | INTEGER | Primary key (auto-increment) |
| `ts` | REAL | Unix timestamp |
| `barIndex` | INTEGER | Bar index |
| `side` | TEXT | Long/Short |
| `fastGrad` | REAL | Fast gradient |
| `accel` | REAL | Acceleration |
| `fastEMA` | REAL | Fast EMA value |
| `close` | REAL | Close price |
| `isBad` | INTEGER | 1 if classified as bad |
| `reason` | TEXT | Classification reason |

---

### dev_metrics

Development: General metrics storage.

| Column | Type | Description |
|--------|------|-------------|
| `id` | INTEGER | Primary key (auto-increment) |
| `ts` | REAL | Unix timestamp |
| `metric` | TEXT | Metric name |
| `value` | REAL | Metric value |
| `details` | TEXT | Additional details |

---

## bars.db

**Location:** `web/dashboard/bars.db`  
**Purpose:** Strategy state + bar OHLC persistence  
**Tables:** 1

### BarsOnTheFlowStateAndBar

Complete strategy state combined with bar OHLC data. Updated every bar when `EnableDashboardDiagnostics = true`.

| Column | Type | Description |
|--------|------|-------------|
| **Identification** | | |
| `id` | INTEGER | Primary key (auto-increment) |
| `timestamp` | TEXT | Strategy timestamp (from state) |
| `receivedTs` | REAL | Server Unix timestamp |
| `strategyName` | TEXT | Strategy name (e.g., "BarsOnTheFlow") |
| `runId` | TEXT | Unique run identifier (UUID or timestamp) |
| **Bar Data** | | |
| `barIndex` | INTEGER | Previous bar index (final OHLC) |
| `barTime` | TEXT | Previous bar timestamp |
| `currentBar` | INTEGER | Current bar being evaluated |
| `open` | REAL | Previous bar open price |
| `high` | REAL | Previous bar high price |
| `low` | REAL | Previous bar low price |
| `close` | REAL | Previous bar close price |
| `volume` | REAL | Previous bar volume |
| `candleType` | TEXT | "good", "bad", or "flat" |
| **Position State** | | |
| `positionMarketPosition` | TEXT | "Long", "Short", or "Flat" |
| `positionQuantity` | INTEGER | Position size |
| `positionAveragePrice` | REAL | Average entry price |
| `intendedPosition` | TEXT | Intended position (for unique entries) |
| `lastEntryBarIndex` | INTEGER | Bar index of last entry |
| `lastEntryDirection` | TEXT | Last entry direction |
| **Stop Loss Config** | | |
| `stopLossPoints` | INTEGER | Fixed stop loss in points |
| `calculatedStopTicks` | INTEGER | Dynamic stop in ticks |
| `calculatedStopPoints` | REAL | Dynamic stop in points |
| `useTrailingStop` | INTEGER | 1 if trailing stop enabled |
| `useDynamicStopLoss` | INTEGER | 1 if dynamic stop enabled |
| `lookback` | INTEGER | Dynamic stop lookback period |
| `multiplier` | REAL | Dynamic stop multiplier |
| **Strategy Parameters** | | |
| `contracts` | INTEGER | Contract size |
| `enableShorts` | INTEGER | 1 if shorts enabled |
| `avoidLongsOnBadCandle` | INTEGER | 1 if avoiding longs on bad candles |
| `avoidShortsOnGoodCandle` | INTEGER | 1 if avoiding shorts on good candles |
| `exitOnTrendBreak` | INTEGER | 1 if exiting on trend break |
| `reverseOnTrendBreak` | INTEGER | 1 if reversing on trend break |
| `fastEmaPeriod` | INTEGER | Fast EMA period |
| `gradientThresholdSkipLongs` | REAL | Min gradient for longs |
| `gradientThresholdSkipShorts` | REAL | Max gradient for shorts |
| `gradientFilterEnabled` | INTEGER | 1 if gradient filter enabled |
| **Trend Parameters** | | |
| `trendLookbackBars` | INTEGER | Trend detection lookback |
| `minConsecutiveBars` | INTEGER | Min consecutive bars for trend |
| `usePnLTiebreaker` | INTEGER | 1 if using PnL tiebreaker |
| **Pending Signals** | | |
| `pendingLongFromBad` | INTEGER | 1 if pending long from bad candle |
| `pendingShortFromGood` | INTEGER | 1 if pending short from good candle |
| **Performance Metrics** | | |
| `unrealizedPnL` | REAL | Current open position P&L (points) |
| `realizedPnL` | REAL | Cumulative closed trades profit (currency) |
| `totalTradesCount` | INTEGER | Total number of trades completed |
| `winningTradesCount` | INTEGER | Number of winning trades |
| `losingTradesCount` | INTEGER | Number of losing trades |
| `winRate` | REAL | Win rate percentage (0-100) |
| **Raw Data** | | |
| `stateJson` | TEXT | Full state JSON for reference |
| `created_at` | TEXT | Record creation timestamp |

**Indexes:**
- `idx_botf_bar` - Fast queries by barIndex
- `idx_botf_ts` - Fast queries by receivedTs (DESC)
- `idx_botf_position` - Filter by position
- `idx_botf_currentbar` - Fast queries by currentBar

**Unique Constraint:**
- `UNIQUE(strategyName, barIndex, runId)` - Prevents duplicate bars within same run, allows multiple runs with different parameters

**API:** 
- POST `/state` (auto-populated with INSERT OR REPLACE)
- GET `/api/bars/state-history?limit=100&strategy=BarsOnTheFlow` - Query historical data
- GET `/api/bars/gaps?strategy=BarsOnTheFlow` - Identify missing bars in sequence

**Persistence Guarantees:**
1. **No Duplicates:** UNIQUE constraint prevents same bar from being logged twice **within same run**
2. **Multi-Run Support:** Different `runId` values allow storing multiple parameter test runs on same historical data
3. **Idempotent Updates:** INSERT OR REPLACE updates bar if already exists in same run (safe across restarts)
4. **Gap Detection:** `/api/bars/gaps` endpoint identifies missing bars in sequence
5. **Session Continuity:** Data persists across strategy restarts - no data loss
6. **Parameter Testing:** Historical runs with different parameters are preserved separately

**Gap Handling:**
- If strategy stops and restarts, existing bars won't be re-inserted
- Missing bars (gaps) can be identified via `/api/bars/gaps` endpoint
- Strategy must send historical bars on startup to backfill gaps (not automatic)
- Or query NinjaTrader for missing bar data and POST manually to `/state`

**Use Cases:**
- Historical strategy state analysis
- Backtest state reconstruction
- Parameter optimization comparison (multiple runs with different parameters)
- Real-time PnL tracking and performance analysis
- Win rate and trade quality assessment per parameter configuration
- Compare PnL curves across different runId values (same bars, different parameters)
- Parameter change tracking over time
- Position state correlation with market conditions
- Bar-by-bar trade decision replay with P&L context
- Session continuity across strategy restarts
- A/B testing different strategy configurations on same historical data

---

*Last Updated: December 21, 2025*
