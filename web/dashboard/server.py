import os
import time
import json
import sqlite3
import hashlib
from datetime import datetime
from typing import List, Dict, Any
from collections import deque
from fastapi import FastAPI, Request
from fastapi.responses import HTMLResponse, JSONResponse
from fastapi.staticfiles import StaticFiles

app = FastAPI()

# --- SQLite configuration with connection pooling ---
USE_SQLITE = True
DB_PATH = os.path.join(os.path.dirname(__file__), 'dashboard.db')
_db_connection = None

def get_db_connection():
    """Get or create persistent database connection with optimizations."""
    global _db_connection
    if _db_connection is None:
        _db_connection = sqlite3.connect(DB_PATH, check_same_thread=False, timeout=10.0)
        # Enable WAL mode for better concurrent access
        _db_connection.execute('PRAGMA journal_mode=WAL')
        # Increase cache size (default 2MB -> 10MB)
        _db_connection.execute('PRAGMA cache_size=-10000')
        # Faster synchronous mode (still safe)
        _db_connection.execute('PRAGMA synchronous=NORMAL')
        print('[DB] Connection pool initialized with WAL mode')
    return _db_connection

def init_db():
    if not USE_SQLITE:
        return
    conn = get_db_connection()
    cur = conn.cursor()
    # Core tables
    cur.execute("""
    CREATE TABLE IF NOT EXISTS diags (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        ts REAL,
        barIndex INTEGER,
        fastGrad REAL,
        rsi REAL,
        adx REAL,
        gradStab REAL,
        bandwidth REAL,
        volume REAL,
        blockersLong TEXT,
        blockersShort TEXT,
        trendSide TEXT
    )
    """)
    cur.execute("""
    CREATE TABLE IF NOT EXISTS overrides_history (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        ts REAL,
        property TEXT,
        oldValue REAL,
        newValue REAL,
        source TEXT
    )
    """)
    cur.execute("""
    CREATE TABLE IF NOT EXISTS auto_apply_events (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        ts REAL,
        property TEXT,
        streakCount INTEGER,
        recommend REAL,
        reason TEXT
    )
    """)
    # Optional suggestions log for analysis
    cur.execute("""
    CREATE TABLE IF NOT EXISTS suggestions (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        ts REAL,
        property TEXT,
        recommend REAL,
        reason TEXT,
        autoTrigger INTEGER,
        applied INTEGER
    )
    """)
    # Entry cancellation tracking for investigation
    cur.execute("""
    CREATE TABLE IF NOT EXISTS entry_cancellations (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        ts REAL,
        barIndex INTEGER,
        fastGrad REAL,
        rsi REAL,
        adx REAL,
        gradStab REAL,
        bandwidth REAL,
        volume REAL,
        blockersLong TEXT,
        blockersShort TEXT,
        trendSide TEXT,
        effectiveMinGrad REAL,
        effectiveRsiFloor REAL,
        weakGradStreak INTEGER,
        rsiBelowStreak INTEGER
    )
    """)
    # Strategy snapshots for session persistence
    cur.execute("""
    CREATE TABLE IF NOT EXISTS strategy_snapshots (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        ts REAL NOT NULL,
        version TEXT,
        overrides_json TEXT,
        streaks_json TEXT,
        perf_json TEXT,
        component_hashes_json TEXT,
        notes TEXT,
        created_at TEXT DEFAULT (datetime('now'))
    )
    """)
    cur.execute("CREATE INDEX IF NOT EXISTS idx_snapshots_ts ON strategy_snapshots(ts DESC)")
    
    # AI decision footprints for reasoning replay
    cur.execute("""
    CREATE TABLE IF NOT EXISTS ai_footprints (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        ts REAL NOT NULL,
        action TEXT NOT NULL,
        details TEXT,
        reasoning TEXT,
        diff_summary TEXT,
        created_at TEXT DEFAULT (datetime('now'))
    )
    """)
    cur.execute("CREATE INDEX IF NOT EXISTS idx_footprints_ts ON ai_footprints(ts DESC)")
    cur.execute("CREATE INDEX IF NOT EXISTS idx_footprints_action ON ai_footprints(action)")

    # Strategy structure index (single-source documentation for AI continuation)
    cur.execute("""
    CREATE TABLE IF NOT EXISTS strategy_index (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        ts REAL NOT NULL,
        version TEXT,
        index_json TEXT,
        created_at TEXT DEFAULT (datetime('now'))
    )
    """)
    cur.execute("CREATE INDEX IF NOT EXISTS idx_strategy_index_ts ON strategy_index(ts DESC)")
    # Development / debugging instrumentation tables
    cur.execute("""
    CREATE TABLE IF NOT EXISTS dev_classifications (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        ts REAL,
        barIndex INTEGER,
        side TEXT,
        fastGrad REAL,
        accel REAL,
        fastEMA REAL,
        close REAL,
        isBad INTEGER,
        reason TEXT
    )
    """)
    cur.execute("CREATE INDEX IF NOT EXISTS idx_dev_classifications_bar ON dev_classifications(barIndex)")
    cur.execute("CREATE INDEX IF NOT EXISTS idx_dev_classifications_ts ON dev_classifications(ts DESC)")
    cur.execute("""
    CREATE TABLE IF NOT EXISTS dev_metrics (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        ts REAL,
        metric TEXT,
        value REAL,
        details TEXT
    )
    """)
    cur.execute("CREATE INDEX IF NOT EXISTS idx_dev_metrics_metric ON dev_metrics(metric)")
    
    conn.commit()
    # Don't close - keep persistent connection

def ensure_db_columns():
    if not USE_SQLITE:
        return
    try:
        conn = get_db_connection()
        cur = conn.cursor()
        # Helper to check column existence
        def has_column(table: str, column: str) -> bool:
            cur.execute(f"PRAGMA table_info({table})")
            return any(row[1] == column for row in cur.fetchall())

        # Add missing 'volume' columns safely
        try:
            if not has_column('diags', 'volume'):
                cur.execute("ALTER TABLE diags ADD COLUMN volume REAL")
        except Exception as ex:
            print('[DB] diags add volume failed:', ex)
        try:
            if not has_column('entry_cancellations', 'volume'):
                cur.execute("ALTER TABLE entry_cancellations ADD COLUMN volume REAL")
        except Exception as ex:
            print('[DB] cancellations add volume failed:', ex)
        # Add diagnostic enrichment columns if missing
        for col, ddl in [
            ('accel','ALTER TABLE diags ADD COLUMN accel REAL'),
            ('fastEMA','ALTER TABLE diags ADD COLUMN fastEMA REAL'),
            ('slowEMA','ALTER TABLE diags ADD COLUMN slowEMA REAL'),
            ('close','ALTER TABLE diags ADD COLUMN close REAL')
        ]:
            try:
                if not has_column('diags', col):
                    cur.execute(ddl)
            except Exception as ex:
                print(f'[DB] diags add {col} failed:', ex)

        conn.commit()
        # Don't close - keep persistent connection
        print('[DB] ensure_db_columns complete')
    except Exception as ex:
        print('[DB] ensure_db_columns error:', ex)

def db_exec(sql: str, params=()):
    if not USE_SQLITE:
        return
    try:
        conn = get_db_connection()
        cur = conn.cursor()
        cur.execute(sql, params)
        conn.commit()
    except sqlite3.OperationalError as e:
        # Handle locked database - retry once
        print(f'[DB] Operational error, retrying: {e}')
        time.sleep(0.1)
        conn = get_db_connection()
        cur = conn.cursor()
        cur.execute(sql, params)
        conn.commit()

def db_query_one(sql: str, params=()):
    if not USE_SQLITE:
        return None
    conn = get_db_connection()
    cur = conn.cursor()
    cur.execute(sql, params)
    row = cur.fetchone()
    return row

init_db()
ensure_db_columns()

# --- Trade performance tracking for exit optimization ---
recent_trades: List[Dict[str, Any]] = []  # stores last N trades for analysis
MAX_RECENT_TRADES = 30
exit_poor_performance_streak: int = 0  # consecutive trades with poor MFE capture
short_hold_streak: int = 0  # consecutive trades held < optimal bars

# --- Entry performance tracking for entry optimization ---
recent_entry_blocks: List[Dict[str, Any]] = []  # stores recent entry blocks for analysis
MAX_ENTRY_BLOCKS = 50
missed_opportunity_streak: int = 0  # consecutive bars with entry blocked but conditions were good

# --- AI Footprint helper ---
def log_ai_footprint(action: str, details: str = '', reasoning: str = '', diff_summary: str = ''):
    """Programmatically log a footprint without needing the REST endpoint."""
    if not USE_SQLITE:
        return
    try:
        ts = time.time()
        db_exec(
            """
            INSERT INTO ai_footprints (ts, action, details, reasoning, diff_summary)
            VALUES (?, ?, ?, ?, ?)
            """,
            (ts, action, details, reasoning, diff_summary)
        )
        print(f"[AI-FOOTPRINT] Logged({action}) at {ts}")
    except Exception as ex:
        print(f"[AI-FOOTPRINT] Log error: {ex}")
blocker_pattern_counts: Dict[str, int] = {}  # tracks which blockers appear most frequently

def analyze_entry_performance(block_data: Dict[str, Any]):
    """Analyze entry block to detect missed opportunities and blocker effectiveness.
    block_data should contain: FastGrad, ADX, RSI, Blockers, TrendSide, MoveAfterBlock (points moved in next N bars)
    """
    global missed_opportunity_streak, blocker_pattern_counts
    
    try:
        blockers = block_data.get('Blockers', [])
        move_after = float(block_data.get('MoveAfterBlock', 0))  # points moved in favorable direction after block
        fast_grad = abs(float(block_data.get('FastGrad', 0)))
        adx = float(block_data.get('ADX', 0))
        
        # Store entry block
        recent_entry_blocks.append(block_data)
        if len(recent_entry_blocks) > MAX_ENTRY_BLOCKS:
            recent_entry_blocks.pop(0)
        
        # Count blocker patterns
        for blocker in blockers:
            blocker_pattern_counts[blocker] = blocker_pattern_counts.get(blocker, 0) + 1
        
        # Detect missed opportunity: Strong move after block (>2 points favorable)
        if move_after > 2.0:
            missed_opportunity_streak += 1
            
            # Analyze which blocker caused the miss
            if 'FastGradMin' in blockers:
                property_streaks['MinEntryFastGradientAbs'] += 1
                print(f"[ENTRY-ANALYSIS] Missed opportunity: FastGrad={fast_grad:.3f} blocked, then moved {move_after:.2f} pts - Streak: {missed_opportunity_streak}")
            elif 'ADXMin' in blockers:
                property_streaks['MinAdxForEntry'] = property_streaks.get('MinAdxForEntry', 0) + 1
                print(f"[ENTRY-ANALYSIS] Missed opportunity: ADX={adx:.1f} blocked, then moved {move_after:.2f} pts")
            elif 'RSIFloor' in blockers:
                property_streaks['RsiEntryFloor'] += 1
                print(f"[ENTRY-ANALYSIS] Missed opportunity: RSI blocked, then moved {move_after:.2f} pts")
        else:
            # Good block - prevented bad entry
            missed_opportunity_streak = 0
            
        # Analyze blocker effectiveness from recent history
        if len(recent_entry_blocks) >= 20:
            analyze_blocker_effectiveness()
            
    except Exception as ex:
        print(f"[ENTRY-ANALYSIS] Error: {ex}")

