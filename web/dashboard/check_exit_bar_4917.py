#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Check why a long trade exited on bar 4917
"""

import sqlite3
import os
import sys

# Fix encoding for Windows console
if sys.stdout.encoding != 'utf-8':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except:
        pass

# Database paths
dashboard_db = os.path.join(os.path.dirname(__file__), 'dashboard.db')
volatility_db = os.path.join(os.path.dirname(__file__), 'volatility.db')

print("=" * 80)
print("LONG TRADE EXIT ANALYSIS - Bar 4917")
print("=" * 80)

# Connect to databases
conn_dashboard = sqlite3.connect(dashboard_db)
conn_volatility = sqlite3.connect(volatility_db)

cur_dashboard = conn_dashboard.cursor()
cur_volatility = conn_volatility.cursor()

# Find the trade that exited on bar 4917
print("\n1. FINDING TRADE THAT EXITED ON BAR 4917")
print("-" * 80)
cur_dashboard.execute("""
    SELECT entry_bar, exit_bar, direction, entry_price, exit_price, 
           entry_time, exit_time, exit_reason, entry_reason, 
           realized_points, bars_held, mfe, mae
    FROM trades
    WHERE exit_bar = 4917 AND direction = 'LONG'
    ORDER BY exit_time DESC
    LIMIT 5
""")

trades = cur_dashboard.fetchall()
if not trades:
    print("[ERROR] No LONG trade found that exited on bar 4917")
    print("\nChecking for any trade on bar 4917...")
    cur_dashboard.execute("""
        SELECT entry_bar, exit_bar, direction, exit_reason
        FROM trades
        WHERE exit_bar = 4917 OR entry_bar = 4917
        ORDER BY exit_time DESC
        LIMIT 5
    """)
    alt_trades = cur_dashboard.fetchall()
    if alt_trades:
        print("Found these trades related to bar 4917:")
        for trade in alt_trades:
            print(f"  Entry: {trade[0]}, Exit: {trade[1]}, Direction: {trade[2]}, Exit Reason: {trade[3]}")
    exit(1)

for trade in trades:
    entry_bar, exit_bar, direction, entry_price, exit_price, entry_time, exit_time, exit_reason, entry_reason, realized_points, bars_held, mfe, mae = trade
    print(f"\n[FOUND] Trade:")
    print(f"  Entry Bar: {entry_bar}")
    print(f"  Exit Bar: {exit_bar}")
    print(f"  Direction: {direction}")
    print(f"  Entry Price: {entry_price}")
    print(f"  Exit Price: {exit_price}")
    print(f"  Entry Time: {entry_time}")
    print(f"  Exit Time: {exit_time}")
    print(f"  Exit Reason: {exit_reason}")
    print(f"  Entry Reason: {entry_reason}")
    print(f"  Realized P&L: {realized_points} points")
    print(f"  Bars Held: {bars_held}")
    print(f"  MFE: {mfe}")
    print(f"  MAE: {mae}")

# Get bar data for bar 4917 (the exit bar)
print("\n\n2. BAR 4917 DATA (EXIT BAR)")
print("-" * 80)
cur_volatility.execute("""
    SELECT bar_index, timestamp, open_price, high_price, low_price, close_price,
           ema_fast_value, ema_slow_value, fast_ema_grad_deg,
           candle_type, trend_up, trend_down, 
           allow_long_this_bar, allow_short_this_bar,
           in_trade, direction, stop_loss_points, entry_reason
    FROM bar_samples
    WHERE bar_index = 4917
    ORDER BY timestamp DESC
    LIMIT 1
""")

bar_4917 = cur_volatility.fetchone()
if bar_4917:
    bar_idx, timestamp, open_p, high_p, low_p, close_p, ema_fast, ema_slow, grad_deg, \
    candle_type, trend_up, trend_down, allow_long, allow_short, in_trade, direction, stop_loss, entry_reason = bar_4917
    
    print(f"Bar Index: {bar_idx}")
    print(f"Timestamp: {timestamp}")
    print(f"Open: {open_p}")
    print(f"High: {high_p}")
    print(f"Low: {low_p}")
    print(f"Close: {close_p}")
    print(f"EMA Fast: {ema_fast}")
    print(f"EMA Slow: {ema_slow}")
    print(f"Gradient Degree: {grad_deg}")
    print(f"Candle Type: {candle_type}")
    print(f"Trend Up: {trend_up}")
    print(f"Trend Down: {trend_down}")
    print(f"Allow Long: {allow_long}")
    print(f"Allow Short: {allow_short}")
    print(f"In Trade: {in_trade}")
    print(f"Direction: {direction}")
    print(f"Stop Loss Points: {stop_loss}")
    print(f"Entry Reason: {entry_reason}")
    
    # Calculate if close was below EMA
    if ema_fast and close_p:
        if close_p < ema_fast:
            print(f"\n[WARNING] Close ({close_p}) < Fast EMA ({ema_fast}) - Could trigger EMA stop")
        else:
            print(f"\n[OK] Close ({close_p}) >= Fast EMA ({ema_fast})")
else:
    print("[ERROR] Bar 4917 not found in bar_samples")

# Get bar data for bar 4916 (the bar before exit)
print("\n\n3. BAR 4916 DATA (BAR BEFORE EXIT)")
print("-" * 80)
cur_volatility.execute("""
    SELECT bar_index, timestamp, open_price, high_price, low_price, close_price,
           ema_fast_value, ema_slow_value, fast_ema_grad_deg,
           candle_type, trend_up, trend_down, in_trade, direction
    FROM bar_samples
    WHERE bar_index = 4916
    ORDER BY timestamp DESC
    LIMIT 1
