#!/usr/bin/env python3
"""Check how many trades have 'Unknown' entry reason"""
import sqlite3

conn = sqlite3.connect('volatility.db')
cursor = conn.cursor()

cursor.execute("SELECT COUNT(*) FROM trades WHERE entry_reason = 'Unknown'")
unknown_count = cursor.fetchone()[0]

print(f"Trades with 'Unknown' entry reason: {unknown_count}")

cursor.execute("""
    SELECT entry_bar, exit_bar, direction, entry_price, entry_reason 
    FROM trades 
    WHERE entry_reason = 'Unknown' 
    ORDER BY entry_bar 
    LIMIT 20
""")
trades = cursor.fetchall()

print(f"\nFirst 20 'Unknown' entry trades:")
for t in trades:
    print(f"  Bar {t[0]}: {t[2]} @ {t[3]:.2f}, exit_bar={t[1]}, reason={t[4]}")

conn.close()
