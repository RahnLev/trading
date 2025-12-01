import requests

resp = requests.get("http://127.0.0.1:5001/diags?since=0")
resp.raise_for_status()
diags = resp.json()

print(f"\n=== CHECKING CLOSE VS FASTEMA (Last 10 bars) ===\n")

for d in diags[-10:]:
    bar = d.get('barIndex', '?')
    close = d.get('close')
    fast_ema = d.get('fastEMA')
    fast_grad = d.get('fastGrad', 0)
    side = "BULL" if fast_grad >= 0 else "BEAR"
    
    if close is not None and fast_ema is not None:
        close_f = float(close)
        fast_f = float(fast_ema)
        diff = close_f - fast_f
        
        # Determine if good or bad based on trend logic
        if side == "BULL":
            is_good = close_f >= fast_f
            is_bad = close_f < fast_f
        else:  # BEAR
            is_good = close_f <= fast_f
            is_bad = close_f > fast_f
        
        print(f"Bar {bar}: Side={side}")
        print(f"  Close: {close_f:.2f} | FastEMA: {fast_f:.2f} | Diff: {diff:+.4f}")
        print(f"  Result: {'GOOD' if is_good else 'BAD'} ({'close>=fast' if close_f >= fast_f else 'close<fast'})")
        print()
    else:
        print(f"Bar {bar}: Missing data - close={close}, fastEMA={fast_ema}\n")

print("\n=== DIAGNOSIS ===")
print("If all bars show GOOD, then:")
print("  - For BULL trends: close is always >= fastEMA (strong trend)")
print("  - For BEAR trends: close is always <= fastEMA (strong trend)")
print("\nThis means NO counter-trend bars are occurring.")
print("Consider checking if fastEMA values are being sent correctly from strategy.")
