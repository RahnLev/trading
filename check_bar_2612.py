import requests

# Get logs for bar 2612
r = requests.get('http://127.0.0.1:51888/logs/recent?limit=5000')
logs = r.json()['logs']

# Filter for bar 2612
bar_2612_logs = [l for l in logs if l.get('barIndex') == 2612]

print(f"Found {len(bar_2612_logs)} logs for bar 2612")
print()

# Look for ENTRY log
entry_logs = [l for l in bar_2612_logs if l.get('action') == 'ENTRY']
if entry_logs:
    print("=== ENTRY LOG ===")
    for log in entry_logs:
        print(f"Direction: {log.get('direction')}")
        print(f"Reason: {log.get('reason')}")
        print(f"Timestamp: {log.get('timestamp')}")
        if log.get('data'):
            print("Data:")
            for k, v in log.get('data', {}).items():
                print(f"  {k}: {v}")

# Look for ENTRY_DECISION log
decision_logs = [l for l in bar_2612_logs if l.get('action') == 'ENTRY_DECISION']
if decision_logs:
    print("\n=== ENTRY_DECISION LOG ===")
    for log in decision_logs:
        print(f"Direction: {log.get('direction')}")
        print(f"Reason: {log.get('reason')}")
        if log.get('data'):
            print("Data:")
            for k, v in log.get('data', {}).items():
                print(f"  {k}: {v}")

# Get bar data
r2 = requests.get('http://127.0.0.1:51888/bars/around?bar=2612&before=1&after=1')
bars = r2.json().get('bars', [])
bar_2612 = next((b for b in bars if b.get('barIndex') == 2612), None)

if bar_2612:
    print("\n=== BAR 2612 DATA ===")
    print(f"Open: {bar_2612.get('open')}")
    print(f"High: {bar_2612.get('high')}")
    print(f"Low: {bar_2612.get('low')}")
    print(f"Close: {bar_2612.get('close')}")
    print(f"Green bar: {bar_2612.get('close', 0) > bar_2612.get('open', 0)}")
    print(f"FastEMA: {bar_2612.get('fastEMA')}")
    print(f"SlowEMA: {bar_2612.get('slowEMA')}")
    print(f"FastGrad: {bar_2612.get('fastGrad')}")
    print(f"SlowGrad: {bar_2612.get('slowGrad')}")
    print(f"Signal: {bar_2612.get('signal')}")
    print(f"MyPosition: {bar_2612.get('myPosition')}")

# Look for signal change logs around this bar
print("\n=== LOGS AROUND BAR 2612 (2610-2614) ===")
nearby_logs = [l for l in logs if 2610 <= l.get('barIndex', 0) <= 2614]
for log in sorted(nearby_logs, key=lambda x: x.get('barIndex', 0)):
    action = log.get('action', 'UNKNOWN')
    bar = log.get('barIndex', 0)
    direction = log.get('direction', '')
    reason = log.get('reason', '')[:80] if log.get('reason') else ''
    print(f"Bar {bar}: {action:20s} {direction:6s} {reason}")
