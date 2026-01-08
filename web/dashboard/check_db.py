"""Simple script to check bar_samples table count and import CSV if needed."""
import os
import sqlite3

# Database path
VOLATILITY_DB_PATH = os.path.join(os.path.dirname(__file__), 'volatility.db')

def main():
    print(f"Database path: {VOLATILITY_DB_PATH}")
    print(f"Database exists: {os.path.exists(VOLATILITY_DB_PATH)}")
    
    if not os.path.exists(VOLATILITY_DB_PATH):
        print("Database not found!")
        return
    
    conn = sqlite3.connect(VOLATILITY_DB_PATH)
    cursor = conn.cursor()
    
    # Check bar_samples count
    cursor.execute("SELECT COUNT(*) FROM bar_samples")
    count = cursor.fetchone()[0]
    print(f"bar_samples rows: {count}")
    
    # Check max bar_index
    cursor.execute("SELECT MAX(bar_index) FROM bar_samples")
    max_bar = cursor.fetchone()[0]
    print(f"Max bar_index: {max_bar}")
    
    # Check some sample rows
    if count > 0:
        cursor.execute("SELECT bar_index, timestamp, candle_type, entry_reason FROM bar_samples ORDER BY bar_index DESC LIMIT 5")
        print("\nLast 5 bars:")
        for row in cursor.fetchall():
            print(f"  Bar {row[0]}: {row[1]}, {row[2]}, reason={row[3]}")
    
    conn.close()

if __name__ == "__main__":
    main()
