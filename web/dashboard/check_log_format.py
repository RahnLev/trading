#!/usr/bin/env python3
"""Check log file format"""
import csv
import os

CUSTOM_DIR = os.path.dirname(os.path.dirname(os.path.dirname(__file__)))
STRATEGY_LOGS_DIR = os.path.join(CUSTOM_DIR, 'strategy_logs')

csv_files = [f for f in os.listdir(STRATEGY_LOGS_DIR) 
             if f.endswith('.csv') and 'BarsOnTheFlow' in f and 'OutputWindow' in f]
csv_files.sort(reverse=True)
latest_csv = os.path.join(STRATEGY_LOGS_DIR, csv_files[0])
print(f"File: {csv_files[0]}")

with open(latest_csv, 'r', encoding='utf-8', errors='ignore') as f:
    reader = csv.DictReader(f)
    rows = [r for r in reader][:20]
    
    print(f"\nColumns: {list(rows[0].keys()) if rows else []}")
    print(f"\nFirst 5 messages:")
    for i, row in enumerate(rows[:5]):
        msg = row.get('Message', row.get('message', ''))
        print(f"{i+1}. {msg[:150]}")
