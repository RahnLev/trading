"""
Migrate trade CSV files to SQLite database for fast analysis.
Run this once to import historical trades, then strategy will write directly to DB.
"""

import sqlite3
import csv
import os
import glob
from datetime import datetime

DB_PATH = os.path.join(os.path.dirname(__file__), 'web', 'dashboard', 'dashboard.db')

def init_trades_table():
    """Create trades table with comprehensive schema."""
    conn = sqlite3.connect(DB_PATH)
    cur = conn.cursor()
    
    # Main trades table
    cur.execute("""
    CREATE TABLE IF NOT EXISTS trades (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        entry_time REAL NOT NULL,
        entry_bar INTEGER,
        direction TEXT NOT NULL,
        entry_price REAL NOT NULL,
        exit_time REAL NOT NULL,
        exit_bar INTEGER,
        exit_price REAL NOT NULL,
        bars_held INTEGER,
        realized_points REAL,
        mfe REAL,
        mae REAL,
        exit_reason TEXT,
        pending_used INTEGER DEFAULT 0,
        confirm_delta REAL,
        min_hold_bars INTEGER,
        min_entry_fast_grad REAL,
        validation_min_fast_grad REAL,
        exit_confirm_fast_ema_delta REAL,
        fast_exit_thresh_long REAL,
        fast_exit_thresh_short REAL,
        strategy_version TEXT,
        symbol TEXT,
        imported_at REAL DEFAULT (strftime('%s', 'now'))
    )
    """)
    
    # Index for fast queries
    cur.execute("CREATE INDEX IF NOT EXISTS idx_trades_entry_time ON trades(entry_time)")
    cur.execute("CREATE INDEX IF NOT EXISTS idx_trades_direction ON trades(direction)")
    cur.execute("CREATE INDEX IF NOT EXISTS idx_trades_exit_reason ON trades(exit_reason)")
    cur.execute("CREATE INDEX IF NOT EXISTS idx_trades_bars_held ON trades(bars_held)")
    
    # Entry blocks table for entry analysis
    cur.execute("""
    CREATE TABLE IF NOT EXISTS entry_blocks (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        block_time REAL NOT NULL,
        block_bar INTEGER,
        fast_grad REAL,
        adx REAL,
        rsi REAL,
        blockers TEXT,
        trend_side TEXT,
        move_after_block REAL,
        move_bars INTEGER DEFAULT 10,
        strategy_version TEXT,
        symbol TEXT,
        created_at REAL DEFAULT (strftime('%s', 'now'))
    )
    """)
    
    cur.execute("CREATE INDEX IF NOT EXISTS idx_entry_blocks_time ON entry_blocks(block_time)")
    cur.execute("CREATE INDEX IF NOT EXISTS idx_entry_blocks_blockers ON entry_blocks(blockers)")
    
    conn.commit()
    conn.close()
    print("[DB] Trades tables created successfully")

def parse_timestamp(ts_str):
    """Parse timestamp from various formats."""
    try:
        # Try ISO format with milliseconds
        dt = datetime.strptime(ts_str, "%Y-%m-%d %H:%M:%S.%f")
        return dt.timestamp()
    except:
        try:
            # Try ISO format without milliseconds
            dt = datetime.strptime(ts_str, "%Y-%m-%d %H:%M:%S")
            return dt.timestamp()
        except:
            try:
                # Try short format
                dt = datetime.strptime(ts_str, "%Y-%m-%d %H:%M")
                return dt.timestamp()
            except:
                return None