""")

bar_4916 = cur_volatility.fetchone()
if bar_4916:
    bar_idx, timestamp, open_p, high_p, low_p, close_p, ema_fast, ema_slow, grad_deg, \
    candle_type, trend_up, trend_down, in_trade, direction = bar_4916
    
    print(f"Bar Index: {bar_idx}")
    print(f"Open: {open_p}")
    print(f"High: {high_p}")
    print(f"Low: {low_p}")
    print(f"Close: {close_p}")
    print(f"EMA Fast: {ema_fast}")
    print(f"EMA Slow: {ema_slow}")
    print(f"Gradient Degree: {grad_deg}")
    print(f"Candle Type: {candle_type}")
    print(f"Trend Up: {trend_up}")
    print(f"Trend Down: {trend_down}")
    print(f"In Trade: {in_trade}")
    print(f"Direction: {direction}")
else:
    print("[ERROR] Bar 4916 not found in bar_samples")

# Check entry bar if we have it
if trades and trades[0][0]:
    entry_bar = trades[0][0]
    print(f"\n\n4. ENTRY BAR {entry_bar} DATA")
    print("-" * 80)
    cur_volatility.execute("""
        SELECT bar_index, open_price, close_price, candle_type, in_trade, direction
        FROM bar_samples
        WHERE bar_index = ?
        ORDER BY timestamp DESC
        LIMIT 1
    """, (entry_bar,))
    
    entry_bar_data = cur_volatility.fetchone()
    if entry_bar_data:
        bar_idx, open_p, close_p, candle_type, in_trade, direction = entry_bar_data
        print(f"Bar Index: {bar_idx}")
        print(f"Open: {open_p}")
        print(f"Close: {close_p}")
        print(f"Candle Type: {candle_type}")
        print(f"In Trade: {in_trade}")
        print(f"Direction: {direction}")
        
        # Check if entry bar was opposite (could trigger ExitIfEntryBarOpposite)
        if candle_type == 'bad':
            print(f"\n[WARNING] Entry bar was BAD candle - Could trigger ExitIfEntryBarOpposite if enabled")

# Analyze exit reason
print("\n\n5. EXIT REASON ANALYSIS")
print("-" * 80)
if trades and trades[0][7]:  # exit_reason
    exit_reason = trades[0][7]
    print(f"Exit Reason: {exit_reason}")
    
    if "EmaStop" in exit_reason or "EMA" in exit_reason:
        print("\n[EMA STOP LOSS EXIT]")
        if bar_4917:
            print(f"  - Close on bar 4917: {bar_4917[5]}")
            print(f"  - Fast EMA on bar 4917: {bar_4917[6]}")
            if bar_4917[5] and bar_4917[6]:
                if bar_4917[5] < bar_4917[6]:
                    print(f"  - [TRIGGERED] Close < Fast EMA - EMA stop triggered")
                else:
                    print(f"  - [WARNING] Close >= Fast EMA - Check previous bar or trigger mode")
    
    if "Gradient" in exit_reason or "gradient" in exit_reason.lower():
        print("\n[GRADIENT STOP LOSS EXIT]")
        if bar_4917:
            grad = bar_4917[8]
            print(f"  - Gradient on bar 4917: {grad}°")
            print(f"  - Check if gradient < ExitLongBelowGradient threshold")
    
    if "EntryBarOpp" in exit_reason:
        print("\n[ENTRY BAR OPPOSITE EXIT]")
        print(f"  - ExitIfEntryBarOpposite was enabled")
        if entry_bar_data:
            print(f"  - Entry bar {entry_bar} candle type: {entry_bar_data[3]}")
    
    if "Retrace" in exit_reason:
        print("\n[RETRACE EXIT]")
        print(f"  - ExitOnRetrace was enabled")
        print(f"  - Trade gave back enough profit to trigger retrace exit")
    
    if "TrendBreak" in exit_reason or "Exit" in exit_reason:
        print("\n[TREND BREAK EXIT]")
        print(f"  - ExitOnTrendBreak was enabled")
        if bar_4917:
            print(f"  - Trend Up on bar 4917: {bar_4917[10]}")
            print(f"  - Trend Down on bar 4917: {bar_4917[11]}")

# Check surrounding bars for context
print("\n\n6. SURROUNDING BARS (4915-4919)")
print("-" * 80)
cur_volatility.execute("""
    SELECT bar_index, open_price, close_price, candle_type, 
           ema_fast_value, fast_ema_grad_deg, in_trade, direction
    FROM bar_samples
    WHERE bar_index BETWEEN 4915 AND 4919
    ORDER BY bar_index
""")

surrounding = cur_volatility.fetchall()
for bar in surrounding:
    bar_idx, open_p, close_p, candle_type, ema_fast, grad_deg, in_trade, direction = bar
    marker = ">>>" if bar_idx == 4917 else "   "
    print(f"{marker} Bar {bar_idx}: O={open_p:.2f} C={close_p:.2f} {candle_type:4s} "
          f"EMA={ema_fast:.2f if ema_fast else 'N/A':>8s} "
          f"Grad={grad_deg:.2f if grad_deg else 'N/A':>7s}° "
          f"Dir={direction or 'FLAT':5s}")

print("\n" + "=" * 80)
print("ANALYSIS COMPLETE")
print("=" * 80)

conn_dashboard.close()
conn_volatility.close()
