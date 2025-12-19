"""
Quick Test - Analyze Your Existing Opportunity Logs

This script finds and analyzes both of your opportunity logs automatically.
"""

import os
import sys
from compare_opportunity_logs import analyze_log, print_comparison, print_top_missed
import argparse


def find_opportunity_logs():
    """Find the two opportunity logs in strategy_logs"""
    log_dir = os.path.join(os.path.dirname(__file__), 'strategy_logs')
    
    if not os.path.exists(log_dir):
        print(f"Error: strategy_logs directory not found at {log_dir}")
        return None, None
    
    logs = []
    for filename in os.listdir(log_dir):
        if filename.startswith('BarsOnTheFlow_Opportunities_') and filename.endswith('.csv'):
            filepath = os.path.join(log_dir, filename)
            # Get file timestamp from name or modification time
            mtime = os.path.getmtime(filepath)
            logs.append((filepath, mtime, filename))
    
    if len(logs) < 2:
        print(f"Found {len(logs)} opportunity log(s). Need at least 2 for comparison.")
        if logs:
            print("Available logs:")
            for path, _, name in logs:
                print(f"  - {name}")
        return None, None
    
    # Sort by modification time (newest first)
    logs.sort(key=lambda x: x[1], reverse=True)
    
    # Return the two most recent logs
    return logs[0][0], logs[1][0]


def main():
    parser = argparse.ArgumentParser(description='Quick test of opportunity analysis')
    parser.add_argument('--min-streak', type=int, default=5)
    parser.add_argument('--max-streak', type=int, default=8)
    parser.add_argument('--min-movement', type=float, default=5.0)
    parser.add_argument('--long-grad', type=float, default=7.0)
    parser.add_argument('--short-grad', type=float, default=-7.0)
    parser.add_argument('--counter-ratio', type=float, default=0.2)
    parser.add_argument('--type', choices=['long', 'short', 'both'], default='both')
    args = parser.parse_args()
    
    print("\n" + "="*70)
    print("QUICK OPPORTUNITY ANALYSIS TEST")
    print("="*70)
    
    # Find logs
    log1, log2 = find_opportunity_logs()
    
    if not log1 or not log2:
        print("\nCannot proceed without two opportunity logs.")
        print("\nTo generate logs:")
        print("1. Set EnableOpportunityLog=true in BarsOnTheFlow strategy")
        print("2. Run strategy on chart")
        print("3. Logs will be created in strategy_logs folder")
        return
    
    # Extract labels from filenames
    label1 = os.path.basename(log1).replace('BarsOnTheFlow_Opportunities_', '').replace('.csv', '')
    label2 = os.path.basename(log2).replace('BarsOnTheFlow_Opportunities_', '').replace('.csv', '')
    
    # Shorten labels if too long
    if len(label1) > 25:
        label1 = label1[:22] + '...'
    if len(label2) > 25:
        label2 = label2[:22] + '...'
    
    print(f"\nFound 2 opportunity logs:")
    print(f"  Log 1: {os.path.basename(log1)}")
    print(f"  Log 2: {os.path.basename(log2)}")
    
    print(f"\nParameters:")
    print(f"  Streak range: {args.min_streak}-{args.max_streak} bars")
    print(f"  Min movement: {args.min_movement} points")
    print(f"  Long gradient: {args.long_grad}°")
    print(f"  Short gradient: {args.short_grad}°")
    print(f"  Counter ratio: {args.counter_ratio}")
    print(f"  Type: {args.type}")
    
    print(f"\n{'Analyzing...':<70}")
    
    # Analyze both logs
    result1 = analyze_log(log1, args)
    result2 = analyze_log(log2, args)
    
    # Print comparison
    print_comparison(result1, result2, label1, label2)
    print_top_missed(result1, result2, label1, label2, n=5)
    
    print("\n" + "="*70)
    print("RECOMMENDATIONS")
    print("="*70)
    
    # Generate recommendations
    better_catch = label1 if result1['catch_rate'] > result2['catch_rate'] else label2
    better_result = result1 if result1['catch_rate'] > result2['catch_rate'] else result2
    
    print(f"\n✅ Better Performance: {better_catch}")
    print(f"   Catch Rate: {better_result['catch_rate']:.1f}%")
    print(f"   Streaks Found: {better_result['total_streaks']}")
    print(f"   Caught: {better_result['caught']}, Missed: {better_result['missed']}")
    
    # Recommendations based on catch rate
    avg_catch = (result1['catch_rate'] + result2['catch_rate']) / 2
    
    if avg_catch < 40:
        print(f"\n⚠️  Low Average Catch Rate ({avg_catch:.1f}%)")
        print("   Consider:")
        print("   - Lower gradient thresholds (try 5° instead of 7°)")
        print("   - Increase counter-ratio (allow more counter-trend bars)")
        print("   - Check most common block reasons above")
    elif avg_catch > 70:
        print(f"\n✅ Good Average Catch Rate ({avg_catch:.1f}%)")
        print("   Consider:")
        print("   - May want to tighten filters slightly to reduce false signals")
        print("   - Validate with live trading or forward testing")
    else:
        print(f"\n📊 Moderate Average Catch Rate ({avg_catch:.1f}%)")
        print("   Consider:")
        print("   - Review block reasons to identify optimization opportunities")
        print("   - Test with adjusted gradient thresholds")
    
    # Block reason recommendations
    common_blocks_1 = sorted(result1['block_reasons'].items(), key=lambda x: x[1], reverse=True)[:3]
    common_blocks_2 = sorted(result2['block_reasons'].items(), key=lambda x: x[1], reverse=True)[:3]
    
    if common_blocks_1 or common_blocks_2:
        print("\n🔍 Most Common Block Reasons:")
        if common_blocks_1:
            print(f"   {label1}:")
            for reason, count in common_blocks_1:
                print(f"     - {reason[:50]}: {count} times")
        if common_blocks_2:
            print(f"   {label2}:")
            for reason, count in common_blocks_2:
                print(f"     - {reason[:50]}: {count} times")
    
    print("\n" + "="*70)
    print("NEXT STEPS")
    print("="*70)
    print("""
1. Use Web Dashboard for Interactive Analysis:
   http://localhost:51888/opportunity_analysis.html
   - Adjust parameters in real-time
   - See immediate impact on catch rates

2. Export Detailed Results:
   python analyze_opportunity_streaks.py "strategy_logs\\*.csv" --output results.csv

3. Modify Strategy Parameters:
   - Adjust gradient thresholds based on block reasons
   - Test with ReverseOnTrendBreak or other settings

4. Re-run and Compare:
   - Generate new logs with adjusted parameters
   - Compare against these baseline results
""")
    
    print("="*70 + "\n")


if __name__ == '__main__':
    main()
