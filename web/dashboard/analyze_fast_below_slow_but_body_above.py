"""
Analyze cases where FastEMA < SlowEMA but body is above both EMAs.
This helps determine if entries should be allowed in this scenario.
"""
import sqlite3
import os
from datetime import datetime

# Database paths
volatility_db = os.path.join(os.path.dirname(__file__), 'volatility.db')
dashboard_db = os.path.join(os.path.dirname(__file__), 'dashboard.db')

def analyze_fast_below_slow_but_body_above():
    """Find bars where FastEMA < SlowEMA but body is above both EMAs."""
    
    if not os.path.exists(volatility_db):
        print(f"Error: Database not found at {volatility_db}")
        return
    
    conn = sqlite3.connect(volatility_db)
    conn.row_factory = sqlite3.Row
    cur = conn.cursor()
    
    print("=" * 80)
    print("ANALYSIS: FastEMA < SlowEMA but Body Above Both EMAs")
    print("=" * 80)
    print()
    
    # Query for bars where:
    # 1. FastEMA < SlowEMA (no bullish crossover)
    # 2. Body is above both FastEMA and SlowEMA
    # 3. Body is above FastEMA (for long entry consideration)
    
    query = """
    SELECT 
        bar_index,
        timestamp,
        open_price,
        close_price,
        high_price,
        low_price,
        ema_fast_value,
        ema_slow_value,
        fast_ema_grad_deg,
        candle_type,
        in_trade,
        direction,
        entry_reason
    FROM bar_samples
    WHERE 
        ema_fast_value IS NOT NULL 
        AND ema_slow_value IS NOT NULL
        AND ema_fast_value < ema_slow_value  -- FastEMA below SlowEMA
        AND open_price IS NOT NULL
        AND close_price IS NOT NULL
        AND (close_price > ema_fast_value AND open_price > ema_fast_value)  -- Body above FastEMA
        AND (close_price > ema_slow_value AND open_price > ema_slow_value)  -- Body above SlowEMA
    ORDER BY bar_index DESC
    LIMIT 100
    """
    
    cur.execute(query)
    rows = cur.fetchall()
    
    if not rows:
        print("No bars found matching the criteria.")
        conn.close()
        return
    
    print(f"Found {len(rows)} bars where FastEMA < SlowEMA but body is above both EMAs")
    print()
    
    # Check which of these bars had entries
    conn_dashboard = sqlite3.connect(dashboard_db)
    conn_dashboard.row_factory = sqlite3.Row
    cur_dashboard = conn_dashboard.cursor()
    
    # Get all trades
    cur_dashboard.execute("""
        SELECT entry_bar, exit_bar, direction, entry_price, exit_price,
               realized_points, bars_held, entry_reason, exit_reason
        FROM trades
        ORDER BY entry_bar DESC
    """)
    all_trades = cur_dashboard.fetchall()
    
    # Create a map of entry bars
    trades_by_entry_bar = {}
    for trade in all_trades:
        entry_bar = trade['entry_bar']
        if entry_bar is not None:
            if entry_bar not in trades_by_entry_bar:
                trades_by_entry_bar[entry_bar] = []
            trades_by_entry_bar[entry_bar].append(trade)
    
    # Analyze each bar
    bars_with_entries = []
    bars_without_entries = []
    
    for row in rows:
        bar_index = row['bar_index']
        open_price = row['open_price']
        close_price = row['close_price']
        ema_fast = row['ema_fast_value']
        ema_slow = row['ema_slow_value']
        gradient = row['fast_ema_grad_deg']
        
        body_top = max(open_price, close_price)
        body_bottom = min(open_price, close_price)
        
        # Check if there was an entry on this bar or next bar
        had_entry = False
        entry_trade = None
        
        # Check this bar
        if bar_index in trades_by_entry_bar:
            for trade in trades_by_entry_bar[bar_index]:
                if trade['direction'] == 'LONG':
                    had_entry = True
                    entry_trade = trade
                    break
        
        # Check next bar (deferred entry)
        if not had_entry and (bar_index + 1) in trades_by_entry_bar:
            for trade in trades_by_entry_bar[bar_index + 1]:
                if trade['direction'] == 'LONG' and trade['entry_reason']:
                    # Check if it's a deferred entry from this bar
                    had_entry = True
                    entry_trade = trade
                    break
        
        bar_info = {
            'bar_index': bar_index,
            'open': open_price,
            'close': close_price,
            'body_top': body_top,
            'body_bottom': body_bottom,
            'ema_fast': ema_fast,
            'ema_slow': ema_slow,
            'fast_below_slow': ema_fast - ema_slow,
            'body_above_fast': body_bottom - ema_fast,
            'body_above_slow': body_bottom - ema_slow,
            'gradient': gradient,
            'had_entry': had_entry,
            'trade': entry_trade
        }
        
        if had_entry:
            bars_with_entries.append(bar_info)
        else:
            bars_without_entries.append(bar_info)
    
    # Print summary
    print(f"Bars WITH entries: {len(bars_with_entries)}")
    print(f"Bars WITHOUT entries: {len(bars_without_entries)}")
    print()
    
    # Analyze bars WITH entries (if any)
    if bars_with_entries:
        print("=" * 80)
        print("BARS WHERE ENTRIES OCCURRED (despite FastEMA < SlowEMA)")
        print("=" * 80)
        print()
        
        total_pnl = 0
        winning_trades = 0
        losing_trades = 0
        
        for bar in bars_with_entries[:20]:  # Show first 20
            trade = bar['trade']
            pnl = trade['realized_points'] if trade else 0
            total_pnl += pnl
            
            if pnl > 0:
                winning_trades += 1
            elif pnl < 0:
                losing_trades += 1
            
            print(f"Bar {bar['bar_index']}:")
            print(f"  Body: {bar['body_bottom']:.2f} - {bar['body_top']:.2f}")
            print(f"  FastEMA: {bar['ema_fast']:.2f}, SlowEMA: {bar['ema_slow']:.2f}")
            print(f"  FastEMA below SlowEMA by: {bar['fast_below_slow']:.2f} points")
            print(f"  Body above FastEMA by: {bar['body_above_fast']:.2f} points")
            print(f"  Body above SlowEMA by: {bar['body_above_slow']:.2f} points")
            print(f"  Gradient: {bar['gradient']:.2f}°")
            if trade:
                print(f"  Entry: Bar {trade['entry_bar']} @ {trade['entry_price']:.2f}")
                print(f"  Exit: Bar {trade['exit_bar']} @ {trade['exit_price']:.2f}")
                print(f"  P&L: {pnl:+.2f} points ({trade['bars_held']} bars)")
                print(f"  Entry Reason: {trade['entry_reason']}")
            print()
        
        if len(bars_with_entries) > 20:
            print(f"... and {len(bars_with_entries) - 20} more bars with entries")
            print()
        
        print(f"Summary for bars WITH entries:")
        print(f"  Total P&L: {total_pnl:+.2f} points")
        print(f"  Winning trades: {winning_trades}")
        print(f"  Losing trades: {losing_trades}")
        if winning_trades + losing_trades > 0:
            win_rate = (winning_trades / (winning_trades + losing_trades)) * 100
            print(f"  Win rate: {win_rate:.1f}%")
        print()
    
    # Analyze bars WITHOUT entries
    if bars_without_entries:
        print("=" * 80)
        print("BARS WHERE ENTRIES WERE BLOCKED (FastEMA < SlowEMA, body above both)")
        print("=" * 80)
        print()
        
        # Calculate statistics
        avg_fast_below_slow = sum(b['fast_below_slow'] for b in bars_without_entries) / len(bars_without_entries)
        avg_body_above_fast = sum(b['body_above_fast'] for b in bars_without_entries) / len(bars_without_entries)
        avg_body_above_slow = sum(b['body_above_slow'] for b in bars_without_entries) / len(bars_without_entries)
        avg_gradient = sum(b['gradient'] for b in bars_without_entries if b['gradient'] is not None) / len([b for b in bars_without_entries if b['gradient'] is not None])
        
        print(f"Statistics for {len(bars_without_entries)} blocked bars:")
        print(f"  Average FastEMA below SlowEMA: {avg_fast_below_slow:.2f} points")
        print(f"  Average body above FastEMA: {avg_body_above_fast:.2f} points")
        print(f"  Average body above SlowEMA: {avg_body_above_slow:.2f} points")
        print(f"  Average gradient: {avg_gradient:.2f}°")
        print()
        
        # Show sample bars
        print("Sample blocked bars:")
        for bar in bars_without_entries[:10]:  # Show first 10
            print(f"Bar {bar['bar_index']}:")
            print(f"  Body: {bar['body_bottom']:.2f} - {bar['body_top']:.2f}")
            print(f"  FastEMA: {bar['ema_fast']:.2f}, SlowEMA: {bar['ema_slow']:.2f}")
            print(f"  FastEMA below SlowEMA by: {bar['fast_below_slow']:.2f} points")
            print(f"  Body above FastEMA by: {bar['body_above_fast']:.2f} points")
            print(f"  Gradient: {bar['gradient']:.2f}°")
            print()
        
        if len(bars_without_entries) > 10:
            print(f"... and {len(bars_without_entries) - 10} more blocked bars")
            print()
    
    # Check what happened on subsequent bars (did price continue up or reverse?)
    print("=" * 80)
    print("PRICE ACTION ANALYSIS: What happened after these bars?")
    print("=" * 80)
    print()
    
    # For bars without entries, check price movement on next 5 bars
    if bars_without_entries:
        continued_up = 0
        reversed_down = 0
        
        for bar in bars_without_entries[:50]:  # Check first 50
            bar_index = bar['bar_index']
            
            # Get next 5 bars
            cur.execute("""
                SELECT bar_index, close_price
                FROM bar_samples
                WHERE bar_index BETWEEN ? AND ?
                ORDER BY bar_index
                LIMIT 6
            """, (bar_index, bar_index + 5))
            
            next_bars = cur.fetchall()
            if len(next_bars) >= 2:
                entry_close = bar['close']
                # Check close of bar 3 bars later
                if len(next_bars) >= 4:
                    future_close = next_bars[3]['close_price']
                    if future_close > entry_close:
                        continued_up += 1
                    else:
                        reversed_down += 1
        
        print(f"Price action after {min(50, len(bars_without_entries))} blocked bars:")
        print(f"  Continued up (3 bars later): {continued_up}")
        print(f"  Reversed down (3 bars later): {reversed_down}")
        if continued_up + reversed_down > 0:
            continuation_rate = (continued_up / (continued_up + reversed_down)) * 100
            print(f"  Continuation rate: {continuation_rate:.1f}%")
        print()
    
    conn.close()
    conn_dashboard.close()
    
    print("=" * 80)
    print("RECOMMENDATION")
    print("=" * 80)
    print()
    
    if bars_with_entries:
        print("⚠️  Some entries DID occur in this scenario (check entry reasons)")
        print("   This suggests the condition might be valid in some cases.")
    else:
        print("✓ No entries occurred in this scenario - all were blocked.")
    
    if bars_without_entries:
        if continued_up > reversed_down:
            print(f"📈 Price continued up {continuation_rate:.1f}% of the time after blocked bars")
            print("   This suggests entries might have been profitable if allowed.")
        else:
            print(f"📉 Price reversed down more often after blocked bars")
            print("   This suggests blocking was correct.")
    
    print()
    print("Consider adding a parameter:")
    print("  'AllowLongWhenBodyAboveFastButFastBelowSlow'")
    print("  When enabled, allows LONG entry if:")
    print("    - Body is completely above FastEMA")
    print("    - FastEMA < SlowEMA (no crossover yet)")
    print("    - Other filters pass (gradient, etc.)")

if __name__ == '__main__':
    analyze_fast_below_slow_but_body_above()
