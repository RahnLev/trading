"""
Opportunity Streak Analysis Script

Analyzes BarsOnTheFlow opportunity logs to find directional streaks (like the 5-bar patterns)
and identify which were caught vs missed.

Usage:
    python analyze_opportunity_streaks.py <opportunity_log.csv> [options]

Options:
    --min-streak <int>       Minimum streak length (default: 5)
    --max-streak <int>       Maximum streak length (default: 8)
    --min-movement <float>   Minimum net price movement in points (default: 5.0)
    --long-grad <float>      Long gradient threshold in degrees (default: 7.0)
    --short-grad <float>     Short gradient threshold in degrees (default: -7.0)
    --counter-ratio <float>  Max counter-trend bar ratio (default: 0.2)
    --type <long|short|both> Analyze longs, shorts, or both (default: both)
    --output <file>          Output CSV file for results (optional)
"""

import csv
import sys
import argparse
from datetime import datetime
from typing import List, Dict, Any


def parse_args():
    parser = argparse.ArgumentParser(description='Analyze opportunity logs for directional streaks')
    parser.add_argument('logfile', help='Path to BarsOnTheFlow_Opportunities_*.csv file')
    parser.add_argument('--min-streak', type=int, default=5, help='Minimum streak length')
    parser.add_argument('--max-streak', type=int, default=8, help='Maximum streak length')
    parser.add_argument('--min-movement', type=float, default=5.0, help='Minimum net movement (points)')
    parser.add_argument('--long-grad', type=float, default=7.0, help='Long gradient threshold (degrees)')
    parser.add_argument('--short-grad', type=float, default=-7.0, help='Short gradient threshold (degrees)')
    parser.add_argument('--counter-ratio', type=float, default=0.2, help='Max counter-trend ratio (0-1)')
    parser.add_argument('--type', choices=['long', 'short', 'both'], default='both', help='Streak type')
    parser.add_argument('--output', help='Output CSV file for results')
    parser.add_argument('--verbose', action='store_true', help='Verbose output')
    return parser.parse_args()


def load_opportunity_log(filepath: str) -> List[Dict[str, Any]]:
    """Load and parse opportunity log CSV"""
    bars = []
    with open(filepath, 'r', encoding='utf-8') as f:
        reader = csv.DictReader(f)
        for row in reader:
            try:
                bars.append({
                    'bar': int(row['bar']),
                    'timestamp': row['timestamp'],
                    'open': float(row['open']),
                    'high': float(row['high']),
                    'low': float(row['low']),
                    'close': float(row['close']),
                    'candleType': row['candleType'],
                    'fastEmaGradDeg': float(row['fastEmaGradDeg']) if row['fastEmaGradDeg'] not in ('', 'NaN') else None,
                    'trendUpSignal': row['trendUpSignal'] == 'True',
                    'trendDownSignal': row['trendDownSignal'] == 'True',
                    'currentPosition': row['currentPosition'],
                    'entryBar': int(row['entryBar']) if row['entryBar'] not in ('', '-1') else -1,
                    'actionTaken': row['actionTaken'],
                    'blockReason': row['blockReason'],
                    'opportunityType': row['opportunityType'],
                    'barPattern': row.get('barPattern', ''),
                    'goodCount': int(row.get('goodCount', 0)),
                    'badCount': int(row.get('badCount', 0)),
                    'netPnl': float(row.get('netPnl', 0.0))
                })
            except Exception as e:
                print(f"Warning: Skipping row due to parse error: {e}")
                continue
    return bars


