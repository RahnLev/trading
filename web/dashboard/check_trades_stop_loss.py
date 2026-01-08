"""
Script to check the trades table and verify stop loss is working correctly.
"""

import sqlite3
import os
import re

DB_PATH = os.path.join(os.path.dirname(__file__), 'dashboard.db')

def check_trades_stop_loss():
    """Analyze trades table to check if stop loss is working correctly."""
    if not os.path.exists(DB_PATH):
        print(f"[ERROR] Database not found at {DB_PATH}")
        return
    
    try:
        conn = sqlite3.connect(DB_PATH)
        cursor = conn.cursor()
        
        # Get all trades ordered by entry_time DESC (newest first)
        cursor.execute("""
            SELECT 
                id,
                entry_time,
                entry_bar,
                direction,
                entry_price,
                exit_time,
                exit_bar,
                exit_price,
                bars_held,
                realized_points,
                mfe,
                mae,
                exit_reason
            FROM trades
            ORDER BY entry_time DESC
            LIMIT 50
        """)
        
        trades = cursor.fetchall()
        
        if not trades:
            print("[INFO] No trades found in database")
            return
        
        print(f"\n{'='*100}")
        print(f"TRADES TABLE ANALYSIS - Last {len(trades)} trades")
        print(f"{'='*100}\n")
        
        # Statistics
        total_trades = len(trades)
        stop_loss_trades = 0
        zero_stop_loss = 0
        same_entry_exit_price = 0
        valid_stop_loss = 0
        break_even_stops = 0
        trail_stops = 0
        
        print(f"{'ID':<6} {'Entry':<12} {'Exit':<12} {'Dir':<5} {'Entry Price':<12} {'Exit Price':<12} {'Pts':<8} {'Stop Loss':<30}")
        print(f"{'-'*6} {'-'*12} {'-'*12} {'-'*5} {'-'*12} {'-'*12} {'-'*8} {'-'*30}")
        
        for trade in trades:
            trade_id, entry_time, entry_bar, direction, entry_price, exit_time, exit_bar, exit_price, bars_held, realized_points, mfe, mae, exit_reason = trade
            
            # Parse exit_reason to extract stop loss info
            stop_loss_info = "N/A"
            stop_loss_points = None
            
            if exit_reason:
                # Check if it's a stop loss exit
                if "Stop" in exit_reason or "stop" in exit_reason.lower():
                    stop_loss_trades += 1
                    
                    # Extract stop loss points from exit_reason
                    # Pattern: "StopMarket @ 25690.25, 0.00 pts" or "Stop loss (StopMarket @ 25690.25, 0.00 pts)"
                    match = re.search(r'([\d.]+)\s*pts', exit_reason)
                    if match:
                        stop_loss_points = float(match.group(1))
                        stop_loss_info = f"{stop_loss_points:.2f} pts"
                        
                        if stop_loss_points == 0.0:
                            zero_stop_loss += 1
                        elif stop_loss_points > 0:
                            valid_stop_loss += 1
                    else:
                        stop_loss_info = "Stop (no pts)"
                    
                    # Check for break-even or trail
                    if "BreakEven" in exit_reason or "break-even" in exit_reason.lower():
                        break_even_stops += 1
                    if "Trail" in exit_reason or "trail" in exit_reason.lower():
                        trail_stops += 1
                else:
                    stop_loss_info = exit_reason[:30] if exit_reason else "N/A"
            
            # Check if entry and exit prices are the same
            if entry_price and exit_price and abs(entry_price - exit_price) < 0.01:
                same_entry_exit_price += 1
            
            # Format entry/exit times
            from datetime import datetime
            try:
                entry_dt = datetime.fromtimestamp(entry_time) if entry_time else None
                exit_dt = datetime.fromtimestamp(exit_time) if exit_time else None
                entry_str = entry_dt.strftime("%m/%d %H:%M") if entry_dt else "N/A"
                exit_str = exit_dt.strftime("%m/%d %H:%M") if exit_dt else "N/A"
            except:
                entry_str = "N/A"
                exit_str = "N/A"
            
            print(f"{trade_id:<6} {entry_str:<12} {exit_str:<12} {direction:<5} {entry_price:<12.2f} {exit_price:<12.2f} {realized_points:<8.2f} {stop_loss_info:<30}")
        
        print(f"\n{'='*100}")
        print("SUMMARY STATISTICS:")
        print(f"{'='*100}")
        print(f"Total trades analyzed: {total_trades}")
        print(f"Stop loss exits: {stop_loss_trades} ({stop_loss_trades/total_trades*100:.1f}%)")
        print(f"  - Valid stop loss (>0 pts): {valid_stop_loss}")
        print(f"  - Zero stop loss (0.00 pts): {zero_stop_loss} ⚠️")
        print(f"  - Break-even stops: {break_even_stops}")
        print(f"  - Trail stops: {trail_stops}")
        print(f"Trades with same entry/exit price: {same_entry_exit_price} ⚠️")
        print(f"\n{'='*100}")
        
        # Detailed analysis of problematic trades
        if zero_stop_loss > 0 or same_entry_exit_price > 0:
            print("\n⚠️  PROBLEMATIC TRADES:")
            print(f"{'='*100}\n")
            
            for trade in trades:
                trade_id, entry_time, entry_bar, direction, entry_price, exit_time, exit_bar, exit_price, bars_held, realized_points, mfe, mae, exit_reason = trade
                
                is_problematic = False
                issues = []
                
                # Check for zero stop loss
                if exit_reason and ("Stop" in exit_reason or "stop" in exit_reason.lower()):
                    match = re.search(r'([\d.]+)\s*pts', exit_reason)
                    if match and float(match.group(1)) == 0.0:
                        is_problematic = True
                        issues.append("Zero stop loss")
                
                # Check for same entry/exit price
                if entry_price and exit_price and abs(entry_price - exit_price) < 0.01:
                    is_problematic = True
                    issues.append("Entry = Exit price")
                
                if is_problematic:
                    print(f"Trade ID {trade_id}: {', '.join(issues)}")
                    print(f"  Entry: {entry_price:.2f}, Exit: {exit_price:.2f}, Reason: {exit_reason}")
                    print()
        
        conn.close()
        
    except Exception as ex:
        print(f"[ERROR] Failed to analyze trades: {ex}")
        import traceback
        traceback.print_exc()

if __name__ == "__main__":
    check_trades_stop_loss()





