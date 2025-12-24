import sqlite3

db_path = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard\bars.db'

try:
    conn = sqlite3.connect(db_path)
    cur = conn.cursor()
    
    # Get the most recent 50 bars
    query = """
    SELECT barIndex, positionMarketPosition, positionQuantity, 
           open, high, low, close, candleType,
           positionAveragePrice, calculatedStopPoints,
           exitOnTrendBreak, reverseOnTrendBreak,
           trendLookbackBars, minConsecutiveBars,
           lastEntryBarIndex
    FROM BarsOnTheFlowStateAndBar 
    ORDER BY barIndex DESC
    LIMIT 50
    """
    
    cur.execute(query)
    rows = cur.fetchall()
    
    if not rows:
        print("No data found")
        conn.close()
        exit()
    
    rows = list(reversed(rows))
    
    print("="*140)
    print("SHORT TRADE DURATION ANALYSIS")
    print("="*140)
    print()
    
    # Track trades
    trades = []
    current_trade = None
    
    for i, row in enumerate(rows):
        bar_idx = row[0]
        pos = row[1]
        qty = row[2]
        entry_price = row[8]
        last_entry = row[14]
        
        # Detect trade start
        if pos != 'Flat' and (i == 0 or rows[i-1][1] == 'Flat'):
            current_trade = {
                'entry_bar': bar_idx,
                'entry_price': entry_price,
                'position': pos,
                'bars': []
            }
        
        # Track bars in trade
        if current_trade and pos != 'Flat':
            current_trade['bars'].append(row)
        
        # Detect trade end
        if current_trade and pos == 'Flat' and rows[i-1][1] != 'Flat':
            current_trade['exit_bar'] = bar_idx
            current_trade['duration'] = bar_idx - current_trade['entry_bar']
            trades.append(current_trade)
            current_trade = None
    
    # Add ongoing trade if exists
    if current_trade:
        current_trade['exit_bar'] = rows[-1][0]
        current_trade['duration'] = rows[-1][0] - current_trade['entry_bar']
        current_trade['ongoing'] = True
        trades.append(current_trade)
    
    print(f"Found {len(trades)} trades in last {len(rows)} bars")
    print()
    
    # Analyze each trade
    for i, trade in enumerate(trades, 1):
        print("="*140)
        print(f"TRADE #{i}: {trade['position']} from bar {trade['entry_bar']} to {trade['exit_bar']}")
        print(f"Duration: {trade['duration']} bars {'(ONGOING)' if trade.get('ongoing') else ''}")
        print("="*140)
        
        # Get 5 bars before entry to see setup
        setup_start = trade['entry_bar'] - 5
        cur.execute("""
            SELECT barIndex, candleType, close
            FROM BarsOnTheFlowStateAndBar 
            WHERE barIndex BETWEEN ? AND ?
            ORDER BY barIndex
        """, (setup_start, trade['entry_bar'] - 1))
        
        setup_bars = cur.fetchall()
        if setup_bars:
            print(f"Setup (5 bars before entry):")
            candles_before = [f"{r[0]}:{r[1]}" for r in setup_bars]
            print(f"  {' -> '.join(candles_before)}")
            print()
        
        # Show each bar in the trade
        print(f"{'Bar':<6} {'Pos':<6} {'Entry':<10} {'Open':<10} {'High':<10} {'Low':<10} {'Close':<10} {'Candle':<6} {'Stop':<10}")
        print("-"*140)
        
        for bar_row in trade['bars']:
            bar, pos, qty, o, h, l, c, candle, entry, stop_pts = bar_row[:10]
            
            if entry > 0 and stop_pts > 0:
                if pos == 'Long':
                    stop_price = entry - stop_pts
                elif pos == 'Short':
                    stop_price = entry + stop_pts
                else:
                    stop_price = 0
            else:
                stop_price = 0
            
            stop_str = f"{stop_price:.2f}" if stop_price > 0 else 'N/A'
            print(f"{bar:<6} {pos:<6} {entry:<10.2f} {o:<10.2f} {h:<10.2f} {l:<10.2f} {c:<10.2f} {candle:<6} {stop_str:<10}")
        
        # Analyze exit
        if not trade.get('ongoing'):
            print()
            print("EXIT ANALYSIS:")
            
            # Get trend window at exit
            exit_bar = trade['exit_bar']
            lookback = trade['bars'][0][12]  # trendLookbackBars
            min_consec = trade['bars'][0][13]  # minConsecutiveBars
            
            window_start = exit_bar - lookback
            cur.execute("""
                SELECT barIndex, candleType
                FROM BarsOnTheFlowStateAndBar 
                WHERE barIndex BETWEEN ? AND ?
                ORDER BY barIndex
            """, (window_start, exit_bar - 1))
            
            trend_bars = cur.fetchall()
            candles = [r[1] for r in trend_bars]
            good_count = sum(1 for c in candles if c == 'good')
            bad_count = sum(1 for c in candles if c == 'bad')
            
            print(f"  Lookback window: {candles}")
            print(f"  Good: {good_count}, Bad: {bad_count} (need {min_consec}+ for trend)")
            
            if trade['position'] == 'Long' and bad_count >= min_consec:
                print(f"  → TrendDown detected ({bad_count} bad) - EXIT LONG")
            elif trade['position'] == 'Short' and good_count >= min_consec:
                print(f"  → TrendUp detected ({good_count} good) - EXIT SHORT")
            
            # Check if stop was hit
            last_bar = trade['bars'][-1]
            entry_price = last_bar[8]
            stop_pts = last_bar[9]
            
            if entry_price > 0 and stop_pts > 0:
                # Get exit bar details
                cur.execute("""
                    SELECT high, low, close
                    FROM BarsOnTheFlowStateAndBar 
                    WHERE barIndex = ?
                """, (exit_bar,))
                
                exit_details = cur.fetchone()
                if exit_details:
                    h, l, c = exit_details
                    
                    if trade['position'] == 'Long':
                        stop_price = entry_price - stop_pts
                        if l <= stop_price:
                            print(f"  ⚠️  STOP LOSS HIT: Low {l:.2f} <= Stop {stop_price:.2f}")
                    elif trade['position'] == 'Short':
                        stop_price = entry_price + stop_pts
                        if h >= stop_price:
                            print(f"  ⚠️  STOP LOSS HIT: High {h:.2f} >= Stop {stop_price:.2f}")
        
        print()
    
    # Summary statistics
    print("="*140)
    print("SUMMARY STATISTICS")
    print("="*140)
    
    durations = [t['duration'] for t in trades if not t.get('ongoing')]
    if durations:
        avg_duration = sum(durations) / len(durations)
        min_duration = min(durations)
        max_duration = max(durations)
        
        print(f"  Total Trades: {len(trades)}")
        print(f"  Average Duration: {avg_duration:.1f} bars")
        print(f"  Shortest: {min_duration} bars")
        print(f"  Longest: {max_duration} bars")
        print()
        
        one_bar_trades = sum(1 for d in durations if d <= 1)
        two_bar_trades = sum(1 for d in durations if d <= 2)
        
        print(f"  1-bar trades: {one_bar_trades} ({one_bar_trades/len(durations)*100:.0f}%)")
        print(f"  2-bar trades: {two_bar_trades} ({two_bar_trades/len(durations)*100:.0f}%)")
        
        if avg_duration < 3:
            print()
            print("  ⚠️  WARNING: Trades are VERY short (avg < 3 bars)")
            print()
            print("  LIKELY CAUSES:")
            print("  1. ExitOnTrendBreak is TOO SENSITIVE")
            print("     - Current: 3 candles in 5-bar window triggers exit")
            print("     - Solution: Increase MinConsecutiveBars to 4")
            print("     - OR: Increase TrendLookbackBars to 7+")
            print()
            print("  2. Stop loss is TOO TIGHT")
            print("     - Check if stops are getting hit immediately")
            print("     - 20-point max clamp may be too small for volatile hours")
            print()
            print("  3. ExitIfEntryBarOpposite is triggering")
            print("     - Exits if entry bar closes opposite to trade direction")
            print("     - Consider disabling this feature")
    
    conn.close()
    
except Exception as e:
    print(f"Error: {e}")
    import traceback
    traceback.print_exc()
