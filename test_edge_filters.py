"""
Test various edge filters for EMA gradient strategy
"""
import csv
import os

def find_latest_csv():
    log_dir = r"c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\indicators_log"
    csv_files = [f for f in os.listdir(log_dir) if f.endswith('.csv') and 'signal' not in f.lower()]
    csv_files.sort(key=lambda x: os.path.getmtime(os.path.join(log_dir, x)), reverse=True)
    return os.path.join(log_dir, csv_files[0])

def calculate_return(entry_price, exit_price, direction):
    if direction == 'LONG':
        return ((exit_price - entry_price) / entry_price) * 100
    else:
        return ((entry_price - exit_price) / entry_price) * 100

def test_filter(data, ranges, filter_name, filter_func):
    """Test a specific filter function"""
    results = {'LONG': {'wins': 0, 'total': 0}, 'SHORT': {'wins': 0, 'total': 0}}
    
    for r in ranges:
        direction = r['signal']
        if direction == 'FLAT':
            continue
            
        # Apply filter
        if not filter_func(r, data):
            continue
        
        # Calculate return
        ret = calculate_return(r['entry_price'], r['end_price'], direction)
        
        results[direction]['total'] += 1
        if ret > 0:
            results[direction]['wins'] += 1
    
    # Calculate stats
    long_wr = (results['LONG']['wins'] / results['LONG']['total'] * 100) if results['LONG']['total'] > 0 else 0
    short_wr = (results['SHORT']['wins'] / results['SHORT']['total'] * 100) if results['SHORT']['total'] > 0 else 0
    total = results['LONG']['total'] + results['SHORT']['total']
    total_wins = results['LONG']['wins'] + results['SHORT']['wins']
    overall_wr = (total_wins / total * 100) if total > 0 else 0
    
    return {
        'filter': filter_name,
        'long_trades': results['LONG']['total'],
        'long_wr': long_wr,
        'short_trades': results['SHORT']['total'],
        'short_wr': short_wr,
        'total_trades': total,
        'overall_wr': overall_wr
    }

# Load data
csv_path = find_latest_csv()
print(f"Analyzing: {os.path.basename(csv_path)}\n")

data = []
with open(csv_path, 'r') as f:
    reader = csv.DictReader(f)
    for row in reader:
        try:
            data.append({
                'bar': int(row['bar_index']),
                'time': row['bar_time_utc'],
                'close': float(row['close']),
                'ema10': float(row['ema10']),
                'ema20': float(row['ema20']),
                'atr30': float(row['atr30']) if 'atr30' in row else 0
            })
        except (ValueError, KeyError):
            continue

print(f"Loaded {len(data)} bars\n")

# Calculate gradients
gradient_history = []
for i in range(len(data)):
    if i == 0:
        gradient_history.append({'bar': data[i]['bar'], 'fe_grad': 0, 'se_grad': 0, 'fe_accel': 0})
    else:
        fe_grad = data[i]['ema10'] - data[i-1]['ema10']
        se_grad = data[i]['ema20'] - data[i-1]['ema20']
        
        # Calculate acceleration (change in gradient)
        prev_grad = gradient_history[-1]['fe_grad']
        fe_accel = fe_grad - prev_grad if prev_grad != 0 else 0
        
        gradient_history.append({
            'bar': data[i]['bar'],
            'fe_grad': fe_grad,
            'se_grad': se_grad,
            'fe_accel': fe_accel
        })

# Identify ranges and prepare for testing
ranges = []
current_signal = 'FLAT'
range_start = None

for i, row in enumerate(data):
    gh = gradient_history[i]
    
    # Determine signal
    if gh['fe_grad'] > 0 and gh['se_grad'] > 0 and row['close'] > row['ema10'] and row['close'] > row['ema20']:
        new_signal = 'LONG'
    elif gh['fe_grad'] < 0 and gh['se_grad'] < 0 and row['close'] < row['ema10'] and row['close'] < row['ema20']:
        new_signal = 'SHORT'
    else:
        new_signal = 'FLAT'
    
    if new_signal != current_signal:
        if current_signal != 'FLAT' and range_start is not None:
            # Range ended - check if lasted 3+ bars
            duration = i - range_start
            if duration >= 3:
                # Entry on 3rd bar
                entry_idx = range_start + 2
                ranges.append({
                    'signal': current_signal,
                    'start_idx': range_start,
                    'entry_idx': entry_idx,
                    'end_idx': i - 1,
                    'duration': duration,
                    'entry_price': data[entry_idx]['close'],
                    'end_price': data[i-1]['close'],
                    'entry_fe_grad': gradient_history[entry_idx]['fe_grad'],
                    'entry_se_grad': gradient_history[entry_idx]['se_grad'],
                    'entry_fe_accel': gradient_history[entry_idx]['fe_accel'],
                    'start_fe_grad': gradient_history[range_start]['fe_grad'],
                    'atr': data[entry_idx]['atr30']
                })
        
        current_signal = new_signal
        range_start = i if new_signal != 'FLAT' else None

