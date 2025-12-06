import requests
import json

try:
    r = requests.get('http://127.0.0.1:5001/bars/latest?limit=400', timeout=5)
    bars = r.json()
    
    bar = next((b for b in bars if b.get('barNumber') == 2682), None)
    
    if not bar:
        print(f"Bar 2682 not found in cache (cache has {len(bars)} bars)")
        if bars:
            print(f"Bar range in cache: {bars[0].get('barNumber')} to {bars[-1].get('barNumber')}")
    else:
        print(f"\n=== Bar 2682 Details ===\n")
        print(f"Long Entry Ready: {bar.get('longEntryReady')}")
        print(f"Short Entry Ready: {bar.get('shortEntryReady')}")
        print(f"Trend Side: {bar.get('trendSide')}")
        
        print(f"\nCore Metrics:")
        print(f"  FastGrad: {bar.get('fastGrad')}")
        print(f"  RSI: {bar.get('rsi')}")
        print(f"  ADX: {bar.get('adx')}")
        print(f"  GradStab: {bar.get('gradStab')}")
        print(f"  Bandwidth: {bar.get('bandwidth')}")
        
        print(f"\nThresholds:")
        print(f"  entryGradThrLong: {bar.get('entryGradThrLong')}")
        print(f"  entryGradThrShort: {bar.get('entryGradThrShort')}")
        print(f"  minAdxForEntry: {bar.get('minAdxForEntry')}")
        print(f"  minRSIForEntry: {bar.get('minRSIForEntry')}")
        print(f"  maxBandwidthForEntry: {bar.get('maxBandwidthForEntry')}")
        print(f"  maxGradientStabilityForEntry: {bar.get('maxGradientStabilityForEntry')}")
        
        print(f"\nBlockers:")
        print(f"  blockersLong: {bar.get('blockersLong')}")
        print(f"  blockersShort: {bar.get('blockersShort')}")
        
        print(f"\nAnalysis:")
        if bar.get('longEntryReady'):
            print("  ✓ Long entry conditions MET")
        else:
            print("  ✗ Long entry blocked by:", bar.get('blockersLong'))
            
        if bar.get('shortEntryReady'):
            print("  ✓ Short entry conditions MET")
        else:
            print("  ✗ Short entry blocked by:", bar.get('blockersShort'))

except Exception as e:
    print(f"Error: {e}")
