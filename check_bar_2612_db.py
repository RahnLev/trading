import sqlite3
import json

db_path = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard\dashboard.db'
conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# Check if trades table has the entry
cursor.execute("""
    SELECT * FROM trades 
    WHERE entryBar = 2612 OR exitBar = 2612
    ORDER BY entryBar
""")

trades = cursor.fetchall()
if trades:
    print("=== TRADES AT BAR 2612 ===")
    columns = [desc[0] for desc in cursor.description]
    for trade in trades:
        trade_dict = dict(zip(columns, trade))
        print(f"\nTrade ID: {trade_dict.get('id')}")
        print(f"Direction: {trade_dict.get('direction')}")
        print(f"Entry Bar: {trade_dict.get('entryBar')}")
        print(f"Entry Price: {trade_dict.get('entryPrice')}")
        print(f"Entry Time: {trade_dict.get('entryTime')}")
        print(f"Exit Bar: {trade_dict.get('exitBar')}")
        print(f"Exit Price: {trade_dict.get('exitPrice')}")
        print(f"PnL: {trade_dict.get('pnl')}")

# Get diag data for bar 2612
cursor.execute("""
    SELECT barIndex, time, open, high, low, close, fastEMA, slowEMA, fastGrad, slowGrad, signal, myPosition, logJson
    FROM diags
    WHERE barIndex BETWEEN 2610 AND 2615
    ORDER BY barIndex
""")

diags = cursor.fetchall()
if diags:
    print("\n=== DIAGNOSTIC DATA AROUND BAR 2612 ===")
    for diag in diags:
        bar_idx, time, open_p, high, low, close, fast_ema, slow_ema, fast_grad, slow_grad, signal, pos, log_json = diag
        green = "🟢" if close > open_p else "🔴"
        print(f"\nBar {bar_idx} {green} {time}")
        print(f"  OHLC: {open_p:.2f} / {high:.2f} / {low:.2f} / {close:.2f}")
        print(f"  FastEMA: {fast_ema:.2f}, SlowEMA: {slow_ema:.2f}")
        print(f"  FastGrad: {fast_grad:.4f}, SlowGrad: {slow_grad:.4f}")
        print(f"  Signal: {signal}, Position: {pos}")
        
        # Parse logJson to look for ENTRY events
        if log_json:
            try:
                logs = json.loads(log_json)
                if isinstance(logs, list):
                    for log in logs:
                        if log.get('action') in ['ENTRY', 'ENTRY_DECISION', 'SIGNAL_CHANGE']:
                            print(f"  >> {log.get('action')}: {log.get('direction')} - {log.get('reason', '')[:100]}")
            except:
                pass

conn.close()
