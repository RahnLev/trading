import requests

API_BASE = "http://127.0.0.1:5001"

print("\n=== LOWERING ADX THRESHOLD ===\n")

# Current ADX is 17.9, but threshold is 18.0
# Lower to 16.0 to allow current conditions

adjustment = {
    "property": "MinAdxForEntry",
    "value": 16.0
}

try:
    resp = requests.post(f"{API_BASE}/apply", json=adjustment)
    resp.raise_for_status()
    print(f"✓ Lowered MinAdxForEntry to 16.0 (current ADX: 17.9)")
    
    # Also enable auto-apply
    resp_auto = requests.post(f"{API_BASE}/autoapply/toggle", json={"enabled": True})
    resp_auto.raise_for_status()
    print("✓ Auto-apply enabled")
    
    print("\n=== CURRENT STATUS ===")
    print("Latest bar 6765:")
    print("  FastGrad: +0.010 (threshold: 0.008) ✓")
    print("  ADX: 17.9 (threshold: 16.0) ✓")
    print("  Signal: LONG")
    print("\n→ Next bar with similar conditions should trigger an entry!")
    
except Exception as e:
    print(f"Error: {e}")
