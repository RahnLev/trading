#!/usr/bin/env python3
"""
Comprehensive diagnostic checker for why no orders are placed.
Queries the dashboard API and database to analyze entry blockers.
"""

import sqlite3
import datetime
import requests
import json

DB_PATH = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard\dashboard.db'
API_BASE = "http://127.0.0.1:5001"

def main():
    print("\n" + "="*80)
    print("TRADING STRATEGY DIAGNOSTIC REPORT")
    print("="*80 + "\n")
    
    # 1. Check if server is running
    try:
        resp = requests.get(f"{API_BASE}/diags?since=0", timeout=2)
        resp.raise_for_status()
        diags = resp.json()
        print(f"✓ Server is running - {len(diags)} diagnostics received\n")
    except Exception as e:
        print(f"✗ Server not accessible: {e}\n")
        print("  Action: Start the server with: python web/dashboard/server.py")
        return
    
    if len(diags) == 0:
        print("✗ No diagnostics in database")
        print("  Possible causes:")
        print("    - Strategy not running in NinjaTrader")
        print("    - Strategy 'StreamBarDiagnostics' parameter is False")
        print("    - Strategy not processing new bars (market closed?)")
        return
    
    # 2. Analyze recent diagnostics
    print(f"=== RECENT MARKET CONDITIONS (Last 5 Bars) ===\n")
    recent = diags[-5:] if len(diags) >= 5 else diags
    
    for d in recent:
        bar_time = d.get('time', 'N/A')
        print(f"Bar {d['barIndex']} at {bar_time}")
        print(f"  FastGrad: {d['fastGrad']:+.3f} | RSI: {d.get('rsi', 0):.1f} | ADX: {d.get('adx', 0):.1f}")
        print(f"  GradStab: {d.get('gradStab', 0):.3f} | Bandwidth: {d.get('bandwidth', 0):.4f}")
        print(f"  Signal: {d.get('signal', 'FLAT')} | Trend: {d.get('trendSide', 'N/A')}")
        
        blockers_long = d.get('blockersLong', [])
        blockers_short = d.get('blockersShort', [])
        
        if blockers_long or blockers_short:
            print(f"  🚫 LONG Blockers:  {', '.join(blockers_long) if blockers_long else 'None'}")
            print(f"  🚫 SHORT Blockers: {', '.join(blockers_short) if blockers_short else 'None'}")
        else:
            print(f"  ✓ No blockers detected")
        print()
    
    # 3. Blocker frequency analysis
    print(f"\n=== BLOCKER FREQUENCY ANALYSIS (All {len(diags)} Bars) ===\n")
    
    long_blockers = {}
    short_blockers = {}
    bars_with_no_long_blockers = 0
    bars_with_no_short_blockers = 0
    
    for d in diags:
        blockers_l = d.get('blockersLong', [])
        blockers_s = d.get('blockersShort', [])
        
        if not blockers_l:
            bars_with_no_long_blockers += 1
        else:
            for b in blockers_l:
                long_blockers[b] = long_blockers.get(b, 0) + 1
        
        if not blockers_s:
            bars_with_no_short_blockers += 1
        else:
            for b in blockers_s:
                short_blockers[b] = short_blockers.get(b, 0) + 1
    
    print(f"LONG Entry Analysis:")
    print(f"  Bars with NO blockers: {bars_with_no_long_blockers} / {len(diags)} ({100*bars_with_no_long_blockers/len(diags):.1f}%)")
    if long_blockers:
        print(f"  Most common blockers:")
        for blocker, count in sorted(long_blockers.items(), key=lambda x: -x[1])[:5]:
            pct = 100 * count / len(diags)
            print(f"    - {blocker}: {count} bars ({pct:.1f}%)")
    
    print(f"\nSHORT Entry Analysis:")
    print(f"  Bars with NO blockers: {bars_with_no_short_blockers} / {len(diags)} ({100*bars_with_no_short_blockers/len(diags):.1f}%)")
    if short_blockers:
        print(f"  Most common blockers:")
        for blocker, count in sorted(short_blockers.items(), key=lambda x: -x[1])[:5]:
            pct = 100 * count / len(diags)
            print(f"    - {blocker}: {count} bars ({pct:.1f}%)")
    
    # 4. Check current thresholds
    print(f"\n=== CURRENT THRESHOLDS (from overrides) ===\n")
    try:
        resp_overrides = requests.get(f"{API_BASE}/overrides")
        resp_overrides.raise_for_status()
        overrides = resp_overrides.json()
        
        if overrides:
            for key, value in overrides.items():
                print(f"  {key}: {value}")
        else:
            print("  No overrides active - using strategy defaults")
            print("  Default thresholds:")
            print("    MinEntryFastGradientAbs: 0.50")
            print("    MinAdxForEntry: 18.0")
            print("    MaxGradientStabilityForEntry: 1.46")
            print("    MaxBandwidthForEntry: 0.100")
    except Exception as e:
        print(f"  Error fetching overrides: {e}")
    
    # 5. Check auto-suggest recommendations
    print(f"\n=== AUTO-SUGGEST RECOMMENDATIONS ===\n")
    try:
        resp_suggest = requests.get(f"{API_BASE}/autosuggest")
        resp_suggest.raise_for_status()
        suggestions = resp_suggest.json()
        
        if suggestions.get('enabled'):
            print(f"  Auto-apply: ENABLED")
            print(f"  Suggestions:")
            for prop, data in suggestions.get('properties', {}).items():
                rec = data.get('recommend')
                streak = data.get('streak', 0)
                print(f"    {prop}: {rec} (streak: {streak})")
        else:
            print(f"  Auto-apply: DISABLED")
    except Exception as e:
        print(f"  Error fetching suggestions: {e}")
    
    # 6. Summary and recommendations
    print(f"\n=== DIAGNOSIS SUMMARY ===\n")
    
    latest = diags[-1]
    latest_signal = latest.get('signal', 'FLAT')
    latest_blockers_long = latest.get('blockersLong', [])
    latest_blockers_short = latest.get('blockersShort', [])
    
    if latest_signal == 'FLAT':
        print("  ⚠ Current signal: FLAT")
        print("    → Strategy is not detecting an entry setup")
        print("    → FastGrad may be too weak or gradients not aligned")
    else:
        print(f"  ℹ Current signal: {latest_signal}")
        if (latest_signal == 'LONG' and latest_blockers_long) or (latest_signal == 'SHORT' and latest_blockers_short):
            blockers = latest_blockers_long if latest_signal == 'LONG' else latest_blockers_short
            print(f"    → Entry blocked by: {', '.join(blockers)}")
            print(f"    → Consider adjusting these thresholds or enabling auto-apply")
        else:
            print(f"    → No current blockers detected!")
            print(f"    → Check if entry delay has been met (entryBarDelay parameter)")
            print(f"    → Check if strategy is in exit cooldown")
            print(f"    → Review strategy logs for ENTRY_CANCELLED messages")
    
    print(f"\n=== ACTION ITEMS ===\n")
    
    if bars_with_no_long_blockers == 0 and bars_with_no_short_blockers == 0:
        print("  1. ALL bars have blockers - thresholds are too restrictive")
        print("     → Enable auto-apply to dynamically adjust thresholds")
        print("     → Or manually lower MinEntryFastGradientAbs, MinAdxForEntry")
    elif bars_with_no_long_blockers < len(diags) * 0.1 and bars_with_no_short_blockers < len(diags) * 0.1:
        print("  1. Very few bars pass filters (<10%)")
        print("     → Consider lowering entry thresholds")
    
    if 'FastGradMin' in long_blockers or 'FastGradMin' in short_blockers:
        print("  2. FastGradMin is a common blocker")
        print("     → Lower MinEntryFastGradientAbs (currently 0.50 by default)")
        print("     → Or enable adaptive entry gradient thresholds")
    
    if 'ADXMin' in long_blockers or 'ADXMin' in short_blockers:
        print("  3. ADXMin is blocking entries")
        print("     → Lower MinAdxForEntry (currently 18.0 by default)")
    
    if 'Bandwidth' in long_blockers or 'Bandwidth' in short_blockers:
        print("  4. Bandwidth filter is blocking entries")
        print("     → Increase MaxBandwidthForEntry or decrease MinBandwidthForEntry")
    
    if 'AccelAlign' in long_blockers or 'AccelAlign' in short_blockers:
        print("  5. Acceleration alignment is blocking entries")
        print("     → Disable RequireAccelAlignment if not needed")
    
    print(f"\n" + "="*80)
    print("END OF DIAGNOSTIC REPORT")
    print("="*80 + "\n")

if __name__ == "__main__":
    main()