def find_streaks(bars: List[Dict], args) -> List[Dict]:
    """
    Find directional streaks in the bar data.
    
    A streak is a sequence of bars with:
    1. Consistent overall direction (net positive/negative movement)
    2. Minimum net movement threshold
    3. Allowable counter-trend bars based on ratio
    4. Optional gradient threshold check
    """
    streaks = []
    i = 0
    
    while i < len(bars) - args.min_streak + 1:
        # Try to find a streak starting at bar i
        for length in range(args.max_streak, args.min_streak - 1, -1):
            if i + length > len(bars):
                continue
            
            streak_bars = bars[i:i+length]
            
            # Calculate net movement and direction
            start_price = streak_bars[0]['open']
            end_price = streak_bars[-1]['close']
            net_movement = end_price - start_price
            
            if abs(net_movement) < args.min_movement:
                continue
            
            direction = 'LONG' if net_movement > 0 else 'SHORT'
            
            # Filter by streak type
            if args.type == 'long' and direction != 'LONG':
                continue
            if args.type == 'short' and direction != 'SHORT':
                continue
            
            # Count good/bad bars
            good_bars = 0
            bad_bars = 0
            for bar in streak_bars:
                is_good_candle = bar['candleType'] == 'good'
                if (direction == 'LONG' and is_good_candle) or (direction == 'SHORT' and not is_good_candle):
                    good_bars += 1
                else:
                    bad_bars += 1
            
            # Check counter-trend ratio
            actual_counter_ratio = bad_bars / length if length > 0 else 0
            if actual_counter_ratio > args.counter_ratio:
                continue
            
            # Calculate average gradient
            gradients = [b['fastEmaGradDeg'] for b in streak_bars if b['fastEmaGradDeg'] is not None]
            avg_gradient = sum(gradients) / len(gradients) if gradients else 0
            
            # Check if gradient meets threshold
            gradient_ok = True
            if direction == 'LONG' and avg_gradient < args.long_grad:
                gradient_ok = False
            if direction == 'SHORT' and avg_gradient > args.short_grad:
                gradient_ok = False
            
            # Determine entry status
            entry_bar = -1
            entry_status = 'missed'
            missed_points = abs(net_movement)
            block_reasons = []
            
            # Check if we entered during this streak
            for idx, bar in enumerate(streak_bars):
                if bar['entryBar'] >= 0 and bar['currentPosition'] != 'Flat':
                    entry_bar = bar['bar']
                    if idx == 0:
                        entry_status = 'caught'
                        missed_points = 0
                    else:
                        entry_status = 'partial'
                        entry_price = streak_bars[idx]['open']
                        missed_points = abs(entry_price - start_price)
                    break
                
                # Capture block reasons
                if bar['opportunityType'] in ('LONG_SIGNAL', 'SHORT_SIGNAL'):
                    if 'SKIPPED' in bar['actionTaken'] or 'BLOCKED' in bar['actionTaken']:
                        if bar['blockReason'] and bar['blockReason'] not in block_reasons:
                            block_reasons.append(bar['blockReason'])
            
            # Only add streaks that were either caught or had a valid signal
            if entry_status != 'missed' or block_reasons:
                streaks.append({
                    'start_bar': streak_bars[0]['bar'],
                    'end_bar': streak_bars[-1]['bar'],
                    'start_time': streak_bars[0]['timestamp'],
                    'end_time': streak_bars[-1]['timestamp'],
                    'length': length,
                    'direction': direction,
                    'net_movement': net_movement,
                    'avg_gradient': avg_gradient,
                    'good_bars': good_bars,
                    'bad_bars': bad_bars,
                    'counter_ratio': actual_counter_ratio,
                    'status': entry_status,
                    'entry_bar': entry_bar,
                    'missed_points': missed_points,
                    'block_reasons': '; '.join(block_reasons),
                    'pattern': streak_bars[0].get('barPattern', ''),
                    'gradient_ok': gradient_ok,
                    'start_price': start_price,
                    'end_price': end_price
                })
                
                # Skip ahead to avoid overlapping streaks
                i += length
                break
        else:
            i += 1
    
    return streaks


def print_summary(streaks: List[Dict], bars: List[Dict], args):
    """Print analysis summary"""
    total = len(streaks)
    caught = sum(1 for s in streaks if s['status'] == 'caught')
    missed = sum(1 for s in streaks if s['status'] == 'missed')
    partial = sum(1 for s in streaks if s['status'] == 'partial')
    
    longs = [s for s in streaks if s['direction'] == 'LONG']
    shorts = [s for s in streaks if s['direction'] == 'SHORT']
    
    avg_length = sum(s['length'] for s in streaks) / total if total > 0 else 0
    avg_movement = sum(abs(s['net_movement']) for s in streaks) / total if total > 0 else 0
    avg_missed = sum(s['missed_points'] for s in streaks if s['missed_points'] > 0) / missed if missed > 0 else 0
    
    catch_rate = (caught / total * 100) if total > 0 else 0
    
    print("\n" + "="*70)
    print("OPPORTUNITY STREAK ANALYSIS SUMMARY")
    print("="*70)
    print(f"\nData Source: {len(bars)} bars analyzed")
    print(f"Parameters:")
    print(f"  - Streak range: {args.min_streak}-{args.max_streak} bars")
    print(f"  - Min movement: {args.min_movement} points")
    print(f"  - Long gradient threshold: {args.long_grad}°")
    print(f"  - Short gradient threshold: {args.short_grad}°")
    print(f"  - Max counter-trend ratio: {args.counter_ratio}")
    print(f"  - Type: {args.type}")
    
    print(f"\n{'RESULTS':^70}")
    print("-"*70)
    print(f"Total Streaks Found:     {total:>6}")
    print(f"  - Long streaks:        {len(longs):>6}")
    print(f"  - Short streaks:       {len(shorts):>6}")
    print()
    print(f"Entry Status:")
    print(f"  - Caught (entered):    {caught:>6}  ({caught/total*100:.1f}%)" if total > 0 else f"  - Caught (entered):    {caught:>6}")
    print(f"  - Missed completely:   {missed:>6}  ({missed/total*100:.1f}%)" if total > 0 else f"  - Missed completely:   {missed:>6}")
    print(f"  - Partial (late):      {partial:>6}  ({partial/total*100:.1f}%)" if total > 0 else f"  - Partial (late):      {partial:>6}")
    print()
    print(f"Performance Metrics:")
    print(f"  - Catch rate:          {catch_rate:>6.1f}%")
    print(f"  - Avg streak length:   {avg_length:>6.1f} bars")
    print(f"  - Avg net movement:    {avg_movement:>6.2f} points")
    print(f"  - Avg missed points:   {avg_missed:>6.2f} points")
    print("="*70 + "\n")


