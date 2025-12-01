import requests

r = requests.get('http://127.0.0.1:5001/diags?since=0')
d = r.json()

print('\n=== BANDWIDTH ANALYSIS (Last 10 bars) ===\n')

for x in d[-10:]:
    bar = x.get('barIndex', '?')
    bw = x.get('bandwidth', 0)
    fast = x.get('fastEMA', 0)
    slow = x.get('slowEMA', 0)
    close = x.get('close', 0)
    
    print(f"Bar {bar}:")
    print(f"  Close: {close:.2f}")
    print(f"  FastEMA: {fast:.2f}")
    print(f"  SlowEMA: {slow:.2f}")
    print(f"  Bandwidth: {bw:.6f} ({bw*100:.4f}%)")
    print(f"  Fast-Slow diff: {abs(fast-slow):.4f}")
    print()

print("=== DIAGNOSIS ===")
avg_bw = sum(x.get('bandwidth', 0) for x in d[-10:]) / 10
print(f"Average bandwidth (last 10): {avg_bw:.6f} ({avg_bw*100:.4f}%)")

if avg_bw < 0.0001:
    print("\n⚠ ISSUE FOUND: Bandwidth is nearly ZERO!")
    print("  This means FastEMA and SlowEMA are essentially the same value.")
    print("  Possible causes:")
    print("    1. Fast and Slow EMA periods are too close (e.g., 10 and 20)")
    print("    2. Market is extremely flat/ranging")
    print("    3. Data calculation issue in strategy")
else:
    print(f"\nBandwidth is normal. Market is genuinely in strong trend with no counter-trend bars.")
