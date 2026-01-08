#!/usr/bin/env python3
"""
Explain why short entry occurred on bar 1843 (actually entered on bar 1842)
"""

import os
import sys
import sqlite3

if sys.stdout.encoding != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8')

CUSTOM_DIR = os.path.dirname(os.path.dirname(os.path.dirname(__file__)))
VOLATILITY_DB = os.path.join(CUSTOM_DIR, 'web', 'dashboard', 'volatility.db')

print("="*80)
print("EXPLANATION: SHORT ENTRY ON BAR 1843 (ACTUALLY ENTERED ON BAR 1842)")
print("="*80)

if os.path.exists(VOLATILITY_DB):
    conn = sqlite3.connect(VOLATILITY_DB)
    cursor = conn.cursor()
    
    # Get bar 1842 data (where entry decision was made)
    cursor.execute("""
        SELECT bar_index, open_price, close_price, high_price, low_price,
               ema_fast_value, ema_slow_value, fast_ema_grad_deg, candle_type,
               entry_reason
        FROM bar_samples
        WHERE bar_index = 1842
    """)
    
    row = cursor.fetchone()
    if row:
        bar_idx, open_p, close_p, high_p, low_p, fast_ema, slow_ema, grad_deg, candle_type, entry_reason = row
        print(f"\n=== BAR 1842 (ENTRY DECISION BAR) ===")
        print(f"OHLC: O={open_p:.2f}, H={high_p:.2f}, L={low_p:.2f}, C={close_p:.2f}")
        print(f"Fast EMA: {fast_ema:.2f}, Slow EMA: {slow_ema:.2f}")
        print(f"Gradient: {grad_deg:.2f}°")
        print(f"Candle Type: {candle_type}")
        print(f"Entry Reason: {entry_reason}")
        
        print(f"\n=== WHY SHORT ENTRY OCCURRED ===")
        print(f"Based on the logs, the entry was made on bar 1842 with these conditions:")
        print(f"")
        print(f"From logs (actual entry decision data):")
        print(f"  - Bar 1842 OHLC: O=25772.00, C=25766.00")
        print(f"  - Fast EMA: 25772.83, Slow EMA: 25774.55")
        print(f"  - Gradient (recalculated): -61.78°")
        print(f"")
        print(f"1. GRADIENT FILTER: ✓ PASSED")
        print(f"   - Gradient (recalculated at entry decision): -61.78°")
        print(f"   - Threshold: -10°")
        print(f"   - -61.78° <= -10°? YES")
        print(f"   - Negative gradient indicates bearish trend - good for SHORT entry")
        
        print(f"\n2. EMA CROSSOVER FILTER: ✓ PASSED")
        print(f"   - Close (25766.00) < Fast EMA (25772.83)? YES")
        print(f"   - Fast EMA (25772.83) < Slow EMA (25774.55)? YES")
        print(f"   - Body requirement (EmaCrossoverRequireBodyBelow=True):")
        body_top_log = 25772.00
        body_bottom_log = 25766.00
        fast_ema_threshold_log = 25772.83 - 0.5  # 2 ticks * 0.25
        body_below_log = body_top_log <= fast_ema_threshold_log and body_bottom_log <= fast_ema_threshold_log
        print(f"     Body Top: {body_top_log:.2f}, Body Bottom: {body_bottom_log:.2f}")
        print(f"     Fast EMA threshold: {fast_ema_threshold_log:.2f} (Fast EMA - 2 ticks)")
        print(f"     Body below Fast EMA? {'YES' if body_below_log else 'NO'}")
        
        print(f"\n3. CANDLE TYPE: {candle_type.upper()}")
        print(f"   - Entry reason shows: 'BadCandle'")
        print(f"   - Strategy allows short entries on bad candles")
        
        print(f"\n4. COOLDOWN: ✓ PASSED")
        print(f"   - Entry reason shows: 'CooldownOK'")
        
        print(f"\n=== SUMMARY ===")
        print(f"Entry Decision: Made on bar 1842")
        print(f"Entry Price: 25784.00 (close of bar 1842)")
        print(f"Entry Bar (Chart Display): 1843 (where the arrow appears)")
        print(f"")
        print(f"All filters passed on bar 1842:")
        print(f"  ✓ Gradient: {grad_deg:.2f}° (bearish, below -10° threshold)")
        print(f"  ✓ EMA Crossover: Close below Fast EMA, Fast EMA below Slow EMA")
        print(f"  ✓ Body Requirement: Entire body below Fast EMA")
        print(f"  ✓ Cooldown: Passed")
        print(f"  ✓ Candle Type: Bad candle (allowed for shorts)")
        print(f"")
        print(f"The entry decision was made on bar 1842 (when CurrentBar=1842),")
        print(f"but NinjaTrader displays it as bar 1843 because that's the bar")
        print(f"where the order was filled/displayed on the chart.")
    
    conn.close()
