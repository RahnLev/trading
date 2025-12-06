import csv

# Bar indices to analyze
target_bars = [2170, 2015]

log_file = r"c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\strategy_logs\GradientSlope_MNQ 12-25_2025-12-04_19-45-39.csv"

print(f"Analyzing bars {target_bars} from strategy log...\n")

with open(log_file, 'r') as f:
    reader = csv.DictReader(f)
    
    for row in reader:
        bar_idx_str = row.get('Bar', '-1')
        if not bar_idx_str or bar_idx_str == '':
            continue
        bar_idx = int(bar_idx_str)
        
        if bar_idx in target_bars:
            print(f"{'='*80}")
            print(f"BAR {bar_idx}")
            print(f"{'='*80}")
            print(f"Time: {row.get('Timestamp')}")
            print(f"Close: {row.get('Close')}")
            print(f"\nGradients:")
            print(f"  Fast Gradient: {row.get('FastGradient')}")
            print(f"  Slow Gradient: {row.get('SlowGradient')}")
            print(f"  Acceleration: {row.get('Accel', 'N/A')}")
            
            print(f"\nSignals:")
            print(f"  Prev Signal: {row.get('PrevSignal')}")
            print(f"  New Signal: {row.get('NewSignal')}")
            print(f"  My Position: {row.get('MyPosition')}")
            print(f"  Actual Position: {row.get('ActualPosition')}")
            
            print(f"\nMetrics:")
            print(f"  Price Gradient: {row.get('PriceGradient', 'N/A')}")
            print(f"  Volume: {row.get('Volume', 'N/A')}")
            
            action = row.get('Action', '')
            notes = row.get('Notes', '')
            
            print(f"\nAction: {action}")
            print(f"Notes: {notes}")
            
            # Parse why entry was blocked
            if 'ENTRY_FILTER_BLOCKED' in action:
                print(f"\n⚠️  ENTRY BLOCKED")
                if 'FILTERS:' in notes:
                    filters_part = notes.split('FILTERS:')[1].split('|')[0]
                    print(f"   Blocked by: {filters_part}")
            elif 'ENTRY' in action:
                print(f"\n✅ ENTRY ALLOWED")
            
            print()

print("Analysis complete.")
