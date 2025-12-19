#!/usr/bin/env python3
"""Analyze bar 2553 trend detection issue"""

import csv

csv_file = r"c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\strategy_logs\BarsOnTheFlow_MNQ 12-25_2025-12-12_22-06-19-286.csv"

with open(csv_file, 'r') as f:
    reader = csv.DictReader(f)
    rows = list(reader)

# Find bars around 2553
target_bars = range(2545, 2555)
relevant_rows = [r for r in rows if int(r['bar']) in target_bars]

print("Bar Analysis for bars 2545-2554:")
print("="*120)
print(f"{'Bar':<6} {'Action':<10} {'CandleType':<12} {'TrendUp':<10} {'TrendDown':<10} {'DecisionBar':<12} {'Pattern':<10}")
print("="*120)

for row in relevant_rows:
    bar = row['bar']
    action = row['action']
    candle = row['candleType']
    trend_up = row['trendUpAtDecision']
    trend_down = row['trendDownAtDecision']
    decision_bar = row['decisionBarIndex']
    pattern = row.get('barPattern', '')
    
    print(f"{bar:<6} {action:<10} {candle:<12} {trend_up:<10} {trend_down:<10} {decision_bar:<12} {pattern:<10}")

# Count consecutive good/bad bars leading to bar 2553
print("\n" + "="*120)
print("Consecutive bar analysis leading to bar 2553:")
print("="*120)

bars_before_2553 = [r for r in rows if int(r['bar']) <= 2552 and r['action'] == 'BAR']
bars_before_2553.sort(key=lambda x: int(x['bar']), reverse=True)

print("\nLast 6 bars before 2553 (most recent first):")
for i, row in enumerate(bars_before_2553[:6]):
    bar = row['bar']
    candle = row['candleType']
    trend_up = row['trendUpAtDecision']
    trend_down = row['trendDownAtDecision']
    print(f"  {i}: Bar {bar} - {candle} (trendUp={trend_up}, trendDown={trend_down})")

# Calculate what trendUp should be for bar 2553
print("\n" + "="*120)
print("Expected trend calculation for bar 2553:")
print("="*120)
print("When bar 2553 starts, the previous bar (2552) is added to the queue.")
print("For MinConsecutiveBars=4, we check the last 4 bars:")

last_4_bars = bars_before_2553[:4]
good_count = sum(1 for r in last_4_bars if r['candleType'].lower() == 'good')
bad_count = sum(1 for r in last_4_bars if r['candleType'].lower() == 'bad')

print(f"\nBars in queue: {[r['bar'] + '=' + r['candleType'] for r in last_4_bars]}")
print(f"Good bars: {good_count}")
print(f"Bad bars: {bad_count}")
print(f"\nFor 4-bar mode: trendUp = (goodCount == 4) OR (goodCount == 3 AND netPnL > 0)")
print(f"Expected trendUp: {good_count == 4 or (good_count == 3)}")
print(f"Actual trendUp on bar 2553 BAR snapshot: {[r for r in relevant_rows if r['bar'] == '2553' and r['action'] == 'BAR'][0]['trendUpAtDecision']}")
