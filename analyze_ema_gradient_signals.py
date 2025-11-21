"""
EMA Gradient-Based Signal Analysis

Identifies entry signals based on:
- SHORT: Both EMA10 (FE) and EMA20 (SE) have negative gradients AND price is below both EMAs
- LONG: Both EMA10 (FE) and EMA20 (SE) have positive gradients AND price is above both EMAs
- FLAT: Any other condition

This helps determine optimal entry conditions based on trend alignment and price position.
"""

import pandas as pd
import numpy as np
import glob
import os
from datetime import datetime
import matplotlib.pyplot as plt

# Configuration
LOG_FOLDER = r"c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\indicators_log"
MIN_GRADIENT_THRESHOLD = 0.0001  # Minimum absolute gradient to consider as "trending"

def find_latest_csv():
    """Find the most recent CSV file (main data file, not signal debug files)"""
    csv_files = glob.glob(os.path.join(LOG_FOLDER, "*.csv"))
    # Exclude signal files - only use main data files
    csv_files = [f for f in csv_files 
                 if not f.endswith("_signals.csv") 
                 and not f.endswith("_signal_debug.csv")
                 and "signal" not in os.path.basename(f).lower()]
    
    if not csv_files:
        raise FileNotFoundError(f"No CSV files found in {LOG_FOLDER}")
    return max(csv_files, key=os.path.getmtime)

def load_data(csv_path):
    """Load and prepare the data"""
    print(f"Loading: {os.path.basename(csv_path)}")
    df = pd.read_csv(csv_path, low_memory=False)
    
    # Convert to numeric
    for col in ['close', 'ema10', 'ema20', 'open', 'high', 'low']:
        df[col] = pd.to_numeric(df[col], errors='coerce')
    
    # Remove rows with missing EMA data
    df = df.dropna(subset=['ema10', 'ema20', 'close'])
    
    print(f"Loaded {len(df):,} bars with valid EMA data")
    return df

def calculate_gradients(df):
    """Calculate EMA gradients (slope/rate of change)"""
    # Calculate gradient as difference from previous bar
    df['fe_gradient'] = df['ema10'].diff()  # Fast EMA (EMA10) gradient
    df['se_gradient'] = df['ema20'].diff()  # Slow EMA (EMA20) gradient
    
    # Normalize by price to get percentage change
    df['fe_gradient_pct'] = (df['fe_gradient'] / df['ema10']) * 100
    df['se_gradient_pct'] = (df['se_gradient'] / df['ema20']) * 100
    
    return df

def identify_signals(df):
    """
    Identify entry signals based on EMA gradients and price position
    
    LONG Signal:
    - FE gradient > 0 (EMA10 rising)
    - SE gradient > 0 (EMA20 rising)
    - Close > EMA10 AND Close > EMA20 (price above both EMAs)
    
    SHORT Signal:
    - FE gradient < 0 (EMA10 falling)
    - SE gradient < 0 (EMA20 falling)
    - Close < EMA10 AND Close < EMA20 (price below both EMAs)
    
    FLAT: All other conditions
    """
    
    # Calculate conditions
    fe_rising = df['fe_gradient'] > MIN_GRADIENT_THRESHOLD
    fe_falling = df['fe_gradient'] < -MIN_GRADIENT_THRESHOLD
    se_rising = df['se_gradient'] > MIN_GRADIENT_THRESHOLD
    se_falling = df['se_gradient'] < -MIN_GRADIENT_THRESHOLD
    
    price_above_fe = df['close'] > df['ema10']
    price_above_se = df['close'] > df['ema20']
    price_below_fe = df['close'] < df['ema10']
    price_below_se = df['close'] < df['ema20']
    
    # Define signals
    df['signal_long'] = fe_rising & se_rising & price_above_fe & price_above_se
    df['signal_short'] = fe_falling & se_falling & price_below_fe & price_below_se
    df['signal_flat'] = ~df['signal_long'] & ~df['signal_short']
    
    # Create signal string for readability
    df['signal'] = 'FLAT'
    df.loc[df['signal_long'], 'signal'] = 'LONG'
    df.loc[df['signal_short'], 'signal'] = 'SHORT'
    
    return df

