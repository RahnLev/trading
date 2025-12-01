import pandas as pd
import numpy as np
from datetime import datetime
import matplotlib.pyplot as plt

# Read the trade summary
df = pd.read_csv('strategy_logs/GradientSlope_TRADES_SUMMARY_ETHUSD_2025-11-30_10-31-45.csv')

print("=" * 80)
print("TRADE PERFORMANCE ANALYSIS")
print("=" * 80)

# Basic stats
total_trades = len(df)
winning_trades = len(df[df['RealizedPoints'] > 0])
losing_trades = len(df[df['RealizedPoints'] < 0])
breakeven_trades = len(df[df['RealizedPoints'] == 0])

win_rate = (winning_trades / total_trades) * 100
total_profit = df['RealizedPoints'].sum()
avg_win = df[df['RealizedPoints'] > 0]['RealizedPoints'].mean() if winning_trades > 0 else 0
avg_loss = df[df['RealizedPoints'] < 0]['RealizedPoints'].mean() if losing_trades > 0 else 0

print(f"\n📊 OVERALL STATISTICS")
print(f"Total Trades: {total_trades}")
print(f"Winning: {winning_trades} ({win_rate:.1f}%)")
print(f"Losing: {losing_trades} ({100-win_rate:.1f}%)")
print(f"Breakeven: {breakeven_trades}")
print(f"Total P/L: {total_profit:.2f} points")
print(f"Average Win: {avg_win:.2f} points")
print(f"Average Loss: {avg_loss:.2f} points")
print(f"Win/Loss Ratio: {abs(avg_win/avg_loss):.2f}" if avg_loss != 0 else "N/A")

# Analyze by bars held
print(f"\n⏱️  BARS HELD ANALYSIS")
bars_groups = df.groupby('BarsHeld').agg({
    'RealizedPoints': ['count', 'mean', 'sum']
}).round(2)
bars_groups.columns = ['Count', 'Avg P/L', 'Total P/L']
print(bars_groups.head(20))

# Exit reasons analysis
print(f"\n🚪 EXIT REASON PATTERNS")
df['ExitReasonCategory'] = df['ExitReason'].str.extract(r'(FastGrad[<>=]+[^(]+)')
exit_analysis = df.groupby('ExitReasonCategory').agg({
    'RealizedPoints': ['count', 'mean', 'sum']
}).round(2)
exit_analysis.columns = ['Count', 'Avg P/L', 'Total P/L']
print(exit_analysis.sort_values('Count', ascending=False).head(10))

# MFE (Maximum Favorable Excursion) vs Profit Analysis
print(f"\n📈 MFE (MAX PROFIT) vs ACTUAL PROFIT")
df['MFE_Captured'] = (df['RealizedPoints'] / df['MFE'] * 100).replace([np.inf, -np.inf], 0)
df['Lost_Opportunity'] = df['MFE'] - df['RealizedPoints']

mfe_stats = df[df['MFE'] > 0].agg({
    'MFE': 'mean',
    'RealizedPoints': 'mean',
    'MFE_Captured': 'mean',
    'Lost_Opportunity': 'mean'
})
print(f"Average MFE (best price reached): {mfe_stats['MFE']:.2f} points")
print(f"Average Actual Profit: {mfe_stats['RealizedPoints']:.2f} points")
print(f"Average % of MFE Captured: {mfe_stats['MFE_Captured']:.1f}%")
print(f"Average Lost Opportunity: {mfe_stats['Lost_Opportunity']:.2f} points")

# Identify trades that gave back significant profit
print(f"\n💸 TRADES THAT GAVE BACK PROFIT (Lost >50% of MFE)")
gave_back = df[(df['MFE'] > 1.0) & (df['MFE_Captured'] < 50)].copy()
if len(gave_back) > 0:
    print(f"Count: {len(gave_back)} trades")
    print(f"Total Lost Opportunity: {gave_back['Lost_Opportunity'].sum():.2f} points")
    print(f"\nSample trades:")
    for idx, row in gave_back.head(10).iterrows():
        print(f"  Bar {row['EntryBar']}: MFE={row['MFE']:.2f}, Actual={row['RealizedPoints']:.2f}, Lost={row['Lost_Opportunity']:.2f}")
else:
    print("No significant profit givebacks found")

# MAE (Maximum Adverse Excursion) Analysis
print(f"\n📉 MAE (MAX DRAWDOWN) ANALYSIS")
df['Hit_Stop'] = df['MAE'] > 1.0  # Trades that went significantly against us
stopped_trades = df[df['Hit_Stop']]
if len(stopped_trades) > 0:
    print(f"Trades with significant adverse move: {len(stopped_trades)}")
    print(f"Win rate on these: {(len(stopped_trades[stopped_trades['RealizedPoints'] > 0]) / len(stopped_trades) * 100):.1f}%")
    print(f"Average MAE: {stopped_trades['MAE'].mean():.2f} points")
    print(f"Average final P/L: {stopped_trades['RealizedPoints'].mean():.2f} points")

# Winning vs Losing Trade Characteristics
print(f"\n🏆 WINNING vs LOSING TRADE CHARACTERISTICS")
winners = df[df['RealizedPoints'] > 0]
losers = df[df['RealizedPoints'] < 0]

print(f"\nWinners:")
print(f"  Average bars held: {winners['BarsHeld'].mean():.1f}")
print(f"  Average MFE: {winners['MFE'].mean():.2f}")
print(f"  Average MAE: {winners['MAE'].mean():.2f}")
print(f"  % of MFE captured: {winners['MFE_Captured'].mean():.1f}%")

