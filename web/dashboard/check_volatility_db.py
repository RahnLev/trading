"""Quick check of volatility database status"""
import sqlite3
import os

DB_PATH = os.path.join(os.path.dirname(__file__), 'volatility.db')

conn = sqlite3.connect(DB_PATH)
cur = conn.cursor()

# List tables
print("=== TABLES ===")
cur.execute("SELECT name FROM sqlite_master WHERE type='table'")
for row in cur.fetchall():
    print(f"  - {row[0]}")

# Count bar samples
print("\n=== BAR SAMPLES ===")
cur.execute("SELECT COUNT(*) FROM bar_samples")
print(f"Total: {cur.fetchone()[0]}")

# Last 5 samples
print("\n=== LAST 5 BAR SAMPLES ===")
cur.execute("""
    SELECT id, timestamp, bar_index, symbol, hour_of_day, 
           open_price, high_price, low_price, close_price, volume,
           bar_range, direction, in_trade
    FROM bar_samples 
    ORDER BY id DESC LIMIT 5
""")
for row in cur.fetchall():
    print(f"  ID {row[0]}: Bar {row[2]} at {row[1]}")
    print(f"    Symbol: {row[3]}, Hour: {row[4]}")
    print(f"    OHLC: {row[5]:.2f}/{row[6]:.2f}/{row[7]:.2f}/{row[8]:.2f}, Vol: {row[9]}")
    print(f"    Range: {row[10]:.4f}, Dir: {row[11]}, InTrade: {row[12]}")

# Volatility stats
print("\n=== VOLATILITY STATS BY HOUR ===")
cur.execute("""
    SELECT hour_of_day, sample_count, avg_bar_range, avg_volume
    FROM volatility_stats 
    WHERE symbol='MNQ'
    ORDER BY hour_of_day
""")
rows = cur.fetchall()
if rows:
    for row in rows:
        print(f"  Hour {row[0]:02d}: {row[1]} samples, avg_range={row[2]:.4f}, avg_vol={row[3]:.0f}")
else:
    print("  No aggregated stats yet")

# Stop loss recommendations
print("\n=== STOP LOSS RECOMMENDATIONS ===")
try:
    cur.execute("""
        SELECT hour_of_day, recommended_stop_ticks, confidence_level
        FROM stop_loss_recommendations 
        WHERE symbol='MNQ'
        ORDER BY hour_of_day
    """)
    rows = cur.fetchall()
    if rows:
        for row in rows:
            print(f"  Hour {row[0]:02d}: {row[1]} ticks ({row[2]})")
    else:
        print("  No recommendations yet")
except Exception as e:
    print(f"  Error: {e}")

conn.close()
print("\nDone!")
