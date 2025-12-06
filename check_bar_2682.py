import sqlite3

conn = sqlite3.connect('web/dashboard/diag.db')
cur = conn.cursor()

bar = 2682
cur.execute('SELECT * FROM diagnostics WHERE bar_number = ? ORDER BY rowid DESC LIMIT 1', (bar,))
row = cur.fetchone()

if not row:
    print(f'No data found for bar {bar}')
else:
    cur.execute('PRAGMA table_info(diagnostics)')
    cols = [c[1] for c in cur.fetchall()]
    data = dict(zip(cols, row))
    
    print(f'\n=== Bar {bar} Diagnostics ===\n')
    
    # Key entry readiness fields
    print(f"Entry Readiness:")
    print(f"  longEntryReady: {data.get('longEntryReady', 'N/A')}")
    print(f"  shortEntryReady: {data.get('shortEntryReady', 'N/A')}")
    print(f"  trendSide: {data.get('trendSide', 'N/A')}")
    
    print(f"\nCore Metrics:")
    print(f"  fastGrad: {data.get('fastGrad', 'N/A')}")
    print(f"  rsi: {data.get('rsi', 'N/A')}")
    print(f"  adx: {data.get('adx', 'N/A')}")
    print(f"  gradStab: {data.get('gradStab', 'N/A')}")
    print(f"  bandwidth: {data.get('bandwidth', 'N/A')}")
    
    print(f"\nThresholds:")
    print(f"  entryGradThrLong: {data.get('entryGradThrLong', 'N/A')}")
    print(f"  entryGradThrShort: {data.get('entryGradThrShort', 'N/A')}")
    print(f"  minAdxForEntry: {data.get('minAdxForEntry', 'N/A')}")
    print(f"  minRSIForEntry: {data.get('minRSIForEntry', 'N/A')}")
    print(f"  maxBandwidthForEntry: {data.get('maxBandwidthForEntry', 'N/A')}")
    print(f"  maxGradientStabilityForEntry: {data.get('maxGradientStabilityForEntry', 'N/A')}")
    
    print(f"\nBlockers:")
    print(f"  blockersLong: {data.get('blockersLong', 'N/A')}")
    print(f"  blockersShort: {data.get('blockersShort', 'N/A')}")
    
    print(f"\nAll fields:")
    for k, v in sorted(data.items()):
        print(f"  {k}: {v}")

conn.close()
