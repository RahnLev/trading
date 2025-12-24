import sqlite3

db_path = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard\bars.db'

try:
    conn = sqlite3.connect(db_path)
    cur = conn.cursor()
    
    # Get the last 30 bars to see the pattern
    query = """
    SELECT barIndex, barTime, positionMarketPosition, positionQuantity, intendedPosition,
           open, high, low, close, candleType,
           positionAveragePrice, unrealizedPnL, calculatedStopPoints,
           lastEntryBarIndex
    FROM BarsOnTheFlowStateAndBar 
    ORDER BY barIndex DESC
    LIMIT 30
    """
    
    cur.execute(query)
    rows = cur.fetchall()
    
    if not rows:
        print("No data found in bars.db")
        conn.close()
        exit()
    
    # Reverse to show chronological order
    rows = list(reversed(rows))
    
    print("="*160)
    print("RECENT TRADING ACTIVITY ANALYSIS")
    print("="*160)
    print()
    print(f"Showing last {len(rows)} bars:")
    print()
    print(f"{'Bar':<6} {'Time':<8} {'Pos':<6} {'Qty':<4} {'Intent':<6} {'Entry':<10} {'Close':<10} {'Low':<10} {'Stop':<10} {'PnL':<8} {'LastEntry':<10} {'Candle':<6}")
    print("-"*160)
    
    entry_count = 0
    exit_count = 0
    last_pos = None
    rapid_entries = []
    
    for i, row in enumerate(rows):
        bar_idx = row[0]
        bar_time = row[1][-8:] if row[1] else 'N/A'
        pos = row[2] if row[2] else 'Flat'
        qty = row[3] if row[3] else 0
        intent = row[4] if row[4] else 'Flat'
        open_val = row[5]
        high_val = row[6]
        low_val = row[7]
        close_val = row[8]
        candle = row[9] if row[9] else 'N/A'
        entry_price = row[10] if row[10] else 0
        pnl = row[11] if row[11] else 0
        stop_points = row[12] if row[12] else 0
        last_entry_bar = row[13] if row[13] else 'N/A'
        
        # Calculate where stop would be
        if entry_price > 0 and stop_points > 0:
            if pos == 'Long':
                stop_price = entry_price - stop_points
            elif pos == 'Short':
                stop_price = entry_price + stop_points
            else:
                stop_price = 0
        else:
            stop_price = 0
        
        # Detect entry
        if pos != 'Flat' and last_pos == 'Flat':
            entry_count += 1
        
        # Detect exit
        if pos == 'Flat' and last_pos != 'Flat' and last_pos is not None:
            exit_count += 1
            
            # Check if rapid re-entry (entry within 3 bars)
            if i > 0 and i < len(rows) - 3:
                next_bars = rows[i+1:i+4]
                for next_bar in next_bars:
                    if next_bar[2] != 'Flat':
                        rapid_entries.append((bar_idx, next_bar[0]))
                        break
        
        last_pos = pos
        
        stop_str = f"{stop_price:.2f}" if stop_price > 0 else 'N/A'
        
        marker = ''
        if pos != 'Flat' and last_entry_bar != 'N/A' and bar_idx == last_entry_bar:
            marker = ' ← ENTRY'
        elif pos == 'Flat' and i > 0 and rows[i-1][2] != 'Flat':
            marker = ' ← EXIT'
        
        print(f"{bar_idx:<6} {bar_time:<8} {pos:<6} {qty:<4} {intent:<6} {entry_price:<10.2f} {close_val:<10.2f} {low_val:<10.2f} {stop_str:<10} {pnl:<8.2f} {last_entry_bar:<10} {candle:<6} {marker}")
    
    print("="*160)
    print()
    print("STATISTICS:")
    print(f"  Total Entries: {entry_count}")
    print(f"  Total Exits: {exit_count}")
    print(f"  Entry/Exit Ratio: {entry_count}/{exit_count}")
    
    if entry_count > len(rows) * 0.5:
        print(f"  ⚠️  WARNING: {entry_count} entries in {len(rows)} bars = {entry_count/len(rows)*100:.0f}% of bars!")
        print("     This is excessive - entering almost every bar")
    
    if rapid_entries:
        print()
        print(f"  ⚠️  RAPID RE-ENTRIES DETECTED: {len(rapid_entries)} times")
        for exit_bar, reentry_bar in rapid_entries:
            print(f"     Exit at bar {exit_bar}, re-entered at bar {reentry_bar} ({reentry_bar - exit_bar} bars later)")
    
    print()
    print("="*160)
    print("POTENTIAL ISSUES:")
    print("="*160)
    
    # Analyze stop loss hits
    stop_hit_count = 0
    for i in range(1, len(rows)):
        prev_row = rows[i-1]
        curr_row = rows[i]
        
        prev_pos = prev_row[2]
        curr_pos = curr_row[2]
        entry_price = prev_row[10]
        low = curr_row[7]
        high = curr_row[6]
        stop_points = prev_row[12]
        
        # Check if position exited
        if prev_pos != 'Flat' and curr_pos == 'Flat':
            # Check if stop was hit
            if entry_price > 0 and stop_points > 0:
                if prev_pos == 'Long':
                    stop_price = entry_price - stop_points
                    if low <= stop_price:
                        stop_hit_count += 1
                        print(f"  Bar {curr_row[0]}: STOP HIT - Long at {entry_price:.2f}, stop {stop_price:.2f}, low {low:.2f}")
                elif prev_pos == 'Short':
                    stop_price = entry_price + stop_points
                    if high >= stop_price:
                        stop_hit_count += 1
                        print(f"  Bar {curr_row[0]}: STOP HIT - Short at {entry_price:.2f}, stop {stop_price:.2f}, high {high:.2f}")
    
    if stop_hit_count > 0:
        print()
        print(f"  ⚠️  STOP LOSS HIT {stop_hit_count} times in {len(rows)} bars")
        print(f"     Stop hit rate: {stop_hit_count/exit_count*100:.0f}% of exits" if exit_count > 0 else "")
        print()
        print("  LIKELY CAUSES:")
        print("  1. Stop loss too tight (20 points may not be enough)")
        print("  2. Break-even activated too early (5 points) then gave back to entry+2")
        print("  3. High volatility period (check hour - afternoon is 30-40 point average)")
        print("  4. Entering on wrong signals (check trend detection)")
    
    print()
    print("="*160)
    print("RECOMMENDATIONS:")
    print("="*160)
    print("1. Check which hour this data is from (afternoon hours need wider stops)")
    print("2. Increase BreakEvenTrigger from 5 to 8-10 points (give trades more room)")
    print("3. Consider increasing stop clamp from 80 ticks (20 pts) to 120-160 ticks (30-40 pts)")
    print("4. Review trend detection - may be entering on marginal signals")
    print("="*160)
    
    conn.close()
    
except Exception as e:
    print(f"Error: {e}")
    import traceback
    traceback.print_exc()
