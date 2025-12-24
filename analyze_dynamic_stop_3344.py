import sqlite3

db_path = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard\bars.db'

try:
    conn = sqlite3.connect(db_path)
    cur = conn.cursor()
    
    # Get the dynamic stop settings and bar data around 3344
    query = """
    SELECT barIndex, open, high, low, close, 
           stopLossPoints, calculatedStopTicks, calculatedStopPoints,
           useDynamicStopLoss, lookback AS dynamicStopLookback, multiplier AS dynamicStopMultiplier,
           positionAveragePrice, positionMarketPosition
    FROM BarsOnTheFlowStateAndBar 
    WHERE barIndex BETWEEN 3338 AND 3344
    ORDER BY barIndex
    """
    
    cur.execute(query)
    rows = cur.fetchall()
    
    if not rows:
        print("No data found for bars 3338-3344")
        conn.close()
        exit()
    
    print("="*140)
    print("DYNAMIC STOP LOSS CALCULATION ANALYSIS: Bar 3344")
    print("="*140)
    
    # Get settings from first row
    use_dynamic = rows[0][8]
    lookback = rows[0][9] if rows[0][9] else 5
    multiplier = rows[0][10] if rows[0][10] else 1.0
    
    print(f"\nDYNAMIC STOP SETTINGS:")
    print(f"  UseDynamicStopLoss: {use_dynamic} ({'Enabled' if use_dynamic == 1 else 'Disabled'})")
    print(f"  DynamicStopLookback: {lookback} bars")
    print(f"  DynamicStopMultiplier: {multiplier}x")
    print()
    
    print("="*140)
    print(f"{'Bar':<6} {'Open':<10} {'High':<10} {'Low':<10} {'Close':<10} {'Range':<10} {'Body':<10} {'CalcStop':<12} {'Position':<8} {'EntryPrice':<10}")
    print("-"*140)
    
    ranges = []
    for i, row in enumerate(rows):
        bar_idx = row[0]
        open_val = row[1]
        high_val = row[2]
        low_val = row[3]
        close_val = row[4]
        calc_stop_points = row[7]
        pos = row[12] if row[12] else 'Flat'
        entry_price = row[11] if row[11] else 0
        
        # Calculate range (High - Low) = full candle INCLUDING wicks
        candle_range = high_val - low_val if high_val and low_val else 0
        
        # Calculate body (abs(Close - Open)) = just the body, NO wicks
        body_size = abs(close_val - open_val) if close_val and open_val else 0
        
        ranges.append(candle_range)
        
        print(f"{bar_idx:<6} {open_val:<10.2f} {high_val:<10.2f} {low_val:<10.2f} {close_val:<10.2f} {candle_range:<10.2f} {body_size:<10.2f} {calc_stop_points:<12.2f} {pos:<8} {entry_price:<10.2f}")
    
    print("="*140)
    
    # Now show the calculation for bar 3344
    bar_3344_idx = next((i for i, row in enumerate(rows) if row[0] == 3344), None)
    
    if bar_3344_idx is not None and bar_3344_idx >= lookback:
        print(f"\nDYNAMIC STOP CALCULATION FOR BAR 3344:")
        print(f"Looking back {lookback} bars from bar 3344 (bars {3344-lookback} to {3344-1}):")
        print()
        
        # Get the lookback window BEFORE bar 3344
        lookback_start = bar_3344_idx - lookback
        lookback_end = bar_3344_idx
        lookback_ranges = ranges[lookback_start:lookback_end]
        
        print(f"Bars used in calculation (previous {lookback} bars):")
        for i, r in enumerate(lookback_ranges):
            bar_num = 3344 - lookback + i
            print(f"  Bar {bar_num}: Range = {r:.2f} points (High - Low)")
        
        print()
        avg_range = sum(lookback_ranges) / len(lookback_ranges)
        stop_distance = avg_range * multiplier
        stop_ticks = int(round(stop_distance * 4))  # Convert points to ticks
        
        print(f"Calculation:")
        print(f"  1. Sum of ranges: {sum(lookback_ranges):.2f} points")
        print(f"  2. Average range: {sum(lookback_ranges):.2f} / {len(lookback_ranges)} = {avg_range:.2f} points")
        print(f"  3. Apply multiplier: {avg_range:.2f} × {multiplier} = {stop_distance:.2f} points")
        print(f"  4. Convert to ticks: {stop_distance:.2f} × 4 = {stop_ticks} ticks")
        print(f"  5. Final stop distance: {stop_ticks / 4:.2f} points ({stop_ticks} ticks)")
        print()
        
        # Compare with actual calculated stop from DB
        actual_stop = rows[bar_3344_idx][7]
        print(f"Calculated stop from DB: {actual_stop:.2f} points")
        print(f"Our calculation: {stop_distance:.2f} points")
        if abs(actual_stop - stop_distance) < 0.1:
            print("✅ Match!")
        else:
            print(f"⚠️ Difference: {abs(actual_stop - stop_distance):.2f} points")
        
        # Show where the stop would be placed
        entry_price = rows[bar_3344_idx][11]
        if entry_price:
            stop_price = entry_price - stop_distance
            print()
            print(f"STOP PLACEMENT:")
            print(f"  Entry Price: {entry_price:.2f}")
            print(f"  Stop Distance: {stop_distance:.2f} points")
            print(f"  Stop Price: {stop_price:.2f}")
    
    print()
    print("="*140)
    print("KEY INSIGHTS:")
    print("1. Dynamic stop uses HIGH - LOW (full candle range INCLUDING wicks)")
    print("2. NOT just the body (Close - Open) - the entire candle range is used")
    print("3. Averages the last N bars (DynamicStopLookback parameter)")
    print("4. Multiplies by DynamicStopMultiplier for adjustment (e.g., 1.5x for wider stops)")
    print("5. Converts points to ticks (4 ticks per point for MNQ)")
    print("="*140)
    
    conn.close()
    
except Exception as e:
    print(f"Error: {e}")
    import traceback
    traceback.print_exc()