def analyze_blocker_effectiveness():
    """Analyze last 20 entry blocks to see if specific blockers are preventing good opportunities."""
    try:
        # Count blockers that preceded profitable moves
        blocker_missed_opps = {}
        total_blocks = 0
        
        for block in recent_entry_blocks[-20:]:
            move = float(block.get('MoveAfterBlock', 0))
            if move > 2.0:  # Would have been profitable
                total_blocks += 1
                for blocker in block.get('Blockers', []):
                    blocker_missed_opps[blocker] = blocker_missed_opps.get(blocker, 0) + 1
        
        # If >50% of blocks from one blocker missed opportunities, flag it
        for blocker, count in blocker_missed_opps.items():
            miss_rate = count / max(total_blocks, 1)
            if miss_rate > 0.5 and count >= 5:
                print(f"[ENTRY-EFFECTIVENESS] {blocker} caused {count}/{total_blocks} missed opps ({miss_rate:.0%}) - Consider loosening threshold")
                
                # Map blocker to property and increase streak aggressively
                if 'FastGrad' in blocker:
                    property_streaks['MinEntryFastGradientAbs'] = min(property_streaks.get('MinEntryFastGradientAbs', 0) + 2, 10)
                elif 'ADX' in blocker:
                    property_streaks['MinAdxForEntry'] = min(property_streaks.get('MinAdxForEntry', 0) + 2, 10)
                elif 'RSI' in blocker:
                    property_streaks['RsiEntryFloor'] = min(property_streaks.get('RsiEntryFloor', 0) + 2, 10)
                    
    except Exception as ex:
        print(f"[ENTRY-EFFECTIVENESS] Analysis error: {ex}")

def analyze_trade_performance(trade_data: Dict[str, Any]):
    """Analyze completed trade and update exit parameter streaks.
    trade_data should contain: MFE, RealizedPoints, BarsHeld, ExitReason
    """
    global exit_poor_performance_streak, short_hold_streak
    
    try:
        mfe = float(trade_data.get('MFE', 0))
        realized = float(trade_data.get('RealizedPoints', 0))
        bars_held = int(trade_data.get('BarsHeld', 0))
        exit_reason = trade_data.get('ExitReason', '')
        
        # Store trade
        recent_trades.append(trade_data)
        if len(recent_trades) > MAX_RECENT_TRADES:
            recent_trades.pop(0)
        
        # Analyze MFE capture rate (if trade had profit potential)
        if mfe > 1.5:  # only analyze if MFE exceeded minimum threshold
            capture_rate = realized / mfe if mfe > 0 else 0
            
            # Poor capture: exited with <40% of max profit reached
            if capture_rate < 0.40 and 'VALIDATION_FAILED' in exit_reason:
                exit_poor_performance_streak += 1
                property_streaks['ValidationMinFastGradientAbs'] = exit_poor_performance_streak
                print(f"[EXIT-ANALYSIS] Poor MFE capture: {capture_rate:.1%} (MFE={mfe:.2f}, Realized={realized:.2f}) - Streak: {exit_poor_performance_streak}")
            else:
                exit_poor_performance_streak = 0
                property_streaks['ValidationMinFastGradientAbs'] = 0
        
        # Analyze hold duration (trades exiting at 3-4 bars typically lose money)
        if bars_held <= 4 and realized < 0:
            short_hold_streak += 1
            property_streaks['MinHoldBars'] = short_hold_streak
            print(f"[EXIT-ANALYSIS] Short losing trade: {bars_held} bars, P/L={realized:.2f} - Streak: {short_hold_streak}")
        elif bars_held >= 6:  # good hold duration
            short_hold_streak = 0
            property_streaks['MinHoldBars'] = 0
            
    except Exception as ex:
        print(f"[EXIT-ANALYSIS] Error: {ex}")

# --- Auto-apply evaluator ---
def evaluate_auto_apply(now_ts: float):
    """Check streak counters and apply overrides when criteria satisfied.
    Guardrails: cooldown, daily limit, bounds.
    Records events in DB and memory cache.
    """
    for prop, streak in property_streaks.items():
        limit = AUTO_APPLY_STREAK_LIMITS.get(prop)
        if not limit or streak < limit:
            continue
        # Cooldown check
        last_ts = last_auto_apply_ts.get(prop, 0)
        if now_ts - last_ts < AUTO_APPLY_COOLDOWN_SEC:
            continue
        # Daily limit check
        day_key = datetime.now().strftime('%Y-%m-%d')
        dc_key = f"{prop}:{day_key}"
        count_today = daily_counts.get(dc_key, 0)
        if count_today >= AUTO_APPLY_DAILY_LIMIT:
            continue
        # Current effective value
        cur_val = float(active_overrides.get(prop, DEFAULT_PARAMS.get(prop)))
        step = AUTO_APPLY_STEPS.get(prop, 0.0)
        if step == 0:
            continue
        low, high = AUTO_APPLY_BOUNDS.get(prop, (None, None))
        # Compute new value (step can be positive for increase, negative for decrease)
        if step > 0:
            new_val = cur_val + step  # increase (e.g., MinHoldBars)
        else:
            new_val = cur_val + step  # decrease (step is negative, e.g., ValidationMinFastGradientAbs)
        # Apply bounds
        if low is not None:
            new_val = max(low, new_val)
        if high is not None:
            new_val = min(high, new_val)
        if new_val == cur_val:
            continue
        # Apply override (source=auto)
        active_overrides[prop] = new_val
        last_auto_apply_ts[prop] = now_ts
        daily_counts[dc_key] = count_today + 1
        event = {
            'ts': now_ts,
            'property': prop,
            'oldValue': cur_val,
            'newValue': new_val,
            'streakCount': streak,
            'reason': f'streak {streak} >= {limit}'
        }
        auto_apply_events_cache.append(event)
        # Trim cache
        if len(auto_apply_events_cache) > 50:
            del auto_apply_events_cache[:-50]
        print(f"[AUTO] Applied {prop}: {cur_val} -> {new_val} (streak={streak})")
        # Persist history & event
        try:
            if USE_SQLITE:
                db_exec(
                    "INSERT INTO overrides_history (ts, property, oldValue, newValue, source) VALUES (?,?,?,?,?)",
                    (now_ts, prop, cur_val, new_val, 'auto')
                )
                db_exec(
                    "INSERT INTO auto_apply_events (ts, property, streakCount, recommend, reason) VALUES (?,?,?,?,?)",
                    (now_ts, prop, streak, new_val, event['reason'])
                )
        except Exception as db_ex:
            print('[DB] auto apply insert failed:', db_ex)
        # Reset streak after apply to avoid immediate repeated application
        property_streaks[prop] = 0


# In-memory store (simple ring buffer)
diags: List[Dict[str, Any]] = []
MAX_DIAGS = 5000

# --- Lightweight in-memory recent bar cache (normalized subset for fast queries) ---
# Stores only the most recent BAR_CACHE_MAX normalized bar diagnostic entries.
# Normalization flattens C# payload variants (FastGrad/fastGrad etc.) into consistent keys.
BAR_CACHE_MAX = 400  # adjustable; small to keep lookup O(n) trivial
bar_cache: deque[Dict[str, Any]] = deque(maxlen=BAR_CACHE_MAX)

def _normalize_bar(p: Dict[str, Any]) -> Dict[str, Any]:
    """Extract a normalized minimal bar record from a raw diag payload."""
    try:
        return {
            'barIndex': p.get('barIndex') or p.get('BarIndex'),
            'ts': p.get('receivedTs'),
            'localTime': p.get('localTime') or p.get('time'),
            'fastGrad': float(p.get('FastGrad') or p.get('fastGrad') or 0.0),
            'slowGrad': float(p.get('SlowGrad') or p.get('slowGrad') or 0.0),
            'accel': float(p.get('Accel') or p.get('accel') or 0.0),
            'adx': float(p.get('ADX') or p.get('adx') or 0.0),
            'rsi': float(p.get('RSI') or p.get('rsi') or 0.0),
            'fastEMA': float(p.get('FastEMA') or p.get('fastEMA') or 0.0),
            'slowEMA': float(p.get('SlowEMA') or p.get('slowEMA') or 0.0),
            'close': float(p.get('Close') or p.get('close') or 0.0),
            'gradStab': float(p.get('GradStab') or p.get('gradStab') or 0.0),
            'bandwidth': float(p.get('Bandwidth') or p.get('bandwidth') or 0.0),
            'unrealized': float(p.get('Unrealized') or p.get('unrealized') or 0.0),
            'trendSide': p.get('trendSide') or p.get('TrendSide') or None,
        }
    except Exception:
        # Fallback with minimal keys if casting fails
        return {
            'barIndex': p.get('barIndex') or p.get('BarIndex'),
            'ts': p.get('receivedTs'),
            'fastGrad': p.get('FastGrad') or p.get('fastGrad'),
            'slowGrad': p.get('SlowGrad') or p.get('slowGrad'),
        }


# Minimum consecutive bars required to confirm a trend flip
MIN_CONSEC_FOR_TREND_FLIP = 2  # adjust as needed

# Pending flip state (counts opposite bars until confirmation)
pending_flip_side: str | None = None
pending_flip_count: int = 0

# Simple trend tracker
current_trend: Dict[str, Any] = {
    'dir': None,           # 'BULL' or 'BEAR'
    'start_time': None,
    'start_bar': None,
    'good_candles': 0,
    'bad_candles': 0,
    'bars': 0,
    'pnl_proxy': 0.0,      # proxy sum of fastGrad * sign(dir)
    'caught': False,       # strategy sign aligns with dir
    'last_close': None,
}
trend_segments: List[Dict[str, Any]] = []
trend_inputs_missing_count: int = 0
trend_goodbad_updates: int = 0
weak_grad_consec: int = 0  # consecutive bars with fastGrad below effective min
WEAK_GRAD_SUGGEST_THRESHOLD = 3  # trigger autosuggest after this many consecutive weak bars
rsi_below_consec: int = 0  # consecutive bars with RSI below floor
RSI_BELOW_SUGGEST_THRESHOLD = 3  # bars before suggesting lowering RSI floor

