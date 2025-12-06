import requests
import json

# Get logs from server
response = requests.get('http://127.0.0.1:51888/logs/recent?limit=1000')
data = response.json()
logs = data.get('logs', [])

# Filter for EXIT_TRACE
exit_traces = [log for log in logs if log.get('action') == 'EXIT_TRACE']

print(f"\nTotal logs: {len(logs)}")
print(f"EXIT_TRACE logs: {len(exit_traces)}")

if exit_traces:
    print("\n=== EXIT_TRACE Logs ===")
    for log in exit_traces[-5:]:  # Show last 5
        print(f"\nBar {log.get('barIndex')}: {log.get('direction')}")
        print(f"Reason: {log.get('reason')}")
        print(f"Data: {json.dumps(log.get('data', {}), indent=2)}")
else:
    print("\n[!] No EXIT_TRACE logs found!")
    print("\nLast 10 log actions:")
    for log in logs[-10:]:
        print(f"  Bar {log.get('barIndex')}: {log.get('action')} {log.get('direction')}")
