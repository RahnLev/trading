"""
Script to analyze trades and write results to a text file that can be read.
"""

import sqlite3
import os
import re
from datetime import datetime

DB_PATH = os.path.join(os.path.dirname(__file__), 'dashboard.db')
OUTPUT_FILE = os.path.join(os.path.dirname(__file__), 'trades_analysis.txt')

def analyze_trades():
    """Analyze trades table and write results to file."""
    if not os.path.exists(DB_PATH):
        with open(OUTPUT_FILE, 'w') as f:
            f.write(f"[ERROR] Database not found at {DB_PATH}\n")
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
            LIMIT 100
        """)
        
        trades = cursor.fetchall()
        
        with open(OUTPUT_FILE, 'w', encoding='utf-8') as f:
            if not trades:
                f.write("[INFO] No trades found in database\n")
                return
            
            f.write("="*100 + "\n")
            f.write(f"TRADES TABLE ANALYSIS - Last {len(trades)} trades\n")
            f.write("="*100 + "\n\n")
            
            # Statistics
            total_trades = len(trades)
            stop_loss_trades = 0
            zero_stop_loss = 0
            same_entry_exit_price = 0
            valid_stop_loss = 0
            break_even_stops = 0
            trail_stops = 0
            problematic_trades = []
            
            f.write(f"{'ID':<6} {'Entry':<12} {'Exit':<12} {'Dir':<5} {'Entry Price':<12} {'Exit Price':<12} {'Pts':<8} {'Stop Loss':<30}\n")
            f.write("-"*100 + "\n")
            
            for trade in trades:
                trade_id, entry_time, entry_bar, direction, entry_price, exit_time, exit_bar, exit_price, bars_held, realized_points, mfe, mae, exit_reason = trade
                
                # Parse exit_reason to extract stop loss info
                stop_loss_info = "N/A"
                stop_loss_points = None
                issues = []
                
                if exit_reason:
                    # Check if it's a stop loss exit
                    if "Stop" in exit_reason or "stop" in exit_reason.lower():
                        stop_loss_trades += 1
                        
                        # Extract stop loss points from exit_reason
                        match = re.search(r'([\d.]+)\s*pts', exit_reason)
                        if match:
                            stop_loss_points = float(match.group(1))
                            stop_loss_info = f"{stop_loss_points:.2f} pts"
                            
                            if stop_loss_points == 0.0:
                                zero_stop_loss += 1
                                issues.append("Zero stop loss")
                            elif stop_loss_points > 0:
                                valid_stop_loss += 1
                        else:
                            stop_loss_info = "Stop (no pts)"
                            issues.append("Stop but no pts")
                        
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
                    issues.append("Entry = Exit price")
                
                if issues:
                    problematic_trades.append({
                        'id': trade_id,
                        'entry_price': entry_price,
                        'exit_price': exit_price,
                        'exit_reason': exit_reason,
                        'issues': issues
                    })
                
                # Format entry/exit times
                try:
                    entry_dt = datetime.fromtimestamp(entry_time) if entry_time else None
                    exit_dt = datetime.fromtimestamp(exit_time) if exit_time else None
                    entry_str = entry_dt.strftime("%m/%d %H:%M") if entry_dt else "N/A"
                    exit_str = exit_dt.strftime("%m/%d %H:%M") if exit_dt else "N/A"
                except:
                    entry_str = "N/A"
                    exit_str = "N/A"
                
                f.write(f"{trade_id:<6} {entry_str:<12} {exit_str:<12} {direction:<5} {entry_price:<12.2f} {exit_price:<12.2f} {realized_points:<8.2f} {stop_loss_info:<30}\n")
            
            f.write("\n" + "="*100 + "\n")
            f.write("SUMMARY STATISTICS:\n")
            f.write("="*100 + "\n")
            f.write(f"Total trades analyzed: {total_trades}\n")
            f.write(f"Stop loss exits: {stop_loss_trades} ({stop_loss_trades/total_trades*100:.1f}%)\n")
            f.write(f"  - Valid stop loss (>0 pts): {valid_stop_loss}\n")
            f.write(f"  - Zero stop loss (0.00 pts): {zero_stop_loss} ⚠️\n")
            f.write(f"  - Break-even stops: {break_even_stops}\n")
            f.write(f"  - Trail stops: {trail_stops}\n")
            f.write(f"Trades with same entry/exit price: {same_entry_exit_price} ⚠️\n")
            f.write("\n" + "="*100 + "\n")
            
            # Detailed analysis of problematic trades
            if problematic_trades:
                f.write("\n⚠️  PROBLEMATIC TRADES:\n")
                f.write("="*100 + "\n\n")
                
                for trade in problematic_trades:
                    f.write(f"Trade ID {trade['id']}: {', '.join(trade['issues'])}\n")
                    f.write(f"  Entry: {trade['entry_price']:.2f}, Exit: {trade['exit_price']:.2f}\n")
                    f.write(f"  Reason: {trade['exit_reason']}\n\n")
        
        conn.close()
        print(f"Analysis written to: {OUTPUT_FILE}")
        
    except Exception as ex:
        with open(OUTPUT_FILE, 'w') as f:
            f.write(f"[ERROR] Failed to analyze trades: {ex}\n")
            import traceback
            f.write(traceback.format_exc())

if __name__ == "__main__":
    analyze_trades()





