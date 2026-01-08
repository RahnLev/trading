#!/usr/bin/env python3
"""
Check why short entry occurred on bar 1843
"""

import os
import sys
import sqlite3
import csv
import glob
import json

if sys.stdout.encoding != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8')

CUSTOM_DIR = os.path.dirname(os.path.dirname(os.path.dirname(__file__)))
VOLATILITY_DB = os.path.join(CUSTOM_DIR, 'web', 'dashboard', 'volatility.db')
STRATEGY_LOGS_DIR = os.path.join(CUSTOM_DIR, 'strategy_logs')

def get_latest_params():
    """Get latest JSON params file"""
    json_files = glob.glob(os.path.join(STRATEGY_LOGS_DIR, 'BarsOnTheFlow_*_params.json'))
    if not json_files:
        return None
    json_files.sort(key=os.path.getmtime, reverse=True)
    return json_files[0]

def check_short_entry_1843():
    """Check why short entered on bar 1843"""
    print("="*80)
    print("CHECKING SHORT ENTRY ON BAR 1843")
    print("="*80)
    
    # Get parameters
    params_file = get_latest_params()
    params = {}
    if params_file:
        with open(params_file, 'r') as f:
            params = json.load(f)
        print(f"\n=== ACTIVE SHORT FILTERS ===")
        print(f"  GradientFilterEnabled: {params.get('GradientFilterEnabled', 'N/A')}")
        print(f"  SkipShortsAboveGradient: {params.get('SkipShortsAboveGradient', 'N/A')}")
        print(f"  UseEmaCrossoverFilter: {params.get('UseEmaCrossoverFilter', 'N/A')}")
        print(f"  EmaCrossoverMinTicksCloseToFast: {params.get('EmaCrossoverMinTicksCloseToFast', 'N/A')}")
        print(f"  EmaCrossoverMinTicksFastToSlow: {params.get('EmaCrossoverMinTicksFastToSlow', 'N/A')}")
        print(f"  EmaCrossoverRequireBodyBelow: {params.get('EmaCrossoverRequireBodyBelow', 'N/A')}")
        print(f"  EmaCrossoverRequireCrossover: {params.get('EmaCrossoverRequireCrossover', 'N/A')}")
    
    # Check database
    if os.path.exists(VOLATILITY_DB):
        conn = sqlite3.connect(VOLATILITY_DB)
        cursor = conn.cursor()
        
        # Find trade that entered on bar 1843
        cursor.execute("""
            SELECT entry_bar, exit_bar, direction, entry_price, exit_price, entry_reason
            FROM trades
            WHERE entry_bar = 1843 AND direction = 'SHORT'
            ORDER BY entry_bar DESC
            LIMIT 1
        """)
        
        trade = cursor.fetchone()
        if trade:
            entry_bar, exit_bar, direction, entry_price, exit_price, entry_reason = trade
            print(f"\n=== TRADE INFO ===")
            print(f"Entry Bar: {entry_bar}, Exit Bar: {exit_bar}")
            print(f"Direction: {direction}, Entry: {entry_price:.2f}, Exit: {exit_price:.2f}")
            print(f"Entry Reason: {entry_reason}")
        
        # Get bar data around entry
        cursor.execute("""
            SELECT bar_index, open_price, close_price, high_price, low_price,
                   ema_fast_value, ema_slow_value, fast_ema_grad_deg, candle_type,
                   allow_long_this_bar, allow_short_this_bar, entry_reason
            FROM bar_samples
            WHERE bar_index IN (1842, 1843, 1844)
            ORDER BY bar_index
        """)
        
        rows = cursor.fetchall()
        print(f"\n=== BAR CONDITIONS AROUND ENTRY ===")
        for row in rows:
            bar_idx, open_p, close_p, high_p, low_p, fast_ema, slow_ema, grad_deg, candle_type, allow_long, allow_short, entry_reason = row
            print(f"\nBar {bar_idx}:")
            print(f"  OHLC: O={open_p:.2f}, H={high_p:.2f}, L={low_p:.2f}, C={close_p:.2f}")
            print(f"  Fast EMA: {fast_ema:.2f}, Slow EMA: {slow_ema:.2f}")
            print(f"  Gradient: {grad_deg:.2f}°")
            print(f"  Candle Type: {candle_type}")
            print(f"  Allow Long: {allow_long}, Allow Short: {allow_short}")
            print(f"  Entry Reason: {entry_reason if entry_reason else '(No entry reason recorded)'}")
            
            if bar_idx == 1843 and fast_ema and slow_ema and close_p:
                print(f"\n  === SHORT ENTRY FILTERS CHECK ===")
                
                # Gradient filter
                gradient_threshold = params.get('SkipShortsAboveGradient', -10)
                if params.get('GradientFilterEnabled', False):
                    grad_ok = grad_deg <= gradient_threshold
                    print(f"  Gradient Filter:")
                    print(f"    Gradient: {grad_deg:.2f}° (threshold: {gradient_threshold}°)")
                    print(f"    ✓ OK: {grad_ok} ({'PASS' if grad_ok else 'BLOCK'})")
                else:
                    print(f"  Gradient Filter: DISABLED")
                    grad_ok = True
                
                # EMA Crossover Filter
                if params.get('UseEmaCrossoverFilter', False):
                    print(f"  EMA Crossover Filter:")
                    min_ticks_close = params.get('EmaCrossoverMinTicksCloseToFast', 2)
                    min_ticks_fast_slow = params.get('EmaCrossoverMinTicksFastToSlow', 2)
                    require_body = params.get('EmaCrossoverRequireBodyBelow', False)
                    require_crossover = params.get('EmaCrossoverRequireCrossover', False)
                    
                    # Convert ticks to price (assuming 0.25 tick size)
                    min_gap_close = min_ticks_close * 0.25
                    min_gap_fast_slow = min_ticks_fast_slow * 0.25
                    
                    # For shorts: Close <= Fast EMA - minGap AND Fast EMA <= Slow EMA - minGap
                    close_below_fast = close_p <= fast_ema - min_gap_close
                    fast_below_slow = fast_ema <= slow_ema - min_gap_fast_slow
                    
                    print(f"    Close to Fast gap: {(fast_ema - close_p):.2f} (need {min_gap_close:.2f}) - {'✓' if close_below_fast else '✗'}")
                    print(f"    Fast to Slow gap: {(slow_ema - fast_ema):.2f} (need {min_gap_fast_slow:.2f}) - {'✓' if fast_below_slow else '✗'}")
                    
                    # Body requirement
                    body_ok = True
                    if require_body:
                        body_top = max(open_p, close_p)
                        body_bottom = min(open_p, close_p)
                        fast_ema_threshold = fast_ema - min_gap_close
                        body_below = body_top <= fast_ema_threshold and body_bottom <= fast_ema_threshold
                        body_ok = body_below
                        print(f"    Body requirement:")
                        print(f"      Body Top: {body_top:.2f}, Body Bottom: {body_bottom:.2f}")
                        print(f"      Fast EMA threshold: {fast_ema_threshold:.2f}")
                        print(f"      Body below Fast EMA? {body_below} - {'✓' if body_below else '✗'}")
                    
                    ema_filter_ok = close_below_fast and fast_below_slow and body_ok
                    print(f"    Overall EMA Filter: {'✓ PASS' if ema_filter_ok else '✗ BLOCK'}")
                else:
                    print(f"  EMA Crossover Filter: DISABLED")
                    ema_filter_ok = True
                
                # Overall check
                if grad_ok and ema_filter_ok:
                    print(f"\n  ✓ ALL FILTERS PASSED - SHORT ENTRY SHOULD OCCUR")
                else:
                    print(f"\n  ✗ SOME FILTERS BLOCKED - SHORT ENTRY SHOULD NOT OCCUR")
                    print(f"     Check logs for actual entry reason")
        
        conn.close()
    
    # Check logs
    log_files = glob.glob(os.path.join(STRATEGY_LOGS_DIR, 'BarsOnTheFlow_OutputWindow_*.csv'))
    if log_files:
        log_files.sort(key=os.path.getmtime, reverse=True)
        latest_log = log_files[0]
        
        print(f"\n=== LOG ENTRIES FOR BAR 1843 ===")
        print(f"Log file: {os.path.basename(latest_log)}")
        
        with open(latest_log, 'r', encoding='utf-8') as f:
            reader = csv.DictReader(f)
            found_1843 = False
            entry_lines = []
            for row in reader:
                bar_num = row.get('Bar', '')
                message = row.get('Message', '')
                
                if '1843' in bar_num:
                    found_1843 = True
                    if any(tag in message for tag in ['ENTRY', 'SHORT', 'Entering', 'Gradient', 'EMA', 'Block', 'Skip', 'Cooldown', 'GRADIENT_RECALC', 'GRADIENT_BLOCK']):
                        entry_lines.append(f"Bar {bar_num}: {message[:300]}")
                elif found_1843 and '1844' in bar_num:
                    if any(tag in message for tag in ['ENTRY', 'Entering', 'SHORT']):
                        entry_lines.append(f"Bar {bar_num}: {message[:300]}")
                        break
            
            if entry_lines:
                for line in entry_lines[:20]:  # Limit to first 20 lines
                    print(f"  {line}")
            else:
                print(f"  No relevant log entries found for bar 1843")

if __name__ == '__main__':
    check_short_entry_1843()
