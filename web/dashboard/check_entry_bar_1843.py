#!/usr/bin/env python3
"""
Check why entry happened on bar 1843 despite being above Fast EMA
"""

import os
import sys
import csv
import glob

if sys.stdout.encoding != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8')

CUSTOM_DIR = os.path.dirname(os.path.dirname(os.path.dirname(__file__)))
STRATEGY_LOGS_DIR = os.path.join(CUSTOM_DIR, 'strategy_logs')

def get_latest_log():
    """Get latest CSV log file"""
    log_files = glob.glob(os.path.join(STRATEGY_LOGS_DIR, 'BarsOnTheFlow_OutputWindow_*.csv'))
    if not log_files:
        return None
    log_files.sort(key=os.path.getmtime, reverse=True)
    return log_files[0]

def check_entry_logs(bar_numbers):
    """Check log entries for specific bars"""
    log_file = get_latest_log()
    if not log_file:
        print("ERROR: No strategy output log file found.")
        return
    
    print("="*80)
    print(f"CHECKING ENTRY LOGS FOR BARS: {bar_numbers}")
    print("="*80)
    print(f"Log file: {log_file}\n")
    
    found_logs = False
    encodings = ['utf-8', 'utf-8-sig', 'latin-1', 'cp1252']
    for encoding in encodings:
        try:
            with open(log_file, 'r', encoding=encoding, errors='replace') as f:
                reader = csv.reader(f)
                for row in reader:
                    if len(row) > 1 and row[1].isdigit() and int(row[1]) in bar_numbers:
                        log_msg = row[3] if len(row) > 3 else row[2] if len(row) > 2 else ""
                        if any(keyword in log_msg.upper() for keyword in ["ENTRY", "SHORT", "PRICE_BLOCK", "GRADIENT", "EMA", "TREND", "BAD", "GOOD", "BLOCK"]):
                            print(f"Bar {row[1]}: {log_msg}")
                            found_logs = True
                break
        except (UnicodeDecodeError, UnicodeError):
            continue
    
    if not found_logs:
        print(f"No relevant log entries found for bars {bar_numbers}")

if __name__ == '__main__':
    check_entry_logs([1841, 1842, 1843])
