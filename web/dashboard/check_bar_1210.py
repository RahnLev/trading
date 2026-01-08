#!/usr/bin/env python3
"""
Check bar 1210 (chart bar 1209) to investigate SHORT entry issue
"""
import sqlite3
import json
import os

# Database path
DB_PATH = os.path.join(os.path.dirname(__file__), 'volatility.db')

def check_bar_1210():
    """Check bar 1210 data and surrounding bars"""
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()
    
    print("=" * 80)
    print("CHECKING BAR 1210 (Chart Bar 1209) - SHORT ENTRY INVESTIGATION")
    print("=" * 80)
    
    # Check bar 1210
    cursor.execute("""
        SELECT bar_index, timestamp, open_price, high_price, low_price, close_price,
               ema_fast_period, ema_slow_period,
               ema_fast_value, ema_slow_value,
               fast_ema_grad_deg, 
               trend_up, trend_down,
               allow_long_this_bar, allow_short_this_bar,
               entry_reason,
               candle_type
        FROM bar_samples
        WHERE bar_index = 1210
        ORDER BY bar_index
        LIMIT 1
    """)
    
    row = cursor.fetchone()
    if not row:
        print("Bar 1210 not found in database!")
        return
    
    # Get column names
    cursor.execute("PRAGMA table_info(bar_samples)")
    columns = [col[1] for col in cursor.fetchall()]
    
    # Create dict
    bar_data = dict(zip(columns, row))
    
    print(f"\nBAR {bar_data['bar_index']} DATA:")
    print(f"  Timestamp: {bar_data.get('timestamp', 'N/A')}")
    print(f"  OHLC: O={bar_data.get('open_price', 'N/A')}, H={bar_data.get('high_price', 'N/A')}, L={bar_data.get('low_price', 'N/A')}, C={bar_data.get('close_price', 'N/A')}")
    print(f"  Fast EMA: {bar_data.get('ema_fast_value', 'N/A')} (Period: {bar_data.get('ema_fast_period', 'N/A')})")
    print(f"  Slow EMA: {bar_data.get('ema_slow_value', 'N/A')} (Period: {bar_data.get('ema_slow_period', 'N/A')})")
    print(f"  Gradient: {bar_data.get('fast_ema_grad_deg', 'N/A')}°")
    print(f"  Trend Up: {bar_data.get('trend_up', 'N/A')}, Trend Down: {bar_data.get('trend_down', 'N/A')}")
    print(f"  Allow Long: {bar_data.get('allow_long_this_bar', 'N/A')}, Allow Short: {bar_data.get('allow_short_this_bar', 'N/A')}")
    print(f"  Entry Reason: {bar_data.get('entry_reason', 'N/A')}")
    print(f"  Candle Type: {bar_data.get('candle_type', 'N/A')}")
    
    # Calculate EMA conditions
    close = bar_data.get('close_price')
    fast_ema = bar_data.get('ema_fast_value')
    slow_ema = bar_data.get('ema_slow_value')
    gradient = bar_data.get('fast_ema_grad_deg')
    
    if close and fast_ema and slow_ema:
        print(f"\nEMA ALIGNMENT ANALYSIS:")
        print(f"  Close: {close}")
        print(f"  Fast EMA: {fast_ema}")
        print(f"  Slow EMA: {slow_ema}")
        print(f"  Close > Fast EMA: {close > fast_ema}")
        print(f"  Close > Slow EMA: {close > slow_ema}")
        print(f"  Fast EMA > Slow EMA: {fast_ema > slow_ema}")
        print(f"  Slow EMA > Fast EMA: {slow_ema > fast_ema}")
        
        # Check SHORT conditions (should be: close < fastEMA AND fastEMA < slowEMA)
        short_close_below_fast = close < fast_ema
        short_fast_below_slow = fast_ema < slow_ema
        print(f"\nSHORT ENTRY CONDITIONS:")
        print(f"  Close < Fast EMA: {short_close_below_fast} ({close} < {fast_ema})")
        print(f"  Fast EMA < Slow EMA: {short_fast_below_slow} ({fast_ema} < {slow_ema})")
        print(f"  SHORT EMA condition met: {short_close_below_fast and short_fast_below_slow}")
    
    if gradient is not None:
        print(f"\nGRADIENT FILTER ANALYSIS:")
        print(f"  Gradient: {gradient}°")
        print(f"  (Note: SkipShortsAboveGradient threshold not stored in bar_samples - check strategy params)")
        if gradient > -7.0:  # Default threshold
            print(f"  Gradient > -7.0°: {gradient > -7.0} - Should block SHORT if GradientFilterEnabled=true")
    
    # Check for trades on this bar
    print(f"\nTRADES ON THIS BAR:")
    cursor.execute("""
        SELECT entry_bar, exit_bar, direction, entry_price, exit_price,
               entry_reason, exit_reason
        FROM trades
        WHERE entry_bar = 1210 OR exit_bar = 1210
        ORDER BY entry_bar
    """)
    
    trades = cursor.fetchall()
    if trades:
        for trade in trades:
            print(f"  Entry Bar: {trade[0]}, Exit Bar: {trade[1]}, Direction: {trade[2]}")
            print(f"    Entry Price: {trade[3]}, Exit Price: {trade[4]}")
            print(f"    Entry Reason: {trade[5]}, Exit Reason: {trade[6]}")
    else:
        print("  No trades found on bar 1210")
    
    # Check surrounding bars for context
    print(f"\nSURROUNDING BARS (1208-1212):")
    cursor.execute("""
        SELECT bar_index, close_price, ema_fast_value, ema_slow_value, fast_ema_grad_deg,
               trend_down, allow_short_this_bar, entry_reason
        FROM bar_samples
        WHERE bar_index BETWEEN 1208 AND 1212
        ORDER BY bar_index
    """)
    
    for row in cursor.fetchall():
        idx, close, fast, slow, grad, trend_d, allow_s, reason = row
        print(f"  Bar {idx}: C={close}, Fast={fast}, Slow={slow}, Grad={grad}°, TrendD={trend_d}, AllowS={allow_s}, Reason={reason}")
    
    conn.close()

if __name__ == "__main__":
    check_bar_1210()
