import os
import time
import json
import csv
import sqlite3
import hashlib
import re
import io
from datetime import datetime
from typing import List, Dict, Any, Optional
from collections import deque
from fastapi import FastAPI, WebSocket, WebSocketDisconnect, Request, Query
from starlette.requests import ClientDisconnect
from fastapi import WebSocket, WebSocketDisconnect
from fastapi.responses import HTMLResponse, JSONResponse, PlainTextResponse, StreamingResponse
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

# Serve monitor.html at root
@app.get('/')
async def serve_monitor():
    try:
        with open('monitor.html', 'r', encoding='utf-8') as f:
            return HTMLResponse(content=f.read())
    except FileNotFoundError:
        return HTMLResponse('<h1>Monitor page not found</h1><p>Create monitor.html in the dashboard directory.</p>', status_code=404)

@app.get('/historical_profitability_analyzer.html')
async def serve_historical_profitability_analyzer():
    try:
        with open('historical_profitability_analyzer.html', 'r', encoding='utf-8') as f:
            return HTMLResponse(content=f.read())
    except FileNotFoundError:
        return HTMLResponse('<h1>Historical Profitability Analyzer not found</h1>', status_code=404)

@app.get('/trend_parameter_analyzer.html')
async def serve_trend_parameter_analyzer():
    try:
        with open('trend_parameter_analyzer.html', 'r', encoding='utf-8') as f:
            return HTMLResponse(content=f.read())
    except FileNotFoundError:
        return HTMLResponse('<h1>Trend Parameter Analyzer not found</h1>', status_code=404)
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
BARS_DB_PATH = os.path.join(os.path.dirname(__file__), 'bars.db')
_db_connection = None
_bars_db_connection = None

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

def get_bars_db_connection():
    """Get or create persistent bars database connection."""
    global _bars_db_connection
    if _bars_db_connection is None:
        _bars_db_connection = sqlite3.connect(BARS_DB_PATH, check_same_thread=False, timeout=10.0)
        _bars_db_connection.execute('PRAGMA journal_mode=WAL')
        _bars_db_connection.execute('PRAGMA cache_size=-10000')
        _bars_db_connection.execute('PRAGMA synchronous=NORMAL')
        print('[BARS_DB] Connection pool initialized with WAL mode')
    return _bars_db_connection

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
    # Trades table for historical profitability analysis
    cur.execute("""
    CREATE TABLE IF NOT EXISTS trades (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        entry_time REAL NOT NULL,
        entry_bar INTEGER,
        direction TEXT,
        entry_price REAL,
        exit_time REAL,
        exit_bar INTEGER,
        exit_price REAL,
        bars_held INTEGER,
        realized_points REAL,
        mfe REAL,
        mae REAL,
        exit_reason TEXT
    )
    """)
    cur.execute("CREATE INDEX IF NOT EXISTS idx_trades_entry_time ON trades(entry_time DESC)")
    cur.execute("CREATE INDEX IF NOT EXISTS idx_trades_direction ON trades(direction)")
    cur.execute("CREATE INDEX IF NOT EXISTS idx_trades_exit_reason ON trades(exit_reason)")
    
    # Create unique index to prevent exact duplicates (same entry_time, entry_price, direction)
    try:
        cur.execute("""
            CREATE UNIQUE INDEX IF NOT EXISTS idx_trades_unique_entry 
            ON trades(entry_time, entry_price, direction)
        """)
        print('[DB] Created unique index on dashboard.db trades(entry_time, entry_price, direction)')
    except sqlite3.OperationalError as e:
        if 'duplicate' not in str(e).lower() and 'UNIQUE constraint' not in str(e):
            print(f'[DB] Warning creating unique index on dashboard.db: {e}')
    
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
    
    # Table status tracking for database monitoring (safe creation with error handling)
    try:
        cur.execute("""
        CREATE TABLE IF NOT EXISTS table_status_history (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            database_name TEXT NOT NULL,
            table_name TEXT NOT NULL,
            status TEXT NOT NULL,
            strategy_name TEXT,
            row_count INTEGER,
            event_type TEXT,
            event_details TEXT,
            timestamp REAL NOT NULL,
            created_at TEXT DEFAULT (datetime('now'))
        )
        """)
        cur.execute("CREATE INDEX IF NOT EXISTS idx_table_status_db_table ON table_status_history(database_name, table_name)")
        cur.execute("CREATE INDEX IF NOT EXISTS idx_table_status_timestamp ON table_status_history(timestamp DESC)")
        print('[DB] table_status_history table initialized successfully')
    except Exception as table_ex:
        # Don't fail server startup if table creation fails - it might already exist with different schema
        print(f'[DB] Warning: Could not create table_status_history table: {table_ex}')
        print('[DB] Server will continue without table status tracking')
    
    conn.commit()
    # Don't close - keep persistent connection

def init_bars_db():
    """Initialize bars.db with BarsOnTheFlowStateAndBar table."""
    if not USE_SQLITE:
        return
    try:
        conn = get_bars_db_connection()
        cur = conn.cursor()
        
        # Main table combining strategy state + bar OHLC
        cur.execute("""
        CREATE TABLE IF NOT EXISTS BarsOnTheFlowStateAndBar (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp TEXT NOT NULL,
            receivedTs REAL NOT NULL,
            strategyName TEXT,
            enableDashboardDiagnostics INTEGER,
            
            -- Bar identification
            barIndex INTEGER NOT NULL,
            barTime TEXT,
            currentBar INTEGER,
            
            -- Previous bar OHLC (final data)
            open REAL,
            high REAL,
            low REAL,
            close REAL,
            volume REAL,
            candleType TEXT,
            
            -- Position state
            positionMarketPosition TEXT,
            positionQuantity INTEGER,
            positionAveragePrice REAL,
            intendedPosition TEXT,
            lastEntryBarIndex INTEGER,
            lastEntryDirection TEXT,
            
            -- Stop loss configuration
            stopLossPoints INTEGER,
            calculatedStopTicks INTEGER,
            calculatedStopPoints REAL,
            useTrailingStop INTEGER,
            useDynamicStopLoss INTEGER,
            lookback INTEGER,
            multiplier REAL,
            
            -- Break-even configuration
            useBreakEven INTEGER,
            breakEvenTrigger INTEGER,
            breakEvenOffset INTEGER,
            breakEvenActivated INTEGER,
            
            -- Strategy parameters
            contracts INTEGER,
            enableShorts INTEGER,
            avoidLongsOnBadCandle INTEGER,
            avoidShortsOnGoodCandle INTEGER,
            exitOnTrendBreak INTEGER,
            reverseOnTrendBreak INTEGER,
            fastEmaPeriod INTEGER,
            gradientThresholdSkipLongs REAL,
            gradientThresholdSkipShorts REAL,
            gradientFilterEnabled INTEGER,
            
            -- Gradient values (degrees)
            fastGradDeg REAL,
            slowGradDeg REAL,
            
            -- Trend parameters
            trendLookbackBars INTEGER,
            minMatchingBars INTEGER,
            usePnLTiebreaker INTEGER,
            
            -- Pending signals
            pendingLongFromBad INTEGER,
            pendingShortFromGood INTEGER,
            
            -- Performance metrics (real-time)
            unrealizedPnL REAL,
            realizedPnL REAL,
            totalTradesCount INTEGER,
            winningTradesCount INTEGER,
            losingTradesCount INTEGER,
            winRate REAL,
            
            -- Full state JSON for reference
            stateJson TEXT,
            
            created_at TEXT DEFAULT (datetime('now')),
            
            -- Prevent duplicate bars: unique constraint on strategy + barIndex
            UNIQUE(strategyName, barIndex)
        )
        """)
        
        # Indexes for common queries
        cur.execute("CREATE INDEX IF NOT EXISTS idx_botf_bar ON BarsOnTheFlowStateAndBar(barIndex)")
        cur.execute("CREATE INDEX IF NOT EXISTS idx_botf_ts ON BarsOnTheFlowStateAndBar(receivedTs DESC)")
        cur.execute("CREATE INDEX IF NOT EXISTS idx_botf_position ON BarsOnTheFlowStateAndBar(positionMarketPosition)")
        cur.execute("CREATE INDEX IF NOT EXISTS idx_botf_currentbar ON BarsOnTheFlowStateAndBar(currentBar)")
        
        conn.commit()
        print('[BARS_DB] BarsOnTheFlowStateAndBar table initialized')
    except Exception as ex:
        print(f'[BARS_DB] Initialization error: {ex}')

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
        
        # Add UNIQUE constraint on (symbol, bar_index) to prevent duplicates
        # Check if unique index already exists
        cur.execute("SELECT name FROM sqlite_master WHERE type='index' AND name='idx_bar_samples_unique'")
        if not cur.fetchone():
            try:
                # Try to create unique index (will fail if duplicates exist, which is fine)
                cur.execute("CREATE UNIQUE INDEX IF NOT EXISTS idx_bar_samples_unique ON bar_samples(symbol, bar_index)")
                print('[DB] Added UNIQUE constraint on (symbol, bar_index) to prevent duplicates')
            except sqlite3.OperationalError as e:
                if 'UNIQUE constraint failed' in str(e) or 'duplicate' in str(e).lower():
                    print('[DB] Warning: Cannot add UNIQUE constraint - duplicates exist. Run cleanup first.')
                else:
                    raise

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
        # Add contracts column to trades table if missing
        try:
            if not has_column('trades', 'contracts'):
                cur.execute("ALTER TABLE trades ADD COLUMN contracts INTEGER")
                print('[DB] Added contracts column to trades table')
        except Exception as ex:
            print('[DB] trades add contracts failed:', ex)
        
        # Add minMatchingBars column to BarsOnTheFlowStateAndBar if missing
        try:
            bars_conn = get_bars_db_connection()
            bars_cur = bars_conn.cursor()
            bars_cur.execute("PRAGMA table_info(BarsOnTheFlowStateAndBar)")
            bars_columns = [row[1] for row in bars_cur.fetchall()]
            
            if 'minMatchingBars' not in bars_columns:
                bars_cur.execute("ALTER TABLE BarsOnTheFlowStateAndBar ADD COLUMN minMatchingBars INTEGER")
                print('[DB] Added minMatchingBars column to BarsOnTheFlowStateAndBar table')
            
            # Add gradient degree columns if missing
            if 'fastGradDeg' not in bars_columns:
                bars_cur.execute("ALTER TABLE BarsOnTheFlowStateAndBar ADD COLUMN fastGradDeg REAL")
                print('[DB] Added fastGradDeg column to BarsOnTheFlowStateAndBar table')
            if 'slowGradDeg' not in bars_columns:
                bars_cur.execute("ALTER TABLE BarsOnTheFlowStateAndBar ADD COLUMN slowGradDeg REAL")
                print('[DB] Added slowGradDeg column to BarsOnTheFlowStateAndBar table')
            bars_conn.commit()
        except Exception as ex:
            print('[DB] BarsOnTheFlowStateAndBar column migration failed:', ex)
        
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
init_bars_db()
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

# --- Strategy state cache (one entry per strategy) ---
# Stores current strategy state for real-time monitoring
strategy_state_cache: Dict[str, Dict[str, Any]] = {}

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
@app.get('/index.html', response_class=HTMLResponse)
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

@app.get('/bar_debug.html', response_class=HTMLResponse)
@app.get('/bar_debug', response_class=HTMLResponse)
def bar_debug_page():
    page_path = os.path.join(os.path.dirname(static_dir), 'bar_debug.html')
    try:
        with open(page_path, 'r', encoding='utf-8') as f:
            return f.read()
    except Exception as e:
        return HTMLResponse(f'<h1>Bar Debug Page Not Found</h1><p>Error: {e}</p>', status_code=404)

@app.get('/barFlowReport.html', response_class=HTMLResponse)
@app.get('/barFlowReport', response_class=HTMLResponse)
def bar_flow_report_page():
    page_path = os.path.join(os.path.dirname(os.path.dirname(static_dir)), 'barFlowReport.html')
    try:
        with open(page_path, 'r', encoding='utf-8') as f:
            return f.read()
    except Exception as e:
        return HTMLResponse(f'<h1>Bar Flow Report Page Not Found</h1><p>Error: {e}</p>', status_code=404)

