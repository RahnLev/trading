"""Check if trades table exists and has data in volatility.db"""
import sqlite3
import os

db_path = os.path.join(os.path.dirname(__file__), 'volatility.db')

print("=" * 80)
print("CHECKING TRADES TABLE IN VOLATILITY.DB")
print("=" * 80)
print(f"\nDatabase path: {db_path}")
print(f"Database exists: {os.path.exists(db_path)}")
print()

if not os.path.exists(db_path):
    print("❌ Database file does not exist!")
    exit(1)

try:
    conn = sqlite3.connect(db_path)
    cursor = conn.cursor()
    
    # Check if trades table exists
    cursor.execute("SELECT name FROM sqlite_master WHERE type='table' AND name='trades'")
    table_exists = cursor.fetchone() is not None
    
    print(f"Trades table exists: {table_exists}")
    
    if table_exists:
        # Get table schema
        cursor.execute("PRAGMA table_info(trades)")
        columns = cursor.fetchall()
        print(f"\nTable columns ({len(columns)}):")
        for col in columns:
            print(f"  - {col[1]} ({col[2]})")
        
        # Count rows
        cursor.execute("SELECT COUNT(*) FROM trades")
        count = cursor.fetchone()[0]
        print(f"\nTotal trades: {count}")
        
        if count > 0:
            # Show latest trades
            cursor.execute("""
                SELECT entry_bar, exit_bar, direction, entry_price, exit_price, 
                       realized_points, exit_reason, entry_reason
                FROM trades 
                ORDER BY entry_time DESC 
                LIMIT 10
            """)
            rows = cursor.fetchall()
            print("\nLatest 10 trades:")
            print("-" * 80)
            for row in rows:
                print(f"EntryBar={row[0]}, ExitBar={row[1]}, {row[2]}, "
                      f"Entry={row[3]:.2f}, Exit={row[4]:.2f}, "
                      f"Points={row[5]:.2f}, ExitReason={row[6]}, EntryReason={row[7]}")
        else:
            print("\n⚠️  Table exists but is empty!")
    else:
        print("\n❌ Trades table does not exist!")
        print("The endpoint should create it automatically on first trade.")
    
    conn.close()
    
except Exception as e:
    print(f"\n❌ ERROR: {e}")
    import traceback
    traceback.print_exc()
