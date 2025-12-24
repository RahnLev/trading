import sqlite3
import os

os.chdir(os.path.dirname(os.path.abspath(__file__)))

for db_name in ['volatility.db', 'dashboard.db', 'bars.db']:
    if os.path.exists(db_name):
        conn = sqlite3.connect(db_name)
        cursor = conn.cursor()
        cursor.execute("SELECT name FROM sqlite_master WHERE type='table'")
        tables = [row[0] for row in cursor.fetchall()]
        print(f"{db_name}: {tables}")
        
        # Show schema for each table
        for table in tables:
            cursor.execute(f"PRAGMA table_info({table})")
            cols = [(row[1], row[2]) for row in cursor.fetchall()]
            print(f"  {table}: {cols}")
        conn.close()
    else:
        print(f"{db_name}: NOT FOUND")
