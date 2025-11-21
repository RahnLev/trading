import csv
import os

# Configuration
MIN_BARS_FOR_TRADE = 3  # Minimum bars required to even have a 3rd bar to enter (must last 3+ bars)
ENTRY_BAR_DELAY = 3     # Wait this many bars before entering (3 = enter on 3rd bar of signal)
MIN_FAST_EMA_GRADIENT = 1.0  # Minimum gradient for fast EMA to enter trade (higher = stronger momentum)
INDICATORS_LOG_FOLDER = r"c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\indicators_log"

def find_latest_csv():
    """Find the most recent CSV file (excluding signal files)"""
    csv_files = [f for f in os.listdir(INDICATORS_LOG_FOLDER) 
                 if f.endswith('.csv') and 'signal' not in f.lower()]
    if not csv_files:
        raise FileNotFoundError("No CSV files found")
    csv_files.sort(key=lambda x: os.path.getmtime(os.path.join(INDICATORS_LOG_FOLDER, x)), reverse=True)
    return os.path.join(INDICATORS_LOG_FOLDER, csv_files[0])

def calculate_return(entry_price, exit_price, direction):
    """Calculate return percentage for a position"""
    if direction == 'LONG':
        return ((exit_price - entry_price) / entry_price) * 100
    else:  # SHORT
        return ((entry_price - exit_price) / entry_price) * 100

print("=" * 80)
print("DETAILED EMA GRADIENT RANGE REPORT")
print("=" * 80)

csv_file = find_latest_csv()
print(f"\nAnalyzing: {os.path.basename(csv_file)}\n")

# Read and process data
data = []
with open(csv_file, 'r', encoding='utf-8-sig') as f:
    reader = csv.DictReader(f)
    for row in reader:
        try:
            data.append({
                'bar': int(row['bar_index']),
                'time': row['bar_time_utc'],
                'close': float(row['close']),
                'ema10': float(row['ema10']),
                'ema20': float(row['ema20'])
            })
        except (ValueError, KeyError) as e:
            continue

print(f"Loaded {len(data)} bars with valid data\n")

# Calculate gradients and identify ranges
ranges = []
current_range = None
gradient_history = []  # Track gradients for each bar

for i in range(1, len(data)):
    row = data[i]
    prev_row = data[i-1]
    
    # Calculate gradients (positive = rising, negative = falling)
    fe_grad = row['ema10'] - prev_row['ema10']
    se_grad = row['ema20'] - prev_row['ema20']
    
    # Store gradient info for this bar
    gradient_history.append({
        'bar': row['bar'],
        'fe_grad': fe_grad,
        'se_grad': se_grad
    })
    
    # Determine conditions (ANY positive/negative change counts)
    fe_rising = fe_grad > 0
    se_rising = se_grad > 0
    fe_falling = fe_grad < 0
    se_falling = se_grad < 0
    
    price_above_both = row['close'] > row['ema10'] and row['close'] > row['ema20']
    price_below_both = row['close'] < row['ema10'] and row['close'] < row['ema20']
    
    # Identify signal
    signal = None
    if fe_rising and se_rising and price_above_both:
        signal = 'LONG'
    elif fe_falling and se_falling and price_below_both:
        signal = 'SHORT'
    
    # Track ranges
    if signal:
        if current_range is None or current_range['signal'] != signal:
            # Start new range
            if current_range:
                ranges.append(current_range)
            current_range = {
                'signal': signal,
                'start_bar': row['bar'],
                'start_time': row['time'],
                'start_price': row['close'],
                'end_bar': row['bar'],
                'end_time': row['time'],
                'end_price': row['close']
            }
        else:
            # Continue current range
            current_range['end_bar'] = row['bar']
            current_range['end_time'] = row['time']
            current_range['end_price'] = row['close']
    else:
        # End current range
        if current_range:
            ranges.append(current_range)
            current_range = None

# Don't forget the last range
if current_range:
    ranges.append(current_range)

# Print detailed report
print("=" * 80)
print(f"LONG RANGES (Both EMAs positive gradient, price above both)")
print(f"Minimum {MIN_BARS_FOR_TRADE} bars required | Entry on bar #{ENTRY_BAR_DELAY}")
print("=" * 80)

long_ranges = [r for r in ranges if r['signal'] == 'LONG']
# Filter for minimum bar count (must have at least ENTRY_BAR_DELAY bars to enter)
long_ranges_filtered = [r for r in long_ranges if (r['end_bar'] - r['start_bar'] + 1) >= MIN_BARS_FOR_TRADE]
long_wins = 0
long_total = 0
long_skipped = 0

