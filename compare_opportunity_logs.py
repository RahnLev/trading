"""
Compare Two Opportunity Logs (e.g., 30-second vs 2-minute bars)

Runs streak analysis on both files and generates a comparative report.

Usage:
    python compare_opportunity_logs.py <log1.csv> <log2.csv> [options]
"""

import sys
import argparse
from analyze_opportunity_streaks import load_opportunity_log, find_streaks


def parse_args():
    parser = argparse.ArgumentParser(description='Compare two opportunity logs')
    parser.add_argument('log1', help='First log file (e.g., 30-second bars)')
    parser.add_argument('log2', help='Second log file (e.g., 2-minute bars)')
    parser.add_argument('--label1', default='Log 1', help='Label for first log')
    parser.add_argument('--label2', default='Log 2', help='Label for second log')
    parser.add_argument('--min-streak', type=int, default=5)
    parser.add_argument('--max-streak', type=int, default=8)
    parser.add_argument('--min-movement', type=float, default=5.0)
    parser.add_argument('--long-grad', type=float, default=7.0)
    parser.add_argument('--short-grad', type=float, default=-7.0)
    parser.add_argument('--counter-ratio', type=float, default=0.2)
    parser.add_argument('--type', choices=['long', 'short', 'both'], default='both')
    return parser.parse_args()


def analyze_log(filepath, args):
    """Analyze a single log file"""
    bars = load_opportunity_log(filepath)
    streaks = find_streaks(bars, args)
    
    total = len(streaks)
    caught = sum(1 for s in streaks if s['status'] == 'caught')
    missed = sum(1 for s in streaks if s['status'] == 'missed')
    partial = sum(1 for s in streaks if s['status'] == 'partial')
    
    longs = [s for s in streaks if s['direction'] == 'LONG']
    shorts = [s for s in streaks if s['direction'] == 'SHORT']
    
    catch_rate = (caught / total * 100) if total > 0 else 0
    avg_length = sum(s['length'] for s in streaks) / total if total > 0 else 0
    avg_movement = sum(abs(s['net_movement']) for s in streaks) / total if total > 0 else 0
    avg_missed = sum(s['missed_points'] for s in streaks if s['missed_points'] > 0) / missed if missed > 0 else 0
    
    # Block reason analysis
    block_reasons = {}
    for s in streaks:
        if s['status'] == 'missed' and s['block_reasons']:
            reasons = s['block_reasons'].split(';')
            for reason in reasons:
                reason = reason.strip()
                if reason:
                    block_reasons[reason] = block_reasons.get(reason, 0) + 1
    
    return {
        'bars': len(bars),
        'total_streaks': total,
        'caught': caught,
        'missed': missed,
        'partial': partial,
        'longs': len(longs),
        'shorts': len(shorts),
        'catch_rate': catch_rate,
        'avg_length': avg_length,
        'avg_movement': avg_movement,
        'avg_missed': avg_missed,
        'block_reasons': block_reasons,
        'streaks': streaks
    }


