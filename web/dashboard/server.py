import os
import time
import json
import csv
import sqlite3
import hashlib
import re
from datetime import datetime
from typing import List, Dict, Any
from collections import deque
from fastapi import FastAPI, Request
from fastapi import WebSocket, WebSocketDisconnect
from fastapi.responses import HTMLResponse, JSONResponse, PlainTextResponse
from fastapi.staticfiles import StaticFiles
from fastapi.middleware.cors import CORSMiddleware

app = FastAPI()

# Add CORS middleware to allow browser access
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # Allow all origins
    allow_credentials=True,
    allow_methods=["*"],  # Allow all methods
    allow_headers=["*"],  # Allow all headers
)
ws_clients: List[WebSocket] = []

async def ws_broadcast(message: Dict[str, Any]):
    """Broadcast a JSON message to all connected WebSocket clients."""
    stale: List[WebSocket] = []
    for ws in ws_clients:
        try:
            await ws.send_json(message)
        except Exception:
            stale.append(ws)
    # Remove stale connections
    for s in stale:
        try:
            ws_clients.remove(s)
        except ValueError:
            pass

@app.websocket('/ws')
async def ws_endpoint(ws: WebSocket):
    await ws.accept()
    ws_clients.append(ws)
    try:
        client_host = ws.client.host if ws.client else 'unknown'
        print(f"[WS] client connected from {client_host}; active={len(ws_clients)}")
    except Exception:
        pass
    try:
        # On connect: send a brief status snapshot
        await ws.send_json({
            'type': 'welcome',
            'diags_count': len(diags),
            'ts': time.time()
        })
        while True:
            data = await ws.receive_json()
            msg_type = data.get('type')
            if msg_type == 'apply':
                # Route to existing apply logic
                prop = data.get('property')
                val = data.get('value') if 'value' in data else data.get('recommend')
                # Construct a fake Request with JSON body is non-trivial; call helper directly
                # Refactor apply logic into a small helper
                result = _apply_override_direct(prop, val)
                await ws.send_json({'type': 'apply_ack', **result})
            elif msg_type == 'recalculate':
                result = _recalculate_direct()
                await ws.send_json({'type': 'recalculate_ack', **result})
            else:
                await ws.send_json({'type': 'error', 'message': 'unknown_ws_message', 'received': data})
    except WebSocketDisconnect:
        # Client disconnected
        try:
            ws_clients.remove(ws)
        except ValueError:
            pass
        try:
            print(f"[WS] client disconnected; active={len(ws_clients)}")
        except Exception:
            pass
    except Exception as ex:
        try:
            await ws.send_json({'type': 'error', 'message': str(ex)})
        except Exception:
            pass
        try:
            ws_clients.remove(ws)
        except ValueError:
            pass
        try:
            print(f"[WS] client error: {ex}; active={len(ws_clients)}")
        except Exception:
            pass

def parse_bool(val):
    """Parse strategy's string booleans (true/false) to Python bool."""
    if isinstance(val, bool):
        return val
    if isinstance(val, str):
        return val.lower() in ('true', '1', 'yes')
    return bool(val)

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
BAR_CACHE_MAX = 1200  # adjustable; governs /bars/latest and in-memory preload
bar_cache: deque[Dict[str, Any]] = deque(maxlen=BAR_CACHE_MAX)

# --- Log entry cache for tracking actual strategy decisions ---
LOG_CACHE_MAX = 1000  # Keep last 1000 log entries (entry/exit/filter decisions)
log_cache: deque[Dict[str, Any]] = deque(maxlen=LOG_CACHE_MAX)

# Strategy log folder (CSV + .log) used by the bar report endpoint
# Path is relative to the Custom folder (two levels up from web/dashboard, then into strategy_logs)
LOG_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', 'strategy_logs'))

# --- Command queue for page -> strategy signals ---
COMMAND_QUEUE_MAX = 200
command_queue: deque[Dict[str, Any]] = deque(maxlen=COMMAND_QUEUE_MAX)
command_seq: int = 0
CMD_LOG_FILE = os.path.join(LOG_DIR, 'command_payloads.log')

def log_command_payload(data: Dict[str, Any]):
    """Append inbound command payloads to disk for traceability."""
    try:
        rec = {
            'ts': time.time(),
            'direction': data.get('direction'),
            'barIndex': data.get('barIndex'),
            'price': data.get('price'),
            'strength': data.get('strength'),
            'note': data.get('note'),
            'source': data.get('source'),
        }
        os.makedirs(LOG_DIR, exist_ok=True)
        with open(CMD_LOG_FILE, 'a', encoding='utf-8') as f:
            f.write(json.dumps(rec, ensure_ascii=False) + '\n')
    except Exception as ex:
        print('[CMD] payload file log error:', ex)

def enqueue_command(cmd: Dict[str, Any]) -> Dict[str, Any]:
    """Assign id/timestamp and enqueue a command for strategies to poll."""
    global command_seq
    command_seq += 1
    cmd['id'] = command_seq
    cmd['ts'] = time.time()
    command_queue.append(cmd)
    return cmd