@app.get('/strategy_state.html', response_class=HTMLResponse)
@app.get('/strategy_state', response_class=HTMLResponse)
def strategy_state_page():
    page_path = os.path.join(os.path.dirname(static_dir), 'strategy_state.html')
    try:
        with open(page_path, 'r', encoding='utf-8') as f:
            return f.read()
    except Exception as e:
        return HTMLResponse(f'<h1>Strategy State Page Not Found</h1><p>Error: {e}</p>', status_code=404)

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
    """Get recommended stop loss in ticks based on quarter hour and current volume.
    
    Query params:
        hour: Hour of day (0-23 ET). If not provided, uses current hour and minute to calculate quarter hour.
        volume: Current bar volume for volume-adjusted stop
        symbol: Trading symbol (default MNQ)
    
    Returns:
        recommended_stop_ticks: Recommended stop loss in ticks
        avg_bar_range: Average bar range for this quarter hour (in points)
        avg_volume: Average volume for this quarter hour
        volume_condition: LOW/NORMAL/HIGH based on current vs average
        confidence: LOW/MEDIUM/HIGH based on sample count
    """
    try:
        now = datetime.now()
        if hour is None:
            hour = now.hour
        
        # Calculate quarter hour: 0-95 (0=00:00-00:14, 1=00:15-00:29, ..., 95=23:45-23:59)
        quarter_hour = hour * 4 + (now.minute // 15)
        
        conn = sqlite3.connect(VOLATILITY_DB_PATH)
        cursor = conn.cursor()
        
        # Get stats for this quarter hour
        cursor.execute('''
            SELECT avg_bar_range, avg_volume, avg_range_per_1k_volume, sample_count
            FROM volatility_stats
            WHERE quarter_hour = ? AND symbol = ? AND day_of_week IS NULL
        ''', (quarter_hour, symbol))
        
        row = cursor.fetchone()
        conn.close()
        
        if not row or row[3] < 10:  # Need at least 10 samples
            return JSONResponse({
                'recommended_stop_ticks': 16,  # Default 4 points
                'avg_bar_range': 0,
                'avg_volume': 0,
                'volume_condition': 'UNKNOWN',
                'confidence': 'LOW',
                'message': f'Insufficient data for quarter hour {quarter_hour} (hour {hour}) ({row[3] if row else 0} samples)'
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
            'hour': hour,
            'quarter_hour': quarter_hour
        })
        
    except Exception as ex:
        print(f'[API] volatility recommended-stop error: {ex}')
        return JSONResponse({'status': 'error', 'message': str(ex)}, status_code=500)

@app.post('/api/volatility/batch-record-bars')
async def api_volatility_batch_record_bars(request: Request):
    """Batch insert multiple bar samples at once.
    
    Used when transitioning from historical to real-time mode.
    Receives all accumulated historical bars in one request.
    
    POST body: JSON array of bar objects
    """
    try:
        bars = await request.json()
    except ClientDisconnect:
        return JSONResponse({"status": "ok", "message": "client_disconnected"}, status_code=200)
    except Exception as e:
        print(f'[BATCH_API] Error parsing JSON: {e}')
        return JSONResponse({"status": "error", "message": f"Invalid JSON: {str(e)}"}, status_code=400)
    
    if not isinstance(bars, list):
        return JSONResponse({"status": "error", "message": "Expected JSON array"}, status_code=400)
    
    print(f'[BATCH_API] Received batch of {len(bars)} bars')
    
    try:
        conn = sqlite3.connect(VOLATILITY_DB_PATH)
        # Set WAL mode and other pragmas
        conn.execute('PRAGMA journal_mode=WAL')
        conn.execute('PRAGMA synchronous=NORMAL')  # Faster writes
        cursor = conn.cursor()
        
        inserted = 0
        skipped = 0
        errors = 0
        
        # Process in chunks of 500 for better performance
        chunk_size = 500
        total_chunks = (len(bars) + chunk_size - 1) // chunk_size
        
        for chunk_idx in range(total_chunks):
            chunk_start = chunk_idx * chunk_size
            chunk_end = min(chunk_start + chunk_size, len(bars))
            chunk = bars[chunk_start:chunk_end]
            
            if chunk_idx % 2 == 0:  # Log every other chunk
                print(f'[BATCH_API] Processing chunk {chunk_idx + 1}/{total_chunks} (bars {chunk_start}-{chunk_end})')
            
            rows_to_insert = []
            
            for data in chunk:
                try:
                    timestamp = data.get('timestamp', '')
                    bar_index = data.get('bar_index', 0)
                    symbol = data.get('symbol', 'MNQ')
                    open_p = float(data.get('open', 0))
                    high_p = float(data.get('high', 0))
                    low_p = float(data.get('low', 0))
                    close_p = float(data.get('close', 0))
                    volume = int(data.get('volume', 0))
                    direction = data.get('direction', 'FLAT')
                    in_trade = data.get('in_trade', False)
                    trade_result = data.get('trade_result_ticks')
                    
                    # Parse timestamp
                    try:
                        dt = datetime.strptime(timestamp.split('.')[0], '%Y-%m-%d %H:%M:%S')
                    except:
                        dt = datetime.now()
                    
                    hour_of_day = dt.hour
                    quarter_hour = hour_of_day * 4 + (dt.minute // 15)
                    day_of_week = dt.weekday()
                    
                    # Calculate metrics
                    bar_range = high_p - low_p
                    body_size = abs(close_p - open_p)
                    
                    # Calculate wicks (required NOT NULL columns)
                    if close_p > open_p:  # Bullish candle
                        upper_wick = high_p - close_p
                        lower_wick = open_p - low_p
                    else:  # Bearish or doji
                        upper_wick = high_p - open_p
                        lower_wick = close_p - low_p
                    
                    range_per_1k_volume = (bar_range / (volume / 1000)) if volume > 0 else 0
                    
                    # Get EMA values if provided
                    ema_fast_period = data.get('ema_fast_period', 5)
                    ema_slow_period = data.get('ema_slow_period', 13)
                    ema_fast_value = data.get('ema_fast_value')
                    ema_slow_value = data.get('ema_slow_value')
                    fast_ema_grad_deg = data.get('fast_ema_grad_deg')
                    stop_loss_points = data.get('stop_loss_points', 0)
                    
                    # Handle null strings for EMA values
                    if ema_fast_value == 'null' or ema_fast_value is None:
                        ema_fast_value = None
                    else:
                        ema_fast_value = float(ema_fast_value) if ema_fast_value else None
                        
                    if ema_slow_value == 'null' or ema_slow_value is None:
                        ema_slow_value = None
                    else:
                        ema_slow_value = float(ema_slow_value) if ema_slow_value else None
                        
                    if fast_ema_grad_deg == 'null' or fast_ema_grad_deg is None:
                        fast_ema_grad_deg = None
                    else:
                        fast_ema_grad_deg = float(fast_ema_grad_deg) if fast_ema_grad_deg else None
                    
                    # Debugging fields
                    candle_type = data.get('candle_type', 'flat')
                    trend_up = 1 if data.get('trend_up', False) else 0
                    trend_down = 1 if data.get('trend_down', False) else 0
                    allow_long = 1 if data.get('allow_long_this_bar', False) else 0
                    allow_short = 1 if data.get('allow_short_this_bar', False) else 0
                    pending_long = 1 if data.get('pending_long_from_bad', False) else 0
                    pending_short = 1 if data.get('pending_short_from_good', False) else 0
                    avoid_longs = 1 if data.get('avoid_longs_on_bad_candle', False) else 0
                    avoid_shorts = 1 if data.get('avoid_shorts_on_good_candle', False) else 0
                    entry_reason = data.get('entry_reason', '')
                    
                    rows_to_insert.append((timestamp, bar_index, symbol, open_p, high_p, low_p, close_p, volume,
                          direction, 1 if in_trade else 0, trade_result, hour_of_day, day_of_week, bar_range, body_size,
                          upper_wick, lower_wick, range_per_1k_volume,
                          ema_fast_period, ema_slow_period, ema_fast_value, ema_slow_value, fast_ema_grad_deg,
                          stop_loss_points, quarter_hour, candle_type, trend_up, trend_down,
                          allow_long, allow_short, pending_long, pending_short, avoid_longs, avoid_shorts, entry_reason))
                        
                except Exception as row_ex:
                    errors += 1
                    if errors <= 5:  # Only log first 5 errors
                        print(f'[BATCH_API] Error on bar {data.get("bar_index", "?")}: {row_ex}')
            
            # Insert chunk - try first row to see error, then use executemany
            if rows_to_insert:
                try:
                    # Get count before insert
                    cursor.execute('SELECT COUNT(*) FROM bar_samples')
                    count_before = cursor.fetchone()[0]
                    
                    # Test first row to see if there's an error
                    if chunk_idx == 0 and len(rows_to_insert) > 0:
                        test_row = rows_to_insert[0]
                        try:
                            cursor.execute('''
                                INSERT INTO bar_samples 
                                (timestamp, bar_index, symbol, open_price, high_price, low_price, close_price, volume, 
                                 direction, in_trade, trade_result_ticks, hour_of_day, day_of_week, bar_range, body_size,
                                 upper_wick, lower_wick, range_per_1k_volume,
                                 ema_fast_period, ema_slow_period, ema_fast_value, ema_slow_value, fast_ema_grad_deg,
                                 stop_loss_points, quarter_hour, candle_type, trend_up, trend_down,
                                 allow_long_this_bar, allow_short_this_bar, pending_long_from_bad, pending_short_from_good,
                                 avoid_longs_on_bad_candle, avoid_shorts_on_good_candle, entry_reason)
                                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                            ''', test_row)
                            print(f'[BATCH_API] Test insert succeeded - rowcount={cursor.rowcount}')
                            conn.rollback()  # Rollback test
                        except Exception as test_ex:
                            print(f'[BATCH_API] TEST INSERT FAILED: {test_ex}')
                            print(f'[BATCH_API] Test row data: bar_index={test_row[1]}, symbol={test_row[2]}, timestamp={test_row[0]}')
                            import traceback
                            traceback.print_exc()
                            conn.rollback()
                    
                    # Use executemany for performance
                    cursor.executemany('''
                        INSERT OR IGNORE INTO bar_samples 
                        (timestamp, bar_index, symbol, open_price, high_price, low_price, close_price, volume, 
                         direction, in_trade, trade_result_ticks, hour_of_day, day_of_week, bar_range, body_size,
                         upper_wick, lower_wick, range_per_1k_volume,
                         ema_fast_period, ema_slow_period, ema_fast_value, ema_slow_value, fast_ema_grad_deg,
                         stop_loss_points, quarter_hour, candle_type, trend_up, trend_down,
                         allow_long_this_bar, allow_short_this_bar, pending_long_from_bad, pending_short_from_good,
                         avoid_longs_on_bad_candle, avoid_shorts_on_good_candle, entry_reason)
                        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    ''', rows_to_insert)
                    
                    conn.commit()  # Commit each chunk - CRITICAL!
                    
                    # Verify count after insert (accurate way to know what was inserted)
                    cursor.execute('SELECT COUNT(*) FROM bar_samples')
                    count_after = cursor.fetchone()[0]
                    actual_inserted_this_chunk = count_after - count_before
                    actual_skipped_this_chunk = len(rows_to_insert) - actual_inserted_this_chunk
                    
                    inserted += actual_inserted_this_chunk
                    skipped += actual_skipped_this_chunk
                    
                    if chunk_idx % 2 == 0 or actual_inserted_this_chunk == 0:  # Log every other chunk OR if nothing inserted
                        print(f'[BATCH_API] Chunk {chunk_idx + 1}: {actual_inserted_this_chunk} inserted (verified), {actual_skipped_this_chunk} skipped, count_before={count_before}, count_after={count_after}')
                except Exception as chunk_ex:
                    print(f'[BATCH_API] ERROR inserting chunk {chunk_idx + 1}: {chunk_ex}')
                    import traceback
                    traceback.print_exc()
                    conn.rollback()  # Rollback this chunk
                    errors += len(rows_to_insert)
        
        # Final commit to ensure everything is saved
        conn.commit()
        
        # Checkpoint WAL to ensure data is visible immediately to other connections
        cursor.execute('PRAGMA wal_checkpoint(TRUNCATE)')
        checkpoint_result = cursor.fetchone()
        print(f'[BATCH_API] WAL checkpoint result: {checkpoint_result}')
        
        # Verify data was actually inserted (on same connection)
        cursor.execute('SELECT COUNT(*) FROM bar_samples')
        actual_count = cursor.fetchone()[0]
        print(f'[BATCH_API] Verification: bar_samples table now has {actual_count} rows (on this connection)')
        
        # Also verify by querying a fresh connection to ensure WAL checkpoint worked
        import sqlite3 as sqlite3_check
        check_conn = sqlite3_check.connect(VOLATILITY_DB_PATH)
        check_cursor = check_conn.cursor()
        check_cursor.execute('SELECT COUNT(*) FROM bar_samples')
        check_count = check_cursor.fetchone()[0]
        check_conn.close()
        print(f'[BATCH_API] Verification (fresh connection): bar_samples table has {check_count} rows')
        
        if actual_count != check_count:
            print(f'[BATCH_API] WARNING: Count mismatch! Same connection={actual_count}, fresh connection={check_count}')
        
        conn.close()
        
        print(f'[BATCH_API] Batch complete: {inserted} inserted, {skipped} skipped (duplicates), {errors} errors')
        
        return JSONResponse({
            "status": "ok",
            "inserted": inserted,
            "skipped": skipped,
            "errors": errors,
            "total": len(bars)
        })
        
    except Exception as ex:
        print(f'[BATCH_API] Database error: {ex}')
        import traceback
        traceback.print_exc()
        return JSONResponse({"status": "error", "message": str(ex)}, status_code=500)

@app.post('/api/volatility/record-trade')
async def api_volatility_record_trade(request: Request):
    """Record a completed trade to volatility.db trades table.
    
    Only records trades that actually executed (filled orders).
    Called from NinjaTrader OnPositionUpdate when position closes.
    
    POST body: JSON with trade data (EntryTime, EntryBar, Direction, EntryPrice, etc.)
    """
    print(f'[API] volatility record-trade: Received request')
    try:
        data = await request.json()
        print(f'[API] volatility record-trade: Parsed JSON - EntryBar={data.get("EntryBar")}, ExitBar={data.get("ExitBar")}, Direction={data.get("Direction")}')
    except Exception as e:
        print(f'[API] volatility record-trade: Error parsing JSON: {e}')
        import traceback
        traceback.print_exc()
        return JSONResponse({"status": "error", "message": f"Invalid JSON: {str(e)}"}, status_code=400)
    
    try:
        conn = sqlite3.connect(VOLATILITY_DB_PATH)
        conn.execute('PRAGMA journal_mode=WAL')
        cursor = conn.cursor()
        
        # Ensure trades table exists in volatility.db
        cursor.execute("""
            CREATE TABLE IF NOT EXISTS trades (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                entry_time REAL NOT NULL,
                entry_bar INTEGER,
                direction TEXT,
                entry_price REAL,
                exit_time REAL,
                exit_bar INTEGER,
                exit_price REAL,
                bars_held INTEGER,
                realized_points REAL,
                mfe REAL,
                mae REAL,
                exit_reason TEXT,
                entry_reason TEXT,
                contracts INTEGER,
                ema_fast_period INTEGER,
                ema_slow_period INTEGER,
                ema_fast_value REAL,
                ema_slow_value REAL,
                candle_type TEXT,
                open_final REAL,
                high_final REAL,
                low_final REAL,
                close_final REAL,
                fast_ema REAL,
                fast_ema_grad_deg REAL,
                bar_pattern TEXT
            )
        """)
        
        # Create indexes if they don't exist
        cursor.execute("CREATE INDEX IF NOT EXISTS idx_volatility_trades_entry_time ON trades(entry_time DESC)")
        cursor.execute("CREATE INDEX IF NOT EXISTS idx_volatility_trades_direction ON trades(direction)")
        cursor.execute("CREATE INDEX IF NOT EXISTS idx_volatility_trades_exit_reason ON trades(exit_reason)")
        
        # Create unique index to prevent exact duplicates (same entry_time, entry_price, direction)
        # This prevents the same trade from being recorded multiple times
        try:
            cursor.execute("""
                CREATE UNIQUE INDEX IF NOT EXISTS idx_volatility_trades_unique_entry 
                ON trades(entry_time, entry_price, direction)
            """)
            print('[API] volatility record-trade: Created unique index on (entry_time, entry_price, direction)')
        except sqlite3.OperationalError as e:
            if 'duplicate' not in str(e).lower() and 'UNIQUE constraint' not in str(e):
                print(f'[API] volatility record-trade: Warning creating unique index: {e}')
        
        # Check which columns exist
        cursor.execute("PRAGMA table_info(trades)")
        columns = [row[1] for row in cursor.fetchall()]
        has_contracts = 'contracts' in columns
        has_ema_fast = 'ema_fast_period' in columns
        has_ema_slow = 'ema_slow_period' in columns
        has_ema_fast_value = 'ema_fast_value' in columns
        has_ema_slow_value = 'ema_slow_value' in columns
        has_candle_type = 'candle_type' in columns
        has_open_final = 'open_final' in columns
        has_high_final = 'high_final' in columns
        has_low_final = 'low_final' in columns
        has_close_final = 'close_final' in columns
        has_fast_ema = 'fast_ema' in columns
        has_fast_ema_grad_deg = 'fast_ema_grad_deg' in columns
        has_bar_pattern = 'bar_pattern' in columns
        has_entry_reason = 'entry_reason' in columns
        
        # Add missing columns
        if not has_contracts:
            cursor.execute("ALTER TABLE trades ADD COLUMN contracts INTEGER")
        if not has_ema_fast:
            cursor.execute("ALTER TABLE trades ADD COLUMN ema_fast_period INTEGER")
        if not has_ema_slow:
            cursor.execute("ALTER TABLE trades ADD COLUMN ema_slow_period INTEGER")
        if not has_ema_fast_value:
            cursor.execute("ALTER TABLE trades ADD COLUMN ema_fast_value REAL")
        if not has_ema_slow_value:
            cursor.execute("ALTER TABLE trades ADD COLUMN ema_slow_value REAL")
        if not has_candle_type:
            cursor.execute("ALTER TABLE trades ADD COLUMN candle_type TEXT")
        if not has_open_final:
            cursor.execute("ALTER TABLE trades ADD COLUMN open_final REAL")
        if not has_high_final:
            cursor.execute("ALTER TABLE trades ADD COLUMN high_final REAL")
        if not has_low_final:
            cursor.execute("ALTER TABLE trades ADD COLUMN low_final REAL")
        if not has_close_final:
            cursor.execute("ALTER TABLE trades ADD COLUMN close_final REAL")
        if not has_fast_ema:
            cursor.execute("ALTER TABLE trades ADD COLUMN fast_ema REAL")
        if not has_fast_ema_grad_deg:
            cursor.execute("ALTER TABLE trades ADD COLUMN fast_ema_grad_deg REAL")
        if not has_bar_pattern:
            cursor.execute("ALTER TABLE trades ADD COLUMN bar_pattern TEXT")
        if not has_entry_reason:
            cursor.execute("ALTER TABLE trades ADD COLUMN entry_reason TEXT")
        
        conn.commit()
        
        # Get values from request
        ema_fast_val = data.get('EmaFastValue')
        ema_slow_val = data.get('EmaSlowValue')
        ema_fast_value = float(ema_fast_val) if ema_fast_val is not None and ema_fast_val != 'null' else None
        ema_slow_value = float(ema_slow_val) if ema_slow_val is not None and ema_slow_val != 'null' else None
        
        open_final = data.get('OpenFinal')
        high_final = data.get('HighFinal')
        low_final = data.get('LowFinal')
        close_final = data.get('CloseFinal')
        fast_ema = data.get('FastEma')
        fast_ema_grad_deg = data.get('FastEmaGradDeg')
        candle_type = data.get('CandleType', '')
        bar_pattern = data.get('BarPattern', '')
        entry_reason = data.get('EntryReason', '')
        
        open_final_val = float(open_final) if open_final is not None and open_final != 'null' else None
        high_final_val = float(high_final) if high_final is not None and high_final != 'null' else None
        low_final_val = float(low_final) if low_final is not None and low_final != 'null' else None
        close_final_val = float(close_final) if close_final is not None and close_final != 'null' else None
        fast_ema_val = float(fast_ema) if fast_ema is not None and fast_ema != 'null' else None
        fast_ema_grad_deg_val = float(fast_ema_grad_deg) if fast_ema_grad_deg is not None and fast_ema_grad_deg != 'null' else None
        
        # Check for duplicate before inserting (same entry_time, entry_price, direction)
        entry_time_val = float(data.get('EntryTime', time.time()))
        entry_price_val = float(data.get('EntryPrice', 0))
        direction_val = data.get('Direction', 'LONG')
        
        cursor.execute("""
            SELECT id FROM trades 
            WHERE entry_time = ? AND entry_price = ? AND direction = ?
        """, (entry_time_val, entry_price_val, direction_val))
        
        existing = cursor.fetchone()
        if existing:
            print(f'[API] volatility record-trade: DUPLICATE DETECTED - Skipping trade EntryBar={data.get("EntryBar")}, EntryTime={entry_time_val}, EntryPrice={entry_price_val}, Direction={direction_val} (existing id={existing[0]})')
            conn.close()
            return JSONResponse({"status": "ok", "message": "Trade already exists (duplicate skipped)", "duplicate": True})
        
        # Insert trade (no duplicate found)
        cursor.execute("""
            INSERT INTO trades (
                entry_time, entry_bar, direction, entry_price,
                exit_time, exit_bar, exit_price, bars_held,
                realized_points, mfe, mae, exit_reason, entry_reason, contracts,
                ema_fast_period, ema_slow_period, ema_fast_value, ema_slow_value,
                candle_type, open_final, high_final, low_final, close_final,
                fast_ema, fast_ema_grad_deg, bar_pattern
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
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
            data.get('ExitReason', ''),
            entry_reason,
            int(data.get('Contracts', 0)),
            int(data.get('EmaFastPeriod', 0)),
            int(data.get('EmaSlowPeriod', 0)),
            ema_fast_value,
            ema_slow_value,
            candle_type,
            open_final_val,
            high_final_val,
            low_final_val,
            close_final_val,
            fast_ema_val,
            fast_ema_grad_deg_val,
            bar_pattern
        ))
        
        conn.commit()
        cursor.execute('PRAGMA wal_checkpoint(TRUNCATE)')
        
        # Verify the trade was inserted
        cursor.execute('SELECT COUNT(*) FROM trades WHERE entry_bar = ? AND exit_bar = ?', 
                      (int(data.get('EntryBar', 0)), int(data.get('ExitBar', 0))))
        count = cursor.fetchone()[0]
        
        conn.close()
        
        print(f'[API] volatility record-trade: Trade recorded - EntryBar={data.get("EntryBar")}, ExitBar={data.get("ExitBar")}, Direction={data.get("Direction")}, Points={data.get("RealizedPoints")}, Verified count={count}')
        
        return JSONResponse({"status": "ok", "message": "Trade recorded", "verified": count > 0})
    except Exception as ex:
        print(f'[API] volatility record-trade: Database error: {ex}')
        import traceback
        traceback.print_exc()
        return JSONResponse({"status": "error", "message": str(ex)}, status_code=500)

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
    except ClientDisconnect:
        # Client disconnected before request completed - this is normal when strategy is cancelled
        return JSONResponse({"status": "ok", "message": "client_disconnected"}, status_code=200)
    except Exception as e:
        print(f'[API] volatility record-bar: Error parsing JSON: {e}')
        return JSONResponse({"status": "error", "message": f"Invalid JSON: {str(e)}"}, status_code=400)
    
    try:
        conn = sqlite3.connect(VOLATILITY_DB_PATH)
        # Enable WAL mode for better concurrent access (if not already enabled)
        conn.execute('PRAGMA journal_mode=WAL')
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
        # Calculate quarter hour: 0-95 (0=00:00-00:14, 1=00:15-00:29, ..., 95=23:45-23:59)
        quarter_hour = hour_of_day * 4 + (dt.minute // 15)
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
        
        # Check if EMA columns exist, add them if missing
        cursor.execute("PRAGMA table_info(bar_samples)")
        columns = [row[1] for row in cursor.fetchall()]
        has_ema_fast = 'ema_fast_period' in columns
        has_ema_slow = 'ema_slow_period' in columns
        has_ema_fast_value = 'ema_fast_value' in columns
        has_ema_slow_value = 'ema_slow_value' in columns
        has_grad_deg = 'fast_ema_grad_deg' in columns
        has_stop_loss = 'stop_loss_points' in columns
        has_candle_type = 'candle_type' in columns
        has_trend_up = 'trend_up' in columns
        has_trend_down = 'trend_down' in columns
        has_allow_long = 'allow_long_this_bar' in columns
        has_allow_short = 'allow_short_this_bar' in columns
        has_pending_long = 'pending_long_from_bad' in columns
        has_pending_short = 'pending_short_from_good' in columns
        has_avoid_longs = 'avoid_longs_on_bad_candle' in columns
        has_avoid_shorts = 'avoid_shorts_on_good_candle' in columns
        has_entry_reason = 'entry_reason' in columns
        
        if not has_ema_fast:
            cursor.execute("ALTER TABLE bar_samples ADD COLUMN ema_fast_period INTEGER")
        if not has_ema_slow:
            cursor.execute("ALTER TABLE bar_samples ADD COLUMN ema_slow_period INTEGER")
        if not has_ema_fast_value:
            cursor.execute("ALTER TABLE bar_samples ADD COLUMN ema_fast_value REAL")
        if not has_ema_slow_value:
            cursor.execute("ALTER TABLE bar_samples ADD COLUMN ema_slow_value REAL")
        if not has_grad_deg:
            cursor.execute("ALTER TABLE bar_samples ADD COLUMN fast_ema_grad_deg REAL")
        if not has_stop_loss:
            cursor.execute("ALTER TABLE bar_samples ADD COLUMN stop_loss_points REAL")
        if not has_candle_type:
            cursor.execute("ALTER TABLE bar_samples ADD COLUMN candle_type TEXT")
        if not has_trend_up:
            cursor.execute("ALTER TABLE bar_samples ADD COLUMN trend_up INTEGER")
        if not has_trend_down:
            cursor.execute("ALTER TABLE bar_samples ADD COLUMN trend_down INTEGER")
        if not has_allow_long:
            cursor.execute("ALTER TABLE bar_samples ADD COLUMN allow_long_this_bar INTEGER")
        if not has_allow_short:
            cursor.execute("ALTER TABLE bar_samples ADD COLUMN allow_short_this_bar INTEGER")
        if not has_pending_long:
            cursor.execute("ALTER TABLE bar_samples ADD COLUMN pending_long_from_bad INTEGER")
        if not has_pending_short:
            cursor.execute("ALTER TABLE bar_samples ADD COLUMN pending_short_from_good INTEGER")
        if not has_avoid_longs:
            cursor.execute("ALTER TABLE bar_samples ADD COLUMN avoid_longs_on_bad_candle INTEGER")
        if not has_avoid_shorts:
            cursor.execute("ALTER TABLE bar_samples ADD COLUMN avoid_shorts_on_good_candle INTEGER")
        if not has_entry_reason:
            cursor.execute("ALTER TABLE bar_samples ADD COLUMN entry_reason TEXT")
        
        # Get EMA values and gradient degree, handle null/None
        # C# sends string "null" when EMA is not ready, Python json.loads() parses it as string "null"
        ema_fast_val = data.get('ema_fast_value')
        ema_slow_val = data.get('ema_slow_value')
        grad_deg_val = data.get('fast_ema_grad_deg')
        stop_loss_val = data.get('stop_loss_points')
        
        # Handle string "null", actual None, empty string, or 0.0 (which might indicate not ready)
        def parse_ema_value(val):
            if val is None:
                return None
            if isinstance(val, str):
                if val.lower() == 'null' or val.strip() == '':
                    return None
                try:
                    fval = float(val)
                    # If value is exactly 0.0, it might be a sentinel for "not ready" - check if it's actually valid
                    # For now, accept 0.0 as valid (could be real EMA value)
                    return fval
                except (ValueError, TypeError):
                    return None
            try:
                fval = float(val)
                return fval
            except (ValueError, TypeError):
                return None
        
        ema_fast_value = parse_ema_value(ema_fast_val)
        ema_slow_value = parse_ema_value(ema_slow_val)
        fast_ema_grad_deg = parse_ema_value(grad_deg_val)
        stop_loss_points = parse_ema_value(stop_loss_val)
        
        # Get debugging fields
        candle_type = data.get('candle_type', '')
        trend_up = 1 if data.get('trend_up', False) in (True, 'true', 1, '1') else 0
        trend_down = 1 if data.get('trend_down', False) in (True, 'true', 1, '1') else 0
        allow_long_this_bar = 1 if data.get('allow_long_this_bar', False) in (True, 'true', 1, '1') else 0
        allow_short_this_bar = 1 if data.get('allow_short_this_bar', False) in (True, 'true', 1, '1') else 0
        pending_long_from_bad = 1 if data.get('pending_long_from_bad', False) in (True, 'true', 1, '1') else 0
        pending_short_from_good = 1 if data.get('pending_short_from_good', False) in (True, 'true', 1, '1') else 0
        avoid_longs_on_bad_candle = 1 if data.get('avoid_longs_on_bad_candle', False) in (True, 'true', 1, '1') else 0
        avoid_shorts_on_good_candle = 1 if data.get('avoid_shorts_on_good_candle', False) in (True, 'true', 1, '1') else 0
        entry_reason = data.get('entry_reason', '') or ''
        
        has_all_debug_cols = (has_candle_type and has_trend_up and has_trend_down and has_allow_long and 
                              has_allow_short and has_pending_long and has_pending_short and 
                              has_avoid_longs and has_avoid_shorts and has_entry_reason)
        
        try:
            if has_ema_fast and has_ema_slow and has_ema_fast_value and has_ema_slow_value and has_grad_deg and has_stop_loss and has_all_debug_cols:
                cursor.execute('''
                    INSERT OR IGNORE INTO bar_samples (
                        timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
                        open_price, high_price, low_price, close_price, volume,
                        bar_range, body_size, upper_wick, lower_wick, ema_fast_period, ema_slow_period,
                        ema_fast_value, ema_slow_value, fast_ema_grad_deg, stop_loss_points,
                        range_per_1k_volume, direction, in_trade, trade_result_ticks,
                        candle_type, trend_up, trend_down, allow_long_this_bar, allow_short_this_bar,
                        pending_long_from_bad, pending_short_from_good, avoid_longs_on_bad_candle, avoid_shorts_on_good_candle, entry_reason
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                ''', (
                    timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
                    open_p, high_p, low_p, close_p, volume,
                    bar_range, body_size, upper_wick, lower_wick,
                    int(data.get('ema_fast_period', 0) or 0), int(data.get('ema_slow_period', 0) or 0),
                    ema_fast_value, ema_slow_value, fast_ema_grad_deg, stop_loss_points,
                    range_per_1k_volume, direction, 1 if in_trade else 0, trade_result,
                    candle_type, trend_up, trend_down, allow_long_this_bar, allow_short_this_bar,
                    pending_long_from_bad, pending_short_from_good, avoid_longs_on_bad_candle, avoid_shorts_on_good_candle, entry_reason
                ))
            elif has_ema_fast and has_ema_slow and has_ema_fast_value and has_ema_slow_value and has_grad_deg and has_stop_loss:
                cursor.execute('''
                    INSERT OR IGNORE INTO bar_samples (
                        timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
                        open_price, high_price, low_price, close_price, volume,
                        bar_range, body_size, upper_wick, lower_wick, ema_fast_period, ema_slow_period,
                        ema_fast_value, ema_slow_value, fast_ema_grad_deg, stop_loss_points,
                        range_per_1k_volume, direction, in_trade, trade_result_ticks
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                ''', (
                    timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
                    open_p, high_p, low_p, close_p, volume,
                    bar_range, body_size, upper_wick, lower_wick,
                    int(data.get('ema_fast_period', 0) or 0), int(data.get('ema_slow_period', 0) or 0),
                    ema_fast_value, ema_slow_value, fast_ema_grad_deg, stop_loss_points,
                    range_per_1k_volume, direction, 1 if in_trade else 0, trade_result
                ))
            elif has_ema_fast and has_ema_slow and has_ema_fast_value and has_ema_slow_value and has_grad_deg:
                cursor.execute('''
                    INSERT OR IGNORE INTO bar_samples (
                        timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
                        open_price, high_price, low_price, close_price, volume,
                        bar_range, body_size, upper_wick, lower_wick, ema_fast_period, ema_slow_period,
                        ema_fast_value, ema_slow_value, fast_ema_grad_deg,
                        range_per_1k_volume, direction, in_trade, trade_result_ticks
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                ''', (
                    timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
                    open_p, high_p, low_p, close_p, volume,
                    bar_range, body_size, upper_wick, lower_wick,
                    int(data.get('ema_fast_period', 0) or 0), int(data.get('ema_slow_period', 0) or 0),
                    ema_fast_value, ema_slow_value, fast_ema_grad_deg,
                    range_per_1k_volume, direction, 1 if in_trade else 0, trade_result
                ))
            elif has_ema_fast and has_ema_slow and has_ema_fast_value and has_ema_slow_value:
                # Fallback if gradient column doesn't exist yet
                cursor.execute('''
                    INSERT OR IGNORE INTO bar_samples (
                        timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
                        open_price, high_price, low_price, close_price, volume,
                        bar_range, body_size, upper_wick, lower_wick, ema_fast_period, ema_slow_period,
                        ema_fast_value, ema_slow_value,
                        range_per_1k_volume, direction, in_trade, trade_result_ticks
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                ''', (
                    timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
                    open_p, high_p, low_p, close_p, volume,
                    bar_range, body_size, upper_wick, lower_wick,
                    int(data.get('ema_fast_period', 0) or 0), int(data.get('ema_slow_period', 0) or 0),
                    ema_fast_value, ema_slow_value,
                    range_per_1k_volume, direction, 1 if in_trade else 0, trade_result
                ))
            elif has_ema_fast and has_ema_slow:
                cursor.execute('''
                    INSERT OR IGNORE INTO bar_samples (
                            timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
                        open_price, high_price, low_price, close_price, volume,
                        bar_range, body_size, upper_wick, lower_wick, ema_fast_period, ema_slow_period,
                        range_per_1k_volume, direction, in_trade, trade_result_ticks
                        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                ''', (
                        timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
                    open_p, high_p, low_p, close_p, volume,
                    bar_range, body_size, upper_wick, lower_wick,
                    int(data.get('ema_fast_period', 0) or 0), int(data.get('ema_slow_period', 0) or 0),
                    range_per_1k_volume, direction, 1 if in_trade else 0, trade_result
                ))
            else:
                cursor.execute('''
                    INSERT OR IGNORE INTO bar_samples (
                            timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
                        open_price, high_price, low_price, close_price, volume,
                        bar_range, body_size, upper_wick, lower_wick,
                        range_per_1k_volume, direction, in_trade, trade_result_ticks
                        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                ''', (
                        timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
                    open_p, high_p, low_p, close_p, volume,
                    bar_range, body_size, upper_wick, lower_wick,
                    range_per_1k_volume, direction, 1 if in_trade else 0, trade_result
                ))
        except sqlite3.OperationalError as col_error:
            if 'no such column: quarter_hour' in str(col_error):
                # Column missing - try to add it
                print('[API] quarter_hour column missing, adding it...')
                try:
                    cursor.execute("ALTER TABLE bar_samples ADD COLUMN quarter_hour INTEGER")
                    cursor.execute("UPDATE bar_samples SET quarter_hour = hour_of_day * 4 WHERE quarter_hour IS NULL")
                    conn.commit()
                    # Retry the insert
                    if has_ema_fast and has_ema_slow and has_ema_fast_value and has_ema_slow_value:
                        cursor.execute('''
                            INSERT OR IGNORE INTO bar_samples (
                                timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
                                open_price, high_price, low_price, close_price, volume,
                                bar_range, body_size, upper_wick, lower_wick, ema_fast_period, ema_slow_period,
                                ema_fast_value, ema_slow_value,
                                range_per_1k_volume, direction, in_trade, trade_result_ticks
                            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                        ''', (
                            timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
                            open_p, high_p, low_p, close_p, volume,
                            bar_range, body_size, upper_wick, lower_wick,
                            int(data.get('ema_fast_period', 0) or 0), int(data.get('ema_slow_period', 0) or 0),
                            ema_fast_value, ema_slow_value,
                            range_per_1k_volume, direction, 1 if in_trade else 0, trade_result
                        ))
                    elif has_ema_fast and has_ema_slow:
                        cursor.execute('''
                            INSERT OR IGNORE INTO bar_samples (
                                timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
                                open_price, high_price, low_price, close_price, volume,
                                bar_range, body_size, upper_wick, lower_wick, ema_fast_period, ema_slow_period,
                                range_per_1k_volume, direction, in_trade, trade_result_ticks
                            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                        ''', (
                            timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
                            open_p, high_p, low_p, close_p, volume,
                            bar_range, body_size, upper_wick, lower_wick,
                            int(data.get('ema_fast_period', 0) or 0), int(data.get('ema_slow_period', 0) or 0),
                            range_per_1k_volume, direction, 1 if in_trade else 0, trade_result
                        ))
                    else:
                        cursor.execute('''
                            INSERT OR IGNORE INTO bar_samples (
                                timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
                                open_price, high_price, low_price, close_price, volume,
                                bar_range, body_size, upper_wick, lower_wick,
                                range_per_1k_volume, direction, in_trade, trade_result_ticks
                            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                        ''', (
                            timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
                            open_p, high_p, low_p, close_p, volume,
                            bar_range, body_size, upper_wick, lower_wick,
                            range_per_1k_volume, direction, 1 if in_trade else 0, trade_result
                        ))
                    print('[API] [OK] Added quarter_hour column and retried insert')
                except Exception as add_ex:
                    conn.close()
                    raise add_ex
            else:
                raise
        
        conn.commit()
        
        # Checkpoint WAL periodically to ensure data is visible (every 10 bars or every 100th bar)
        if bar_index <= 10 or bar_index % 100 == 0:
            try:
                cursor.execute('PRAGMA wal_checkpoint(TRUNCATE)')
                cursor.fetchone()  # Execute the checkpoint
            except:
                pass  # Ignore checkpoint errors
        
        conn.close()
        
        # Log successful save (first 20 bars or every 100th bar to avoid spam)
        if bar_index <= 20 or bar_index % 100 == 0:
            print(f'[API] volatility record-bar: Saved bar {bar_index} to database')
        
        return JSONResponse({'status': 'ok', 'bar_index': bar_index, 'hour': hour_of_day, 'quarter_hour': quarter_hour})
        
    except Exception as ex:
        print(f'[API] volatility record-bar error: {ex}')
        return JSONResponse({'status': 'error', 'message': str(ex)}, status_code=500)

@app.get('/api/volatility/stats')
def api_volatility_stats(symbol: str = 'MNQ'):
    """Get volatility statistics by quarter hour (15 minutes) for analysis."""
    try:
        conn = sqlite3.connect(VOLATILITY_DB_PATH)
        cursor = conn.cursor()
        
        cursor.execute('''
            SELECT quarter_hour, hour_of_day, sample_count, avg_bar_range, avg_volume, 
                   avg_range_per_1k_volume, min_bar_range, max_bar_range
            FROM volatility_stats
            WHERE symbol = ? AND day_of_week IS NULL
            ORDER BY quarter_hour
        ''', (symbol,))
        
        rows = cursor.fetchall()
        conn.close()
        
        stats = []
        for row in rows:
            quarter_hour = row[0]
            hour = row[1]
            minute_start = (quarter_hour % 4) * 15
            time_label = f"{hour:02d}:{minute_start:02d}"
            stats.append({
                'quarter_hour': quarter_hour,
                'time': time_label,
                'hour': hour,
                'sample_count': row[2],
                'avg_bar_range': round(row[3], 2) if row[3] else 0,
                'avg_volume': int(row[4]) if row[4] else 0,
                'avg_range_per_1k_vol': round(row[5], 4) if row[5] else 0,
                'min_bar_range': round(row[6], 2) if row[6] else 0,
                'max_bar_range': round(row[7], 2) if row[7] else 0
            })
        
        return JSONResponse({'status': 'ok', 'stats': stats})
        
    except Exception as ex:
        print(f'[API] volatility stats error: {ex}')
        return JSONResponse({'status': 'error', 'message': str(ex)}, status_code=500)

@app.post('/api/volatility/update-aggregates')
def api_volatility_update_aggregates(symbol: str = 'MNQ'):
    """Recalculate aggregated volatility statistics from bar samples (by quarter hour)."""
    try:
        conn = sqlite3.connect(VOLATILITY_DB_PATH)
        cursor = conn.cursor()
        
        # Calculate stats for each quarter hour (0-95)
        for quarter_hour in range(96):
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
                WHERE quarter_hour = ? AND symbol = ?
            ''', (quarter_hour, symbol))
            
            row = cursor.fetchone()
            if row and row[7] > 0:  # sample_count > 0
                # Calculate hour_of_day from quarter_hour for backward compatibility
                hour_of_day = quarter_hour // 4
                cursor.execute('''
                    INSERT OR REPLACE INTO volatility_stats (
                        hour_of_day, quarter_hour, day_of_week, symbol,
                        avg_volume, min_volume, max_volume,
                        avg_bar_range, min_bar_range, max_bar_range,
                        avg_range_per_1k_volume,
                        sample_count, first_sample_time, last_sample_time, last_updated
                    ) VALUES (?, ?, NULL, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, datetime('now'))
                ''', (
                    hour_of_day, quarter_hour, symbol,
                    row[0], row[1], row[2],  # volume stats
                    row[3], row[4], row[5],  # range stats
                    row[6],                   # range per 1k volume
                    row[7], row[8], row[9]   # counts and times
                ))
        
        conn.commit()
        conn.close()
        
        return JSONResponse({'status': 'ok', 'message': 'Aggregates updated (by quarter hour)'})
        
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
    search_list =  diags[-500:] if len(diags) > 500 else diags
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
    except ClientDisconnect:
        # client closed early; treat as best-effort
        return JSONResponse({"status": "client_disconnected"}, status_code=499)
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

# --- Strategy State Endpoints ---
@app.post('/state')
async def receive_state(request: Request):
    """Receive strategy state updates and cache them for real-time monitoring."""
    try:
        payload = await request.json()
    except ClientDisconnect:
        # Client disconnected before request completed - this is normal when strategy is cancelled
        return JSONResponse({"status": "ok", "message": "client_disconnected"}, status_code=200)
    except Exception:
        try:
            text = await request.body()
            try:
                payload = json.loads(text)
            except Exception:
                print("[STATE] Failed to parse JSON from request body")
                return JSONResponse({"error": "invalid_json"}, status_code=400)
        except ClientDisconnect:
            # Client disconnected while reading body
            return JSONResponse({"status": "ok", "message": "client_disconnected"}, status_code=200)
    
    try:
        # Enrich with server timestamp
        payload["receivedTs"] = time.time()
        payload["receivedAt"] = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        
        # Get strategy name (default to BarsOnTheFlow if not provided)
        strategy_name = payload.get("strategyName", "BarsOnTheFlow")
        bar_idx = payload.get("barIndex", "?")
        
        print(f"[STATE] Received bar {bar_idx} from {strategy_name}")
        
        # Cache the state (overwrites previous state for this strategy)
        strategy_state_cache[strategy_name] = payload
        
        # Log every state update for debugging
        bar = payload.get("currentBar", "?")
        position = payload.get("positionMarketPosition", "?")
        print(f"[STATE] {strategy_name} bar={bar} position={position} receivedAt={payload['receivedAt']}")
        
        # Broadcast to WebSocket clients
        try:
            await ws_broadcast({
                'type': 'state',
                'strategy': strategy_name,
                'data': payload
            })
        except Exception as _ex_ws:
            pass
        
        # Persist to bars.db for historical analysis (fire-and-forget, don't block response)
        if strategy_name == "BarsOnTheFlow":
            # Run DB insert in background thread to avoid blocking the HTTP response
            import threading
            db_thread = threading.Thread(target=_save_state_to_db, args=(payload, strategy_name), daemon=True)
            db_thread.start()
            print(f"[STATE] Started background save thread for bar {bar_idx}")
        
        return JSONResponse({"status": "ok", "strategy": strategy_name})
    
    except Exception as ex:
        print(f"[STATE] Error processing state: {ex}")
        import traceback
        traceback.print_exc()
        return JSONResponse({"error": str(ex)}, status_code=500)

def _save_state_to_db(payload: dict, strategy_name: str):
    """Save state to database in background thread."""
    try:
        bar_idx = payload.get('barIndex', '?')
        print(f"[BG_SAVE] Starting save for bar {bar_idx}")
        
        # Determine candle type
        candle_type = "flat"
        if payload.get('open') and payload.get('close'):
            if payload['close'] > payload['open']:
                candle_type = "good"
            elif payload['close'] < payload['open']:
                candle_type = "bad"
        
        conn = get_bars_db_connection()
        cur = conn.cursor()
        
        cur.execute("""
            INSERT OR REPLACE INTO BarsOnTheFlowStateAndBar (
                timestamp, receivedTs, strategyName, enableDashboardDiagnostics,
                barIndex, barTime, currentBar,
                open, high, low, close, volume, candleType,
                positionMarketPosition, positionQuantity, positionAveragePrice,
                intendedPosition, lastEntryBarIndex, lastEntryDirection,
                stopLossPoints, calculatedStopTicks, calculatedStopPoints,
                useTrailingStop, useDynamicStopLoss, lookback, multiplier,
                useBreakEven, breakEvenTrigger, breakEvenOffset, breakEvenActivated,
                contracts, enableShorts, avoidLongsOnBadCandle, avoidShortsOnGoodCandle,
                exitOnTrendBreak, reverseOnTrendBreak, fastEmaPeriod,
                gradientThresholdSkipLongs, gradientThresholdSkipShorts, gradientFilterEnabled,
                fastGradDeg, slowGradDeg,
                trendLookbackBars, minMatchingBars, usePnLTiebreaker,
                pendingLongFromBad, pendingShortFromGood,
                unrealizedPnL, realizedPnL, totalTradesCount,
                winningTradesCount, losingTradesCount, winRate,
                stateJson
            ) VALUES (
                ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?,
                ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?
            )
        """, (
            payload.get('timestamp'),
            payload['receivedTs'],
            strategy_name,
            1 if payload.get('enableDashboardDiagnostics') else 0,
            payload.get('barIndex'),
            payload.get('barTime'),
            payload.get('currentBar'),
            payload.get('open'),
            payload.get('high'),
            payload.get('low'),
            payload.get('close'),
            payload.get('volume'),
            candle_type,
            payload.get('positionMarketPosition'),
            payload.get('positionQuantity'),
            payload.get('positionAveragePrice'),
            payload.get('intendedPosition'),
            payload.get('lastEntryBarIndex'),
            payload.get('lastEntryDirection'),
            payload.get('stopLossPoints'),
            payload.get('calculatedStopTicks'),
            payload.get('calculatedStopPoints'),
            1 if payload.get('useTrailingStop') else 0,
            1 if payload.get('useDynamicStopLoss') else 0,
            payload.get('lookback'),
            payload.get('multiplier'),
            1 if payload.get('useBreakEven') else 0,
            payload.get('breakEvenTrigger'),
            payload.get('breakEvenOffset'),
            1 if payload.get('breakEvenActivated') else 0,
            payload.get('contracts'),
            1 if payload.get('enableShorts') else 0,
            1 if payload.get('avoidLongsOnBadCandle') else 0,
            1 if payload.get('avoidShortsOnGoodCandle') else 0,
            1 if payload.get('exitOnTrendBreak') else 0,
            1 if payload.get('reverseOnTrendBreak') else 0,
            payload.get('fastEmaPeriod'),
            payload.get('gradientThresholdSkipLongs'),
            payload.get('gradientThresholdSkipShorts'),
            1 if payload.get('gradientFilterEnabled') else 0,
            payload.get('fastGradDeg'),
            payload.get('slowGradDeg'),
            payload.get('trendLookbackBars'),
            payload.get('minMatchingBars'),
            1 if payload.get('usePnLTiebreaker') else 0,
            1 if payload.get('pendingLongFromBad') else 0,
            1 if payload.get('pendingShortFromGood') else 0,
            payload.get('unrealizedPnL'),
            payload.get('realizedPnL'),
            payload.get('totalTradesCount'),
            payload.get('winningTradesCount'),
            payload.get('losingTradesCount'),
            payload.get('winRate'),
            json.dumps(payload)
        ))
        conn.commit()
        # Don't close global connection - keep it open for reuse
        print(f"[BG_SAVE] [OK] Saved bar {bar_idx} to BarsOnTheFlowStateAndBar")
    except sqlite3.IntegrityError as integrity_ex:
        # UNIQUE constraint violation - bar already exists (this is normal with INSERT OR REPLACE)
        print(f"[BG_SAVE] Bar {bar_idx} already exists (replaced): {integrity_ex}")
        conn.rollback()
    except Exception as db_ex:
        print(f"[BG_SAVE] ✗ ERROR saving bar {bar_idx} to database: {db_ex}")
        import traceback
        traceback.print_exc()
        # Don't re-raise - let it fail silently to avoid crashing the background thread

@app.get('/strategy/state')
async def get_strategy_state(strategy: str = "BarsOnTheFlow"):
    """Get the current cached state for a specific strategy."""
    state = strategy_state_cache.get(strategy)
    if state:
        return JSONResponse(state)
    else:
        return JSONResponse({"error": "no_state_cached", "strategy": strategy}, status_code=404)

@app.post('/api/strategy/update-params')
async def update_strategy_params(request: Request):
    """Update strategy parameters in the state JSON file."""
    try:
        data = await request.json()
        strategy = data.get('strategy', 'BarsOnTheFlow')
        params = data.get('params', {})
        
        if not params:
            return JSONResponse({'error': 'No parameters provided'}, status_code=400)
        
        # Path to strategy state JSON file
        state_dir = os.path.join(os.path.dirname(os.path.dirname(__file__)), 'strategy_state')
        state_file = os.path.join(state_dir, f'{strategy}_state.json')
        
        # Create directory if it doesn't exist
        os.makedirs(state_dir, exist_ok=True)
        
        # Load existing state or create new
        if os.path.exists(state_file):
            with open(state_file, 'r', encoding='utf-8') as f:
                state = json.load(f)
        else:
            state = {}
        
        # Update parameters
        for key, value in params.items():
            state[key] = value
        
        # Add timestamp
        state['lastUpdated'] = datetime.now().isoformat()
        state['updatedBy'] = 'OpportunityAnalysis'
        
        # Save updated state
        with open(state_file, 'w', encoding='utf-8') as f:
            json.dump(state, f, indent=2)
        
        print(f"[STRATEGY] Updated {strategy} parameters: {params}")
        
        return JSONResponse({
            'success': True,
            'strategy': strategy,
            'updated_params': params,
            'state_file': state_file
        })
        
    except Exception as ex:
        print(f"[STRATEGY] Error updating parameters: {ex}")
        import traceback
        traceback.print_exc()
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.get('/strategies')
async def list_strategies():
    """List all strategies that have sent state updates."""
    strategies = []
    for name, state in strategy_state_cache.items():
        strategies.append({
            "name": name,
            "isRunning": state.get("isRunning", False),
            "currentBar": state.get("currentBar", 0),
            "position": state.get("positionMarketPosition", "Unknown"),
            "lastUpdate": state.get("receivedAt", "Never")
        })
    return JSONResponse({"strategies": strategies, "count": len(strategies)})

@app.get('/api/bars/state-history')
async def get_state_history(limit: int = 100, strategy: str = "BarsOnTheFlow"):
    """Get historical strategy state + bar data from bars.db."""
    try:
        conn = get_bars_db_connection()
        cur = conn.cursor()
        
        cur.execute("""
            SELECT 
                id, timestamp, receivedTs, barIndex, barTime, currentBar,
                open, high, low, close, volume, candleType,
                positionMarketPosition, positionQuantity, positionAveragePrice,
                intendedPosition, lastEntryBarIndex, lastEntryDirection,
                stopLossPoints, calculatedStopTicks, calculatedStopPoints,
                contracts, pendingLongFromBad, pendingShortFromGood
            FROM BarsOnTheFlowStateAndBar
            WHERE strategyName = ?
            ORDER BY receivedTs DESC
            LIMIT ?
        """, (strategy, limit))
        
        rows = cur.fetchall()
        results = []
        for row in rows:
            results.append({
                "id": row[0],
                "timestamp": row[1],
                "receivedTs": row[2],
                "barIndex": row[3],
                "barTime": row[4],
                "currentBar": row[5],
                "open": row[6],
                "high": row[7],
                "low": row[8],
                "close": row[9],
                "volume": row[10],
                "candleType": row[11],
                "positionMarketPosition": row[12],
                "positionQuantity": row[13],
                "positionAveragePrice": row[14],
                "intendedPosition": row[15],
                "lastEntryBarIndex": row[16],
                "lastEntryDirection": row[17],
                "stopLossPoints": row[18],
                "calculatedStopTicks": row[19],
                "calculatedStopPoints": row[20],
                "contracts": row[21],
                "pendingLongFromBad": bool(row[22]),
                "pendingShortFromGood": bool(row[23])
            })
        
        return JSONResponse({"data": results, "count": len(results)})
    
    except Exception as ex:
        return JSONResponse({"error": str(ex)}, status_code=500)

@app.get('/api/bars/gaps')
async def get_bar_gaps(strategy: str = "BarsOnTheFlow"):
    """Identify missing bars (gaps) in the recorded sequence."""
    try:
        conn = get_bars_db_connection()
        cur = conn.cursor()
        
        # Get min and max barIndex
        cur.execute("""
            SELECT MIN(barIndex), MAX(barIndex), COUNT(*)
            FROM BarsOnTheFlowStateAndBar
            WHERE strategyName = ?
        """, (strategy,))
        
        row = cur.fetchone()
        if not row or row[0] is None:
            return JSONResponse({"gaps": [], "message": "No data found"})
        
        min_bar, max_bar, count = row
        expected_count = max_bar - min_bar + 1
        missing_count = expected_count - count
        
        if missing_count == 0:
            return JSONResponse({
                "gaps": [],
                "minBar": min_bar,
                "maxBar": max_bar,
                "recorded": count,
                "expected": expected_count,
                "message": "No gaps - sequence is complete"
            })
        
        # Find actual gaps
        cur.execute("""
            WITH RECURSIVE seq(n) AS (
                SELECT ? UNION ALL
                SELECT n + 1 FROM seq WHERE n < ?
            )
            SELECT seq.n as missing_bar
            FROM seq
            LEFT JOIN BarsOnTheFlowStateAndBar b
                ON b.barIndex = seq.n AND b.strategyName = ?
            WHERE b.barIndex IS NULL
            ORDER BY seq.n
            LIMIT 1000
        """, (min_bar, max_bar, strategy))
        
        gaps = [{"barIndex": row[0]} for row in cur.fetchall()]
        
        return JSONResponse({
            "gaps": gaps,
            "gapCount": len(gaps),
            "minBar": min_bar,
            "maxBar": max_bar,
            "recorded": count,
            "expected": expected_count,
            "missing": missing_count
        })
    
    except Exception as ex:
        return JSONResponse({"error": str(ex)}, status_code=500)

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
        
        # Check if trades table exists, create if it doesn't
        cur = conn.cursor()
        cur.execute("""
            SELECT name FROM sqlite_master 
            WHERE type='table' AND name='trades'
        """)
        if not cur.fetchone():
            # Table doesn't exist, create it
            cur.execute("""
                CREATE TABLE IF NOT EXISTS trades (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    entry_time REAL NOT NULL,
                    entry_bar INTEGER,
                    direction TEXT,
                    entry_price REAL,
                    exit_time REAL,
                    exit_bar INTEGER,
                    exit_price REAL,
                    bars_held INTEGER,
                    realized_points REAL,
                    mfe REAL,
                    mae REAL,
                    exit_reason TEXT
                )
            """)
            cur.execute("CREATE INDEX IF NOT EXISTS idx_trades_entry_time ON trades(entry_time DESC)")
            cur.execute("CREATE INDEX IF NOT EXISTS idx_trades_direction ON trades(direction)")
            cur.execute("CREATE INDEX IF NOT EXISTS idx_trades_exit_reason ON trades(exit_reason)")
            conn.commit()
        
        # Overall performance
        cur.execute("""
            SELECT 
                COUNT(*) as total_trades,
                SUM(CASE WHEN realized_points > 0 THEN 1 ELSE 0 END) as winners,
                SUM(CASE WHEN realized_points < 0 THEN 1 ELSE 0 END) as losers,
                COALESCE(SUM(realized_points), 0) as total_pnl,
                COALESCE(AVG(realized_points), 0) as avg_pnl,
                COALESCE(AVG(mfe), 0) as avg_mfe,
                COALESCE(AVG(mae), 0) as avg_mae,
                COALESCE(AVG(bars_held), 0) as avg_bars_held,
                COALESCE(MAX(realized_points), 0) as best_trade,
                COALESCE(MIN(realized_points), 0) as worst_trade
            FROM trades
            WHERE entry_time >= ?
        """, (cutoff_ts,))
        
        row = cur.fetchone()
        if row:
            overall = dict(zip([d[0] for d in cur.description], row))
        else:
            overall = {
                'total_trades': 0, 'winners': 0, 'losers': 0,
                'total_pnl': 0, 'avg_pnl': 0, 'avg_mfe': 0,
                'avg_mae': 0, 'avg_bars_held': 0,
                'best_trade': 0, 'worst_trade': 0
            }
        
        # MFE capture analysis
        cur.execute("""
            SELECT 
                COALESCE(AVG(CASE WHEN mfe > 0 THEN realized_points / mfe ELSE 0 END) * 100, 0) as avg_mfe_capture_pct,
                COALESCE(COUNT(CASE WHEN mfe > 1.5 AND realized_points / mfe < 0.4 THEN 1 END), 0) as poor_capture_count
            FROM trades
            WHERE entry_time >= ? AND mfe > 0
        """, (cutoff_ts,))
        
        row = cur.fetchone()
        if row:
            mfe_stats = dict(zip([d[0] for d in cur.description], row))
        else:
            mfe_stats = {'avg_mfe_capture_pct': 0, 'poor_capture_count': 0}
        
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
            'mfe_stats': mfe_stats,
            'bars_analysis': bars_analysis,
            'exit_reasons': exit_reasons,
            'hourly': hourly,
            'recent': recent,
            'period_days': days
        })
        
    except Exception as ex:
        import traceback
        error_trace = traceback.format_exc()
        print(f"[ANALYTICS] Error: {ex}")
        print(f"[ANALYTICS] Traceback: {error_trace}")
        return JSONResponse({
            'error': str(ex),
            'overall': {
                'total_trades': 0, 'winners': 0, 'losers': 0,
                'total_pnl': 0, 'avg_pnl': 0, 'avg_mfe': 0,
                'avg_mae': 0, 'avg_bars_held': 0,
                'best_trade': 0, 'worst_trade': 0
            },
            'mfe_stats': {'avg_mfe_capture_pct': 0, 'poor_capture_count': 0},
            'bars_analysis': [],
            'exit_reasons': [],
            'hourly': [],
            'recent': []
        }, status_code=500)

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

@app.get('/api/monitor/stats')
async def get_monitor_stats():
    """Get database statistics for monitoring."""
    try:
        # Use bars.db, not dashboard.db
        conn = get_bars_db_connection()
        cur = conn.cursor()
        
        # Check if table exists
        table_check = cur.execute(
            "SELECT name FROM sqlite_master WHERE type='table' AND name='BarsOnTheFlowStateAndBar'"
        ).fetchone()
        
        if not table_check:
            return JSONResponse({
                'status': 'ok',
                'row_count': 0,
                'min_index': 0,
                'max_index': 0,
                'expected_count': 0,
                'gaps': 0,
                'latest_bars': [],
                'message': 'No data yet - waiting for strategy to start',
                'ts': time.time()
            })
        
        # Row count
        count_result = cur.execute('SELECT COUNT(*) FROM BarsOnTheFlowStateAndBar').fetchone()
        row_count = count_result[0] if count_result else 0
        
        if row_count == 0:
            return JSONResponse({
                'status': 'ok',
                'row_count': 0,
                'min_index': 0,
                'max_index': 0,
                'expected_count': 0,
                'gaps': 0,
                'latest_bars': [],
                'message': 'Waiting for first bar...',
                'ts': time.time()
            })
        
        # Min, max, expected count
        range_result = cur.execute('SELECT MIN(barIndex), MAX(barIndex) FROM BarsOnTheFlowStateAndBar').fetchone()
        min_idx = range_result[0] if range_result and range_result[0] is not None else 0
        max_idx = range_result[1] if range_result and range_result[1] is not None else 0
        expected_count = (max_idx - min_idx + 1) if max_idx >= min_idx else 0
        gaps = expected_count - row_count if expected_count > 0 else 0

        # Latest stop/exit settings from the newest bar
        latest_stop = {
            'useTrailingStop': None,
            'useDynamicStopLoss': None,
            'lookback': None,
            'multiplier': None,
            'stopLossPoints': None,
            'calculatedStopPoints': None
        }
        try:
            stop_row = cur.execute(
                '''SELECT useTrailingStop, useDynamicStopLoss, lookback, multiplier,
                          stopLossPoints, calculatedStopPoints
                   FROM BarsOnTheFlowStateAndBar
                   ORDER BY barIndex DESC
                   LIMIT 1'''
            ).fetchone()
            if stop_row:
                latest_stop = {
                    'useTrailingStop': bool(stop_row[0]) if stop_row[0] is not None else None,
                    'useDynamicStopLoss': bool(stop_row[1]) if stop_row[1] is not None else None,
                    'lookback': stop_row[2],
                    'multiplier': stop_row[3],
                    'stopLossPoints': stop_row[4],
                    'calculatedStopPoints': stop_row[5]
                }
        except Exception as _stop_err:
            pass
        
        # Latest 5 bars - fetch all data while connection is open
        latest_bars = []
        try:
            # Try with available columns; receivedTs always exists
            rows = cur.execute(
                '''SELECT barIndex, positionMarketPosition, realizedPnL, winRate, receivedTs
                   FROM BarsOnTheFlowStateAndBar 
                   ORDER BY barIndex DESC LIMIT 5'''
            ).fetchall()
            latest_bars = [(row[0], row[1], row[2], row[3], row[4]) for row in rows]
        except Exception as query_err:
            # If that fails, try a simpler query
            try:
                rows = cur.execute(
                    'SELECT barIndex, positionMarketPosition, realizedPnL, winRate FROM BarsOnTheFlowStateAndBar ORDER BY barIndex DESC LIMIT 5'
                ).fetchall()
                latest_bars = [(row[0], row[1], row[2], row[3], None) for row in rows]
            except:
                pass  # Table exists but columns may differ, just return stats without latest bars
        
        # Don't close global connection - keep it open for reuse
        
        # Now build response
        response_bars = []
        for row in latest_bars:
            ts_val = ''
            try:
                if len(row) > 4 and row[4] is not None:
                    ts_val = datetime.fromtimestamp(row[4]).strftime('%Y-%m-%d %H:%M:%S')
            except Exception:
                ts_val = ''

            response_bars.append({
                'barIndex': row[0],
                'position': row[1] or 'Flat',
                'realizedPnL': round(row[2], 2) if row[2] else 0,
                'winRate': round(row[3], 2) if row[3] else 0,
                'timestamp': ts_val
            })
        
        return JSONResponse({
            'status': 'ok',
            'row_count': row_count,
            'min_index': min_idx,
            'max_index': max_idx,
            'expected_count': expected_count,
            'gaps': gaps,
            'latest_bars': response_bars,
            'latest_stop': latest_stop,
            'ts': time.time()
        })
    except Exception as e:
        print(f"[MONITOR] Error: {e}")
        import traceback
        traceback.print_exc()
        return JSONResponse({
            'status': 'ok',
            'row_count': 0,
            'min_index': 0,
            'max_index': 0,
            'expected_count': 0,
            'gaps': 0,
            'latest_bars': [],
            'message': f'Data pending: {str(e)}',
            'ts': time.time()
        })

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
    except ClientDisconnect:
        # Client closed connection early; treat as best-effort
        return JSONResponse({"status": "client_disconnected"}, status_code=499)
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

@app.get('/api/trades/by-bar')
async def get_trades_by_bar(entry_bar: int = None, exit_bar: int = None):
    """Query trades by entry_bar or exit_bar number"""
    try:
        if not USE_SQLITE:
            return JSONResponse({'error': 'Database not enabled'}, status_code=500)
        
        if entry_bar is None and exit_bar is None:
            return JSONResponse({'error': 'Must provide entry_bar or exit_bar parameter'}, status_code=400)
        
        conn = get_db_connection()
        cur = conn.cursor()
        
        # Build query based on provided parameters
        conditions = []
        params = []
        
        if entry_bar is not None:
            conditions.append("entry_bar = ?")
            params.append(entry_bar)
        
        if exit_bar is not None:
            conditions.append("exit_bar = ?")
            params.append(exit_bar)
        
        where_clause = " OR ".join(conditions)
        
        cur.execute(f"""
            SELECT 
                id,
                entry_time,
                entry_bar,
                direction,
                entry_price,
                exit_time,
                exit_bar,
                exit_price,
                bars_held,
                realized_points,
                mfe,
                mae,
                exit_reason
            FROM trades
            WHERE {where_clause}
            ORDER BY entry_bar
        """, params)
        
        trades = []
        for row in cur.fetchall():
            trade = dict(zip([d[0] for d in cur.description], row))
            if trade.get('entry_time'):
                trade['entry_time_str'] = datetime.fromtimestamp(trade['entry_time']).strftime("%Y-%m-%d %H:%M:%S")
            if trade.get('exit_time'):
                trade['exit_time_str'] = datetime.fromtimestamp(trade['exit_time']).strftime("%Y-%m-%d %H:%M:%S")
            trades.append(trade)
        
        return JSONResponse({
            'trades': trades,
            'count': len(trades),
            'query': {
                'entry_bar': entry_bar,
                'exit_bar': exit_bar
            }
        })
        
    except Exception as ex:
        import traceback
        error_trace = traceback.format_exc()
        print(f"[GET_TRADES_BY_BAR] Error: {ex}")
        print(f"[GET_TRADES_BY_BAR] Traceback: {error_trace}")
        return JSONResponse({'error': str(ex)}, status_code=500)

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
                # Check if columns exist before inserting
                conn = get_db_connection()
                cur = conn.cursor()
                cur.execute("PRAGMA table_info(trades)")
                columns = [row[1] for row in cur.fetchall()]
                has_contracts = 'contracts' in columns
                has_ema_fast = 'ema_fast_period' in columns
                has_ema_slow = 'ema_slow_period' in columns
                has_ema_fast_value = 'ema_fast_value' in columns
                has_ema_slow_value = 'ema_slow_value' in columns
                has_candle_type = 'candle_type' in columns
                has_open_final = 'open_final' in columns
                has_high_final = 'high_final' in columns
                has_low_final = 'low_final' in columns
                has_close_final = 'close_final' in columns
                has_fast_ema = 'fast_ema' in columns
                has_fast_ema_grad_deg = 'fast_ema_grad_deg' in columns
                has_bar_pattern = 'bar_pattern' in columns
                has_entry_reason = 'entry_reason' in columns
                
                # Add missing columns if needed
                if not has_ema_fast:
                    cur.execute("ALTER TABLE trades ADD COLUMN ema_fast_period INTEGER")
                    conn.commit()
                    has_ema_fast = True
                if not has_ema_slow:
                    cur.execute("ALTER TABLE trades ADD COLUMN ema_slow_period INTEGER")
                    conn.commit()
                    has_ema_slow = True
                if not has_ema_fast_value:
                    cur.execute("ALTER TABLE trades ADD COLUMN ema_fast_value REAL")
                    conn.commit()
                    has_ema_fast_value = True
                if not has_ema_slow_value:
                    cur.execute("ALTER TABLE trades ADD COLUMN ema_slow_value REAL")
                    conn.commit()
                    has_ema_slow_value = True
                if not has_candle_type:
                    cur.execute("ALTER TABLE trades ADD COLUMN candle_type TEXT")
                    conn.commit()
                    has_candle_type = True
                if not has_open_final:
                    cur.execute("ALTER TABLE trades ADD COLUMN open_final REAL")
                    conn.commit()
                    has_open_final = True
                if not has_high_final:
                    cur.execute("ALTER TABLE trades ADD COLUMN high_final REAL")
                    conn.commit()
                    has_high_final = True
                if not has_low_final:
                    cur.execute("ALTER TABLE trades ADD COLUMN low_final REAL")
                    conn.commit()
                    has_low_final = True
                if not has_close_final:
                    cur.execute("ALTER TABLE trades ADD COLUMN close_final REAL")
                    conn.commit()
                    has_close_final = True
                if not has_fast_ema:
                    cur.execute("ALTER TABLE trades ADD COLUMN fast_ema REAL")
                    conn.commit()
                    has_fast_ema = True
                if not has_fast_ema_grad_deg:
                    cur.execute("ALTER TABLE trades ADD COLUMN fast_ema_grad_deg REAL")
                    conn.commit()
                    has_fast_ema_grad_deg = True
                if not has_bar_pattern:
                    cur.execute("ALTER TABLE trades ADD COLUMN bar_pattern TEXT")
                    conn.commit()
                    has_bar_pattern = True
                if not has_entry_reason:
                    cur.execute("ALTER TABLE trades ADD COLUMN entry_reason TEXT")
                    conn.commit()
                    has_entry_reason = True
                
                # Get EMA values, handle null/None
                ema_fast_val = data.get('EmaFastValue')
                ema_slow_val = data.get('EmaSlowValue')
                ema_fast_value = float(ema_fast_val) if ema_fast_val is not None and ema_fast_val != 'null' else None
                ema_slow_value = float(ema_slow_val) if ema_slow_val is not None and ema_slow_val != 'null' else None
                
                # Get exit bar OHLC and other exit bar data
                open_final = data.get('OpenFinal')
                high_final = data.get('HighFinal')
                low_final = data.get('LowFinal')
                close_final = data.get('CloseFinal')
                fast_ema = data.get('FastEma')
                fast_ema_grad_deg = data.get('FastEmaGradDeg')
                candle_type = data.get('CandleType', '')
                bar_pattern = data.get('BarPattern', '')
                
                open_final_val = float(open_final) if open_final is not None and open_final != 'null' else None
                high_final_val = float(high_final) if high_final is not None and high_final != 'null' else None
                low_final_val = float(low_final) if low_final is not None and low_final != 'null' else None
                close_final_val = float(close_final) if close_final is not None and close_final != 'null' else None
                fast_ema_val = float(fast_ema) if fast_ema is not None and fast_ema != 'null' else None
                fast_ema_grad_deg_val = float(fast_ema_grad_deg) if fast_ema_grad_deg is not None and fast_ema_grad_deg != 'null' else None
                
                # Get entry reason
                entry_reason = data.get('EntryReason', '')
                
                # Check for duplicate before inserting (same entry_time, entry_price, direction)
                entry_time_val = float(data.get('EntryTime', time.time()))
                entry_price_val = float(data.get('EntryPrice', 0))
                direction_val = data.get('Direction', 'LONG')
                
                cur.execute("""
                    SELECT id FROM trades 
                    WHERE entry_time = ? AND entry_price = ? AND direction = ?
                """, (entry_time_val, entry_price_val, direction_val))
                
                existing = cur.fetchone()
                if existing:
                    print(f'[trade_completed] DUPLICATE DETECTED - Skipping trade EntryBar={data.get("EntryBar")}, EntryTime={entry_time_val}, EntryPrice={entry_price_val}, Direction={direction_val} (existing id={existing[0]})')
                    return JSONResponse({"status": "ok", "message": "Trade already exists (duplicate skipped)", "duplicate": True})
                
                # Check if all new columns exist
                has_all_new_cols = (has_candle_type and has_open_final and has_high_final and 
                                   has_low_final and has_close_final and has_fast_ema and 
                                   has_fast_ema_grad_deg and has_bar_pattern and has_entry_reason)
                
                if has_contracts and has_ema_fast and has_ema_slow and has_ema_fast_value and has_ema_slow_value and has_all_new_cols:
                    db_exec("""
                        INSERT INTO trades (
                            entry_time, entry_bar, direction, entry_price,
                            exit_time, exit_bar, exit_price, bars_held,
                            realized_points, mfe, mae, exit_reason, entry_reason, contracts,
                            ema_fast_period, ema_slow_period, ema_fast_value, ema_slow_value,
                            candle_type, open_final, high_final, low_final, close_final,
                            fast_ema, fast_ema_grad_deg, bar_pattern
                        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
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
                        data.get('ExitReason', ''),
                        entry_reason,
                        int(data.get('Contracts', 0)),
                        int(data.get('EmaFastPeriod', 0)),
                        int(data.get('EmaSlowPeriod', 0)),
                        ema_fast_value,
                        ema_slow_value,
                        candle_type,
                        open_final_val,
                        high_final_val,
                        low_final_val,
                        close_final_val,
                        fast_ema_val,
                        fast_ema_grad_deg_val,
                        bar_pattern
                    ))
                elif has_contracts and has_ema_fast and has_ema_slow:
                    db_exec("""
                        INSERT INTO trades (
                            entry_time, entry_bar, direction, entry_price,
                            exit_time, exit_bar, exit_price, bars_held,
                            realized_points, mfe, mae, exit_reason, contracts,
                            ema_fast_period, ema_slow_period
                        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
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
                        data.get('ExitReason', ''),
                        int(data.get('Contracts', 0)),
                        int(data.get('EmaFastPeriod', 0)),
                        int(data.get('EmaSlowPeriod', 0))
                    ))
                elif has_contracts:
                    db_exec("""
                        INSERT INTO trades (
                            entry_time, entry_bar, direction, entry_price,
                            exit_time, exit_bar, exit_price, bars_held,
                            realized_points, mfe, mae, exit_reason, contracts
                        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
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
                        data.get('ExitReason', ''),
                        int(data.get('Contracts', 0))
                    ))
                else:
                    # Fallback for older schema without contracts column
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
                import traceback
                error_trace = traceback.format_exc()
                print(f"[TRADE_COMPLETED] DB insert failed: {db_ex}")
                print(f"[TRADE_COMPLETED] DB insert traceback: {error_trace}")
        
        # Analyze performance for auto-optimization
        try:
            analyze_trade_performance(data)
        except Exception as perf_ex:
            print(f"[TRADE_COMPLETED] Performance analysis error (non-critical): {perf_ex}")
        
        # Trigger auto-apply evaluation if enabled
        try:
            if AUTO_APPLY_ENABLED:
                evaluate_auto_apply(time.time())
        except Exception as auto_ex:
            print(f"[TRADE_COMPLETED] Auto-apply evaluation error (non-critical): {auto_ex}")
        
        return JSONResponse({'status': 'ok', 'analyzed': True, 'saved_to_db': USE_SQLITE})
    except Exception as ex:
        import traceback
        error_trace = traceback.format_exc()
        print(f"[TRADE_COMPLETED] Error: {ex}")
        print(f"[TRADE_COMPLETED] Traceback: {error_trace}")
        return JSONResponse({'status': 'error', 'message': str(ex)}, status_code=500)

@app.get('/api/trades/stop-loss-analysis')
async def analyze_stop_loss(format: str = 'json'):
    """Analyze trades table to check if stop loss is working correctly.
    Supports format='json' (default) or format='text' for plain text output.
    """
    """Analyze trades table to check if stop loss is working correctly."""
    try:
        if not USE_SQLITE:
            return JSONResponse({'error': 'Database not enabled'}, status_code=500)
        
        import re
        conn = get_db_connection()
        cursor = conn.cursor()
        
        # Get all trades ordered by entry_time DESC (newest first)
        cursor.execute("""
            SELECT 
                id,
                entry_time,
                entry_bar,
                direction,
                entry_price,
                exit_time,
                exit_bar,
                exit_price,
                bars_held,
                realized_points,
                mfe,
                mae,
                exit_reason
            FROM trades
            ORDER BY entry_time DESC
            LIMIT 100
        """)
        
        trades = cursor.fetchall()
        
        if not trades:
            return JSONResponse({
                'total_trades': 0,
                'message': 'No trades found in database'
            })
        
        # Statistics
        total_trades = len(trades)
        stop_loss_trades = 0
        zero_stop_loss = 0
        same_entry_exit_price = 0
        valid_stop_loss = 0
        break_even_stops = 0
        trail_stops = 0
        problematic_trades = []
        
        trade_details = []
        
        for trade in trades:
            trade_id, entry_time, entry_bar, direction, entry_price, exit_time, exit_bar, exit_price, bars_held, realized_points, mfe, mae, exit_reason = trade
            
            # Parse exit_reason to extract stop loss info
            stop_loss_info = None
            stop_loss_points = None
            is_stop_loss = False
            issues = []
            
            if exit_reason:
                # Check if it's a stop loss exit
                if "Stop" in exit_reason or "stop" in exit_reason.lower():
                    is_stop_loss = True
                    stop_loss_trades += 1
                    
                    # Extract stop loss points from exit_reason
                    match = re.search(r'([\d.]+)\s*pts', exit_reason)
                    if match:
                        stop_loss_points = float(match.group(1))
                        stop_loss_info = f"{stop_loss_points:.2f} pts"
                        
                        if stop_loss_points == 0.0:
                            zero_stop_loss += 1
                            issues.append("Zero stop loss")
                        elif stop_loss_points > 0:
                            valid_stop_loss += 1
                    else:
                        stop_loss_info = "Stop (no pts)"
                        issues.append("Stop loss but no points found")
                    
                    # Check for break-even or trail
                    if "BreakEven" in exit_reason or "break-even" in exit_reason.lower():
                        break_even_stops += 1
                    if "Trail" in exit_reason or "trail" in exit_reason.lower():
                        trail_stops += 1
            
            # Check if entry and exit prices are the same
            if entry_price and exit_price and abs(entry_price - exit_price) < 0.01:
                same_entry_exit_price += 1
                issues.append("Entry = Exit price")
            
            # Check if problematic
            if issues:
                problematic_trades.append({
                    'id': trade_id,
                    'entry_price': entry_price,
                    'exit_price': exit_price,
                    'exit_reason': exit_reason,
                    'issues': issues
                })
            
            trade_details.append({
                'id': trade_id,
                'entry_time': entry_time,
                'entry_bar': entry_bar,
                'direction': direction,
                'entry_price': entry_price,
                'exit_time': exit_time,
                'exit_bar': exit_bar,
                'exit_price': exit_price,
                'bars_held': bars_held,
                'realized_points': realized_points,
                'mfe': mfe,
                'mae': mae,
                'exit_reason': exit_reason,
                'stop_loss_points': stop_loss_points,
                'is_stop_loss': is_stop_loss,
                'issues': issues
            })
        
        # Return in requested format
        if format == 'text':
            # Plain text format for easy reading
            text_output = []
            text_output.append("="*100)
            text_output.append(f"TRADES TABLE - STOP LOSS ANALYSIS")
            text_output.append("="*100)
            text_output.append("")
            text_output.append("SUMMARY STATISTICS:")
            text_output.append(f"  Total trades analyzed: {total_trades}")
            text_output.append(f"  Stop loss exits: {stop_loss_trades} ({round(stop_loss_trades / total_trades * 100, 1) if total_trades > 0 else 0}%)")
            text_output.append(f"    - Valid stop loss (>0 pts): {valid_stop_loss}")
            text_output.append(f"    - Zero stop loss (0.00 pts): {zero_stop_loss} ⚠️")
            text_output.append(f"    - Break-even stops: {break_even_stops}")
            text_output.append(f"    - Trail stops: {trail_stops}")
            text_output.append(f"  Trades with same entry/exit price: {same_entry_exit_price} ⚠️")
            text_output.append("")
            
            if problematic_trades:
                text_output.append("="*100)
                text_output.append(f"⚠️  PROBLEMATIC TRADES ({len(problematic_trades)}):")
                text_output.append("-"*100)
                for trade in problematic_trades:
                    text_output.append(f"  Trade ID {trade['id']}: {', '.join(trade['issues'])}")
                    text_output.append(f"    Entry: {trade['entry_price']:.2f}, Exit: {trade['exit_price']:.2f}")
                    text_output.append(f"    Reason: {trade['exit_reason']}")
                    text_output.append("")
            
            text_output.append("="*100)
            text_output.append("RECENT TRADES (Last 20):")
            text_output.append("="*100)
            text_output.append("")
            text_output.append(f"{'ID':<6} {'Dir':<5} {'Entry Price':<12} {'Exit Price':<12} {'Pts':<8} {'Stop Loss':<15} {'Issues':<30}")
            text_output.append("-"*100)
            
            for trade in trade_details[:20]:
                issues_str = ', '.join(trade.get('issues', [])) if trade.get('issues') else 'OK'
                stop_loss_str = f"{trade.get('stop_loss_points', 0):.2f} pts" if trade.get('stop_loss_points') is not None else "N/A"
                text_output.append(f"{trade['id']:<6} {trade.get('direction', 'N/A'):<5} {trade.get('entry_price', 0):<12.2f} {trade.get('exit_price', 0):<12.2f} {trade.get('realized_points', 0):<8.2f} {stop_loss_str:<15} {issues_str:<30}")
            
            return PlainTextResponse('\n'.join(text_output))
        else:
            # JSON format (default)
            return JSONResponse({
                'total_trades': total_trades,
                'statistics': {
                    'stop_loss_exits': stop_loss_trades,
                    'stop_loss_percentage': round(stop_loss_trades / total_trades * 100, 1) if total_trades > 0 else 0,
                    'valid_stop_loss': valid_stop_loss,
                    'zero_stop_loss': zero_stop_loss,
                    'break_even_stops': break_even_stops,
                    'trail_stops': trail_stops,
                    'same_entry_exit_price': same_entry_exit_price
                },
                'problematic_trades': problematic_trades,
                'trades': trade_details[:20]  # Return first 20 for display
            })
        
    except Exception as ex:
        import traceback
        error_trace = traceback.format_exc()
        print(f"[STOP_LOSS_ANALYSIS] Error: {ex}\n{error_trace}")
        return JSONResponse({'error': str(ex), 'trace': error_trace}, status_code=500)

@app.post('/api/trades/clear')
async def clear_trades():
    """Clear all trades from the trades table (for fresh runs)."""
    try:
        if not USE_SQLITE:
            return JSONResponse({'error': 'Database not enabled'}, status_code=500)
        
        conn = get_db_connection()
        cur = conn.cursor()
        
        # Get count before deletion
        cur.execute("SELECT COUNT(*) FROM trades")
        count_before = cur.fetchone()[0]
        
        # Delete all trades
        cur.execute("DELETE FROM trades")
        
        # Reset AUTOINCREMENT counter so IDs start from 1 again
        cur.execute("DELETE FROM sqlite_sequence WHERE name='trades'")
        
        conn.commit()
        
        print(f"[TRADES_CLEAR] Cleared {count_before} trades from database and reset AUTOINCREMENT counter")
        
        return JSONResponse({
            'status': 'ok',
            'message': f'Cleared {count_before} trades',
            'trades_deleted': count_before
        })
    except Exception as ex:
        import traceback
        error_trace = traceback.format_exc()
        print(f"[TRADES_CLEAR] Error: {ex}")
        print(f"[TRADES_CLEAR] Traceback: {error_trace}")
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.post('/api/databases/clear-bars-table')
async def clear_bars_table():
    """Clear all rows from BarsOnTheFlowStateAndBar table (for fresh runs)."""
    try:
        if not os.path.exists(BARS_DB_PATH):
            return JSONResponse({'error': 'Database not found'}, status_code=404)
        
        conn = sqlite3.connect(BARS_DB_PATH)
        cursor = conn.cursor()
        
        # Get count before deletion
        cursor.execute("SELECT COUNT(*) FROM BarsOnTheFlowStateAndBar")
        count_before = cursor.fetchone()[0]
        
        # Delete all rows
        cursor.execute("DELETE FROM BarsOnTheFlowStateAndBar")
        
        # Reset AUTOINCREMENT counter so IDs start from 1 again
        cursor.execute("DELETE FROM sqlite_sequence WHERE name='BarsOnTheFlowStateAndBar'")
        
        conn.commit()
        conn.close()
        
        print(f"[BARS_CLEAR] Cleared {count_before} rows from BarsOnTheFlowStateAndBar and reset AUTOINCREMENT counter")
        
        return JSONResponse({
            'status': 'ok',
            'message': f'Cleared {count_before} rows',
            'rows_deleted': count_before
        })
    except Exception as ex:
        import traceback
        error_trace = traceback.format_exc()
        print(f"[BARS_CLEAR] Error: {ex}")
        print(f"[BARS_CLEAR] Traceback: {error_trace}")
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.post('/api/databases/clear-bar-samples')
async def clear_bar_samples():
    """Clear all rows from bar_samples table (for fresh runs)."""
    try:
        if not os.path.exists(VOLATILITY_DB_PATH):
            return JSONResponse({'error': 'Database not found'}, status_code=404)
        
        conn = sqlite3.connect(VOLATILITY_DB_PATH)
        cursor = conn.cursor()
        
        # Get count before deletion
        cursor.execute("SELECT COUNT(*) FROM bar_samples")
        count_before = cursor.fetchone()[0]
        
        # Delete all rows
        cursor.execute("DELETE FROM bar_samples")
        
        # Reset AUTOINCREMENT counter so IDs start from 1 again
        cursor.execute("DELETE FROM sqlite_sequence WHERE name='bar_samples'")
        
        conn.commit()
        conn.close()
        
        import time
        print(f"[BAR_SAMPLES_CLEAR] *** CLEARED {count_before} rows from bar_samples at {time.strftime('%H:%M:%S')} ***")
        
        return JSONResponse({
            'status': 'ok',
            'message': f'Cleared {count_before} rows',
            'rows_deleted': count_before
        })
    except Exception as ex:
        import traceback
        error_trace = traceback.format_exc()
        print(f"[BAR_SAMPLES_CLEAR] Error: {ex}")
        print(f"[BAR_SAMPLES_CLEAR] Traceback: {error_trace}")
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.post('/api/volatility/clear-trades')
async def clear_volatility_trades():
    """Clear all trades from volatility.db trades table (for fresh runs)."""
    try:
        if not os.path.exists(VOLATILITY_DB_PATH):
            return JSONResponse({'error': 'Database not found'}, status_code=404)
        
        conn = sqlite3.connect(VOLATILITY_DB_PATH)
        cursor = conn.cursor()
        
        # Check if trades table exists
        cursor.execute("SELECT name FROM sqlite_master WHERE type='table' AND name='trades'")
        if not cursor.fetchone():
            conn.close()
            return JSONResponse({
                'status': 'ok',
                'message': 'Trades table does not exist (nothing to clear)',
                'trades_deleted': 0
            })
        
        # Get count before deletion
        cursor.execute("SELECT COUNT(*) FROM trades")
        count_before = cursor.fetchone()[0]
        
        # Delete all trades
        cursor.execute("DELETE FROM trades")
        
        # Reset AUTOINCREMENT counter so IDs start from 1 again
        cursor.execute("DELETE FROM sqlite_sequence WHERE name='trades'")
        
        conn.commit()
        conn.close()
        
        import time
        print(f"[VOLATILITY_TRADES_CLEAR] *** CLEARED {count_before} trades from volatility.db at {time.strftime('%H:%M:%S')} ***")
        
        return JSONResponse({
            'status': 'ok',
            'message': f'Cleared {count_before} trades',
            'trades_deleted': count_before
        })
    except Exception as ex:
        import traceback
        error_trace = traceback.format_exc()
        print(f"[VOLATILITY_TRADES_CLEAR] Error: {ex}")
        print(f"[VOLATILITY_TRADES_CLEAR] Traceback: {error_trace}")
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.post('/api/databases/import-csv')
async def import_csv_to_database(request: Request):
    """Import bar samples from CSV file to database (fire-and-forget).
    
    POST body:
        csv_path: Path to the CSV file to import
    """
    try:
        data = await request.json()
        csv_path = data.get('csv_path', '')
        
        if not csv_path:
            return JSONResponse({'error': 'csv_path is required'}, status_code=400)
        
        if not os.path.exists(csv_path):
            return JSONResponse({'error': f'CSV file not found: {csv_path}'}, status_code=404)
        
        # Fire-and-forget: Start import in background, return immediately
        import subprocess
        import sys
        
        script_path = os.path.join(os.path.dirname(__file__), 'import_csv_to_database.py')
        
        # Start import process in background (don't wait)
        subprocess.Popen(
            [sys.executable, script_path, csv_path],
            cwd=os.path.dirname(__file__),
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL
        )
        
        # Return immediately - import happens in background
        return JSONResponse({
            'status': 'ok',
            'message': 'CSV import started in background (fire-and-forget)',
            'csv_path': csv_path
        })
            
    except Exception as ex:
        print(f'[API] import-csv error: {ex}')
        import traceback
        traceback.print_exc()
        return JSONResponse({'error': str(ex)}, status_code=500)

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
        breakeven_trigger = params.get('breakeven_trigger', 0)
        breakeven_offset = params.get('breakeven_offset', 2)
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
                               short_gradient_threshold, min_movement, breakeven_trigger, 
                               breakeven_offset, streak_type)
        
        # Calculate statistics
        total = len(streaks)
        caught = sum(1 for s in streaks if s['status'] == 'caught')
        missed = sum(1 for s in streaks if s['status'] == 'missed')
        partial = sum(1 for s in streaks if s['status'] == 'partial')
        avg_length = sum(s['length'] for s in streaks) / total if total > 0 else 0
        avg_missed_points = sum(s['missed_points'] for s in streaks if s['missed_points'] > 0) / missed if missed > 0 else 0
        
        # Calculate PnL with break-even awareness
        total_pnl = 0.0
        missed_pnl = 0.0
        breakeven_locked_pnl = 0.0
        
        for streak in streaks:
            if streak['status'] == 'caught':
                # Check if break-even would have triggered
                if breakeven_trigger > 0 and abs(streak['net_movement']) >= breakeven_trigger:
                    # Break-even triggered - lock in partial profit
                    locked_profit = breakeven_offset  # e.g., entry + 2 points
                    remaining_profit = abs(streak['net_movement']) - locked_profit
                    total_pnl += locked_profit
                    breakeven_locked_pnl += locked_profit
                    
                    # Remaining profit would be captured if streak continued favorably
                    # But for conservative estimate, we only count locked profit
                    missed_pnl += remaining_profit
                else:
                    # No break-even or didn't hit trigger - full profit captured
                    total_pnl += abs(streak['net_movement'])
            elif streak['status'] == 'partial':
                # Partial profit (net movement - missed points)
                caught_pnl = abs(streak['net_movement']) - streak['missed_points']
                
                # Check if break-even would have triggered on the caught portion
                if breakeven_trigger > 0 and caught_pnl >= breakeven_trigger:
                    locked_profit = breakeven_offset
                    total_pnl += locked_profit
                    breakeven_locked_pnl += locked_profit
                    missed_pnl += (caught_pnl - locked_profit) + streak['missed_points']
                else:
                    total_pnl += caught_pnl
                    missed_pnl += streak['missed_points']
            elif streak['status'] == 'missed':
                # All profit missed
                missed_pnl += abs(streak['net_movement'])
        
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
            'potential_pnl': potential_pnl,
            'breakeven_locked_pnl': breakeven_locked_pnl,
            'breakeven_enabled': breakeven_trigger > 0
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
                 min_movement, breakeven_trigger, breakeven_offset, streak_type):
    """
    Find directional streaks in the bar data similar to BarsOnTheFlow's 5-bar patterns.
    
    A streak is a sequence of bars with consistent overall direction (net positive/negative movement).
    Break-even logic: if movement >= breakeven_trigger, only breakeven_offset profit is locked.
    """
    streaks = []
    i = 0
    counter_ratio = 0.4  # Allow up to 40% counter-trend bars in a streak
    
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
                # Build pattern string from streak bars (e.g., "3G2B" for 3 good then 2 bad)
                def build_pattern(bar_list):
                    pattern_parts = []
                    current_type = None
                    current_count = 0
                    for bar in bar_list:
                        bar_type = 'G' if bar['candleType'] == 'good' else 'B'
                        if bar_type == current_type:
                            current_count += 1
                        else:
                            if current_type is not None:
                                pattern_parts.append(f"{current_count}{current_type}")
                            current_type = bar_type
                            current_count = 1
                    if current_type is not None:
                        pattern_parts.append(f"{current_count}{current_type}")
                    return ''.join(pattern_parts)

                # Get 5 bars before the streak (if available)
                lookback_n = 5
                lookback_start = max(0, i - lookback_n)
                lookback_bars = bars[lookback_start:i] if i > 0 else []
                lookback_pattern = build_pattern(lookback_bars) if lookback_bars else ''
                streak_pattern = build_pattern(streak_bars)
                full_pattern = (lookback_pattern + '-' if lookback_pattern else '') + streak_pattern

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
                    'pattern': full_pattern,
                    'gradient_ok': gradient_ok
                })
                
                # Skip ahead to avoid overlapping streaks
                i += length
                break
        else:
            i += 1
    
    return streaks

