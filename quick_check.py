import requests

r = requests.get('http://127.0.0.1:5001/diags?since=0')
d = r.json()[-5:]

print("\n=== LATEST 5 BARS ===\n")
for x in d:
    print(f"Bar {x['barIndex']}: Signal={x.get('signal','?')} FastGrad={x.get('fastGrad',0):+.3f} ADX={x.get('adx',0):.1f} RSI={x.get('rsi',0):.1f}")
    print(f"  BlockersLong: {x.get('blockersLong',[])}")
    print(f"  BlockersShort: {x.get('blockersShort',[])}\n")

# Check overrides
print("\n=== CURRENT OVERRIDES ===\n")
r2 = requests.get('http://127.0.0.1:5001/overrides')
overrides = r2.json()
for k, v in overrides.items():
    print(f"  {k}: {v}")
