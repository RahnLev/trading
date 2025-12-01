import requests

resp = requests.get("http://127.0.0.1:5001/diags?since=0")
resp.raise_for_status()
diags = resp.json()

print("\n=== DETAILED CANDLE ANALYSIS (Bars 6629-6632) ===\n")

for d in diags:
    if d.get('barIndex') in [6629, 6630, 6631, 6632]:
        bar = d.get('barIndex')
        close = d.get('close', 0)
        fast_ema = d.get('fastEMA', 0)
        fast_grad = d.get('fastGrad', 0)
        side = "BULL" if fast_grad >= 0 else "BEAR"
        
        # Good/bad logic from server
        if side == "BULL":
            is_good = close >= fast_ema
            criterion = "close >= fastEMA"
        else:
            is_good = close <= fast_ema
            criterion = "close <= fastEMA"
        
        print(f"Bar {bar}: Trend={side}")
        print(f"  Close: {close:.2f}")
        print(f"  FastEMA: {fast_ema:.2f}")
        print(f"  FastGrad: {fast_grad:+.4f}")
        print(f"  Criterion: {criterion}")
        print(f"  Result: {'GOOD' if is_good else 'BAD'}")
        print()

print("=== ANALYSIS ===")
print("Current logic:")
print("  BULL trend: GOOD if close >= fastEMA (price staying above EMA)")
print("             BAD if close < fastEMA (pullback/retracement)")
print()
print("Potential issue:")
print("  A 'bad bar' might be better defined as:")
print("    - Candle color (red/green) vs trend direction")
print("    - Candle close relative to OPEN, not just EMA")
print("    - Price moving against the trend (fastGrad sign flip)")
print()
print("If bar 6630 had negative fastGrad but was in BULL trend,")
print("that would indicate a retracement/pullback = BAD bar")
