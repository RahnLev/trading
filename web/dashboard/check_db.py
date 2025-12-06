import sqlite3

try:
    conn = sqlite3.connect('bars.db')
    cursor = conn.cursor()
    
    # Get all tables
    cursor.execute("SELECT name FROM sqlite_master WHERE type='table'")
    tables = cursor.fetchall()
    print('Tables in bars.db:', [t[0] for t in tables])
    
    # Check rows in each table
    for table in tables:
        table_name = table[0]
        cursor.execute(f'SELECT COUNT(*) FROM {table_name}')
        count = cursor.fetchone()[0]
        print(f'  {table_name}: {count} rows')
    
    conn.close()
except Exception as e:
    print('Error:', e)
