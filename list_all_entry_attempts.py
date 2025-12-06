"""
Extract all bars that had entry attempts (both successful and blocked) from logs.
Show bar number, trend direction, entry signal, and result.
"""

import re
import pandas as pd

def parse_log_entries():
    """Parse the log file for entry attempts."""
    entries = []
    
    with open('strategy_logs/GradientSlope_MNQ 12-25_2025-12-02_01-00-48.log', 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    for i, line in enumerate(lines):
        # Look for ENTRY DECISION lines (signal present)
        if 'ENTRY DECISION' in line and 'CurrSignal:' in line:
            match = re.search(r'Bar:(\d+).*CurrSignal:(LONG|SHORT)', line)
            if match:
                bar_num = int(match.group(1))
                signal = match.group(2)
                
                # Check if bar is in our range
                if 3683 <= bar_num <= 3913:
                    # Look ahead for ENTRY_FILTER_BLOCKED or ORDER
                    blocked = False
                    entered = False
                    blockers = []
                    
                    # Check next few lines for filter block or order
                    for j in range(i, min(i+5, len(lines))):
                        next_line = lines[j]
                        if 'ENTRY_FILTER_BLOCKED' in next_line and f'Bar:{bar_num}' not in next_line and signal in ['LONG', 'SHORT']:
                            blocked = True
                            # Extract blockers
                            blocker_match = re.search(r'FILTERS:(.*?)\s+ADX:', next_line)
                            if blocker_match:
                                blockers = blocker_match.group(1).split('|')
                            break
                        elif 'ORDER SUBMITTED' in next_line or 'EnterLong' in next_line or 'EnterShort' in next_line:
                            entered = True
                            break
                    
                    # Also check same line for immediate block
                    if 'ENTRY_FILTER_BLOCKED' in line:
                        blocked = True
                        blocker_match = re.search(r'FILTERS:(.*?)\s+ADX:', line)
                        if blocker_match:
                            blockers = blocker_match.group(1).split('|')
                    
                    entries.append({
                        'barIndex': bar_num,
                        'signal': signal,
                        'entered': entered,
                        'blocked': blocked,
                        'status': 'ENTERED' if entered else ('BLOCKED' if blocked else 'PENDING'),
                        'blockers': ', '.join([b.strip() for b in blockers]) if blockers else ''
                    })
        
        # Also catch standalone ENTRY_FILTER_BLOCKED with bar context
        elif 'ENTRY_FILTER_BLOCKED' in line and 'FILTERS:' in line:
            # Try to extract bar from previous BAR SNAPSHOT
            for j in range(max(0, i-5), i):
                prev_line = lines[j]
                if 'BAR SNAPSHOT' in prev_line:
                    bar_match = re.search(r'Bar:(\d+)', prev_line)
                    signal_match = re.search(r'CurrSignal:(LONG|SHORT|FLAT)', prev_line)
                    if bar_match and signal_match:
                        bar_num = int(bar_match.group(1))
                        signal_text = signal_match.group(1)
                        # Skip FLAT signals
                        if signal_text in ['LONG', 'SHORT']:
                            signal = signal_text
                            if 3683 <= bar_num <= 3913:
                                blocker_match = re.search(r'FILTERS:(.*?)\s+ADX:', line)
                                blockers = blocker_match.group(1).split('|') if blocker_match else []
                                
                                # Check if already added
                                if not any(e['barIndex'] == bar_num and e['signal'] == signal for e in entries):
                                    entries.append({
                                        'barIndex': bar_num,
                                        'signal': signal,
                                        'entered': False,
                                        'blocked': True,
                                        'status': 'BLOCKED',
                                        'blockers': ', '.join([b.strip() for b in blockers])
                                    })
                    break
    
    return entries

def merge_with_bars(entries_list):
    """Merge entry attempts with bar data from cache."""
    df_bars = pd.read_csv('bar_analysis_3683_3909.csv')
    df_entries = pd.DataFrame(entries_list)
    
    # Merge
    merged = pd.merge(df_entries, df_bars, on='barIndex', how='left')
    
    # Select relevant columns
    result = merged[['barIndex', 'Time', 'signal', 'trendSide', 'status', 'blockers', 
                     'fastGrad', 'slowGrad', 'accel', 'adx', 'rsi', 'gradStab', 'bandwidth']]
    
    return result

def main():
    print("=" * 100)
    print("ENTRY ATTEMPTS ANALYSIS - Bars 3683-3913")
    print("=" * 100)
    
    # Parse log entries
    entries = parse_log_entries()
    print(f"\n✓ Found {len(entries)} entry attempts in log")
    
    # Merge with bar data
    df = merge_with_bars(entries)
    
    # Remove duplicates (keep first occurrence)
    df = df.drop_duplicates(subset=['barIndex', 'signal'], keep='first')
    
    # Sort by bar index
    df = df.sort_values('barIndex')
    
    print(f"✓ After deduplication: {len(df)} unique entry attempts")
    
    # Summary statistics
    entered_count = len(df[df['status'] == 'ENTERED'])
    blocked_count = len(df[df['status'] == 'BLOCKED'])
    pending_count = len(df[df['status'] == 'PENDING'])
    
    long_attempts = len(df[df['signal'] == 'LONG'])
    short_attempts = len(df[df['signal'] == 'SHORT'])
    
    print("\n" + "=" * 100)
    print("SUMMARY")
    print("=" * 100)
    print(f"Total Entry Attempts: {len(df)}")
    print(f"  ✅ ENTERED  : {entered_count:3d} ({entered_count/len(df)*100:.1f}%)")
    print(f"  ⛔ BLOCKED  : {blocked_count:3d} ({blocked_count/len(df)*100:.1f}%)")
    print(f"  ⏳ PENDING  : {pending_count:3d} ({pending_count/len(df)*100:.1f}%)")
    print(f"\nDirection Breakdown:")
    print(f"  LONG attempts : {long_attempts}")
    print(f"  SHORT attempts: {short_attempts}")
    
    # Trend alignment check
    df['trend_aligned'] = ((df['signal'] == 'LONG') & (df['trendSide'] == 'BULL')) | \
                          ((df['signal'] == 'SHORT') & (df['trendSide'] == 'BEAR'))
    
    aligned_count = df['trend_aligned'].sum()
    counter_trend_count = len(df) - aligned_count
    
    print(f"\nTrend Alignment:")
    print(f"  With trend     : {aligned_count} ({aligned_count/len(df)*100:.1f}%)")
    print(f"  Counter-trend  : {counter_trend_count} ({counter_trend_count/len(df)*100:.1f}%)")
    
    # Show all entries
    print("\n" + "=" * 100)
    print("ALL ENTRY ATTEMPTS (Bar | Time | Signal | Trend | Status)")
    print("=" * 100)
    
    for idx, row in df.iterrows():
        trend_icon = "✓" if row['trend_aligned'] else "⚠"
        status_icon = "✅" if row['status'] == 'ENTERED' else "⛔"
        
        print(f"{status_icon} Bar {row['barIndex']:4d} @ {row['Time']} | "
              f"{row['signal']:5s} {trend_icon} ({row['trendSide']:4s} trend) | "
              f"fastGrad:{row['fastGrad']:6.3f} ADX:{row['adx']:5.1f} RSI:{row['rsi']:5.1f}")
        
        if row['status'] == 'BLOCKED' and row['blockers']:
            print(f"         Blockers: {row['blockers']}")
    
    # Export to CSV
    output_file = 'all_entry_attempts_3683_3913.csv'
    df.to_csv(output_file, index=False)
    print(f"\n✓ Detailed results saved to: {output_file}")
    
    print("\n" + "=" * 100)

if __name__ == '__main__':
    main()
