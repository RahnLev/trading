"""
Check for asymmetries between LONG and SHORT entry/exit logic.
This script compares the conditions to ensure they're symmetric.
"""
import sqlite3
import os
from datetime import datetime

# Database paths
dashboard_db = os.path.join(os.path.dirname(__file__), 'dashboard.db')

def check_long_short_symmetry():
    """Analyze trades to check if longs and shorts behave symmetrically."""
    
    if not os.path.exists(dashboard_db):
        print(f"Error: Database not found at {dashboard_db}")
        return
    
    conn = sqlite3.connect(dashboard_db)
    conn.row_factory = sqlite3.Row
    cur = conn.cursor()
    
    print("=" * 80)
    print("LONG vs SHORT SYMMETRY ANALYSIS")
    print("=" * 80)
    print()
    
    # Get all trades
    cur.execute("""
        SELECT 
            direction,
            entry_bar,
            exit_bar,
            entry_price,
            exit_price,
            realized_points,
            bars_held,
            entry_reason,
            exit_reason
        FROM trades
        ORDER BY entry_bar DESC
    """)
    
    all_trades = cur.fetchall()
    
    if not all_trades:
        print("No trades found in database.")
        conn.close()
        return
    
    # Separate longs and shorts
    long_trades = [t for t in all_trades if t['direction'] == 'LONG']
    short_trades = [t for t in all_trades if t['direction'] == 'SHORT']
    
    print(f"Total trades: {len(all_trades)}")
    print(f"  LONG trades: {len(long_trades)}")
    print(f"  SHORT trades: {len(short_trades)}")
    print()
    
    # Analyze P&L
    long_pnl = sum(t['realized_points'] or 0 for t in long_trades)
    short_pnl = sum(t['realized_points'] or 0 for t in short_trades)
    
    long_wins = sum(1 for t in long_trades if (t['realized_points'] or 0) > 0)
    long_losses = sum(1 for t in long_trades if (t['realized_points'] or 0) < 0)
    long_breakeven = sum(1 for t in long_trades if (t['realized_points'] or 0) == 0)
    
    short_wins = sum(1 for t in short_trades if (t['realized_points'] or 0) > 0)
    short_losses = sum(1 for t in short_trades if (t['realized_points'] or 0) < 0)
    short_breakeven = sum(1 for t in short_trades if (t['realized_points'] or 0) == 0)
    
    print("=" * 80)
    print("P&L ANALYSIS")
    print("=" * 80)
    print()
    print(f"LONG Trades:")
    print(f"  Total P&L: {long_pnl:+.2f} points")
    print(f"  Winning: {long_wins}")
    print(f"  Losing: {long_losses}")
    print(f"  Breakeven: {long_breakeven}")
    if long_wins + long_losses > 0:
        long_win_rate = (long_wins / (long_wins + long_losses)) * 100
        print(f"  Win Rate: {long_win_rate:.1f}%")
    if len(long_trades) > 0:
        avg_long_pnl = long_pnl / len(long_trades)
        print(f"  Average P&L: {avg_long_pnl:+.2f} points")
    
    print()
    print(f"SHORT Trades:")
    print(f"  Total P&L: {short_pnl:+.2f} points")
    print(f"  Winning: {short_wins}")
    print(f"  Losing: {short_losses}")
    print(f"  Breakeven: {short_breakeven}")
    if short_wins + short_losses > 0:
        short_win_rate = (short_wins / (short_wins + short_losses)) * 100
        print(f"  Win Rate: {short_win_rate:.1f}%")
    if len(short_trades) > 0:
        avg_short_pnl = short_pnl / len(short_trades)
        print(f"  Average P&L: {avg_short_pnl:+.2f} points")
    
    print()
    print(f"Difference (LONG - SHORT):")
    print(f"  P&L Difference: {long_pnl - short_pnl:+.2f} points")
    print(f"  Win Rate Difference: {(long_win_rate if long_wins + long_losses > 0 else 0) - (short_win_rate if short_wins + short_losses > 0 else 0):+.1f}%")
    print()
    
    # Analyze exit reasons
    print("=" * 80)
    print("EXIT REASONS ANALYSIS")
    print("=" * 80)
    print()
    
    long_exit_reasons = {}
    short_exit_reasons = {}
    
    for trade in long_trades:
        reason = trade['exit_reason'] or 'Unknown'
        if reason not in long_exit_reasons:
            long_exit_reasons[reason] = {'count': 0, 'pnl': 0}
        long_exit_reasons[reason]['count'] += 1
        long_exit_reasons[reason]['pnl'] += trade['realized_points'] or 0
    
    for trade in short_trades:
        reason = trade['exit_reason'] or 'Unknown'
        if reason not in short_exit_reasons:
            short_exit_reasons[reason] = {'count': 0, 'pnl': 0}
        short_exit_reasons[reason]['count'] += 1
        short_exit_reasons[reason]['pnl'] += trade['realized_points'] or 0
    
    print("LONG Exit Reasons:")
    for reason, data in sorted(long_exit_reasons.items(), key=lambda x: x[1]['count'], reverse=True):
        avg_pnl = data['pnl'] / data['count'] if data['count'] > 0 else 0
        print(f"  {reason}: {data['count']} trades, Total P&L: {data['pnl']:+.2f}, Avg: {avg_pnl:+.2f}")
    
    print()
    print("SHORT Exit Reasons:")
    for reason, data in sorted(short_exit_reasons.items(), key=lambda x: x[1]['count'], reverse=True):
        avg_pnl = data['pnl'] / data['count'] if data['count'] > 0 else 0
        print(f"  {reason}: {data['count']} trades, Total P&L: {data['pnl']:+.2f}, Avg: {avg_pnl:+.2f}")
    
    print()
    
    # Analyze entry reasons
    print("=" * 80)
    print("ENTRY REASONS ANALYSIS")
    print("=" * 80)
    print()
    
    long_entry_reasons = {}
    short_entry_reasons = {}
    
    for trade in long_trades:
        reason = trade['entry_reason'] or 'Unknown'
        if reason not in long_entry_reasons:
            long_entry_reasons[reason] = {'count': 0, 'pnl': 0}
        long_entry_reasons[reason]['count'] += 1
        long_entry_reasons[reason]['pnl'] += trade['realized_points'] or 0
    
    for trade in short_trades:
        reason = trade['entry_reason'] or 'Unknown'
        if reason not in short_entry_reasons:
            short_entry_reasons[reason] = {'count': 0, 'pnl': 0}
        short_entry_reasons[reason]['count'] += 1
        short_entry_reasons[reason]['pnl'] += trade['realized_points'] or 0
    
    print("LONG Entry Reasons:")
    for reason, data in sorted(long_entry_reasons.items(), key=lambda x: x[1]['count'], reverse=True):
        avg_pnl = data['pnl'] / data['count'] if data['count'] > 0 else 0
        print(f"  {reason}: {data['count']} trades, Total P&L: {data['pnl']:+.2f}, Avg: {avg_pnl:+.2f}")
    
    print()
    print("SHORT Entry Reasons:")
    for reason, data in sorted(short_entry_reasons.items(), key=lambda x: x[1]['count'], reverse=True):
        avg_pnl = data['pnl'] / data['count'] if data['count'] > 0 else 0
        print(f"  {reason}: {data['count']} trades, Total P&L: {data['pnl']:+.2f}, Avg: {avg_pnl:+.2f}")
    
    print()
    
    # Check for asymmetries in trade duration
    print("=" * 80)
    print("TRADE DURATION ANALYSIS")
    print("=" * 80)
    print()
    
    long_bars_held = [t['bars_held'] or 0 for t in long_trades if t['bars_held'] is not None]
    short_bars_held = [t['bars_held'] or 0 for t in short_trades if t['bars_held'] is not None]
    
    if long_bars_held:
        avg_long_duration = sum(long_bars_held) / len(long_bars_held)
        min_long = min(long_bars_held)
        max_long = max(long_bars_held)
        print(f"LONG Trades Duration:")
        print(f"  Average: {avg_long_duration:.1f} bars")
        print(f"  Min: {min_long} bars")
        print(f"  Max: {max_long} bars")
    
    if short_bars_held:
        avg_short_duration = sum(short_bars_held) / len(short_bars_held)
        min_short = min(short_bars_held)
        max_short = max(short_bars_held)
        print(f"SHORT Trades Duration:")
        print(f"  Average: {avg_short_duration:.1f} bars")
        print(f"  Min: {min_short} bars")
        print(f"  Max: {max_short} bars")
    
    if long_bars_held and short_bars_held:
        duration_diff = avg_long_duration - avg_short_duration
        print(f"  Difference: {duration_diff:+.1f} bars (LONG - SHORT)")
    
    print()
    
    # Summary and recommendations
    print("=" * 80)
    print("SYMMETRY CHECK SUMMARY")
    print("=" * 80)
    print()
    
    if long_pnl > short_pnl and long_win_rate > short_win_rate:
        print("⚠️  ASYMMETRY DETECTED: LONG trades are performing better than SHORT trades")
        print(f"   LONG P&L: {long_pnl:+.2f} points, Win Rate: {long_win_rate:.1f}%")
        print(f"   SHORT P&L: {short_pnl:+.2f} points, Win Rate: {short_win_rate:.1f}%")
        print()
        print("   Possible causes:")
        print("   1. Entry conditions are not symmetric (LONG may not require prevGood like SHORT requires prevBad)")
        print("   2. Exit conditions may favor longs")
        print("   3. Market bias (uptrending market)")
        print("   4. Stop loss or exit logic differences")
    elif short_pnl > long_pnl and short_win_rate > long_win_rate:
        print("⚠️  ASYMMETRY DETECTED: SHORT trades are performing better than LONG trades")
        print(f"   SHORT P&L: {short_pnl:+.2f} points, Win Rate: {short_win_rate:.1f}%")
        print(f"   LONG P&L: {long_pnl:+.2f} points, Win Rate: {long_win_rate:.1f}%")
    else:
        print("✓ LONG and SHORT trades appear to be performing similarly")
        print(f"   LONG P&L: {long_pnl:+.2f} points, Win Rate: {long_win_rate:.1f}%")
        print(f"   SHORT P&L: {short_pnl:+.2f} points, Win Rate: {short_win_rate:.1f}%")
    
    conn.close()

if __name__ == '__main__':
    check_long_short_symmetry()