@app.get('/api/strategy-log-files')
def get_strategy_log_files():
    """List available main strategy log CSV files"""
    try:
        files = []
        for filename in os.listdir(LOG_DIR):
            if filename.startswith('BarsOnTheFlow_') and filename.endswith('.csv') and 'Opportunities' not in filename and 'OutputWindow' not in filename and 'FastGradDebug' not in filename:
                filepath = os.path.join(LOG_DIR, filename)
                stat = os.stat(filepath)
                mtime = datetime.fromtimestamp(stat.st_mtime)
                
                try:
                    with open(filepath, 'r', encoding='utf-8') as f:
                        row_count = sum(1 for _ in f) - 1
                except:
                    row_count = 0
                
                files.append({
                    'filename': filename,
                    'timestamp': mtime.strftime('%Y-%m-%d %H:%M:%S'),
                    'bars': row_count,
                    'size': stat.st_size
                })
        
        files.sort(key=lambda x: x['timestamp'], reverse=True)
        return JSONResponse({'files': files, 'count': len(files)})
    except Exception as ex:
        print(f"[STRATEGY_LOG] Error listing files: {ex}")
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.post('/api/analyze-historical-profitability')
async def analyze_historical_profitability(request: Request):
    """Analyze historical strategy run to identify profitable patterns and bad trades"""
    try:
        params = await request.json()
        filename = params.get('filename')
        min_profit = params.get('minProfit', 10.0)
        max_loss = params.get('maxLoss', -20.0)
        analysis_type = params.get('analysisType', 'all')
        
        if not filename:
            return JSONResponse({'error': 'filename required'}, status_code=400)
        
        main_log_path = os.path.join(LOG_DIR, filename)
        if not os.path.exists(main_log_path):
            return JSONResponse({'error': 'log file not found'}, status_code=404)
        
        # Try to find corresponding opportunity log
        opp_filename = filename.replace('BarsOnTheFlow_', 'BarsOnTheFlow_Opportunities_')
        opp_log_path = os.path.join(LOG_DIR, opp_filename)
        has_opp_log = os.path.exists(opp_log_path)
        
        # Parse main log
        trades = []
        bars = []
        with open(main_log_path, 'r', encoding='utf-8') as f:
            reader = csv.DictReader(f)
            for row in reader:
                bar_data = {
                    'bar': int(row['bar']),
                    'timestamp': row['timestamp'],
                    'action': row['action'],
                    'direction': row['direction'],
                    'orderName': row.get('orderName', ''),
                    'quantity': int(row['quantity']) if row['quantity'] else 0,
                    'price': float(row['price']) if row['price'] else 0,
                    'pnl': float(row['pnl']) if row['pnl'] else 0,
                    'reason': row.get('reason', ''),
                    'barPattern': row.get('barPattern', ''),
                    'fastEmaGradDeg': float(row['fastEmaGradDeg']) if row.get('fastEmaGradDeg') and row['fastEmaGradDeg'] not in ('', 'NaN') else None,
                    'open': float(row['open']),
                    'high': float(row['high']),
                    'low': float(row['low']),
                    'close': float(row['close']),
                    'candleType': row.get('candleType', ''),
                    'trendUpAtDecision': row.get('trendUpAtDecision', '') == 'True',
                    'trendDownAtDecision': row.get('trendDownAtDecision', '') == 'True'
                }
                bars.append(bar_data)
                
                # Track entries
                if bar_data['action'] == 'ENTRY':
                    trades.append({
                        'entryBar': bar_data['bar'],
                        'entryTimestamp': bar_data['timestamp'],
                        'direction': bar_data['direction'],
                        'entryPrice': bar_data['price'],
                        'entryPattern': bar_data['barPattern'],
                        'entryGradient': bar_data['fastEmaGradDeg'],
                        'entryTrend': 'UP' if bar_data['trendUpAtDecision'] else 'DOWN' if bar_data['trendDownAtDecision'] else 'NONE',
                        'entryCandleType': bar_data['candleType']
                    })
                # Track exits and complete trades
                elif bar_data['action'] == 'EXIT' and trades:
                    # Find the most recent open trade
                    for trade in reversed(trades):
                        if 'exitBar' not in trade:
                            trade['exitBar'] = bar_data['bar']
                            trade['exitTimestamp'] = bar_data['timestamp']
                            trade['exitPrice'] = bar_data['price']
                            trade['exitReason'] = bar_data['reason']
                            trade['pnl'] = bar_data['pnl']
                            trade['barsHeld'] = trade['exitBar'] - trade['entryBar']
                            break
        
        # Parse opportunity log if available
        opp_data = {}
        if has_opp_log:
            with open(opp_log_path, 'r', encoding='utf-8') as f:
                reader = csv.DictReader(f)
                for row in reader:
                    bar_num = int(row['bar'])
                    opp_data[bar_num] = {
                        'actionTaken': row.get('actionTaken', ''),
                        'blockReason': row.get('blockReason', ''),
                        'opportunityType': row.get('opportunityType', ''),
                        'gradientFilterLong': row.get('gradientFilterLong', '') == 'True',
                        'gradientFilterShort': row.get('gradientFilterShort', '') == 'True'
                    }
        
        # Enrich trades with opportunity data
        for trade in trades:
            if trade['entryBar'] in opp_data:
                opp = opp_data[trade['entryBar']]
                trade['entryAction'] = opp['actionTaken']
                trade['entryBlockReason'] = opp['blockReason']
                trade['opportunityType'] = opp['opportunityType']
        
        # Calculate max profit for each trade (from bars data)
        for trade in trades:
            if 'exitBar' in trade:
                max_profit = 0
                max_loss = 0
                entry_price = trade['entryPrice']
                direction = trade['direction']
                
                # Find bars between entry and exit
                for bar in bars:
                    if trade['entryBar'] <= bar['bar'] <= trade['exitBar']:
                        if direction == 'LONG':
                            profit = bar['high'] - entry_price
                            loss = entry_price - bar['low']
                        else:  # SHORT
                            profit = entry_price - bar['low']
                            loss = bar['high'] - entry_price
                        
                        max_profit = max(max_profit, profit)
                        max_loss = max(max_loss, loss)
                
                trade['maxProfit'] = max_profit
                trade['maxLoss'] = max_loss
                trade['profitCapture'] = (trade['pnl'] / max_profit * 100) if max_profit > 0 else 0
        
        # Categorize trades
        profitable_trades = [t for t in trades if 'pnl' in t and t['pnl'] >= min_profit]
        unprofitable_trades = [t for t in trades if 'pnl' in t and t['pnl'] <= max_loss]
        exit_analysis = []
        for t in trades:
            if 'maxProfit' in t and t['maxProfit'] > 0 and 'pnl' in t and t['pnl'] > 0:
                # Add exitProfit and exitPattern for frontend compatibility
                exit_trade = t.copy()
                exit_trade['exitProfit'] = t['pnl']
                exit_trade['exitPattern'] = t.get('entryPattern', '')  # Use entry pattern as exit pattern for now
                exit_analysis.append(exit_trade)
        
        # Calculate summary stats
        total_trades = len([t for t in trades if 'pnl' in t])
        profitable_count = len(profitable_trades)
        unprofitable_count = len(unprofitable_trades)
        total_pnl = sum(t['pnl'] for t in trades if 'pnl' in t)
        avg_profit = sum(t['pnl'] for t in profitable_trades) / profitable_count if profitable_count > 0 else 0
        avg_loss = sum(t['pnl'] for t in unprofitable_trades) / unprofitable_count if unprofitable_count > 0 else 0
        win_rate = (profitable_count / total_trades * 100) if total_trades > 0 else 0
        profit_factor = abs(avg_profit * profitable_count / (avg_loss * unprofitable_count)) if unprofitable_count > 0 and avg_loss < 0 else 0
        
        # Generate insights
        insights = []
        if profitable_count > 0:
            avg_bars_held_profitable = sum(t['barsHeld'] for t in profitable_trades) / profitable_count
            common_pattern = max(set(t['entryPattern'] for t in profitable_trades if t.get('entryPattern')), key=lambda x: list(t['entryPattern'] for t in profitable_trades if t.get('entryPattern')).count(x), default='N/A')
            insights.append({
                'type': 'success',
                'title': 'Profitable Trade Characteristics',
                'text': f'Average bars held: {avg_bars_held_profitable:.1f} | Most common pattern: {common_pattern} | Avg profit: ${avg_profit:.2f}'
            })
        
        if unprofitable_count > 0:
            avg_bars_held_unprofitable = sum(t['barsHeld'] for t in unprofitable_trades) / unprofitable_count
            common_bad_pattern = max(set(t['entryPattern'] for t in unprofitable_trades if t.get('entryPattern')), key=lambda x: list(t['entryPattern'] for t in unprofitable_trades if t.get('entryPattern')).count(x), default='N/A')
            insights.append({
                'type': 'warning',
                'title': 'Unprofitable Trade Characteristics',
                'text': f'Average bars held: {avg_bars_held_unprofitable:.1f} | Common bad pattern: {common_bad_pattern} | Avg loss: ${avg_loss:.2f}'
            })
        
        # Pattern analysis
        pattern_stats = {}
        for trade in trades:
            if 'pnl' in trade and trade.get('entryPattern'):
                pattern = trade['entryPattern']
                if pattern not in pattern_stats:
                    pattern_stats[pattern] = {'count': 0, 'totalPnl': 0, 'wins': 0}
                pattern_stats[pattern]['count'] += 1
                pattern_stats[pattern]['totalPnl'] += trade['pnl']
                if trade['pnl'] > 0:
                    pattern_stats[pattern]['wins'] += 1
        
        profitable_patterns = []
        unprofitable_patterns = []
        for pattern, stats in pattern_stats.items():
            avg_pnl = stats['totalPnl'] / stats['count']
            win_rate = (stats['wins'] / stats['count'] * 100) if stats['count'] > 0 else 0
            pattern_data = {
                'pattern': pattern,
                'count': stats['count'],
                'avgPnl': avg_pnl,
                'winRate': win_rate
            }
            if avg_pnl > 0:
                profitable_patterns.append(pattern_data)
            else:
                unprofitable_patterns.append(pattern_data)
        
        profitable_patterns.sort(key=lambda x: x['avgPnl'], reverse=True)
        unprofitable_patterns.sort(key=lambda x: x['avgPnl'])
        
        # Add "why bad" analysis for unprofitable trades
        for trade in unprofitable_trades:
            reasons = []
            if trade.get('barsHeld', 0) < 2:
                reasons.append('Exited too early')
            if trade.get('maxProfit', 0) > abs(trade['pnl']) * 2:
                reasons.append('Poor profit capture')
            if trade.get('entryGradient') and trade['direction'] == 'LONG' and trade['entryGradient'] < 0:
                reasons.append('Negative gradient on long entry')
            if trade.get('entryGradient') and trade['direction'] == 'SHORT' and trade['entryGradient'] > 0:
                reasons.append('Positive gradient on short entry')
            trade['whyBad'] = '; '.join(reasons) if reasons else 'Unknown'
        
        return JSONResponse({
            'summary': {
                'totalTrades': total_trades,
                'profitableTrades': profitable_count,
                'unprofitableTrades': unprofitable_count,
                'totalPnl': total_pnl,
                'avgProfit': avg_profit,
                'avgLoss': avg_loss,
                'winRate': win_rate,
                'profitFactor': profit_factor
            },
            'insights': insights,
            'profitableTrades': profitable_trades[:50],  # Limit to 50
            'unprofitableTrades': unprofitable_trades[:50],
            'exitAnalysis': exit_analysis[:50],
            'patternAnalysis': {
                'profitablePatterns': profitable_patterns[:20],
                'unprofitablePatterns': unprofitable_patterns[:20]
            }
        })
    except Exception as ex:
        import traceback
        error_trace = traceback.format_exc()
        print(f"[HISTORICAL_PROFITABILITY] Error: {ex}")
        print(f"[HISTORICAL_PROFITABILITY] Traceback: {error_trace}")
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.get('/api/strategy-log-data')
async def get_strategy_log_data(filename: str):
    """Get bar data from strategy log file for chart visualization"""
    try:
        if not filename:
            return JSONResponse({'error': 'filename required'}, status_code=400)
        
        main_log_path = os.path.join(LOG_DIR, filename)
        if not os.path.exists(main_log_path):
            return JSONResponse({'error': 'log file not found'}, status_code=404)
        
        # Use a dictionary to store the final value for each bar (handles duplicate bar numbers)
        # Prefer rows with action='BAR' as they represent finalized bars (like candles.html does)
        bars_dict = {}
        bars_by_action = {}  # Track BAR action rows separately
        
        with open(main_log_path, 'r', encoding='utf-8') as f:
            reader = csv.DictReader(f)
            for row in reader:
                try:
                    bar_num = int(row['bar'])
                    action = row.get('action', '').upper()
                    
                    # Use Final OHLC values if available, otherwise fall back to regular OHLC
                    open_val = float(row.get('openFinal') or row.get('open') or 0)
                    high_val = float(row.get('highFinal') or row.get('high') or 0)
                    low_val = float(row.get('lowFinal') or row.get('low') or 0)
                    close_val = float(row.get('closeFinal') or row.get('close') or 0)
                    
                    # Skip if no valid price data
                    if open_val == 0 and high_val == 0 and low_val == 0 and close_val == 0:
                        continue
                    
                    bar_data = {
                        'bar': bar_num,
                        'timestamp': row.get('timestamp', ''),
                        'open': open_val,
                        'high': high_val,
                        'low': low_val,
                        'close': close_val,
                        'candleType': row.get('candleType', '')
                    }
                    
                    # Store BAR action rows separately (these are finalized bars)
                    if action == 'BAR':
                        bars_by_action[bar_num] = bar_data
                    # Otherwise, store as fallback (last row for each bar wins)
                    else:
                        bars_dict[bar_num] = bar_data
                except Exception as e:
                    continue
        
        # Use BAR action rows where available, otherwise use the last row for that bar
        final_bars = {}
        all_bar_nums = set(list(bars_by_action.keys()) + list(bars_dict.keys()))
        for bar_num in all_bar_nums:
            if bar_num in bars_by_action:
                final_bars[bar_num] = bars_by_action[bar_num]
            elif bar_num in bars_dict:
                final_bars[bar_num] = bars_dict[bar_num]
        
        # Convert to sorted list by bar number
        bars = sorted(final_bars.values(), key=lambda x: x['bar'])
        
        return JSONResponse({'bars': bars})
    except Exception as ex:
        import traceback
        error_trace = traceback.format_exc()
        print(f"[STRATEGY_LOG_DATA] Error: {ex}")
        print(f"[STRATEGY_LOG_DATA] Traceback: {error_trace}")
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.post('/api/analyze-trends-and-trades')
async def analyze_trends_and_trades(request: Request):
    """Analyze historical data to find trends and match trades to them, identifying optimal parameters"""
    try:
        params = await request.json()
        filename = params.get('filename')
        min_trend_length = params.get('minTrendLength', 3)
        min_trend_movement = params.get('minTrendMovement', 5.0)
        
        if not filename:
            return JSONResponse({'error': 'filename required'}, status_code=400)
        
        main_log_path = os.path.join(LOG_DIR, filename)
        if not os.path.exists(main_log_path):
            return JSONResponse({'error': 'log file not found'}, status_code=404)
        
        # Try to find corresponding opportunity log
        opp_filename = filename.replace('BarsOnTheFlow_', 'BarsOnTheFlow_Opportunities_')
        opp_log_path = os.path.join(LOG_DIR, opp_filename)
        has_opp_log = os.path.exists(opp_log_path)
        
        # Parse main log to get all bars
        bars = []
        with open(main_log_path, 'r', encoding='utf-8') as f:
            reader = csv.DictReader(f)
            for row in reader:
                try:
                    bar_data = {
                        'bar': int(row['bar']),
                        'timestamp': row['timestamp'],
                        'open': float(row['open']),
                        'high': float(row['high']),
                        'low': float(row['low']),
                        'close': float(row['close']),
                        'candleType': row.get('candleType', ''),
                        'direction': row.get('direction', 'FLAT'),
                        'action': row.get('action', ''),
                        'pnl': float(row['pnl']) if row.get('pnl') else 0,
                        'fastEmaGradDeg': float(row['fastEmaGradDeg']) if row.get('fastEmaGradDeg') and row['fastEmaGradDeg'] not in ('', 'NaN') else None,
                        'trendUpAtDecision': row.get('trendUpAtDecision', '') == 'True',
                        'trendDownAtDecision': row.get('trendDownAtDecision', '') == 'True',
                        'barPattern': row.get('barPattern', '')
                    }
                    bars.append(bar_data)
                except:
                    continue
        
        # Parse opportunity log if available
        opp_data = {}
        if has_opp_log:
            with open(opp_log_path, 'r', encoding='utf-8') as f:
                reader = csv.DictReader(f)
                for row in reader:
                    try:
                        bar_num = int(row['bar'])
                        opp_data[bar_num] = {
                            'trendUpSignal': row.get('trendUpSignal', '') == 'True',
                            'trendDownSignal': row.get('trendDownSignal', '') == 'True',
                            'goodCount': int(row.get('goodCount', 0)),
                            'badCount': int(row.get('badCount', 0)),
                            'netPnl': float(row.get('netPnl', 0)),
                            'actionTaken': row.get('actionTaken', ''),
                            'blockReason': row.get('blockReason', ''),
                            'gradientValue': float(row['fastEmaGradDeg']) if row.get('fastEmaGradDeg') and row['fastEmaGradDeg'] not in ('', 'NaN') else None
                        }
                    except:
                        continue
        
        # Find complete trends (one direction from start to end)
        trends = find_complete_trends(bars, opp_data, min_trend_length, min_trend_movement)
        
        # Parse trades from main log - need to re-read to get price field
        trades = []
        current_trade = None
        with open(main_log_path, 'r', encoding='utf-8') as f:
            reader = csv.DictReader(f)
            for row in reader:
                try:
                    bar_num = int(row['bar'])
                    action = row.get('action', '')
                    direction = row.get('direction', '')
                    price_str = row.get('price', '')
                    pnl_str = row.get('pnl', '')
                    
                    if action == 'ENTRY':
                        current_trade = {
                            'entryBar': bar_num,
                            'entryTimestamp': row.get('timestamp', ''),
                            'direction': direction,
                            'entryPrice': float(price_str) if price_str and price_str not in ('', '0') else 0,
                            'entryPattern': row.get('barPattern', ''),
                            'entryGradient': float(row['fastEmaGradDeg']) if row.get('fastEmaGradDeg') and row['fastEmaGradDeg'] not in ('', 'NaN') else None,
                            'entryTrend': 'UP' if row.get('trendUpAtDecision', '') == 'True' else 'DOWN' if row.get('trendDownAtDecision', '') == 'True' else 'NONE'
                        }
                    elif action == 'EXIT' and current_trade:
                        current_trade['exitBar'] = bar_num
                        current_trade['exitTimestamp'] = row.get('timestamp', '')
                        current_trade['exitPrice'] = float(price_str) if price_str and price_str not in ('', '0') else 0
                        current_trade['exitReason'] = row.get('reason', '')
                        current_trade['pnl'] = float(pnl_str) if pnl_str and pnl_str not in ('', '') else 0
                        current_trade['barsHeld'] = current_trade['exitBar'] - current_trade['entryBar']
                        trades.append(current_trade)
                        current_trade = None
                except Exception as e:
                    continue
        
        # Match trades to trends
        matched_trends = match_trades_to_trends(trends, trades, bars)
        
        # Analyze parameters for each trend
        analyzed_trends = analyze_trend_parameters(matched_trends, bars, opp_data)
        
        # Aggregate optimal parameters across all trends
        parameter_summary = aggregate_optimal_parameters(analyzed_trends)
        
        return JSONResponse({
            'trends': analyzed_trends,
            'totalTrends': len(analyzed_trends),
            'totalTrades': len(trades),
            'matchedTrends': len([t for t in analyzed_trends if t.get('tradeMatched')]),
            'parameterSummary': parameter_summary
        })
    except Exception as ex:
        import traceback
        error_trace = traceback.format_exc()
        print(f"[TREND_ANALYSIS] Error: {ex}")
        print(f"[TREND_ANALYSIS] Traceback: {error_trace}")
        return JSONResponse({'error': str(ex)}, status_code=500)

