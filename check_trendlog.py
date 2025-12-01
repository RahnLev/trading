import requests

resp = requests.get("http://127.0.0.1:5001/trendlog")
resp.raise_for_status()
data = resp.json()

print("\n=== TRENDLOG RESPONSE ===\n")
print(f"Current trend: {data.get('current')}")
print(f"\nRecent segments ({len(data.get('segments', []))} total):")

for seg in data.get('segments', [])[-5:]:
    print(f"\n  Side: {seg.get('side')}")
    print(f"  Count: {seg.get('count')}")
    print(f"  Good: {seg.get('good')}")
    print(f"  Bad: {seg.get('bad')}")
    print(f"  All keys: {list(seg.keys())}")

print(f"\nGood/Bad updates count: {data.get('goodBadUpdates', 0)}")
print(f"Inputs missing count: {data.get('inputsMissingCount', 0)}")
