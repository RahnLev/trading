#!/usr/bin/env python3
"""Analyze why there was a long entry on bar 2409"""
import sqlite3
import json
import os
import csv
import sys

if sys.stdout.encoding != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8')

CUSTOM_DIR = os.path.dirname(os.path.dirname(os.path.dirname(__file__)))
STRATEGY_LOGS_DIR = os.path.join(CUSTOM_DIR, 'strategy_logs')
VOLATILITY_DB = os.path.join(os.path.dirname(__file__), 'volatility.db')

# Load parameters
json_files = [f for f in os.listdir(STRATEGY_LOGS_DIR) 
             if f.endswith('_params.json') and 'BarsOnTheFlow' in f]
json_files.sort(reverse=True)
latest_json = os.path.join(STRATEGY_LOGS_DIR, json_files[0])
params = json.load(open(latest_json, 'r'))

print("="*80)
print("ANALYZING LONG ENTRY ON BAR 2409")
print("="*80)

# Load trade data
conn = sqlite3.connect(VOLATILITY_DB)
cursor = conn.cursor()

cursor.execute("""
    SELECT entry_bar, exit_bar, direction, entry_price, exit_price, entry_reason, 
           exit_reason, bars_held, realized_points, mfe, mae, fast_ema, fast_ema_grad_deg
    FROM trades 
    WHERE entry_bar = 2409
""")
trades = cursor.fetchall()

print(f"\nFound {len(trades)} trade(s) entering on bar 2409:")
for t in trades:
    print(f"  {t[2]} @ {t[3]:.2f} → Exit {t[1]} @ {t[4]:.2f}")
    print(f"    Entry Reason: {t[5]}")
    print(f"    Exit Reason: {t[6]}")
    print(f"    Bars Held: {t[7]}, Realized: {t[8]:.2f}, MFE: {t[9]:.2f}, MAE: {t[10]:.2f}")
    print(f"    Fast EMA at entry: {t[11]:.2f}, Gradient: {t[12]:.2f}°")

# Load bar data
cursor.execute("""
    SELECT bar_index, open_price, high_price, low_price, close_price, 
           ema_fast_value, ema_slow_value, fast_ema_grad_deg, candle_type
    FROM bar_samples 
    WHERE bar_index IN (2407, 2408, 2409, 2410)
    ORDER BY bar_index
""")
bars = cursor.fetchall()

