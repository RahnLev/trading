"""Check NinjaTrader output for database write issues."""
import os
import glob

logs_dir = r"\\Mac\Home\Documents\NinjaTrader 8\bin\Custom\strategy_logs"
pattern = os.path.join(logs_dir, "BarsOnTheFlow_OutputWindow_*.csv")
files = glob.glob(pattern)

if not files:
    print("No output files found!")
    exit(1)

latest = max(files, key=os.path.getmtime)
print(f"Checking: {os.path.basename(latest)}\n")
print("=" * 70)

# Search for key messages
keywords = [
    'Successfully recorded',
    'BAR_SAMPLE',
    'BAR_SAMPLE_ERROR',
    'HTTP',
    'timeout',
    'TaskCanceled',
    'ObjectDisposed',
    'api/volatility/record-bar',
    'volatility record-bar',
    'Saved bar'
]

found = {k: [] for k in keywords}

try:
    with open(latest, 'r', encoding='utf-8', errors='ignore') as f:
        lines = f.readlines()
        recent = lines[-10000:] if len(lines) > 10000 else lines
        
        for line in recent:
            for kw in keywords:
                if kw.lower() in line.lower():
                    found[kw].append(line.strip()[:200])
                    if len(found[kw]) > 20:
                        found[kw] = found[kw][-20:]  # Keep last 20
except Exception as e:
    print(f"Error: {e}")
    exit(1)

# Print findings
print("DATABASE WRITE ANALYSIS:")
print("=" * 70)

# Count successes
success_count = len(found['Successfully recorded'])
print(f"\n✅ Successfully recorded: {success_count} bars")

# Count errors
error_count = len(found['BAR_SAMPLE_ERROR']) + len(found['timeout']) + len(found['TaskCanceled'])
print(f"❌ Errors/Timeouts: {error_count}")

# Show HTTP responses
if found['HTTP']:
    print(f"\n📡 HTTP responses found: {len(found['HTTP'])}")
    print("   Last 5 HTTP messages:")
    for msg in found['HTTP'][-5:]:
        print(f"   {msg[:150]}")

# Show server responses
if found['volatility record-bar'] or found['Saved bar']:
    print(f"\n💾 Server save messages found: {len(found.get('volatility record-bar', [])) + len(found.get('Saved bar', []))}")
    print("   Last 5 server messages:")
    for msg in (found.get('volatility record-bar', []) + found.get('Saved bar', []))[-5:]:
        print(f"   {msg[:150]}")

# Show errors
if found['BAR_SAMPLE_ERROR']:
    print(f"\n❌ BAR_SAMPLE_ERROR messages ({len(found['BAR_SAMPLE_ERROR'])}):")
    print("   Last 5 errors:")
    for msg in found['BAR_SAMPLE_ERROR'][-5:]:
        print(f"   {msg[:150]}")

if found['timeout']:
    print(f"\n⏱️  Timeout messages ({len(found['timeout'])}):")
    print("   Last 5 timeouts:")
    for msg in found['timeout'][-5:]:
        print(f"   {msg[:150]}")

# Show successful recordings
if found['Successfully recorded']:
    print(f"\n✅ Successfully recorded messages ({len(found['Successfully recorded'])}):")
    print("   All successful bars:")
    for msg in found['Successfully recorded']:
        # Extract bar number
        if 'Bar ' in msg:
            bar_num = msg.split('Bar ')[1].split(':')[0] if 'Bar ' in msg else '?'
            print(f"   Bar {bar_num}")

print("\n" + "=" * 70)
print("DIAGNOSIS:")
print("=" * 70)

if success_count == 0:
    print("❌ NO bars were successfully recorded!")
    print("   → All HTTP requests are failing or timing out")
    print("   → Check if server is running")
    print("   → Check server logs for errors")
elif success_count < 10:
    print(f"⚠️  Only {success_count} bars recorded (very low)")
    print("   → Most requests are timing out")
    print("   → Increase delay between requests")
    print("   → Or reduce concurrent requests")
else:
    print(f"✅ {success_count} bars recorded successfully")
    if error_count > success_count * 2:
        print(f"   ⚠️  But {error_count} errors/timeouts (many failures)")

print("\n" + "=" * 70)