def print_detailed_streaks(streaks: List[Dict], args):
    """Print detailed streak information"""
    if not args.verbose:
        return
    
    print("\nDETAILED STREAK BREAKDOWN")
    print("="*70)
    
    for idx, streak in enumerate(streaks, 1):
        status_emoji = "✅" if streak['status'] == 'caught' else "❌" if streak['status'] == 'missed' else "⚠️"
        direction_emoji = "📈" if streak['direction'] == 'LONG' else "📉"
        
        print(f"\n{status_emoji} Streak #{idx} {direction_emoji} {streak['direction']}")
        print(f"  Bars: {streak['start_bar']}-{streak['end_bar']} (length: {streak['length']})")
        print(f"  Time: {streak['start_time']} → {streak['end_time']}")
        print(f"  Price: {streak['start_price']:.2f} → {streak['end_price']:.2f} (net: {streak['net_movement']:+.2f})")
        print(f"  Composition: {streak['good_bars']} good / {streak['bad_bars']} bad (ratio: {streak['counter_ratio']:.2%})")
        print(f"  Avg Gradient: {streak['avg_gradient']:.2f}° {'✓' if streak['gradient_ok'] else '✗'}")
        print(f"  Status: {streak['status'].upper()}")
        
        if streak['entry_bar'] >= 0:
            print(f"  Entry Bar: {streak['entry_bar']}")
        if streak['missed_points'] > 0:
            print(f"  Missed Points: {streak['missed_points']:.2f}")
        if streak['block_reasons']:
            print(f"  Block Reasons: {streak['block_reasons']}")
        if streak['pattern']:
            print(f"  Pattern: {streak['pattern']}")


def save_to_csv(streaks: List[Dict], output_path: str):
    """Save results to CSV file"""
    if not output_path:
        return
    
    fieldnames = [
        'streak_num', 'direction', 'status', 'start_bar', 'end_bar', 'length',
        'start_time', 'end_time', 'start_price', 'end_price', 'net_movement',
        'good_bars', 'bad_bars', 'counter_ratio', 'avg_gradient', 'gradient_ok',
        'entry_bar', 'missed_points', 'block_reasons', 'pattern'
    ]
    
    with open(output_path, 'w', newline='', encoding='utf-8') as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        
        for idx, streak in enumerate(streaks, 1):
            row = {
                'streak_num': idx,
                **{k: v for k, v in streak.items() if k in fieldnames}
            }
            writer.writerow(row)
    
    print(f"\n✓ Results saved to: {output_path}")


def main():
    args = parse_args()
    
    print(f"\nLoading opportunity log: {args.logfile}")
    bars = load_opportunity_log(args.logfile)
    print(f"✓ Loaded {len(bars)} bars")
    
    print(f"\nSearching for {args.type} streaks ({args.min_streak}-{args.max_streak} bars)...")
    streaks = find_streaks(bars, args)
    print(f"✓ Found {len(streaks)} streaks")
    
    print_summary(streaks, bars, args)
    print_detailed_streaks(streaks, args)
    
    if args.output:
        save_to_csv(streaks, args.output)
    
    # Print top missed opportunities
    missed_streaks = [s for s in streaks if s['status'] == 'missed' and s['missed_points'] > 0]
    if missed_streaks:
        print("\nTOP 10 MISSED OPPORTUNITIES (by points)")
        print("-"*70)
        missed_streaks.sort(key=lambda x: x['missed_points'], reverse=True)
        for idx, streak in enumerate(missed_streaks[:10], 1):
            print(f"{idx:2}. Bar {streak['start_bar']:>4}-{streak['end_bar']:<4} {streak['direction']:>5}: "
                  f"{streak['missed_points']:>6.2f} pts  |  {streak['block_reasons'][:50]}")


if __name__ == '__main__':
    main()
