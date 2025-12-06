import requests
import json
import sys

bars = [2170, 2015]

print("Querying bar details from dashboard API...\n")

for bar_index in bars:
    try:
        response = requests.get(f"http://127.0.0.1:51888/bars/detail/{bar_index}")
        if response.status_code == 200:
            data = response.json()
            bar = data.get('bar', {})
            logs = data.get('logs', [])
            entry_attempt = data.get('entryAttempt')
            filter_blocks = data.get('filterBlocks', [])
            
            print(f"{'='*80}")
            print(f"BAR {bar_index}")
            print(f"{'='*80}")
            
            # Show bar metrics
            print(f"\nBar Metrics:")
            print(f"  Time: {bar.get('localTime')}")
            print(f"  Close: {bar.get('close'):.2f}")
            print(f"  Fast Gradient: {bar.get('fastGrad'):.4f}")
            print(f"  Slow Gradient: {bar.get('slowGrad'):.4f}")
            print(f"  RSI: {bar.get('rsi'):.2f}")
            print(f"  ADX: {bar.get('adx'):.2f}")
            print(f"  ATR: {bar.get('atr'):.4f}")
            print(f"  Bandwidth: {bar.get('bandwidth'):.4f}")
            print(f"  Grad Stability: {bar.get('gradStab'):.4f}")
            
            # Show entry readiness
            print(f"\nEntry Readiness:")
            print(f"  Long Ready: {bar.get('entryLongReady')}")
            print(f"  Short Ready: {bar.get('entryShortReady')}")
            
            # Show filter results
            print(f"\nFilter Results:")
            print(f"  ADX OK: {bar.get('adxOk')}")
            print(f"  RSI OK: {bar.get('rsiOk')}")
            print(f"  ATR OK: {bar.get('atrOk')}")
            print(f"  Bandwidth OK: {bar.get('bandwidthOk')}")
            print(f"  Grad Stability OK: {bar.get('gradStabOk')}")
            print(f"  Not Overextended: {bar.get('notOverextended')}")
            print(f"  Fast Strong Long: {bar.get('fastStrongForEntryLong')}")
            print(f"  Fast Strong Short: {bar.get('fastStrongForEntryShort')}")
            print(f"  Accel Align Long: {bar.get('accelAlignOkLong')}")
            print(f"  Accel Align Short: {bar.get('accelAlignOkShort')}")
            
            # Show thresholds
            print(f"\nThresholds:")
            print(f"  Min Entry Grad Long: {bar.get('entryGradThrLong'):.4f}")
            print(f"  Min Entry Grad Short: {bar.get('entryGradThrShort'):.4f}")
            print(f"  Max Entry Grad Abs: {bar.get('maxEntryFastGradientAbs', 0):.4f}")
            print(f"  Min ADX: {bar.get('minAdxForEntry'):.2f}")
            print(f"  Min RSI: {bar.get('minRSIForEntry'):.2f}")
            print(f"  Max ATR: {bar.get('maxATRForEntry'):.4f}")
            print(f"  Min Bandwidth: {bar.get('minBandwidthForEntry'):.4f}")
            print(f"  Max Bandwidth: {bar.get('maxBandwidthForEntry'):.4f}")
            print(f"  Max Grad Stability: {bar.get('maxGradientStabilityForEntry'):.4f}")
            
            # Show filter blocks
            if filter_blocks:
                print(f"\nFILTER BLOCKS:")
                for block in filter_blocks:
                    print(f"  Direction: {block.get('direction')}")
                    print(f"  Reason: {block.get('reason')}")
                    print(f"  Filters: {block.get('filters')}")
            
            # Show entry attempt
            if entry_attempt:
                print(f"\nEntry Attempt:")
                print(f"  Action: {entry_attempt.get('action')}")
                print(f"  Direction: {entry_attempt.get('direction')}")
                print(f"  Reason: {entry_attempt.get('reason')}")
                
            print()
            
        else:
            print(f"Bar {bar_index}: Not found (status {response.status_code})")
            
    except Exception as e:
        print(f"Error querying bar {bar_index}: {e}")