def _normalize_bar(p: Dict[str, Any]) -> Dict[str, Any]:
    """Extract a normalized minimal bar record from a raw diag payload."""
    try:
        return {
            'barIndex': p.get('barIndex') or p.get('BarIndex'),
            'ts': p.get('receivedTs'),
            'localTime': p.get('localTime') or p.get('time'),
            'open': float(p.get('Open') or p.get('open') or 0.0),
            'high': float(p.get('High') or p.get('high') or 0.0),
            'low': float(p.get('Low') or p.get('low') or 0.0),
            'close': float(p.get('Close') or p.get('close') or 0.0),
            'fastGrad': float(p.get('FastGrad') or p.get('fastGrad') or 0.0),
            'fastGradDeg': float(p.get('FastGradDeg') or p.get('fastGradDeg') or 0.0),
            'slowGrad': float(p.get('SlowGrad') or p.get('slowGrad') or 0.0),
            'accel': float(p.get('Accel') or p.get('accel') or 0.0),
            'adx': float(p.get('ADX') or p.get('adx') or 0.0),
            'rsi': float(p.get('RSI') or p.get('rsi') or 0.0),
            'fastEMA': float(p.get('FastEMA') or p.get('fastEMA') or 0.0),
            'slowEMA': float(p.get('SlowEMA') or p.get('slowEMA') or 0.0),
            'gradStab': float(p.get('GradStab') or p.get('gradStab') or 0.0),
            'bandwidth': float(p.get('Bandwidth') or p.get('bandwidth') or 0.0),
            'unrealized': float(p.get('Unrealized') or p.get('unrealized') or 0.0),
            'trendSide': p.get('trendSide') or p.get('TrendSide') or None,
            # entry diagnostics (flat fields)
            'signalEligibleLong': bool(p.get('signalEligibleLong')),
            'signalEligibleShort': bool(p.get('signalEligibleShort')),
            'streakLong': int(p.get('streakLong') or 0),
            'streakShort': int(p.get('streakShort') or 0),
            'priceAboveEMAs': bool(p.get('priceAboveEMAs')),
            'priceBelowEMAs': bool(p.get('priceBelowEMAs')),
            'gradDirLongOk': bool(p.get('gradDirLongOk')),
            'gradDirShortOk': bool(p.get('gradDirShortOk')),
            'fastStrongForEntryLong': bool(p.get('fastStrongForEntryLong')),
            'fastStrongForEntryShort': bool(p.get('fastStrongForEntryShort')),
            'notOverextended': parse_bool(p.get('notOverextended')),
            'adxOk': parse_bool(p.get('adxOk')),
            'gradStabOk': parse_bool(p.get('gradStabOk')),
            'bandwidthOk': parse_bool(p.get('bandwidthOk')),
            'accelAlignOkLong': parse_bool(p.get('accelAlignOkLong')),
            'accelAlignOkShort': parse_bool(p.get('accelAlignOkShort')),
            'atrOk': parse_bool(p.get('atrOk')),
            'rsiOk': parse_bool(p.get('rsiOk')),
            'entryLongReady': bool(p.get('entryLongReady')),
            'entryShortReady': bool(p.get('entryShortReady')),
            # thresholds snapshot
            'entryGradThrLong': float(p.get('entryGradThrLong') or 0.0),
            'entryGradThrShort': float(p.get('entryGradThrShort') or 0.0),
            'maxEntryFastGradAbs': float(p.get('maxEntryFastGradAbs') or 0.0),
            'minAdxForEntry': float(p.get('minAdxForEntry') or 0.0),
            'maxGradientStabilityForEntry': float(p.get('maxGradientStabilityForEntry') or 0.0),
            'minBandwidthForEntry': float(p.get('minBandwidthForEntry') or 0.0),
            'maxBandwidthForEntry': float(p.get('maxBandwidthForEntry') or 0.0),
            'maxATRForEntry': float(p.get('maxATRForEntry') or 0.0),
            'minRSIForEntry': float(p.get('minRSIForEntry') or 0.0),
            'entryBarDelay': int(p.get('entryBarDelay') or 0),
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
    'MinEntryFastGradientAbs': (0.001, 1.0),
    'MinAdxForEntry': (8.0, 30.0),  # ADX bounds
    'RsiEntryFloor': (30.0, 70.0),
    'ValidationMinFastGradientAbs': (0.05, 0.20),  # exit threshold bounds
    'MinHoldBars': (2, 10),  # hold time bounds
    'MaxBandwidthForEntry': (0.05, 0.60),  # overextension cap
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
    'MaxEntryFastGradientAbs': 0.80,  # overextension cap - max absolute fast gradient for entry
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

@app.get('/filter_analysis.html', response_class=HTMLResponse)
@app.get('/filter_analysis', response_class=HTMLResponse)
def filter_analysis():
    # Serve the filter analysis page
    analysis_path = os.path.join(os.path.dirname(static_dir), 'filter_analysis.html')
    try:
        with open(analysis_path, 'r', encoding='utf-8') as f:
            return f.read()
    except Exception as e:
        return HTMLResponse(f'<h1>Filter Analysis Page Not Found</h1><p>Error: {e}</p>', status_code=404)

# BarsOnTheFlow-specific filter analysis page
@app.get('/botf_filter_analysis.html', response_class=HTMLResponse)
@app.get('/botf_filter_analysis', response_class=HTMLResponse)
def botf_filter_analysis():
    page_path = os.path.join(os.path.dirname(static_dir), 'botf_filter_analysis.html')
    try:
        with open(page_path, 'r', encoding='utf-8') as f:
            return f.read()
    except Exception as e:
        return HTMLResponse(f'<h1>BOTF Filter Analysis Page Not Found</h1><p>Error: {e}</p>', status_code=404)

@app.get('/bar_report.html', response_class=HTMLResponse)
@app.get('/bar_report', response_class=HTMLResponse)
def bar_report_page():
    page_path = os.path.join(os.path.dirname(static_dir), 'bar_report.html')
    try:
        with open(page_path, 'r', encoding='utf-8') as f:
            return f.read()
    except Exception as e:
        return HTMLResponse(f'<h1>Bar Report Page Not Found</h1><p>Error: {e}</p>', status_code=404)

@app.get('/barFlowReport.html', response_class=HTMLResponse)
@app.get('/barFlowReport', response_class=HTMLResponse)
def bar_flow_report_page():
    page_path = os.path.join(os.path.dirname(os.path.dirname(static_dir)), 'barFlowReport.html')
    try:
        with open(page_path, 'r', encoding='utf-8') as f:
            return f.read()
    except Exception as e:
        return HTMLResponse(f'<h1>Bar Flow Report Page Not Found</h1><p>Error: {e}</p>', status_code=404)

@app.get('/candles.html', response_class=HTMLResponse)
@app.get('/candles', response_class=HTMLResponse)
def candles_page():
    page_path = os.path.join(os.path.dirname(static_dir), 'candles.html')
    print(f"[CANDLES] Attempting to load from: {page_path}")
    print(f"[CANDLES] File exists: {os.path.exists(page_path)}")
    try:
        with open(page_path, 'r', encoding='utf-8') as f:
            content = f.read()
            print(f"[CANDLES] Successfully loaded {len(content)} bytes")
            return content
    except Exception as e:
        print(f"[CANDLES] Error loading file: {e}")
        return HTMLResponse(f'<h1>Candles Page Not Found</h1><p>Error: {e}</p><p>Tried path: {page_path}</p>', status_code=404)

@app.get('/ping')
def ping():
    return JSONResponse({'status': 'ok', 'time': time.time()})

@app.get('/api/latest-log')
def api_latest_log():
    """Return the path to the most recent BarsOnTheFlow CSV log file."""
    csv_path = _pick_recent_csv()
    if not csv_path or not os.path.isfile(csv_path):
        return JSONResponse({'status': 'error', 'message': 'No log files found'}, status_code=404)
    return JSONResponse({'status': 'ok', 'path': csv_path, 'filename': os.path.basename(csv_path)})

@app.get('/api/bar-data')
def api_bar_data(bar: int):
    """Return detailed data for a specific bar from the latest log file."""
    csv_path = _pick_recent_csv()
    print(f'[API] bar-data request for bar={bar}, csv_path={csv_path}')
    if not csv_path or not os.path.isfile(csv_path):
        print(f'[API] bar-data: No CSV file found')
        return JSONResponse({'status': 'error', 'message': 'No log files found'}, status_code=404)
    
    try:
        with open(csv_path, 'r', encoding='utf-8') as f:
            reader = csv.DictReader(f)
            row_count = 0
            for row in reader:
                row_count += 1
                if int(row.get('bar', -1)) == bar:
                    print(f'[API] bar-data: Found bar {bar} at row {row_count}')
                    # Convert boolean string values to actual booleans
                    bar_data = dict(row)
                    for key in ['allowLongThisBar', 'allowShortThisBar', 'trendUpAtDecision', 'trendDownAtDecision', 'pendingShortFromGood', 'pendingLongFromBad']:
                        if key in bar_data:
                            bar_data[key] = bar_data[key].lower() == 'true'
                    return JSONResponse(bar_data)
        
        print(f'[API] bar-data: Bar {bar} not found in log (scanned {row_count} rows)')
        return JSONResponse({'status': 'error', 'message': f'Bar {bar} not found in log'}, status_code=404)
    except Exception as ex:
        print(f'[API] bar-data error: {ex}')
        import traceback
        traceback.print_exc()
        return JSONResponse({'status': 'error', 'message': str(ex)}, status_code=500)

@app.get('/api/strategy-params')
def api_strategy_params():
    """Return strategy parameters from the latest params JSON file."""
    csv_path = _pick_recent_csv()
    if not csv_path or not os.path.isfile(csv_path):
        return JSONResponse({'status': 'error', 'message': 'No log files found'}, status_code=404)
    
    # Look for matching params file (same basename but with _params.json suffix)
    base_path = os.path.splitext(csv_path)[0]
    params_path = base_path + '_params.json'
    
    if not os.path.isfile(params_path):
        return JSONResponse({'status': 'error', 'message': 'No parameters file found'}, status_code=404)
    
    try:
        with open(params_path, 'r', encoding='utf-8') as f:
            params = json.load(f)
            return JSONResponse(params)
    except Exception as ex:
        print(f'[API] strategy-params error: {ex}')
        return JSONResponse({'status': 'error', 'message': str(ex)}, status_code=500)

@app.get('/api/strategy-state')
def api_strategy_state():
    """Return current strategy state from the live state JSON file.
    This allows external tools (like Copilot) to query real-time strategy state
    including contracts, position, intended position, and all key parameters.
    """
    state_path = os.path.join(LOG_DIR, '..', 'strategy_state', 'BarsOnTheFlow_state.json')
    state_path = os.path.normpath(state_path)
    
    if not os.path.isfile(state_path):
        return JSONResponse({
            'status': 'offline',
            'message': 'Strategy state file not found - strategy may not be running',
            'expectedPath': state_path
        }, status_code=404)
    
    try:
        # Check file age - if older than 5 minutes, strategy may have stopped
        file_age = time.time() - os.path.getmtime(state_path)
        
        with open(state_path, 'r', encoding='utf-8') as f:
            state = json.load(f)
        
        state['_fileAgeSeconds'] = round(file_age, 1)
        state['_isStale'] = file_age > 300  # Stale if older than 5 minutes
        
        return JSONResponse(state)
    except Exception as ex:
        print(f'[API] strategy-state error: {ex}')
        return JSONResponse({'status': 'error', 'message': str(ex)}, status_code=500)

# ============================================================================
# VOLATILITY / DYNAMIC STOP LOSS API
# ============================================================================

VOLATILITY_DB_PATH = os.path.join(os.path.dirname(__file__), 'volatility.db')

@app.get('/api/volatility/recommended-stop')
def api_volatility_recommended_stop(hour: int = None, volume: int = 0, symbol: str = 'MNQ'):
    """Get recommended stop loss in ticks based on hour and current volume.
    
    Query params:
        hour: Hour of day (0-23 ET). If not provided, uses current hour.
        volume: Current bar volume for volume-adjusted stop
        symbol: Trading symbol (default MNQ)
    
    Returns:
        recommended_stop_ticks: Recommended stop loss in ticks
        avg_bar_range: Average bar range for this hour (in points)
        avg_volume: Average volume for this hour
        volume_condition: LOW/NORMAL/HIGH based on current vs average
        confidence: LOW/MEDIUM/HIGH based on sample count
    """
    try:
        if hour is None:
            hour = datetime.now().hour
        
        conn = sqlite3.connect(VOLATILITY_DB_PATH)
        cursor = conn.cursor()
        
        # Get stats for this hour
        cursor.execute('''
            SELECT avg_bar_range, avg_volume, avg_range_per_1k_volume, sample_count
            FROM volatility_stats
            WHERE hour_of_day = ? AND symbol = ? AND day_of_week IS NULL
        ''', (hour, symbol))
        
        row = cursor.fetchone()
        conn.close()
        
        if not row or row[3] < 10:  # Need at least 10 samples
            return JSONResponse({
                'recommended_stop_ticks': 16,  # Default 4 points
                'avg_bar_range': 0,
                'avg_volume': 0,
                'volume_condition': 'UNKNOWN',
                'confidence': 'LOW',
                'message': f'Insufficient data for hour {hour} ({row[3] if row else 0} samples)'
            })
        
        avg_range, avg_volume, avg_range_per_vol, sample_count = row
        
        # Determine volume condition
        if volume > 0 and avg_volume > 0:
            volume_ratio = volume / avg_volume
            if volume_ratio < 0.7:
                volume_condition = 'LOW'
                volume_multiplier = 0.85  # Tighter stops in low volume
            elif volume_ratio > 1.3:
                volume_condition = 'HIGH'
                volume_multiplier = 1.25  # Wider stops in high volume
            else:
                volume_condition = 'NORMAL'
                volume_multiplier = 1.0
        else:
            volume_condition = 'NORMAL'
            volume_multiplier = 1.0
        
        # Calculate recommended stop
        # Base: average bar range * 1.2 buffer * volume adjustment
        base_stop_points = avg_range * 1.2 * volume_multiplier
        recommended_ticks = int(base_stop_points * 4)  # 4 ticks per point
        
        # Confidence based on sample count
        if sample_count >= 100:
            confidence = 'HIGH'
        elif sample_count >= 30:
            confidence = 'MEDIUM'
        else:
            confidence = 'LOW'
        
        # Clamp to reasonable range (2-20 points = 8-80 ticks)
        recommended_ticks = max(8, min(80, recommended_ticks))
        
        return JSONResponse({
            'recommended_stop_ticks': recommended_ticks,
            'avg_bar_range': round(avg_range, 2),
            'avg_volume': int(avg_volume),
            'volume_condition': volume_condition,
            'confidence': confidence,
            'sample_count': sample_count,
            'hour': hour
        })
        
    except Exception as ex:
        print(f'[API] volatility recommended-stop error: {ex}')
        return JSONResponse({'status': 'error', 'message': str(ex)}, status_code=500)

@app.post('/api/volatility/record-bar')
async def api_volatility_record_bar(request: Request):
    """Record a bar sample for volatility tracking.
    
    POST body:
        timestamp: Bar timestamp (string)
        bar_index: Bar number
        symbol: Trading symbol
        open, high, low, close: Bar OHLC prices
        volume: Bar volume
        direction: LONG/SHORT/FLAT
        in_trade: Boolean
        trade_result_ticks: P/L in ticks if exiting (optional)
    """
    try:
        data = await request.json()
        
        conn = sqlite3.connect(VOLATILITY_DB_PATH)
        cursor = conn.cursor()
        
        timestamp = data['timestamp']
        bar_index = data.get('bar_index', 0)
        symbol = data.get('symbol', 'MNQ')
        open_p = float(data['open'])
        high_p = float(data['high'])
        low_p = float(data['low'])
        close_p = float(data['close'])
        volume = int(data['volume'])
        direction = data.get('direction', 'FLAT')
        in_trade = data.get('in_trade', False)
        trade_result = data.get('trade_result_ticks')
        
        # Parse timestamp
        try:
            dt = datetime.strptime(timestamp.split('.')[0], '%Y-%m-%d %H:%M:%S')
        except:
            dt = datetime.now()
        
        hour_of_day = dt.hour
        day_of_week = dt.weekday()
        
        # Calculate metrics
        bar_range = high_p - low_p
        body_size = abs(close_p - open_p)
        
        if close_p >= open_p:
            upper_wick = high_p - close_p
            lower_wick = open_p - low_p
        else:
            upper_wick = high_p - open_p
            lower_wick = close_p - low_p
        
        range_per_1k_volume = (bar_range / (volume / 1000)) if volume > 0 else 0
        
        cursor.execute('''
            INSERT INTO bar_samples (
                timestamp, bar_index, symbol, hour_of_day, day_of_week,
                open_price, high_price, low_price, close_price, volume,
                bar_range, body_size, upper_wick, lower_wick,
                range_per_1k_volume, direction, in_trade, trade_result_ticks
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ''', (
            timestamp, bar_index, symbol, hour_of_day, day_of_week,
            open_p, high_p, low_p, close_p, volume,
            bar_range, body_size, upper_wick, lower_wick,
            range_per_1k_volume, direction, 1 if in_trade else 0, trade_result
        ))
        
        conn.commit()
        conn.close()
        
        return JSONResponse({'status': 'ok', 'bar_index': bar_index, 'hour': hour_of_day})
        
    except Exception as ex:
        print(f'[API] volatility record-bar error: {ex}')
        return JSONResponse({'status': 'error', 'message': str(ex)}, status_code=500)

@app.get('/api/volatility/stats')
def api_volatility_stats(symbol: str = 'MNQ'):
    """Get volatility statistics by hour for analysis."""
    try:
        conn = sqlite3.connect(VOLATILITY_DB_PATH)
        cursor = conn.cursor()
        
        cursor.execute('''
            SELECT hour_of_day, sample_count, avg_bar_range, avg_volume, 
                   avg_range_per_1k_volume, min_bar_range, max_bar_range
            FROM volatility_stats
            WHERE symbol = ? AND day_of_week IS NULL
            ORDER BY hour_of_day
        ''', (symbol,))
        
        rows = cursor.fetchall()
        conn.close()
        
        stats = []
        for row in rows:
            stats.append({
                'hour': row[0],
                'sample_count': row[1],
                'avg_bar_range': round(row[2], 2) if row[2] else 0,
                'avg_volume': int(row[3]) if row[3] else 0,
                'avg_range_per_1k_vol': round(row[4], 4) if row[4] else 0,
                'min_bar_range': round(row[5], 2) if row[5] else 0,
                'max_bar_range': round(row[6], 2) if row[6] else 0
            })
        
        return JSONResponse({'status': 'ok', 'stats': stats})
        
    except Exception as ex:
        print(f'[API] volatility stats error: {ex}')
        return JSONResponse({'status': 'error', 'message': str(ex)}, status_code=500)

@app.post('/api/volatility/update-aggregates')
def api_volatility_update_aggregates(symbol: str = 'MNQ'):
    """Recalculate aggregated volatility statistics from bar samples."""
    try:
        conn = sqlite3.connect(VOLATILITY_DB_PATH)
        cursor = conn.cursor()
        
        # Calculate stats for each hour
        for hour in range(24):
            cursor.execute('''
                SELECT 
                    AVG(volume) as avg_vol,
                    MIN(volume) as min_vol,
                    MAX(volume) as max_vol,
                    AVG(bar_range) as avg_range,
                    MIN(bar_range) as min_range,
                    MAX(bar_range) as max_range,
                    AVG(range_per_1k_volume) as avg_range_per_1k,
                    COUNT(*) as sample_count,
                    MIN(timestamp) as first_sample,
                    MAX(timestamp) as last_sample
                FROM bar_samples
                WHERE hour_of_day = ? AND symbol = ?
            ''', (hour, symbol))
            
            row = cursor.fetchone()
            if row and row[7] > 0:  # sample_count > 0
                cursor.execute('''
                    INSERT OR REPLACE INTO volatility_stats (
                        hour_of_day, day_of_week, symbol,
                        avg_volume, min_volume, max_volume,
                        avg_bar_range, min_bar_range, max_bar_range,
                        avg_range_per_1k_volume,
                        sample_count, first_sample_time, last_sample_time, last_updated
                    ) VALUES (?, NULL, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, datetime('now'))
                ''', (
                    hour, symbol,
                    row[0], row[1], row[2],  # volume stats
                    row[3], row[4], row[5],  # range stats
                    row[6],                   # range per 1k volume
                    row[7], row[8], row[9]   # counts and times
                ))
        
        conn.commit()
        conn.close()
        
        return JSONResponse({'status': 'ok', 'message': 'Aggregates updated'})
        
    except Exception as ex:
        print(f'[API] volatility update-aggregates error: {ex}')
        return JSONResponse({'status': 'error', 'message': str(ex)}, status_code=500)

# ============================================================================
# END VOLATILITY API
# ============================================================================

@app.post('/recalculate')
def recalculate():
    """Trigger a lightweight recalculation cycle: recompute effective params,
    run auto-apply evaluator once, and return current state for the UI.
    """
    try:
        effective = { k: float(active_overrides.get(k, v)) for k, v in DEFAULT_PARAMS.items() }
        if AUTO_APPLY_ENABLED:
            evaluate_auto_apply(time.time())
            # Refresh effective after potential changes
            effective = { k: float(active_overrides.get(k, v)) for k, v in DEFAULT_PARAMS.items() }
        return JSONResponse({
            'status': 'ok',
            'effectiveParams': effective,
            'overrides': active_overrides,
            'streaks': property_streaks,
            'recentAutoApplyEvents': auto_apply_events_cache[-10:]
        })
    except Exception as ex:
        return JSONResponse({'status': 'error', 'message': str(ex)}, status_code=500)
    
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
                'fastGradDeg': d.get('fastGradDeg') if d.get('fastGradDeg') is not None else d.get('FastGradDeg'),
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

            # Broadcast compact diagnostic to WebSocket clients
            try:
                compact = {
                    'receivedAt': p.get('receivedTs'),
                    'time': p.get('localTime') or p.get('time'),
                    'barIndex': p.get('barIndex') or p.get('BarIndex'),
                    # Price data used by candles.html live feed
                    'open': p.get('open') if p.get('open') is not None else p.get('Open'),
                    'high': p.get('high') if p.get('high') is not None else p.get('High'),
                    'low': p.get('low') if p.get('low') is not None else p.get('Low'),
                    'close': p.get('close') if p.get('close') is not None else p.get('Close'),
                    'volume': p.get('volume') if p.get('volume') is not None else p.get('Volume'),

                    # Strategy diagnostics for dashboard chips
                    'fastGrad': p.get('fastGrad') if p.get('fastGrad') is not None else p.get('FastGrad'),
                    'fastGradDeg': p.get('fastGradDeg') if p.get('fastGradDeg') is not None else p.get('FastGradDeg'),
                    'slowGrad': p.get('slowGrad') if p.get('slowGrad') is not None else p.get('SlowGrad'),
                    'accel': p.get('accel') if p.get('accel') is not None else p.get('Accel'),
                    'adx': p.get('adx') if p.get('adx') is not None else p.get('ADX'),
                    'fastEMA': p.get('fastEMA') if p.get('fastEMA') is not None else p.get('FastEMA'),
                    'slowEMA': p.get('slowEMA') if p.get('slowEMA') is not None else p.get('SlowEMA'),
                    'atr': p.get('atr') if p.get('atr') is not None else p.get('ATR'),
                    'rsi': p.get('rsi') if p.get('rsi') is not None else p.get('RSI'),
                    'signal': p.get('signal') if p.get('signal') is not None else p.get('Signal'),
                    'blockersLong': p.get('blockersLong') or [],
                    'blockersShort': p.get('blockersShort') or [],
                    'gradStab': p.get('gradStab') or p.get('GradStab'),
                    'bandwidth': p.get('bandwidth') or p.get('Bandwidth'),
                    # Timing fields if present
                    'barsInSignal': p.get('barsInSignal'),
                    'signalStartBar': p.get('signalStartBar'),
                    'entryDelayMet': p.get('entryDelayMet'),
                    'canEnterLong': p.get('canEnterLong'),
                    'canEnterShort': p.get('canEnterShort'),
                    'myPosition': p.get('myPosition'),
                    'inWeakReversalDelay': p.get('inWeakReversalDelay'),
                    # Threshold fields for chip display
                    'entryGradThrLong': p.get('entryGradThrLong'),
                    'entryGradThrShort': p.get('entryGradThrShort'),
                    'minAdxForEntry': p.get('minAdxForEntry'),
                    'maxGradientStabilityForEntry': p.get('maxGradientStabilityForEntry'),
                    'minBandwidthForEntry': p.get('minBandwidthForEntry'),
                    'maxBandwidthForEntry': p.get('maxBandwidthForEntry'),
                    'maxATRForEntry': p.get('maxATRForEntry'),
                    'minRSIForEntry': p.get('minRSIForEntry'),
                    'maxEntryFastGradientAbs': p.get('maxEntryFastGradientAbs') or p.get('MaxEntryFastGradientAbs'),
                    # Condition result booleans
                    'adxOk': p.get('adxOk'),
                    'gradStabOk': p.get('gradStabOk'),
                    'bandwidthOk': p.get('bandwidthOk'),
                    'atrOk': p.get('atrOk'),
                    'rsiOk': p.get('rsiOk'),
                    'accelAlignOkLong': p.get('accelAlignOkLong'),
                    'accelAlignOkShort': p.get('accelAlignOkShort'),
                    'fastStrongForEntryLong': p.get('fastStrongForEntryLong'),
                    'fastStrongForEntryShort': p.get('fastStrongForEntryShort'),
                    'notOverextended': p.get('notOverextended'),
                    'priceAboveEMAs': p.get('priceAboveEMAs'),
                    'priceBelowEMAs': p.get('priceBelowEMAs'),
                    'gradDirLongOk': p.get('gradDirLongOk'),
                    'gradDirShortOk': p.get('gradDirShortOk'),
                    'signalEligibleLong': p.get('signalEligibleLong'),
                    'signalEligibleShort': p.get('signalEligibleShort'),
                    'streakLong': p.get('streakLong'),
                    'streakShort': p.get('streakShort'),
                    'entryBarDelay': p.get('entryBarDelay')
                }
                await ws_broadcast({'type': 'diag', 'data': compact})
            except Exception as _ex_ws:
                # Non-fatal
                pass
        except Exception as ex:
            print(f"[DIAG] Error processing item: {ex}")
    
    return JSONResponse({"status": "ok", "count": len(items)})

# --- Small helpers to re-use apply/recalculate logic for WebSocket ---
def _recalculate_direct() -> Dict[str, Any]:
    try:
        effective = { k: float(active_overrides.get(k, v)) for k, v in DEFAULT_PARAMS.items() }
        if AUTO_APPLY_ENABLED:
            evaluate_auto_apply(time.time())
            effective = { k: float(active_overrides.get(k, v)) for k, v in DEFAULT_PARAMS.items() }
        return {
            'status': 'ok',
            'effectiveParams': effective,
            'overrides': active_overrides,
            'streaks': property_streaks,
            'recentAutoApplyEvents': auto_apply_events_cache[-10:]
        }
    except Exception as ex:
        return {'status': 'error', 'message': str(ex)}

def _apply_override_direct(prop: Any, val: Any) -> Dict[str, Any]:
    try:
        if prop is None:
            return {'error': 'missing_property', 'message': 'Provide property'}
        if val is None:
            return {'error': 'missing_value', 'message': 'Provide value'}
        alias_map = {
            'minadxforentry': 'MinAdxForEntry',
            'minAdxForEntry': 'MinAdxForEntry',
            'rsientryfloor': 'RsiEntryFloor',
            'RsiEntryFloor': 'RsiEntryFloor',
            'rsishortfloor': 'RsiEntryFloor',
            'RsiShortFloor': 'RsiEntryFloor',
            'rsilongfloor': 'RsiEntryFloor',
            'RsiLongFloor': 'RsiEntryFloor',
            'fastgrad': 'MinEntryFastGradientAbs',
            'FastGrad': 'MinEntryFastGradientAbs',
            'overextend': 'MaxBandwidthForEntry',
            'Overextend': 'MaxBandwidthForEntry',
            'maxbandwidthforentry': 'MaxBandwidthForEntry',
            'MaxBandwidthForEntry': 'MaxBandwidthForEntry',
            'minentryfastgradientabs': 'MinEntryFastGradientAbs',
            'MinEntryFastGradientAbs': 'MinEntryFastGradientAbs',
            'maxentryfastgradientabs': 'MaxEntryFastGradientAbs',
            'MaxEntryFastGradientAbs': 'MaxEntryFastGradientAbs',
            'validationminfastgradientabs': 'ValidationMinFastGradientAbs',
            'ValidationMinFastGradientAbs': 'ValidationMinFastGradientAbs',
            'minholdbars': 'MinHoldBars',
            'MinHoldBars': 'MinHoldBars',
            'maxgradientstabilityforentry': 'MaxGradientStabilityForEntry',
            'MaxGradientStabilityForEntry': 'MaxGradientStabilityForEntry'
        }
        prop_n = alias_map.get(prop, alias_map.get(str(prop).lower(), prop))
        known_props = set(DEFAULT_PARAMS.keys())
        if prop_n not in known_props:
            return {'error': 'unknown_property', 'property': prop_n, 'known': sorted(list(known_props))}
        try:
            val_f = float(val)
        except Exception:
            return {'error': 'invalid_value', 'message': f'Value for {prop_n} must be numeric', 'received': val}
        bounds = AUTO_APPLY_BOUNDS.get(prop_n)
        if bounds:
            lo, hi = bounds
            if not (lo <= val_f <= hi):
                return {'error': 'out_of_bounds', 'property': prop_n, 'value': val_f, 'bounds': {'min': lo, 'max': hi}}
        prev = active_overrides.get(prop_n)
        active_overrides[prop_n] = val_f
        print(f"[APPLY/WS] Override set {prop_n}={val} (prev={prev})")
        try:
            if USE_SQLITE:
                db_exec(
                    "INSERT INTO overrides_history (ts, property, oldValue, newValue, source) VALUES (?,?,?,?,?)",
                    (time.time(), prop_n, float(prev) if prev is not None else None, val_f, 'ws')
                )
        except Exception:
            pass
        save_overrides_to_disk()
        effective = { k: float(active_overrides.get(k, v)) for k, v in DEFAULT_PARAMS.items() }
        # Also broadcast new effective params to all clients
        try:
            import asyncio
            asyncio.create_task(ws_broadcast({'type': 'overrides', 'effectiveParams': effective, 'overrides': active_overrides}))
        except Exception:
            pass
        return {'status': 'ok', 'property': prop_n, 'value': val_f, 'overrides': active_overrides, 'effectiveParams': effective, 'defaultParams': DEFAULT_PARAMS}
    except Exception as ex:
        return {'status': 'error', 'message': str(ex)}

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
    # Keep payload ordered by barIndex for predictable rendering on refresh
    bars = sorted(list(bar_cache)[-limit:], key=lambda b: b.get('barIndex') or 0)
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

@app.post('/log')
async def post_log(request: Request):
    """Receive log entries from strategy (entry/exit/filter decisions)."""
    try:
        payload = await request.json()
        log_entry = {
            'ts': time.time(),
            'barIndex': payload.get('barIndex'),
            'action': payload.get('action'),  # 'ENTRY', 'EXIT', 'FILTER_BLOCK'
            'direction': payload.get('direction'),  # 'LONG', 'SHORT'
            'reason': payload.get('reason', ''),
            'data': payload.get('data', {})  # Additional context
        }
        log_cache.append(log_entry)
        return JSONResponse({'status': 'ok', 'cached': len(log_cache)})
    except Exception as e:
        print(f'[LOG] Error: {e}')
        return JSONResponse({'status': 'error', 'message': str(e)}, status_code=500)

@app.get('/logs/recent')
def get_recent_logs(limit: int = 100):
    """Get recent log entries."""
    limit = max(1, min(limit, LOG_CACHE_MAX))
    logs = list(log_cache)[-limit:]
    return JSONResponse({'logs': logs, 'count': len(logs)})

@app.post('/logs/save-analysis')
async def save_analysis(request: Request):
    """Persist a client-submitted analysis JSON into strategy_logs."""
    try:
        data = await request.json()
    except Exception:
        return JSONResponse({'status': 'error', 'message': 'Invalid JSON payload'}, status_code=400)
# --- Command bridge: page -> strategy ---


    try:
        os.makedirs(LOG_DIR, exist_ok=True)
        ts = datetime.utcnow().strftime('%Y%m%d_%H%M%S')
        fname = f"analysis_{ts}.json"
        fpath = os.path.join(LOG_DIR, fname)
        with open(fpath, 'w', encoding='utf-8') as f:
            json.dump(data, f, indent=2)
        return JSONResponse({'status': 'ok', 'file': fname, 'path': fpath})
    except Exception as ex:
        return JSONResponse({'status': 'error', 'message': str(ex)}, status_code=500)

@app.post('/logs/clear')
async def clear_logs():
    """Clear the log cache (useful to reset position tracking)."""
    try:
        log_cache.clear()
        print('[LOG] Cache cleared')
        return JSONResponse({'status': 'ok', 'message': 'Log cache cleared'})
    except Exception as e:
        print(f'[LOG] Clear error: {e}')
        return JSONResponse({'status': 'error', 'message': str(e)}, status_code=500)

# --- Command bridge: page -> strategy ---
@app.post('/commands/trend')
async def commands_trend(request: Request):
    """Enqueue a trend command from the UI for strategies to poll."""
    try:
        data = await request.json()
    except Exception:
        return JSONResponse({'status': 'error', 'message': 'invalid_json'}, status_code=400)

    direction = str(data.get('direction', '')).upper()
    if direction not in ('UP', 'DOWN', 'FLAT'):
        return JSONResponse({'status': 'error', 'message': 'direction must be UP, DOWN, or FLAT'}, status_code=400)

    # Lightweight payload log so we can trace exactly what the UI sent
    try:
        print('[CMD] incoming payload:', json.dumps(data))
        log_command_payload(data)
    except Exception:
        pass

    cmd = enqueue_command({
        'type': 'trend',
        'direction': direction,
        'barIndex': data.get('barIndex'),
        'price': data.get('price'),
        'strength': data.get('strength'),
        'note': data.get('note'),
        'source': data.get('source', 'dashboard')
    })
    try:
        print(f"[CMD] queued trend #{cmd['id']} dir={direction} bar={cmd.get('barIndex')} price={cmd.get('price')}")
    except Exception:
        pass
    return JSONResponse({'status': 'ok', 'command': cmd, 'queued': len(command_queue)})

@app.get('/commands/next')
def commands_next():
    """Pop the next pending command for the strategy to consume."""
    if not command_queue:
        return JSONResponse({'status': 'empty', 'remaining': 0})
    cmd = command_queue.popleft()
    return JSONResponse({'status': 'ok', 'command': cmd, 'remaining': len(command_queue)})

@app.get('/logs/latest-csv')
def get_latest_csv():
    """Serve the most recent strategy CSV from disk for the candles viewer."""

    csv_path = _pick_recent_csv()
    if not csv_path or not os.path.isfile(csv_path):
        return JSONResponse({'status': 'error', 'message': 'No CSV files found in strategy_logs'}, status_code=404)

    try:
        with open(csv_path, 'r', encoding='utf-8') as f:
            content = f.read()
        headers = {'X-Log-Filename': os.path.basename(csv_path)}
        return PlainTextResponse(content, media_type='text/csv', headers=headers)
    except Exception as ex:
        print(f'[LOG] latest-csv read error: {ex}')
        return JSONResponse({'status': 'error', 'message': str(ex)}, status_code=500)

def _pick_latest_file(ext: str, exclude_substrings: List[str] | None = None) -> str | None:
    if not os.path.isdir(LOG_DIR):
        return None
    exclude_substrings = exclude_substrings or []
    newest_path = None
    newest_mtime = -1.0
    for name in os.listdir(LOG_DIR):
        lower = name.lower()
        if not lower.endswith(ext.lower()):
            continue
        if any(sub.lower() in lower for sub in exclude_substrings):
            continue
        path = os.path.join(LOG_DIR, name)
        try:
            mtime = os.path.getmtime(path)
        except OSError:
            continue
        if mtime > newest_mtime:
            newest_mtime = mtime
            newest_path = path
    return newest_path

def _pick_recent_csv() -> str | None:
    # Look specifically for BarsOnTheFlow strategy CSV files (exclude Opportunities and OutputWindow logs)
    if not os.path.isdir(LOG_DIR):
        return None
    newest_path = None
    newest_mtime = -1.0
    for name in os.listdir(LOG_DIR):
        lower = name.lower()
        # Only select BarsOnTheFlow CSV files (not NinjaTrader Grid exports or other CSVs)
        if not lower.startswith('barsontheflow') or not lower.endswith('.csv'):
            continue
        # Exclude the opportunity log and output window log
        if 'opportunities' in lower or 'outputwindow' in lower:
            continue
        path = os.path.join(LOG_DIR, name)
        try:
            mtime = os.path.getmtime(path)
        except OSError:
            continue
        if mtime > newest_mtime:
            newest_mtime = mtime
            newest_path = path
    return newest_path

def _pick_recent_log() -> str | None:
    return _pick_latest_file('.log')

def _load_csv_rows_for_bars(csv_path: str, bars: List[int]) -> Dict[int, List[Dict[str, Any]]]:
    rows_by_bar: Dict[int, List[Dict[str, Any]]] = {b: [] for b in bars}
    keep_keys = ['Timestamp', 'Bar', 'PrevSignal', 'NewSignal', 'MyPosition', 'Action', 'Notes', 'ActualPosition', 'EntryBar', 'EntryPrice']
    targets = set(bars)
    try:
        with open(csv_path, newline='', encoding='utf-8') as f:
            reader = csv.DictReader(f)
            for r in reader:
                try:
                    bar_val = r.get('Bar')
                    if bar_val is None:
                        continue
                    bar_int = int(float(bar_val))
                except Exception:
                    continue
                if bar_int not in targets:
                    continue
                filtered = {k: r.get(k) for k in keep_keys if k in r}
                filtered['Bar'] = bar_int
                rows_by_bar.setdefault(bar_int, []).append(filtered)
    except Exception as ex:
        print('[BAR-REPORT] CSV read error:', ex)
    return rows_by_bar

def _extract_skip_reason(rows: List[Dict[str, Any]]) -> tuple[str | None, int | None]:
    for r in rows:
        action = (r.get('Action') or '').upper()
        notes = r.get('Notes') or ''
        bar_idx = r.get('Bar') if isinstance(r.get('Bar'), int) else None
        if 'CANCEL' in action or 'SUPPRESS' in action or 'BLOCK' in action:
            return notes or action, bar_idx
    if rows:
        first_notes = rows[0].get('Notes')
        bar_idx = rows[0].get('Bar') if isinstance(rows[0].get('Bar'), int) else None
        return (first_notes if first_notes else None, bar_idx)
    return (None, None)

def _log_context_for_bar(log_path: str, bar: int, window: int = 3, max_hits: int = 3) -> List[Dict[str, Any]]:
    if not log_path or not os.path.isfile(log_path):
        return []
    try:
        with open(log_path, 'r', encoding='utf-8', errors='ignore') as f:
            lines = f.read().splitlines()
    except Exception as ex:
        print('[BAR-REPORT] Log read error:', ex)
        return []
    hits: List[Dict[str, Any]] = []
    pattern = re.compile(rf'\bBar[:= ]{bar}\b')
    for idx, line in enumerate(lines, start=1):
        if not pattern.search(line):
            continue
        start = max(0, idx - 1 - window)
        end = min(len(lines), idx + window)
        hits.append({'line': idx, 'context': lines[start:end]})
        if len(hits) >= max_hits:
            break
    return hits

@app.get('/bar-report')
def bar_report_api(bar: int, csvFile: str | None = None, logFile: str | None = None):
    result: Dict[str, Any] = {'bar': bar}

    def _resolve(path_val: str | None) -> str | None:
        if not path_val:
            return None
        return path_val if os.path.isabs(path_val) else os.path.join(LOG_DIR, path_val)

    csv_path = _resolve(csvFile) if csvFile else _pick_recent_csv()
    if csv_path and os.path.isfile(csv_path):
        bars_to_fetch = [bar, bar - 1, bar - 2]
        rows_map = _load_csv_rows_for_bars(csv_path, bars_to_fetch)
        result['csvRows'] = rows_map.get(bar, [])
        result['csvRowsPrev1'] = rows_map.get(bar - 1, [])
        result['csvRowsPrev2'] = rows_map.get(bar - 2, [])

        reason, src_bar = _extract_skip_reason(result['csvRows'])
        if not reason:
            reason, src_bar = _extract_skip_reason(result['csvRowsPrev1'])
        if not reason:
            reason, src_bar = _extract_skip_reason(result['csvRowsPrev2'])
        result['skipReason'] = reason
        result['skipReasonSourceBar'] = src_bar
        result['csvFile'] = os.path.basename(csv_path)
    else:
        result['csvRows'] = []
        result['csvRowsPrev1'] = []
        result['csvRowsPrev2'] = []
        if csvFile:
            result['csvError'] = 'csv_not_found'

    log_path = _resolve(logFile) if logFile else _pick_recent_log()
    if log_path and os.path.isfile(log_path):
        result['logHits'] = _log_context_for_bar(log_path, bar)
        result['logFile'] = os.path.basename(log_path)
    else:
        result['logHits'] = []
        if logFile:
            result['logError'] = 'log_not_found'

    return JSONResponse(result)

@app.get('/trends/segments')
def get_trend_segments(limit: int = 20):
    """Get recent trend segments with start bar."""
    limit = max(1, min(limit, 100))
    segments = trend_segments[-limit:] if trend_segments else []
    current = current_trend if current_trend.get('side') else None
    return JSONResponse({'segments': segments, 'current': current, 'count': len(segments)})

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

@app.get('/bars/detail/{barIndex}')
def get_bar_detail(barIndex: int):
    """Get comprehensive bar details including metrics and entry decision diagnostics from logs."""
    # Find bar in cache
    bar_data = next((b for b in bar_cache if b.get('barIndex') == barIndex), None)
    if not bar_data:
        return JSONResponse({'status': 'error', 'message': f'Bar {barIndex} not found in cache'}, status_code=404)
    
    # Find related log entries (entry decisions, filter blocks, etc.)
    related_logs = [log for log in log_cache if log.get('barIndex') == barIndex]
    
    # Build comprehensive response
    result = {
        'barIndex': barIndex,
        'bar': bar_data,
        'logs': related_logs,
        'entryAttempt': None,
        'filterBlocks': []
    }
    
    # Extract entry decision details
    for log in related_logs:
        action = log.get('action', '')
        if action in ['ENTRY_DECISION', 'ENTRY', 'ENTRY_FILTER_BLOCKED']:
            result['entryAttempt'] = {
                'action': action,
                'direction': log.get('direction'),
                'reason': log.get('reason', ''),
                'data': log.get('data', {})
            }
            if action == 'ENTRY_FILTER_BLOCKED':
                result['filterBlocks'].append({
                    'direction': log.get('direction'),
                    'reason': log.get('reason', ''),
                    'filters': log.get('data', {})
                })
    
    return JSONResponse(result)

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
        return JSONResponse({'error': 'invalid_json', 'message': 'Body must be valid JSON with keys property/value'}, status_code=400)

    prop = payload.get('property')
    val = payload.get('value') if 'value' in payload else payload.get('recommend')
    if not prop:
        return JSONResponse({'error': 'missing_property', 'message': 'Provide "property": canonical or alias name'}, status_code=400)
    if val is None:
        return JSONResponse({'error': 'missing_value', 'message': 'Provide numeric "value" or "recommend"'}, status_code=400)

    # Normalize property aliases/casing for robustness
    alias_map = {
        'minadxforentry': 'MinAdxForEntry',
        'minAdxForEntry': 'MinAdxForEntry',
        'rsientryfloor': 'RsiEntryFloor',
        'RsiEntryFloor': 'RsiEntryFloor',
        'rsishortfloor': 'RsiEntryFloor',
        'RsiShortFloor': 'RsiEntryFloor',
        'rsilongfloor': 'RsiEntryFloor',
        'RsiLongFloor': 'RsiEntryFloor',
        'fastgrad': 'MinEntryFastGradientAbs',
        'FastGrad': 'MinEntryFastGradientAbs',
        'overextend': 'MaxBandwidthForEntry',
        'Overextend': 'MaxBandwidthForEntry',
        'maxbandwidthforentry': 'MaxBandwidthForEntry',
        'MaxBandwidthForEntry': 'MaxBandwidthForEntry',
        'minentryfastgradientabs': 'MinEntryFastGradientAbs',
        'MinEntryFastGradientAbs': 'MinEntryFastGradientAbs',
        'maxentryfastgradientabs': 'MaxEntryFastGradientAbs',
        'MaxEntryFastGradientAbs': 'MaxEntryFastGradientAbs',
        'validationminfastgradientabs': 'ValidationMinFastGradientAbs',
        'ValidationMinFastGradientAbs': 'ValidationMinFastGradientAbs',
        'minholdbars': 'MinHoldBars',
        'MinHoldBars': 'MinHoldBars',
        'maxgradientstabilityforentry': 'MaxGradientStabilityForEntry',
        'MaxGradientStabilityForEntry': 'MaxGradientStabilityForEntry'
    }
    if prop:
        prop = alias_map.get(prop, alias_map.get(str(prop).lower(), prop))

    # Validate property is known
    known_props = set(DEFAULT_PARAMS.keys())
    if prop not in known_props:
        return JSONResponse({'error': 'unknown_property', 'property': prop, 'known': sorted(list(known_props))}, status_code=400)

    # Coerce value to float if possible
    try:
        val_f = float(val)
    except Exception:
        return JSONResponse({'error': 'invalid_value', 'message': f'Value for {prop} must be numeric', 'received': val}, status_code=400)

    # Optional bounds check if defined
    bounds = AUTO_APPLY_BOUNDS.get(prop)
    if bounds:
        lo, hi = bounds
        if not (lo <= val_f <= hi):
            return JSONResponse({'error': 'out_of_bounds', 'property': prop, 'value': val_f, 'bounds': {'min': lo, 'max': hi}}, status_code=400)

    # Store override
    prev = active_overrides.get(prop)
    active_overrides[prop] = val_f
    print(f"[APPLY] Override set {prop}={val} (prev={prev})")
    try:
        if USE_SQLITE:
            db_exec(
                "INSERT INTO overrides_history (ts, property, oldValue, newValue, source) VALUES (?,?,?,?,?)",
                (time.time(), prop, float(prev) if prev is not None else None, val_f, 'manual')
            )
    except Exception as db_ex:
        print('[DB] override history insert failed:', db_ex)
    save_overrides_to_disk()
    effective = { k: float(active_overrides.get(k, v)) for k, v in DEFAULT_PARAMS.items() }
    return JSONResponse({'status': 'ok', 'property': prop, 'value': val_f, 'overrides': active_overrides, 'effectiveParams': effective, 'defaultParams': DEFAULT_PARAMS})

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

@app.post('/reset_thresholds')
async def reset_thresholds():
    """Reset all threshold overrides to strategy defaults."""
    try:
        count = len(active_overrides)
        print(f"[RESET] Clearing {count} active overrides")
        
        # Log reset event to history
        if USE_SQLITE:
            try:
                db_exec(
                    "INSERT INTO overrides_history (ts, property, oldValue, newValue, source) VALUES (?,?,?,?,?)",
                    (time.time(), 'ALL_RESET', None, None, 'manual-reset')
                )
            except Exception as db_ex:
                print('[DB] reset history insert failed:', db_ex)
        
        # Clear all overrides
        active_overrides.clear()
        save_overrides_to_disk()
        
        effective = { k: float(active_overrides.get(k, v)) for k, v in DEFAULT_PARAMS.items() }
        return JSONResponse({
            'status': 'ok',
            'message': f'Reset {count} threshold overrides to defaults',
            'count': count,
            'overrides': active_overrides,
            'effectiveParams': effective,
            'defaultParams': DEFAULT_PARAMS
        })
    except Exception as ex:
        print(f"[RESET] Error: {ex}")
        return JSONResponse({'error': str(ex)}, status_code=500)

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
        # Fetch latest diags with needed fields including open/high/low
        cur.execute("SELECT id, ts, barIndex, fastGrad, accel, fastEMA, open, high, low, close, trendSide FROM diags ORDER BY id DESC LIMIT ?", (min(limit,2000),))
        rows = cur.fetchall()
        recomputed = []
        mismatches = 0
        for r in rows:
            fg = float(r['fastGrad'] or 0.0)
            accel = float(r['accel'] or 0.0)
            fastEMA = float(r['fastEMA'] or 0.0)
            open_price = float(r['open'] or 0.0)
            high_price = float(r['high'] or 0.0)
            low_price = float(r['low'] or 0.0)
            close = float(r['close'] or 0.0)
            side = r['trendSide']
            
            # Candle color: red if close < open, green if close >= open
            is_red_candle = close < open_price
            is_green_candle = close >= open_price
            
            # Centralized bar classification logic (matches C# ClassifyBarType)
            # BULL: positive gradient AND close above Fast EMA
            # BEAR: negative gradient AND close below Fast EMA
            # Otherwise: NEUTRAL (contradictory/flat)
            if fg > 0 and close > fastEMA:
                bar_type = "BULL"
            elif fg < 0 and close < fastEMA:
                bar_type = "BEAR"
            else:
                bar_type = "NEUTRAL"
            
            # A bar is "bad" if:
            # 1. It contradicts the trend side (bar type doesn't match)
            # 2. It has negative acceleration during a bull trend (deceleration/pullback)
            # 3. RED candle in bull trend or GREEN candle in bear trend (contradictory candle color)
            # 4. It's neutral (gradient and price position contradict each other)
            is_bad = False
            reason = "good"
            
            if side == 'BULL':
                if is_red_candle:
                    is_bad = True
                    reason = f"red_candle_in_uptrend(open={open_price:.2f},close={close:.2f})"
                elif bar_type != 'BULL':
                    is_bad = True
                    reason = f"not_bull_bar(fg={fg:.2f},close={close:.2f}vsEMA={fastEMA:.2f})"
                elif accel < -0.3:  # Significant deceleration in uptrend
                    is_bad = True
                    reason = f"decel_in_uptrend(accel={accel:.2f})"
            elif side == 'BEAR':
                if is_green_candle:
                    is_bad = True
                    reason = f"green_candle_in_downtrend(open={open_price:.2f},close={close:.2f})"
                elif bar_type != 'BEAR':
                    is_bad = True
                    reason = f"not_bear_bar(fg={fg:.2f},close={close:.2f}vsEMA={fastEMA:.2f})"
                elif accel > 0.3:  # Deceleration in downtrend (less negative)
                    is_bad = True
                    reason = f"decel_in_downtrend(accel={accel:.2f})"
            
            recomputed.append({
                'barIndex': r['barIndex'], 
                'side': side, 
                'barType': bar_type, 
                'fastGrad': fg, 
                'accel': accel, 
                'fastEMA': fastEMA, 
                'open': open_price,
                'high': high_price,
                'low': low_price,
                'close': close, 
                'candleColor': 'red' if is_red_candle else 'green',
                'isBad': int(is_bad), 
                'reason': reason
            })
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

# ==================== Opportunity Analysis Endpoints ====================

@app.get('/opportunity_analysis.html', response_class=HTMLResponse)
@app.get('/opportunity_analysis', response_class=HTMLResponse)
def opportunity_analysis():
    """Serve the opportunity analysis page for streak detection"""
    page_path = os.path.join(os.path.dirname(static_dir), 'opportunity_analysis.html')
    try:
        with open(page_path, 'r', encoding='utf-8') as f:
            return HTMLResponse(f.read())
    except Exception as e:
        return HTMLResponse(f'<h1>Opportunity Analysis Page Not Found</h1><p>Error: {e}</p>', status_code=404)

@app.get('/api/opportunity-files')
def get_opportunity_files():
    """List available opportunity log CSV files"""
    try:
        files = []
        for filename in os.listdir(LOG_DIR):
            if filename.startswith('BarsOnTheFlow_Opportunities_') and filename.endswith('.csv'):
                filepath = os.path.join(LOG_DIR, filename)
                # Get file stats
                stat = os.stat(filepath)
                mtime = datetime.fromtimestamp(stat.st_mtime)
                
                # Count rows
                try:
                    with open(filepath, 'r', encoding='utf-8') as f:
                        row_count = sum(1 for _ in f) - 1  # Subtract header
                except:
                    row_count = 0
                
                files.append({
                    'filename': filename,
                    'timestamp': mtime.strftime('%Y-%m-%d %H:%M:%S'),
                    'bars': row_count,
                    'size': stat.st_size
                })
        
        # Sort by timestamp descending (newest first)
        files.sort(key=lambda x: x['timestamp'], reverse=True)
        
        return JSONResponse({'files': files, 'count': len(files)})
    except Exception as ex:
        print(f"[OPPORTUNITY] Error listing files: {ex}")
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.post('/api/analyze-streaks')
async def analyze_streaks(request: Request):
    """Analyze opportunity log for directional streaks"""
    try:
        params = await request.json()
        filename = params.get('filename')
        min_streak = params.get('min_streak', 5)
        max_streak = params.get('max_streak', 8)
        long_gradient_threshold = params.get('long_gradient_threshold', 7.0)
        short_gradient_threshold = params.get('short_gradient_threshold', -7.0)
        min_movement = params.get('min_movement', 5.0)
        counter_ratio = params.get('counter_ratio', 0.2)
        streak_type = params.get('streak_type', 'both')
        
        if not filename:
            return JSONResponse({'error': 'filename required'}, status_code=400)
        
        filepath = os.path.join(LOG_DIR, filename)
        if not os.path.exists(filepath):
            return JSONResponse({'error': 'file not found'}, status_code=404)
        
        # Read CSV data
        bars = []
        with open(filepath, 'r', encoding='utf-8') as f:
            reader = csv.DictReader(f)
            for row in reader:
                bars.append({
                    'bar': int(row['bar']),
                    'timestamp': row['timestamp'],
                    'open': float(row['open']),
                    'high': float(row['high']),
                    'low': float(row['low']),
                    'close': float(row['close']),
                    'candleType': row['candleType'],
                    'fastEmaGradDeg': float(row['fastEmaGradDeg']) if row['fastEmaGradDeg'] not in ('', 'NaN') else None,
                    'trendUpSignal': row['trendUpSignal'] == 'True',
                    'trendDownSignal': row['trendDownSignal'] == 'True',
                    'currentPosition': row['currentPosition'],
                    'entryBar': int(row['entryBar']) if row['entryBar'] not in ('', '-1') else -1,
                    'actionTaken': row['actionTaken'],
                    'blockReason': row['blockReason'],
                    'opportunityType': row['opportunityType'],
                    'barPattern': row.get('barPattern', '')
                })
        
        # Find streaks
        streaks = find_streaks(bars, min_streak, max_streak, long_gradient_threshold, 
                               short_gradient_threshold, min_movement, counter_ratio, streak_type)
        
        # Calculate statistics
        total = len(streaks)
        caught = sum(1 for s in streaks if s['status'] == 'caught')
        missed = sum(1 for s in streaks if s['status'] == 'missed')
        partial = sum(1 for s in streaks if s['status'] == 'partial')
        avg_length = sum(s['length'] for s in streaks) / total if total > 0 else 0
        avg_missed_points = sum(s['missed_points'] for s in streaks if s['missed_points'] > 0) / missed if missed > 0 else 0
        
        # Calculate PnL
        total_pnl = 0.0
        missed_pnl = 0.0
        for streak in streaks:
            if streak['status'] == 'caught':
                # Full profit captured
                total_pnl += streak['net_movement']
            elif streak['status'] == 'partial':
                # Partial profit (net movement - missed points)
                caught_pnl = streak['net_movement'] - streak['missed_points']
                total_pnl += caught_pnl
                missed_pnl += streak['missed_points']
            elif streak['status'] == 'missed':
                # All profit missed
                missed_pnl += streak['net_movement']
        
        potential_pnl = total_pnl + missed_pnl
        
        stats = {
            'total_streaks': total,
            'caught': caught,
            'missed': missed,
            'partial': partial,
            'avg_length': avg_length,
            'avg_missed_points': avg_missed_points,
            'total_bars': len(bars),
            'total_pnl': total_pnl,
            'missed_pnl': missed_pnl,
            'potential_pnl': potential_pnl
        }
        
        return JSONResponse({
            'streaks': streaks,
            'stats': stats,
            'params': params
        })
        
    except Exception as ex:
        print(f"[OPPORTUNITY] Analysis error: {ex}")
        import traceback
        traceback.print_exc()
        return JSONResponse({'error': str(ex)}, status_code=500)

def find_streaks(bars, min_streak, max_streak, long_grad_thresh, short_grad_thresh, 
                 min_movement, counter_ratio, streak_type):
    """
    Find directional streaks in the bar data similar to BarsOnTheFlow's 5-bar patterns.
    
    A streak is a sequence of bars with consistent overall direction (net positive/negative movement)
    allowing for some counter-trend bars based on counter_ratio.
    """
    streaks = []
    i = 0
    
    while i < len(bars) - min_streak + 1:
        # Try to find a streak starting at bar i
        for length in range(max_streak, min_streak - 1, -1):
            if i + length > len(bars):
                continue
            
            streak_bars = bars[i:i+length]
            
            # Calculate net movement and direction
            start_price = streak_bars[0]['open']
            end_price = streak_bars[-1]['close']
            net_movement = end_price - start_price
            
            if abs(net_movement) < min_movement:
                continue
            
            direction = 'LONG' if net_movement > 0 else 'SHORT'
            
            # Filter by streak type
            if streak_type == 'long' and direction != 'LONG':
                continue
            if streak_type == 'short' and direction != 'SHORT':
                continue
            
            # Count good/bad bars (good = candle matches direction)
            good_bars = 0
            bad_bars = 0
            for bar in streak_bars:
                is_good_candle = bar['candleType'] == 'good'
                if (direction == 'LONG' and is_good_candle) or (direction == 'SHORT' and not is_good_candle):
                    good_bars += 1
                else:
                    bad_bars += 1
            
            # Check counter-trend ratio
            actual_counter_ratio = bad_bars / length if length > 0 else 0
            if actual_counter_ratio > counter_ratio:
                continue
            
            # Calculate average gradient
            gradients = [b['fastEmaGradDeg'] for b in streak_bars if b['fastEmaGradDeg'] is not None]
            avg_gradient = sum(gradients) / len(gradients) if gradients else 0
            
            # Check if gradient meets threshold
            gradient_ok = True
            if direction == 'LONG' and avg_gradient < long_grad_thresh:
                gradient_ok = False
            if direction == 'SHORT' and avg_gradient > short_grad_thresh:
                gradient_ok = False
            
            # Determine entry status
            entry_bar = -1
            entry_status = 'missed'
            missed_points = abs(net_movement)
            block_reason = ''
            
            # Check if we entered during this streak
            for idx, bar in enumerate(streak_bars):
                if bar['entryBar'] >= 0 and bar['currentPosition'] != 'Flat':
                    entry_bar = bar['bar']
                    if idx == 0:
                        entry_status = 'caught'
                        missed_points = 0
                    else:
                        entry_status = 'partial'
                        # Calculate how many points were missed before entry
                        entry_price = streak_bars[idx]['open']
                        missed_points = abs(entry_price - start_price)
                    break
                
                # Capture block reason from missed opportunities
                if bar['opportunityType'] in ('LONG_SIGNAL', 'SHORT_SIGNAL'):
                    if 'SKIPPED' in bar['actionTaken'] or 'BLOCKED' in bar['actionTaken']:
                        block_reason = bar['blockReason']
            
            # Only add valid streaks (either caught or had valid signal but was blocked)
            if entry_status != 'missed' or block_reason:
                streaks.append({
                    'start_bar': streak_bars[0]['bar'],
                    'end_bar': streak_bars[-1]['bar'],
                    'length': length,
                    'direction': direction,
                    'net_movement': abs(net_movement),
                    'avg_gradient': avg_gradient,
                    'good_bars': good_bars,
                    'bad_bars': bad_bars,
                    'status': entry_status,
                    'entry_bar': entry_bar,
                    'missed_points': missed_points,
                    'block_reason': block_reason,
                    'pattern': streak_bars[0].get('barPattern', ''),
                    'gradient_ok': gradient_ok
                })
                
                # Skip ahead to avoid overlapping streaks
                i += length
                break
        else:
            i += 1
    
    return streaks

if __name__ == '__main__':
    import uvicorn
    # Default to dashboard port used by server_manager (can override via PORT env)
    port = int(os.environ.get('PORT', '51888'))
    uvicorn.run(app, host='127.0.0.1', port=port)