print(f"\nLosers:")
print(f"  Average bars held: {losers['BarsHeld'].mean():.1f}")
print(f"  Average MFE: {losers['MFE'].mean():.2f}")
print(f"  Average MAE: {losers['MAE'].mean():.2f}")

# Trades exited too early (had MFE > 2x realized profit)
print(f"\n⏰ POTENTIALLY EXITED TOO EARLY")
too_early = df[(df['MFE'] > df['RealizedPoints'] * 2) & (df['RealizedPoints'] > 0)]
if len(too_early) > 0:
    print(f"Count: {len(too_early)} winning trades exited with <50% of max profit")
    print(f"Total missed profit: {(too_early['MFE'] - too_early['RealizedPoints']).sum():.2f} points")
    print(f"Average MFE: {too_early['MFE'].mean():.2f}, Average captured: {too_early['RealizedPoints'].mean():.2f}")

# Best and worst trades
print(f"\n🌟 BEST TRADES")
best = df.nlargest(5, 'RealizedPoints')[['EntryBar', 'RealizedPoints', 'MFE', 'MAE', 'BarsHeld', 'ExitReason']]
print(best.to_string(index=False))

print(f"\n💀 WORST TRADES")
worst = df.nsmallest(5, 'RealizedPoints')[['EntryBar', 'RealizedPoints', 'MFE', 'MAE', 'BarsHeld', 'ExitReason']]
print(worst.to_string(index=False))

# Time-based analysis
df['EntryTime'] = pd.to_datetime(df['EntryTime'])
df['Hour'] = df['EntryTime'].dt.hour

print(f"\n🕐 PERFORMANCE BY HOUR")
hourly = df.groupby('Hour').agg({
    'RealizedPoints': ['count', 'mean', 'sum']
}).round(2)
hourly.columns = ['Trades', 'Avg P/L', 'Total P/L']
print(hourly[hourly['Trades'] > 5])  # Only show hours with >5 trades

# Consecutive losses
print(f"\n📉 DRAWDOWN ANALYSIS")
df = df.sort_values('EntryBar')
df['Cumulative_PL'] = df['RealizedPoints'].cumsum()
df['Running_Max'] = df['Cumulative_PL'].cummax()
df['Drawdown'] = df['Cumulative_PL'] - df['Running_Max']
max_dd = df['Drawdown'].min()
print(f"Maximum Drawdown: {max_dd:.2f} points")
print(f"Maximum Cumulative Profit: {df['Cumulative_PL'].max():.2f} points")

# Validation threshold issue
print(f"\n🔍 VALIDATION THRESHOLD ANALYSIS")
df['FastGrad_at_Exit'] = df['ExitReason'].str.extract(r'FastGrad[<>=]+([+-]?\d+\.\d+)').astype(float)
df['Threshold'] = df['ExitReason'].str.extract(r'need[<>=]+([+-]?\d+\.\d+)').astype(float)

validation_exits = df[df['ExitReason'].str.contains('VALIDATION_FAILED', na=False)]
print(f"Total validation exits: {len(validation_exits)} ({len(validation_exits)/total_trades*100:.1f}%)")
print(f"Average gradient at exit: {validation_exits['FastGrad_at_Exit'].abs().mean():.4f}")
print(f"Average threshold: {validation_exits['Threshold'].abs().mean():.4f}")
print(f"Average P/L on validation exits: {validation_exits['RealizedPoints'].mean():.2f}")

# Trades that were profitable but exited by validation
print(f"\n✅ PROFITABLE BUT EXITED BY VALIDATION")
prof_valid = validation_exits[(validation_exits['RealizedPoints'] > 0) & (validation_exits['MFE'] > 2)]
if len(prof_valid) > 0:
    print(f"Count: {len(prof_valid)}")
    print(f"Average profit captured: {prof_valid['RealizedPoints'].mean():.2f}")
    print(f"Average MFE: {prof_valid['MFE'].mean():.2f}")
    print(f"Average lost: {(prof_valid['MFE'] - prof_valid['RealizedPoints']).mean():.2f}")
    
print(f"\n⚠️  VALIDATION THRESHOLD TOO HIGH?")
print(f"The 0.15 (15%) validation threshold is forcing exits early.")
print(f"Many trades show FastGrad=0.10-0.14 at exit with positive MFE.")
print(f"Consider lowering ValidationMinFastGrad to 0.08-0.10 to let winners run.")

print(f"\n" + "=" * 80)
print("KEY FINDINGS:")
print("=" * 80)
print(f"1. Win Rate: {win_rate:.1f}% - {'✅ Good' if win_rate > 45 else '❌ Needs improvement'}")
print(f"2. Average P/L: {df['RealizedPoints'].mean():.2f} - {'✅ Profitable' if df['RealizedPoints'].mean() > 0 else '❌ Losing'}")
print(f"3. MFE Capture: {mfe_stats['MFE_Captured']:.1f}% - {'❌ Exiting too early' if mfe_stats['MFE_Captured'] < 60 else '✅ Good'}")
print(f"4. Exit Threshold: 0.15 - {'❌ TOO HIGH - Consider 0.08-0.10' if validation_exits['FastGrad_at_Exit'].abs().mean() < 0.12 else '✅ OK'}")
print(f"5. Bars Held: {df['BarsHeld'].mean():.1f} avg - {'❌ Too short' if df['BarsHeld'].mean() < 5 else '✅ OK'}")