# --- Auto-apply config & state ---
AUTO_APPLY_ENABLED: bool = True
AUTO_APPLY_STREAK_LIMITS = {
    'MinEntryFastGradientAbs': 3,
    'MinAdxForEntry': 4,  # ADX blocks - slightly more cautious
    'RsiEntryFloor': 3,
    'ValidationMinFastGradientAbs': 5,  # exit threshold - more cautious
    'MinHoldBars': 5,  # minimum hold time
}
AUTO_APPLY_STEPS = {
    'MinEntryFastGradientAbs': -0.05,  # decrease threshold (negative)
    'MinAdxForEntry': -2.0,  # decrease ADX requirement (negative)
    'RsiEntryFloor': -2.0,  # decrease RSI floor (negative)
    'ValidationMinFastGradientAbs': -0.02,  # decrease to let winners run (negative)
    'MinHoldBars': 1,  # increase to force longer holds (positive)
}
AUTO_APPLY_BOUNDS = {
    'MinEntryFastGradientAbs': (0.05, 1.0),
    'MinAdxForEntry': (10.0, 30.0),  # ADX bounds
    'RsiEntryFloor': (30.0, 70.0),
    'ValidationMinFastGradientAbs': (0.05, 0.20),  # exit threshold bounds
    'MinHoldBars': (2, 10),  # hold time bounds
}
AUTO_APPLY_COOLDOWN_SEC = 300  # min seconds between auto applies for same property
AUTO_APPLY_DAILY_LIMIT = 5     # per property per calendar day
property_streaks: Dict[str,int] = { 'MinEntryFastGradientAbs': 0, 'MinAdxForEntry': 0, 'RsiEntryFloor': 0, 'ValidationMinFastGradientAbs': 0, 'MinHoldBars': 0 }
last_auto_apply_ts: Dict[str,float] = {}
daily_counts: Dict[str,int] = {}
auto_apply_events_cache: List[Dict[str,Any]] = []  # recent events for UI (not a source of truth; DB holds full)

# Active parameter overrides applied from suggestions
active_overrides: Dict[str, Any] = {}
OVERRIDES_FILE = os.path.join(os.path.dirname(__file__), 'overrides_state.json')

def load_overrides_from_disk():
    global active_overrides
    if not os.path.exists(OVERRIDES_FILE):
        return
    try:
        with open(OVERRIDES_FILE, 'r', encoding='utf-8') as f:
            data = json.load(f)
            if isinstance(data, dict):
                # Only accept numeric or string convertible values
                cleaned = {}
                for k, v in data.items():
                    try:
                        # Keep as-is if numeric; attempt float conversion otherwise
                        if isinstance(v, (int, float)):
                            cleaned[k] = v
                        else:
                            cleaned[k] = float(v)
                    except Exception:
                        # Skip invalid entries
                        continue
                active_overrides = cleaned
                print(f"[OVERRIDES] Loaded {len(active_overrides)} overrides from disk")
    except Exception as ex:
        print(f"[OVERRIDES] Load failed: {ex}")

def save_overrides_to_disk():
    tmp_path = OVERRIDES_FILE + '.tmp'
    try:
        with open(tmp_path, 'w', encoding='utf-8') as f:
            json.dump(active_overrides, f, ensure_ascii=False, indent=2)
        # Atomic replace
        if os.path.exists(OVERRIDES_FILE):
            try:
                os.replace(tmp_path, OVERRIDES_FILE)
            except Exception:
                # Fallback: remove and rename
                os.remove(OVERRIDES_FILE)
                os.replace(tmp_path, OVERRIDES_FILE)
        else:
            os.replace(tmp_path, OVERRIDES_FILE)
        print(f"[OVERRIDES] Saved {len(active_overrides)} overrides to disk")
    except Exception as ex:
        print(f"[OVERRIDES] Save failed: {ex}")
        try:
            if os.path.exists(tmp_path):
                os.remove(tmp_path)
        except Exception:
            pass

# Initial load at import time
load_overrides_from_disk()

# Baseline default parameters used for suggestion logic (tunable)
DEFAULT_PARAMS = {
    'MinAdxForEntry': 16.0,  # updated from analysis showing ADX blocking good entries
    'MaxGradientStabilityForEntry': 2.0,
    'MinEntryFastGradientAbs': 0.30,
    'MaxBandwidthForEntry': 0.120,
    # Adaptive entry controls
    'AdaptiveMinFloor': 0.30,
    'AdaptiveNearZeroMultiplier': 0.85,
    # RSI entry floor (strategy currently blocks SHORT when RSI < 50)
    'RsiEntryFloor': 50.0,
    # Exit controls (NEW - for auto-suggest optimization)
    'ValidationMinFastGradientAbs': 0.09,  # gradient threshold to stay in position
    'MinHoldBars': 5,  # minimum bars before allowing exits
    'MFETrailingStopPercent': 0.40,  # exit if profit < 40% of peak MFE
}

# Mount static
static_dir = os.path.join(os.path.dirname(__file__), 'static')
app.mount('/static', StaticFiles(directory=static_dir), name='static')

@app.get('/', response_class=HTMLResponse)
def index():
    # Read index.html on each request so HTML/CSS updates without server restart
    index_path = os.path.join(static_dir, 'index.html')
    try:
        with open(index_path, 'r', encoding='utf-8') as f:
            return f.read()
    except Exception:
        return '<h1>Dashboard</h1>'

@app.get('/favicon.ico')
def favicon():
    # Silence browser favicon requests; you can add a real icon in /static later
    return HTMLResponse(status_code=204)
    
@app.get('/health')
def health():
    return JSONResponse({
        'status': 'ok',
        'diags_count': len(diags),
        'current_trend': { k: current_trend.get(k) for k in ['dir','bars','good_candles','bad_candles','pnl_proxy','caught'] }
    })

@app.get('/diags')
def get_diags(since: float = 0.0):
    """Return recent diagnostics since a given timestamp."""
    out = []
    # Use last 500 to bound work
    search_list = diags[-500:] if len(diags) > 500 else diags
    for d in search_list:
        ts = d.get('receivedTs') or d.get('receivedAt') or 0
        if ts > since:
            out.append({
                'receivedAt': ts,
                'time': d.get('localTime') or d.get('time'),
                'barIndex': d.get('barIndex') or d.get('BarIndex'),
                'fastGrad': d.get('fastGrad') if d.get('fastGrad') is not None else d.get('FastGrad'),
                'slowGrad': d.get('slowGrad') if d.get('slowGrad') is not None else d.get('SlowGrad'),
                'accel': d.get('accel') if d.get('accel') is not None else d.get('Accel'),
                'adx': d.get('adx') if d.get('adx') is not None else d.get('ADX'),
                'fastEMA': d.get('fastEMA') if d.get('fastEMA') is not None else d.get('FastEMA'),
                'slowEMA': d.get('slowEMA') if d.get('slowEMA') is not None else d.get('SlowEMA'),
                'close': d.get('close') if d.get('close') is not None else d.get('Close'),
                'atr': d.get('atr') if d.get('atr') is not None else d.get('ATR'),
                'rsi': d.get('rsi') if d.get('rsi') is not None else d.get('RSI'),
                'signal': d.get('signal') if d.get('signal') is not None else d.get('Signal'),
                'blockersLong': d.get('blockersLong') or [],
                'blockersShort': d.get('blockersShort') or [],
                'gradStab': d.get('gradStab') or d.get('GradStab'),
                'bandwidth': d.get('bandwidth') or d.get('Bandwidth'),
            })
    # Debug: log when frontend polls with stale timestamp
    if since > 0 and len(out) == 0 and len(search_list) > 0:
        latest_ts = search_list[-1].get('receivedTs', 0)
        print(f"[DEBUG] /diags poll: since={since:.2f} latest={latest_ts:.2f} diff={latest_ts-since:.2f}s total_diags={len(diags)}")
    return JSONResponse(out)

