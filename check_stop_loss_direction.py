import csv

csv_path = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\strategy_logs\BarsOnTheFlow_MNQ 12-25_2025-12-13_22-42-28-830.csv'

last_entry_dir = None

with open(csv_path, 'r') as f:
    reader = csv.DictReader(f)
    for row in reader:
        if row['action'] == 'ENTRY':
            last_entry_dir = row['direction']
        elif row['action'] == 'EXIT' and row['orderName'] == 'Stop loss':
            print(f"bar {row['bar']}: EXIT dir={row['direction']}, last ENTRY dir={last_entry_dir}, PnL={row.get('pnl','')}")
