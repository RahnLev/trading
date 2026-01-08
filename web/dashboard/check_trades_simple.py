"""
Simple script to read and display trades from the database.
"""

import sqlite3
import os
import json
from datetime import datetime

DB_PATH = os.path.join(os.path.dirname(__file__), 'dashboard.db')

if not os.path.exists(DB_PATH):
    print("❌ Database not found at:", DB_PATH)
    exit(1)

conn = sqlite3.connect(DB_PATH)
conn.row_factory = sqlite3.Row
cursor = conn.cursor()

# Get trade count
cursor.execute("SELECT COUNT(*) FROM trades")
trade_count = cursor.fetchone()[0]
print(f"Total trades in database: {trade_count}\n")

if trade_count == 0:
    print("No trades found in the database.")
    conn.close()
    exit(0)

# Get all trades, newest first
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
    ORDER BY COALESCE(exit_time, entry_time) DESC, entry_time DESC
    LIMIT 50
""")

rows = cursor.fetchall()
trades = []

print("Recent trades (newest first):")
print("=" * 120)
print(f"{'ID':<6} {'Entry Bar':<10} {'Direction':<10} {'Entry Price':<12} {'Exit Bar':<10} {'Exit Price':<12} {'Points':<10} {'Exit Reason':<30}")
print("=" * 120)

for row in rows:
    entry_bar = row['entry_bar'] if row['entry_bar'] else 'N/A'
    exit_bar = row['exit_bar'] if row['exit_bar'] else 'Open'
    direction = row['direction'] if row['direction'] else 'N/A'
    entry_price = f"{row['entry_price']:.2f}" if row['entry_price'] else 'N/A'
    exit_price = f"{row['exit_price']:.2f}" if row['exit_price'] else 'N/A'
    points = f"{row['realized_points']:.2f}" if row['realized_points'] is not None else 'N/A'
    exit_reason = row['exit_reason'] if row['exit_reason'] else 'N/A'
    
    print(f"{row['id']:<6} {entry_bar:<10} {direction:<10} {entry_price:<12} {exit_bar:<10} {exit_price:<12} {points:<10} {exit_reason[:30]:<30}")
    
    trades.append({
        'id': row['id'],
        'entry_bar': row['entry_bar'],
        'direction': row['direction'],
        'entry_price': row['entry_price'],
        'exit_bar': row['exit_bar'],
        'exit_price': row['exit_price'],
        'realized_points': row['realized_points'],
        'exit_reason': row['exit_reason']
    })

print("=" * 120)
print(f"\nDisplayed {len(trades)} most recent trades")

# Summary statistics
cursor.execute("""
    SELECT 
        COUNT(*) as total,
        SUM(CASE WHEN direction = 'Long' THEN 1 ELSE 0 END) as longs,
        SUM(CASE WHEN direction = 'Short' THEN 1 ELSE 0 END) as shorts,
        SUM(realized_points) as total_points,
        AVG(realized_points) as avg_points,
        SUM(CASE WHEN realized_points > 0 THEN 1 ELSE 0 END) as winners,
        SUM(CASE WHEN realized_points < 0 THEN 1 ELSE 0 END) as losers
    FROM trades
    WHERE exit_time IS NOT NULL
""")

stats = cursor.fetchone()
if stats and stats[0]:
    print("\nSummary Statistics (closed trades only):")
    print(f"  Total closed trades: {stats[0]}")
    print(f"  Long trades: {stats[1]}")
    print(f"  Short trades: {stats[2]}")
    print(f"  Total points: {stats[3]:.2f}" if stats[3] else "  Total points: 0.00")
    print(f"  Average points per trade: {stats[4]:.2f}" if stats[4] else "  Average points per trade: 0.00")
    print(f"  Winners: {stats[5]}")
    print(f"  Losers: {stats[6]}")

conn.close()

# Also save to JSON for AI to read
output_file = os.path.join(os.path.dirname(__file__), 'trades_output.json')
with open(output_file, 'w', encoding='utf-8') as f:
    json.dump({
        'timestamp': datetime.now().isoformat(),
        'total_trades': trade_count,
        'recent_trades': trades,
        'statistics': {
            'total_closed': stats[0] if stats else 0,
            'longs': stats[1] if stats else 0,
            'shorts': stats[2] if stats else 0,
            'total_points': float(stats[3]) if stats and stats[3] else 0.0,
            'avg_points': float(stats[4]) if stats and stats[4] else 0.0,
            'winners': stats[5] if stats else 0,
            'losers': stats[6] if stats else 0
        }
    }, f, indent=2, default=str)

print(f"\n✅ Data also saved to: {output_file}")

