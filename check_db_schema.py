import sqlite3
import json

# Connect to database
db_path = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard\dashboard.db'
conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# Get table schema
cursor.execute("PRAGMA table_info(trades)")
columns = cursor.fetchall()
print("=== Trades Table Columns ===")
for col in columns:
    print(f"  {col[1]} ({col[2]})")

# Check recent trades
cursor.execute("""
    SELECT *
    FROM trades 
    ORDER BY exit_bar DESC 
    LIMIT 3
""")

rows = cursor.fetchall()
print(f"\n=== Recent {len(rows)} Trades ===")
for row in rows:
    print(row)

# Check diag table structure
print("\n=== Diag Table Columns ===")
cursor.execute("PRAGMA table_info(diag)")
diag_columns = cursor.fetchall()
for col in diag_columns:
    print(f"  {col[1]} ({col[2]})")

# Check if logJson column exists and has EXIT_TRACE
cursor.execute("""
    SELECT COUNT(*) 
    FROM diag
    WHERE logJson IS NOT NULL AND logJson != ''
""")
log_json_count = cursor.fetchone()[0]
print(f"\nDiag rows with logJson: {log_json_count}")

conn.close()
