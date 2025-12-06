"""
Analyze 217 bars to identify MISSED GOOD TRADE opportunities.

Strategy: Find bars that had strong momentum signals but were blocked by filters.
Goal: Identify which blockers are preventing good trades vs preventing bad trades.
"""

import pandas as pd
import json
import argparse

# Defaults (current strategy thresholds)
DEFAULTS = {
    'MinAdxForEntry': 16.0,
    'MaxGradientStabilityForEntry': 2.0,
    'MinEntryFastGradientAbs': 0.008,
    'MaxBandwidthForEntry': 0.12,
    'RsiLongFloor': 32.0,
    'RsiShortFloor': 50.0,
}

def load_bars():
    """Load the exported bars CSV."""
    df = pd.read_csv('bar_analysis_3683_3909.csv')
    return df

def check_entry_conditions(row, direction, thresholds):
    """
    Check if a bar would have triggered entry for given direction.
    Returns: (would_enter, blockers_list)
    """
    blockers = []
    
    # Direction-specific gradient check
    if direction == 'LONG':
        if row['fastGrad'] < thresholds['MinEntryFastGradientAbs']:
            blockers.append(f"FastGrad too weak ({row['fastGrad']:.3f} < {thresholds['MinEntryFastGradientAbs']})")
        # RSI floor for LONG
        if row['rsi'] < thresholds['RsiLongFloor']:
            blockers.append(f"RSI below floor ({row['rsi']:.1f} < {thresholds['RsiLongFloor']})")
        # Price should be above fastEMA
        if row['close'] < row['fastEMA']:
            blockers.append(f"Close below fastEMA ({row['close']:.2f} < {row['fastEMA']:.2f})")
        # Acceleration alignment (positive fastGrad should have positive accel)
        if row['fastGrad'] > 0 and row['accel'] < 0:
            blockers.append(f"AccelMisaligned (fastGrad={row['fastGrad']:.3f} but accel={row['accel']:.3f})")
    
    elif direction == 'SHORT':
        if row['fastGrad'] > -thresholds['MinEntryFastGradientAbs']:
            blockers.append(f"FastGrad too weak ({row['fastGrad']:.3f} > -{thresholds['MinEntryFastGradientAbs']})")
        # RSI floor for SHORT (configurable)
        if row['rsi'] < thresholds['RsiShortFloor']:
            blockers.append(f"RSI below short floor ({row['rsi']:.1f} < {thresholds['RsiShortFloor']})")
        # Price should be below fastEMA
        if row['close'] > row['fastEMA']:
            blockers.append(f"Close above fastEMA ({row['close']:.2f} > {row['fastEMA']:.2f})")
        # Acceleration alignment (negative fastGrad should have negative accel)
        if row['fastGrad'] < 0 and row['accel'] > 0:
            blockers.append(f"AccelMisaligned (fastGrad={row['fastGrad']:.3f} but accel={row['accel']:.3f})")
    
    # Universal filters
    if row['adx'] < thresholds['MinAdxForEntry']:
        blockers.append(f"ADX too low ({row['adx']:.1f} < {thresholds['MinAdxForEntry']})")
    
    if row['gradStab'] > thresholds['MaxGradientStabilityForEntry']:
        blockers.append(f"GradStab too high ({row['gradStab']:.3f} > {thresholds['MaxGradientStabilityForEntry']})")
    
    if row['bandwidth'] > thresholds['MaxBandwidthForEntry']:
        blockers.append(f"Bandwidth too high ({row['bandwidth']:.6f} > {thresholds['MaxBandwidthForEntry']})")
    
    would_enter = len(blockers) == 0
    return would_enter, blockers

def identify_strong_moves(df):
    """
    Identify bars that had STRONG subsequent price movement.
    These are the "good trades we missed".
    """
    df = df.copy()
    
    # Calculate forward price movement (next 5 bars)
    df['forward_5bar_pnl_long'] = 0.0
    df['forward_5bar_pnl_short'] = 0.0
    
    for i in range(len(df) - 5):
        current_price = df.iloc[i]['close']
        future_high = df.iloc[i+1:i+6]['close'].max()
        future_low = df.iloc[i+1:i+6]['close'].min()
        
        df.loc[df.index[i], 'forward_5bar_pnl_long'] = future_high - current_price
        df.loc[df.index[i], 'forward_5bar_pnl_short'] = current_price - future_low
    
    return df

