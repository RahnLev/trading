#!/usr/bin/env python3
"""Check if break-even caused the FullCandle exit on bar 1263"""
entry_price = 25760.5
fast_ema = 25756.7516
high = 25761.25
low = 25748.0

print("=" * 80)
print("BREAK-EVEN ANALYSIS FOR BAR 1263")
print("=" * 80)
print(f"Entry Price: {entry_price}")
print(f"Fast EMA: {fast_ema}")
print(f"Bar 1263 - High: {high}, Low: {low}")
print()

print("FullCandle Check with Fast EMA only:")
print(f"  High < FastEMA: {high < fast_ema} ({high} < {fast_ema})")
print(f"  Low < FastEMA: {low < fast_ema} ({low} < {fast_ema})")
both_below_ema = high < fast_ema and low < fast_ema
print(f"  Both below: {both_below_ema} -> Should NOT trigger")
print()

print("If Break-Even is activated:")
for offset in [0, 0.5, 1.0, 1.5, 2.0]:
    break_even_floor = entry_price + offset
    effective_stop = max(fast_ema, break_even_floor)
    high_below = high < effective_stop
    low_below = low < effective_stop
    both_below = high_below and low_below
    
    print(f"  Offset {offset}:")
    print(f"    BreakEven Floor: {break_even_floor}")
    print(f"    Effective Stop: max({fast_ema}, {break_even_floor}) = {effective_stop}")
    print(f"    High < Effective: {high_below} ({high} < {effective_stop})")
    print(f"    Low < Effective: {low_below} ({low} < {effective_stop})")
    trigger_text = "WOULD TRIGGER!" if both_below else ""
    print(f"    FullCandle trigger: {both_below} {trigger_text}")
    print()