@app.post('/diag')
async def receive_diag(request: Request):
    # Accept either a single dict or a list of dicts (batched)
    try:
        payload = await request.json()
    except Exception:
        text = await request.body()
        try:
            payload = json.loads(text)
        except Exception:
            return JSONResponse({"error": "invalid_json"}, status_code=400)

    items = payload if isinstance(payload, list) else [payload]
    for p in items:
        try:
            # Enrich
            p["receivedTs"] = time.time()
            p["localTime"] = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
            bar_index = p.get("BarIndex") or p.get("barIndex")
            if bar_index is not None:
                try:
                    p["barIndex"] = int(bar_index)
                except Exception:
                    p["barIndex"] = bar_index

            diags.append(p)
            if len(diags) > MAX_DIAGS:
                del diags[:len(diags) - MAX_DIAGS]

            # Update recent bar cache with normalized record
            try:
                bar_cache.append(_normalize_bar(p))
            except Exception as _ex_bc:
                print('[BARCACHE] append error:', _ex_bc)

            # Log every 50 and show keys to verify what's being sent
            if len(diags) % 50 == 0:
                keys = sorted(p.keys())
                print(f"[SERVER] Received diags: {len(diags)} last barIndex={p.get('barIndex')} time={p.get('localTime')}")
                print(f"[SERVER] Keys in payload: {keys}")

            # Trend update
            fast_grad = p.get("FastGrad") or p.get("fastGrad") or 0.0
            side = "BULL" if float(fast_grad) >= 0 else "BEAR"
            now_ts = time.time()
            global current_trend, trend_segments, pending_flip_side, pending_flip_count
            # Add trendSide enrichment to last cached bar if matching
            try:
                if bar_cache and bar_cache[-1].get('barIndex') == p.get('barIndex'):
                    bar_cache[-1]['trendSide'] = side
            except Exception:
                pass
            if (current_trend is None) or (current_trend.get("side") is None):
                current_trend = {
                    "side": side,
                    "startTs": now_ts,
                    "startLocal": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
                    "startBarIndex": p.get("barIndex"),
                    "count": 0,
                    "good": 0,
                    "bad": 0,
                    "pnlProxy": 0.0,
                }
            elif current_trend.get("side") != side:
                # Handle potential trend flip with confirmation logic
                if pending_flip_side == side:
                    pending_flip_count += 1
                else:
                    pending_flip_side = side
                    pending_flip_count = 1

                # Confirm flip only after required consecutive bars
                if pending_flip_count >= MIN_CONSEC_FOR_TREND_FLIP:
                    current_trend["endTs"] = now_ts
                    current_trend["endLocal"] = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
                    current_trend["endBarIndex"] = p.get("barIndex")
                    trend_segments.append(current_trend)
                    current_trend = {
                        "side": side,
                        "startTs": now_ts,
                        "startLocal": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
                        "startBarIndex": p.get("barIndex"),
                        "count": 0,
                        "good": 0,
                        "bad": 0,
                        "pnlProxy": 0.0,
                    }
                    # reset pending flip state
                    pending_flip_side = None
                    pending_flip_count = 0
            else:
                # Same side continues, reset any pending opposite flip
                pending_flip_side = None
                pending_flip_count = 0

            current_trend["count"] += 1
            close = p.get("Close") if (p.get("Close") is not None) else p.get("close")
            fast_ema = p.get("FastEMA") if (p.get("FastEMA") is not None) else p.get("fastEMA")
            fast_grad_val = p.get("FastGrad") if (p.get("FastGrad") is not None) else p.get("fastGrad")
            accel_val = p.get("Accel") if (p.get("Accel") is not None) else p.get("accel")
            try:
                global trend_inputs_missing_count, trend_goodbad_updates
                if (close is not None) and (fast_ema is not None):
                    close_f = float(close)
                    fast_f = float(fast_ema)
                    fg = float(fast_grad_val) if fast_grad_val is not None else 0.0
                    accel = float(accel_val) if accel_val is not None else 0.0
                    
                    # Improved bad bar detection: check momentum direction vs trend direction
                    # BULL trend: bad if fastGrad turns negative (counter-trend momentum)
                    # BEAR trend: bad if fastGrad turns positive (counter-trend momentum)
                    # Also consider strong deceleration as bad (accel opposing gradient)
                    if side == "BULL":
                        # Good: fastGrad positive and price above EMA
                        # Bad: fastGrad negative OR strong deceleration (accel < -0.01 and fg < 0.02)
                        is_bad = (fg < 0) or (accel < -0.01 and fg < 0.02)
                        reason = []
                        if fg < 0: reason.append("fastGrad<0 counter-trend")
                        if (accel < -0.01 and fg < 0.02): reason.append("deceleration")
                        current_trend["good"] += 0 if is_bad else 1
                        current_trend["bad"] += 1 if is_bad else 0
                        current_trend["pnlProxy"] += (close_f - fast_f)
                    else:  # BEAR
                        # Good: fastGrad negative and price below EMA
                        # Bad: fastGrad positive OR strong acceleration (accel > 0.01 and fg > -0.02)
                        is_bad = (fg > 0) or (accel > 0.01 and fg > -0.02)
                        reason = []
                        if fg > 0: reason.append("fastGrad>0 counter-trend")
                        if (accel > 0.01 and fg > -0.02): reason.append("acceleration")
                        current_trend["good"] += 0 if is_bad else 1
                        current_trend["bad"] += 1 if is_bad else 0
                        current_trend["pnlProxy"] += (fast_f - close_f)
                    # Mirror counts to legacy keys for UI consistency
                    current_trend["good_candles"] = current_trend.get("good", 0)
                    current_trend["bad_candles"] = current_trend.get("bad", 0)
                    # Persist classification for debugging
                    try:
                        if USE_SQLITE:
                            db_exec(
                                "INSERT INTO dev_classifications (ts, barIndex, side, fastGrad, accel, fastEMA, close, isBad, reason) VALUES (?,?,?,?,?,?,?,?,?)",
                                (
                                    p.get('receivedTs'),
                                    p.get('barIndex'),
                                    side,
                                    fg,
                                    accel,
                                    fast_f,
                                    close_f,
                                    1 if is_bad else 0,
                                    ";".join(reason) if reason else ("good" if not is_bad else "bad")
                                )
                            )
                    except Exception as ex_cls:
                        print('[DB] dev_classifications insert failed:', ex_cls)
                    trend_goodbad_updates += 1
                else:
                    trend_inputs_missing_count += 1
            except Exception:
                trend_inputs_missing_count += 1
            # --- Persist to SQLite (basic subset) ---
            try:
                if USE_SQLITE:
                    blk_long = p.get('blockersLong') or []
                    blk_short = p.get('blockersShort') or []
                    accel_val = p.get('Accel') if (p.get('Accel') is not None) else p.get('accel')
                    fast_ema_val = p.get('FastEMA') if (p.get('FastEMA') is not None) else p.get('fastEMA')
                    slow_ema_val = p.get('SlowEMA') if (p.get('SlowEMA') is not None) else p.get('slowEMA')
                    close_val = p.get('Close') if (p.get('Close') is not None) else p.get('close')
                    db_exec(
                        "INSERT INTO diags (ts, barIndex, fastGrad, rsi, adx, gradStab, bandwidth, volume, accel, fastEMA, slowEMA, close, blockersLong, blockersShort, trendSide) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                        (
                            p.get('receivedTs'),
                            p.get('barIndex'),
                            float(p.get('FastGrad') or p.get('fastGrad') or 0.0),
                            float(p.get('RSI') or p.get('rsi') or 0.0),
                            float(p.get('ADX') or p.get('adx') or 0.0),
                            float(p.get('GradStab') or p.get('gradStab') or 0.0),
                            float(p.get('Bandwidth') or p.get('bandwidth') or 0.0),
                            float(p.get('Volume') or p.get('volume') or 0.0),
                            float(accel_val or 0.0),
                            float(fast_ema_val or 0.0),
                            float(slow_ema_val or 0.0),
                            float(close_val or 0.0),
                            json.dumps(blk_long)[:1000],
                            json.dumps(blk_short)[:1000],
                            side
                        )
                    )
            except Exception as db_ex:
                print('[DB] diag insert failed:', db_ex)
            # Debug metric: static bad count anomaly detection every 25 bars
            try:
                if USE_SQLITE and current_trend.get('count',0) % 25 == 0:
                    # If bad stayed zero while at least 10 bearish or bullish bars processed and fg variance observed
                    bad_val = current_trend.get('bad',0)
                    good_val = current_trend.get('good',0)
                    count_val = current_trend.get('count',0)
                    if bad_val == 0 and count_val >= 25:
                        db_exec(
                            "INSERT INTO dev_metrics (ts, metric, value, details) VALUES (?,?,?,?)",
                            (time.time(), 'anomaly_no_bad_bars', 1, f'side={side} count={count_val} good={good_val} bad={bad_val}')
                        )
                    else:
                        db_exec(
                            "INSERT INTO dev_metrics (ts, metric, value, details) VALUES (?,?,?,?)",
                            (time.time(), 'trend_progress', count_val, f'good={good_val} bad={bad_val}')
                        )
            except Exception as exm:
                print('[DB] dev_metrics insert failed:', exm)

            # ---- Weak gradient auto-suggestion counter update ----
            try:
                global weak_grad_consec
                effective_min_fast = float(active_overrides.get('MinEntryFastGradientAbs', DEFAULT_PARAMS['MinEntryFastGradientAbs']))
                adaptive_floor = float(active_overrides.get('AdaptiveMinFloor', DEFAULT_PARAMS['AdaptiveMinFloor']))
                effective_min_fast = max(adaptive_floor, effective_min_fast)
                fg_abs = abs(float(fast_grad))
                tolerance = 0.02
                if fg_abs < effective_min_fast - tolerance:
                    weak_grad_consec += 1
                else:
                    weak_grad_consec = 0
            except Exception:
                pass

            # ---- RSI below floor streak counter ----
            try:
                global rsi_below_consec
                rsi_val = p.get('RSI') if p.get('RSI') is not None else p.get('rsi')
                if rsi_val is not None:
                    rsi_f = float(rsi_val)
                    rsi_floor = float(active_overrides.get('RsiEntryFloor', DEFAULT_PARAMS['RsiEntryFloor']))
                    tolerance = 0.5
                    if rsi_f < rsi_floor - tolerance:
                        rsi_below_consec += 1
                        property_streaks['RsiEntryFloor'] += 1
                    else:
                        rsi_below_consec = 0
                        property_streaks['RsiEntryFloor'] = 0
            except Exception:
                pass

            # ---- Property-specific streak accumulation for auto-apply (fast gradient) ----
            try:
                fg_abs_local = abs(float(fast_grad))
                eff_min_fast_local = max(
                    float(active_overrides.get('AdaptiveMinFloor', DEFAULT_PARAMS['AdaptiveMinFloor'])),
                    float(active_overrides.get('MinEntryFastGradientAbs', DEFAULT_PARAMS['MinEntryFastGradientAbs']))
                )
                if fg_abs_local < eff_min_fast_local - 0.02:
                    property_streaks['MinEntryFastGradientAbs'] += 1
                    # Log entry cancellation for investigation
                    try:
                        rsi_val = p.get('RSI') if p.get('RSI') is not None else p.get('rsi')
                        adx_val = p.get('ADX') if p.get('ADX') is not None else p.get('adx')
                        grad_stab_val = p.get('GradStab') if p.get('GradStab') is not None else p.get('gradStab')
                        bandwidth_val = p.get('Bandwidth') if p.get('Bandwidth') is not None else p.get('bandwidth')
                        blockers_long_val = p.get('BlockersLong', p.get('blockersLong', []))
                        blockers_short_val = p.get('BlockersShort', p.get('blockersShort', []))
                        rsi_floor_eff = float(active_overrides.get('RsiEntryFloor', DEFAULT_PARAMS['RsiEntryFloor']))
                        db_exec("""
                            INSERT INTO entry_cancellations 
                            (ts, barIndex, fastGrad, rsi, adx, gradStab, bandwidth, volume,
                             blockersLong, blockersShort, trendSide, effectiveMinGrad, 
                             effectiveRsiFloor, weakGradStreak, rsiBelowStreak)
                            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                        """, (
                            now_ts, p.get('barIndex'), fast_grad, rsi_val, adx_val, grad_stab_val, bandwidth_val,
                            float(p.get('Volume') or p.get('volume') or 0.0),
                            json.dumps(blockers_long_val), json.dumps(blockers_short_val), side,
                            eff_min_fast_local, rsi_floor_eff,
                            property_streaks['MinEntryFastGradientAbs'], rsi_below_consec
                        ))
                    except Exception as db_ex:
                        print('[DB] entry_cancellations insert failed:', db_ex)
                else:
                    property_streaks['MinEntryFastGradientAbs'] = 0
            except Exception:
                pass

            # ---- Evaluate auto-apply conditions ----
            try:
                if AUTO_APPLY_ENABLED:
                    evaluate_auto_apply(now_ts)
            except Exception as ex_auto:
                print('[AUTO] evaluation error:', ex_auto)
        except Exception as ex:
            print("[SERVER] diag process error:", ex)

    return {"status": "ok", "received": len(items)}

@app.get('/stats')
def stats():
    return JSONResponse({
        'diags_count': len(diags),
        'last_diag': diags[-1] if diags else None,
        'trend_segments': len(trend_segments),
        'current_trend': current_trend,
        'overrides': active_overrides,
        'exit_performance': {
            'poor_mfe_capture_streak': exit_poor_performance_streak,
            'short_hold_streak': short_hold_streak,
            'recent_trades_count': len(recent_trades),
        },
        'entry_performance': {
            'missed_opportunity_streak': missed_opportunity_streak,
            'recent_blocks_count': len(recent_entry_blocks),
            'blocker_patterns': blocker_pattern_counts,
            'top_blockers': sorted(blocker_pattern_counts.items(), key=lambda x: x[1], reverse=True)[:5],
        },
        'property_streaks': property_streaks,
    })