def import_trades_summary_csv(csv_path):
    """Import trades from TRADES_SUMMARY CSV file."""
    conn = sqlite3.connect(DB_PATH)
    cur = conn.cursor()
    
    imported_count = 0
    skipped_count = 0
    
    print(f"[IMPORT] Reading {csv_path}...")
    
    with open(csv_path, 'r') as f:
        reader = csv.DictReader(f)
        
        for row in reader:
            try:
                # Parse timestamps
                entry_ts = parse_timestamp(row.get('EntryTime', ''))
                exit_ts = parse_timestamp(row.get('ExitTime', ''))
                
                if not entry_ts or not exit_ts:
                    skipped_count += 1
                    continue
                
                # Check if already imported (avoid duplicates)
                cur.execute("SELECT id FROM trades WHERE entry_time = ? AND exit_time = ?", (entry_ts, exit_ts))
                if cur.fetchone():
                    skipped_count += 1
                    continue
                
                # Insert trade
                cur.execute("""
                    INSERT INTO trades (
                        entry_time, entry_bar, direction, entry_price,
                        exit_time, exit_bar, exit_price, bars_held,
                        realized_points, mfe, mae, exit_reason,
                        pending_used, confirm_delta, min_hold_bars,
                        min_entry_fast_grad, validation_min_fast_grad,
                        exit_confirm_fast_ema_delta, fast_exit_thresh_long, fast_exit_thresh_short
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """, (
                    entry_ts,
                    int(row.get('EntryBar', 0)) if row.get('EntryBar') else None,
                    row.get('Direction', ''),
                    float(row.get('EntryPrice', 0)),
                    exit_ts,
                    int(row.get('ExitBar', 0)) if row.get('ExitBar') else None,
                    float(row.get('ExitPrice', 0)),
                    int(row.get('BarsHeld', 0)) if row.get('BarsHeld') else None,
                    float(row.get('RealizedPoints', 0)) if row.get('RealizedPoints') else None,
                    float(row.get('MFE', 0)) if row.get('MFE') else None,
                    float(row.get('MAE', 0)) if row.get('MAE') else None,
                    row.get('ExitReason', ''),
                    int(row.get('PendingUsed', 0)) if row.get('PendingUsed') else 0,
                    float(row.get('ConfirmDelta', 0)) if row.get('ConfirmDelta') else None,
                    int(row.get('MinHoldBars', 0)) if row.get('MinHoldBars') else None,
                    float(row.get('MinEntryFastGrad', 0)) if row.get('MinEntryFastGrad') else None,
                    float(row.get('ValidationMinFastGrad', 0)) if row.get('ValidationMinFastGrad') else None,
                    float(row.get('ExitConfirmFastEMADelta', 0)) if row.get('ExitConfirmFastEMADelta') else None,
                    float(row.get('FastExitThreshLong', 0)) if row.get('FastExitThreshLong') else None,
                    float(row.get('FastExitThreshShort', 0)) if row.get('FastExitThreshShort') else None
                ))
                
                imported_count += 1
                
            except Exception as ex:
                print(f"[ERROR] Failed to import row: {ex}")
                skipped_count += 1
    
    conn.commit()
    conn.close()
    
    return imported_count, skipped_count

def main():
    """Main migration process."""
    print("=" * 80)
    print("TRADE CSV TO DATABASE MIGRATION")
    print("=" * 80)
    
    # Initialize tables
    init_trades_table()
    
    # Find all TRADES_SUMMARY CSV files
    csv_pattern = os.path.join(os.path.dirname(__file__), 'strategy_logs', '*TRADES_SUMMARY*.csv')
    csv_files = glob.glob(csv_pattern)
    
    if not csv_files:
        print("[WARNING] No TRADES_SUMMARY CSV files found in strategy_logs/")
        print(f"[INFO] Searched pattern: {csv_pattern}")
        return
    
    print(f"[INFO] Found {len(csv_files)} CSV file(s) to import")
    print()
    
    total_imported = 0
    total_skipped = 0
    
    for csv_file in csv_files:
        filename = os.path.basename(csv_file)
        imported, skipped = import_trades_summary_csv(csv_file)
        total_imported += imported
        total_skipped += skipped
        print(f"  {filename}: {imported} imported, {skipped} skipped")
    
    print()
    print("=" * 80)
    print(f"MIGRATION COMPLETE")
    print(f"Total imported: {total_imported}")
    print(f"Total skipped: {total_skipped} (duplicates or invalid)")
    print("=" * 80)
    
    # Verify
    conn = sqlite3.connect(DB_PATH)
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM trades")
    count = cur.fetchone()[0]
    cur.execute("SELECT MIN(entry_time), MAX(entry_time) FROM trades")
    min_ts, max_ts = cur.fetchone()
    conn.close()
    
    print(f"\n[VERIFY] Database now contains {count} trades")
    if min_ts and max_ts:
        min_date = datetime.fromtimestamp(min_ts).strftime("%Y-%m-%d %H:%M")
        max_date = datetime.fromtimestamp(max_ts).strftime("%Y-%m-%d %H:%M")
        print(f"[VERIFY] Date range: {min_date} to {max_date}")
    
    print("\n[SUCCESS] You can now query trades with SQL!")
    print("[NEXT] Update strategy to write to DB using HTTP POST to /trade_completed")

if __name__ == '__main__':
    main()
