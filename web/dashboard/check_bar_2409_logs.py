#!/usr/bin/env python3
"""Check actual log entries for bar 2409 to see filter check results"""
import csv
import os
import sys

if sys.stdout.encoding != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8')

CUSTOM_DIR = os.path.dirname(os.path.dirname(os.path.dirname(__file__)))
STRATEGY_LOGS_DIR = os.path.join(CUSTOM_DIR, 'strategy_logs')

csv_files = [f for f in os.listdir(STRATEGY_LOGS_DIR) 
             if f.endswith('.csv') and 'OutputWindow' in f and 'BarsOnTheFlow' in f]
csv_files.sort(reverse=True)
latest_csv = os.path.join(STRATEGY_LOGS_DIR, csv_files[0])

print("="*80)
print(f"CHECKING LOG ENTRIES FOR BAR 2409")
print("="*80)
print(f"Log file: {csv_files[0]}\n")

with open(latest_csv, 'r', encoding='utf-8', errors='ignore') as f:
    reader = csv.DictReader(f)
    entries = []
    for row in reader:
        bar_str = row.get('bar', '') or row.get('Bar', '')
        msg = row.get('message', '') or row.get('Message', '')
        
        if '2409' in bar_str or ('2409' in msg and ('ENTRY' in msg or 'GRADIENT' in msg or 'EMA' in msg or 'FILTER' in msg or 'DECISION' in msg or 'PENDING' in msg)):
            entries.append(row)
    
    print(f"Found {len(entries)} relevant log entries for bar 2409:\n")
    
    # Group by type
    entry_logs = [e for e in entries if 'ENTRY' in (e.get('message', '') or e.get('Message', ''))]
    gradient_logs = [e for e in entries if 'GRADIENT' in (e.get('message', '') or e.get('Message', ''))]
    ema_logs = [e for e in entries if 'EMA' in (e.get('message', '') or e.get('Message', '')) and 'FILTER' in (e.get('message', '') or e.get('Message', ''))]
    decision_logs = [e for e in entries if 'ENTRY_DECISION' in (e.get('message', '') or e.get('Message', ''))]
    pending_logs = [e for e in entries if 'PENDING' in (e.get('message', '') or e.get('Message', ''))]
    
    print("ENTRY LOGS:")
    for e in entry_logs[:5]:
        print(f"  {e.get('message', e.get('Message', ''))}")
    
    print("\nGRADIENT CHECK LOGS:")
    for e in gradient_logs[:10]:
        print(f"  {e.get('message', e.get('Message', ''))}")
    
    print("\nEMA FILTER LOGS:")
    for e in ema_logs[:10]:
        print(f"  {e.get('message', e.get('Message', ''))}")
    
    print("\nENTRY DECISION LOGS:")
    for e in decision_logs[:5]:
        print(f"  {e.get('message', e.get('Message', ''))}")
    
    print("\nPENDING LOGS:")
    for e in pending_logs[:10]:
        print(f"  {e.get('message', e.get('Message', ''))}")
