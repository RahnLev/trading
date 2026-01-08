"""
Check the most recent NinjaTrader OutputWindow CSV file for diagnostic messages.
"""
import os
import glob
from pathlib import Path

# Strategy logs directory
STRATEGY_LOGS_DIR = os.path.join(os.path.dirname(__file__), '..', '..', 'strategy_logs')

def find_latest_output_file():
    """Find the most recent OutputWindow CSV file."""
    pattern = os.path.join(STRATEGY_LOGS_DIR, 'BarsOnTheFlow_OutputWindow_*.csv')
    files = glob.glob(pattern)
    if not files:
        return None
    files.sort(key=os.path.getmtime, reverse=True)
    return files[0]

def check_output_file(filepath):
    """Check output file for diagnostic messages."""
    print(f"Checking: {os.path.basename(filepath)}")
    print(f"File size: {os.path.getsize(filepath) / 1024 / 1024:.2f} MB")
    print()
    
    # Keywords to search for
    keywords = [
        'BAR_RECORD',
        'BAR_SAMPLE',
        'CSV_IMPORT',
        'REALTIME',
        'HttpClient',
        'DashboardBaseUrl',
        'api/volatility/record-bar',
        'Successfully recorded',
        'ERROR',
        'Error'
    ]
    
    found_messages = {kw: [] for kw in keywords}
    
    try:
        with open(filepath, 'r', encoding='utf-8', errors='ignore') as f:
            # Read last 5000 lines (to avoid reading entire huge file)
            lines = f.readlines()
            recent_lines = lines[-5000:] if len(lines) > 5000 else lines
            
            for line in recent_lines:
                for keyword in keywords:
                    if keyword.lower() in line.lower():
                        found_messages[keyword].append(line.strip())
                        if len(found_messages[keyword]) > 10:  # Limit to 10 per keyword
                            found_messages[keyword] = found_messages[keyword][-10:]
    except Exception as e:
        print(f"Error reading file: {e}")
        return
    
    # Print findings
    print("=" * 60)
    print("Diagnostic Messages Found (last 10 per keyword):")
    print("=" * 60)
    
    for keyword, messages in found_messages.items():
        if messages:
            print(f"\n[{keyword}] - Found {len(messages)} messages:")
            for msg in messages[-5:]:  # Show last 5
                print(f"  {msg[:150]}")  # Truncate long lines
    
    # Summary
    print("\n" + "=" * 60)
    print("Summary:")
    print("=" * 60)
    
    if found_messages['BAR_RECORD']:
        print("✅ BAR_RECORD messages found - RecordBarSample is being called")
    else:
        print("❌ No BAR_RECORD messages - RecordBarSample may not be called")
    
    if found_messages['BAR_SAMPLE']:
        print("✅ BAR_SAMPLE messages found - HTTP requests are being sent")
        if any('Successfully recorded' in m for m in found_messages['BAR_SAMPLE']):
            print("✅ Success messages found - Server is responding")
        else:
            print("⚠️  No success messages - Check for errors")
    else:
        print("❌ No BAR_SAMPLE messages - HTTP requests may not be sent")
    
    if found_messages['ERROR'] or found_messages['Error']:
        print(f"⚠️  Found {len(found_messages['ERROR']) + len(found_messages['Error'])} error messages")
        print("   Check the messages above for details")
    
    if found_messages['DashboardBaseUrl']:
        print(f"✅ DashboardBaseUrl found: {found_messages['DashboardBaseUrl'][-1][:100]}")

if __name__ == "__main__":
    print("=" * 60)
    print("NinjaTrader Output Window Checker")
    print("=" * 60)
    print()
    
    latest_file = find_latest_output_file()
    if not latest_file:
        print("No OutputWindow CSV files found!")
        sys.exit(1)
    
    check_output_file(latest_file)
