"""
EMA Gradient Threshold Optimization
Tests different gradient thresholds for LONG and SHORT entries independently
"""

import csv
import os
from datetime import datetime

# Configuration
MIN_BARS_FOR_TRADE = 4
ENTRY_BAR_DELAY = 3
GRADIENT_THRESHOLDS = [0.2, 0.4, 0.6, 0.8, 1.0]  # Test values

def find_latest_csv():
    """Find the latest CSV file in indicators_log directory"""
    log_dir = os.path.join(os.path.dirname(__file__), 'indicators_log')
    if not os.path.exists(log_dir):
        raise FileNotFoundError(f"Directory not found: {log_dir}")
    
    csv_files = [f for f in os.listdir(log_dir) 
                 if f.endswith('.csv') and 'signal' not in f.lower()]
    
    if not csv_files:
        raise FileNotFoundError(f"No CSV files found in {log_dir}")
    
    csv_files.sort(key=lambda x: os.path.getmtime(os.path.join(log_dir, x)), reverse=True)
    return os.path.join(log_dir, csv_files[0])

def calculate_return(entry_price, exit_price, direction):
    """Calculate return percentage"""
    if direction == 'LONG':
        return ((exit_price - entry_price) / entry_price) * 100
    else:  # SHORT
        return ((entry_price - exit_price) / entry_price) * 100

def test_gradient_threshold(data, gradient_history, ranges, direction, threshold):
    """Test a specific gradient threshold for LONG or SHORT trades"""
    filtered_ranges = [r for r in ranges 
                      if r['signal'] == direction 
                      and (r['end_bar'] - r['start_bar'] + 1) >= MIN_BARS_FOR_TRADE]
    
    wins = 0
    total = 0
    skipped = 0
    total_return = 0
    
    for r in filtered_ranges:
        entry_bar_index = r['start_bar'] + (ENTRY_BAR_DELAY - 1)
        
        # Find entry price
        entry_price = r['start_price']
        for d in data:
            if d['bar'] == entry_bar_index:
                entry_price = d['close']
                break
        
        # Get gradient at entry bar
        entry_gradient = 0
        for gh in gradient_history:
            if gh['bar'] == entry_bar_index:
                entry_gradient = gh['fe_grad']
                break
        
        # Check gradient threshold
        if direction == 'LONG':
            if entry_gradient < threshold:
                skipped += 1
                continue
        else:  # SHORT
            if entry_gradient > -threshold:
                skipped += 1
                continue
        
        # Calculate return
        ret = calculate_return(entry_price, r['end_price'], direction)
        total_return += ret
        
        if ret > 0:
            wins += 1
        total += 1
    
    win_rate = (wins / total * 100) if total > 0 else 0
    avg_return = (total_return / total) if total > 0 else 0
    
    return {
        'threshold': threshold,
        'direction': direction,
        'total': total,
        'wins': wins,
        'win_rate': win_rate,
        'skipped': skipped,
        'avg_return': avg_return,
        'total_return': total_return
    }