def find_complete_trends(bars, opp_data, min_length=3, min_movement=5.0):
    """Find complete trends - consecutive bars in one direction (good candles = UP, bad candles = DOWN)
    Allows for small pullbacks/neutral bars within a trend to avoid premature ending due to break-even stops"""
    trends = []
    current_trend = None
    neutral_bar_count = 0  # Track consecutive neutral bars
    max_neutral_bars = 2  # Allow up to 2 neutral bars before ending trend
    
    for i, bar in enumerate(bars):
        candle_type = bar.get('candleType', '').lower()
        is_good = candle_type == 'good'
        is_bad = candle_type == 'bad'
        
        # Handle neutral bars (not clearly good or bad)
        if not is_good and not is_bad:
            if current_trend:
                # Allow a few neutral bars within a trend (break-even scenarios, small pullbacks)
                neutral_bar_count += 1
                current_trend['neutralBarsInTrend'] = current_trend.get('neutralBarsInTrend', 0) + 1
                if neutral_bar_count <= max_neutral_bars:
                    # Continue the trend, update end bar but don't add to bars list
                    current_trend['endBar'] = bar['bar']
                    current_trend['endTimestamp'] = bar['timestamp']
                    # Update price tracking but don't add bar to trend bars
                    continue
                else:
                    # Too many neutral bars - end the trend
                    trends.append(current_trend)
                    current_trend = None
                    neutral_bar_count = 0
            continue
        
        # Reset neutral bar count when we see a good/bad candle
        neutral_bar_count = 0
        
        # Determine trend direction from candle type
        # Good candles = upward movement (UP trend for LONG trades)
        # Bad candles = downward movement (DOWN trend for SHORT trades)
        if is_good:
            if current_trend is None or current_trend['direction'] != 'UP':
                # Start new UP trend or switch from DOWN to UP
                if current_trend:
                    trends.append(current_trend)
                current_trend = {
                    'direction': 'UP',
                    'startBar': bar['bar'],
                    'startTimestamp': bar['timestamp'],
                    'startPrice': bar['close'],
                    'endBar': bar['bar'],
                    'endTimestamp': bar['timestamp'],
                    'endPrice': bar['close'],
                    'bars': [bar],
                    'netMovement': 0,
                    'maxProfit': 0,
                    'minLoss': 0
                }
            else:
                # Continue UP trend
                current_trend['endBar'] = bar['bar']
                current_trend['endTimestamp'] = bar['timestamp']
                current_trend['endPrice'] = bar['close']
                current_trend['bars'].append(bar)
                # Calculate movement from start
                current_trend['netMovement'] = bar['close'] - current_trend['startPrice']
                # For LONG trades: profit = high - entry, loss = entry - low
                current_trend['maxProfit'] = max(current_trend['maxProfit'], bar['high'] - current_trend['startPrice'])
                current_trend['minLoss'] = min(current_trend['minLoss'], current_trend['startPrice'] - bar['low'])
        
        elif is_bad:
            if current_trend is None or current_trend['direction'] != 'DOWN':
                # Start new DOWN trend or switch from UP to DOWN
                if current_trend:
                    trends.append(current_trend)
                current_trend = {
                    'direction': 'DOWN',
                    'startBar': bar['bar'],
                    'startTimestamp': bar['timestamp'],
                    'startPrice': bar['close'],
                    'endBar': bar['bar'],
                    'endTimestamp': bar['timestamp'],
                    'endPrice': bar['close'],
                    'bars': [bar],
                    'netMovement': 0,
                    'maxProfit': 0,
                    'minLoss': 0
                }
            else:
                # Continue DOWN trend
                current_trend['endBar'] = bar['bar']
                current_trend['endTimestamp'] = bar['timestamp']
                current_trend['endPrice'] = bar['close']
                current_trend['bars'].append(bar)
                # Calculate movement from start (DOWN = start - end)
                current_trend['netMovement'] = current_trend['startPrice'] - bar['close']
                # For SHORT trades: profit = entry - low, loss = high - entry
                current_trend['maxProfit'] = max(current_trend['maxProfit'], current_trend['startPrice'] - bar['low'])
                current_trend['minLoss'] = min(current_trend['minLoss'], bar['high'] - current_trend['startPrice'])
    
    # Add final trend if exists
    if current_trend:
        trends.append(current_trend)
    
    # Filter trends by minimum length and movement (using parameters passed in)
    filtered_trends = [
        t for t in trends 
        if len(t['bars']) >= min_length and abs(t['netMovement']) >= min_movement
    ]
    
    return filtered_trends

