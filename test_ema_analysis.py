"""Quick test of EMA gradient analysis"""
import pandas as pd
import glob
import os

LOG_FOLDER = r"c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\indicators_log"

# Find latest CSV
csv_files = glob.glob(os.path.join(LOG_FOLDER, "*.csv"))
csv_files = [f for f in csv_files if not f.endswith("_signals.csv") and not f.endswith("_signal_debug.csv")]

if csv_files:
    latest = max(csv_files, key=os.path.getctime)
    print(f"Found: {os.path.basename(latest)}")
    
    # Load data
    df = pd.read_csv(latest, low_memory=False)
    print(f"Loaded {len(df)} rows")
    
    # Check for required columns
    required = ['close', 'ema10', 'ema20']
    for col in required:
        if col in df.columns:
            non_null = pd.to_numeric(df[col], errors='coerce').dropna()
            print(f"✓ {col}: {len(non_null)} valid values (min={non_null.min():.2f}, max={non_null.max():.2f})")
        else:
            print(f"✗ {col}: MISSING")
    
    # Calculate gradients
    df['ema10'] = pd.to_numeric(df['ema10'], errors='coerce')
    df['ema20'] = pd.to_numeric(df['ema20'], errors='coerce')
    df['close'] = pd.to_numeric(df['close'], errors='coerce')
    
    df['fe_grad'] = df['ema10'].diff()
    df['se_grad'] = df['ema20'].diff()
    
    # Count signals
    long_signal = (df['fe_grad'] > 0) & (df['se_grad'] > 0) & (df['close'] > df['ema10']) & (df['close'] > df['ema20'])
    short_signal = (df['fe_grad'] < 0) & (df['se_grad'] < 0) & (df['close'] < df['ema10']) & (df['close'] < df['ema20'])
    
    print(f"\nLONG bars: {long_signal.sum()} ({long_signal.sum()/len(df)*100:.1f}%)")
    print(f"SHORT bars: {short_signal.sum()} ({short_signal.sum()/len(df)*100:.1f}%)")
    print(f"FLAT bars: {(~long_signal & ~short_signal).sum()} ({(~long_signal & ~short_signal).sum()/len(df)*100:.1f}%)")
    
    print("\n✓ Analysis working! Run full script for detailed results.")
else:
    print("No CSV files found")
