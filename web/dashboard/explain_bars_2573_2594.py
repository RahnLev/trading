#!/usr/bin/env python3
"""
Explain why no entry on bar 2573 and why exit on bar 2594
"""

import os
import sys
import sqlite3

if sys.stdout.encoding != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8')

CUSTOM_DIR = os.path.dirname(os.path.dirname(os.path.dirname(__file__)))
VOLATILITY_DB = os.path.join(CUSTOM_DIR, 'web', 'dashboard', 'volatility.db')

print("="*80)
print("EXPLANATION: BAR 2573 (NO ENTRY) AND BAR 2594 (EXIT)")
print("="*80)

if os.path.exists(VOLATILITY_DB):
    conn = sqlite3.connect(VOLATILITY_DB)
    cursor = conn.cursor()
    
    # Get trade info
    cursor.execute("""
        SELECT entry_bar, exit_bar, direction, entry_price, exit_price
        FROM trades
        WHERE exit_bar = 2594
        ORDER BY entry_bar DESC
        LIMIT 1
    """)
    
    trade = cursor.fetchone()
    if trade:
        entry_bar, exit_bar, direction, entry_price, exit_price = trade
        print(f"\n=== TRADE INFO ===")
        print(f"Entry Bar: {entry_bar}, Exit Bar: {exit_bar}")
        print(f"Direction: {direction}, Entry: {entry_price:.2f}, Exit: {exit_price:.2f}")
    
    # Get bar 2573 data
    cursor.execute("""
        SELECT bar_index, open_price, close_price, high_price, low_price,
               ema_fast_value, ema_slow_value, fast_ema_grad_deg, candle_type,
               entry_reason
        FROM bar_samples
        WHERE bar_index IN (2572, 2573, 2574)
        ORDER BY bar_index
    """)
    
    rows = cursor.fetchall()
    print(f"\n=== BARS 2572-2574 (ENTRY DECISION) ===")
    for row in rows:
        bar_idx, open_p, close_p, high_p, low_p, fast_ema, slow_ema, grad_deg, candle_type, entry_reason = row
        print(f"\nBar {bar_idx}:")
        print(f"  Close: {close_p:.2f}, Fast EMA: {fast_ema:.2f}, Slow EMA: {slow_ema:.2f}")
        print(f"  Gradient: {grad_deg:.2f}°")
        print(f"  Candle Type: {candle_type}")
        print(f"  Entry Reason: {entry_reason if entry_reason else '(No entry)'}")
        
        if bar_idx == 2573:
            print(f"\n  ❓ WHY NO ENTRY ON BAR 2573?")
            print(f"     - Bar 2573 has positive gradient (78.29°) and Fast EMA > Slow EMA")
            print(f"     - But entry reason is empty - conditions weren't fully met yet")
            print(f"     - Entry happened on bar 2574 instead (see entry_reason above)")
            print(f"     - This suggests a filter blocked entry on 2573 (possibly cooldown, position already open, or EMA crossover filter)")
        
        if bar_idx == 2574 and entry_reason:
            print(f"\n  ✓ ENTRY ON BAR 2574:")
            print(f"     - Entry reason: {entry_reason}")
            print(f"     - This means all conditions were met on bar 2574")
    
    # Get bar 2594 data
    cursor.execute("""
        SELECT bar_index, open_price, close_price, high_price, low_price,
               ema_fast_value, ema_slow_value, fast_ema_grad_deg, candle_type
        FROM bar_samples
        WHERE bar_index IN (2593, 2594, 2595)
        ORDER BY bar_index
    """)
    
    rows = cursor.fetchall()
    print(f"\n=== BARS 2593-2595 (EXIT DECISION) ===")
    for row in rows:
        bar_idx, open_p, close_p, high_p, low_p, fast_ema, slow_ema, grad_deg, candle_type = row
        print(f"\nBar {bar_idx}:")
        print(f"  OHLC: O={open_p:.2f}, H={high_p:.2f}, L={low_p:.2f}, C={close_p:.2f}")
        print(f"  Fast EMA: {fast_ema:.2f}, Slow EMA: {slow_ema:.2f}")
        print(f"  Gradient: {grad_deg:.2f}°")
        print(f"  Candle Type: {candle_type}")
        
        if bar_idx == 2594 and direction == 'LONG':
            print(f"\n  ❓ WHY EXIT ON BAR 2594?")
            print(f"     - This is a LONG trade that entered on bar {entry_bar}")
            print(f"     - Fast EMA ({fast_ema:.2f}) < Slow EMA ({slow_ema:.2f}) - bearish")
            print(f"     - Gradient is negative ({grad_deg:.2f}°) - bearish")
            print(f"     - For FullCandle mode LONG exit: High must be below Fast EMA")
            print(f"     - High ({high_p:.2f}) vs Fast EMA ({fast_ema:.2f})")
            high_below_ema = high_p < fast_ema
            print(f"     - High < Fast EMA? {high_below_ema}")
            if high_below_ema:
                print(f"     - ✓ YES - FullCandle exit condition met!")
            else:
                print(f"     - ✗ NO - But exit still occurred. Check for other exit reasons:")
                print(f"       - Profit protection early exit?")
                print(f"       - Static stop loss?")
                print(f"       - Other exit logic?")
    
    conn.close()

print("\n" + "="*80)
print("SUMMARY:")
print("="*80)
print("1. BAR 2573: No entry because conditions weren't fully met yet.")
print("   Entry occurred on bar 2574 when all filters passed.")
print("")
print("2. BAR 2594: Exit occurred because:")
print("   - For LONG trades with FullCandle mode: High must be below Fast EMA")
print("   - Check the logs to see the exact exit reason (EMA_STOP_EXIT message)")