def match_trades_to_trends(trends, trades, bars):
    """Match trades to trends and calculate how well they captured the trend"""
    matched = []
    
    for trend in trends:
        trend_info = trend.copy()
        trend_info['tradeMatched'] = False
        trend_info['trade'] = None
        trend_info['entryBar'] = None
        trend_info['exitBar'] = None
        trend_info['capturedMovement'] = 0
        trend_info['capturePercentage'] = 0
        trend_info['missedMovement'] = abs(trend['netMovement'])
        
        # Find trades that overlap with this trend
        for trade in trades:
            # Check if trade direction matches trend direction
            # UP trend = LONG trades, DOWN trend = SHORT trades
            trade_dir = trade['direction']
            if (trend['direction'] == 'UP' and trade_dir != 'LONG') or \
               (trend['direction'] == 'DOWN' and trade_dir != 'SHORT'):
                continue
            
            # Check if trade overlaps with trend
            trade_start = trade['entryBar']
            trade_end = trade['exitBar']
            trend_start = trend['startBar']
            trend_end = trend['endBar']
            
            # Overlap if trade starts/ends within trend or trend starts/ends within trade
            if (trend_start <= trade_start <= trend_end) or (trend_start <= trade_end <= trend_end) or \
               (trade_start <= trend_start <= trade_end) or (trade_start <= trend_end <= trade_end):
                
                # Calculate how much of the trend was captured
                entry_price = trade['entryPrice']
                exit_price = trade['exitPrice']
                
                if trend['direction'] == 'UP' and trade_dir == 'LONG':
                    # LONG trade in UP trend
                    captured = exit_price - entry_price
                    # Calculate what was missed before entry and after exit
                    missed_before = max(0, entry_price - trend['startPrice'])
                    missed_after = max(0, trend['endPrice'] - exit_price)
                elif trend['direction'] == 'DOWN' and trade_dir == 'SHORT':
                    # SHORT trade in DOWN trend
                    captured = entry_price - exit_price
                    missed_before = max(0, trend['startPrice'] - entry_price)
                    missed_after = max(0, exit_price - trend['endPrice'])
                else:
                    # Mismatch - shouldn't happen but handle gracefully
                    continue
                
                total_missed = missed_before + missed_after
                capture_pct = (captured / abs(trend['netMovement']) * 100) if trend['netMovement'] != 0 else 0
                
                trend_info['tradeMatched'] = True
                trend_info['trade'] = trade
                trend_info['entryBar'] = trade['entryBar']
                trend_info['exitBar'] = trade['exitBar']
                trend_info['capturedMovement'] = captured
                trend_info['capturePercentage'] = capture_pct
                trend_info['missedMovement'] = total_missed
                trend_info['missedBefore'] = missed_before
                trend_info['missedAfter'] = missed_after
                break
        
        matched.append(trend_info)
    
    return matched

