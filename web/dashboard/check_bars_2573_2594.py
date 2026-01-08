#!/usr/bin/env python3
"""
Check why no entry on bar 2573 and why exit on bar 2594
"""

import os
import sys
import sqlite3
import csv
import glob

if sys.stdout.encoding != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8')

CUSTOM_DIR = os.path.dirname(os.path.dirname(os.path.dirname(__file__)))
VOLATILITY_DB = os.path.join(CUSTOM_DIR, 'web', 'dashboard', 'volatility.db')
STRATEGY_LOGS_DIR = os.path.join(CUSTOM_DIR, 'strategy_logs')

def get_latest_log():
    """Get latest CSV log file"""
    log_files = glob.glob(os.path.join(STRATEGY_LOGS_DIR, 'BarsOnTheFlow_OutputWindow_*.csv'))
    if not log_files:
        return None
    log_files.sort(key=os.path.getmtime, reverse=True)
    return log_files[0]

def check_bar_2573():
    """Check why no entry on bar 2573"""
    print("="*80)
    print("CHECKING BAR 2573 - WHY NO ENTRY?")
    print("="*80)
    
    # Check database
    if os.path.exists(VOLATILITY_DB):
        conn = sqlite3.connect(VOLATILITY_DB)
        cursor = conn.cursor()
        
        cursor.execute("""
            SELECT bar_index, open_price, close_price, high_price, low_price,
                   ema_fast_value, ema_slow_value, fast_ema_grad_deg, candle_type,
                   allow_long_this_bar, allow_short_this_bar, entry_reason
            FROM bar_samples
            WHERE bar_index IN (2572, 2573, 2574)
            ORDER BY bar_index
        """)
        
        rows = cursor.fetchall()
        print("\n=== BAR DATA FROM DATABASE ===")
        for row in rows:
            bar_idx, open_p, close_p, high_p, low_p, fast_ema, slow_ema, grad_deg, candle_type, allow_long, allow_short, entry_reason = row
            print(f"\nBar {bar_idx}:")
            print(f"  OHLC: O={open_p:.2f}, H={high_p:.2f}, L={low_p:.2f}, C={close_p:.2f}")
            print(f"  Fast EMA: {fast_ema:.2f}, Slow EMA: {slow_ema:.2f}")
            print(f"  Gradient: {grad_deg:.2f}°")
            print(f"  Candle Type: {candle_type}")
            print(f"  Allow Long: {allow_long}, Allow Short: {allow_short}")
            print(f"  Entry Reason: {entry_reason}")
        
        conn.close()
    
    # Check logs
    log_file = get_latest_log()
    if log_file:
        print("\n=== LOG ENTRIES FOR BAR 2573 ===")
        with open(log_file, 'r', encoding='utf-8') as f:
            reader = csv.DictReader(f)
            found_2573 = False
            for row in reader:
                bar_num = row.get('Bar', '')
                message = row.get('Message', '')
                if '2573' in bar_num:
                    found_2573 = True
                    if any(tag in message for tag in ['ENTRY', 'BLOCK', 'Skip', 'Gradient', 'EMA', 'Cooldown', 'GRADIENT_RECALC', 'GRADIENT_BLOCK']):
                        print(f"Bar {bar_num}: {message[:200]}")
                elif found_2573 and '2574' in bar_num:
                    if any(tag in message for tag in ['ENTRY', 'Entering']):
                        print(f"\nBar {bar_num}: {message[:200]}")
                        break

def check_bar_2594():
    """Check why exit on bar 2594"""
    print("\n" + "="*80)
    print("CHECKING BAR 2594 - WHY EXIT?")
    print("="*80)
    
    # Check database
    if os.path.exists(VOLATILITY_DB):
        conn = sqlite3.connect(VOLATILITY_DB)
        cursor = conn.cursor()
        
        # Find trade that exited on bar 2594
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
            print(f"\n=== TRADE THAT EXITED ON BAR 2594 ===")
            print(f"Entry Bar: {entry_bar}, Exit Bar: {exit_bar}")
            print(f"Direction: {direction}, Entry: {entry_price:.2f}, Exit: {exit_price:.2f}")
        
        # Get bar data around exit
        cursor.execute("""
            SELECT bar_index, open_price, close_price, high_price, low_price,
                   ema_fast_value, ema_slow_value, fast_ema_grad_deg, candle_type
            FROM bar_samples
            WHERE bar_index IN (2593, 2594, 2595)
            ORDER BY bar_index
        """)
        
        rows = cursor.fetchall()
        print("\n=== BAR DATA AROUND EXIT ===")
        for row in rows:
            bar_idx, open_p, close_p, high_p, low_p, fast_ema, slow_ema, grad_deg, candle_type = row
            print(f"\nBar {bar_idx}:")
            print(f"  OHLC: O={open_p:.2f}, H={high_p:.2f}, L={low_p:.2f}, C={close_p:.2f}")
            print(f"  Fast EMA: {fast_ema:.2f}, Slow EMA: {slow_ema:.2f}")
            print(f"  Gradient: {grad_deg:.2f}°")
            print(f"  Candle Type: {candle_type}")
            if bar_idx == 2594 and fast_ema and slow_ema:
                # Check if FullCandle mode would trigger
                if direction == 'LONG':
                    # For LONG: High must be below Fast EMA
                    high_below_ema = high_p < fast_ema
                    print(f"  FullCandle Exit Check (LONG): High={high_p:.2f} < Fast EMA={fast_ema:.2f}? {high_below_ema}")
                elif direction == 'SHORT':
                    # For SHORT: Low must be above Fast EMA
                    low_above_ema = low_p > fast_ema
                    print(f"  FullCandle Exit Check (SHORT): Low={low_p:.2f} > Fast EMA={fast_ema:.2f}? {low_above_ema}")
        
        conn.close()
    
    # Check logs
    log_file = get_latest_log()
    if log_file:
        print("\n=== LOG ENTRIES FOR BAR 2594 ===")
        with open(log_file, 'r', encoding='utf-8') as f:
            reader = csv.DictReader(f)
            for row in reader:
                bar_num = row.get('Bar', '')
                message = row.get('Message', '')
                if '2594' in bar_num:
                    if any(tag in message for tag in ['EXIT', 'EMA_STOP', 'Exit', 'Trigger', 'FullCandle']):
                        print(f"Bar {bar_num}: {message[:300]}")

if __name__ == '__main__':
    check_bar_2573()
    check_bar_2594()