print(f"Found {len(ranges)} ranges lasting 3+ bars\n")
print("=" * 80)
print("TESTING EDGE FILTERS")
print("=" * 80)

# Define filters to test
filters = [
    ("No Filter", lambda r, d: True),
    
    ("Strong Gradient (>=1.0)", 
     lambda r, d: abs(r['entry_fe_grad']) >= 1.0),
    
    ("Very Strong Gradient (>=2.0)", 
     lambda r, d: abs(r['entry_fe_grad']) >= 2.0),
    
    ("Accelerating (grad growing)", 
     lambda r, d: (r['entry_fe_accel'] > 0 if r['signal'] == 'LONG' else r['entry_fe_accel'] < 0)),
    
    ("Strong + Accelerating", 
     lambda r, d: abs(r['entry_fe_grad']) >= 1.0 and 
                  (r['entry_fe_accel'] > 0 if r['signal'] == 'LONG' else r['entry_fe_accel'] < 0)),
    
    ("Price Away from EMA (>0.5 ATR)", 
     lambda r, d: abs(r['entry_price'] - d[r['entry_idx']]['ema10']) > (r['atr'] * 0.5) if r['atr'] > 0 else True),
    
    ("Both Gradients Strong (>=1.0)", 
     lambda r, d: abs(r['entry_fe_grad']) >= 1.0 and abs(r['entry_se_grad']) >= 0.5),
    
    ("Gradient Ratio (fast > 2x slow)", 
     lambda r, d: abs(r['entry_fe_grad']) > abs(r['entry_se_grad']) * 2 if r['entry_se_grad'] != 0 else False),
    
    ("Price Gradient >0.5", 
     lambda r, d: (d[r['entry_idx']]['close'] - d[r['entry_idx']-1]['close']) > 0.5 if r['signal'] == 'LONG' 
                  else (d[r['entry_idx']-1]['close'] - d[r['entry_idx']]['close']) > 0.5 if r['entry_idx'] > 0 else False),
    
    ("Price Gradient >1.0", 
     lambda r, d: (d[r['entry_idx']]['close'] - d[r['entry_idx']-1]['close']) > 1.0 if r['signal'] == 'LONG' 
                  else (d[r['entry_idx']-1]['close'] - d[r['entry_idx']]['close']) > 1.0 if r['entry_idx'] > 0 else False),
    
    ("Price Gradient >2.0", 
     lambda r, d: (d[r['entry_idx']]['close'] - d[r['entry_idx']-1]['close']) > 2.0 if r['signal'] == 'LONG' 
                  else (d[r['entry_idx']-1]['close'] - d[r['entry_idx']]['close']) > 2.0 if r['entry_idx'] > 0 else False),
    
    ("Price + EMA Gradient Both Strong", 
     lambda r, d: abs(r['entry_fe_grad']) >= 1.0 and 
                  ((d[r['entry_idx']]['close'] - d[r['entry_idx']-1]['close']) > 1.0 if r['signal'] == 'LONG' 
                   else (d[r['entry_idx']-1]['close'] - d[r['entry_idx']]['close']) > 1.0) if r['entry_idx'] > 0 else False),
    
    ("Avg Price Gradient (3 bars) >0.5", 
     lambda r, d: _avg_price_gradient(r, d, 3, 0.5)),
    
    ("Avg Price Gradient (3 bars) >1.0", 
     lambda r, d: _avg_price_gradient(r, d, 3, 1.0)),
]

def _avg_price_gradient(r, d, bars, threshold):
    """Calculate average price gradient over N bars"""
    if r['entry_idx'] < bars:
        return False
    
    total_grad = 0
    for i in range(bars):
        idx = r['entry_idx'] - i
        if idx <= 0:
            return False
        grad = d[idx]['close'] - d[idx-1]['close']
        if r['signal'] == 'LONG':
            total_grad += grad
        else:
            total_grad += -grad  # Reverse for short
    
    avg_grad = total_grad / bars
    return avg_grad > threshold

# Test each filter
results = []
for filter_name, filter_func in filters:
    result = test_filter(data, ranges, filter_name, filter_func)
    results.append(result)
    
    print(f"\n{filter_name}")
    print(f"  LONG:  {result['long_wr']:.1f}% ({result['long_trades']} trades)")
    print(f"  SHORT: {result['short_wr']:.1f}% ({result['short_trades']} trades)")
    print(f"  OVERALL: {result['overall_wr']:.1f}% ({result['total_trades']} trades)")

print("\n" + "=" * 80)
print("RANKED BY OVERALL WIN RATE")
print("=" * 80)

results.sort(key=lambda x: x['overall_wr'], reverse=True)
for i, r in enumerate(results, 1):
    print(f"\n#{i}: {r['filter']}")
    print(f"    Overall: {r['overall_wr']:.1f}% ({r['total_trades']} trades)")
    print(f"    LONG: {r['long_wr']:.1f}% | SHORT: {r['short_wr']:.1f}%")

print("\n")
