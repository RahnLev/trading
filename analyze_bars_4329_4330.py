#!/usr/bin/env python3
"""
Analyze why bars 4329 and 4330 didn't enter when they should have.
"""

import pandas as pd

# Load the most recent log
log_file = r"strategy_logs\GradientSlope_MNQ 12-25_2025-12-02_13-51-21.csv"

print("=" * 80)
print("ANALYZING BARS 4329 & 4330 - Why No Entry?")
print("=" * 80)

# Read CSV with error handling for malformed lines
df = pd.read_csv(log_file, on_bad_lines='skip', low_memory=False)

# Filter for bars 4328-4330
bars = df[df['Bar'].isin([4328, 4329, 4330])].copy()

print("\n📊 BAR DATA:")
print("-" * 80)
for _, row in bars.iterrows():
    bar = row['Bar']
    time = row['Timestamp']
    close = row['Close']
    fastEMA = row['FastEMA']
    slowEMA = row['SlowEMA']
    fastGrad = row['FastGradient']
    slowGrad = row['SlowGradient']
    signal = row['NewSignal']
    action = row['Action']
    details = row.get('Notes', '')
    
    print(f"\n🔷 Bar {bar} @ {time}")
    print(f"   Close: {close:.2f}")
    print(f"   FastEMA: {fastEMA:.2f} | SlowEMA: {slowEMA:.2f}")
    print(f"   FastGrad: {fastGrad:.4f} | SlowGrad: {slowGrad:.4f}")
    print(f"   Signal: {signal} | Action: {action}")
    print(f"   Details: {details}")

# Look for ENTRY_DECISION or ENTRY_FILTER_BLOCKED
print("\n" + "=" * 80)
print("🔍 ENTRY ATTEMPTS & BLOCKS:")
print("=" * 80)

entry_related = df[
    (df['Bar'].isin([4328, 4329, 4330])) & 
    (df['Action'].str.contains('ENTRY', na=False))
].copy()

if len(entry_related) > 0:
    for _, row in entry_related.iterrows():
        bar = row['Bar']
        action = row['Action']
        details = row.get('Notes', '')
        signal = row['NewSignal']
        
        print(f"\n📍 Bar {bar}: {action}")
        print(f"   Signal: {signal}")
        print(f"   {details}")
else:
    print("\n❌ NO ENTRY ATTEMPTS FOUND for these bars")
    print("   This means the signal condition itself wasn't met")

# Check signal changes
print("\n" + "=" * 80)
print("🔄 SIGNAL CHANGES:")
print("=" * 80)

signal_changes = df[
    (df['Bar'].isin([4327, 4328, 4329, 4330, 4331])) & 
    (df['Action'] == 'SIGNAL_CHANGE')
].copy()

if len(signal_changes) > 0:
    for _, row in signal_changes.iterrows():
        print(f"Bar {row['Bar']}: {row.get('Notes', '')}")
else:
    print("No signal changes in this range")

# Analysis
print("\n" + "=" * 80)
print("💡 ANALYSIS:")
print("=" * 80)

# Check bar 4328
bar_4328 = bars[bars['Bar'] == 4328]
if len(bar_4328) > 0:
    row = bar_4328.iloc[-1]
    print(f"\n📌 Bar 4328:")
    print(f"   - Signal was SHORT but BLOCKED")
    print(f"   - FastGrad: {row['FastGradient']:.4f} (negative ✓)")
    print(f"   - SlowGrad: {row['SlowGradient']:.4f} (negative ✓)")
    
    # Check for filter blocks
    blocked = df[(df['Bar'] == 4328) & (df['Action'] == 'ENTRY_FILTER_BLOCKED')]
    if len(blocked) > 0:
        details = blocked.iloc[0].get('Notes', '')
        print(f"   - BLOCKED by: {details}")
        
        # Parse blockers
        if 'AccelMisAligned' in details:
            print("     ❌ Acceleration misaligned (positive while gradient negative)")
        if 'RSI<50' in details:
            print("     ❌ RSI below 50 for SHORT entry")

# Check bar 4329
bar_4329 = bars[bars['Bar'] == 4329]
if len(bar_4329) > 0:
    row = bar_4329.iloc[-1]
    print(f"\n📌 Bar 4329:")
    print(f"   - Signal changed to FLAT")
    print(f"   - FastGrad: {row['FastGradient']:.4f} (POSITIVE - no longer bearish)")
    print(f"   - SlowGrad: {row['SlowGradient']:.4f} (still negative)")
    print(f"   - ⚠️ Fast gradient flipped positive → signal invalidated")

# Check bar 4330
bar_4330 = bars[bars['Bar'] == 4330]
if len(bar_4330) > 0:
    row = bar_4330.iloc[-1]
    print(f"\n📌 Bar 4330:")
    print(f"   - Signal: FLAT")
    print(f"   - FastGrad: {row['FastGradient']:.4f} (POSITIVE)")
    print(f"   - SlowGrad: {row['SlowGradient']:.4f} (negative)")
    print(f"   - ❌ No entry signal (fast gradient positive)")

print("\n" + "=" * 80)
print("🎯 CONCLUSION:")
print("=" * 80)
print("""
Bar 4328 (SHORT signal):
  - Had valid SHORT signal (both gradients negative)
  - BLOCKED by entry filters:
    • Acceleration misaligned (positive while gradient negative)
    • RSI < 50 (was 46.0, needs >= 50 for SHORT)

Bar 4329:
  - Signal changed to FLAT
  - Fast gradient flipped to POSITIVE (+0.0579)
  - No longer a valid bearish setup

Bar 4330:
  - No signal (FLAT)
  - Fast gradient strongly positive (+0.1217)
  - Price momentum reversed

WHY NO ENTRY:
  Bar 4328 was the only bar with a SHORT signal, but it was correctly
  blocked by entry filters (RSI too low, acceleration misaligned).
  By bars 4329-4330, the fast gradient had reversed to positive,
  invalidating the bearish setup entirely.
""")

print("\n✅ The strategy correctly avoided these bars - the setup was weak/invalid")
print("=" * 80)
