#!/usr/bin/env python3
"""
Check why entry blocks aren't working - analyze logs for a specific bar
"""

import os
import csv
import sys
import glob

if sys.stdout.encoding != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8')

CUSTOM_DIR = os.path.dirname(os.path.dirname(os.path.dirname(__file__)))
STRATEGY_LOGS_DIR = os.path.join(CUSTOM_DIR, 'strategy_logs')

def get_latest_log_file():
    """Get the most recent strategy log file"""
    log_files = []
    for root, dirs, files in os.walk(STRATEGY_LOGS_DIR):
        for file in files:
            if file.startswith('BarsOnTheFlow_OutputWindow_') and file.endswith('.csv'):
                log_files.append(os.path.join(root, file))
    
    if not log_files:
        return None
    
    log_files.sort(key=os.path.getmtime, reverse=True)
    return log_files[0]

def extract_logs_for_bar(log_file_path, bar_number):
    """Extract all relevant log entries for a specific bar"""
    relevant_logs = []
    if not log_file_path or not os.path.exists(log_file_path):
        print(f"Error: Log file not found at {log_file_path}")
        return []
    
    with open(log_file_path, 'r', encoding='utf-8', errors='ignore') as f:
        reader = csv.reader(f)
        for row in reader:
            if len(row) > 1:
                message = row[1]
                # Look for messages mentioning the bar number
                if f"Bar {bar_number}" in message or f"baseBar={bar_number}" in message or f"CurrentBar={bar_number}":
                    relevant_logs.append(message)
    return relevant_logs

def categorize_logs(logs):
    """Categorize logs by type"""
    categorized = {
        "GRADIENT_RECALC": [],
        "GRADIENT_BLOCK": [],
        "GRADIENT_CHECK": [],
        "EMA_CROSSOVER_CHECK": [],
        "ENTRY_DECISION": [],
        "ENTRY": [],
        "ENTRY_DEBUG": [],
        "OTHER": []
    }
    
    for log in logs:
        if "[GRADIENT_RECALC]" in log:
            categorized["GRADIENT_RECALC"].append(log)
        elif "[GRADIENT_BLOCK]" in log:
            categorized["GRADIENT_BLOCK"].append(log)
        elif "[GRADIENT_CHECK]" in log or "[GRADIENT_CHECK_DETAIL]" in log:
            categorized["GRADIENT_CHECK"].append(log)
        elif "[EMA_CROSSOVER_CHECK]" in log:
            categorized["EMA_CROSSOVER_CHECK"].append(log)
        elif "[ENTRY_DECISION]" in log:
            categorized["ENTRY_DECISION"].append(log)
        elif "[ENTRY]" in log and "Entering" in log:
            categorized["ENTRY"].append(log)
        elif "[ENTRY_DEBUG]" in log:
            categorized["ENTRY_DEBUG"].append(log)
        else:
            categorized["OTHER"].append(log)
    
    return categorized

if __name__ == "__main__":
    # Default to bar 2408 (the problematic bar from earlier)
    bar_to_check = 2408
    if len(sys.argv) > 1:
        try:
            bar_to_check = int(sys.argv[1])
        except ValueError:
            print(f"Invalid bar number: {sys.argv[1]}, using default: {bar_to_check}")
    
    log_file = get_latest_log_file()
    
    print("="*80)
    print(f"CHECKING ENTRY BLOCKS FOR BAR {bar_to_check}")
    print("="*80)
    print(f"Log file: {os.path.basename(log_file) if log_file else 'NOT FOUND'}\n")
    
    if not log_file:
        print("ERROR: No log file found!")
        sys.exit(1)
    
    all_logs = extract_logs_for_bar(log_file, bar_to_check)
    print(f"Found {len(all_logs)} relevant log entries for bar {bar_to_check}:\n")
    
    categorized_logs = categorize_logs(all_logs)
    
    # Print in order of importance
    for category in ["GRADIENT_RECALC", "GRADIENT_BLOCK", "GRADIENT_CHECK", "EMA_CROSSOVER_CHECK", "ENTRY_DECISION", "ENTRY", "ENTRY_DEBUG", "OTHER"]:
        logs = categorized_logs[category]
        if logs:
            print(f"\n{'='*80}")
            print(f"{category}:")
            print('='*80)
            for log in logs:
                print(f"  {log}")
    
    # Summary
    print("\n" + "="*80)
    print("SUMMARY:")
    print("="*80)
    
    if categorized_logs["GRADIENT_RECALC"]:
        print("✓ Gradient recalculation occurred")
    else:
        print("✗ NO gradient recalculation found - code may not be executing")
    
    if categorized_logs["GRADIENT_BLOCK"]:
        print("✓ EMA crossover block triggered (Fast EMA < Slow EMA)")
    else:
        print("✗ NO EMA crossover block - Fast EMA may not be < Slow EMA")
    
    if categorized_logs["ENTRY_DECISION"]:
        for log in categorized_logs["ENTRY_DECISION"]:
            if "skipDueToGradient" in log:
                print(f"  Entry decision: {log}")
    else:
        print("✗ NO entry decision log found")
    
    if categorized_logs["ENTRY"]:
        print("⚠ ENTRY OCCURRED despite blocks!")
        for log in categorized_logs["ENTRY"]:
            print(f"  {log}")
    else:
        print("✓ No entry occurred (or entry was blocked)")
