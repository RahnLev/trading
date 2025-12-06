import csv
import re
from datetime import datetime

# Find the most recent log files
import os
import glob

log_dir = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\strategy_logs'

# Get the most recent log files
csv_files = sorted(glob.glob(os.path.join(log_dir, 'GradientSlope_MNQ*.csv')), 
                   key=os.path.getctime, reverse=True)
log_files = sorted(glob.glob(os.path.join(log_dir, 'GradientSlope_MNQ*.log')), 
                   key=os.path.getctime, reverse=True)

print("Most recent CSV file:", csv_files[0] if csv_files else "None")
print("Most recent LOG file:", log_files[0] if log_files else "None")
print()

# Parse CSV file - looking for bar 2612
if csv_files:
    csv_file = csv_files[0]
    print(f"=== Analyzing {os.path.basename(csv_file)} ===\n")
    
    with open(csv_file, 'r') as f:
        reader = csv.DictReader(f)
        rows = list(reader)
    
    # Find rows around bar 2612
    bar_2612_rows = [r for r in rows if r.get('Bar') and int(r.get('Bar', 0)) == 2612]
    
    if bar_2612_rows:
        print(f"Found {len(bar_2612_rows)} entries for bar 2612")
        for row in bar_2612_rows:
            print("\n--- Bar 2612 Data from CSV ---")
            for key, val in row.items():
                print(f"{key:20s}: {val}")
    else:
        # Try to find nearby bars
        nearby = [r for r in rows if r.get('Bar') and 2610 <= int(r.get('Bar', 0)) <= 2614]
        if nearby:
            print(f"Found {len(nearby)} entries for bars 2610-2614:")
            for row in nearby:
                bar = row.get('Bar')
                open_p = row.get('Open')
                close = row.get('Close')
                signal = row.get('Signal')
                action = row.get('Action')
                print(f"Bar {bar:5s}: Open={open_p:10s}, Close={close:10s}, Signal={signal:8s}, Action={action}")
        else:
            print("No data found for bars 2610-2614 in CSV")

print("\n" + "="*80 + "\n")

# Parse LOG file - looking for bar 2612
if log_files:
    log_file = log_files[0]
    print(f"=== Analyzing {os.path.basename(log_file)} ===\n")
    
    with open(log_file, 'r', encoding='utf-8', errors='ignore') as f:
        log_content = f.read()
    
    # Search for bar 2612 in log
    lines = log_content.split('\n')
    
    # Find lines mentioning bar 2612
    bar_2612_lines = [l for l in lines if '2612' in l]
    
    if bar_2612_lines:
        print(f"Found {len(bar_2612_lines)} lines mentioning bar 2612:\n")
        for i, line in enumerate(bar_2612_lines[:50]):  # Show first 50 matches
            print(f"{i+1}. {line}")
    
    # Also search for ENTRY near bar 2612
    print("\n" + "="*80)
    print("\nSearching for ENTRY events near bar 2612...\n")
    
    entry_lines = [l for l in lines if 'ENTRY' in l and any(str(x) in l for x in range(2610, 2615))]
    if entry_lines:
        print(f"Found {len(entry_lines)} ENTRY events near bar 2612:\n")
        for line in entry_lines:
            print(line)
    
    # Search for SHORT signal/entry
    print("\n" + "="*80)
    print("\nSearching for SHORT signal and entry decisions...\n")
    
    short_lines = [l for l in lines if 'SHORT' in l and any(str(x) in l for x in range(2608, 2616))]
    if short_lines:
        print(f"Found {len(short_lines)} SHORT-related events around bar 2612:\n")
        for line in short_lines[:30]:  # Show first 30
            print(line)

print("\n" + "="*80)
print("\nDone!")