def analyze_missed_opportunities(df, thresholds):
    """
    Find bars where:
    1. Strong subsequent movement happened (good opportunity)
    2. Entry was blocked by filters
    """
    results = []
    
    for idx, row in df.iterrows():
        # Check LONG opportunities
        if row['forward_5bar_pnl_long'] >= 4.0:  # 4+ points profit available
            would_enter, blockers = check_entry_conditions(row, 'LONG', thresholds)
            if not would_enter:
                results.append({
                    'barIndex': int(row['barIndex']),
                    'time': row['Time'],
                    'direction': 'LONG',
                    'trendSide': row['trendSide'],
                    'missed_profit': round(row['forward_5bar_pnl_long'], 2),
                    'fastGrad': round(row['fastGrad'], 3),
                    'accel': round(row['accel'], 3),
                    'adx': round(row['adx'], 1),
                    'rsi': round(row['rsi'], 1),
                    'gradStab': round(row['gradStab'], 3),
                    'bandwidth': f"{row['bandwidth']:.6f}",
                    'blockers': blockers,
                    'blocker_count': len(blockers)
                })
        
        # Check SHORT opportunities
        if row['forward_5bar_pnl_short'] >= 4.0:  # 4+ points profit available
            would_enter, blockers = check_entry_conditions(row, 'SHORT', thresholds)
            if not would_enter:
                results.append({
                    'barIndex': int(row['barIndex']),
                    'time': row['Time'],
                    'direction': 'SHORT',
                    'trendSide': row['trendSide'],
                    'missed_profit': round(row['forward_5bar_pnl_short'], 2),
                    'fastGrad': round(row['fastGrad'], 3),
                    'accel': round(row['accel'], 3),
                    'adx': round(row['adx'], 1),
                    'rsi': round(row['rsi'], 1),
                    'gradStab': round(row['gradStab'], 3),
                    'bandwidth': f"{row['bandwidth']:.6f}",
                    'blockers': blockers,
                    'blocker_count': len(blockers)
                })
    
    return pd.DataFrame(results)

def summarize_blocker_patterns(missed_df):
    """Count which blockers appear most frequently in missed opportunities."""
    blocker_freq = {}
    
    for _, row in missed_df.iterrows():
        for blocker in row['blockers']:
            # Extract blocker type (first word/phrase)
            blocker_type = blocker.split('(')[0].strip()
            blocker_freq[blocker_type] = blocker_freq.get(blocker_type, 0) + 1
    
    return dict(sorted(blocker_freq.items(), key=lambda x: x[1], reverse=True))

