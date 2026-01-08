#!/usr/bin/env python3
"""
Analyze specific trades visible on the chart to understand trade decisions
"""
import sqlite3
import os
import sys

# Configure UTF-8 output
if sys.stdout.encoding != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8')

VOLATILITY_DB = os.path.join(os.path.dirname(__file__), 'volatility.db')

def analyze_trades_in_range(start_bar, end_bar):
    """Analyze trades in a specific bar range"""
    if not os.path.exists(VOLATILITY_DB):
        print(f"Database not found: {VOLATILITY_DB}")
        return
    
    conn = sqlite3.connect(VOLATILITY_DB)
    cursor = conn.cursor()
    
    # Get trades in range
    cursor.execute("""
        SELECT entry_bar, exit_bar, direction, entry_price, exit_price,
               realized_points, exit_reason, entry_reason, mfe, mae,
               bars_held
        FROM trades
        WHERE entry_bar >= ? AND entry_bar <= ?
        ORDER BY entry_bar
    """, (start_bar, end_bar))
    
    trades = cursor.fetchall()
    
    print("="*80)
    print(f"TRADES IN BAR RANGE {start_bar} - {end_bar}")
    print("="*80)
    print(f"Found {len(trades)} trades\n")
    
    for trade in trades:
        entry_bar, exit_bar, direction, entry_price, exit_price, realized, exit_reason, entry_reason, mfe, mae, bars_held = trade
        
        print(f"Trade: {direction} Entry Bar {entry_bar} @ {entry_price:.2f} → Exit Bar {exit_bar} @ {exit_price:.2f}")
        print(f"  Bars Held: {bars_held}, Realized: {realized:.2f} points, MFE: {mfe:.2f}, MAE: {mae:.2f}")
        print(f"  Entry Reason: {entry_reason or 'N/A'}")
        print(f"  Exit Reason: {exit_reason or 'N/A'}")
        
        # Get bar data for entry
        cursor.execute("""
            SELECT bar_index, open_price, high_price, low_price, close_price,
                   ema_fast_value, ema_slow_value, fast_ema_grad_deg,
                   candle_type, direction, allow_long_this_bar, allow_short_this_bar
            FROM bar_samples
            WHERE bar_index = ?
        """, (entry_bar,))
        
        entry_bar_data = cursor.fetchone()
        if entry_bar_data:
            bar_idx, open_p, high_p, low_p, close_p, ema_fast, ema_slow, gradient, candle_type, bar_dir, allow_long, allow_short = entry_bar_data
            print(f"  Entry Bar Data:")
            print(f"    OHLC: O={open_p:.2f} H={high_p:.2f} L={low_p:.2f} C={close_p:.2f}")
            print(f"    Fast EMA: {ema_fast:.2f}, Slow EMA: {ema_slow:.2f}, Gradient: {gradient:.2f}")
            print(f"    Candle Type: {candle_type}, Direction: {bar_dir}")
            print(f"    Allow Long: {allow_long}, Allow Short: {allow_short}")
            
            # Check body vs EMA
            if ema_fast:
                body_top = max(open_p, close_p)
                body_bottom = min(open_p, close_p)
                if direction == 'Long':
                    body_above = body_bottom >= ema_fast
                    print(f"    Body vs Fast EMA: Top={body_top:.2f}, Bottom={body_bottom:.2f}, BodyAbove={body_above}")
                else:
                    body_below = body_top <= ema_fast
                    print(f"    Body vs Fast EMA: Top={body_top:.2f}, Bottom={body_bottom:.2f}, BodyBelow={body_below}")
        
        # Get bar data for exit
        cursor.execute("""
            SELECT bar_index, open_price, high_price, low_price, close_price,
                   ema_fast_value, ema_slow_value
            FROM bar_samples
            WHERE bar_index = ?
        """, (exit_bar,))
        
        exit_bar_data = cursor.fetchone()
        if exit_bar_data:
            bar_idx, open_p, high_p, low_p, close_p, ema_fast, ema_slow = exit_bar_data
            print(f"  Exit Bar Data:")
            print(f"    OHLC: O={open_p:.2f} H={high_p:.2f} L={low_p:.2f} C={close_p:.2f}")
            print(f"    Fast EMA: {ema_fast:.2f}, Slow EMA: {ema_slow:.2f}")
            
            # Check EMA stop trigger
            if ema_fast and 'EMA' in (exit_reason or ''):
                body_top = max(open_p, close_p)
                body_bottom = min(open_p, close_p)
                if direction == 'Long':
                    body_below_ema = body_top < ema_fast and body_bottom < ema_fast
                    close_below_ema = close_p < ema_fast
                    print(f"    EMA Stop Check (BodyOnly): BodyTop={body_top:.2f}, BodyBottom={body_bottom:.2f}, EMA={ema_fast:.2f}")
                    print(f"      BodyBelow: {body_below_ema}, CloseBelow: {close_below_ema}")
        
        print()
    
    conn.close()

if __name__ == '__main__':
    # Based on the chart, bars appear to be in the 2200-2400 range
    # Let's analyze a specific range
    if len(sys.argv) >= 3:
        start = int(sys.argv[1])
        end = int(sys.argv[2])
    else:
        # Default: analyze recent bars (chart shows bars around 2268-2315+)
        start = 2260
        end = 2320
    
    analyze_trades_in_range(start, end)
