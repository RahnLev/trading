"""
Direct database analysis of trades table for stop loss issues.
Reads dashboard.db directly without needing the API server.
"""

import sqlite3
import os
import re
from datetime import datetime

DB_PATH = os.path.join(os.path.dirname(__file__), 'dashboard.db')
OUTPUT_FILE = os.path.join(os.path.dirname(__file__), 'trades_analysis_output.txt')

def analyze_trades_direct():
    """Read and analyze trades table directly from database."""
    output_lines = []
    
    def log(msg):
        output_lines.append(msg)
        print(msg)
    
    log("="*100)
    log("DIRECT DATABASE ANALYSIS - TRADES TABLE")
    log("="*100)
    log(f"\nDatabase: {DB_PATH}\n")
    
    if not os.path.exists(DB_PATH):
        log(f"❌ ERROR: Database not found at {DB_PATH}")
        with open(OUTPUT_FILE, 'w', encoding='utf-8') as f:
            f.write('\n'.join(output_lines))
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
        
        if not trades:
            log("ℹ️  No trades found in database")
            conn.close()
            with open(OUTPUT_FILE, 'w', encoding='utf-8') as f:
                f.write('\n'.join(output_lines))
            return
        
        # Statistics
        total_trades = len(trades)
        stop_loss_trades = 0
        zero_stop_loss = 0
        same_entry_exit_price = 0
        valid_stop_loss = 0
        break_even_stops = 0
        trail_stops = 0
        problematic_trades = []
        
        log(f"{'ID':<6} {'Entry Time':<20} {'Dir':<5} {'Entry Price':<12} {'Exit Price':<12} {'Pts':<8} {'Stop Loss':<30} {'Issues':<30}")
        log("-"*100)
        
        for trade in trades:
            trade_id, entry_time, entry_bar, direction, entry_price, exit_time, exit_bar, exit_price, bars_held, realized_points, mfe, mae, exit_reason = trade
            
            stop_loss_info = "N/A"
            stop_loss_points = None
            issues = []
            
            if exit_reason:
                if "Stop" in exit_reason or "stop" in exit_reason.lower():
                    stop_loss_trades += 1
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
                
                if "BreakEven" in exit_reason or "break-even" in exit_reason.lower():
                    break_even_stops += 1
                if "Trail" in exit_reason or "trail" in exit_reason.lower():
                    trail_stops += 1
            
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
            
            try:
                entry_dt = datetime.fromtimestamp(entry_time) if entry_time else None
                entry_str = entry_dt.strftime("%m/%d %H:%M:%S") if entry_dt else "N/A"
            except:
                entry_str = "N/A"
            
            issues_str = ', '.join(issues) if issues else 'OK'
            
            log(f"{trade_id:<6} {entry_str:<20} {direction:<5} {entry_price:<12.2f} {exit_price:<12.2f} {realized_points:<8.2f} {stop_loss_info:<30} {issues_str:<30}")
        
        log("\n" + "="*100)
        log("SUMMARY STATISTICS:")
        log("="*100)
        log(f"Total trades analyzed: {total_trades}")
        log(f"Stop loss exits: {stop_loss_trades} ({stop_loss_trades/total_trades*100:.1f}%)")
        log(f"  - Valid stop loss (>0 pts): {valid_stop_loss}")
        log(f"  - Zero stop loss (0.00 pts): {zero_stop_loss} ⚠️")
        log(f"  - Break-even stops: {break_even_stops}")
        log(f"  - Trail stops: {trail_stops}")
        log(f"Trades with same entry/exit price: {same_entry_exit_price} ⚠️")
        
        if problematic_trades:
            log("\n" + "="*100)
            log(f"⚠️  PROBLEMATIC TRADES ({len(problematic_trades)}):")
            log("="*100)
            for trade in problematic_trades:
                log(f"\nTrade ID {trade['id']}: {', '.join(trade['issues'])}")
                log(f"  Entry: {trade['entry_price']:.2f}, Exit: {trade['exit_price']:.2f}")
                log(f"  Reason: {trade['exit_reason']}")
        
        log("\n" + "="*100)
        
        conn.close()
        
        # Write output to file
        with open(OUTPUT_FILE, 'w', encoding='utf-8') as f:
            f.write('\n'.join(output_lines))
        
    except Exception as ex:
        error_msg = f"❌ ERROR: {ex}"
        log(error_msg)
        import traceback
        traceback.print_exc()
        with open(OUTPUT_FILE, 'w', encoding='utf-8') as f:
            f.write('\n'.join(output_lines))

if __name__ == "__main__":
    analyze_trades_direct()
    print(f"\n✅ Analysis complete! Results saved to: {OUTPUT_FILE}")
    # input("\nPress Enter to exit...")