def main():
    parser = argparse.ArgumentParser(description="Analyze missed trade opportunities with configurable thresholds")
    parser.add_argument('--input', default='bar_analysis_3683_3909.csv', help='Input bars CSV file')
    parser.add_argument('--adx', type=float, default=DEFAULTS['MinAdxForEntry'], help='Min ADX for entry')
    parser.add_argument('--rsi-long', type=float, default=DEFAULTS['RsiLongFloor'], help='RSI long floor')
    parser.add_argument('--rsi-short', type=float, default=DEFAULTS['RsiShortFloor'], help='RSI short floor')
    parser.add_argument('--min-grad', type=float, default=DEFAULTS['MinEntryFastGradientAbs'], help='Min absolute fast gradient')
    parser.add_argument('--max-stab', type=float, default=DEFAULTS['MaxGradientStabilityForEntry'], help='Max gradient stability')
    parser.add_argument('--max-band', type=float, default=DEFAULTS['MaxBandwidthForEntry'], help='Max bandwidth for entry')
    args = parser.parse_args()

    thresholds = {
        'MinAdxForEntry': args.adx,
        'MaxGradientStabilityForEntry': args.max_stab,
        'MinEntryFastGradientAbs': args.min_grad,
        'MaxBandwidthForEntry': args.max_band,
        'RsiLongFloor': args.rsi_long,
        'RsiShortFloor': args.rsi_short,
    }
    print("=" * 80)
    print("MISSED TRADE OPPORTUNITIES ANALYSIS")
    print("=" * 80)
    
    # Load data
    df = pd.read_csv(args.input)
    print(f"\n✓ Loaded {len(df)} bars (range: {df['barIndex'].min()} to {df['barIndex'].max()})")
    print(f"✓ Thresholds: ADX>={thresholds['MinAdxForEntry']}, RSI_LONG>={thresholds['RsiLongFloor']}, RSI_SHORT>={thresholds['RsiShortFloor']}, |fastGrad|>={thresholds['MinEntryFastGradientAbs']}, gradStab<={thresholds['MaxGradientStabilityForEntry']}, bandwidth<={thresholds['MaxBandwidthForEntry']}")
    
    # Calculate forward movement
    df = identify_strong_moves(df)
    print(f"✓ Calculated forward price movement for opportunity detection")
    
    # Find missed opportunities
    missed_df = analyze_missed_opportunities(df, thresholds)
    print(f"\n📊 Found {len(missed_df)} MISSED OPPORTUNITIES (4+ point moves that were blocked)")
    
    if len(missed_df) == 0:
        print("\n✅ No significant missed opportunities in this dataset!")
        return
    
    # Sort by missed profit
    missed_df = missed_df.sort_values('missed_profit', ascending=False)
    
    # Show top 10 worst misses
    print("\n" + "=" * 80)
    print("TOP 10 BIGGEST MISSED OPPORTUNITIES")
    print("=" * 80)
    for idx, row in missed_df.head(10).iterrows():
        print(f"\nBar {row['barIndex']} @ {row['time']} - {row['direction']} ({row['trendSide']} trend)")
        print(f"  💰 Missed Profit: {row['missed_profit']} points")
        print(f"  📈 Metrics: fastGrad={row['fastGrad']} accel={row['accel']} ADX={row['adx']} RSI={row['rsi']}")
        print(f"  ⛔ Blockers ({row['blocker_count']}):")
        for blocker in row['blockers']:
            print(f"     • {blocker}")
    
    # Blocker frequency analysis
    print("\n" + "=" * 80)
    print("BLOCKER FREQUENCY (Which filters blocked the most good trades?)")
    print("=" * 80)
    blocker_freq = summarize_blocker_patterns(missed_df)
    for blocker, count in blocker_freq.items():
        pct = (count / len(missed_df)) * 100
        print(f"  {blocker:40s} : {count:3d} times ({pct:.1f}%)")
    
    # Direction breakdown
    print("\n" + "=" * 80)
    print("DIRECTION BREAKDOWN")
    print("=" * 80)
    long_missed = missed_df[missed_df['direction'] == 'LONG']
    short_missed = missed_df[missed_df['direction'] == 'SHORT']
    print(f"  LONG opportunities missed : {len(long_missed)} (avg profit: {long_missed['missed_profit'].mean():.2f} pts)")
    print(f"  SHORT opportunities missed: {len(short_missed)} (avg profit: {short_missed['missed_profit'].mean():.2f} pts)")
    
    # Export results
    output_file = 'missed_opportunities_analysis.csv'
    missed_df.to_csv(output_file, index=False)
    print(f"\n✓ Detailed results saved to: {output_file}")
    
    # Summary JSON
    summary = {
        'total_bars_analyzed': len(df),
        'missed_opportunities': len(missed_df),
        'total_missed_profit': float(missed_df['missed_profit'].sum()),
        'avg_missed_profit_per_opportunity': float(missed_df['missed_profit'].mean()),
        'blocker_frequency': blocker_freq,
        'long_misses': len(long_missed),
        'short_misses': len(short_missed),
    }
    
    with open('missed_opportunities_summary.json', 'w') as f:
        json.dump(summary, f, indent=2)
    
    print(f"\n✓ Summary saved to: missed_opportunities_summary.json")
    print("\n" + "=" * 80)

if __name__ == '__main__':
    main()