def print_comparison(result1, result2, label1, label2):
    """Print side-by-side comparison"""
    print("\n" + "="*90)
    print("OPPORTUNITY LOG COMPARISON")
    print("="*90)
    
    print(f"\n{'Metric':<30} {label1:>25} {label2:>25}")
    print("-"*90)
    
    # Basic metrics
    print(f"{'Total Bars':<30} {result1['bars']:>25,} {result2['bars']:>25,}")
    print(f"{'Total Streaks Found':<30} {result1['total_streaks']:>25} {result2['total_streaks']:>25}")
    print(f"{'  - Long streaks':<30} {result1['longs']:>25} {result2['longs']:>25}")
    print(f"{'  - Short streaks':<30} {result1['shorts']:>25} {result2['shorts']:>25}")
    
    print()
    print(f"{'Entry Performance':<30}")
    print(f"{'  - Caught':<30} {result1['caught']:>25} {result2['caught']:>25}")
    print(f"{'  - Missed':<30} {result1['missed']:>25} {result2['missed']:>25}")
    print(f"{'  - Partial':<30} {result1['partial']:>25} {result2['partial']:>25}")
    print(f"{'  - Catch Rate':<30} {result1['catch_rate']:>24.1f}% {result2['catch_rate']:>24.1f}%")
    
    print()
    print(f"{'Streak Characteristics':<30}")
    print(f"{'  - Avg Length (bars)':<30} {result1['avg_length']:>24.1f} {result2['avg_length']:>24.1f}")
    print(f"{'  - Avg Movement (pts)':<30} {result1['avg_movement']:>24.2f} {result2['avg_movement']:>24.2f}")
    print(f"{'  - Avg Missed (pts)':<30} {result1['avg_missed']:>24.2f} {result2['avg_missed']:>24.2f}")
    
    print("\n" + "="*90)
    
    # Winner indicators
    winner_streaks = label1 if result1['total_streaks'] > result2['total_streaks'] else label2
    winner_catch = label1 if result1['catch_rate'] > result2['catch_rate'] else label2
    winner_movement = label1 if result1['avg_movement'] > result2['avg_movement'] else label2
    
    print(f"\n📊 Most Streaks: {winner_streaks}")
    print(f"✅ Best Catch Rate: {winner_catch}")
    print(f"📈 Largest Avg Movement: {winner_movement}")
    
    # Block reason comparison
    print("\n" + "="*90)
    print("TOP BLOCK REASONS")
    print("="*90)
    
    all_reasons = set(result1['block_reasons'].keys()) | set(result2['block_reasons'].keys())
    
    print(f"\n{'Reason':<50} {label1[:20]:>15} {label2[:20]:>15}")
    print("-"*90)
    
    for reason in sorted(all_reasons, key=lambda r: result1['block_reasons'].get(r, 0) + result2['block_reasons'].get(r, 0), reverse=True)[:10]:
        count1 = result1['block_reasons'].get(reason, 0)
        count2 = result2['block_reasons'].get(reason, 0)
        print(f"{reason[:50]:<50} {count1:>15} {count2:>15}")
    
    print("\n" + "="*90)


def print_top_missed(result1, result2, label1, label2, n=5):
    """Print top missed opportunities from each log"""
    missed1 = [s for s in result1['streaks'] if s['status'] == 'missed']
    missed1.sort(key=lambda x: x['missed_points'], reverse=True)
    
    missed2 = [s for s in result2['streaks'] if s['status'] == 'missed']
    missed2.sort(key=lambda x: x['missed_points'], reverse=True)
    
    print(f"\nTOP {n} MISSED OPPORTUNITIES - {label1}")
    print("-"*90)
    for idx, streak in enumerate(missed1[:n], 1):
        print(f"{idx}. Bar {streak['start_bar']:>4}-{streak['end_bar']:<4} {streak['direction']:>5}: "
              f"{streak['missed_points']:>6.2f} pts | {streak['block_reasons'][:50]}")
    
    print(f"\nTOP {n} MISSED OPPORTUNITIES - {label2}")
    print("-"*90)
    for idx, streak in enumerate(missed2[:n], 1):
        print(f"{idx}. Bar {streak['start_bar']:>4}-{streak['end_bar']:<4} {streak['direction']:>5}: "
              f"{streak['missed_points']:>6.2f} pts | {streak['block_reasons'][:50]}")


def main():
    args = parse_args()
    
    print(f"\n🔍 Comparing Opportunity Logs...")
    print(f"Log 1: {args.log1}")
    print(f"Log 2: {args.log2}")
    
    print(f"\nAnalyzing {args.label1}...")
    result1 = analyze_log(args.log1, args)
    
    print(f"Analyzing {args.label2}...")
    result2 = analyze_log(args.log2, args)
    
    print_comparison(result1, result2, args.label1, args.label2)
    print_top_missed(result1, result2, args.label1, args.label2, n=5)
    
    print("\n✓ Comparison complete!\n")


if __name__ == '__main__':
    main()