def main():
    print("=" * 80)
    print("EMA GRADIENT THRESHOLD OPTIMIZATION")
    print("=" * 80)
    
    # Find and load CSV
    csv_path = find_latest_csv()
    print(f"\nAnalyzing: {os.path.basename(csv_path)}")
    
    # Load data
    data = []
    with open(csv_path, 'r') as f:
        reader = csv.DictReader(f)
        for row in reader:
            try:
                bar = int(row['bar_index'])
                close = float(row['close'])
                ema10 = float(row['ema10'])
                ema20 = float(row['ema20'])
                
                data.append({
                    'bar': bar,
                    'time': row['bar_time_utc'],
                    'close': close,
                    'ema10': ema10,
                    'ema20': ema20
                })
            except (ValueError, KeyError):
                continue
    
    print(f"Loaded {len(data)} bars with valid data\n")
    
    # Calculate gradients and identify ranges
    gradient_history = []
    prev_fe = None
    prev_se = None
    
    for row in data:
        fe = row['ema10']
        se = row['ema20']
        
        if prev_fe is not None and prev_se is not None:
            fe_grad = fe - prev_fe
            se_grad = se - prev_se
            
            gradient_history.append({
                'bar': row['bar'],
                'fe_grad': fe_grad,
                'se_grad': se_grad
            })
        else:
            gradient_history.append({
                'bar': row['bar'],
                'fe_grad': 0,
                'se_grad': 0
            })
        
        prev_fe = fe
        prev_se = se
    
    # Identify signal ranges
    ranges = []
    current_signal = 'FLAT'
    range_start = None
    range_start_time = None
    range_start_price = None
    
    for i, row in enumerate(data):
        close = row['close']
        ema10 = row['ema10']
        ema20 = row['ema20']
        bar = row['bar']
        
        # Get gradients
        fe_grad = 0
        se_grad = 0
        for gh in gradient_history:
            if gh['bar'] == bar:
                fe_grad = gh['fe_grad']
                se_grad = gh['se_grad']
                break
        
        # Determine signal (without gradient threshold)
        if fe_grad > 0 and se_grad > 0 and close > ema10 and close > ema20:
            new_signal = 'LONG'
        elif fe_grad < 0 and se_grad < 0 and close < ema10 and close < ema20:
            new_signal = 'SHORT'
        else:
            new_signal = 'FLAT'
        
        # Detect signal changes
        if new_signal != current_signal:
            if current_signal != 'FLAT' and range_start is not None:
                ranges.append({
                    'signal': current_signal,
                    'start_bar': range_start,
                    'end_bar': data[i-1]['bar'],
                    'start_time': range_start_time,
                    'end_time': data[i-1]['time'],
                    'start_price': range_start_price,
                    'end_price': data[i-1]['close']
                })
            
            current_signal = new_signal
            range_start = bar
            range_start_time = row['time']
            range_start_price = close
    
    # Add final range
    if current_signal != 'FLAT' and range_start is not None:
        ranges.append({
            'signal': current_signal,
            'start_bar': range_start,
            'end_bar': data[-1]['bar'],
            'start_time': range_start_time,
            'end_time': data[-1]['time'],
            'start_price': range_start_price,
            'end_price': data[-1]['close']
        })
    
    print(f"Identified {len(ranges)} signal ranges\n")
    
    # Run optimization
    results = []
    
    print("=" * 80)
    print("OPTIMIZATION RESULTS")
    print("=" * 80)
    print()
    
    # Test each combination
    for long_threshold in GRADIENT_THRESHOLDS:
        for short_threshold in GRADIENT_THRESHOLDS:
            # Test LONG with long_threshold
            long_result = test_gradient_threshold(
                data, gradient_history, ranges, 'LONG', long_threshold
            )
            
            # Test SHORT with short_threshold
            short_result = test_gradient_threshold(
                data, gradient_history, ranges, 'SHORT', short_threshold
            )
            
            # Combined metrics
            total_trades = long_result['total'] + short_result['total']
            total_wins = long_result['wins'] + short_result['wins']
            overall_win_rate = (total_wins / total_trades * 100) if total_trades > 0 else 0
            total_skipped = long_result['skipped'] + short_result['skipped']
            combined_avg_return = (
                (long_result['total_return'] + short_result['total_return']) / total_trades
            ) if total_trades > 0 else 0
            
            result = {
                'long_threshold': long_threshold,
                'short_threshold': short_threshold,
                'long_trades': long_result['total'],
                'long_wins': long_result['wins'],
                'long_win_rate': long_result['win_rate'],
                'long_skipped': long_result['skipped'],
                'short_trades': short_result['total'],
                'short_wins': short_result['wins'],
                'short_win_rate': short_result['win_rate'],
                'short_skipped': short_result['skipped'],
                'total_trades': total_trades,
                'total_wins': total_wins,
                'overall_win_rate': overall_win_rate,
                'total_skipped': total_skipped,
                'avg_return': combined_avg_return
            }
            
            results.append(result)
            
            print(f"LONG Threshold: {long_threshold:.1f} | SHORT Threshold: {short_threshold:.1f}")
            print(f"  LONG:  {long_result['wins']}/{long_result['total']} = {long_result['win_rate']:.1f}% | Skipped: {long_result['skipped']}")
            print(f"  SHORT: {short_result['wins']}/{short_result['total']} = {short_result['win_rate']:.1f}% | Skipped: {short_result['skipped']}")
            print(f"  OVERALL: {total_wins}/{total_trades} = {overall_win_rate:.1f}% | Avg Return: {combined_avg_return:+.3f}%")
            print()
    
    # Sort by overall win rate
    results.sort(key=lambda x: x['overall_win_rate'], reverse=True)
    
    print("=" * 80)
    print("TOP 10 CONFIGURATIONS (by win rate)")
    print("=" * 80)
    print()
    
    for i, r in enumerate(results[:10], 1):
        print(f"#{i}: LONG={r['long_threshold']:.1f}, SHORT={r['short_threshold']:.1f}")
        print(f"    Win Rate: {r['overall_win_rate']:.1f}% ({r['total_wins']}/{r['total_trades']} trades)")
        print(f"    LONG: {r['long_win_rate']:.1f}% ({r['long_wins']}/{r['long_trades']})")
        print(f"    SHORT: {r['short_win_rate']:.1f}% ({r['short_wins']}/{r['short_trades']})")
        print(f"    Avg Return: {r['avg_return']:+.3f}%")
        print()
    
    # Save detailed results to CSV
    output_file = f"gradient_optimization_results_{datetime.now().strftime('%Y%m%d_%H%M%S')}.csv"
    with open(output_file, 'w', newline='') as f:
        writer = csv.DictWriter(f, fieldnames=[
            'long_threshold', 'short_threshold', 
            'long_trades', 'long_wins', 'long_win_rate', 'long_skipped',
            'short_trades', 'short_wins', 'short_win_rate', 'short_skipped',
            'total_trades', 'total_wins', 'overall_win_rate', 'total_skipped', 'avg_return'
        ])
        writer.writeheader()
        writer.writerows(results)
    
    print(f"Detailed results saved to: {output_file}")
    print("\nOPTIMIZATION COMPLETE")

if __name__ == '__main__':
    main()
