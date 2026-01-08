#!/usr/bin/env python3
"""
Check position state around bars 2409/2410 to see if position was already open
"""

import os
import sys
import sqlite3

if sys.stdout.encoding != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8')

CUSTOM_DIR = os.path.dirname(os.path.dirname(os.path.dirname(__file__)))
VOLATILITY_DB = os.path.join(CUSTOM_DIR, 'web', 'dashboard', 'volatility.db')

def check_position_state(bar_numbers):
    """Check if position was open around these bars"""
    if not os.path.exists(VOLATILITY_DB):
        print(f"ERROR: Database not found at {VOLATILITY_DB}")
        return
    
    conn = sqlite3.connect(VOLATILITY_DB)
    cursor = conn.cursor()
    
    print("="*80)
    print(f"CHECKING POSITION STATE AROUND BARS: {bar_numbers}")
    print("="*80)
    
    # Check trades to see if position was open
    min_bar = min(bar_numbers) - 5
    max_bar = max(bar_numbers) + 5
    
    cursor.execute("""
        SELECT entry_bar, exit_bar, direction, entry_price, exit_price
        FROM trades
        WHERE (entry_bar <= ? AND (exit_bar IS NULL OR exit_bar >= ?))
           OR (entry_bar BETWEEN ? AND ?)
           OR (exit_bar BETWEEN ? AND ?)
        ORDER BY entry_bar
    """, (max_bar, min_bar, min_bar, max_bar, min_bar, max_bar))
    
    trades = cursor.fetchall()
    
    print(f"\n=== TRADES AROUND BARS {min_bar}-{max_bar} ===")
    if trades:
        for trade in trades:
            entry_bar, exit_bar, direction, entry_price, exit_price = trade
            print(f"  {direction}: Entry Bar {entry_bar}, Exit Bar {exit_bar or 'OPEN'}")
            if entry_bar <= max(bar_numbers) and (exit_bar is None or exit_bar >= min(bar_numbers)):
                print(f"    → Position was OPEN during bars {bar_numbers}!")
    else:
        print("  No trades found in this range")
    
    # Check bar_samples for in_trade flag
    print(f"\n=== IN_TRADE FLAG ON BARS {min(bar_numbers)-2} to {max(bar_numbers)+2} ===")
    cursor.execute("""
        SELECT bar_index, in_trade, entry_reason
        FROM bar_samples
        WHERE bar_index BETWEEN ? AND ?
        ORDER BY bar_index
    """, (min_bar, max_bar))
    
    bars = cursor.fetchall()
    for bar_idx, in_trade, entry_reason in bars:
        marker = "🔴" if in_trade else "⚪"
        print(f"  Bar {bar_idx}: {marker} in_trade={in_trade}, reason='{entry_reason or ''}'")
    
    conn.close()

if __name__ == "__main__":
    bars_to_check = [2409, 2410]
    check_position_state(bars_to_check)