@app.get('/analytics')
def analytics(days: int = 7, min_trades: int = 10):
    """Fast SQL-based trade analytics."""
    try:
        if not USE_SQLITE:
            return JSONResponse({'error': 'Database not enabled'}, status_code=500)
        
        conn = get_db_connection()
        cutoff_ts = time.time() - (days * 86400)
        
        # Overall performance
        cur = conn.cursor()
        cur.execute("""
            SELECT 
                COUNT(*) as total_trades,
                SUM(CASE WHEN realized_points > 0 THEN 1 ELSE 0 END) as winners,
                SUM(CASE WHEN realized_points < 0 THEN 1 ELSE 0 END) as losers,
                SUM(realized_points) as total_pnl,
                AVG(realized_points) as avg_pnl,
                AVG(mfe) as avg_mfe,
                AVG(mae) as avg_mae,
                AVG(bars_held) as avg_bars_held,
                MAX(realized_points) as best_trade,
                MIN(realized_points) as worst_trade
            FROM trades
            WHERE entry_time >= ?
        """, (cutoff_ts,))
        
        overall = dict(zip([d[0] for d in cur.description], cur.fetchone()))
        
        # MFE capture analysis
        cur.execute("""
            SELECT 
                AVG(CASE WHEN mfe > 0 THEN realized_points / mfe ELSE 0 END) * 100 as avg_mfe_capture_pct,
                COUNT(CASE WHEN mfe > 1.5 AND realized_points / mfe < 0.4 THEN 1 END) as poor_capture_count
            FROM trades
            WHERE entry_time >= ? AND mfe > 0
        """, (cutoff_ts,))
        
        mfe_stats = dict(zip([d[0] for d in cur.description], cur.fetchone()))
        
        # Bars held analysis
        cur.execute("""
            SELECT 
                bars_held,
                COUNT(*) as count,
                AVG(realized_points) as avg_pnl,
                SUM(realized_points) as total_pnl
            FROM trades
            WHERE entry_time >= ?
            GROUP BY bars_held
            ORDER BY bars_held
        """, (cutoff_ts,))
        
        bars_analysis = [dict(zip([d[0] for d in cur.description], row)) for row in cur.fetchall()]
        
        # Exit reason analysis
        cur.execute("""
            SELECT 
                exit_reason,
                COUNT(*) as count,
                AVG(realized_points) as avg_pnl,
                AVG(mfe) as avg_mfe
            FROM trades
            WHERE entry_time >= ?
            GROUP BY exit_reason
            ORDER BY count DESC
            LIMIT 10
        """, (cutoff_ts,))
        
        exit_reasons = [dict(zip([d[0] for d in cur.description], row)) for row in cur.fetchall()]
        
        # Hourly performance
        cur.execute("""
            SELECT 
                CAST(strftime('%H', datetime(entry_time, 'unixepoch')) AS INTEGER) as hour,
                COUNT(*) as count,
                AVG(realized_points) as avg_pnl,
                SUM(realized_points) as total_pnl
            FROM trades
            WHERE entry_time >= ?
            GROUP BY hour
            ORDER BY hour
        """, (cutoff_ts,))
        
        hourly = [dict(zip([d[0] for d in cur.description], row)) for row in cur.fetchall()]
        
        # Recent trades
        cur.execute("""
            SELECT 
                entry_time,
                direction,
                entry_price,
                exit_price,
                bars_held,
                realized_points,
                mfe,
                mae,
                exit_reason
            FROM trades
            WHERE entry_time >= ?
            ORDER BY entry_time DESC
            LIMIT 20
        """, (cutoff_ts,))
        
        recent = []
        for row in cur.fetchall():
            trade = dict(zip([d[0] for d in cur.description], row))
            trade['entry_time_str'] = datetime.fromtimestamp(trade['entry_time']).strftime("%Y-%m-%d %H:%M")
            recent.append(trade)
        
        return JSONResponse({
            'overall': overall,
            'mfe_analysis': mfe_stats,
            'bars_held_performance': bars_analysis,
            'exit_reasons': exit_reasons,
            'hourly_performance': hourly,
            'recent_trades': recent,
            'period_days': days,
            'query_time_ms': int((time.time() - cutoff_ts + (days * 86400)) * 1000) % 1000
        })
        
    except Exception as ex:
        print(f"[ANALYTICS] Error: {ex}")
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.get('/debugdump')
def debugdump(n: int = 50):
    n = max(1, min(500, n))
    return JSONResponse({
        'recent': diags[-n:],
        'count': len(diags),
    })

@app.get('/bars/latest')
def bars_latest(limit: int = 50):
    """Return latest normalized bar records from in-memory cache with optional classification join."""
    limit = max(1, min(limit, BAR_CACHE_MAX))
    bars = list(bar_cache)[-limit:]
    if USE_SQLITE and bars:
        idx = [b.get('barIndex') for b in bars if b.get('barIndex') is not None]
        if idx:
            try:
                placeholders = ','.join(['?'] * len(idx))
                cur = get_db_connection().cursor()
                cur.execute(f"SELECT barIndex, isBad, reason FROM dev_classifications WHERE barIndex IN ({placeholders}) ORDER BY id DESC", idx)
                cls_map = {}
                for row in cur.fetchall():
                    b_i, is_bad, reason = row
                    if b_i not in cls_map:
                        cls_map[b_i] = (is_bad, reason)
                for b in bars:
                    bi = b.get('barIndex')
                    if bi in cls_map:
                        b['isBad'] = int(cls_map[bi][0])
                        b['badReason'] = cls_map[bi][1]
            except Exception as ex:
                print('[BARS] classification join error:', ex)
    return JSONResponse({'bars': bars, 'count': len(bars)})

@app.get('/bars/around')
def bars_around(center: int, window: int = 10):
    """Return bars around a center barIndex (inclusive range)."""
    window = max(1, min(window, 100))
    lo = center - window
    hi = center + window
    subset = [b for b in bar_cache if isinstance(b.get('barIndex'), int) and lo <= b['barIndex'] <= hi]
    subset.sort(key=lambda x: x.get('barIndex'))
    if USE_SQLITE and subset:
        idx = [b.get('barIndex') for b in subset if b.get('barIndex') is not None]
        if idx:
            try:
                placeholders = ','.join(['?'] * len(idx))
                cur = get_db_connection().cursor()
                cur.execute(f"SELECT barIndex, isBad, reason FROM dev_classifications WHERE barIndex IN ({placeholders}) ORDER BY id DESC", idx)
                cls_map = {}
                for row in cur.fetchall():
                    b_i, is_bad, reason = row
                    if b_i not in cls_map:
                        cls_map[b_i] = (is_bad, reason)
                for b in subset:
                    bi = b.get('barIndex')
                    if bi in cls_map:
                        b['isBad'] = int(cls_map[bi][0])
                        b['badReason'] = cls_map[bi][1]
            except Exception as ex:
                print('[BARS] classification join error:', ex)
    return JSONResponse({'center': center, 'window': window, 'bars': subset, 'count': len(subset)})

@app.post('/trade_completed')
async def trade_completed(request: Request):
    """Receive trade completion data for exit performance analysis AND save to database.
    Expected fields: EntryTime, EntryBar, Direction, EntryPrice, ExitTime, ExitBar, ExitPrice,
                     BarsHeld, RealizedPoints, MFE, MAE, ExitReason
    """
    try:
        data = await request.json()
        
        # Save to database
        if USE_SQLITE:
            try:
                db_exec("""
                    INSERT INTO trades (
                        entry_time, entry_bar, direction, entry_price,
                        exit_time, exit_bar, exit_price, bars_held,
                        realized_points, mfe, mae, exit_reason
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """, (
                    float(data.get('EntryTime', time.time())),
                    int(data.get('EntryBar', 0)),
                    data.get('Direction', 'LONG'),
                    float(data.get('EntryPrice', 0)),
                    float(data.get('ExitTime', time.time())),
                    int(data.get('ExitBar', 0)),
                    float(data.get('ExitPrice', 0)),
                    int(data.get('BarsHeld', 0)),
                    float(data.get('RealizedPoints', 0)),
                    float(data.get('MFE', 0)),
                    float(data.get('MAE', 0)),
                    data.get('ExitReason', '')
                ))
            except Exception as db_ex:
                print(f"[TRADE_COMPLETED] DB insert failed: {db_ex}")
        
        # Analyze performance for auto-optimization
        analyze_trade_performance(data)
        
        # Trigger auto-apply evaluation if enabled
        if AUTO_APPLY_ENABLED:
            evaluate_auto_apply(time.time())
        
        return JSONResponse({'status': 'ok', 'analyzed': True, 'saved_to_db': USE_SQLITE})
    except Exception as ex:
        print(f"[TRADE_COMPLETED] Error: {ex}")
        return JSONResponse({'status': 'error', 'message': str(ex)}, status_code=500)

@app.post('/entry_blocked')
async def entry_blocked(request: Request):
    """Receive entry block data for entry performance analysis.
    Expected fields: FastGrad, ADX, RSI, Blockers (array), TrendSide, MoveAfterBlock (optional - will be sent later)
    Strategy should track price movement after block and send update with actual favorable move.
    """
    try:
        data = await request.json()
        analyze_entry_performance(data)
        
        # Trigger auto-apply evaluation if enabled
        if AUTO_APPLY_ENABLED:
            evaluate_auto_apply(time.time())
        
        return JSONResponse({'status': 'ok', 'analyzed': True})
    except Exception as ex:
        print(f"[ENTRY_BLOCKED] Error: {ex}")
        return JSONResponse({'status': 'error', 'message': str(ex)}, status_code=500)

