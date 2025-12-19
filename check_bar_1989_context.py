import csv

csv_file = r"c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\strategy_logs\BarsOnTheFlow_MNQ 12-25_2025-12-13_17-25-46-039.csv"

# Get bars 1984-1989 to see the pattern leading to bar 1989
target_bars = ['1984', '1985', '1986', '1987', '1988', '1989']

print("\n" + "="*100)
print("BAR SEQUENCE ANALYSIS (1984-1989)")
print("="*100)

with open(csv_file, 'r') as f:
    reader = csv.DictReader(f)
    for row in reader:
        if row['bar'] in target_bars and row['action'] == 'BAR':
            print(f"\nBar {row['bar']:>4} | {row['timestamp']} | CandleType: {row['candleType']:>4} | "
                  f"TrendUp: {row['trendUpAtDecision']:>5} | TrendDown: {row['trendDownAtDecision']:>5} | "
                  f"Pattern: {row['barPattern']:>10}")
            print(f"         | Open: {row['open']:>10} | Close: {row['close']:>10} | "
                  f"Gradient: {row['fastEmaGradDeg']:>8}")

print("\n" + "="*100)
print("Looking at the last 5 bars before 1989:")
print("="*100)

with open(csv_file, 'r') as f:
    reader = csv.DictReader(f)
    bars_data = []
    for row in reader:
        if row['action'] == 'BAR':
            bar_num = int(row['bar'])
            if 1984 <= bar_num <= 1989:
                bars_data.append({
                    'bar': bar_num,
                    'candleType': row['candleType'],
                    'open': float(row['open']),
                    'close': float(row['close']),
                    'pnl': float(row['close']) - float(row['open'])
                })

print("\nCandle sequence (last 5 bars before/including 1989):")
for i, bar in enumerate(bars_data[-5:]):
    print(f"  Bar {bar['bar']}: {bar['candleType']:>4} candle | "
          f"Open: {bar['open']:.2f} | Close: {bar['close']:.2f} | "
          f"PnL: {bar['pnl']:+8.2f}")

# Check if we have 5 consecutive bad bars
recent_candles = [b['candleType'] for b in bars_data[-5:]]
print(f"\nLast 5 candle types: {recent_candles}")
print(f"All bad? {all(c == 'bad' for c in recent_candles)}")

# Check PnL tiebreaker for 3/4 pattern
if len(recent_candles) >= 4:
    last_4_candles = recent_candles[-4:]
    bad_count = sum(1 for c in last_4_candles if c == 'bad')
    good_count = sum(1 for c in last_4_candles if c == 'good')
    print(f"\nLast 4 candles: {last_4_candles}")
    print(f"Bad count: {bad_count}, Good count: {good_count}")
    
    if bad_count == 3 and good_count == 1:
        total_pnl = sum(b['pnl'] for b in bars_data[-4:])
        print(f"3/4 pattern detected! Total PnL of last 4 bars: {total_pnl:+.2f}")
        print(f"Trend Down requires negative PnL (< 0): {total_pnl < 0}")
