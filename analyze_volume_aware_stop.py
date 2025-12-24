import sqlite3
from datetime import datetime

db_path = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard\volatility.db'
bars_db_path = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard\bars.db'

print("="*140)
print("VOLUME-AWARE STOP CALCULATION ANALYSIS")
print("="*140)
print()

try:
    # First, get bar 3344 details
    conn_bars = sqlite3.connect(bars_db_path)
    cur_bars = conn_bars.cursor()
    
    cur_bars.execute("""
        SELECT barIndex, barTime, volume, high, low, 
               calculatedStopTicks, calculatedStopPoints
        FROM BarsOnTheFlowStateAndBar 
        WHERE barIndex = 3344
    """)
    
    bar_3344 = cur_bars.fetchone()
    conn_bars.close()
    
    if bar_3344:
        bar_idx, bar_time, volume, high, low, calc_ticks, calc_points = bar_3344
        bar_range = high - low
        
        print(f"BAR 3344 DETAILS:")
        print(f"  Time: {bar_time}")
        print(f"  Volume: {volume}")
        print(f"  High: {high:.2f}")
        print(f"  Low: {low:.2f}")
        print(f"  Bar Range: {bar_range:.2f} points")
        print(f"  Calculated Stop: {calc_points:.2f} points ({calc_ticks} ticks)")
        
        # Parse hour from timestamp
        try:
            # Try ISO format first
            dt = datetime.fromisoformat(bar_time.replace('T', ' ').split('.')[0])
            hour = dt.hour
        except:
            try:
                dt = datetime.strptime(bar_time.split('.')[0], '%Y-%m-%d %H:%M:%S')
                hour = dt.hour
            except:
                hour = None
        
        print(f"  Hour: {hour}")
        print()
    else:
        print("Bar 3344 not found in database")
        hour = None
        volume = 0
    
    # Now check volatility database
    conn_vol = sqlite3.connect(db_path)
    cur_vol = conn_vol.cursor()
    
    # Check if table exists
    cur_vol.execute("SELECT name FROM sqlite_master WHERE type='table'")
    tables = cur_vol.fetchall()
    
    print("="*140)
    print("VOLATILITY DATABASE STATUS:")
    print("="*140)
    
    if not tables:
        print("❌ No tables found in volatility.db")
        print("   API will return default: 16 ticks (4 points)")
        conn_vol.close()
        exit()
    
    print(f"✅ Tables found: {[t[0] for t in tables]}")
    print()
    
    # Get stats for the hour
    if hour is not None:
        cur_vol.execute("""
            SELECT hour_of_day, avg_bar_range, avg_volume, avg_range_per_1k_volume, sample_count
            FROM volatility_stats
            WHERE hour_of_day = ? AND symbol = 'MNQ' AND day_of_week IS NULL
        """, (hour,))
        
        stats = cur_vol.fetchone()
        
        print(f"VOLATILITY STATS FOR HOUR {hour}:")
        print("-"*140)
        
        if not stats:
            print(f"❌ No data found for hour {hour}")
            print("   API will return default: 16 ticks (4 points)")
        else:
            hour_val, avg_range, avg_volume, avg_range_per_vol, sample_count = stats
            
            print(f"  Sample Count: {sample_count} bars")
            print(f"  Avg Bar Range: {avg_range:.2f} points")
            print(f"  Avg Volume: {avg_volume:.0f}")
            print(f"  Avg Range per 1k Volume: {avg_range_per_vol:.4f}")
            print()
            
            if sample_count < 10:
                print(f"⚠️  WARNING: Only {sample_count} samples (need 10+)")
                print("   API will return default: 16 ticks (4 points)")
            else:
                print("="*140)
                print("API CALCULATION LOGIC:")
                print("="*140)
                print()
                
                # Volume condition
                if volume > 0 and avg_volume > 0:
                    volume_ratio = volume / avg_volume
                    
                    if volume_ratio < 0.7:
                        volume_condition = 'LOW'
                        volume_multiplier = 0.85
                    elif volume_ratio > 1.3:
                        volume_condition = 'HIGH'
                        volume_multiplier = 1.25
                    else:
                        volume_condition = 'NORMAL'
                        volume_multiplier = 1.0
                    
                    print(f"1. VOLUME ANALYSIS:")
                    print(f"   Current Volume: {volume}")
                    print(f"   Average Volume: {avg_volume:.0f}")
                    print(f"   Volume Ratio: {volume_ratio:.2f}x")
                    print(f"   Condition: {volume_condition}")
                    print(f"   Multiplier: {volume_multiplier}x")
                    print()
                else:
                    volume_condition = 'NORMAL'
                    volume_multiplier = 1.0
                    print("1. VOLUME ANALYSIS: NORMAL (no volume data)")
                    print()
                
                # Stop calculation
                print("2. STOP CALCULATION:")
                print(f"   Base: Avg Bar Range = {avg_range:.2f} points")
                print(f"   × 1.2 buffer = {avg_range * 1.2:.2f} points")
                print(f"   × {volume_multiplier} volume adj = {avg_range * 1.2 * volume_multiplier:.2f} points")
                base_stop_points = avg_range * 1.2 * volume_multiplier
                recommended_ticks = int(base_stop_points * 4)
                print(f"   × 4 ticks/point = {recommended_ticks} ticks")
                print()
                
                # Clamping
                clamped_ticks = max(8, min(80, recommended_ticks))
                print("3. CLAMPING:")
                print(f"   Raw: {recommended_ticks} ticks")
                print(f"   Clamped (8-80): {clamped_ticks} ticks ({clamped_ticks/4:.2f} points)")
                print()
                
                # Confidence
                if sample_count >= 100:
                    confidence = 'HIGH'
                elif sample_count >= 30:
                    confidence = 'MEDIUM'
                else:
                    confidence = 'LOW'
                
                print(f"4. CONFIDENCE: {confidence} (based on {sample_count} samples)")
                print()
                
                print("="*140)
                print("COMPARISON:")
                print("-"*140)
                print(f"  API Should Return: {clamped_ticks} ticks ({clamped_ticks/4:.2f} points)")
                print(f"  Actual in DB: {calc_ticks} ticks ({calc_points:.2f} points)")
                
                if calc_ticks == clamped_ticks:
                    print("  ✅ MATCH - Calculation is correct!")
                else:
                    diff = calc_ticks - clamped_ticks
                    print(f"  ⚠️  MISMATCH: Difference of {diff} ticks ({diff/4:.2f} points)")
                    
                    # Check if it might be using multiplier
                    if calc_ticks == int(clamped_ticks * 1.0):
                        print("  Note: DynamicStopMultiplier parameter may be applied on top")
                print()
        
        # Show all hour stats
        print("="*140)
        print("ALL HOUR STATISTICS:")
        print("="*140)
        cur_vol.execute("""
            SELECT hour_of_day, avg_bar_range, avg_volume, sample_count
            FROM volatility_stats
            WHERE symbol = 'MNQ' AND day_of_week IS NULL
            ORDER BY hour_of_day
        """)
        
        all_stats = cur_vol.fetchall()
        
        if all_stats:
            print(f"{'Hour':<6} {'Avg Range':<12} {'Avg Volume':<12} {'Samples':<10} {'Recommended Stop':<20}")
            print("-"*140)
            for stat in all_stats:
                h, avg_r, avg_v, samp = stat
                base_stop = avg_r * 1.2 * 1.0  # Normal volume
                ticks = int(base_stop * 4)
                ticks_clamped = max(8, min(80, ticks))
                marker = " ← Current" if h == hour else ""
                print(f"{h:<6} {avg_r:<12.2f} {avg_v:<12.0f} {samp:<10} {ticks_clamped} ticks ({ticks_clamped/4:.1f} pts){marker}")
        else:
            print("No hourly statistics found")
    
    conn_vol.close()
    
    print()
    print("="*140)
    print("VERDICT:")
    print("="*140)
    print()
    print("The API calculation appears to be:")
    print("  Formula: (Avg Bar Range × 1.2 buffer × Volume Multiplier) × 4 ticks/point")
    print("  Clamped: Between 8-80 ticks (2-20 points)")
    print()
    print("This is REASONABLE because:")
    print("  ✅ Uses historical average range for the specific hour")
    print("  ✅ Adds 20% buffer for safety")
    print("  ✅ Adjusts for current volume conditions (±15-25%)")
    print("  ✅ Prevents extreme values with clamping")
    print()
    print("HOWEVER, verify if 1.2x buffer is appropriate for your strategy.")
    print("You might want 1.5x or 2.0x depending on your risk tolerance.")
    print("="*140)

except Exception as e:
    print(f"Error: {e}")
    import traceback
    traceback.print_exc()
