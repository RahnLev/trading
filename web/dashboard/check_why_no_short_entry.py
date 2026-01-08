#!/usr/bin/env python3
"""
Check why short didn't enter on bars 2409/2410 despite favorable conditions
"""

import os
import sys
import sqlite3
import csv
import json
import glob

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

def get_latest_log():
    """Get latest CSV log file"""
    log_files = glob.glob(os.path.join(STRATEGY_LOGS_DIR, 'BarsOnTheFlow_OutputWindow_*.csv'))
    if not log_files:
        return None
    log_files.sort(key=os.path.getmtime, reverse=True)
    return log_files[0]

def check_bar_data(bar_numbers):
    """Check bar data from database"""
    conn = sqlite3.connect(VOLATILITY_DB)
    cursor = conn.cursor()
    
    placeholders = ','.join('?' * len(bar_numbers))
    query = f"""
        SELECT bar_index, close_price, ema_fast_value, ema_slow_value, fast_ema_grad_deg,
               candle_type, allow_short_this_bar, entry_reason
        FROM bar_samples
        WHERE bar_index IN ({placeholders})
        ORDER BY bar_index
    """
    
    cursor.execute(query, bar_numbers)
    rows = cursor.fetchall()
    conn.close()
    return rows

def check_logs_for_bar(log_file, bar_number):
    """Extract relevant log entries for a bar"""
    if not log_file or not os.path.exists(log_file):
        return []
    
    relevant = []
    with open(log_file, 'r', encoding='utf-8', errors='ignore') as f:
        reader = csv.reader(f)
        for row in reader:
            if len(row) > 1:
                msg = row[1]
                # Look for entry-related messages for this bar
                if f"Bar {bar_number}" in msg or f"baseBar={bar_number}" in msg:
                    if any(tag in msg for tag in [
                        "[ENTRY", "[SHORT", "[GRADIENT", "[EMA_CROSSOVER", 
                        "[ENTRY_DECISION", "[ENTRY_DEBUG", "EnterShort", "SkipShort"
                    ]):
                        relevant.append(msg)
    return relevant

def analyze_short_filters(params_file):
    """Analyze what filters are active for shorts"""
    if not params_file or not os.path.exists(params_file):
        return {}
    
    with open(params_file, 'r') as f:
        params = json.load(f)
    
    short_filters = {
        "GradientFilterEnabled": params.get("GradientFilterEnabled", False),
        "SkipShortsAboveGradient": params.get("SkipShortsAboveGradient", 0),
        "UseEmaCrossoverFilter": params.get("UseEmaCrossoverFilter", False),
        "EmaCrossoverMinTicksCloseToFast": params.get("EmaCrossoverMinTicksCloseToFast", 0),
        "EmaCrossoverMinTicksFastToSlow": params.get("EmaCrossoverMinTicksFastToSlow", 0),
        "EmaCrossoverRequireBodyBelow": params.get("EmaCrossoverRequireBodyBelow", False),
        "EmaCrossoverRequireCrossover": params.get("EmaCrossoverRequireCrossover", False),
    }
    return short_filters

