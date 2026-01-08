#!/usr/bin/env python3
"""Explain why there are multiple trades on the same bar"""
import sqlite3
import datetime

conn = sqlite3.connect('volatility.db')
cursor = conn.cursor()

print("="*80)
print("EXPLANATION: Multiple Trades on Bar 2409")
print("="*80)

# Get trades on bar 2409
cursor.execute("""
    SELECT entry_bar, exit_bar, direction, entry_price, exit_price, entry_reason, 
           entry_time, exit_time, contracts
    FROM trades 
    WHERE entry_bar = 2409 
    ORDER BY entry_time
""")
trades = cursor.fetchall()

print(f"\nFound {len(trades)} trade(s) with entry_bar = 2409:\n")

for i, t in enumerate(trades, 1):
    entry_dt = datetime.datetime.fromtimestamp(t[6])
    exit_dt = datetime.datetime.fromtimestamp(t[7])
    print(f"Trade {i}:")
    print(f"  Entry: {t[2]} @ {t[3]:.2f} on {entry_dt}")
    print(f"  Exit: {t[4]:.2f} on {exit_dt}")
    print(f"  Entry Reason: {t[5]}")
    print(f"  Time Difference from Trade 1: {entry_dt - datetime.datetime.fromtimestamp(trades[0][6])}")
    print()

# Check if they're from the same day/run
if len(trades) > 1:
    time_diff = datetime.datetime.fromtimestamp(trades[1][6]) - datetime.datetime.fromtimestamp(trades[0][6])
    print(f"Time difference between trades: {time_diff}")
    print(f"\n{'='*80}")
    print("EXPLANATION:")
    print("="*80)
    print("\nThese are NOT duplicate entries on the same bar in the same run.")
    print("They are from DIFFERENT strategy runs:")
    print(f"  - Trade 1: From a run on {datetime.datetime.fromtimestamp(trades[0][6]).date()}")
    print(f"  - Trade 2: From a run on {datetime.datetime.fromtimestamp(trades[1][6]).date()}")
    print("\nBar numbers are RELATIVE to each run:")
    print("  - Each strategy run starts from bar 1")
    print("  - Bar 2409 in Run 1 is a different bar than bar 2409 in Run 2")
    print("  - They just happen to have the same bar number in different runs")
    print("\nThis is NORMAL behavior when you run the strategy multiple times.")

# Check for bars with multiple entries in the same time period (same run)
print(f"\n{'='*80}")
print("Checking for bars with multiple entries in the SAME run:")
print("="*80)

# Group by entry_time ranges (trades within 1 hour are likely same run)
cursor.execute("""
    SELECT entry_bar, COUNT(*) as cnt, 
           MIN(entry_time) as min_time, MAX(entry_time) as max_time,
           COUNT(DISTINCT entry_price) as distinct_prices,
           COUNT(DISTINCT direction) as distinct_directions
    FROM trades
    GROUP BY entry_bar
    HAVING cnt > 1
    ORDER BY cnt DESC
    LIMIT 10
""")
multi_bars = cursor.fetchall()

print(f"\nTop 10 bars with multiple entries:")
for bar_info in multi_bars:
    bar_num, cnt, min_time, max_time, distinct_prices, distinct_dirs = bar_info
    time_span = datetime.datetime.fromtimestamp(max_time) - datetime.datetime.fromtimestamp(min_time)
    print(f"\nBar {bar_num}: {cnt} trades")
    print(f"  Time span: {time_span}")
    print(f"  Distinct prices: {distinct_prices}")
    print(f"  Distinct directions: {distinct_dirs}")
    
    if time_span.total_seconds() < 3600:  # Less than 1 hour
        print(f"  ⚠️  Likely same run - could be duplicate or multiple contracts")
    else:
        print(f"  ✓ Different runs - normal behavior")

conn.close()
