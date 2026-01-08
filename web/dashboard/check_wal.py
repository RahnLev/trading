"""Check WAL files and checkpoint if needed."""
import os
import sqlite3

db_path = os.path.join(os.path.dirname(__file__), 'volatility.db')
wal_path = db_path + '-wal'
shm_path = db_path + '-shm'

print("=" * 60)
print("CHECKING WAL FILES")
print("=" * 60)
print(f"Database: {db_path}")
print(f"WAL file exists: {os.path.exists(wal_path)}")
print(f"SHM file exists: {os.path.exists(shm_path)}")
print()

if os.path.exists(wal_path):
    wal_size = os.path.getsize(wal_path)
    print(f"⚠️  WAL file found! Size: {wal_size / 1024:.2f} KB")
    print("   This means there are uncommitted transactions.")
    print("   Let's checkpoint them...")
    print()
    
    try:
        conn = sqlite3.connect(db_path)
        # Check journal mode
        cursor = conn.cursor()
        cursor.execute("PRAGMA journal_mode")
        journal_mode = cursor.fetchone()[0]
        print(f"Journal mode: {journal_mode}")
        
        # Checkpoint WAL to main database
        print("Running checkpoint...")
        cursor.execute("PRAGMA wal_checkpoint(TRUNCATE)")
        result = cursor.fetchone()
        print(f"Checkpoint result: {result}")
        
        # Count rows before and after
        cursor.execute("SELECT COUNT(*) FROM bar_samples")
        count_before = cursor.fetchone()[0]
        print(f"Rows in database: {count_before}")
        
        conn.commit()
        conn.close()
        
        # Check WAL size after checkpoint
        if os.path.exists(wal_path):
            wal_size_after = os.path.getsize(wal_path)
            print(f"WAL file size after checkpoint: {wal_size_after / 1024:.2f} KB")
        else:
            print("✅ WAL file removed (all transactions committed)")
            
    except Exception as e:
        print(f"Error during checkpoint: {e}")
        import traceback
        traceback.print_exc()
else:
    print("✅ No WAL file - all transactions are committed")
    
    # Still check row count
    try:
        conn = sqlite3.connect(db_path)
        cursor = conn.cursor()
        cursor.execute("SELECT COUNT(*) FROM bar_samples")
        count = cursor.fetchone()[0]
        print(f"Rows in database: {count}")
        conn.close()
    except Exception as e:
        print(f"Error checking database: {e}")

print()
print("=" * 60)
