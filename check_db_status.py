import sqlite3
import datetime
import os

DB_PATH = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard\dashboard.db'

if not os.path.exists(DB_PATH):
    print(f"ERROR: Database not found at {DB_PATH}")
    exit(1)

conn = sqlite3.connect(DB_PATH)
cur = conn.cursor()

# Check database tables
cur.execute("SELECT name FROM sqlite_master WHERE type='table'")
tables = cur.fetchall()
print(f"\n=== TABLES IN DATABASE ===")
for t in tables:
    print(f"  - {t[0]}")

# Count diagnostics
cur.execute('SELECT COUNT(*) FROM diags')
count = cur.fetchone()[0]
print(f"\n=== TOTAL DIAGNOSTICS: {count} ===\n")

if count == 0:
    print("No diagnostics in database. The strategy may not be posting them, or the server is not receiving them.")
    print("\nPossible causes:")
    print("  1. Strategy 'StreamBarDiagnostics' parameter is set to False")
    print("  2. Strategy is not running or not processing new bars")
    print("  3. Server is not running or not accessible at http://127.0.0.1:5001")
    print("  4. Network/firewall blocking localhost connections")
else:
    print("=== LAST 5 DIAGNOSTICS ===\n")
    cur.execute('''
        SELECT ts, barIndex, fastGrad, rsi, adx, gradStab, bandwidth, volume, 
               blockersLong, blockersShort, trendSide 
        FROM diags 
        ORDER BY id DESC 
        LIMIT 5
    ''')
    rows = cur.fetchall()
    for r in rows:
        ts, barIdx, fastGrad, rsi, adx, gradStab, bw, vol, blockL, blockS, trend = r
        time_str = datetime.datetime.fromtimestamp(ts).strftime('%Y-%m-%d %H:%M:%S')
        print(f"Bar {barIdx} at {time_str}")
        print(f"  FastGrad: {fastGrad:+.3f} | RSI: {rsi:.1f} | ADX: {adx:.1f} | GradStab: {gradStab:.3f}")
        print(f"  Bandwidth: {bw:.4f} | Volume: {vol:.0f}")
        print(f"  BlockersLong:  {blockL}")
        print(f"  BlockersShort: {blockS}")
        print(f"  Trend: {trend}\n")

# Check entry cancellations
cur.execute('SELECT COUNT(*) FROM entry_cancellations')
cancel_count = cur.fetchone()[0]
print(f"\n=== TOTAL ENTRY CANCELLATIONS: {cancel_count} ===\n")

if cancel_count > 0:
    print("=== LAST 3 ENTRY CANCELLATIONS ===\n")
    cur.execute('''
        SELECT ts, barIndex, fastGrad, rsi, adx, effectiveMinGrad, effectiveRsiFloor,
               weakGradStreak, rsiBelowStreak, blockersLong, blockersShort
        FROM entry_cancellations
        ORDER BY id DESC
        LIMIT 3
    ''')
    cancels = cur.fetchall()
    for c in cancels:
        ts, barIdx, fastGrad, rsi, adx, minGrad, rsiFloor, weakStreak, rsiStreak, blockL, blockS = c
        time_str = datetime.datetime.fromtimestamp(ts).strftime('%Y-%m-%d %H:%M:%S')
        print(f"Bar {barIdx} at {time_str}")
        print(f"  FastGrad: {fastGrad:+.3f} (required: >{minGrad:.3f})")
        print(f"  RSI: {rsi:.1f} (required: >{rsiFloor:.1f})")
        print(f"  WeakGradStreak: {weakStreak} | RSIBelowStreak: {rsiStreak}")
        print(f"  BlockersLong: {blockL}")
        print(f"  BlockersShort: {blockS}\n")

conn.close()
print("\nCheck complete.")
