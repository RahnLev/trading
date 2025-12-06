import csv
import sys

# Bar indices to analyze
target_bars = [2170, 2015]

log_file = r"c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\strategy_logs\GradientSlope_MNQ 12-25_2025-12-04_19-45-39.csv"

print(f"Analyzing bars {target_bars} from strategy log...\n")

with open(log_file, 'r') as f:
    reader = csv.DictReader(f)
    
    for row in reader:
        bar_idx = int(row.get('BarIndex', -1))
        
        if bar_idx in target_bars:
                print(f"{'='*80}")
                print(f"BAR {bar_idx}")
                print(f"{'='*80}")
                print(f"Time: {row.get('Timestamp')}")
                print(f"Close: {row.get('Close')}")
                print(f"\nGradients:")
                print(f"  Fast Gradient: {row.get('FastGrad')}")
                print(f"  Slow Gradient: {row.get('SlowGrad')}")
                print(f"  Acceleration: {row.get('Accel', 'N/A')}")
                
                print(f"\nSignals:")
                print(f"  Fast Signal: {row.get('SignalFast')}")
                print(f"  Slow Signal: {row.get('SignalSlow')}")
                print(f"  Position: {row.get('Position')}")
                
                print(f"\nMetrics:")
                print(f"  ADX: {row.get('ADX', 'N/A')}")
                print(f"  RSI: {row.get('RSI', 'N/A')}")
                print(f"  ATR: {row.get('ATR', 'N/A')}")
                print(f"  Gradient Stability: {row.get('GradStab', 'N/A')}")
                print(f"  Bandwidth: {row.get('Bandwidth', 'N/A')}")
                
                action = row.get('Action', '')
                details = row.get('Details', '')
                
                print(f"\nAction: {action}")
                print(f"Details: {details}")
                
            # Parse why entry was blocked
            if 'ENTRY_FILTER_BLOCKED' in action:
                print(f"\n⚠️  ENTRY BLOCKED")
                if 'FILTERS:' in details:
                    filters_part = details.split('FILTERS:')[1].split('|')[0]
                    print(f"   Blocked by: {filters_part}")
            elif 'ENTRY' in action:
                print(f"\n✅ ENTRY ALLOWED")
            
            print()

print("Analysis complete.")
