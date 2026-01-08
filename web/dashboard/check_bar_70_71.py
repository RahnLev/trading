import sqlite3
import sys

conn = sqlite3.connect('volatility.db')
cur = conn.cursor()

# Check bar samples
print("=== BAR SAMPLES (68-72) ===")
cur.execute("""
    SELECT bar_index, timestamp, direction, close_price, ema_fast_value, ema_slow_value, 
           candle_type, trend_up, trend_down, allow_long_this_bar, allow_short_this_bar, entry_reason
    FROM bar_samples 
    WHERE bar_index BETWEEN 68 AND 72 
    ORDER BY bar_index
""")
rows = cur.fetchall()
print(f"{'Bar':<5} {'Time':<10} {'Dir':<5} {'Close':<8} {'EMA Fast':<10} {'EMA Slow':<10} {'Candle':<7} {'Trend↑':<7} {'Trend↓':<7} {'AllowL':<7} {'AllowS':<7} {'Entry Reason':<30}")
print("-" * 120)
for r in rows:
    bar_idx, ts, direction, close, ema_fast, ema_slow, candle, trend_up, trend_down, allow_l, allow_s, entry_reason = r
    ts_short = ts[11:19] if ts and len(ts) > 19 else ts
    ema_fast_str = f"{ema_fast:.2f}" if ema_fast else "NULL"
    ema_slow_str = f"{ema_slow:.2f}" if ema_slow else "NULL"
    print(f"{bar_idx:<5} {ts_short:<10} {str(direction):<5} {close:<8.2f} {ema_fast_str:<10} {ema_slow_str:<10} {str(candle):<7} {trend_up:<7} {trend_down:<7} {allow_l:<7} {allow_s:<7} {str(entry_reason)[:30] if entry_reason else '':<30}")

print("\n=== TRADES (entry or exit between 68-72) ===")
cur.execute("""
    SELECT entry_bar, exit_bar, entry_price, exit_price, entry_reason, exit_reason, trade_result_ticks
    FROM trades 
    WHERE entry_bar BETWEEN 68 AND 72 OR exit_bar BETWEEN 68 AND 72
    ORDER BY entry_bar
""")
rows = cur.fetchall()
if rows:
    print(f"{'Entry Bar':<10} {'Exit Bar':<10} {'Entry Price':<12} {'Exit Price':<12} {'Entry Reason':<25} {'Exit Reason':<25} {'Result Ticks':<12}")
    print("-" * 120)
    for r in rows:
        entry_bar, exit_bar, entry_price, exit_price_val, entry_reason, exit_reason, result_ticks = r
        print(f"{entry_bar if entry_bar else '':<10} {exit_bar if exit_bar else '':<10} {entry_price if entry_price else '':<12.2f} {exit_price_val if exit_price_val else '':<12.2f} {str(entry_reason)[:25] if entry_reason else '':<25} {str(exit_reason)[:25] if exit_reason else '':<25} {result_ticks if result_ticks else '':<12}")
else:
    print("No trades found in this range")

conn.close()