def analyze_trend_parameters(matched_trends, bars, opp_data):
    """Analyze what parameters would have been needed to enter/exit each trend optimally"""
    analyzed = []
    
    for trend in matched_trends:
        analysis = trend.copy()
        
        # Find entry parameters needed
        start_bar = trend['startBar']
        start_bar_data = next((b for b in bars if b['bar'] == start_bar), None)
        
        if start_bar_data:
            # What would have triggered entry at trend start?
            entry_params = {
                'trendLookbackBars': 5,  # Default
                'minMatchingBars': 1,  # Would need just 1 bar to trigger
                'avoidLongsOnBadCandle': not (trend['direction'] == 'LONG' and start_bar_data['candleType'] == 'bad'),
                'avoidShortsOnGoodCandle': not (trend['direction'] == 'SHORT' and start_bar_data['candleType'] == 'good'),
                'gradientThreshold': start_bar_data.get('fastEmaGradDeg'),
                'entryGradient': start_bar_data.get('fastEmaGradDeg')
            }
            
            # Check opportunity log for trend signal
            if start_bar in opp_data:
                opp = opp_data[start_bar]
                entry_params['trendUpSignal'] = opp.get('trendUpSignal', False)
                entry_params['trendDownSignal'] = opp.get('trendDownSignal', False)
                entry_params['goodCount'] = opp.get('goodCount', 0)
                entry_params['badCount'] = opp.get('badCount', 0)
                entry_params['netPnl'] = opp.get('netPnl', 0)
                entry_params['actionTaken'] = opp.get('actionTaken', '')
                entry_params['blockReason'] = opp.get('blockReason', '')
            
            analysis['entryParameters'] = entry_params
        
        # Find exit parameters needed
        end_bar = trend['endBar']
        end_bar_data = next((b for b in bars if b['bar'] == end_bar), None)
        
        if end_bar_data:
            # Calculate optimal stop loss (wouldn't have been hit during trend)
            if trend['direction'] == 'UP':
                # For LONG: stop below lowest point
                min_low = min(b['low'] for b in trend['bars'])
                optimal_stop = trend['startPrice'] - (trend['startPrice'] - min_low) * 1.1  # 10% buffer
                stop_distance = trend['startPrice'] - optimal_stop
            else:  # DOWN
                # For SHORT: stop above highest point
                max_high = max(b['high'] for b in trend['bars'])
                optimal_stop = trend['startPrice'] + (max_high - trend['startPrice']) * 1.1  # 10% buffer
                stop_distance = optimal_stop - trend['startPrice']
            
            exit_params = {
                'optimalStopLossPoints': stop_distance,
                'optimalStopLossTicks': int(stop_distance * 4),  # 4 ticks per point for MNQ
                'trendRetraceFraction': 0.0,  # Would exit at trend end (no retrace)
                'exitOnTrendBreak': True,
                'exitBar': end_bar,
                'exitPrice': trend['endPrice']
            }
            
            analysis['exitParameters'] = exit_params
        
        # Calculate what parameters would capture full trend
        if trend['tradeMatched']:
            trade = trend['trade']
            analysis['optimalEntryBar'] = trend['startBar']
            analysis['optimalExitBar'] = trend['endBar']
            analysis['optimalEntryPrice'] = trend['startPrice']
            analysis['optimalExitPrice'] = trend['endPrice']
            analysis['optimalPnL'] = abs(trend['netMovement'])
            analysis['actualPnL'] = trade['pnl']
            analysis['pnlDifference'] = abs(trend['netMovement']) - trade['pnl']
        else:
            analysis['optimalEntryBar'] = trend['startBar']
            analysis['optimalExitBar'] = trend['endBar']
            analysis['optimalEntryPrice'] = trend['startPrice']
            analysis['optimalExitPrice'] = trend['endPrice']
            analysis['optimalPnL'] = abs(trend['netMovement'])
            analysis['actualPnL'] = 0
            analysis['pnlDifference'] = abs(trend['netMovement'])
            analysis['missedOpportunity'] = True
        
        analyzed.append(analysis)
    
    return analyzed

