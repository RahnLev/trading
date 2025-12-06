import sqlite3
import json

db_path = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard\dashboard.db'
conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# Check recent trades
print("=== Recent Trades ===")
cursor.execute("""
    SELECT entry_bar, exit_bar, direction, entry_price, exit_price, exit_reason
    FROM trades 
    ORDER BY exit_bar DESC 
    LIMIT 5
""")
for row in cursor.fetchall():
    entry_bar, exit_bar, direction, entry_price, exit_price, exit_reason = row
    print(f"{direction} trade: Entry bar {entry_bar} @ ${entry_price:.2f}, Exit bar {exit_bar} @ ${exit_price:.2f}")
    print(f"  Exit reason: {exit_reason}")
    print()

# Check if there are current positions in the diag data
print("\n=== Latest Diagnostic Entries ===")
cursor.execute("""
    SELECT barIndex, ts, entryReadyLong, entryReadyShort, inLong, inShort
    FROM diags
    ORDER BY barIndex DESC
    LIMIT 10
""")

diags = cursor.fetchall()
for bar_idx, ts, ready_long, ready_short, in_long, in_short in diags:
    pos = "FLAT"
    if in_long == 1:
        pos = "LONG"
    elif in_short == 1:
        pos = "SHORT"
    print(f"Bar {bar_idx}: Position={pos}, ReadyL={ready_long}, ReadyS={ready_short}")

conn.close()

print("\nNote: The web dashboard reads from the server's in-memory cache (bar_cache)")
print("which gets populated from POST /diag requests from the strategy.")
print("If you just entered a trade, make sure:")
print("1. The strategy is actively sending diagnostics")
print("2. The server is receiving them (check server logs)")
print("3. The web page is connected via WebSocket")