@app.post('/suggest')
async def suggest(request: Request):
    try:
        d = await request.json()
    except Exception:
        text = await request.body()
        try:
            d = json.loads(text)
        except Exception:
            return JSONResponse({'error': 'invalid_json'}, status_code=400)

    print(f"[SUGGEST] Received payload keys: {list(d.keys())}")
    
    try:
        suggestions: List[Dict[str, Any]] = []
        adx = float(d.get('adx', 0) or 0)
        grad_stab = float(d.get('gradStab', 0) or 0)
        fast_grad = float(d.get('fastGrad', 0) or 0)
        bandwidth = float(d.get('bandwidth', 0) or 0)

        # Effective params (override falls back to default)
        effective = { k: float(active_overrides.get(k, v)) for k, v in DEFAULT_PARAMS.items() }

        print(f"[SUGGEST] Values - adx:{adx:.2f} gradStab:{grad_stab:.3f} fastGrad:{fast_grad:.3f} bandwidth:{bandwidth:.4f} effective:{effective}")

        # ADX recommendation (only if below effective threshold minus tolerance)
        if adx < effective['MinAdxForEntry'] - 2.0:
            candidate = max(10.0, adx + 2.0)
            if candidate > effective['MinAdxForEntry']:
                suggestions.append({
                    'property': 'MinAdxForEntry',
                    'recommend': round(candidate, 2),
                    'reason': f'ADX {adx:.1f} < target {effective['MinAdxForEntry']:.1f}',
                })

        # Gradient stability (recommend lower max if exceeded beyond buffer)
        if grad_stab > effective['MaxGradientStabilityForEntry'] + 0.3:
            candidate = max(0.5, grad_stab - 0.3)
            if candidate < effective['MaxGradientStabilityForEntry']:
                suggestions.append({
                    'property': 'MaxGradientStabilityForEntry',
                    'recommend': round(candidate, 2),
                    'reason': f'GradStab {grad_stab:.2f} > limit {effective['MaxGradientStabilityForEntry']:.2f}',
                })

        # Fast gradient minimum (recommend raise if too weak)
        if abs(fast_grad) < effective['MinEntryFastGradientAbs'] - 0.05:
            # Prefer adjusting adaptive floor first if it is constraining entries
            floor = effective.get('AdaptiveMinFloor', 0.30)
            tol = 0.02
            if abs(fast_grad) < floor - tol:
                # Suggest lowering floor towards observed fast_grad (with small margin)
                candidate = max(0.01, round(abs(fast_grad) + 0.02, 2))
                if candidate < floor:
                    suggestions.append({
                        'property': 'AdaptiveMinFloor',
                        'recommend': candidate,
                        'reason': f'FastGrad {fast_grad:.3f} below adaptive floor {floor:.2f}',
                    })
            else:
                # If floor is okay, suggest raising min entry threshold only if below a desired target
                target = max(0.30, round(abs(fast_grad) + 0.05, 2))
                if target > effective['MinEntryFastGradientAbs']:
                    suggestions.append({
                        'property': 'MinEntryFastGradientAbs',
                        'recommend': target,
                        'reason': f'FastGrad {fast_grad:.3f} < min {effective['MinEntryFastGradientAbs']:.2f}',
                    })

        # Bandwidth (recommend tighter cap if exceeded by margin)
        if bandwidth > effective['MaxBandwidthForEntry'] + 0.005:
            candidate = max(0.110, effective['MaxBandwidthForEntry'] - 0.010)
            if candidate < effective['MaxBandwidthForEntry']:
                suggestions.append({
                    'property': 'MaxBandwidthForEntry',
                    'recommend': round(candidate, 3),
                    'reason': f'Bandwidth {bandwidth:.3f} > cap {effective['MaxBandwidthForEntry']:.3f}',
                })

        if not suggestions:
            suggestions.append({ 'property': 'General', 'recommend': 'N/A', 'reason': 'Values within effective thresholds. No adjustment needed.', 'canApply': False })

        # Add apply metadata & mark activeOverride if already satisfied
        for s in suggestions:
            if s['property'] != 'General':
                s['canApply'] = True
                s['applyUrl'] = '/apply'
                cur_override = active_overrides.get(s['property'])
                if cur_override is not None:
                    try:
                        cv = float(cur_override)
                        rv = float(s['recommend']) if isinstance(s['recommend'], (int, float)) else None
                        if rv is not None:
                            if s['property'] in ('MinAdxForEntry','MinEntryFastGradientAbs') and cv >= rv:
                                s['activeOverride'] = True
                            if s['property'] in ('MaxGradientStabilityForEntry','MaxBandwidthForEntry') and cv <= rv:
                                s['activeOverride'] = True
                    except Exception:
                        pass
            else:
                s['canApply'] = False

        ts = { k: current_trend.get(k) for k in ['side','startTs','startLocal','startBarIndex','count','good','bad','pnlProxy'] }
        response = { 'suggestions': suggestions, 'trend': ts, 'effectiveParams': effective, 'overrides': active_overrides }
        print(f"[SUGGEST] Returning {len(suggestions)} suggestions")
        return JSONResponse(response)
    except Exception as ex:
        print(f"[SUGGEST] Error: {ex}")
        return JSONResponse({ 'error': str(ex) }, status_code=400)

@app.get("/trendlog")
async def get_trendlog():
    return {
        "current": current_trend,
        "segments": trend_segments[-100:],
        "totalSegments": len(trend_segments),
        "minConsecForFlip": MIN_CONSEC_FOR_TREND_FLIP,
        "pendingFlip": {"side": pending_flip_side, "count": pending_flip_count} if pending_flip_side else None,
        "overrides": active_overrides,
        "inputsMissingCount": trend_inputs_missing_count,
        "goodBadUpdates": trend_goodbad_updates,
        "weakGradConsec": weak_grad_consec,
        "rsiBelowConsec": rsi_below_consec,
        "rsiFloor": float(active_overrides.get('RsiEntryFloor', DEFAULT_PARAMS['RsiEntryFloor'])),
    }

@app.get('/autosuggest')
def autosuggest():
    """Automatic suggestions based on consecutive weak fast gradient bars.
    Returns suggestions without needing manual /suggest POST when pattern persists.
    """
    suggestions: List[Dict[str, Any]] = []
    effective = { k: float(active_overrides.get(k, v)) for k, v in DEFAULT_PARAMS.items() }
    floor = effective.get('AdaptiveMinFloor', DEFAULT_PARAMS['AdaptiveMinFloor'])
    min_fast = max(floor, effective['MinEntryFastGradientAbs'])
    if weak_grad_consec >= WEAK_GRAD_SUGGEST_THRESHOLD:
        # Primary suggestion: lower MinEntryFastGradientAbs by small step, respecting floor
        candidate = round(max(floor, min_fast - 0.05), 3)
        if candidate < effective['MinEntryFastGradientAbs']:
            suggestions.append({
                'property': 'MinEntryFastGradientAbs',
                'recommend': candidate,
                'reason': f'{weak_grad_consec} consecutive weak fastGrad (< {min_fast:.2f}) bars',
                'autoTrigger': True,
                'canApply': True,
                'applyUrl': '/apply'
            })
        # If floor is the constraining element (dominates threshold), consider lowering floor
        if floor >= effective['MinEntryFastGradientAbs'] and floor > 0.01:
            new_floor = round(max(0.01, floor - 0.02), 3)
            if new_floor < floor:
                suggestions.append({
                    'property': 'AdaptiveMinFloor',
                    'recommend': new_floor,
                    'reason': f'Floor {floor:.2f} may block entries, {weak_grad_consec} weak bars',
                    'autoTrigger': True,
                    'canApply': True,
                    'applyUrl': '/apply'
                })
    # RSI floor suggestion logic
    rsi_floor_eff = effective.get('RsiEntryFloor', DEFAULT_PARAMS['RsiEntryFloor'])
    if rsi_below_consec >= RSI_BELOW_SUGGEST_THRESHOLD:
        # Lower RSI floor gradually, keep above 30
        candidate_rsi_floor = round(max(30.0, rsi_floor_eff - 2.0), 2)
        if candidate_rsi_floor < rsi_floor_eff:
            suggestions.append({
                'property': 'RsiEntryFloor',
                'recommend': candidate_rsi_floor,
                'reason': f'RSI below floor {rsi_floor_eff:.1f} for {rsi_below_consec} consecutive bars',
                'autoTrigger': True,
                'canApply': True,
                'applyUrl': '/apply'
            })
    return JSONResponse({
        'suggestions': suggestions,
        'weakGradConsec': weak_grad_consec,
        'rsiBelowConsec': rsi_below_consec,
        'rsiFloor': rsi_floor_eff,
        'threshold': WEAK_GRAD_SUGGEST_THRESHOLD,
        'autoApply': {
            'enabled': AUTO_APPLY_ENABLED,
            'streaks': property_streaks,
            'recentEvents': auto_apply_events_cache[-10:],
            'cooldownSec': AUTO_APPLY_COOLDOWN_SEC,
            'dailyLimit': AUTO_APPLY_DAILY_LIMIT
        },
        'effectiveParams': effective,
        'overrides': active_overrides
    })

@app.post('/autoapply/toggle')
def toggle_auto_apply():
    global AUTO_APPLY_ENABLED
    AUTO_APPLY_ENABLED = not AUTO_APPLY_ENABLED
    return JSONResponse({'enabled': AUTO_APPLY_ENABLED})

@app.get('/cancellations')
def get_cancellations(limit: int = 100, minStreak: int = 1):
    """Query entry cancellations for analysis"""
    if not USE_SQLITE:
        return JSONResponse({'error': 'sqlite_disabled'}, status_code=500)
    
    try:
        conn = sqlite3.connect(DB_PATH)
        conn.row_factory = sqlite3.Row
        cur = conn.cursor()
        
        query = """
            SELECT * FROM entry_cancellations 
            WHERE weakGradStreak >= ?
            ORDER BY ts DESC 
            LIMIT ?
        """
        cur.execute(query, (minStreak, min(limit, 1000)))
        rows = cur.fetchall()
        conn.close()
        
        results = []
        for row in rows:
            results.append({
                'id': row['id'],
                'ts': row['ts'],
                'localTime': datetime.fromtimestamp(row['ts']).strftime("%Y-%m-%d %H:%M:%S"),
                'barIndex': row['barIndex'],
                'fastGrad': row['fastGrad'],
                'rsi': row['rsi'],
                'adx': row['adx'],
                'gradStab': row['gradStab'],
                'bandwidth': row['bandwidth'],
                'volume': row['volume'],
                'blockersLong': json.loads(row['blockersLong']) if row['blockersLong'] else [],
                'blockersShort': json.loads(row['blockersShort']) if row['blockersShort'] else [],
                'trendSide': row['trendSide'],
                'effectiveMinGrad': row['effectiveMinGrad'],
                'effectiveRsiFloor': row['effectiveRsiFloor'],
                'weakGradStreak': row['weakGradStreak'],
                'rsiBelowStreak': row['rsiBelowStreak']
            })
        
        return JSONResponse({
            'cancellations': results,
            'count': len(results),
            'limit': limit,
            'minStreak': minStreak
        })
    except Exception as ex:
        print('[DB] cancellations query error:', ex)
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.get('/overrides')
def get_overrides():
    # Include effective params (defaults merged with overrides) and expose defaults for diff UI
    effective = { k: float(active_overrides.get(k, v)) for k, v in DEFAULT_PARAMS.items() }
    return JSONResponse({'overrides': active_overrides, 'effectiveParams': effective, 'defaultParams': DEFAULT_PARAMS, 'count': len(active_overrides)})

@app.post('/apply')
async def apply_override(request: Request):
    """Apply a suggestion immediately by storing it in active overrides.
    Expected JSON: { "property": "MinAdxForEntry", "value": 18.5 }
    """
    try:
        payload = await request.json()
    except Exception:
        return JSONResponse({'error': 'invalid_json'}, status_code=400)

    prop = payload.get('property')
    val = payload.get('value') if 'value' in payload else payload.get('recommend')
    if not prop:
        return JSONResponse({'error': 'missing_property'}, status_code=400)
    if val is None:
        return JSONResponse({'error': 'missing_value'}, status_code=400)

    # Store override
    prev = active_overrides.get(prop)
    active_overrides[prop] = val
    print(f"[APPLY] Override set {prop}={val} (prev={prev})")
    try:
        if USE_SQLITE:
            db_exec(
                "INSERT INTO overrides_history (ts, property, oldValue, newValue, source) VALUES (?,?,?,?,?)",
                (time.time(), prop, float(prev) if prev is not None else None, float(val), 'manual')
            )
    except Exception as db_ex:
        print('[DB] override history insert failed:', db_ex)
    save_overrides_to_disk()
    effective = { k: float(active_overrides.get(k, v)) for k, v in DEFAULT_PARAMS.items() }
    return JSONResponse({'status': 'ok', 'overrides': active_overrides, 'effectiveParams': effective, 'defaultParams': DEFAULT_PARAMS})

