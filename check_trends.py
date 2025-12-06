import requests
import json

r = requests.get('http://127.0.0.1:51888/trends/segments?limit=50')
data = r.json()

print("=== TRENDS/SEGMENTS ENDPOINT ===")
print(json.dumps(data, indent=2))

if data.get('segments'):
    print(f"\n=== Found {len(data['segments'])} trend segments ===")
    for seg in data['segments'][-5:]:  # Show last 5
        print(f"\nSide: {seg.get('side')}")
        print(f"Start Bar: {seg.get('startBarIndex')}")
        print(f"End Bar: {seg.get('endBarIndex')}")
        print(f"Start Time: {seg.get('startLocal')}")
        print(f"Count: {seg.get('count')}")
else:
    print("\nNo segments found!")
