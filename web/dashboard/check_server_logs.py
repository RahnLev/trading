"""Check if server is receiving and processing requests."""
import os
import subprocess
import time

print("=" * 60)
print("CHECKING SERVER STATUS")
print("=" * 60)
print()

# Check if server process is running
try:
    result = subprocess.run(['netstat', '-ano'], capture_output=True, text=True, timeout=5)
    if ':51888' in result.stdout:
        print("✅ Server appears to be listening on port 51888")
        # Extract PID
        for line in result.stdout.split('\n'):
            if ':51888' in line and 'LISTENING' in line:
                parts = line.split()
                if len(parts) > 0:
                    print(f"   PID: {parts[-1]}")
    else:
        print("❌ Server is NOT listening on port 51888")
        print("   → Start the server: python server.py")
except Exception as e:
    print(f"⚠️  Could not check server status: {e}")

print()
print("=" * 60)
print("TESTING SERVER ENDPOINT")
print("=" * 60)
print()

# Test the endpoint
try:
    import requests
    test_data = {
        "timestamp": "2026-01-01 18:00:00",
        "bar_index": 9999,
        "symbol": "MNQ",
        "open": 25000.0,
        "high": 25010.0,
        "low": 24990.0,
        "close": 25005.0,
        "volume": 1000,
        "direction": "FLAT",
        "in_trade": False
    }
    
    print("Sending test request to server...")
    response = requests.post(
        "http://127.0.0.1:51888/api/volatility/record-bar",
        json=test_data,
        timeout=10
    )
    
    print(f"Status Code: {response.status_code}")
    print(f"Response: {response.text[:200]}")
    
    if response.status_code == 200:
        print("✅ Server is responding correctly!")
    else:
        print(f"⚠️  Server returned status {response.status_code}")
        
except requests.exceptions.ConnectionError:
    print("❌ Cannot connect to server")
    print("   → Server is not running or not accessible")
except requests.exceptions.Timeout:
    print("❌ Request timed out")
    print("   → Server is running but not responding")
except Exception as e:
    print(f"❌ Error: {e}")

print()
print("=" * 60)
