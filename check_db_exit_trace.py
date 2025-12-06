import sqlite3
import json

# Connect to database
db_path = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard\dashboard.db'
conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# Check if we have trades logged
cursor.execute("SELECT COUNT(*) FROM trades")
trade_count = cursor.fetchone()[0]
print(f"Total trades in database: {trade_count}")

# Check recent trades
cursor.execute("""
    SELECT 
        tradeId, entryBar, exitBar, direction, entryPrice, exitPrice, profitTicks
    FROM trades 
    ORDER BY exitBar DESC 
    LIMIT 5
""")

print("\n=== Recent Trades ===")
for row in cursor.fetchall():
    trade_id, entry_bar, exit_bar, direction, entry_price, exit_price, profit_ticks = row
    print(f"\nTrade {trade_id}: {direction}")
    print(f"  Entry: Bar {entry_bar} @ ${entry_price:.2f}")
    print(f"  Exit: Bar {exit_bar} @ ${exit_price:.2f}")
    print(f"  Profit: {profit_ticks} ticks")

# Check if we have diag entries for recent exit bars
print("\n=== Checking EXIT_TRACE in Database (via diag.logJson) ===")
cursor.execute("""
    SELECT barIndex, logJson
    FROM diag
    WHERE logJson LIKE '%EXIT_TRACE%'
    ORDER BY barIndex DESC
    LIMIT 5
""")

exit_trace_rows = cursor.fetchall()
print(f"Found {len(exit_trace_rows)} diag rows with EXIT_TRACE in logJson")

if exit_trace_rows:
    for bar_idx, log_json in exit_trace_rows:
        print(f"\nBar {bar_idx}:")
        print(f"  LogJson preview: {log_json[:200]}...")

conn.close()