def analyze_signal_performance(df):
    """Analyze the performance of the gradient-based signals"""
    
    # Detect signal changes (entry points)
    df['signal_change'] = df['signal'] != df['signal'].shift(1)
    df['entry_signal'] = df['signal'].where(df['signal_change'])
    
    # Count signals
    long_entries = (df['entry_signal'] == 'LONG').sum()
    short_entries = (df['entry_signal'] == 'SHORT').sum()
    flat_entries = (df['entry_signal'] == 'FLAT').sum()
    
    print("\n" + "=" * 80)
    print("SIGNAL SUMMARY")
    print("=" * 80)
    print(f"Total bars analyzed: {len(df):,}")
    print(f"\nSignal Distribution:")
    print(f"  LONG bars:  {df['signal_long'].sum():,} ({df['signal_long'].sum()/len(df)*100:.1f}%)")
    print(f"  SHORT bars: {df['signal_short'].sum():,} ({df['signal_short'].sum()/len(df)*100:.1f}%)")
    print(f"  FLAT bars:  {df['signal_flat'].sum():,} ({df['signal_flat'].sum()/len(df)*100:.1f}%)")
    print(f"\nEntry Signals (signal changes):")
    print(f"  LONG entries:  {long_entries}")
    print(f"  SHORT entries: {short_entries}")
    print(f"  FLAT entries:  {flat_entries}")
    
    # Analyze forward returns for each signal type
    # Calculate N-bar forward returns
    for n_bars in [1, 5, 10, 20]:
        df[f'forward_return_{n_bars}'] = (df['close'].shift(-n_bars) - df['close']) / df['close'] * 100
    
    print("\n" + "=" * 80)
    print("FORWARD RETURN ANALYSIS")
    print("=" * 80)
    
    for signal_type in ['LONG', 'SHORT']:
        mask = df['signal'] == signal_type
        if mask.sum() == 0:
            continue
            
        print(f"\n{signal_type} Signal Performance:")
        print(f"  Bars in {signal_type}: {mask.sum():,}")
        
        for n_bars in [1, 5, 10, 20]:
            ret_col = f'forward_return_{n_bars}'
            returns = df.loc[mask, ret_col].dropna()
            
            if len(returns) > 0:
                avg_return = returns.mean()
                win_rate = (returns > 0).sum() / len(returns) * 100
                
                # For SHORT, we want negative returns (price going down)
                if signal_type == 'SHORT':
                    directional_correct = (returns < 0).sum() / len(returns) * 100
                else:
                    directional_correct = (returns > 0).sum() / len(returns) * 100
                
                print(f"    {n_bars}-bar forward: avg={avg_return:+.3f}%, "
                      f"directional_correct={directional_correct:.1f}%")
    
    return df

def find_signal_transitions(df):
    """Find and display signal transition points"""
    
    # Find where signal changes
    df['prev_signal'] = df['signal'].shift(1)
    transitions = df[df['signal'] != df['prev_signal']].copy()
    
    print("\n" + "=" * 80)
    print("RECENT SIGNAL TRANSITIONS (Last 20)")
    print("=" * 80)
    
    # Show last 20 transitions
    recent = transitions.tail(20)
    
    print(f"{'Bar':<8} {'Time':<20} {'From':<6} → {'To':<6} {'Close':>10} "
          f"{'EMA10':>10} {'EMA20':>10} {'FE_Grad':>10} {'SE_Grad':>10}")
    print("-" * 110)
    
    for idx, row in recent.iterrows():
        bar_idx = row.get('bar_index', idx)
        time_str = row.get('ts_local', 'N/A')[:19] if 'ts_local' in row else 'N/A'
        prev_sig = row['prev_signal']
        curr_sig = row['signal']
        close = row['close']
        ema10 = row['ema10']
        ema20 = row['ema20']
        fe_grad = row.get('fe_gradient', 0)
        se_grad = row.get('se_gradient', 0)
        
        print(f"{bar_idx:<8} {time_str:<20} {prev_sig:<6} → {curr_sig:<6} "
              f"{close:>10.2f} {ema10:>10.2f} {ema20:>10.2f} "
              f"{fe_grad:>+10.4f} {se_grad:>+10.4f}")
    
    return transitions

