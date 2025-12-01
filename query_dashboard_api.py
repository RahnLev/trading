import requests
import json

# Query the dashboard API to see recent diagnostics
url = "http://127.0.0.1:5001/diags?since=0"

try:
    response = requests.get(url)
    response.raise_for_status()
    data = response.json()
    
    print(f"\n=== DIAGNOSTICS FROM API ({len(data)} total) ===\n")
    
    # Show last 10
    recent = data[-10:] if len(data) > 10 else data
    
    for d in recent:
        print(f"Bar {d['barIndex']} at {d['time']}")
        print(f"  FastGrad: {d['fastGrad']:+.3f} | RSI: {d['rsi']:.1f} | ADX: {d['adx']:.1f}")
        print(f"  GradStab: {d['gradStab']:.3f} | Bandwidth: {d['bandwidth']:.4f} | Volume: {d.get('volume', 0):.0f}")
        print(f"  Signal: {d.get('signal', 'N/A')} | Trend: {d.get('trendSide', 'N/A')}")
        print(f"  BlockersLong: {d.get('blockersLong', [])}")
        print(f"  BlockersShort: {d.get('blockersShort', [])}\n")
    
    # Analyze blockers
    if recent:
        print("\n=== BLOCKER ANALYSIS ===\n")
        blocker_counts_long = {}
        blocker_counts_short = {}
        for d in data:
            for b in d.get('blockersLong', []):
                blocker_counts_long[b] = blocker_counts_long.get(b, 0) + 1
            for b in d.get('blockersShort', []):
                blocker_counts_short[b] = blocker_counts_short.get(b, 0) + 1
        
        print("Long Entry Blockers (frequency):")
        for blocker, count in sorted(blocker_counts_long.items(), key=lambda x: -x[1]):
            pct = 100 * count / len(data)
            print(f"  {blocker}: {count} / {len(data)} bars ({pct:.1f}%)")
        
        print("\nShort Entry Blockers (frequency):")
        for blocker, count in sorted(blocker_counts_short.items(), key=lambda x: -x[1]):
            pct = 100 * count / len(data)
            print(f"  {blocker}: {count} / {len(data)} bars ({pct:.1f}%)")
    
    # Check current overrides
    print("\n\n=== CURRENT OVERRIDES ===\n")
    resp_overrides = requests.get("http://127.0.0.1:5001/overrides")
    resp_overrides.raise_for_status()
    overrides = resp_overrides.json()
    if overrides:
        for key, value in overrides.items():
            print(f"  {key}: {value}")
    else:
        print("  No overrides currently applied.")
        
except Exception as e:
    print(f"Error querying API: {e}")