print("\n" + "="*80)
print("BAR DATA AROUND ENTRY")
print("="*80)
for b in bars:
    bar_num = b[0]
    open_p = b[1]
    high_p = b[2]
    low_p = b[3]
    close_p = b[4]
    fast_ema = b[5]
    slow_ema = b[6]
    gradient = b[7]
    candle_type = b[8]
    
    body_top = max(open_p, close_p)
    body_bottom = min(open_p, close_p)
    
    print(f"\nBar {bar_num} ({candle_type} candle):")
    print(f"  OHLC: O={open_p:.2f}, H={high_p:.2f}, L={low_p:.2f}, C={close_p:.2f}")
    print(f"  Body: Top={body_top:.2f}, Bottom={body_bottom:.2f}")
    print(f"  Fast EMA: {fast_ema:.2f}, Slow EMA: {slow_ema:.2f}")
    print(f"  Gradient: {gradient:.2f}°")
    
    if bar_num == 2409:
        print(f"\n  >>> ENTRY BAR ANALYSIS <<<")
        
        # Check EMA Crossover Filter
        if params.get('UseEmaCrossoverFilter'):
            print(f"\n  EMA Crossover Filter (enabled):")
            min_gap_close_to_fast = params.get('EmaCrossoverMinTicksCloseToFast', 0) * 0.25  # Assuming 0.25 per tick
            min_gap_fast_to_slow = params.get('EmaCrossoverMinTicksFastToSlow', 0) * 0.25
            
            close_above_fast = close_p >= fast_ema + min_gap_close_to_fast
            fast_above_slow = fast_ema >= slow_ema + min_gap_fast_to_slow
            
            print(f"    Close ({close_p:.2f}) >= FastEMA ({fast_ema:.2f}) + {min_gap_close_to_fast:.2f}: {close_above_fast}")
            print(f"    FastEMA ({fast_ema:.2f}) >= SlowEMA ({slow_ema:.2f}) + {min_gap_fast_to_slow:.2f}: {fast_above_slow}")
            
            if params.get('EmaCrossoverRequireBodyBelow'):
                body_above_fast = body_bottom >= fast_ema + min_gap_close_to_fast and body_top >= fast_ema + min_gap_close_to_fast
                print(f"    BodyBelow requirement (enabled):")
                print(f"      Body bottom ({body_bottom:.2f}) >= FastEMA ({fast_ema:.2f}) + {min_gap_close_to_fast:.2f}: {body_bottom >= fast_ema + min_gap_close_to_fast}")
                print(f"      Body top ({body_top:.2f}) >= FastEMA ({fast_ema:.2f}) + {min_gap_close_to_fast:.2f}: {body_top >= fast_ema + min_gap_close_to_fast}")
                print(f"      BodyAboveFast: {body_above_fast}")
                
                if not body_above_fast:
                    print(f"    ❌ FAILED: BodyBelow requirement not met!")
                else:
                    print(f"    ✓ PASSED: BodyBelow requirement")
            else:
                if close_above_fast and fast_above_slow:
                    print(f"    ✓ PASSED: EMA Crossover Filter")
                else:
                    print(f"    ❌ FAILED: EMA Crossover Filter")
        
        # Check Gradient Filter
        if params.get('GradientFilterEnabled'):
            skip_longs = params.get('SkipLongsBelowGradient', 0)
            print(f"\n  Gradient Filter (enabled):")
            print(f"    Gradient ({gradient:.2f}°) >= SkipLongsBelowGradient ({skip_longs}): {gradient >= skip_longs}")
            if gradient < skip_longs:
                print(f"    ❌ FAILED: Gradient too low!")
            else:
                print(f"    ✓ PASSED: Gradient Filter")
        
        # Check AvoidLongsOnBadCandle
        if params.get('AvoidLongsOnBadCandle') and params.get('EnableBarsOnTheFlowTrendDetection'):
            if candle_type.lower() == 'bad':
                print(f"\n  ❌ FAILED: AvoidLongsOnBadCandle - entry bar is a bad candle")
            else:
                print(f"\n  ✓ PASSED: AvoidLongsOnBadCandle - entry bar is {candle_type} candle")

# Check log entries for bar 2409
print("\n" + "="*80)
print("LOG ENTRIES FOR BAR 2409")
print("="*80)

csv_files = [f for f in os.listdir(STRATEGY_LOGS_DIR) 
             if f.endswith('.csv') and 'BarsOnTheFlow' in f and 'OutputWindow' in f]
csv_files.sort(reverse=True)
latest_csv = os.path.join(STRATEGY_LOGS_DIR, csv_files[0])

with open(latest_csv, 'r', encoding='utf-8', errors='ignore') as f:
    reader = csv.DictReader(f)
    log_entries = [r for r in reader if '2409' in (r.get('bar', '') or r.get('Bar', '')) or 
                   ('2409' in (r.get('message', '') or r.get('Message', '')) and 
                    ('ENTRY' in (r.get('message', '') or r.get('Message', '')) or 
                     'LONG' in (r.get('message', '') or r.get('Message', '')) or
                     'FILTER' in (r.get('message', '') or r.get('Message', ''))))]
    
    print(f"\nFound {len(log_entries)} relevant log entries:")
    for i, entry in enumerate(log_entries[:20], 1):
        msg = entry.get('message', entry.get('Message', ''))
        print(f"{i}. {msg[:200]}")

conn.close()
