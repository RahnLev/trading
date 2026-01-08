"""
Check if entries are exiting immediately after entry
"""
import os
import glob
import re
from collections import defaultdict

def find_latest_log_file():
    """Find the most recent NinjaTrader output log file"""
    log_dirs = [
        r"\\mac\Home\Documents\NinjaTrader 8\bin\Custom\strategy_logs",
    ]
    
    all_logs = []
    for pattern in log_dirs:
        if os.path.exists(pattern):
            logs = glob.glob(os.path.join(pattern, "*OutputWindow*.csv"))
            all_logs.extend(logs)
    
    if not all_logs:
        logs = glob.glob("**/*OutputWindow*.csv", recursive=True)
        all_logs.extend(logs)
    
    if not all_logs:
        return None
    
    return max(all_logs, key=os.path.getmtime)

def analyze_entry_exits(log_file):
    """Analyze entries and their exits"""
    print("=" * 80)
    print("ENTRY/EXIT ANALYSIS")
    print("=" * 80)
    print(f"\n📄 Analyzing: {os.path.basename(log_file)}\n")
    
    entries = {}  # bar -> {direction, timestamp, entry_reason}
    exits = {}    # bar -> {direction, timestamp, exit_reason}
    
    try:
        with open(log_file, 'r', encoding='utf-8', errors='ignore') as f:
            for line in f:
                if '[ENTRY] Bar' in line and 'Entering' in line:
                    # Parse: timestamp,bar,level,"[ENTRY] Bar X: Entering LONG/SHORT..."
                    parts = line.strip().split(',', 3)
                    if len(parts) >= 4:
                        timestamp = parts[0].strip('"')
                        bar = int(parts[1]) if parts[1].isdigit() else None
                        message = parts[3].strip('"')
                        
                        if bar is not None:
                            direction = 'LONG' if 'LONG' in message else 'SHORT' if 'SHORT' in message else 'UNKNOWN'
                            entries[bar] = {
                                'direction': direction,
                                'timestamp': timestamp,
                                'message': message
                            }
                
                # Only look for actual exit messages, not debug messages containing "exit"
                if '[EXIT_DEBUG]' in line or '[EXIT]' in line or 'ExitLong' in line or 'ExitShort' in line:
                    parts = line.strip().split(',', 3)
                    if len(parts) >= 4:
                        timestamp = parts[0].strip('"')
                        bar = int(parts[1]) if parts[1].isdigit() else None
                        message = parts[3].strip('"')
                        
                        # Only count actual exit messages, not debug messages
                        if bar is not None and ('[EXIT_DEBUG]' in message or '[EXIT]' in message or 'ExitLong' in message or 'ExitShort' in message):
                            direction = 'LONG' if 'LONG' in message or 'Sell' in message else 'SHORT' if 'SHORT' in message or 'BuyToCover' in message else 'UNKNOWN'
                            if bar not in exits:
                                exits[bar] = []
                            exits[bar].append({
                                'direction': direction,
                                'timestamp': timestamp,
                                'message': message
                            })
    except Exception as e:
        print(f"❌ Error: {e}")
        return
    
    print(f"✅ Found {len(entries)} entries")
    print(f"✅ Found {len(exits)} exit bars\n")
    
    # Analyze entry/exit pairs
    print("=" * 80)
    print("ENTRY/EXIT PAIRS")
    print("=" * 80)
    
    immediate_exits = []
    normal_exits = []
    no_exit = []
    
    for entry_bar in sorted(entries.keys()):
        entry = entries[entry_bar]
        direction = entry['direction']
        
        # Find the next exit for this direction
        exit_found = False
        for exit_bar in sorted(exits.keys()):
            if exit_bar > entry_bar:
                for exit_info in exits[exit_bar]:
                    if exit_info['direction'] == direction:
                        bars_held = exit_bar - entry_bar
                        if bars_held <= 1:
                            immediate_exits.append({
                                'entry_bar': entry_bar,
                                'exit_bar': exit_bar,
                                'bars_held': bars_held,
                                'direction': direction,
                                'entry': entry,
                                'exit': exit_info
                            })
                        else:
                            normal_exits.append({
                                'entry_bar': entry_bar,
                                'exit_bar': exit_bar,
                                'bars_held': bars_held,
                                'direction': direction,
                                'entry': entry,
                                'exit': exit_info
                            })
                        exit_found = True
                        break
                if exit_found:
                    break
        
        if not exit_found:
            no_exit.append({
                'entry_bar': entry_bar,
                'direction': direction,
                'entry': entry
            })
    
    print(f"\n📊 Summary:")
    print(f"   Total entries: {len(entries)}")
    print(f"   Immediate exits (0-1 bars): {len(immediate_exits)}")
    print(f"   Normal exits (2+ bars): {len(normal_exits)}")
    print(f"   No exit found: {len(no_exit)}\n")
    
    if immediate_exits:
        print("=" * 80)
        print(f"⚠️  IMMEDIATE EXITS ({len(immediate_exits)} entries)")
        print("=" * 80)
        for item in immediate_exits[:10]:  # Show first 10
            print(f"\nEntry Bar {item['entry_bar']} ({item['direction']}):")
            print(f"  Entry: {item['entry']['message']}")
            print(f"  Exit Bar {item['exit_bar']} (held {item['bars_held']} bars):")
            print(f"  Exit: {item['exit']['message']}")
        if len(immediate_exits) > 10:
            print(f"\n... and {len(immediate_exits) - 10} more immediate exits")
    
    if no_exit:
        print("\n" + "=" * 80)
        print(f"✅ ENTRIES WITHOUT EXIT ({len(no_exit)} entries)")
        print("=" * 80)
        for item in no_exit[:10]:  # Show first 10
            print(f"\nEntry Bar {item['entry_bar']} ({item['direction']}):")
            print(f"  {item['entry']['message']}")
        if len(no_exit) > 10:
            print(f"\n... and {len(no_exit) - 10} more entries without exit")

if __name__ == "__main__":
    log_file = find_latest_log_file()
    if log_file:
        analyze_entry_exits(log_file)
    else:
        print("❌ Could not find log file")
