#!/usr/bin/env python3
"""Test the volatility API endpoints."""
import urllib.request
import json

BASE_URL = "http://localhost:51888"

def test_stats():
    print("\n=== Testing /api/volatility/stats ===")
    url = f"{BASE_URL}/api/volatility/stats"
    try:
        resp = urllib.request.urlopen(url, timeout=5)
        data = json.loads(resp.read().decode())
        print(f"Status: {data.get('status')}")
        stats = data.get('stats', [])
        print(f"Found {len(stats)} hourly stats")
        for s in stats[:3]:
            print(f"  Hour {s['hour']}: samples={s['sample_count']}, avg_range={s['avg_bar_range']}, avg_vol={s['avg_volume']}")
        if len(stats) > 3:
            print(f"  ... and {len(stats)-3} more hours")
        return True
    except Exception as e:
        print(f"Error: {e}")
        return False

def test_recommended_stop():
    print("\n=== Testing /api/volatility/recommended-stop ===")
    # Hour 17 (5pm) with typical volume
    url = f"{BASE_URL}/api/volatility/recommended-stop?hour=17&volume=10000&symbol=MNQ"
    try:
        resp = urllib.request.urlopen(url, timeout=5)
        data = json.loads(resp.read().decode())
        print(f"Response: {json.dumps(data, indent=2)}")
        return True
    except Exception as e:
        print(f"Error: {e}")
        return False

def test_record_bar():
    print("\n=== Testing /api/volatility/record-bar (POST) ===")
    url = f"{BASE_URL}/api/volatility/record-bar"
    payload = {
        "timestamp": "2024-01-15 14:30:00",
        "bar_index": 999,
        "symbol": "MNQ",
        "open": 20000.0,
        "high": 20004.25,
        "low": 20000.0,
        "close": 20003.0,
        "volume": 5000,
        "direction": "LONG",
        "in_trade": False
    }
    data = json.dumps(payload).encode('utf-8')
    req = urllib.request.Request(url, data=data, headers={'Content-Type': 'application/json'})
    try:
        resp = urllib.request.urlopen(req, timeout=5)
        result = json.loads(resp.read().decode())
        print(f"Response: {json.dumps(result, indent=2)}")
        return True
    except Exception as e:
        print(f"Error: {e}")
        return False

if __name__ == "__main__":
    print("Testing Volatility API Endpoints...")
    stats_ok = test_stats()
    stop_ok = test_recommended_stop()
    record_ok = test_record_bar()
    
    print("\n=== SUMMARY ===")
    print(f"Stats API: {'PASS' if stats_ok else 'FAIL'}")
    print(f"Recommended Stop API: {'PASS' if stop_ok else 'FAIL'}")
    print(f"Record Bar API: {'PASS' if record_ok else 'FAIL'}")
