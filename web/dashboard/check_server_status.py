#!/usr/bin/env python
"""Check if the dashboard server is running"""
import os
import sys
import socket
import requests

def check_port(host, port):
    """Check if a port is open"""
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(1)
        result = sock.connect_ex((host, port))
        sock.close()
        return result == 0
    except:
        return False

def check_server_health():
    """Check if server responds to health endpoint"""
    try:
        response = requests.get('http://127.0.0.1:51888/health', timeout=2)
        return response.status_code == 200, response.json() if response.status_code == 200 else None
    except requests.exceptions.RequestException:
        return False, None

def check_pid_file():
    """Check if PID file exists"""
    pid_file = os.path.join(os.path.dirname(__file__), '.server.pid')
    if os.path.exists(pid_file):
        try:
            with open(pid_file, 'r') as f:
                pid = f.read().strip()
            return True, pid
        except:
            return False, None
    return False, None

print("=" * 60)
print("Dashboard Server Status Check")
print("=" * 60)

# Check port
port_open = check_port('127.0.0.1', 51888)
print(f"\n1. Port 51888: {'✓ OPEN' if port_open else '✗ CLOSED'}")

# Check health endpoint
if port_open:
    health_ok, health_data = check_server_health()
    print(f"2. Health endpoint: {'✓ RESPONDING' if health_ok else '✗ NOT RESPONDING'}")
    if health_data:
        print(f"   Response: {health_data}")
else:
    print("2. Health endpoint: ✗ SKIPPED (port closed)")

# Check PID file
pid_exists, pid = check_pid_file()
if pid_exists:
    print(f"3. PID file: ✓ EXISTS (PID: {pid})")
    # Check if process is actually running
    try:
        import psutil
        if psutil.pid_exists(int(pid)):
            proc = psutil.Process(int(pid))
            print(f"   Process: ✓ RUNNING ({proc.name()})")
        else:
            print(f"   Process: ✗ NOT RUNNING (stale PID file)")
    except ImportError:
        print(f"   Process: ? CANNOT CHECK (psutil not installed)")
    except:
        print(f"   Process: ? CANNOT CHECK")
else:
    print("3. PID file: ✗ NOT FOUND")

print("\n" + "=" * 60)
if port_open and health_ok:
    print("✓ SERVER IS RUNNING")
    print("\nAccess the database monitor at:")
    print("  http://127.0.0.1:51888/database-monitor.html")
else:
    print("✗ SERVER IS NOT RUNNING")
    print("\nTo start the server, run:")
    print("  start-dashboard.cmd")
    print("  OR")
    print("  python server.py")
print("=" * 60)