for i, r in enumerate(long_ranges_filtered, 1):
    duration = r['end_bar'] - r['start_bar'] + 1
    
    # Calculate entry price (enter on ENTRY_BAR_DELAY-th bar of signal)
    entry_bar_index = r['start_bar'] + (ENTRY_BAR_DELAY - 1)
    entry_price = r['start_price']  # Default to start
    
    # Find actual entry price and gradient at entry bar
    entry_gradient = 0
    for d in data:
        if d['bar'] == entry_bar_index:
            entry_price = d['close']
            break
    
    # Get gradient at entry bar
    for gh in gradient_history:
        if gh['bar'] == entry_bar_index:
            entry_gradient = gh['fe_grad']
            break
    
    # Check if gradient meets minimum requirement for LONG (positive gradient >= threshold)
    if entry_gradient < MIN_FAST_EMA_GRADIENT:
        long_skipped += 1
        continue  # Skip this trade
    
    ret = calculate_return(entry_price, r['end_price'], 'LONG')
    is_win = ret > 0
    if is_win:
        long_wins += 1
    long_total += 1
    
    print(f"\n#{long_total:3d} | Bars {r['start_bar']:4d} -> {r['end_bar']:4d} ({duration:3d} bars)")
    print(f"      | Time: {r['start_time']} -> {r['end_time']}")
    print(f"      | Entry bar {entry_bar_index} (delay={ENTRY_BAR_DELAY}, grad={entry_gradient:+.3f}): {entry_price:.2f} -> Exit: {r['end_price']:.2f}")
    print(f"      | Return: {ret:+.3f}% {'WIN' if is_win else 'LOSS'}")

long_win_rate = (long_wins / long_total * 100) if long_total > 0 else 0
print(f"\nLONG Summary: {long_total} ranges (>={MIN_BARS_FOR_TRADE} bars), {long_wins} wins, {long_win_rate:.1f}% win rate")
print(f"   Excluded: {len(long_ranges) - len(long_ranges_filtered)} ranges with <{MIN_BARS_FOR_TRADE} bars")

print("\n" + "=" * 80)
print(f"SHORT RANGES (Both EMAs negative gradient, price below both)")
print(f"Minimum {MIN_BARS_FOR_TRADE} bars required | Entry on bar #{ENTRY_BAR_DELAY}")
print("=" * 80)

short_ranges = [r for r in ranges if r['signal'] == 'SHORT']
# Filter for minimum bar count (must have at least ENTRY_BAR_DELAY bars to enter)
short_ranges_filtered = [r for r in short_ranges if (r['end_bar'] - r['start_bar'] + 1) >= MIN_BARS_FOR_TRADE]
short_wins = 0
short_total = 0
short_skipped = 0

for i, r in enumerate(short_ranges_filtered, 1):
    duration = r['end_bar'] - r['start_bar'] + 1
    
    # Calculate entry price (enter on ENTRY_BAR_DELAY-th bar of signal)
    entry_bar_index = r['start_bar'] + (ENTRY_BAR_DELAY - 1)
    entry_price = r['start_price']  # Default to start
    
    # Find actual entry price and gradient at entry bar
    entry_gradient = 0
    for d in data:
        if d['bar'] == entry_bar_index:
            entry_price = d['close']
            break
    
    # Get gradient at entry bar
    for gh in gradient_history:
        if gh['bar'] == entry_bar_index:
            entry_gradient = gh['fe_grad']
            break
    
    # Check if gradient meets minimum requirement for SHORT (negative gradient <= -threshold)
    if entry_gradient > -MIN_FAST_EMA_GRADIENT:
        short_skipped += 1
        continue  # Skip this trade
    
    ret = calculate_return(entry_price, r['end_price'], 'SHORT')
    is_win = ret > 0
    if is_win:
        short_wins += 1
    short_total += 1
    
    print(f"\n#{short_total:3d} | Bars {r['start_bar']:4d} -> {r['end_bar']:4d} ({duration:3d} bars)")
    print(f"      | Time: {r['start_time']} -> {r['end_time']}")
    print(f"      | Entry bar {entry_bar_index} (delay={ENTRY_BAR_DELAY}, grad={entry_gradient:+.3f}): {entry_price:.2f} -> Exit: {r['end_price']:.2f}")
    print(f"      | Return: {ret:+.3f}% {'WIN' if is_win else 'LOSS'}")

short_win_rate = (short_wins / short_total * 100) if short_total > 0 else 0
print(f"\nSHORT Summary: {short_total} ranges (>={MIN_BARS_FOR_TRADE} bars), {short_wins} wins, {short_win_rate:.1f}% win rate")
print(f"   Excluded: {len(short_ranges) - len(short_ranges_filtered)} ranges with <{MIN_BARS_FOR_TRADE} bars")
print(f"   Skipped (weak gradient): {short_skipped} ranges")

# Overall summary
print("\n" + "=" * 80)
print("OVERALL SUMMARY")
print("=" * 80)
print(f"\nTotal LONG ranges:  {long_total} (of {len(long_ranges)} detected, {long_skipped} skipped)")
print(f"Total SHORT ranges: {short_total} (of {len(short_ranges)} detected, {short_skipped} skipped)")
print(f"Total valid ranges: {long_total + short_total} (minimum {MIN_BARS_FOR_TRADE} bars, gradient >= {MIN_FAST_EMA_GRADIENT})")
print(f"Total excluded:     {(len(long_ranges) - long_total) + (len(short_ranges) - short_total)} ranges")
print(f"Total skipped:      {long_skipped + short_skipped} ranges (weak gradient)")
print(f"\nLONG win rate:  {long_win_rate:.1f}%")
print(f"SHORT win rate: {short_win_rate:.1f}%")
overall_total = long_total + short_total
if overall_total > 0:
    print(f"Overall win rate: {((long_wins + short_wins) / overall_total * 100):.1f}%")
else:
    print("Overall win rate: N/A (no ranges found)")

print("\nDETAILED REPORT COMPLETE\n")
