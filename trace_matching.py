import csv

csv_path = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\strategy_logs\BarsOnTheFlow_MNQ 12-25_2025-12-13_22-42-28-830.csv'

entriesLong = []
entriesShort = []
trades = []
lastEntryDirection = None

with open(csv_path, 'r') as f:
    reader = csv.DictReader(f)
    for row in reader:
        action = row['action']
        order = row['orderName']
        bar = row['bar']
        
        if action not in ['ENTRY', 'EXIT']:
            continue
            
        pnl = row.get('pnl', '')
        pnlVal = float(pnl) if pnl else float('nan')
        
        isEntryLong = action == 'ENTRY' and order == 'BarsOnTheFlowLong'
        isEntryShort = action == 'ENTRY' and order == 'BarsOnTheFlowShort'
        isExit = action == 'EXIT'
        
        # LONG exits: BarsOnTheFlowExit, BarsOnTheFlowRetrace
        # SHORT exits: Buy to cover, BarsOnTheFlowExitS, BarsOnTheFlowRetraceS
        # Stop loss: use lastEntryDirection to determine which to exit
        isExitLong = isExit and (order == 'BarsOnTheFlowExit' or order == 'BarsOnTheFlowRetrace' or (order == 'Stop loss' and lastEntryDirection == 'LONG'))
        isExitShort = isExit and (order == 'Buy to cover' or order == 'BarsOnTheFlowExitS' or order == 'BarsOnTheFlowRetraceS' or (order == 'Stop loss' and lastEntryDirection == 'SHORT'))
        
        # Process exits BEFORE entries
        if isExitLong and len(entriesLong):
            entry = entriesLong.pop(0)
            trades.append({
                'direction': 'LONG',
                'pnl': pnlVal,
                'bar': bar,
                'order': order,
                'entryBar': entry['bar']
            })
        elif isExitShort and len(entriesShort):
            entry = entriesShort.pop(0)
            trades.append({
                'direction': 'SHORT',
                'pnl': pnlVal,
                'bar': bar,
                'order': order,
                'entryBar': entry['bar']
            })
        elif isExitLong and not len(entriesLong):
            print(f"WARNING: EXIT LONG at bar {bar} ({order}) but no LONG entries! lastEntryDirection={lastEntryDirection}")
        elif isExitShort and not len(entriesShort):
            print(f"WARNING: EXIT SHORT at bar {bar} ({order}) but no SHORT entries! lastEntryDirection={lastEntryDirection}")
        elif isEntryLong:
            entriesLong.append({'bar': bar})
            lastEntryDirection = 'LONG'
        elif isEntryShort:
            entriesShort.append({'bar': bar})
            lastEntryDirection = 'SHORT'

print(f"\nTotal trades matched: {len(trades)}")
print(f"Unmatched LONG entries: {len(entriesLong)}")
print(f"Unmatched SHORT entries: {len(entriesShort)}")

losses = [t for t in trades if t['pnl'] < 0]
print(f"\nLosses: {len(losses)}")

print("\nLosses by order type:")
by_order = {}
for loss in losses:
    order = loss['order']
    by_order[order] = by_order.get(order, 0) + 1

for name, count in sorted(by_order.items()):
    print(f"  {name}: {count}")

# Show first few trades for verification
print("\nFirst 10 trades:")
for i, t in enumerate(trades[:10]):
    print(f"  {i+1}. {t['direction']:5} entry bar {t['entryBar']:4} → exit bar {t['bar']:4} ({t['order']:25}) PnL: {t['pnl']:7.2f}")
