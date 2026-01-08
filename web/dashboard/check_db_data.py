"""Quick check if database has bar data."""
import os
import sqlite3

db_path = os.path.join(os.path.dirname(__file__), 'volatility.db')

if not os.path.exists(db_path):
    print(f"❌ Database not found: {db_path}")
    exit(1)

conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# Count bars
cursor.execute("SELECT COUNT(*) FROM bar_samples")
count = cursor.fetchone()[0]

# Get bar range
cursor.execute("SELECT MIN(bar_index), MAX(bar_index) FROM bar_samples WHERE bar_index IS NOT NULL")
min_max = cursor.fetchone()

# Get sample of recent bars
cursor.execute("SELECT bar_index, timestamp, symbol FROM bar_samples ORDER BY bar_index DESC LIMIT 10")
recent = cursor.fetchall()

print("=" * 60)
print("DATABASE STATUS")
print("=" * 60)
print(f"Database: {db_path}")
print(f"Total bars in database: {count}")
if min_max[0]:
    print(f"Bar range: {min_max[0]} to {min_max[1]}")
else:
    print("Bar range: No data")

print("\nRecent 10 bars:")
for bar in recent:
    print(f"  Bar {bar[0]}: {bar[2]} at {bar[1]}")

conn.close()

if count > 0:
    print("\n✅ Database has data! Check bar_debug.html")
else:
    print("\n❌ Database is empty. Check server logs for errors.")
