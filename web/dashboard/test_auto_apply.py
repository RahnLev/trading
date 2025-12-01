#!/usr/bin/env python
"""
Test script to simulate consecutive weak gradient bars and verify auto-apply triggers.
Posts synthetic diags to trigger MinEntryFastGradientAbs auto-lowering after 3 streaks.
"""
import requests
import time
import json

BASE_URL = 'http://127.0.0.1:5001'

def post_diag(bar_idx, fast_grad, rsi=50, adx=25):
    payload = {
        'fastGrad': fast_grad,
        'rsi': rsi,
        'adx': adx,
        'fastEMA': 100.0,
        'slowEMA': 101.0,
        'close': 102.0,
        'barIndex': bar_idx,
        'time': time.strftime('%Y-%m-%d %H:%M:%S')
    }
    r = requests.post(f'{BASE_URL}/diag', json=payload)
    print(f"[POST] bar {bar_idx} fastGrad={fast_grad:.3f} -> {r.status_code}")
    return r.status_code == 200

def get_autosuggest():
    r = requests.get(f'{BASE_URL}/autosuggest')
    if r.status_code == 200:
        data = r.json()
        return data
    return {}

def get_overrides():
    r = requests.get(f'{BASE_URL}/overrides')
    if r.status_code == 200:
        return r.json()
    return {}

def main():
    print("=== Auto-Apply Streak Test ===")
    
    # Check initial state
    initial = get_overrides()
    print(f"\n[INIT] Overrides: {initial.get('overrides', {})}")
    print(f"[INIT] Effective MinEntryFastGradientAbs: {initial.get('effectiveParams', {}).get('MinEntryFastGradientAbs', 'N/A')}")
    
    # Post 3 consecutive weak gradient bars (below default 0.30 threshold)
    print("\n[TEST] Posting 3 consecutive weak gradient bars (fastGrad ~0.10, below 0.30)...")
    for i in range(3):
        post_diag(9000 + i, 0.10 + i * 0.01)
        time.sleep(0.5)
    
    # Wait a moment for processing
    time.sleep(2)
    
    # Check autosuggest
    auto = get_autosuggest()
    print(f"\n[AUTO] weakGradConsec: {auto.get('weakGradConsec', 0)}")
    print(f"[AUTO] property_streaks MinEntryFastGradientAbs: {auto.get('autoApply', {}).get('streaks', {}).get('MinEntryFastGradientAbs', 0)}")
    print(f"[AUTO] Auto-apply enabled: {auto.get('autoApply', {}).get('enabled', False)}")
    
    recent_events = auto.get('autoApply', {}).get('recentEvents', [])
    if recent_events:
        print(f"\n[AUTO] Recent auto-apply events:")
        for ev in recent_events:
            print(f"  - {ev.get('property')}: {ev.get('oldValue')} -> {ev.get('newValue')} (streak {ev.get('streakCount')})")
    else:
        print("\n[AUTO] No recent auto-apply events yet.")
    
    # Check final overrides
    final = get_overrides()
    print(f"\n[FINAL] Overrides: {final.get('overrides', {})}")
    print(f"[FINAL] Effective MinEntryFastGradientAbs: {final.get('effectiveParams', {}).get('MinEntryFastGradientAbs', 'N/A')}")
    
    # Verify change
    init_val = initial.get('effectiveParams', {}).get('MinEntryFastGradientAbs', 0.30)
    final_val = final.get('effectiveParams', {}).get('MinEntryFastGradientAbs', 0.30)
    if final_val < init_val:
        print(f"\n✅ SUCCESS: MinEntryFastGradientAbs auto-lowered from {init_val} to {final_val}")
    else:
        print(f"\n⚠️ No change detected. Initial: {init_val}, Final: {final_val}")
        print("   Check: AUTO_APPLY_ENABLED=True, streak >= 3, cooldown satisfied")
    
    # Post RSI below floor streak test
    print("\n[TEST] Posting 3 consecutive RSI below floor bars (RSI ~44, floor 50)...")
    for i in range(3):
        post_diag(9100 + i, 0.15, rsi=44.0 + i * 0.1)
        time.sleep(0.5)
    
    time.sleep(2)
    auto2 = get_autosuggest()
    print(f"\n[AUTO] rsiBelowConsec: {auto2.get('rsiBelowConsec', 0)}")
    print(f"[AUTO] property_streaks RsiEntryFloor: {auto2.get('autoApply', {}).get('streaks', {}).get('RsiEntryFloor', 0)}")
    
    recent_events2 = auto2.get('autoApply', {}).get('recentEvents', [])
    rsi_event = [e for e in recent_events2 if e.get('property') == 'RsiEntryFloor']
    if rsi_event:
        print(f"\n✅ RSI floor auto-apply triggered: {rsi_event[0]}")
    else:
        print("\n⚠️ No RSI floor auto-apply event yet (may need more bars or cooldown wait)")
    
    print("\n=== Test Complete ===")

if __name__ == '__main__':
    main()
