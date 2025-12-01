import sqlite3
import datetime

DB_PATH = 'web/dashboard/dashboard.db'

conn = sqlite3.connect(DB_PATH)
cur = conn.cursor()

print("\n=== RECENT DIAGNOSTICS (Last 20) ===\n")
cur.execute('''
    SELECT ts, barIndex, fastGrad, rsi, adx, gradStab, bandwidth, volume, 
           blockersLong, blockersShort, trendSide 
    FROM diags 
    ORDER BY id DESC 
    LIMIT 20
''')
rows = cur.fetchall()

if not rows:
    print("No diagnostics found in database.")
else:
    for r in rows:
        ts, barIdx, fastGrad, rsi, adx, gradStab, bw, vol, blockL, blockS, trend = r
        time_str = datetime.datetime.fromtimestamp(ts).strftime('%H:%M:%S')
        print(f"Bar {barIdx} | {time_str} | FastGrad: {fastGrad:+.3f} | RSI: {rsi:.1f} | ADX: {adx:.1f} | GradStab: {gradStab:.3f} | BW: {bw:.4f} | Vol: {vol:.0f}")
        print(f"  → BlockersLong:  {blockL}")
        print(f"  → BlockersShort: {blockS}")
        print(f"  → Trend: {trend}\n")

print("\n=== ENTRY CANCELLATIONS (Last 10) ===\n")
cur.execute('''
    SELECT ts, barIndex, fastGrad, rsi, adx, gradStab, bandwidth, volume,
           blockersLong, blockersShort, trendSide, effectiveMinGrad, effectiveRsiFloor,
           weakGradStreak, rsiBelowStreak
    FROM entry_cancellations
    ORDER BY id DESC
    LIMIT 10
''')
cancellations = cur.fetchall()

if not cancellations:
    print("No entry cancellations logged.")
else:
    for c in cancellations:
        ts, barIdx, fastGrad, rsi, adx, gradStab, bw, vol, blockL, blockS, trend, minGrad, rsiFloor, weakStreak, rsiStreak = c
        time_str = datetime.datetime.fromtimestamp(ts).strftime('%H:%M:%S')
        print(f"Bar {barIdx} | {time_str} | FastGrad: {fastGrad:+.3f} | RSI: {rsi:.1f}")
        print(f"  → Effective MinGrad: {minGrad:.3f} | RSI Floor: {rsiFloor:.1f}")
        print(f"  → WeakGradStreak: {weakStreak} | RSIBelowStreak: {rsiStreak}")
        print(f"  → BlockersLong: {blockL}")
        print(f"  → BlockersShort: {blockS}\n")

conn.close()
