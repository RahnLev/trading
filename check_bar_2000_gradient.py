import csv

csv_file = r"c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\strategy_logs\BarsOnTheFlow_MNQ 12-25_2025-12-13_17-25-46-039.csv"

with open(csv_file, 'r') as f:
    reader = csv.DictReader(f)
    for row in reader:
        if row['bar'] == '2000':
            print(f"\n{'='*80}")
            print(f"Bar 2000 - Action: {row['action']}")
            print(f"{'='*80}")
            print(f"Timestamp: {row['timestamp']}")
            print(f"Direction: {row['direction']}")
            print(f"Action: {row['action']}")
            print(f"Order Name: {row['orderName']}")
            print(f"Reason: {row['reason']}")
            print(f"\nGradient Info:")
            print(f"  fastEmaGradDeg: {row['fastEmaGradDeg']}")
            print(f"\nTrend Info:")
            print(f"  trendUpAtDecision: {row['trendUpAtDecision']}")
            print(f"  trendDownAtDecision: {row['trendDownAtDecision']}")
            print(f"\nBar Pattern:")
            print(f"  barPattern: {row['barPattern']}")
            print(f"  candleType: {row['candleType']}")
            print(f"  prevCandleType: {row['prevCandleType']}")
            print(f"\nEntry Flags:")
            print(f"  allowLongThisBar: {row['allowLongThisBar']}")
            print(f"  allowShortThisBar: {row['allowShortThisBar']}")
            print(f"  pendingShortFromGood: {row['pendingShortFromGood']}")
            print(f"  pendingLongFromBad: {row['pendingLongFromBad']}")
            print(f"\nPrice Info:")
            print(f"  open: {row['open']}")
            print(f"  close: {row['close']}")
            print(f"  fastEma: {row['fastEma']}")
            print(f"  price: {row.get('price', 'N/A')}")
            print()