def aggregate_optimal_parameters(analyzed_trends):
    """Aggregate optimal parameters across all trends to find best settings for maximum P&L capture"""
    if not analyzed_trends:
        return {
            'maxCapturablePnL': 0,
            'optimalParameters': {},
            'parameterStats': {},
            'coverage': {}
        }
    
    # Calculate maximum capturable P&L (if all trends were captured optimally)
    max_capturable_pnl = sum(t.get('optimalPnL', 0) for t in analyzed_trends)
    
    # Collect all entry parameters
    entry_params_list = []
    exit_params_list = []
    
    for trend in analyzed_trends:
        if trend.get('entryParameters'):
            entry_params_list.append(trend['entryParameters'])
        if trend.get('exitParameters'):
            exit_params_list.append(trend['exitParameters'])
    
    # Find most common/optimal values for entry parameters
    entry_param_stats = {}
    if entry_params_list:
        # TrendLookbackBars
        lookback_values = [p.get('trendLookbackBars', 5) for p in entry_params_list if p.get('trendLookbackBars')]
        if lookback_values:
            entry_param_stats['trendLookbackBars'] = {
                'min': min(lookback_values),
                'max': max(lookback_values),
                'most_common': max(set(lookback_values), key=lookback_values.count),
                'avg': sum(lookback_values) / len(lookback_values)
            }
        
        # MinMatchingBars
        matching_values = [p.get('minMatchingBars', 1) for p in entry_params_list if p.get('minMatchingBars')]
        if matching_values:
            entry_param_stats['minMatchingBars'] = {
                'min': min(matching_values),
                'max': max(matching_values),
                'most_common': max(set(matching_values), key=matching_values.count),
                'avg': sum(matching_values) / len(matching_values)
            }
        
        # Gradient thresholds
        gradient_values = [p.get('entryGradient') for p in entry_params_list if p.get('entryGradient') is not None]
        if gradient_values:
            entry_param_stats['entryGradient'] = {
                'min': min(gradient_values),
                'max': max(gradient_values),
                'avg': sum(gradient_values) / len(gradient_values)
            }
        
        # AvoidLongsOnBadCandle
        avoid_long_bad = [p.get('avoidLongsOnBadCandle', True) for p in entry_params_list]
        entry_param_stats['avoidLongsOnBadCandle'] = {
            'true_count': sum(avoid_long_bad),
            'false_count': len(avoid_long_bad) - sum(avoid_long_bad),
            'recommended': sum(avoid_long_bad) > len(avoid_long_bad) / 2
        }
        
        # AvoidShortsOnGoodCandle
        avoid_short_good = [p.get('avoidShortsOnGoodCandle', True) for p in entry_params_list]
        entry_param_stats['avoidShortsOnGoodCandle'] = {
            'true_count': sum(avoid_short_good),
            'false_count': len(avoid_short_good) - sum(avoid_short_good),
            'recommended': sum(avoid_short_good) > len(avoid_short_good) / 2
        }
        
        # Block reasons analysis
        block_reasons = [p.get('blockReason', '') for p in entry_params_list if p.get('blockReason')]
        if block_reasons:
            reason_counts = {}
            for reason in block_reasons:
                reason_counts[reason] = reason_counts.get(reason, 0) + 1
            entry_param_stats['commonBlockReasons'] = sorted(reason_counts.items(), key=lambda x: x[1], reverse=True)[:5]
    
    # Find optimal stop loss settings
    exit_param_stats = {}
    if exit_params_list:
        # Stop loss points
        stop_points = [p.get('optimalStopLossPoints') for p in exit_params_list if p.get('optimalStopLossPoints')]
        if stop_points:
            exit_param_stats['optimalStopLossPoints'] = {
                'min': min(stop_points),
                'max': max(stop_points),
                'avg': sum(stop_points) / len(stop_points),
                'median': sorted(stop_points)[len(stop_points) // 2]
            }
        
        # Stop loss ticks
        stop_ticks = [p.get('optimalStopLossTicks') for p in exit_params_list if p.get('optimalStopLossTicks')]
        if stop_ticks:
            exit_param_stats['optimalStopLossTicks'] = {
                'min': min(stop_ticks),
                'max': max(stop_ticks),
                'avg': sum(stop_ticks) / len(stop_ticks),
                'median': sorted(stop_ticks)[len(stop_ticks) // 2]
            }
    
    # Calculate coverage statistics
    total_trends = len(analyzed_trends)
    captured_trends = len([t for t in analyzed_trends if t.get('tradeMatched')])
    fully_captured = len([t for t in analyzed_trends if t.get('tradeMatched') and t.get('capturePercentage', 0) >= 80])
    missed_trends = len([t for t in analyzed_trends if not t.get('tradeMatched')])
    
    # Calculate actual vs optimal P&L
    actual_pnl = sum(t.get('actualPnL', 0) for t in analyzed_trends if t.get('tradeMatched'))
    optimal_pnl = sum(t.get('optimalPnL', 0) for t in analyzed_trends)
    missed_pnl = optimal_pnl - actual_pnl
    
    # Determine recommended parameters (most common values that would capture most trends)
    recommended_params = {
        'entry': {
            'trendLookbackBars': entry_param_stats.get('trendLookbackBars', {}).get('most_common', 5),
            'minMatchingBars': entry_param_stats.get('minMatchingBars', {}).get('most_common', 1),
            'avoidLongsOnBadCandle': entry_param_stats.get('avoidLongsOnBadCandle', {}).get('recommended', True),
            'avoidShortsOnGoodCandle': entry_param_stats.get('avoidShortsOnGoodCandle', {}).get('recommended', True),
            'minGradientThreshold': entry_param_stats.get('entryGradient', {}).get('min') if entry_param_stats.get('entryGradient') else None
        },
        'exit': {
            'stopLossPoints': exit_param_stats.get('optimalStopLossPoints', {}).get('median') if exit_param_stats.get('optimalStopLossPoints') else None,
            'stopLossTicks': exit_param_stats.get('optimalStopLossTicks', {}).get('median') if exit_param_stats.get('optimalStopLossTicks') else None,
            'exitOnTrendBreak': True
        }
    }
    
    return {
        'maxCapturablePnL': max_capturable_pnl,
        'actualPnL': actual_pnl,
        'missedPnL': missed_pnl,
        'optimalParameters': recommended_params,
        'parameterStats': {
            'entry': entry_param_stats,
            'exit': exit_param_stats
        },
    }

@app.get('/api/databases/status')
async def get_databases_status():
    """Get status of all databases and tables with their current state and history."""
    try:
        databases = {}
        # VOLATILITY_DB_PATH is defined later in the file, but will be available at runtime
        volatility_db_path = os.path.join(os.path.dirname(__file__), 'volatility.db')
        db_files = {
            'dashboard.db': DB_PATH,
            'bars.db': BARS_DB_PATH,
            'volatility.db': volatility_db_path
        }
        
        conn = get_db_connection()
        cur = conn.cursor()
        
        for db_name, db_path in db_files.items():
            if not os.path.exists(db_path):
                databases[db_name] = {
                    'exists': False,
                    'tables': {}
                }
                continue
            
            databases[db_name] = {
                'exists': True,
                'path': db_path,
                'tables': {}
            }
            
            # Connect to each database to get table information
            try:
                db_conn = sqlite3.connect(db_path)
                db_cur = db_conn.cursor()
                
                # Get all tables
                db_cur.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")
                tables = [row[0] for row in db_cur.fetchall()]
                
                for table_name in tables:
                    # Get row count (table name is safe - comes from sqlite_master)
                    # Use parameterized query with identifier quoting for safety
                    db_cur.execute(f'SELECT COUNT(*) FROM "{table_name}"')
                    row_count = db_cur.fetchone()[0]
                    
                    # Determine status
                    if row_count == 0:
                        status = 'empty'
                    else:
                        # Check if table is actively being populated (has recent entries)
                        # Look for timestamp columns to determine if actively populating
                        db_cur.execute(f'PRAGMA table_info("{table_name}")')
                        columns = db_cur.fetchall()
                        has_timestamp = any('ts' in col[1].lower() or 'timestamp' in col[1].lower() or 'created_at' in col[1].lower() for col in columns)
                        
                        if has_timestamp:
                            # Check for recent activity (within last hour)
                            timestamp_cols = [col[1] for col in columns if 'ts' in col[1].lower() or 'timestamp' in col[1].lower() or 'created_at' in col[1].lower()]
                            if timestamp_cols:
                                ts_col = timestamp_cols[0]
                                current_time = time.time()
                                hour_ago = current_time - 3600
                                
                                # Try to query recent entries
                                try:
                                    if 'created_at' in ts_col.lower():
                                        db_cur.execute(f'SELECT COUNT(*) FROM "{table_name}" WHERE datetime("{ts_col}") > datetime(\'now\', \'-1 hour\')')
                                    else:
                                        db_cur.execute(f'SELECT COUNT(*) FROM "{table_name}" WHERE "{ts_col}" > ?', (hour_ago,))
                                    recent_count = db_cur.fetchone()[0]
                                    status = 'populating' if recent_count > 0 else 'populated'
                                except:
                                    status = 'populated'
                            else:
                                status = 'populated'
                        else:
                            status = 'populated'
                    
                    # Get responsible strategy from schema documentation or history
                    strategy_name = None
                    
                    # Get last 5 events from history (safely handle if table doesn't exist)
                    history = []
                    try:
                        cur.execute("""
                            SELECT status, strategy_name, event_type, event_details, timestamp, row_count
                            FROM table_status_history
                            WHERE database_name = ? AND table_name = ?
                            ORDER BY timestamp DESC
                            LIMIT 5
                        """, (db_name, table_name))
                        for row in cur.fetchall():
                            history.append({
                                'status': row[0],
                                'strategy_name': row[1],
                                'event_type': row[2],
                                'event_details': row[3],
                                'timestamp': row[4],
                                'row_count': row[5]
                            })
                    except Exception as hist_ex:
                        # Table might not exist yet, that's okay
                        print(f'[DATABASES_STATUS] Could not read history for {db_name}.{table_name}: {hist_ex}')
                        history = []
                    
                    # If no history, try to infer strategy from schema
                    if not strategy_name and not history:
                        # Check schema documentation patterns
                        if 'volatility' in db_name.lower():
                            strategy_name = 'BarsOnTheFlow'
                        elif 'bars' in db_name.lower():
                            strategy_name = 'BarsOnTheFlow'
                        elif 'dashboard' in db_name.lower():
                            strategy_name = 'BarsOnTheFlow'
                    
                    # Get strategy from most recent history entry
                    if not strategy_name and history:
                        strategy_name = history[0].get('strategy_name')
                    
                    databases[db_name]['tables'][table_name] = {
                        'status': status,
                        'row_count': row_count,
                        'strategy_name': strategy_name,
                        'history': history
                    }
                
                db_conn.close()
            except Exception as db_ex:
                databases[db_name]['error'] = str(db_ex)
        
        return JSONResponse(databases)
    except Exception as ex:
        import traceback
        error_trace = traceback.format_exc()
        print(f"[DATABASES_STATUS] Error: {ex}")
        print(f"[DATABASES_STATUS] Traceback: {error_trace}")
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.post('/api/databases/table-event')
async def record_table_event(request: Request):
    """Record an event for a table (populated, emptied, populating, etc.)"""
    try:
        data = await request.json()
        database_name = data.get('database_name')
        table_name = data.get('table_name')
        status = data.get('status')  # 'populated', 'empty', 'populating', 'emptied'
        strategy_name = data.get('strategy_name')
        event_type = data.get('event_type')  # 'populate', 'empty', 'start_populating', 'stop_populating'
        event_details = data.get('event_details', '')
        
        if not database_name or not table_name or not status:
            return JSONResponse({'error': 'database_name, table_name, and status are required'}, status_code=400)
        
        # Get current row count
        volatility_db_path = os.path.join(os.path.dirname(__file__), 'volatility.db')
        db_files = {
            'dashboard.db': DB_PATH,
            'bars.db': BARS_DB_PATH,
            'volatility.db': volatility_db_path
        }
        
        row_count = 0
        if database_name in db_files and os.path.exists(db_files[database_name]):
            try:
                db_conn = sqlite3.connect(db_files[database_name])
                db_cur = db_conn.cursor()
                db_cur.execute(f'SELECT COUNT(*) FROM "{table_name}"')
                row_count = db_cur.fetchone()[0]
                db_conn.close()
            except:
                pass
        
        # Record event in history (safely handle if table doesn't exist)
        try:
            conn = get_db_connection()
            cur = conn.cursor()
            cur.execute("""
                INSERT INTO table_status_history 
                (database_name, table_name, status, strategy_name, row_count, event_type, event_details, timestamp)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """, (database_name, table_name, status, strategy_name, row_count, event_type, event_details, time.time()))
            conn.commit()
        except Exception as insert_ex:
            # Table might not exist yet, that's okay - just log and continue
            print(f'[TABLE_EVENT] Could not record event (table may not exist): {insert_ex}')
            # Don't fail the request - just return success without recording
        
        return JSONResponse({
            'status': 'ok',
            'message': 'Event recorded',
            'row_count': row_count
        })
    except Exception as ex:
        import traceback
        error_trace = traceback.format_exc()
        print(f"[TABLE_EVENT] Error: {ex}")
        print(f"[TABLE_EVENT] Traceback: {error_trace}")
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.get('/api/databases/table-data')
async def get_table_data(database_name: str, table_name: str, limit: int = 1000, offset: int = 0, barIndex: int = None):
    """Get table data with pagination. Optionally filter by barIndex."""
    try:
        # Get database path
        volatility_db_path = os.path.join(os.path.dirname(__file__), 'volatility.db')
        db_files = {
            'dashboard.db': DB_PATH,
            'bars.db': BARS_DB_PATH,
            'volatility.db': volatility_db_path
        }
        
        if database_name not in db_files:
            return JSONResponse({'error': f'Database {database_name} not found'}, status_code=404)
        
        db_path = db_files[database_name]
        if not os.path.exists(db_path):
            return JSONResponse({'error': f'Database file not found: {db_path}'}, status_code=404)
        
        # Connect to database
        db_conn = sqlite3.connect(db_path)
        db_conn.row_factory = sqlite3.Row  # Return rows as dictionaries
        db_cur = db_conn.cursor()
        
        # Get column information
        db_cur.execute(f'PRAGMA table_info("{table_name}")')
        columns = [{'name': row[1], 'type': row[2]} for row in db_cur.fetchall()]
        
        if not columns:
            db_conn.close()
            return JSONResponse({'error': f'Table {table_name} not found'}, status_code=404)
        
        # Build WHERE clause if barIndex filter is provided
        where_clause = ""
        query_params = []
        if barIndex is not None:
            # Check if table has barIndex column (case-insensitive)
            column_names = [col['name'].lower() for col in columns]
            if 'barindex' in column_names or 'bar_index' in column_names:
                # Find the actual column name
                bar_col = next((col['name'] for col in columns if col['name'].lower() in ['barindex', 'bar_index']), None)
                if bar_col:
                    where_clause = f'WHERE "{bar_col}" = ?'
                    query_params = [barIndex]
        
        # Get total row count
        count_query = f'SELECT COUNT(*) FROM "{table_name}"'
        if where_clause:
            count_query += ' ' + where_clause
        db_cur.execute(count_query, query_params)
        total_rows = db_cur.fetchone()[0]
        
        # For BarsOnTheFlowStateAndBar, also get some statistics
        table_stats = {}
        if table_name == 'BarsOnTheFlowStateAndBar':
            try:
                # Get min/max barIndex, unique runs, and date range
                db_cur.execute(f'''
                    SELECT 
                        MIN(barIndex) as min_bar,
                        MAX(barIndex) as max_bar,
                        COUNT(DISTINCT runId) as unique_runs,
                        MIN(receivedTs) as first_ts,
                        MAX(receivedTs) as last_ts,
                        COUNT(DISTINCT barIndex) as unique_bars
                    FROM "{table_name}"
                ''')
                stat_row = db_cur.fetchone()
                if stat_row and stat_row[0] is not None:
                    table_stats = {
                        'min_bar_index': stat_row[0],
                        'max_bar_index': stat_row[1],
                        'unique_runs': stat_row[2] if stat_row[2] else 1,
                        'unique_bars': stat_row[5] if stat_row[5] else 0,
                        'first_timestamp': stat_row[3],
                        'last_timestamp': stat_row[4],
                        'bar_range_size': (stat_row[1] - stat_row[0] + 1) if stat_row[1] and stat_row[0] else 0
                    }
            except Exception as stat_ex:
                print(f'[TABLE_DATA] Could not get stats: {stat_ex}')
                pass
        
        # Get data with pagination - order by most recent first for specific tables
        if table_name == 'BarsOnTheFlowStateAndBar':
            query = f'SELECT * FROM "{table_name}"'
            if where_clause:
                query += ' ' + where_clause
            query += ' ORDER BY receivedTs DESC, barIndex DESC LIMIT ? OFFSET ?'
            db_cur.execute(query, query_params + [limit, offset])
        elif table_name == 'bar_samples':
            # Order bar_samples by timestamp DESC (newest first)
            query = f'SELECT * FROM "{table_name}"'
            if where_clause:
                query += ' ' + where_clause
            query += ' ORDER BY timestamp DESC, created_at DESC LIMIT ? OFFSET ?'
            db_cur.execute(query, query_params + [limit, offset])
        elif table_name == 'trades':
            # Order trades by exit_time DESC (newest first), fallback to entry_time for open trades
            query = f'SELECT * FROM "{table_name}"'
            if where_clause:
                query += ' ' + where_clause
            query += ' ORDER BY COALESCE(exit_time, entry_time) DESC, entry_time DESC LIMIT ? OFFSET ?'
            db_cur.execute(query, query_params + [limit, offset])
        else:
            query = f'SELECT * FROM "{table_name}"'
            if where_clause:
                query += ' ' + where_clause
            query += ' LIMIT ? OFFSET ?'
            db_cur.execute(query, query_params + [limit, offset])
        rows = []
        for row in db_cur.fetchall():
            rows.append(dict(row))
        
        db_conn.close()
        
        return JSONResponse({
            'database_name': database_name,
            'table_name': table_name,
            'columns': columns,
            'total_rows': total_rows,
            'rows': rows,
            'limit': limit,
            'offset': offset,
            'has_more': (offset + len(rows)) < total_rows,
            'table_stats': table_stats
        })
    except Exception as ex:
        import traceback
        error_trace = traceback.format_exc()
        print(f"[TABLE_DATA] Error: {ex}")
        print(f"[TABLE_DATA] Traceback: {error_trace}")
        return JSONResponse({'error': str(ex)}, status_code=500)

@app.get('/table-viewer.html')
async def serve_table_viewer():
    """Serve the table viewer page."""
    try:
        with open('table-viewer.html', 'r', encoding='utf-8') as f:
            return HTMLResponse(content=f.read())
    except FileNotFoundError:
        return HTMLResponse('<h1>Table Viewer page not found</h1>', status_code=404)

@app.get('/database-monitor.html')
async def serve_database_monitor():
    """Serve the database monitoring page."""
    try:
        with open('database-monitor.html', 'r', encoding='utf-8') as f:
            return HTMLResponse(content=f.read())
    except FileNotFoundError:
        return HTMLResponse('<h1>Database Monitor page not found</h1>', status_code=404)

@app.post('/api/databases/reset-bars-table')
async def reset_bars_table_endpoint():
    """Reset (delete and recreate) the BarsOnTheFlowStateAndBar table."""
    try:
        if not os.path.exists(BARS_DB_PATH):
            return JSONResponse({'error': 'Database not found'}, status_code=404)
        
        conn = sqlite3.connect(BARS_DB_PATH)
        cursor = conn.cursor()
        
        # Get current row count
        cursor.execute("SELECT COUNT(*) FROM BarsOnTheFlowStateAndBar")
        row_count = cursor.fetchone()[0]
        
        # Drop the table
        cursor.execute("DROP TABLE IF EXISTS BarsOnTheFlowStateAndBar")
        
        # Drop indexes
        cursor.execute("DROP INDEX IF EXISTS idx_botf_bar")
        cursor.execute("DROP INDEX IF EXISTS idx_botf_ts")
        cursor.execute("DROP INDEX IF EXISTS idx_botf_position")
        cursor.execute("DROP INDEX IF EXISTS idx_botf_currentbar")
        
        # Recreate the table (call init_bars_db to recreate it)
        init_bars_db()
        
        conn.commit()
        conn.close()
        
        return JSONResponse({
            'status': 'ok',
            'message': f'Table reset successfully. Deleted {row_count} rows.',
            'deleted_rows': row_count
        })
        
    except Exception as ex:
        print(f'[RESET_BARS_TABLE] Error: {ex}')
        import traceback
        traceback.print_exc()
        return JSONResponse({'error': str(ex)}, status_code=500)

def find_ninjatrader_db_directory() -> Optional[str]:
    """Find NinjaTrader database directory using multiple methods."""
    possible_paths = []
    
    # Method 1: Use workspace path (most reliable since we know where we are)
    try:
        # Current file is at: web/dashboard/server.py
        # Workspace is at: /Home/Documents/NinjaTrader 8/bin/Custom
        # So db should be at: /Home/Documents/NinjaTrader 8/db
        current_file = __file__
        # Go from web/dashboard/server.py -> web/dashboard -> web -> Custom -> bin -> NinjaTrader 8 -> db
        current_dir = os.path.dirname(current_file)  # web/dashboard
        web_dir = os.path.dirname(current_dir)  # web
        custom_dir = os.path.dirname(web_dir)  # Custom
        bin_dir = os.path.dirname(custom_dir)  # bin
        nt8_dir = os.path.dirname(bin_dir)  # NinjaTrader 8
        db_path = os.path.join(nt8_dir, "db")
        # Normalize path separators
        db_path = os.path.normpath(db_path)
        print(f"[FIND_DB] Checking workspace-derived path: {db_path} (exists: {os.path.exists(db_path)})")
        if os.path.exists(db_path):
            print(f"[FIND_DB] Found database at: {db_path}")
            return db_path
        possible_paths.insert(0, db_path)  # Add at beginning as it's most likely
    except Exception as e:
        print(f"[FIND_DB] Error deriving from workspace: {e}")
        import traceback
        traceback.print_exc()
    
    # Method 2: Standard Windows Documents folder
    try:
        docs_path = os.path.join(os.path.expanduser("~"), "Documents", "NinjaTrader 8", "db")
        possible_paths.append(docs_path)
    except:
        pass
    
    # Method 3: Windows USERPROFILE environment variable
    try:
        userprofile = os.environ.get('USERPROFILE', '')
        if userprofile:
            possible_paths.append(os.path.join(userprofile, "Documents", "NinjaTrader 8", "db"))
    except:
        pass
    
    # Method 4: Try paths with different separators (Windows network share style)
    try:
        # Try C:\Users\mm\Documents\NinjaTrader 8\db
        userprofile = os.environ.get('USERPROFILE', '')
        if userprofile:
            # Try both forward and backslash versions
            possible_paths.append(os.path.join(userprofile, "Documents", "NinjaTrader 8", "db").replace('\\', '/'))
            possible_paths.append(os.path.join(userprofile, "Documents", "NinjaTrader 8", "db").replace('/', '\\'))
    except:
        pass
    
    # Method 5: Common Windows paths
    windows_paths = [
        "C:/Users/Public/Documents/NinjaTrader 8/db",
        "C:\\Users\\Public\\Documents\\NinjaTrader 8\\db",
        os.path.join("C:", "Users", "Public", "Documents", "NinjaTrader 8", "db"),
    ]
    possible_paths.extend(windows_paths)
    
    # Method 6: macOS/Unix paths and Windows network share paths to Mac
    mac_paths = [
        "/Users/mm/Documents/NinjaTrader 8/db",
        os.path.expanduser("~/Documents/NinjaTrader 8/db"),
        "/Home/Documents/NinjaTrader 8/db",  # Based on workspace path
        # Windows network share paths to Mac (common when Mac is shared over network)
        "c:\\Mac\\Home\\Documents\\NinjaTrader 8\\db",
        "c:/Mac/Home/Documents/NinjaTrader 8/db",
        "C:\\Mac\\Home\\Documents\\NinjaTrader 8\\db",
        "C:/Mac/Home/Documents/NinjaTrader 8/db",
        "\\\\Mac\\Home\\Documents\\NinjaTrader 8\\db",
    ]
    possible_paths.extend(mac_paths)
    
    # Method 7: Check for NinjaTrader installation directory
    program_files_paths = [
        os.path.join(os.environ.get('ProgramFiles', ''), 'NinjaTrader 8', 'db'),
        os.path.join(os.environ.get('ProgramFiles(x86)', ''), 'NinjaTrader 8', 'db'),
        "C:/Program Files/NinjaTrader 8/db",
        "C:/Program Files (x86)/NinjaTrader 8/db",
    ]
    possible_paths.extend(program_files_paths)
    
    # Try all paths
    for path in possible_paths:
        if path and os.path.exists(path):
            return path
    
    return None

@app.get('/api/data/download-tick')
async def download_tick_data(
    instrument: str = Query(..., description="Instrument symbol (e.g., 'MNQ 03-26')"),
    start_date: Optional[str] = Query(None, description="Start date (YYYY-MM-DD)"),
    end_date: Optional[str] = Query(None, description="End date (YYYY-MM-DD)"),
    format: str = Query('csv', description="Output format: 'csv' or 'json'"),
    db_path_override: Optional[str] = Query(None, description="Override database path (optional)"),
    max_ticks: Optional[int] = Query(None, description="Maximum number of ticks to return (for large files)")
):
    """
    Download historical tick data from NinjaTrader's local database.
    
    This endpoint attempts to read tick data from NinjaTrader's data files.
    Note: The exact file location and format depends on your data feed provider.
    """
    try:
        # Parse dates
        start_dt = None
        end_dt = None
        if start_date:
            start_dt = datetime.strptime(start_date, '%Y-%m-%d')
        if end_date:
            end_dt = datetime.strptime(end_date, '%Y-%m-%d')
            # Set to end of day
            end_dt = end_dt.replace(hour=23, minute=59, second=59)
        
        # Try to find and read tick data
        tick_data = []
        
        # Find database path
        if db_path_override:
            db_path = db_path_override
            if not os.path.exists(db_path):
                return JSONResponse({
                    'error': 'Specified database path does not exist',
                    'path': db_path
                }, status_code=404)
        else:
            db_path = find_ninjatrader_db_directory()
            if db_path is None:
                # Return helpful error with all paths we tried
                all_tried_paths = []
                try:
                    docs_path = os.path.join(os.path.expanduser("~"), "Documents", "NinjaTrader 8", "db")
                    all_tried_paths.append(docs_path)
                except:
                    pass
                try:
                    userprofile = os.environ.get('USERPROFILE', '')
                    if userprofile:
                        all_tried_paths.append(os.path.join(userprofile, "Documents", "NinjaTrader 8", "db"))
                except:
                    pass
                all_tried_paths.extend([
                    "C:/Users/Public/Documents/NinjaTrader 8/db",
                    "C:\\Users\\Public\\Documents\\NinjaTrader 8\\db",
                    "/Users/mm/Documents/NinjaTrader 8/db",
                ])
                
                return JSONResponse({
                    'error': 'NinjaTrader database directory not found',
                    'message': 'Please ensure NinjaTrader is installed and data has been downloaded.',
                    'searched_paths': all_tried_paths,
                    'suggestion': 'You can specify a custom path using the db_path_override parameter, or check the NinjaTrader installation directory.'
                }, status_code=404)
        
        if db_path is None:
            return JSONResponse({
                'error': 'NinjaTrader database directory not found',
                'message': 'Please ensure NinjaTrader is installed and data has been downloaded.',
                'searched_paths': possible_paths
            }, status_code=404)
        
        # Look for tick data files
        # Format varies by provider - this is a generic approach
        instrument_clean = instrument.replace(" ", "_").replace("/", "-")
        tick_files = []
        found_items = []  # Track what we found for debugging
        
        # Check custom Historical directory first (user's dedicated directory)
        # Try multiple possible paths
        historical_paths = [
            # Path relative to server.py: web/dashboard -> web -> Custom -> Historical
            os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(__file__))), "Historical"),
            # Direct path from workspace root
            os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(__file__)))), "Custom", "Historical"),
            # Windows network share path
            r"\\Mac\Home\Documents\NinjaTrader 8\bin\Custom\Historical",
            # Alternative path format
            r"c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\Historical",
        ]
        
        for historical_dir in historical_paths:
            if os.path.exists(historical_dir):
                try:
                    print(f"[DOWNLOAD_TICK] Checking Historical directory: {historical_dir}")
                    for item in os.listdir(historical_dir):
                        item_path = os.path.join(historical_dir, item)
                        if os.path.isfile(item_path):
                            # Check if file matches instrument
                            if instrument in item or instrument_clean in item:
                                # Prioritize .Last.txt files
                                if item.endswith('.Last.txt') or item.endswith('.last.txt'):
                                    tick_files.insert(0, item_path)  # Highest priority
                                    print(f"[DOWNLOAD_TICK] Found .Last.txt file: {item_path}")
                                elif item.endswith(('.txt', '.csv', '.Last', '.last')):
                                    tick_files.append(item_path)
                    break  # Found the directory, no need to check other paths
                except Exception as e:
                    print(f"[DOWNLOAD_TICK] Error reading Historical directory {historical_dir}: {e}")
                    continue
        
        # Check tick subdirectory
        tick_dir = os.path.join(db_path, "tick")
        if os.path.exists(tick_dir):
            try:
                for item in os.listdir(tick_dir):
                    item_path = os.path.join(tick_dir, item)
                    found_items.append({
                        'name': item,
                        'path': item_path,
                        'is_dir': os.path.isdir(item_path),
                        'is_file': os.path.isfile(item_path)
                    })
                    
                    # Check if item matches instrument
                    if instrument in item or instrument_clean in item:
                        if os.path.isfile(item_path):
                            # Prioritize .Last.txt files (most common text format)
                            if item.endswith('.Last.txt') or item.endswith('.last.txt'):
                                tick_files.insert(0, item_path)  # Add to beginning (highest priority)
                            else:
                                tick_files.append(item_path)
                        elif os.path.isdir(item_path):
                            # It's a directory - look inside for files
                            # NinjaTrader stores tick data in files inside instrument directories
                            try:
                                dir_contents = []
                                for sub_item in os.listdir(item_path):
                                    sub_item_path = os.path.join(item_path, sub_item)
                                    is_file = os.path.isfile(sub_item_path)
                                    is_dir = os.path.isdir(sub_item_path)
                                    dir_contents.append({
                                        'name': sub_item,
                                        'path': sub_item_path,
                                        'is_file': is_file,
                                        'is_dir': is_dir,
                                        'size': os.path.getsize(sub_item_path) if is_file else None
                                    })
                                    
                                    if is_file:
                                        # Accept ANY file in the directory - NinjaTrader might use various formats
                                        # Common tick data file patterns
                                        # NinjaTrader uses: .Last.txt, .Bid.txt, .Ask.txt, .ncd (compressed), or date-based files
                                        # Priority: .Last.txt files are the most common text format
                                        if sub_item.endswith('.Last.txt') or sub_item.endswith('.last.txt'):
                                            tick_files.insert(0, sub_item_path)  # Add .Last.txt files first (highest priority)
                                        elif any(sub_item.endswith(ext) for ext in ['.txt', '.csv', '.Last', '.last', '.bid', '.ask', '.Bid.txt', '.Ask.txt', '.bin', '.dat', '.ncd']):
                                            tick_files.append(sub_item_path)
                                        # Or if it has no extension but matches instrument
                                        elif instrument in sub_item or instrument_clean in sub_item:
                                            tick_files.append(sub_item_path)
                                        # Or if it looks like a date-based file (e.g., 20251228.txt or 20251228)
                                        elif sub_item.replace('.', '').replace('-', '').replace('_', '').isdigit():
                                            tick_files.append(sub_item_path)
                                        # Or if it's a non-empty file (might be tick data), accept it
                                        elif os.path.getsize(sub_item_path) > 0:
                                            # Accept any non-empty file as potential tick data
                                            tick_files.append(sub_item_path)
                                    elif is_dir:
                                        # Nested directory - look one level deeper (e.g., by date)
                                        try:
                                            for deep_item in os.listdir(sub_item_path):
                                                deep_item_path = os.path.join(sub_item_path, deep_item)
                                                if os.path.isfile(deep_item_path):
                                                    if any(deep_item.endswith(ext) for ext in ['.txt', '.csv', '.Last', '.last', '.bin', '.dat']):
                                                        tick_files.append(deep_item_path)
                                                    elif os.path.getsize(deep_item_path) > 0:
                                                        tick_files.append(deep_item_path)
                                        except:
                                            pass
                                
                                # Store directory contents for debugging
                                if not tick_files:
                                    found_items[-1]['directory_contents'] = dir_contents
                            except Exception as e:
                                print(f"[DOWNLOAD_TICK] Error reading directory {item_path}: {e}")
                                found_items[-1]['error'] = str(e)
            except Exception as e:
                print(f"[DOWNLOAD_TICK] Error reading tick directory {tick_dir}: {e}")
        
        # Also check root db directory
        if os.path.exists(db_path):
            try:
                for item in os.listdir(db_path):
                    item_path = os.path.join(db_path, item)
                    if (instrument in item or instrument_clean in item):
                        if os.path.isfile(item_path) and item.endswith(('.txt', '.csv', '.Last', '.last')):
                            tick_files.append(item_path)
                        elif os.path.isdir(item_path) and 'tick' in item.lower():
                            # Directory that might contain tick data
                            try:
                                for sub_item in os.listdir(item_path):
                                    sub_item_path = os.path.join(item_path, sub_item)
                                    if os.path.isfile(sub_item_path) and (instrument in sub_item or instrument_clean in sub_item):
                                        tick_files.append(sub_item_path)
                            except:
                                pass
            except Exception as e:
                print(f"[DOWNLOAD_TICK] Error reading db directory {db_path}: {e}")
        
        if not tick_files:
            # Provide helpful debugging info
            matching_items = [item for item in found_items if instrument in item['name'] or instrument_clean in item['name']]
            
            # Get directory contents for matching items if not already populated
            for item in matching_items:
                if item.get('is_dir') and 'directory_contents' not in item:
                    try:
                        dir_path = item['path']
                        dir_contents = []
                        for sub_item in os.listdir(dir_path):
                            sub_item_path = os.path.join(dir_path, sub_item)
                            try:
                                dir_contents.append({
                                    'name': sub_item,
                                    'is_file': os.path.isfile(sub_item_path),
                                    'is_dir': os.path.isdir(sub_item_path),
                                    'size': os.path.getsize(sub_item_path) if os.path.isfile(sub_item_path) else None
                                })
                            except:
                                dir_contents.append({
                                    'name': sub_item,
                                    'error': 'Could not read file info'
                                })
                        item['directory_contents'] = dir_contents
                    except Exception as e:
                        item['directory_error'] = str(e)
            
            return JSONResponse({
                'error': 'Tick data file not found',
                'message': f'No tick data files found for instrument: {instrument}',
                'searched_path': db_path,
                'tick_directory': tick_dir if os.path.exists(tick_dir) else 'not found',
                'matching_items_found': matching_items,
                'all_items_in_tick_dir': [item['name'] for item in found_items[:20]],  # First 20 items
                'suggestion': 'Ensure historical data has been downloaded in NinjaTrader for this instrument. Tick data files may have different extensions (.txt, .Last, etc.) or may be stored in a different format (binary, database, etc.). Check the directory_contents in matching_items_found to see what files are actually in the directory.'
            }, status_code=404)
        
        # Separate .ncd files (binary) from text files
        ncd_files = [f for f in tick_files if f.endswith('.ncd')]
        text_files = [f for f in tick_files if not f.endswith('.ncd')]
        
        # Read tick data from text files first
        for file_path in text_files:
            try:
                print(f"[DOWNLOAD_TICK] Reading file: {file_path}")
                file_size = os.path.getsize(file_path)
                print(f"[DOWNLOAD_TICK] File size: {file_size / (1024*1024):.2f} MB")
                
                max_lines = 1000000  # Limit to 1 million lines to prevent freezing on huge files
                max_ticks_limit = max_ticks if max_ticks is not None else None  # Store max_ticks parameter in local variable
                lines_read = 0
                lines_skipped = 0
                parse_errors = 0
                sample_parse_errors = []  # Store first few parse errors for debugging
                
                with open(file_path, 'r', encoding='utf-8', errors='ignore') as f:
                    for line_num, line in enumerate(f, 1):
                        if line_num > max_lines:
                            print(f"[DOWNLOAD_TICK] Reached maximum line limit ({max_lines}), stopping read")
                            break
                        
                        # Progress logging every 100k lines
                        if line_num % 100000 == 0:
                            print(f"[DOWNLOAD_TICK] Processed {line_num:,} lines, found {len(tick_data):,} valid ticks, {parse_errors:,} parse errors...")
                        
                        line = line.strip()
                        if not line or line.startswith('#'):
                            lines_skipped += 1
                            continue
                        
                        # Show first few lines for debugging
                        if line_num <= 5:
                            print(f"[DOWNLOAD_TICK] Sample line {line_num}: {line[:80]}")
                        
                        lines_read = line_num
                        
                        # Try to parse different tick data formats
                        # Format 1: yyyyMMdd HHmmss;price;volume (NinjaTrader .Last.txt standard format)
                        # Format 2: timestamp,price,volume
                        # Format 3: yyyyMMdd,HHmmss,price,volume
                        # Format 4: Unix timestamp,price,volume
                        
                        tick_time = None
                        price = None
                        volume = 1
                        
                        # Try semicolon-separated format first (NinjaTrader standard)
                        if ';' in line:
                            parts = line.split(';')
                            if len(parts) >= 3:
                                try:
                                    # Format variations:
                                    # 1. yyyyMMdd HHmmss;price;volume (simple)
                                    # 2. yyyyMMdd HHmmss mmmsss;bid;ask;last;volume (with milliseconds and bid/ask/last)
                                    
                                    date_time_str = parts[0].strip()
                                    if line_num <= 5:
                                        print(f"[DOWNLOAD_TICK] Debug: date_time_str='{date_time_str}', parts count={len(parts)}")
                                    
                                    # Check if it has milliseconds component (format 2)
                                    if ' ' in date_time_str:
                                        date_parts = date_time_str.split()
                                        if len(date_parts) >= 3:
                                            # Format: yyyyMMdd HHmmss mmmsss;bid;ask;last;volume
                                            date_str = f"{date_parts[0]} {date_parts[1]}"
                                            # Parse milliseconds/microseconds
                                            try:
                                                time_component = int(date_parts[2])
                                                # The component appears to be in microseconds (e.g., 0160000 = 160000 microseconds = 160ms)
                                                # Values like 0160000, 0200000 are already in microseconds
                                                # Use directly as microseconds (ensure it's in valid range 0-999999)
                                                microseconds = time_component % 1000000
                                                
                                                tick_time = datetime.strptime(date_str, "%Y%m%d %H%M%S")
                                                tick_time = tick_time.replace(microsecond=microseconds)
                                                
                                                if line_num <= 5:
                                                    print(f"[DOWNLOAD_TICK] Debug: time_component={time_component}, microseconds={microseconds}, tick_time={tick_time}")
                                            except (ValueError, IndexError) as e:
                                                # If milliseconds parsing fails, just use the date/time
                                                if line_num <= 5:
                                                    print(f"[DOWNLOAD_TICK] Error parsing time component: {e}, value: {date_parts[2] if len(date_parts) > 2 else 'N/A'}")
                                                    import traceback
                                                    traceback.print_exc()
                                                tick_time = datetime.strptime(date_str, "%Y%m%d %H%M%S")
                                            
                                            # Get Last price (4th field: bid;ask;last;volume)
                                            # parts[0] = date/time string
                                            # parts[1] = bid
                                            # parts[2] = ask
                                            # parts[3] = last (this is what we want)
                                            # parts[4] = volume
                                            if len(parts) >= 4:
                                                price = float(parts[3].strip())  # Last price
                                                volume = int(float(parts[4].strip())) if len(parts) > 4 and parts[4].strip() else 1
                                                if line_num <= 5:
                                                    print(f"[DOWNLOAD_TICK] Debug: Parsed tick - time={tick_time}, price={price}, volume={volume}")
                                            else:
                                                # Fallback to first price if structure is different
                                                price = float(parts[1].strip())
                                                volume = int(float(parts[2].strip())) if len(parts) > 2 and parts[2].strip() else 1
                                                if line_num <= 5:
                                                    print(f"[DOWNLOAD_TICK] Debug: Using fallback - price={price}, volume={volume}")
                                        elif len(date_parts) == 2:
                                            # Format: yyyyMMdd HHmmss;price;volume (simple format)
                                            tick_time = datetime.strptime(date_time_str, "%Y%m%d %H%M%S")
                                            price = float(parts[1].strip())
                                            volume = int(float(parts[2].strip())) if len(parts) > 2 and parts[2].strip() else 1
                                        else:
                                            raise ValueError("Unexpected date format")
                                    elif len(date_time_str) == 14:
                                        # Format: yyyyMMddHHmmss (no space)
                                        tick_time = datetime.strptime(date_time_str, "%Y%m%d%H%M%S")
                                        price = float(parts[1].strip())
                                        volume = int(float(parts[2].strip())) if len(parts) > 2 and parts[2].strip() else 1
                                    else:
                                        # Try as Unix timestamp
                                        tick_time = datetime.fromtimestamp(float(date_time_str))
                                        price = float(parts[1].strip())
                                        volume = int(float(parts[2].strip())) if len(parts) > 2 and parts[2].strip() else 1
                                except (ValueError, IndexError) as e:
                                    # Skip this line, try comma format below
                                    if line_num <= 10:  # Log first few errors
                                        print(f"[DOWNLOAD_TICK] Parse error on line {line_num}: {e}, line: {line[:60]}")
                                        import traceback
                                        traceback.print_exc()
                                    parse_errors += 1
                                    if len(sample_parse_errors) < 5:
                                        sample_parse_errors.append(f"Line {line_num}: {str(e)} - {line[:60]}")
                                    pass
                        
                        # Try comma-separated format
                        if tick_time is None and ',' in line:
                            parts = line.split(',')
                            if len(parts) >= 2:
                                try:
                                    if ' ' in parts[0] and len(parts[0].strip()) >= 15:
                                        # Format: yyyyMMdd HHmmss,price,volume
                                        tick_time = datetime.strptime(parts[0].strip(), "%Y%m%d %H%M%S")
                                        price = float(parts[1].strip())
                                        volume = int(float(parts[2].strip())) if len(parts) > 2 and parts[2].strip() else 1
                                    else:
                                        # Unix timestamp format
                                        tick_time = datetime.fromtimestamp(float(parts[0].strip()))
                                        price = float(parts[1].strip())
                                        volume = int(float(parts[2].strip())) if len(parts) > 2 and parts[2].strip() else 1
                                except (ValueError, IndexError):
                                    pass
                        
                        # Try space-separated format
                        if tick_time is None:
                            parts = line.split()
                            if len(parts) >= 2:
                                try:
                                    if len(parts) >= 3 and ' ' in f"{parts[0]} {parts[1]}":
                                        # Format: yyyyMMdd HHmmss price volume
                                        tick_time = datetime.strptime(f"{parts[0]} {parts[1]}", "%Y%m%d %H%M%S")
                                        price = float(parts[2])
                                        volume = int(float(parts[3])) if len(parts) > 3 else 1
                                    else:
                                        # Unix timestamp
                                        tick_time = datetime.fromtimestamp(float(parts[0]))
                                        price = float(parts[1])
                                        volume = int(float(parts[2])) if len(parts) > 2 else 1
                                except (ValueError, IndexError):
                                    pass
                        
                        # If we successfully parsed the line
                        if tick_time is not None and price is not None:
                            # Filter by date range
                            if start_dt and tick_time < start_dt:
                                continue
                            if end_dt and tick_time > end_dt:
                                continue
                            
                            tick_data.append({
                                'timestamp': tick_time.isoformat(),
                                'price': price,
                                'volume': volume
                            })
                            
                            # Limit results if max_ticks parameter is specified
                            if max_ticks_limit and len(tick_data) >= max_ticks_limit:
                                print(f"[DOWNLOAD_TICK] Reached max_ticks limit ({max_ticks_limit}), stopping read")
                                break
                        
                        # If parsing failed, skip the line silently (we already tried all formats)
                        if tick_time is None:
                            parse_errors += 1
                            if len(sample_parse_errors) < 5:
                                sample_parse_errors.append(f"Line {line_num}: {line[:60]}")
                
                print(f"[DOWNLOAD_TICK] Finished reading file. Total lines: {lines_read:,}, Valid ticks: {len(tick_data):,}, Parse errors: {parse_errors:,}, Skipped: {lines_skipped:,}")
                if parse_errors > 0 and len(tick_data) == 0:
                    print(f"[DOWNLOAD_TICK] WARNING: All lines failed to parse. Sample errors:")
                    for err in sample_parse_errors:
                        print(f"  {err}")
                
                # If we found data, break (use first file that has data)
                if tick_data:
                    break
            except Exception as e:
                print(f"[DOWNLOAD_TICK] Error reading {file_path}: {e}")
                import traceback
                traceback.print_exc()
                continue
        
        # If no data was parsed, provide helpful error message
        if not tick_data:
            # Check if we tried to read text files
            if text_files:
                # We found text files but couldn't parse them
                # Try to read a sample line to show the format
                sample_line = None
                if text_files:
                    try:
                        with open(text_files[0], 'r', encoding='utf-8', errors='ignore') as f:
                            for i, line in enumerate(f):
                                if i >= 5:  # Get a few sample lines
                                    break
                                line = line.strip()
                                if line and not line.startswith('#'):
                                    sample_line = line[:100]  # First 100 chars
                                    break
                    except:
                        pass
                
                return JSONResponse({
                    'error': 'No tick data parsed',
                    'message': f'Found text file(s) but could not parse any tick data. This might indicate a format issue.',
                    'text_files_found': text_files,
                    'sample_line': sample_line,
                    'suggestion': 'Please check the file format. Expected formats:\n'
                                  '- yyyyMMdd HHmmss;price;volume (semicolon-separated)\n'
                                  '- yyyyMMdd HHmmss,price,volume (comma-separated)\n'
                                  '- Unix timestamp,price,volume'
                }, status_code=400)
            
            # Check for .ncd files
            if ncd_files:
                # .ncd files are NinjaTrader's proprietary binary format
                # Check if there are exported CSV files in the tick_export directory
                export_dir = os.path.join(db_path, "tick_export")
                exported_files = []
                if os.path.exists(export_dir):
                    try:
                        for file in os.listdir(export_dir):
                            if file.startswith(instrument.replace(" ", "_").replace("/", "-")) and file.endswith('.csv'):
                                exported_files.append(os.path.join(export_dir, file))
                    except:
                        pass
                
                if exported_files:
                    # Use exported CSV files instead
                    tick_files = exported_files
                    # Continue to read from CSV files (code below will handle this)
                else:
                    # .ncd files are NinjaTrader's proprietary binary format
                    # We cannot read them directly without NinjaTrader's API
                    return JSONResponse({
                        'error': 'Tick data in binary format (.ncd)',
                        'message': f'Found {len(ncd_files)} .ncd files for {instrument}, but these are NinjaTrader\'s proprietary binary format and cannot be read directly.',
                        'files_found': ncd_files[:10],  # Show first 10 files
                        'total_ncd_files': len(ncd_files),
                        'export_directory': export_dir,
                        'suggestion': 'To export tick data from .ncd files, you can:\n'
                                      '1. Use the TickDataExporter AddOn in NinjaTrader to export data to CSV format\n'
                                      '2. Use NinjaTrader\'s Historical Data Manager to export data to CSV/text format\n'
                                      '3. The exported CSV files will be automatically detected in the tick_export directory'
                    }, status_code=400)
            
            # No files found at all
            return JSONResponse({
                'error': 'No tick data found',
                'message': f'No tick data found for {instrument} in the specified date range.',
                'files_checked': tick_files,
                'text_files': text_files,
                'ncd_files': ncd_files[:10] if ncd_files else []
            }, status_code=404)
        
        # Return data in requested format
        if format.lower() == 'json':
            return JSONResponse({
                'instrument': instrument,
                'count': len(tick_data),
                'start_date': start_date,
                'end_date': end_date,
                'data': tick_data
            })
        else:
            # CSV format
            output = io.StringIO()
            writer = csv.DictWriter(output, fieldnames=['timestamp', 'price', 'volume'])
            writer.writeheader()
            for tick in tick_data:
                writer.writerow(tick)
            output.seek(0)
            
            filename = f"{instrument_clean}_tick_data_{start_date or 'all'}_{end_date or 'all'}.csv"
            return StreamingResponse(
                iter([output.getvalue()]),
                media_type="text/csv",
                headers={"Content-Disposition": f"attachment; filename={filename}"}
            )
    
    except Exception as ex:
        import traceback
        error_trace = traceback.format_exc()
        print(f'[DOWNLOAD_TICK_DATA] Error: {ex}\n{error_trace}')
        return JSONResponse({
            'error': str(ex),
            'trace': error_trace
        }, status_code=500)

