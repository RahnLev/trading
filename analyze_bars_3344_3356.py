import sqlite3
import sys

db_path = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard\bars.db'

try:
    conn = sqlite3.connect(db_path)
    cur = conn.cursor()
    
    query = """
    SELECT barIndex, barTime, positionMarketPosition, positionQuantity, intendedPosition, 
           candleType, close, exitOnTrendBreak, reverseOnTrendBreak, 
           trendLookbackBars, minConsecutiveBars, usePnLTiebreaker,
           lastEntryBarIndex, lastEntryDirection
    FROM BarsOnTheFlowStateAndBar 
    WHERE barIndex BETWEEN 3344 AND 3356 
    ORDER BY barIndex
    """
    
    cur.execute(query)
    rows = cur.fetchall()
    
    print("="*120)
    print("TRADE ANALYSIS: Entry Bar 3344, Exit Bar 3356")
    print("="*120)
    print(f"{'Bar':<6} {'Time':<8} {'Pos':<6} {'Qty':<4} {'Intent':<6} {'Candle':<5} {'Close':<8} {'ExitTrend':<9} {'EntryBar':<9} {'EntryDir':<9}")
    print("-"*120)
    
    for row in rows:
        bar_idx = row[0]
        bar_time = row[1][-8:] if row[1] else 'N/A'  # Last 8 chars (HH:MM:SS)
        pos = row[2] if row[2] else 'Flat'
        qty = row[3] if row[3] else 0
        intent = row[4] if row[4] else 'Flat'
        candle = row[5] if row[5] else 'N/A'
        close_val = f"{row[6]:.2f}" if row[6] else 'N/A'
        exit_trend = 'Yes' if row[7] == 1 else 'No'
        entry_bar = row[12] if row[12] else 'N/A'
        entry_dir = row[13] if row[13] else 'N/A'
        
        print(f"{bar_idx:<6} {bar_time:<8} {pos:<6} {qty:<4} {intent:<6} {candle:<5} {close_val:<8} {exit_trend:<9} {entry_bar:<9} {entry_dir:<9}")
    
    print("="*120)
    print("\nTREND SETTINGS:")
    if rows:
        print(f"  ExitOnTrendBreak: {rows[0][7]}")
        print(f"  ReverseOnTrendBreak: {rows[0][8]}")
        print(f"  TrendLookbackBars: {rows[0][9]}")
        print(f"  MinConsecutiveBars: {rows[0][10]}")
        print(f"  UsePnLTiebreaker: {rows[0][11]}")
    
    conn.close()
    
except Exception as e:
    print(f"Error: {e}")
    import traceback
    traceback.print_exc()
