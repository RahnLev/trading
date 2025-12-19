import csv

csv_path = r'c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\strategy_logs\BarsOnTheFlow_MNQ 12-25_2025-12-13_22-42-28-830.csv'

entriesLong = []
entriesShort = []
trades = []
currentPosition = 'FLAT'

with open(csv_path, 'r') as f:
    reader = csv.DictReader(f)
    for row in reader:
        action = row['action']
        order = row['orderName']
        
        if action not in ['ENTRY', 'EXIT']:
            continue
            
        pnl = row.get('pnl', '')
        pnlVal = float(pnl) if pnl else float('nan')
        
        isEntryLong = action == 'ENTRY' and order == 'BarsOnTheFlowLong'
        isEntryShort = action == 'ENTRY' and order == 'BarsOnTheFlowShort'
        isExit = action == 'EXIT'
        
        # LONG exits: BarsOnTheFlowExit, BarsOnTheFlowRetrace
        # SHORT exits: Buy to cover, BarsOnTheFlowExitS, BarsOnTheFlowRetraceS
        # Stop loss: use currentPosition to determine which to exit
        isExitLong = isExit and (order == 'BarsOnTheFlowExit' or order == 'BarsOnTheFlowRetrace' or (order == 'Stop loss' and currentPosition == 'LONG'))
        isExitShort = isExit and (order == 'Buy to cover' or order == 'BarsOnTheFlowExitS' or order == 'BarsOnTheFlowRetraceS' or (order == 'Stop loss' and currentPosition == 'SHORT'))
        
        # Process exits BEFORE entries
        if isExitLong and len(entriesLong):
            entry = entriesLong.pop(0)
            trades.append({
                'direction': 'LONG',
                'pnl': pnlVal,
                'bar': row['bar'],
                'order': order
            })
            currentPosition = 'FLAT'
        elif isExitShort and len(entriesShort):
            entry = entriesShort.pop(0)
            trades.append({
                'direction': 'SHORT',
                'pnl': pnlVal,
                'bar': row['bar'],
                'order': order
            })
            currentPosition = 'FLAT'
        elif isEntryLong:
            entriesLong.append({'bar': row['bar']})
            currentPosition = 'LONG'
        elif isEntryShort:
            entriesShort.append({'bar': row['bar']})
            currentPosition = 'SHORT'

print(f"Total trades: {len(trades)}")
losses = [t for t in trades if t['pnl'] < 0]
print(f"Losses: {len(losses)}")

print("\nLosses by order type:")
by_order = {}
for loss in losses:
    order = loss['order']
    by_order[order] = by_order.get(order, 0) + 1

for name, count in sorted(by_order.items()):
    print(f"  {name}: {count}")

print(f"\nUnmatched LONG entries: {len(entriesLong)}")
print(f"Unmatched SHORT entries: {len(entriesShort)}")
