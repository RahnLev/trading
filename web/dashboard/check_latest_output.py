"""Check the latest NinjaTrader output file for BAR_SAMPLE messages."""
import os
import glob

logs_dir = r"\\Mac\Home\Documents\NinjaTrader 8\bin\Custom\strategy_logs"
pattern = os.path.join(logs_dir, "BarsOnTheFlow_OutputWindow_*.csv")
files = glob.glob(pattern)

if not files:
    print("No output files found!")
    exit(1)

# Get most recent
latest = max(files, key=os.path.getmtime)
print(f"Latest file: {os.path.basename(latest)}")
print(f"Size: {os.path.getsize(latest) / 1024 / 1024:.1f} MB\n")
print("=" * 70)

# Search for diagnostic messages
search_terms = [
    'BAR_RECORD',
    'BAR_SAMPLE', 
    'Successfully recorded',
    'BAR_SAMPLE_ERROR',
    'HttpClient',
    'DashboardBaseUrl',
    'api/volatility/record-bar',
    'REALTIME_TRANSITION',
    'CSV_IMPORT'
]

results = {term: [] for term in search_terms}

try:
    with open(latest, 'r', encoding='utf-8', errors='ignore') as f:
        lines = f.readlines()
        print(f"Total lines in file: {len(lines):,}")
        
        # Check last 5000 lines (most recent activity)
        recent_lines = lines[-5000:] if len(lines) > 5000 else lines
        print(f"Checking last {len(recent_lines):,} lines...\n")
        
        for line in recent_lines:
            for term in search_terms:
                if term.lower() in line.lower():
                    # Extract just the message part (remove timestamp if present)
                    clean_line = line.strip()
                    if len(clean_line) > 200:
                        clean_line = clean_line[:200] + "..."
                    results[term].append(clean_line)
                    # Keep only last 10 per term
                    if len(results[term]) > 10:
                        results[term] = results[term][-10:]

except Exception as e:
    print(f"Error reading file: {e}")
    import traceback
    traceback.print_exc()
    exit(1)

# Print findings
print("FINDINGS:")
print("=" * 70)

for term in search_terms:
    if results[term]:
        print(f"\n[{term}] - Found {len(results[term])} messages (showing last 5):")
        for msg in results[term][-5:]:
            print(f"  {msg}")
    else:
        print(f"\n[{term}] - No messages found")

# Diagnosis
print("\n" + "=" * 70)
print("DIAGNOSIS:")
print("=" * 70)

if results['BAR_RECORD']:
    print("✅ BAR_RECORD messages found")
    print("   → RecordBarSample() IS being called")
else:
    print("❌ BAR_RECORD messages NOT found")
    print("   → RecordBarSample() is NOT being called")
    print("   → Check if CurrentBar >= 1 condition is met")

if results['BAR_SAMPLE']:
    print("✅ BAR_SAMPLE messages found")
    print("   → HTTP requests ARE being sent")
    
    if results['Successfully recorded']:
        print("✅ 'Successfully recorded' messages found")
        print("   → Server IS receiving and saving data")
    else:
        print("⚠️  'Successfully recorded' messages NOT found")
        print("   → HTTP requests sent but may be failing")
        
    if results['BAR_SAMPLE_ERROR']:
        print(f"⚠️  Found {len(results['BAR_SAMPLE_ERROR'])} error messages")
        print("   → Check error messages above")
else:
    print("❌ BAR_SAMPLE messages NOT found")
    print("   → HTTP requests are NOT being sent")
    print("   → Check HttpClient initialization")

if results['DashboardBaseUrl']:
    print(f"✅ DashboardBaseUrl configured")
    for msg in results['DashboardBaseUrl'][-1:]:
        print(f"   → {msg[:100]}")

if results['REALTIME_TRANSITION']:
    print("✅ REALTIME_TRANSITION messages found")
    print("   → Strategy transitioned to real-time mode")

if results['CSV_IMPORT']:
    print("✅ CSV_IMPORT messages found")
    print("   → CSV import was attempted")

print("\n" + "=" * 70)
print("RECOMMENDATION:")
print("=" * 70)

if not results['BAR_RECORD']:
    print("→ Strategy is not calling RecordBarSample()")
    print("  Check: Is CurrentBar >= 1? Is ProcessDatabaseOperations() being called?")
elif not results['BAR_SAMPLE']:
    print("→ RecordBarSample() is called but HTTP requests are not being sent")
    print("  Check: Is HttpClient initialized? Is DashboardBaseUrl correct?")
elif not results['Successfully recorded']:
    print("→ HTTP requests are sent but server is not responding successfully")
    print("  Check: Is server running? Check server logs for errors")
else:
    print("→ Everything looks good! Data should be in database.")
    print("  If website still shows no data, check:")
    print("  1. Database file exists: volatility.db")
    print("  2. Run: python diagnose_issue.py")
    print("  3. Check browser console (F12) for JavaScript errors")