@app.delete('/override/{prop}')
def delete_override(prop: str):
    if prop in active_overrides:
        removed = active_overrides.pop(prop)
        print(f"[OVERRIDE-DEL] Removed {prop} (was {removed})")
        save_overrides_to_disk()
        try:
            if USE_SQLITE:
                db_exec(
                    "INSERT INTO overrides_history (ts, property, oldValue, newValue, source) VALUES (?,?,?,?,?)",
                    (time.time(), prop, float(removed), None, 'manual-delete')
                )
        except Exception as db_ex:
            print('[DB] override delete history insert failed:', db_ex)
        effective = { k: float(active_overrides.get(k, v)) for k, v in DEFAULT_PARAMS.items() }
        return JSONResponse({'status': 'ok', 'removed': prop, 'overrides': active_overrides, 'effectiveParams': effective, 'defaultParams': DEFAULT_PARAMS})
    effective = { k: float(active_overrides.get(k, v)) for k, v in DEFAULT_PARAMS.items() }
    return JSONResponse({'status': 'not_found', 'overrides': active_overrides, 'effectiveParams': effective, 'defaultParams': DEFAULT_PARAMS}, status_code=404)

@app.get('/dbstats')
def dbstats():
    if not USE_SQLITE:
        return JSONResponse({'sqlite': False})
    try:
        d_count = db_query_one('SELECT COUNT(*) FROM diags') or [0]
        o_count = db_query_one('SELECT COUNT(*) FROM overrides_history') or [0]
        a_count = db_query_one('SELECT COUNT(*) FROM auto_apply_events') or [0]
        s_count = db_query_one('SELECT COUNT(*) FROM suggestions') or [0]
        snap_count = db_query_one('SELECT COUNT(*) FROM strategy_snapshots') or [0]
        foot_count = db_query_one('SELECT COUNT(*) FROM ai_footprints') or [0]
        return JSONResponse({'sqlite': True, 'diags': d_count[0], 'overrides_history': o_count[0], 'auto_apply_events': a_count[0], 'suggestions': s_count[0], 'snapshots': snap_count[0], 'footprints': foot_count[0]})
    except Exception as ex:
        return JSONResponse({'sqlite': True, 'error': str(ex)}, status_code=500)

# --- Strategy Snapshot & AI Footprint Endpoints ---

def compute_file_hash(filepath: str) -> str:
    """Compute SHA256 hash of a file for change detection."""
    try:
        with open(filepath, 'rb') as f:
            return hashlib.sha256(f.read()).hexdigest()
    except Exception as ex:
        print(f"[HASH] Error hashing {filepath}: {ex}")
        return "error"

def create_strategy_snapshot(notes: str = "") -> Dict[str, Any]:
    """Create a strategy snapshot capturing current state for session persistence."""
    try:
        # Component hashes for change detection
        base_path = os.path.dirname(os.path.dirname(os.path.dirname(__file__)))
        component_hashes = {}
        key_files = [
            os.path.join(base_path, 'Strategies', 'GradientSlopeStrategy.cs'),
            os.path.join(base_path, 'Indicators', 'CBASTestingIndicator3.cs'),
        ]
        for fpath in key_files:
            if os.path.exists(fpath):
                fname = os.path.basename(fpath)
                component_hashes[fname] = compute_file_hash(fpath)
        
        # Performance summary from trades table
        perf_summary = {}
        if USE_SQLITE:
            try:
                conn = get_db_connection()
                cur = conn.cursor()
                cutoff_ts = time.time() - (7 * 86400)  # last 7 days
                cur.execute("""
                    SELECT 
                        COUNT(*) as total,
                        SUM(CASE WHEN realized_points > 0 THEN 1 ELSE 0 END) as winners,
                        SUM(realized_points) as total_pnl,
                        AVG(realized_points) as avg_pnl,
                        AVG(mfe) as avg_mfe,
                        AVG(bars_held) as avg_bars_held
                    FROM trades
                    WHERE entry_time >= ?
                """, (cutoff_ts,))
                row = cur.fetchone()
                if row:
                    perf_summary = {
                        'total_trades': row[0] or 0,
                        'winners': row[1] or 0,
                        'total_pnl': row[2] or 0.0,
                        'avg_pnl': row[3] or 0.0,
                        'avg_mfe': row[4] or 0.0,
                        'avg_bars_held': row[5] or 0.0,
                        'period_days': 7
                    }
            except Exception as db_ex:
                print(f"[SNAPSHOT] Error querying trades: {db_ex}")
        
        # Current trend segment
        trend_summary = {
            'side': current_trend.get('side'),
            'count': current_trend.get('count', 0),
            'good': current_trend.get('good', 0),
            'bad': current_trend.get('bad', 0),
            'pnl_proxy': current_trend.get('pnlProxy', 0.0)
        }
        
        snapshot = {
            'ts': time.time(),
            'version': '1.0',
            'overrides': active_overrides.copy(),
            'streaks': property_streaks.copy(),
            'performance': perf_summary,
            'trend': trend_summary,
            'component_hashes': component_hashes,
            'notes': notes,
            'diags_count': len(diags),
            'trend_segments_count': len(trend_segments)
        }
        def build_strategy_index() -> Dict[str, Any]:
            """Create a documented index of the strategy structure and recent analyses for AI continuity."""
            # Summarize recent trade analysis
            trade_summary = {
                'recent_count': len(recent_trades),
                'poor_capture_streak': exit_poor_performance_streak,
                'short_hold_streak': short_hold_streak,
                'samples': recent_trades[-5:]  # last few for context
            }
            # Summarize recent entry block analysis
            entry_summary = {
                'recent_blocks': len(recent_entry_blocks),
                'missed_opportunity_streak': missed_opportunity_streak,
                'common_blockers': blocker_pattern_counts,
                'samples': recent_entry_blocks[-5:]
            }
            # Current config and state
            cfg = {
                'defaults': DEFAULT_PARAMS,
                'overrides': active_overrides,
                'streaks': property_streaks,
            }
            # Components and hashes
            snapshot = create_strategy_snapshot()
            components = snapshot.get('component_hashes', {})
            trend = snapshot.get('trend', {})

            # Narrative guidance
            guidance = [
                'If missed_opportunity_streak is elevated and common_blockers include FastGradMin or ADXMin, consider loosening thresholds within bounds.',
                'If exit_poor_performance_streak rises, reduce ValidationMinFastGradientAbs or increase MinHoldBars per AUTO_APPLY_STEPS.',
                'Use overrides_history and auto_apply_events tables to correlate changes with outcomes.'
            ]
            index_obj = {
                'ts': time.time(),
                'version': '1.0',
                'components': components,
                'config': cfg,
                'trend': trend,
                'trade_analysis': trade_summary,
                'entry_analysis': entry_summary,
                'diags_count': len(diags),
                'guidance': guidance
            }
            # Log a footprint whenever we rebuild the index to aid continuity
            try:
                log_ai_footprint(
                    action='INDEX_REBUILT',
                    details=json.dumps({'components': list(components.keys()), 'diags_count': len(diags)}),
                    reasoning='Periodic index rebuild for AI continuity',
                    diff_summary='N/A'
                )
            except Exception as _ex:
                pass
            return index_obj

        @app.post('/structure/doc/save')
        def save_structure_index():
            """Persist a strategy structure index document into the DB."""
            try:
                idx = build_strategy_index()
                db_exec(
                    "INSERT INTO strategy_index (ts, version, index_json) VALUES (?,?,?)",
                    (idx['ts'], idx['version'], json.dumps(idx))
                )
                return JSONResponse({'status': 'ok', 'saved_ts': idx['ts']})
            except Exception as ex:
                return JSONResponse({'error': str(ex)}, status_code=500)

        @app.get('/structure/doc/latest')
        def get_structure_index_latest():
            """Retrieve the latest saved strategy structure index document."""
            try:
                conn = get_db_connection()
                conn.row_factory = sqlite3.Row
                cur = conn.cursor()
                cur.execute("SELECT * FROM strategy_index ORDER BY ts DESC LIMIT 1")
                row = cur.fetchone()
                if not row:
                    # If none saved, return a fresh index without saving
                    return JSONResponse(build_strategy_index())
                return JSONResponse(json.loads(row['index_json']))
            except Exception as ex:
                return JSONResponse({'error': str(ex)}, status_code=500)
        
        return snapshot
    except Exception as ex:
        print(f"[SNAPSHOT] Creation error: {ex}")
        return {'error': str(ex)}

