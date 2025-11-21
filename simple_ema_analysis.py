"""
Simple EMA Gradient Analysis - No external dependencies
Analyzes when both EMAs are rising/falling with price positioned accordingly
"""

import csv
import os
from collections import defaultdict

LOG_FOLDER = r"c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\indicators_log"
MIN_GRADIENT = 0.0001

print("=" * 80)
print("EMA GRADIENT SIGNAL ANALYSIS (Simple Version)")
print("=" * 80)
print()

# Find the main CSV file
csv_files = []
for f in os.listdir(LOG_FOLDER):
    if f.endswith('.csv') and 'signal' not in f.lower():
        csv_files.append(os.path.join(LOG_FOLDER, f))

if not csv_files:
    print("❌ No CSV files found")
    exit(1)

# Use the most recent
latest = max(csv_files, key=os.path.getmtime)
print(f"📁 Analyzing: {os.path.basename(latest)}")
print()

# Read and analyze
rows = []
with open(latest, 'r', encoding='utf-8') as f:
    reader = csv.DictReader(f)
    for row in reader:
        try:
            rows.append({
                'bar': row.get('bar_index', ''),
                'time': row.get('ts_local', '')[:19],
                'close': float(row.get('close', 0)),
                'ema10': float(row.get('ema10', 0)),
                'ema20': float(row.get('ema20', 0))
            })
        except (ValueError, KeyError):
            continue

print(f"✅ Loaded {len(rows):,} bars with valid data")
print()

# Calculate gradients and signals
signals = []
prev_ema10 = None
prev_ema20 = None

for i, row in enumerate(rows):
    if prev_ema10 is None:
        prev_ema10 = row['ema10']
        prev_ema20 = row['ema20']
        signals.append('FLAT')
        continue
    
    # Calculate gradients
    fe_grad = row['ema10'] - prev_ema10
    se_grad = row['ema20'] - prev_ema20
    
    # Check conditions
    fe_rising = fe_grad > MIN_GRADIENT
    fe_falling = fe_grad < -MIN_GRADIENT
    se_rising = se_grad > MIN_GRADIENT
    se_falling = se_grad < -MIN_GRADIENT
    
    price_above_both = row['close'] > row['ema10'] and row['close'] > row['ema20']
    price_below_both = row['close'] < row['ema10'] and row['close'] < row['ema20']
    
    # Determine signal
    if fe_rising and se_rising and price_above_both:
        signal = 'LONG'
    elif fe_falling and se_falling and price_below_both:
        signal = 'SHORT'
    else:
        signal = 'FLAT'
    
    signals.append(signal)
    
    # Store for next iteration
    prev_ema10 = row['ema10']
    prev_ema20 = row['ema20']

# Count signals
long_count = signals.count('LONG')
short_count = signals.count('SHORT')
flat_count = signals.count('FLAT')
total = len(signals)

print("=" * 80)
print("SIGNAL DISTRIBUTION")
print("=" * 80)
print(f"LONG bars:  {long_count:,} ({long_count/total*100:.1f}%)")
print(f"SHORT bars: {short_count:,} ({short_count/total*100:.1f}%)")
print(f"FLAT bars:  {flat_count:,} ({flat_count/total*100:.1f}%)")

# Count entry signals (signal changes)
long_entries = 0
short_entries = 0
prev_signal = 'FLAT'

for sig in signals:
    if sig == 'LONG' and prev_signal != 'LONG':
        long_entries += 1
    elif sig == 'SHORT' and prev_signal != 'SHORT':
        short_entries += 1
    prev_signal = sig

print(f"\nEntry Signals:")
print(f"  LONG entries:  {long_entries}")
print(f"  SHORT entries: {short_entries}")

# Calculate forward returns
print("\n" + "=" * 80)
print("FORWARD RETURN ANALYSIS")
print("=" * 80)

for n_bars in [1, 5, 10, 20]:
    long_returns = []
    short_returns = []
    
    for i in range(len(rows) - n_bars):
        if i >= len(signals):
            break
            
        current_price = rows[i]['close']
        future_price = rows[i + n_bars]['close']
        ret = ((future_price - current_price) / current_price) * 100
        
        if signals[i] == 'LONG':
            long_returns.append(ret)
        elif signals[i] == 'SHORT':
            short_returns.append(ret)
    
    if long_returns:
        avg_long = sum(long_returns) / len(long_returns)
        correct_long = sum(1 for r in long_returns if r > 0) / len(long_returns) * 100
        print(f"\nLONG Performance ({n_bars}-bar forward):")
        print(f"  Average return: {avg_long:+.3f}%")
        print(f"  Directional accuracy: {correct_long:.1f}%")
    
    if short_returns:
        avg_short = sum(short_returns) / len(short_returns)
        correct_short = sum(1 for r in short_returns if r < 0) / len(short_returns) * 100
        print(f"\nSHORT Performance ({n_bars}-bar forward):")
        print(f"  Average return: {avg_short:+.3f}%")
        print(f"  Directional accuracy: {correct_short:.1f}%")

# Show last 10 signal changes
print("\n" + "=" * 80)
print("LAST 10 SIGNAL CHANGES")
print("=" * 80)

transitions = []
prev_sig = signals[0] if signals else 'FLAT'
for i in range(1, len(signals)):
    if signals[i] != prev_sig:
        transitions.append((i, prev_sig, signals[i]))
        prev_sig = signals[i]

for i, prev, curr in transitions[-10:]:
    if i < len(rows):
        row = rows[i]
        print(f"{row['bar']:<8} {row['time']:<20} {prev:<6} → {curr:<6} @ {row['close']:.2f}")

print("\n" + "=" * 80)
print("CURRENT STATE ANALYSIS")
print("=" * 80)
print("\nWhat signal is active at each bar (present condition, not prediction):")
print(f"  • When LONG: Both EMAs are rising AND price is above both")
print(f"  • When SHORT: Both EMAs are falling AND price is below both")
print(f"  • When FLAT: Mixed conditions or price between EMAs")
print()
print(f"Market shows LONG conditions {long_count/total*100:.1f}% of the time")
print(f"Market shows SHORT conditions {short_count/total*100:.1f}% of the time")
print(f"Market shows FLAT conditions {flat_count/total*100:.1f}% of the time")
print()
print("=" * 80)
print("INTERPRETATION")
print("=" * 80)
print("This tells you WHEN to be in a position based on CURRENT conditions:")
print("  • LONG signal = Enter/Stay LONG (trend is up, price above EMAs)")
print("  • SHORT signal = Enter/Stay SHORT (trend is down, price below EMAs)")
print("  • FLAT signal = Exit/Stay FLAT (no clear trend or price mixed)")
print()
print("Forward returns show if staying in that signal type is profitable.")
print()
print("✅ ANALYSIS COMPLETE")