@app.get('/api/data/find-db-path')
async def find_db_path_diagnostic():
    """
    Diagnostic endpoint to help find the NinjaTrader database directory.
    Returns information about paths checked and suggestions.
    """
    try:
        # Try to find the path
        db_path = find_ninjatrader_db_directory()
        
        # Get all paths we checked
        checked_paths = []
        
        # Workspace-derived path
        try:
            current_file = __file__
            current_dir = os.path.dirname(current_file)
            web_dir = os.path.dirname(current_dir)
            custom_dir = os.path.dirname(web_dir)
            bin_dir = os.path.dirname(custom_dir)
            nt8_dir = os.path.dirname(bin_dir)
            derived_path = os.path.join(nt8_dir, "db")
            derived_path = os.path.normpath(derived_path)
            checked_paths.append({
                'path': derived_path,
                'exists': os.path.exists(derived_path),
                'type': 'workspace-derived'
            })
        except Exception as e:
            checked_paths.append({
                'path': 'error',
                'exists': False,
                'type': 'workspace-derived',
                'error': str(e)
            })
        
        # Other common paths
        other_paths = [
            os.path.join(os.path.expanduser("~"), "Documents", "NinjaTrader 8", "db"),
            os.path.join(os.environ.get('USERPROFILE', ''), "Documents", "NinjaTrader 8", "db") if os.environ.get('USERPROFILE') else None,
            "C:/Users/Public/Documents/NinjaTrader 8/db",
            "/Users/mm/Documents/NinjaTrader 8/db",
            "/Home/Documents/NinjaTrader 8/db",
        ]
        
        for path in other_paths:
            if path:
                checked_paths.append({
                    'path': path,
                    'exists': os.path.exists(path),
                    'type': 'common-path'
                })
        
        # Check if any parent directories exist
        parent_info = []
        try:
            current_file = __file__
            current_dir = os.path.dirname(current_file)
            web_dir = os.path.dirname(current_dir)
            custom_dir = os.path.dirname(web_dir)
            bin_dir = os.path.dirname(custom_dir)
            nt8_dir = os.path.dirname(bin_dir)
            
            parent_info = [
                {'name': 'web/dashboard', 'path': current_dir, 'exists': os.path.exists(current_dir)},
                {'name': 'web', 'path': web_dir, 'exists': os.path.exists(web_dir)},
                {'name': 'Custom', 'path': custom_dir, 'exists': os.path.exists(custom_dir)},
                {'name': 'bin', 'path': bin_dir, 'exists': os.path.exists(bin_dir)},
                {'name': 'NinjaTrader 8', 'path': nt8_dir, 'exists': os.path.exists(nt8_dir)},
            ]
        except:
            pass
        
        return JSONResponse({
            'found': db_path is not None,
            'db_path': db_path,
            'checked_paths': checked_paths,
            'parent_directories': parent_info,
            'suggestion': 'If no path was found, check your NinjaTrader installation directory. The db folder should be at: [NinjaTrader 8 Installation]/db'
        })
    except Exception as ex:
        import traceback
        return JSONResponse({
            'error': str(ex),
            'trace': traceback.format_exc()
        }, status_code=500)

@app.get('/api/data/list-instruments')
async def list_available_instruments(
    db_path_override: Optional[str] = Query(None, description="Override database path (optional)")
):
    """
    List available instruments with tick data in NinjaTrader's database.
    """
    try:
        # Find database path
        if db_path_override:
            db_path = db_path_override
            if not os.path.exists(db_path):
                return JSONResponse({
                    'error': 'Specified database path does not exist',
                    'path': db_path
                }, status_code=404)
        else:
            db_path = find_ninjatrader_db_directory()
            if db_path is None:
                return JSONResponse({
                    'error': 'NinjaTrader database directory not found',
                    'message': 'Use db_path_override parameter to specify the path manually.',
                    'suggestion': 'The database is typically located at: [Your Documents]/NinjaTrader 8/db'
                }, status_code=404)
        
        instruments = set()
        
        # Check tick directory
        tick_dir = os.path.join(db_path, "tick")
        if os.path.exists(tick_dir):
            for file in os.listdir(tick_dir):
                # Extract instrument name from filename
                # Format: "MNQ 03-26.Last.txt" -> "MNQ 03-26"
                name = file.replace('.Last.txt', '').replace('.txt', '').replace('_', ' ')
                if name:
                    instruments.add(name)
        
        # Also check root directory
        if os.path.exists(db_path):
            for file in os.listdir(db_path):
                if file.endswith(('.Last', '.txt', '.csv')):
                    name = file.replace('.Last', '').replace('.txt', '').replace('.csv', '').replace('_', ' ')
                    if name and not name.startswith('.'):
                        instruments.add(name)
        
        return JSONResponse({
            'db_path': db_path,
            'instruments': sorted(list(instruments)),
            'count': len(instruments)
        })
    
    except Exception as ex:
        import traceback
        error_trace = traceback.format_exc()
        print(f'[LIST_INSTRUMENTS] Error: {ex}\n{error_trace}')
        return JSONResponse({
            'error': str(ex),
            'trace': error_trace
        }, status_code=500)

@app.on_event("startup")
async def startup_event():
    """Run migrations and initialization on server startup."""
    try:
        # Run volatility database migration
        try:
            from migrate_volatility_db import migrate_volatility_db
            migrate_volatility_db()
        except ImportError:
            print('[STARTUP] Migration module not found, skipping')
        except Exception as mig_ex:
            print(f'[STARTUP] Migration warning: {mig_ex}')
    except Exception as ex:
        print(f'[STARTUP] Startup event error: {ex}')
        # Don't fail startup if migration fails - might be first run or other issue

if __name__ == '__main__':
    import uvicorn
    # Default to dashboard port used by server_manager (can override via PORT env)
    port = int(os.environ.get('PORT', '51888'))
    uvicorn.run(app, host='127.0.0.1', port=port)
