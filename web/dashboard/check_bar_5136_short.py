#!/usr/bin/env python3
"""
Check why short entry didn't occur on bar 5136
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

def check_bar_data(bar_number):
    """Check bar data from database"""
    conn = sqlite3.connect(VOLATILITY_DB)
    cursor = conn.cursor()
    
    # Get the bar and surrounding bars for context
    query = """
        SELECT bar_index, open_price, close_price, high_price, low_price,
               ema_fast_value, ema_slow_value, fast_ema_grad_deg, slow_ema_grad_deg,
               candle_type, allow_short_this_bar, in_trade, entry_reason, exit_reason
        FROM bar_samples
        WHERE bar_index BETWEEN ? AND ?
        ORDER BY bar_index
    """
    
    cursor.execute(query, (bar_number - 3, bar_number + 3))
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
        next(reader, None)  # Skip header
        for row in reader:
            if len(row) >= 3:
                bar_idx_str = row[1] if len(row) > 1 else ""
                msg = row[2] if len(row) > 2 else row[1] if len(row) > 1 else ""
                # Look for entry-related messages for this bar
                if f"Bar {bar_number}" in msg or f"baseBar={bar_number}" in msg or bar_idx_str == str(bar_number):
                    if any(tag in msg for tag in [
                        "[ENTRY", "[SHORT", "[GRADIENT", "[EMA_CROSSOVER", 
                        "[ENTRY_DECISION", "[ENTRY_DEBUG", "[SHORT_ENTRY_DEBUG",
                        "EnterShort", "SkipShort", "trendDown", "allowShort",
                        "[PRICE_BLOCK]", "[GRADIENT_BLOCK]", "[EMA_FILTER_DEBUG]"
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
        "EnableShorts": params.get("EnableShorts", False),
        "GradientFilterEnabled": params.get("GradientFilterEnabled", False),
        "SkipShortsAboveGradient": params.get("SkipShortsAboveGradient", 0),
        "UseEmaCrossoverFilter": params.get("UseEmaCrossoverFilter", False),
        "EmaCrossoverMinTicksCloseToFast": params.get("EmaCrossoverMinTicksCloseToFast", 0),
        "EmaCrossoverMinTicksFastToSlow": params.get("EmaCrossoverMinTicksFastToSlow", 0),
        "EmaCrossoverRequireBodyBelow": params.get("EmaCrossoverRequireBodyBelow", False),
        "EmaCrossoverRequireCrossover": params.get("EmaCrossoverRequireCrossover", False),
        "AllowShortWhenBodyBelowFastButFastAboveSlow": params.get("AllowShortWhenBodyBelowFastButFastAboveSlow", False),
        "AvoidShortsOnGoodCandle": params.get("AvoidShortsOnGoodCandle", False),
        "EnableBarsOnTheFlowTrendDetection": params.get("EnableBarsOnTheFlowTrendDetection", False),
    }
    return short_filters

if __name__ == "__main__":
    bar_to_check = 5136
    
    print("="*80)
    print(f"WHY DIDN'T SHORT ENTER ON BAR {bar_to_check}?")
    print("="*80)
    
    # Get active parameters
    params_file = get_latest_params()
    if not params_file:
        print("WARNING: No parameters file found!")
        filters = {}
    else:
        filters = analyze_short_filters(params_file)
        print(f"\nParameters file: {os.path.basename(params_file)}")
    
    print("\n=== ACTIVE SHORT FILTERS ===")
    for key, value in filters.items():
        print(f"  {key}: {value}")
    
    # Get bar data
    print(f"\n=== BAR CONDITIONS (bars {bar_to_check-3} to {bar_to_check+3}) ===")
    bar_data = check_bar_data(bar_to_check)
    
    target_bar = None
    for row in bar_data:
        bar_idx, open_p, close_p, high_p, low_p, fast_ema, slow_ema, fast_grad, slow_grad, candle_type, allow_short, in_trade, entry_reason, exit_reason = row
        is_target = (bar_idx == bar_to_check)
        marker = ">>>" if is_target else "   "
        
        print(f"\n{marker} Bar {bar_idx}:")
        print(f"{marker}   OHLC: O={open_p:.2f}, H={high_p:.2f}, L={low_p:.2f}, C={close_p:.2f}")
        if fast_ema:
            print(f"{marker}   Fast EMA: {fast_ema:.2f}, Slow EMA: {slow_ema:.2f}")
            if fast_ema and slow_ema:
                print(f"{marker}   Fast vs Slow: FastEMA {'>' if fast_ema > slow_ema else '<='} SlowEMA (gap: {abs(fast_ema - slow_ema):.2f})")
        if fast_grad is not None:
            print(f"{marker}   Fast Gradient: {fast_grad:.2f}°")
        print(f"{marker}   Candle Type: {candle_type} (prevGood={candle_type=='Good'}, prevBad={candle_type=='Bad'})")
        print(f"{marker}   Allow Short: {allow_short}")
        print(f"{marker}   In Trade: {in_trade}")
        if entry_reason:
            print(f"{marker}   Entry Reason: {entry_reason}")
        if exit_reason:
            print(f"{marker}   Exit Reason: {exit_reason}")
        
        if is_target:
            target_bar = row
            
            # Check gradient filter
            if filters.get("GradientFilterEnabled") and fast_grad is not None:
                threshold = filters.get("SkipShortsAboveGradient", 0)
                if fast_grad > threshold:
                    print(f"{marker}   ❌ GRADIENT BLOCK: Gradient {fast_grad:.2f}° > threshold {threshold}° (blocks shorts)")
                else:
                    print(f"{marker}   ✓ Gradient OK: {fast_grad:.2f}° <= {threshold}°")
            
            # Check if shorts are enabled
            if not filters.get("EnableShorts", True):
                print(f"{marker}   ❌ SHORTS DISABLED: EnableShorts={filters.get('EnableShorts')}")
            
            # Check AvoidShortsOnGoodCandle
            if filters.get("AvoidShortsOnGoodCandle", False) and candle_type == "Good":
                print(f"{marker}   ❌ CANDLE TYPE BLOCK: AvoidShortsOnGoodCandle=True and candle is Good")
            
            # Check EMA crossover filter
            if filters.get("UseEmaCrossoverFilter") and fast_ema and slow_ema and close_p:
                min_ticks_close = filters.get("EmaCrossoverMinTicksCloseToFast", 0)
                min_ticks_fast_slow = filters.get("EmaCrossoverMinTicksFastToSlow", 0)
                require_body = filters.get("EmaCrossoverRequireBodyBelow", False)
                require_crossover = filters.get("EmaCrossoverRequireCrossover", False)
                allow_override = filters.get("AllowShortWhenBodyBelowFastButFastAboveSlow", False)
                
                print(f"{marker}   EMA Crossover Filter Analysis:")
                tick_size = 0.25  # Default for MNQ/MES, adjust if needed
                
                # For shorts: Close <= Fast EMA - minGap AND Fast EMA <= Slow EMA - minGap
                min_gap_close_to_fast = min_ticks_close * tick_size
                min_gap_fast_to_slow = min_ticks_fast_slow * tick_size
                
                close_below_fast = close_p <= (fast_ema - min_gap_close_to_fast) if fast_ema else False
                fast_below_slow = fast_ema <= (slow_ema - min_gap_fast_to_slow) if (fast_ema and slow_ema) else False
                
                gap_close_fast = (fast_ema - close_p) / tick_size if (fast_ema and close_p) else 0
                gap_fast_slow = (slow_ema - fast_ema) / tick_size if (fast_ema and slow_ema) else 0
                
                print(f"{marker}     Close to Fast: gap={gap_close_fast:.1f} ticks (need {min_ticks_close}) {'✓' if close_below_fast else '❌'}")
                print(f"{marker}     Fast to Slow: gap={gap_fast_slow:.1f} ticks (need {min_ticks_fast_slow}) {'✓' if fast_below_slow else '❌'}")
                print(f"{marker}     Fast > Slow: {fast_ema > slow_ema if (fast_ema and slow_ema) else 'N/A'} (bearish crossover needed: Fast <= Slow)")
                
                if require_body:
                    body_top = max(open_p, close_p)
                    body_bottom = min(open_p, close_p)
                    fast_ema_threshold = fast_ema - min_gap_close_to_fast if fast_ema else 0
                    
                    body_below = (body_top <= fast_ema_threshold and body_bottom <= fast_ema_threshold) if fast_ema else False
                    print(f"{marker}     Body requirement:")
                    print(f"{marker}       Body Top: {body_top:.2f}, Body Bottom: {body_bottom:.2f}")
                    print(f"{marker}       Fast EMA threshold: {fast_ema_threshold:.2f} (Fast EMA {fast_ema:.2f} - {min_ticks_close} ticks)")
                    if body_below:
                        print(f"{marker}       ✓ Body is below Fast EMA threshold")
                    else:
                        print(f"{marker}       ❌ BODY BLOCK: Body NOT entirely below Fast EMA threshold!")
                        if fast_ema:
                            print(f"{marker}          Body top {body_top:.2f} > threshold {fast_ema_threshold:.2f} by {(body_top - fast_ema_threshold):.2f} points")
                
                # Check override parameter
                if allow_override and close_below_fast and not fast_below_slow and fast_ema and slow_ema:
                    if fast_ema > slow_ema:
                        print(f"{marker}     ✓ OVERRIDE ALLOWED: AllowShortWhenBodyBelowFastButFastAboveSlow=True")
                        print(f"{marker}        Close is below FastEMA, but FastEMA > SlowEMA (override allows entry)")
                elif not fast_below_slow and fast_ema and slow_ema:
                    if fast_ema > slow_ema:
                        print(f"{marker}     ❌ EMA CROSSOVER BLOCK: FastEMA ({fast_ema:.2f}) > SlowEMA ({slow_ema:.2f})")
                        if not allow_override:
                            print(f"{marker}        Override parameter is disabled (AllowShortWhenBodyBelowFastButFastAboveSlow=False)")
                        else:
                            print(f"{marker}        But Close is not below FastEMA, so override cannot apply")
    
    # Check logs
    print(f"\n=== LOG ENTRIES FOR BAR {bar_to_check} ===")
    log_file = get_latest_log()
    if log_file:
        print(f"Log file: {os.path.basename(log_file)}")
        logs = check_logs_for_bar(log_file, bar_to_check)
        if logs:
            print(f"\nFound {len(logs)} relevant log entries:")
            for log in logs[:30]:  # Limit to first 30
                print(f"  {log}")
        else:
            print(f"\nNo relevant log entries found for bar {bar_to_check}")
    else:
        print("No log file found")
