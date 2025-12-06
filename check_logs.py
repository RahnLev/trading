import requests

r = requests.get('http://127.0.0.1:51888/logs/recent?limit=1000')
logs = r.json()['logs']
print(f'Total logs: {len(logs)}')
print('Actions:', sorted(set(l.get('action') for l in logs)))
entry_logs = [l for l in logs if l.get('action') == 'ENTRY']
exit_logs = [l for l in logs if l.get('action') == 'EXIT']
print(f'ENTRY logs: {len(entry_logs)}')
print(f'EXIT logs: {len(exit_logs)}')
if entry_logs:
    print('\nLatest ENTRY:', entry_logs[-1])
if exit_logs:
    print('\nLatest EXIT:', exit_logs[-1])
