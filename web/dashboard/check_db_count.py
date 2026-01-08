"""Check exact row count in bar_samples table."""
import os
import sqlite3

db_path = os.path.join(os.path.dirname(__file__), 'volatility.db')

if not os.path.exists(db_path):
    print(f"❌ Database not found: {db_path}")
    exit(1)

conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# Count all rows
cursor.execute("SELECT COUNT(*) FROM bar_samples")
total_count = cursor.fetchone()[0]

# Count by symbol
cursor.execute("SELECT symbol, COUNT(*) FROM bar_samples GROUP BY symbol")
by_symbol = cursor.fetchall()

# Get all bar_indexes
cursor.execute("SELECT bar_index, timestamp, symbol FROM bar_samples ORDER BY bar_index")
all_bars = cursor.fetchall()

# Get min/max
cursor.execute("SELECT MIN(bar_index), MAX(bar_index) FROM bar_samples WHERE bar_index IS NOT NULL")
min_max = cursor.fetchone()

print("=" * 60)
print("EXACT DATABASE COUNT")
print("=" * 60)
print(f"Total rows in bar_samples: {total_count}")
print()

if by_symbol:
    print("Rows by symbol:")
    for symbol, count in by_symbol:
        print(f"  {symbol}: {count} rows")
    print()

if min_max[0] is not None:
    print(f"Bar index range: {min_max[0]} to {min_max[1]}")
    print()

print(f"All {len(all_bars)} bars:")
for bar in all_bars:
    print(f"  Bar {bar[0]}: {bar[2]} at {bar[1]}")

conn.close()

print()
print("=" * 60)
if total_count == 0:
    print("❌ Database is EMPTY - no bars recorded")
elif total_count < 10:
    print(f"⚠️  Only {total_count} bars in database (expected more)")
else:
    print(f"✅ Database has {total_count} bars")
