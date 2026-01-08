#!/usr/bin/env python3
"""
Check bar conditions for specific bars to understand why shorts might have entered
"""

import os
import sys
import sqlite3

if sys.stdout.encoding != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8')

CUSTOM_DIR = os.path.dirname(os.path.dirname(os.path.dirname(__file__)))
VOLATILITY_DB = os.path.join(CUSTOM_DIR, 'web', 'dashboard', 'volatility.db')

def check_bar_conditions(bar_numbers):
    """Check bar conditions for specific bars"""
    if not os.path.exists(VOLATILITY_DB):
        print(f"ERROR: Database not found at {VOLATILITY_DB}")
        return
    
    conn = sqlite3.connect(VOLATILITY_DB)
    cursor = conn.cursor()
    
    print("="*80)
    print(f"CHECKING BAR CONDITIONS FOR BARS: {bar_numbers}")
    print("="*80)
    
    placeholders = ','.join('?' * len(bar_numbers))
    query = f"""
        SELECT bar_index, timestamp, open_price, high_price, low_price, close_price,
               ema_fast_value, ema_slow_value, fast_ema_grad_deg, candle_type,
               allow_long_this_bar, allow_short_this_bar, entry_reason
        FROM bar_samples
        WHERE bar_index IN ({placeholders})
        ORDER BY bar_index
    """
    
    cursor.execute(query, bar_numbers)
    rows = cursor.fetchall()
    
    if not rows:
        print(f"\nNo bar data found for bars {bar_numbers}")
        print("\nChecking what bar range exists in database...")
        cursor.execute("SELECT MIN(bar_index), MAX(bar_index), COUNT(*) FROM bar_samples")
        min_bar, max_bar, count = cursor.fetchone()
        print(f"  Bar range: {min_bar} to {max_bar} ({count} total bars)")
    else:
        print(f"\nFound {len(rows)} bar(s):\n")
        for row in rows:
            bar_idx, timestamp, open_p, high_p, low_p, close_p, fast_ema, slow_ema, grad_deg, candle_type, allow_long, allow_short, entry_reason = row
            print(f"Bar {bar_idx} ({timestamp}):")
            print(f"  OHLC: O={open_p:.2f}, H={high_p:.2f}, L={low_p:.2f}, C={close_p:.2f}")
            print(f"  Fast EMA: {fast_ema:.2f}, Slow EMA: {slow_ema:.2f}")
            print(f"  Gradient: {grad_deg:.2f}°")
            print(f"  Candle Type: {candle_type}")
            print(f"  Allow Long: {allow_long}, Allow Short: {allow_short}")
            print(f"  Entry Reason: {entry_reason}")
            
            # Check if conditions favor short
            if fast_ema and slow_ema:
                if fast_ema < slow_ema:
                    print(f"  → Fast EMA < Slow EMA (bearish - favors SHORT)")
                else:
                    print(f"  → Fast EMA > Slow EMA (bullish - favors LONG)")
            
            if grad_deg is not None:
                if grad_deg < 0:
                    print(f"  → Negative gradient (bearish - favors SHORT)")
                elif grad_deg > 10:
                    print(f"  → Positive gradient > 10° (bullish - favors LONG)")
                else:
                    print(f"  → Gradient between -10° and 10° (neutral)")
            print()
    
    conn.close()

if __name__ == "__main__":
    bars_to_check = [2409, 2410]
    
    if len(sys.argv) > 1:
        try:
            bars_to_check = [int(b) for b in sys.argv[1:]]
        except ValueError:
            print(f"Invalid bar numbers, using defaults: {bars_to_check}")
    
    check_bar_conditions(bars_to_check)
