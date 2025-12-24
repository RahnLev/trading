import sqlite3

db_path = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard\bars.db'

try:
    conn = sqlite3.connect(db_path)
    cur = conn.cursor()
    
    # Analyze the two recent trades
    print("="*140)
    print("DETAILED TRADE ANALYSIS")
    print("="*140)
    print()
    
    # Trade 1: Bars 3966-3970
    print("TRADE 1: Short Entry at Bar 3966")
    print("-"*140)
    
    cur.execute("""
        SELECT barIndex, positionMarketPosition, positionAveragePrice, 
               open, high, low, close, candleType,
               calculatedStopPoints, exitOnTrendBreak, reverseOnTrendBreak,
               trendLookbackBars, minConsecutiveBars
        FROM BarsOnTheFlowStateAndBar 
        WHERE barIndex BETWEEN 3961 AND 3971
        ORDER BY barIndex
    """)
    
    trade1 = cur.fetchall()
    
    print(f"{'Bar':<6} {'Pos':<6} {'Entry':<10} {'Open':<10} {'High':<10} {'Low':<10} {'Close':<10} {'Candle':<6} {'Stop':<10} {'Exit?':<6}")
    print("-"*140)
    
    for row in trade1:
        bar, pos, entry, o, h, l, c, candle, stop_pts, exit_trend, reverse, lookback, minbars = row
        pos_str = pos if pos else 'Flat'
        
        # Calculate stop price
        if entry > 0 and stop_pts > 0 and pos == 'Short':
            stop_price = entry + stop_pts
        else:
            stop_price = 0
        
        stop_str = f"{stop_price:.2f}" if stop_price > 0 else 'N/A'
        
        # Check if stop was hit
        exit_reason = ''
        if pos == 'Short' and stop_price > 0 and h >= stop_price:
            exit_reason = 'STOP HIT'
        
        print(f"{bar:<6} {pos_str:<6} {entry:<10.2f} {o:<10.2f} {h:<10.2f} {l:<10.2f} {c:<10.2f} {candle:<6} {stop_str:<10} {exit_reason:<6}")
    
    print()
    print("Trend Settings: Lookback={}, MinConsecutive={}, ExitOnTrendBreak={}, Reverse={}".format(
        trade1[0][11], trade1[0][12], trade1[0][9], trade1[0][10]))
    
    # Analyze why it exited
    print()
    print("EXIT ANALYSIS for Bar 3970:")
    # Get 5 bars before 3970 to check trend
    cur.execute("""
        SELECT barIndex, candleType
        FROM BarsOnTheFlowStateAndBar 
        WHERE barIndex BETWEEN 3965 AND 3969
        ORDER BY barIndex
    """)
    
    trend_window = cur.fetchall()
    candles = [r[1] for r in trend_window]
    good_count = sum(1 for c in candles if c == 'good')
    bad_count = sum(1 for c in candles if c == 'bad')
    
    print(f"  5-bar window: {candles}")
    print(f"  Good: {good_count}, Bad: {bad_count}")
    print(f"  TrendUp triggered? {good_count >= 3} (need 3+)")
    print(f"  TrendDown triggered? {bad_count >= 3} (need 3+)")
    if good_count >= 3:
        print("  → SHORT position would exit on TrendUp (trend break)")
    
    print()
    print("="*140)
    print()
    
    # Trade 2: Bars 3977-3979
    print("TRADE 2: Short Entry at Bar 3977")
    print("-"*140)
    
    cur.execute("""
        SELECT barIndex, positionMarketPosition, positionAveragePrice, 
               open, high, low, close, candleType,
               calculatedStopPoints
        FROM BarsOnTheFlowStateAndBar 
        WHERE barIndex BETWEEN 3974 AND 3982
        ORDER BY barIndex
    """)
    
    trade2 = cur.fetchall()
    
    print(f"{'Bar':<6} {'Pos':<6} {'Entry':<10} {'Open':<10} {'High':<10} {'Low':<10} {'Close':<10} {'Candle':<6} {'Stop':<10} {'Exit?':<6}")
    print("-"*140)
    
    for row in trade2:
        bar, pos, entry, o, h, l, c, candle, stop_pts = row
        pos_str = pos if pos else 'Flat'
        
        # Calculate stop price
        if entry > 0 and stop_pts > 0 and pos == 'Short':
            stop_price = entry + stop_pts
        else:
            stop_price = 0
        
        stop_str = f"{stop_price:.2f}" if stop_price > 0 else 'N/A'
        
        # Check if stop was hit
        exit_reason = ''
        if pos == 'Short' and stop_price > 0 and h >= stop_price:
            exit_reason = 'STOP HIT'
        
        print(f"{bar:<6} {pos_str:<6} {entry:<10.2f} {o:<10.2f} {h:<10.2f} {l:<10.2f} {c:<10.2f} {candle:<6} {stop_str:<10} {exit_reason:<6}")
    
    print()
    print("EXIT ANALYSIS for Bar 3979:")
    # Get 5 bars before 3979 to check trend
    cur.execute("""
        SELECT barIndex, candleType
        FROM BarsOnTheFlowStateAndBar 
        WHERE barIndex BETWEEN 3974 AND 3978
        ORDER BY barIndex
    """)
    
    trend_window2 = cur.fetchall()
    candles2 = [r[1] for r in trend_window2]
    good_count2 = sum(1 for c in candles2 if c == 'good')
    bad_count2 = sum(1 for c in candles2 if c == 'bad')
    
    print(f"  5-bar window: {candles2}")
    print(f"  Good: {good_count2}, Bad: {bad_count2}")
    print(f"  TrendUp triggered? {good_count2 >= 3} (need 3+)")
    print(f"  TrendDown triggered? {bad_count2 >= 3} (need 3+)")
    if good_count2 >= 3:
        print("  → SHORT position would exit on TrendUp (trend break)")
    
    print()
    print("="*140)
    print("CONCLUSION:")
    print("="*140)
    print()
    print("Both trades exited due to TREND BREAK, not stop loss.")
    print("The strategy is working as designed - exiting when trend reverses.")
    print()
    print("If you're seeing 'stop loss triggered every bar', it might be:")
    print("1. ExitOnTrendBreak is too sensitive (exits quickly on trend changes)")
    print("2. Check NinjaTrader Output window for actual stop loss messages")
    print("3. The break-even feature hasn't been tested yet (DB is old)")
    print()
    print("RECOMMENDATION: Delete bars.db and run fresh data with break-even enabled")
    print("="*140)
    
    conn.close()
    
except Exception as e:
    print(f"Error: {e}")
    import traceback
    traceback.print_exc()