@app.get('/snapshot/latest')
def get_latest_snapshot():
    """Retrieve the most recent strategy snapshot."""
    if not USE_SQLITE:
        # Return ephemeral snapshot if DB disabled
        return JSONResponse(create_strategy_snapshot())
    
    try:
        conn = get_db_connection()
        conn.row_factory = sqlite3.Row
        cur = conn.cursor()
        cur.execute("SELECT * FROM strategy_snapshots ORDER BY ts DESC LIMIT 1")
        row = cur.fetchone()
        
        if row:
            snapshot = {
                'id': row['id'],
                'ts': row['ts'],
                'version': row['version'],
                'overrides': json.loads(row['overrides_json']) if row['overrides_json'] else {},
                'streaks': json.loads(row['streaks_json']) if row['streaks_json'] else {},
                'performance': json.loads(row['perf_json']) if row['perf_json'] else {},
                'component_hashes': json.loads(row['component_hashes_json']) if row['component_hashes_json'] else {},
                'notes': row['notes'],
                'created_at': row['created_at']
            }
            return JSONResponse(snapshot)
        else:
            # No stored snapshot, return current state
            return JSONResponse(create_strategy_snapshot())
    except Exception as ex:
        print(f"[SNAPSHOT] Error retrieving latest: {ex}")
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.get('/dev/metrics/latest')
def dev_metrics_latest(limit: int = 50):
    """Return latest development/debug metrics."""
    try:
        conn = get_db_connection()
        conn.row_factory = sqlite3.Row
        cur = conn.cursor()
        cur.execute("SELECT ts, metric, value, details FROM dev_metrics ORDER BY ts DESC LIMIT ?", (min(limit,200),))
        rows = cur.fetchall()
        return JSONResponse({'metrics': [dict(r) for r in rows], 'count': len(rows)})
    except Exception as ex:
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.get('/dev/classifications/latest')
def dev_classifications_latest(limit: int = 100):
    """Return latest per-bar classification decisions."""
    try:
        conn = get_db_connection()
        conn.row_factory = sqlite3.Row
        cur = conn.cursor()
        cur.execute("SELECT ts, barIndex, side, fastGrad, accel, isBad, reason FROM dev_classifications ORDER BY ts DESC LIMIT ?", (min(limit,500),))
        rows = cur.fetchall()
        return JSONResponse({'classifications': [dict(r) for r in rows], 'count': len(rows)})
    except Exception as ex:
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.get('/dev/scan/anomalies')
def dev_scan_anomalies():
    """Scan recent metrics for anomalies (e.g., missing bad bars)."""
    try:
        conn = get_db_connection()
        conn.row_factory = sqlite3.Row
        cur = conn.cursor()
        cur.execute("SELECT ts, metric, value, details FROM dev_metrics WHERE metric='anomaly_no_bad_bars' ORDER BY ts DESC LIMIT 20")
        anomalies = [dict(r) for r in cur.fetchall()]
        cur.execute("SELECT COUNT(*) FROM dev_classifications WHERE isBad=1")
        bad_total = cur.fetchone()[0]
        # Log footprint about anomaly state
        try:
            log_ai_footprint(
                action='ANOMALY_SCAN',
                details=json.dumps({'anomalies_count': len(anomalies), 'bad_total': bad_total}),
                reasoning='Track anomaly: prolonged no-bad-bars condition',
                diff_summary='N/A'
            )
        except Exception:
            pass
        return JSONResponse({'anomalies': anomalies, 'bad_classifications_total': bad_total})
    except Exception as ex:
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.get('/dev/dedup/diags')
def dev_dedup_diags():
    """Remove duplicate diags entries keeping latest per barIndex."""
    try:
        conn = get_db_connection()
        cur = conn.cursor()
        # Count before
        cur.execute('SELECT COUNT(*) FROM diags')
        before = cur.fetchone()[0]
        # Delete rows whose id is not the max id for their barIndex
        cur.execute('DELETE FROM diags WHERE id NOT IN (SELECT MAX(id) FROM diags GROUP BY barIndex)')
        conn.commit()
        cur.execute('SELECT COUNT(*) FROM diags')
        after = cur.fetchone()[0]
        removed = before - after
        # Metric record
        try:
            db_exec("INSERT INTO dev_metrics (ts, metric, value, details) VALUES (?,?,?,?)", (time.time(), 'dedup_diags', removed, f'before={before} after={after}'))
        except Exception:
            pass
        return JSONResponse({'status': 'ok', 'removed': removed, 'remaining': after})
    except Exception as ex:
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.get('/dev/scan/bad')
def dev_scan_bad(limit: int = 500):
    """Recompute bad classifications from stored diags and compare with dev_classifications."""
    try:
        conn = get_db_connection()
        conn.row_factory = sqlite3.Row
        cur = conn.cursor()
        # Fetch latest diags with needed fields
        cur.execute("SELECT id, ts, barIndex, fastGrad, accel, fastEMA, close, trendSide FROM diags ORDER BY id DESC LIMIT ?", (min(limit,2000),))
        rows = cur.fetchall()
        recomputed = []
        mismatches = 0
        for r in rows:
            fg = float(r['fastGrad'] or 0.0)
            accel = float(r['accel'] or 0.0)
            fastEMA = float(r['fastEMA'] or 0.0)
            close = float(r['close'] or 0.0)
            side = r['trendSide']
            is_bad = False
            if side == 'BULL':
                is_bad = (fg < 0) or (accel < -0.01 and fg < 0.02)
            elif side == 'BEAR':
                is_bad = (fg > 0) or (accel > 0.01 and fg > -0.02)
            recomputed.append({'barIndex': r['barIndex'], 'side': side, 'fastGrad': fg, 'accel': accel, 'fastEMA': fastEMA, 'close': close, 'isBad': int(is_bad)})
        # Compare with last classifications
        cur.execute("SELECT barIndex, isBad FROM dev_classifications ORDER BY id DESC LIMIT ?", (len(recomputed),))
        cls_rows = cur.fetchall()
        cls_map = {}
        for cr in cls_rows:
            b, ib = cr
            if b not in cls_map:
                cls_map[b] = ib
        for item in recomputed:
            prev = cls_map.get(item['barIndex'])
            if prev is not None and int(prev) != item['isBad']:
                mismatches += 1
        return JSONResponse({'recomputed_count': len(recomputed), 'mismatches': mismatches, 'sample': recomputed[:25]})
    except Exception as ex:
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.post('/snapshot/save')
async def save_snapshot(request: Request):
    """Save a strategy snapshot to the database."""
    if not USE_SQLITE:
        return JSONResponse({'error': 'Database not enabled'}, status_code=500)
    
    try:
        payload = await request.json()
        notes = payload.get('notes', '')
        
        snapshot = create_strategy_snapshot(notes)
        
        if 'error' in snapshot:
            return JSONResponse({'error': snapshot['error']}, status_code=500)
        
        db_exec("""
            INSERT INTO strategy_snapshots 
            (ts, version, overrides_json, streaks_json, perf_json, component_hashes_json, notes)
            VALUES (?, ?, ?, ?, ?, ?, ?)
        """, (
            snapshot['ts'],
            snapshot['version'],
            json.dumps(snapshot['overrides']),
            json.dumps(snapshot['streaks']),
            json.dumps(snapshot['performance']),
            json.dumps(snapshot['component_hashes']),
            snapshot['notes']
        ))
        
        print(f"[SNAPSHOT] Saved snapshot at {snapshot['ts']} with notes: {notes[:50]}")
        return JSONResponse({'status': 'ok', 'snapshot': snapshot})
    except Exception as ex:
        print(f"[SNAPSHOT] Save error: {ex}")
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.get('/snapshot/list')
def list_snapshots(limit: int = 20):
    """List recent strategy snapshots."""
    if not USE_SQLITE:
        return JSONResponse({'error': 'Database not enabled'}, status_code=500)
    
    try:
        conn = get_db_connection()
        conn.row_factory = sqlite3.Row
        cur = conn.cursor()
        cur.execute("""
            SELECT id, ts, version, notes, created_at
            FROM strategy_snapshots 
            ORDER BY ts DESC 
            LIMIT ?
        """, (min(limit, 100),))
        
        snapshots = []
        for row in cur.fetchall():
            snapshots.append({
                'id': row['id'],
                'ts': row['ts'],
                'version': row['version'],
                'notes': row['notes'],
                'created_at': row['created_at'],
                'local_time': datetime.fromtimestamp(row['ts']).strftime("%Y-%m-%d %H:%M:%S")
            })
        
        return JSONResponse({'snapshots': snapshots, 'count': len(snapshots)})
    except Exception as ex:
        print(f"[SNAPSHOT] List error: {ex}")
        return JSONResponse({'error': str(ex)}, status_code=500)


@app.post('/ai/footprint/add')
async def add_ai_footprint(request: Request):
    """Log an AI decision footprint for reasoning replay."""
    if not USE_SQLITE:
        return JSONResponse({'error': 'Database not enabled'}, status_code=500)
    
    try:
        payload = await request.json()
        action = payload.get('action', '')
        details = payload.get('details', '')
        reasoning = payload.get('reasoning', '')
        diff_summary = payload.get('diff_summary', '')
        
        if not action:
            return JSONResponse({'error': 'action required'}, status_code=400)
        
        ts = time.time()
        db_exec("""
            INSERT INTO ai_footprints (ts, action, details, reasoning, diff_summary)
            VALUES (?, ?, ?, ?, ?)
        """, (ts, action, details, reasoning, diff_summary))
        
        print(f"[AI-FOOTPRINT] Logged: {action} at {ts}")
        return JSONResponse({'status': 'ok', 'ts': ts, 'action': action})
    except Exception as ex:
        print(f"[AI-FOOTPRINT] Add error: {ex}")
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.post('/advisor/recommendations')
async def advisor_recommendations(request: Request):
    """Accept timeframe advisor recommendations and persist to ai_footprints."""
    if not USE_SQLITE:
        return JSONResponse({'error': 'Database not enabled'}, status_code=500)
    try:
        payload = await request.json()
        instrument = payload.get('instrument', '')
        timeframe = payload.get('timeframe', '')
        recommendation = payload.get('recommendation', '')  # Prefer30s/Prefer60s/Keep
        explanation = payload.get('explanation', '')
        metrics = payload.get('metrics', {})
        if not recommendation:
            return JSONResponse({'error': 'recommendation required'}, status_code=400)
        details = json.dumps({
            'instrument': instrument,
            'timeframe': timeframe,
            'recommendation': recommendation,
            'metrics': metrics
        })
        reasoning = explanation or 'TimeframeAdvisor heuristic evaluation'
        ts = time.time()
        db_exec(
            "INSERT INTO ai_footprints (ts, action, details, reasoning, diff_summary) VALUES (?,?,?,?,?)",
            (ts, 'TIMEFRAME_ADVICE', details, reasoning, '')
        )
        return JSONResponse({'status': 'ok', 'ts': ts})
    except Exception as ex:
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.get('/advisor/recommendations/latest')
def advisor_recommendations_latest(limit: int = 50):
    """List latest timeframe advisor recommendations from ai_footprints."""
    if not USE_SQLITE:
        return JSONResponse({'error': 'Database not enabled'}, status_code=500)
    try:
        conn = get_db_connection()
        conn.row_factory = sqlite3.Row
        cur = conn.cursor()
        cur.execute("SELECT ts, details, reasoning FROM ai_footprints WHERE action='TIMEFRAME_ADVICE' ORDER BY ts DESC LIMIT ?", (min(limit,200),))
        rows = cur.fetchall()
        recs = []
        for r in rows:
            d = {}
            try:
                d = json.loads(r['details'])
            except Exception:
                d = {'raw': r['details']}
            recs.append({'ts': r['ts'], 'details': d, 'reasoning': r['reasoning']})
        return JSONResponse({'recommendations': recs, 'count': len(recs)})
    except Exception as ex:
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.get('/ai/footprints')
def get_ai_footprints(limit: int = 50, action: str = None):
    """Retrieve AI decision footprints."""
    if not USE_SQLITE:
        return JSONResponse({'error': 'Database not enabled'}, status_code=500)
    
    try:
        conn = get_db_connection()
        conn.row_factory = sqlite3.Row
        cur = conn.cursor()
        
        if action:
            cur.execute("""
                SELECT * FROM ai_footprints 
                WHERE action = ?
                ORDER BY ts DESC 
                LIMIT ?
            """, (action, min(limit, 200)))
        else:
            cur.execute("""
                SELECT * FROM ai_footprints 
                ORDER BY ts DESC 
                LIMIT ?
            """, (min(limit, 200),))
        
        footprints = []
        for row in cur.fetchall():
            footprints.append({
                'id': row['id'],
                'ts': row['ts'],
                'action': row['action'],
                'details': row['details'],
                'reasoning': row['reasoning'],
                'diff_summary': row['diff_summary'],
                'created_at': row['created_at'],
                'local_time': datetime.fromtimestamp(row['ts']).strftime("%Y-%m-%d %H:%M:%S")
            })
        
        return JSONResponse({'footprints': footprints, 'count': len(footprints)})
    except Exception as ex:
        print(f"[AI-FOOTPRINT] Query error: {ex}")
        return JSONResponse({'error': str(ex)}, status_code=500)

if __name__ == '__main__':
    import uvicorn
    port = int(os.environ.get('PORT', '5001'))
    uvicorn.run(app, host='127.0.0.1', port=port)
