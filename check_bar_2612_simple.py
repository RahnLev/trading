import sqlite3
import json

db_path = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard\dashboard.db'
conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# Get schema
cursor.execute("SELECT sql FROM sqlite_master WHERE type='table'")
schemas = cursor.fetchall()
print("=== DATABASE SCHEMA ===")
for schema in schemas:
    print(schema[0])
    print()

# Get diag data for bar 2612
cursor.execute("""
    SELECT barIndex, time, open, high, low, close, fastEMA, slowEMA, fastGrad, slowGrad
    FROM diags
    WHERE barIndex BETWEEN 2610 AND 2615
    ORDER BY barIndex
""")

diags = cursor.fetchall()
if diags:
    print("\n=== DIAGNOSTIC DATA AROUND BAR 2612 ===")
    for diag in diags:
        bar_idx, time, open_p, high, low, close, fast_ema, slow_ema, fast_grad, slow_grad = diag
        green = "🟢 GREEN" if close > open_p else "🔴 RED"
        print(f"\nBar {bar_idx} {green} @ {time}")
        print(f"  OHLC: Open={open_p:.2f}, High={high:.2f}, Low={low:.2f}, Close={close:.2f}")
        print(f"  FastEMA: {fast_ema:.2f}, SlowEMA: {slow_ema:.2f}")
        print(f"  FastGrad: {fast_grad:.4f}, SlowGrad: {slow_grad:.4f}")
        print(f"  Close vs EMAs: {'Above' if close > fast_ema else 'Below'} FastEMA, {'Above' if close > slow_ema else 'Below'} SlowEMA")

conn.close()
