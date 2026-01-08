#!/usr/bin/env python3
"""Check what log messages contain bar data"""
import csv
import os
import sys

if sys.stdout.encoding != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8')

CUSTOM_DIR = os.path.dirname(os.path.dirname(os.path.dirname(__file__)))
STRATEGY_LOGS_DIR = os.path.join(CUSTOM_DIR, 'strategy_logs')

csv_files = [f for f in os.listdir(STRATEGY_LOGS_DIR) 
             if f.endswith('.csv') and 'BarsOnTheFlow' in f and 'OutputWindow' in f]
csv_files.sort(reverse=True)
latest_csv = os.path.join(STRATEGY_LOGS_DIR, csv_files[0])
print(f"File: {csv_files[0]}\n")

with open(latest_csv, 'r', encoding='utf-8', errors='ignore') as f:
    reader = csv.DictReader(f)
    rows = [r for r in reader]
    
    # Find messages with bar data
    patterns = ['Open=', 'Close=', 'High=', 'Low=', 'EMA=', 'Fast=', 'Slow=']
    matching_rows = []
    for row in rows:
        msg = row.get('message', '')
        if any(p in msg for p in patterns):
            matching_rows.append(row)
            if len(matching_rows) >= 20:
                break
    
    print(f"Found {len(matching_rows)} messages with bar data patterns\n")
    print("Sample messages:")
    for i, row in enumerate(matching_rows[:10]):
        bar = row.get('bar', '?')
        msg = row.get('message', '')
        print(f"{i+1}. Bar {bar}: {msg[:200]}")
