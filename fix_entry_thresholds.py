import requests
import json

API_BASE = "http://127.0.0.1:5001"

print("\n=== APPLYING THRESHOLD ADJUSTMENTS ===\n")

# Current situation:
# - FastGrad = +0.011, but threshold is 0.02
# - ADX = 19.7, but threshold is 20.0
# - Auto-apply is DISABLED

# Solution: Lower thresholds to allow entries with current market conditions

adjustments = {
    "MinEntryFastGradientAbs": 0.008,  # Lower from 0.02 to 0.008 (below current 0.011)
    "MinAdxForEntry": 18.0,  # Lower from 20.0 to 18.0 (below current 19.7)
    "AdaptiveMinFloor": 0.008,  # Lower adaptive floor to match
}

print("Applying the following threshold adjustments:")
for key, value in adjustments.items():
    print(f"  {key}: {value}")

try:
    # Apply each override
    for key, value in adjustments.items():
        payload = {
            "property": key,
            "value": value
        }
        resp = requests.post(f"{API_BASE}/apply", json=payload)
        resp.raise_for_status()
        print(f"✓ Applied {key} = {value}")
    
    print("\n✓ All adjustments applied successfully!")
    print("\nThe strategy should now be able to enter trades with current market conditions.")
    print("FastGrad threshold lowered from 0.02 to 0.008 (current FastGrad is 0.011)")
    print("ADX threshold lowered from 20.0 to 18.0 (current ADX is 19.7)")
    
    # Enable auto-apply for future adjustments
    print("\n=== ENABLING AUTO-APPLY ===\n")
    resp_auto = requests.post(f"{API_BASE}/autoapply/toggle", json={"enabled": True})
    resp_auto.raise_for_status()
    print("✓ Auto-apply enabled - system will automatically adjust thresholds if entries continue to be missed")
    
except Exception as e:
    print(f"\n✗ Error applying adjustments: {e}")

print("\n" + "="*60)
print("Monitor the dashboard at http://127.0.0.1:5001")
print("Entries should start firing within the next few bars!")
print("="*60 + "\n")
