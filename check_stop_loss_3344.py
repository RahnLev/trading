import sqlite3

db_path = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard\bars.db'

try:
    conn = sqlite3.connect(db_path)
    cur = conn.cursor()
    
    query = """
    SELECT barIndex, barTime, positionMarketPosition, positionQuantity, 
           open, high, low, close, candleType,
           stopLossPoints, calculatedStopTicks, calculatedStopPoints,
           useTrailingStop, useDynamicStopLoss,
           positionAveragePrice, unrealizedPnL
    FROM BarsOnTheFlowStateAndBar 
    WHERE barIndex BETWEEN 3343 AND 3356 
    ORDER BY barIndex
    """
    
    cur.execute(query)
    rows = cur.fetchall()
    
    print("="*150)
    print("STOP LOSS ANALYSIS: Bar 3344-3356")
    print("="*150)
    
    if not rows:
        print("No data found")
        conn.close()
        exit()
    
    # Get entry info
    entry_bar = None
    entry_price = None
    for row in rows:
        if row[2] == 'Long' and row[3] > 0:
            entry_bar = row[0]
            entry_price = row[14]  # positionAveragePrice
            break
    
    print(f"Entry Bar: {entry_bar}, Entry Price: {entry_price:.2f}" if entry_price else "Entry info not found")
    print()
    
    # Stop loss settings
    stop_points = rows[0][9]
    calc_stop_ticks = rows[0][10]
    calc_stop_points = rows[0][11]
    use_trailing = rows[0][12]
    use_dynamic = rows[0][13]
    
    print(f"Stop Loss Settings:")
    print(f"  StopLossPoints: {stop_points}")
    print(f"  CalculatedStopTicks: {calc_stop_ticks}")
    print(f"  CalculatedStopPoints: {calc_stop_points}")
    print(f"  UseTrailingStop: {use_trailing}")
    print(f"  UseDynamicStopLoss: {use_dynamic}")
    print()
    
    if entry_price and calc_stop_points:
        stop_price = entry_price - calc_stop_points
        print(f"Calculated Stop Price: {stop_price:.2f} (Entry {entry_price:.2f} - {calc_stop_points:.2f} points)")
    else:
        stop_price = None
        print("Could not calculate stop price")
    
    print("="*150)
    print(f"{'Bar':<6} {'Pos':<6} {'Qty':<4} {'Open':<10} {'High':<10} {'Low':<10} {'Close':<10} {'Candle':<6} {'StopPrice':<10} {'HitStop?':<10} {'UnrealPnL':<10}")
    print("-"*150)
    
    for row in rows:
        bar_idx = row[0]
        pos = row[2] if row[2] else 'Flat'
        qty = row[3] if row[3] else 0
        open_val = f"{row[4]:.2f}" if row[4] else 'N/A'
        high_val = f"{row[5]:.2f}" if row[5] else 'N/A'
        low_val = f"{row[6]:.2f}" if row[6] else 'N/A'
        close_val = f"{row[7]:.2f}" if row[7] else 'N/A'
        candle = row[8] if row[8] else 'N/A'
        unrealized = f"{row[15]:.2f}" if row[15] is not None else 'N/A'
        
        # Check if stop was hit
        hit_stop = ''
        if pos == 'Long' and stop_price and row[6]:  # Check low
            if row[6] <= stop_price:
                hit_stop = '❌ YES'
            else:
                hit_stop = 'No'
        elif pos == 'Flat' and bar_idx == 3355:
            hit_stop = '(Exited)'
        
        stop_str = f"{stop_price:.2f}" if stop_price and pos == 'Long' else 'N/A'
        
        print(f"{bar_idx:<6} {pos:<6} {qty:<4} {open_val:<10} {high_val:<10} {low_val:<10} {close_val:<10} {candle:<6} {stop_str:<10} {hit_stop:<10} {unrealized:<10}")
    
    print("="*150)
    print("\nCONCLUSION:")
    
    # Check if any bar hit the stop
    stop_hit = False
    if stop_price:
        for row in rows:
            if row[2] == 'Long' and row[6] and row[6] <= stop_price:
                print(f"❌ STOP LOSS HIT at bar {row[0]}: Low={row[6]:.2f} <= Stop={stop_price:.2f}")
                stop_hit = True
                break
    
    if not stop_hit:
        print(f"✅ Stop loss was NOT hit (Stop at {stop_price:.2f})")
        print(f"   The exit at bar 3355 was due to TREND BREAK, not stop loss")
        
        # Find the lowest low
        lowest_low = min(row[6] for row in rows if row[2] == 'Long' and row[6])
        lowest_bar = [row[0] for row in rows if row[2] == 'Long' and row[6] == lowest_low][0]
        if stop_price:
            distance = lowest_low - stop_price
            print(f"   Lowest point: {lowest_low:.2f} at bar {lowest_bar} ({distance:.2f} points above stop)")
    
    conn.close()
    
except Exception as e:
    print(f"Error: {e}")
    import traceback
    traceback.print_exc()
