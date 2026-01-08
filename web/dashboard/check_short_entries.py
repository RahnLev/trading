#!/usr/bin/env python3
"""
Check why short entries are happening on specific bars
"""

import os
import sys
import sqlite3
import json

if sys.stdout.encoding != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8')

CUSTOM_DIR = os.path.dirname(os.path.dirname(os.path.dirname(__file__)))
VOLATILITY_DB = os.path.join(CUSTOM_DIR, 'web', 'dashboard', 'volatility.db')

def check_trades_on_bars(bar_numbers):
    """Check trades that entered on specific bars"""
    if not os.path.exists(VOLATILITY_DB):
        print(f"ERROR: Database not found at {VOLATILITY_DB}")
        return
    
    conn = sqlite3.connect(VOLATILITY_DB)
    cursor = conn.cursor()
    
    print("="*80)
    print(f"CHECKING TRADES ON BARS: {bar_numbers}")
    print("="*80)
    
    placeholders = ','.join('?' * len(bar_numbers))
    query = f"""
        SELECT entry_bar, direction, entry_price, entry_time, entry_reason, exit_bar, exit_price
        FROM trades
        WHERE entry_bar IN ({placeholders})
        ORDER BY entry_bar, entry_time
    """
    
    cursor.execute(query, bar_numbers)
    rows = cursor.fetchall()
    
    if not rows:
        print(f"\nNo trades found on bars {bar_numbers}")
        print("\nChecking recent trades to see what bars they're on...")
        cursor.execute("""
            SELECT entry_bar, direction, entry_price, entry_time, entry_reason
            FROM trades
            ORDER BY entry_time DESC
            LIMIT 20
        """)
        recent = cursor.fetchall()
        if recent:
            print("\nRecent trades:")
            for row in recent:
                print(f"  Bar {row[0]}: {row[1]} @ {row[2]:.2f} - {row[5] if len(row) > 5 else 'N/A'}")
    else:
        print(f"\nFound {len(rows)} trade(s) on these bars:\n")
        for row in rows:
            entry_bar, direction, entry_price, entry_time, entry_reason, exit_bar, exit_price = row
            print(f"Bar {entry_bar}: {direction}")
            print(f"  Entry Price: {entry_price:.2f}")
            print(f"  Entry Time: {entry_time}")
            print(f"  Entry Reason: {entry_reason}")
            if exit_bar:
                print(f"  Exit Bar: {exit_bar}, Exit Price: {exit_price:.2f}")
            print()
    
    conn.close()

if __name__ == "__main__":
    # Default bars to check
    bars_to_check = [2409, 2410]
    
    if len(sys.argv) > 1:
        try:
            bars_to_check = [int(b) for b in sys.argv[1:]]
        except ValueError:
            print(f"Invalid bar numbers, using defaults: {bars_to_check}")
    
    check_trades_on_bars(bars_to_check)