def plot_signals(df, n_bars=500):
    """Plot the last N bars with signals"""
    
    df_plot = df.tail(n_bars).copy()
    
    fig, (ax1, ax2, ax3) = plt.subplots(3, 1, figsize=(16, 12), sharex=True)
    
    # Plot 1: Price and EMAs with signals
    ax1.plot(df_plot.index, df_plot['close'], label='Close', color='black', linewidth=1.5, alpha=0.7)
    ax1.plot(df_plot.index, df_plot['ema10'], label='EMA10 (FE)', color='blue', linewidth=1.2)
    ax1.plot(df_plot.index, df_plot['ema20'], label='EMA20 (SE)', color='red', linewidth=1.2)
    
    # Highlight signal regions
    long_mask = df_plot['signal'] == 'LONG'
    short_mask = df_plot['signal'] == 'SHORT'
    
    ax1.fill_between(df_plot.index, df_plot['close'].min(), df_plot['close'].max(),
                     where=long_mask, alpha=0.2, color='green', label='LONG')
    ax1.fill_between(df_plot.index, df_plot['close'].min(), df_plot['close'].max(),
                     where=short_mask, alpha=0.2, color='red', label='SHORT')
    
    ax1.set_ylabel('Price')
    ax1.set_title(f'EMA Gradient Signals - Last {n_bars} Bars')
    ax1.legend(loc='best')
    ax1.grid(True, alpha=0.3)
    
    # Plot 2: EMA Gradients
    ax2.plot(df_plot.index, df_plot['fe_gradient'], label='FE Gradient (EMA10)', color='blue', alpha=0.7)
    ax2.plot(df_plot.index, df_plot['se_gradient'], label='SE Gradient (EMA20)', color='red', alpha=0.7)
    ax2.axhline(y=0, color='gray', linestyle='--', linewidth=0.8)
    ax2.axhline(y=MIN_GRADIENT_THRESHOLD, color='green', linestyle=':', linewidth=0.8, alpha=0.5)
    ax2.axhline(y=-MIN_GRADIENT_THRESHOLD, color='red', linestyle=':', linewidth=0.8, alpha=0.5)
    ax2.set_ylabel('Gradient')
    ax2.set_title('EMA Gradients (Slope)')
    ax2.legend(loc='best')
    ax2.grid(True, alpha=0.3)
    
    # Plot 3: Signal state
    signal_numeric = df_plot['signal'].map({'LONG': 1, 'FLAT': 0, 'SHORT': -1})
    ax3.fill_between(df_plot.index, 0, signal_numeric, where=signal_numeric > 0,
                     color='green', alpha=0.5, label='LONG', step='post')
    ax3.fill_between(df_plot.index, 0, signal_numeric, where=signal_numeric < 0,
                     color='red', alpha=0.5, label='SHORT', step='post')
    ax3.set_ylabel('Signal')
    ax3.set_xlabel('Bar Index')
    ax3.set_title('Signal State')
    ax3.set_ylim(-1.5, 1.5)
    ax3.legend(loc='best')
    ax3.grid(True, alpha=0.3)
    
    plt.tight_layout()
    
    # Save plot
    output_path = os.path.join(LOG_FOLDER, 'ema_gradient_signals.png')
    plt.savefig(output_path, dpi=150, bbox_inches='tight')
    print(f"\n📊 Chart saved to: {output_path}")
    
    plt.show()

def export_signals(df):
    """Export signal data to CSV"""
    
    # Select relevant columns
    export_cols = [
        'bar_index', 'ts_local', 'close', 'ema10', 'ema20',
        'fe_gradient', 'se_gradient', 'signal',
        'forward_return_1', 'forward_return_5', 'forward_return_10', 'forward_return_20'
    ]
    
    available_cols = [col for col in export_cols if col in df.columns]
    df_export = df[available_cols].copy()
    
    # Export
    output_path = os.path.join(LOG_FOLDER, 'ema_gradient_signals.csv')
    df_export.to_csv(output_path, index=False)
    print(f"\n💾 Signal data exported to: {output_path}")
    print(f"   Total rows: {len(df_export):,}")

def main():
    print("=" * 80)
    print("EMA GRADIENT SIGNAL ANALYSIS")
    print("=" * 80)
    print(f"Analysis Date: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print(f"Minimum Gradient Threshold: {MIN_GRADIENT_THRESHOLD}")
    print()
    
    # Load data
    csv_path = find_latest_csv()
    df = load_data(csv_path)
    
    # Calculate gradients
    print("\nCalculating EMA gradients...")
    df = calculate_gradients(df)
    
    # Identify signals
    print("Identifying entry signals...")
    df = identify_signals(df)
    
    # Analyze performance
    df = analyze_signal_performance(df)
    
    # Show transitions
    transitions = find_signal_transitions(df)
    
    # Export results
    export_signals(df)
    
    # Plot (optional - comment out if not needed)
    try:
        plot_signals(df, n_bars=500)
    except Exception as e:
        print(f"\n⚠️  Plotting skipped: {e}")
    
    print("\n" + "=" * 80)
    print("ANALYSIS COMPLETE")
    print("=" * 80)
    
    # Summary statistics
    print("\nKey Findings:")
    long_pct = (df['signal'] == 'LONG').sum() / len(df) * 100
    short_pct = (df['signal'] == 'SHORT').sum() / len(df) * 100
    flat_pct = (df['signal'] == 'FLAT').sum() / len(df) * 100
    
    print(f"  • Market is in LONG condition {long_pct:.1f}% of the time")
    print(f"  • Market is in SHORT condition {short_pct:.1f}% of the time")
    print(f"  • Market is in FLAT condition {flat_pct:.1f}% of the time")
    
    # Calculate average gradient strength during signals
    long_bars = df[df['signal'] == 'LONG']
    short_bars = df[df['signal'] == 'SHORT']
    
    if len(long_bars) > 0:
        avg_fe_grad_long = long_bars['fe_gradient'].mean()
        avg_se_grad_long = long_bars['se_gradient'].mean()
        print(f"\n  During LONG signals:")
        print(f"    - Avg FE gradient: {avg_fe_grad_long:+.6f}")
        print(f"    - Avg SE gradient: {avg_se_grad_long:+.6f}")
    
    if len(short_bars) > 0:
        avg_fe_grad_short = short_bars['fe_gradient'].mean()
        avg_se_grad_short = short_bars['se_gradient'].mean()
        print(f"\n  During SHORT signals:")
        print(f"    - Avg FE gradient: {avg_fe_grad_short:+.6f}")
        print(f"    - Avg SE gradient: {avg_se_grad_short:+.6f}")

if __name__ == "__main__":
    main()
