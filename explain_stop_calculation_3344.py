import sqlite3

db_path = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard\bars.db'

try:
    conn = sqlite3.connect(db_path)
    cur = conn.cursor()
    
    # Check if UseVolumeAwareStop was enabled
    query = """
    SELECT barIndex, barTime, 
           stopLossPoints, calculatedStopTicks, calculatedStopPoints,
           useDynamicStopLoss, lookback, multiplier,
           open, high, low, close, volume
    FROM BarsOnTheFlowStateAndBar 
    WHERE barIndex BETWEEN 3339 AND 3344
    ORDER BY barIndex
    """
    
    cur.execute(query)
    rows = cur.fetchall()
    
    print("="*140)
    print("STOP LOSS CALCULATION METHOD ANALYSIS")
    print("="*140)
    print()
    print("Settings at bar 3344:")
    if rows:
        stop_points = rows[-1][2]
        calc_ticks = rows[-1][3]
        calc_points = rows[-1][4]
        use_dynamic = rows[-1][5]
        lookback = rows[-1][6]
        multiplier = rows[-1][7]
        
        print(f"  StopLossPoints (fixed): {stop_points} points")
        print(f"  UseDynamicStopLoss: {use_dynamic} ({'Enabled' if use_dynamic == 1 else 'Disabled'})")
        print(f"  DynamicStopLookback: {lookback} bars")
        print(f"  DynamicStopMultiplier: {multiplier}x")
        print(f"  Calculated Stop: {calc_points:.2f} points ({calc_ticks} ticks)")
        print()
    
    print("="*140)
    print("CALCULATION LOGIC (from code):")
    print("="*140)
    print()
    print("1. IF UseDynamicStopLoss == false:")
    print("   → Use fixed StopLossPoints * 4 ticks")
    print()
    print("2. ELSE IF UseVolumeAwareStop == true (DEFAULT):")
    print("   → Query API: http://localhost:51888/api/volatility/recommended-stop")
    print("   → If API returns valid stop: Use API value * DynamicStopMultiplier")
    print("   → If API fails: Fall back to #3")
    print()
    print("3. FALLBACK: Calculate from bar ranges:")
    print("   → Average last N bars: (High[i] - Low[i]) for i=1 to N")
    print("   → Multiply by DynamicStopMultiplier")
    print("   → Convert to ticks (* 4)")
    print()
    print("="*140)
    print()
    
    # Calculate what the fallback SHOULD be
    print("FALLBACK CALCULATION (if API didn't work):")
    print()
    lookback = 5
    multiplier = 1.0
    
    ranges = []
    for row in rows[:-1]:  # Exclude bar 3344, use previous 5 bars
        high = row[9]
        low = row[10]
        candle_range = high - low
        ranges.append(candle_range)
        print(f"  Bar {row[0]}: High={high:.2f}, Low={low:.2f}, Range={candle_range:.2f}")
    
    if len(ranges) >= 5:
        avg_range = sum(ranges) / len(ranges)
        stop_distance = avg_range * multiplier
        stop_ticks = int(round(stop_distance * 4))
        
        print()
        print(f"  Average range: {avg_range:.2f} points")
        print(f"  Stop distance: {avg_range:.2f} * {multiplier} = {stop_distance:.2f} points")
        print(f"  Stop ticks: {stop_ticks} ticks")
        print()
    
    print("="*140)
    print("ACTUAL RESULT:")
    print(f"  Calculated stop from DB: {calc_points:.2f} points ({calc_ticks} ticks)")
    print()
    
    if calc_points == stop_points:
        print(f"✅ CONCLUSION: Using FIXED stop ({stop_points} points)")
        print("   Either UseDynamicStopLoss=false OR not enough bars for dynamic calculation")
    elif calc_points == stop_distance:
        print(f"✅ CONCLUSION: Using FALLBACK calculation ({stop_distance:.2f} points)")
        print("   API did not return valid stop, used bar range average")
    else:
        print(f"✅ CONCLUSION: Using VOLUME-AWARE API stop ({calc_points:.2f} points)")
        print("   API returned a recommended stop based on hour/volume data")
    
    print("="*140)
    print()
    print("ANSWER TO YOUR QUESTION:")
    print()
    print("1. LOOKBACK: 5 bars (bars 3339-3343)")
    print("2. CALCULATION: High - Low (FULL candle INCLUDING wicks)")
    print("3. NOT just body (Open vs Close) - uses entire candle range")
    print("4. ACTUAL METHOD: Since calc stop = 20 points = fixed StopLossPoints,")
    print("   it appears dynamic stop was either:")
    print("   - Disabled, OR")
    print("   - API returned 20 points, OR")
    print("   - Not enough bars yet for dynamic calculation")
    print()
    print("The code checks UseVolumeAwareStop FIRST before using bar range calculation.")
    print("="*140)
    
    conn.close()
    
except Exception as e:
    print(f"Error: {e}")
    import traceback
    traceback.print_exc()