if __name__ == "__main__":
    bars_to_check = [2409, 2410]
    
    print("="*80)
    print("WHY DIDN'T SHORT ENTER ON BARS 2409/2410?")
    print("="*80)
    
    # Get active parameters
    params_file = get_latest_params()
    filters = analyze_short_filters(params_file)
    
    print("\n=== ACTIVE SHORT FILTERS ===")
    for key, value in filters.items():
        print(f"  {key}: {value}")
    
    # Get bar data
    print("\n=== BAR CONDITIONS ===")
    conn = sqlite3.connect(VOLATILITY_DB)
    cursor = conn.cursor()
    placeholders = ','.join('?' * len(bars_to_check))
    query = f"""
        SELECT bar_index, open_price, close_price, high_price, low_price,
               ema_fast_value, ema_slow_value, fast_ema_grad_deg, candle_type, allow_short_this_bar
        FROM bar_samples
        WHERE bar_index IN ({placeholders})
        ORDER BY bar_index
    """
    cursor.execute(query, bars_to_check)
    bar_data = cursor.fetchall()
    conn.close()
    
    for row in bar_data:
        bar_idx, open_p, close_p, high_p, low_p, fast_ema, slow_ema, grad_deg, candle_type, allow_short = row
        print(f"\nBar {bar_idx}:")
        print(f"  OHLC: O={open_p:.2f}, H={high_p:.2f}, L={low_p:.2f}, C={close_p:.2f}")
        print(f"  Fast EMA: {fast_ema:.2f}, Slow EMA: {slow_ema:.2f}")
        print(f"  Gradient: {grad_deg:.2f}°")
        print(f"  Candle Type: {candle_type}")
        print(f"  Allow Short: {allow_short}")
        
        # Check gradient filter
        if filters.get("GradientFilterEnabled"):
            threshold = filters.get("SkipShortsAboveGradient", 0)
            if grad_deg is not None:
                if grad_deg > threshold:
                    print(f"  ❌ GRADIENT BLOCK: Gradient {grad_deg:.2f}° > threshold {threshold}° (blocks shorts)")
                else:
                    print(f"  ✓ Gradient OK: {grad_deg:.2f}° <= {threshold}°")
        
        # Check EMA crossover filter
        if filters.get("UseEmaCrossoverFilter"):
            min_ticks_close = filters.get("EmaCrossoverMinTicksCloseToFast", 0)
            min_ticks_fast_slow = filters.get("EmaCrossoverMinTicksFastToSlow", 0)
            require_body = filters.get("EmaCrossoverRequireBodyBelow", False)
            require_crossover = filters.get("EmaCrossoverRequireCrossover", False)
            
            print(f"  EMA Crossover Filter:")
            print(f"    Min ticks Close to Fast: {min_ticks_close}")
            print(f"    Min ticks Fast to Slow: {min_ticks_fast_slow}")
            print(f"    Require Body Below: {require_body}")
            print(f"    Require Crossover: {require_crossover}")
            
            # For shorts: Close <= Fast EMA - minGap AND Fast EMA <= Slow EMA - minGap
            if fast_ema and slow_ema and close_p:
                gap_close_to_fast = (fast_ema - close_p) * 4  # Convert to ticks (assuming 0.25 tick size)
                gap_fast_to_slow = (slow_ema - fast_ema) * 4
                
                close_ok = gap_close_to_fast >= min_ticks_close
                fast_slow_ok = gap_fast_to_slow >= min_ticks_fast_slow
                
                print(f"    Close to Fast gap: {gap_close_to_fast:.1f} ticks (need {min_ticks_close}) {'✓' if close_ok else '❌'}")
                print(f"    Fast to Slow gap: {gap_fast_to_slow:.1f} ticks (need {min_ticks_fast_slow}) {'✓' if fast_slow_ok else '❌'}")
                
                if not close_ok or not fast_slow_ok:
                    print(f"  ❌ EMA CROSSOVER BLOCK: Gap requirements not met")
                
                # Check body requirement if enabled
                if require_body:
                    body_top = max(open_p, close_p)
                    body_bottom = min(open_p, close_p)
                    min_gap_ticks = min_ticks_close
                    fast_ema_threshold = fast_ema - (min_gap_ticks * 0.25)  # Convert ticks to price
                    
                    body_below = body_top <= fast_ema_threshold and body_bottom <= fast_ema_threshold
                    print(f"    Body requirement:")
                    print(f"      Body Top: {body_top:.2f}, Body Bottom: {body_bottom:.2f}")
                    print(f"      Fast EMA threshold: {fast_ema_threshold:.2f} (Fast EMA {fast_ema:.2f} - {min_gap_ticks} ticks)")
                    if body_below:
                        print(f"      ✓ Body is below Fast EMA threshold")
                    else:
                        print(f"      ❌ BODY BLOCK: Body NOT entirely below Fast EMA threshold!")
                        print(f"         Body top {body_top:.2f} > threshold {fast_ema_threshold:.2f}")
    
    # Check logs
    print("\n=== LOG ENTRIES FOR THESE BARS ===")
    log_file = get_latest_log()
    if log_file:
        print(f"Log file: {os.path.basename(log_file)}")
        for bar in bars_to_check:
            logs = check_logs_for_bar(log_file, bar)
            if logs:
                print(f"\nBar {bar} logs:")
                for log in logs[:10]:  # Limit to first 10
                    print(f"  {log}")
            else:
                print(f"\nBar {bar}: No relevant log entries found")
    else:
        print("No log file found")
