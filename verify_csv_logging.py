"""
Verify CSV logging integrity - check that all variables are logged correctly
"""
import pandas as pd
import glob
import os
import numpy as np
from pathlib import Path

# Find the most recent CSV file
log_folder = r"c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\indicators_log"
csv_files = glob.glob(os.path.join(log_folder, "*.csv"))

if not csv_files:
    print(f"❌ No CSV files found in {log_folder}")
    exit(1)

latest_file = max(csv_files, key=os.path.getctime)
print(f"📁 Analyzing: {os.path.basename(latest_file)}")
print(f"   File size: {os.path.getsize(latest_file):,} bytes")
print()

# Read the CSV
df = pd.read_csv(latest_file, low_memory=False)
print(f"✅ Loaded {len(df):,} rows × {len(df.columns)} columns")
print()

# Define expected columns and their data types
expected_cols = {
    # Basic columns (always present)
    'ts_local': 'string',
    'bar_time_utc': 'string', 
    'bar_index': 'int',
    'open': 'float',
    'high': 'float',
    'low': 'float',
    'close': 'float',
    'vpm': 'float',
    'attract': 'float',
    'objection': 'float',
    'netflow': 'float',
    'ema_color': 'float',
    'momentum': 'float',
    'supertrend': 'float',
    'upper_band': 'float',
    'lower_band': 'float',
    'bull_cross': 'bool',
    'bear_cross': 'bool',
    'bull_cross_raw': 'bool',
    'bear_cross_raw': 'bool',
    'bull_reason': 'string',
    'bear_reason': 'string',
    'realtime_state': 'string',
    'realtime_reason': 'string',
    'regime': 'string',
    'atr30': 'float',
    'ema10': 'float',
    'ema20': 'float',
    'energy_ema_first': 'float',
    'range_max': 'float',
    'range_min': 'float',
    'range_mid': 'float',
    'range_os': 'int',
    'range_count': 'int',
    # Extended logging columns
    'score_bull': 'int',
    'score_bear': 'int',
    'adx': 'float',
    'atr_ratio': 'float',
    'ema_spread': 'float',
    'price_to_band': 'float',
    'momentum_ext': 'float',
    'curvature_ratio': 'float',  # CRITICAL: verify this
    'range_break': 'bool',
    'exit_long': 'bool',
    'exit_short': 'bool'
}

print("=" * 80)
print("COLUMN VERIFICATION")
print("=" * 80)

# Check for missing columns
missing_cols = set(expected_cols.keys()) - set(df.columns)
if missing_cols:
    print(f"❌ MISSING COLUMNS: {missing_cols}")
else:
    print("✅ All expected columns present")

# Check for extra columns
extra_cols = set(df.columns) - set(expected_cols.keys())
if extra_cols:
    print(f"⚠️  EXTRA COLUMNS: {extra_cols}")
else:
    print("✅ No unexpected columns")

print()
print("=" * 80)
print("DATA QUALITY CHECKS")
print("=" * 80)

def check_column(col_name, expected_type):
    """Verify a column has genuine data"""
    if col_name not in df.columns:
        print(f"❌ {col_name}: MISSING")
        return
    
    col = df[col_name]
    
    # Basic stats
    null_count = col.isnull().sum()
    null_pct = (null_count / len(col)) * 100
    
    if expected_type == 'float':
        # Check for numeric validity
        valid_nums = pd.to_numeric(col, errors='coerce')
        invalid_count = valid_nums.isnull().sum() - null_count
        
        if invalid_count > 0:
            print(f"❌ {col_name}: {invalid_count} invalid numeric values")
            return
        
        # Get stats on non-null values
        non_null = valid_nums.dropna()
        if len(non_null) > 0:
            stats = {
                'count': len(non_null),
                'mean': non_null.mean(),
                'std': non_null.std(),
                'min': non_null.min(),
                'max': non_null.max(),
                'null%': f"{null_pct:.1f}%"
            }
            
            # Check for suspicious patterns
            all_zero = (non_null == 0).all()
            all_same = non_null.nunique() == 1
            no_variation = non_null.std() < 0.0001
            
            status = "✅"
            issues = []
            if all_zero:
                status = "⚠️ "
                issues.append("ALL ZEROS")
            elif all_same:
                status = "⚠️ "
                issues.append("ALL SAME VALUE")
            elif no_variation:
                status = "⚠️ "
                issues.append("NO VARIATION")
            
            issue_str = f" ({', '.join(issues)})" if issues else ""
            print(f"{status} {col_name}: min={stats['min']:.6f}, max={stats['max']:.6f}, "
                  f"mean={stats['mean']:.6f}, std={stats['std']:.6f}, null={stats['null%']}{issue_str}")
        else:
            print(f"⚠️  {col_name}: ALL NULL")
    
    elif expected_type == 'int':
        # Check for integer validity
        try:
            valid_ints = pd.to_numeric(col, errors='coerce').astype('Int64')
            invalid_count = valid_ints.isnull().sum() - null_count
            
            if invalid_count > 0:
                print(f"❌ {col_name}: {invalid_count} invalid integer values")
                return
            
            non_null = valid_ints.dropna()
            if len(non_null) > 0:
                print(f"✅ {col_name}: min={non_null.min()}, max={non_null.max()}, "
                      f"unique={non_null.nunique()}, null={null_pct:.1f}%")
            else:
                print(f"⚠️  {col_name}: ALL NULL")
        except:
            print(f"❌ {col_name}: TYPE ERROR")
    
    elif expected_type == 'bool':
        # Check boolean validity (should be 0 or 1)
        valid_vals = col.isin([0, 1, '0', '1', True, False])
        invalid_count = (~valid_vals).sum()
        
        if invalid_count > 0:
            print(f"❌ {col_name}: {invalid_count} invalid boolean values")
            return
        
        true_count = col.isin([1, '1', True]).sum()
        false_count = col.isin([0, '0', False]).sum()
        print(f"✅ {col_name}: true={true_count}, false={false_count}, null={null_count}")
    
    elif expected_type == 'string':
        # Check string validity
        non_null = col.dropna()
        if len(non_null) > 0:
            print(f"✅ {col_name}: unique={non_null.nunique()}, null={null_pct:.1f}%")
        else:
            print(f"⚠️  {col_name}: ALL NULL")

# Check critical trading columns
print("\n--- PRICE DATA ---")
for col in ['open', 'high', 'low', 'close']:
    check_column(col, 'float')

print("\n--- INDICATOR SIGNALS ---")
for col in ['bull_cross', 'bear_cross', 'bull_cross_raw', 'bear_cross_raw']:
    check_column(col, 'bool')

print("\n--- VPM & FLOW ---")
for col in ['vpm', 'attract', 'objection', 'netflow']:
    check_column(col, 'float')

print("\n--- SUPERTREND ---")
for col in ['supertrend', 'upper_band', 'lower_band']:
    check_column(col, 'float')

print("\n--- MOMENTUM & CURVATURE (CRITICAL) ---")
check_column('momentum', 'float')
check_column('momentum_ext', 'float')
check_column('curvature_ratio', 'float')

print("\n--- EXTENDED METRICS ---")
for col in ['adx', 'atr_ratio', 'ema_spread', 'price_to_band']:
    check_column(col, 'float')

print("\n--- EXIT SIGNALS ---")
for col in ['exit_long', 'exit_short', 'range_break']:
    check_column(col, 'bool')

print()
print("=" * 80)
print("CURVATURE RATIO DEEP DIVE")
print("=" * 80)

if 'curvature_ratio' in df.columns:
    curv = pd.to_numeric(df['curvature_ratio'], errors='coerce')
    non_null = curv.dropna()
    
    if len(non_null) > 0:
        print(f"Total rows: {len(curv):,}")
        print(f"Non-null: {len(non_null):,} ({len(non_null)/len(curv)*100:.1f}%)")
        print(f"Null: {curv.isnull().sum():,} ({curv.isnull().sum()/len(curv)*100:.1f}%)")
        print()
        print(f"Min: {non_null.min():.6f}")
        print(f"Max: {non_null.max():.6f}")
        print(f"Mean: {non_null.mean():.6f}")
        print(f"Median: {non_null.median():.6f}")
        print(f"Std: {non_null.std():.6f}")
        print()
        
        # Check distribution
        print("Distribution:")
        print(f"  Positive: {(non_null > 0).sum():,} ({(non_null > 0).sum()/len(non_null)*100:.1f}%)")
        print(f"  Negative: {(non_null < 0).sum():,} ({(non_null < 0).sum()/len(non_null)*100:.1f}%)")
        print(f"  Zero: {(non_null == 0).sum():,} ({(non_null == 0).sum()/len(non_null)*100:.1f}%)")
        print()
        
        # Check for reasonable range
        very_large = (abs(non_null) > 1000).sum()
        if very_large > 0:
            print(f"⚠️  {very_large} values with |curvature| > 1000 (may indicate scaling issue)")
        
        very_small = (abs(non_null) < 0.0001).sum()
        if very_small > len(non_null) * 0.5:
            print(f"⚠️  {very_small} values with |curvature| < 0.0001 (may indicate underscaling)")
        
        # Show sample values
        print("\nLast 10 non-null values:")
        last_10 = non_null.tail(10)
        for idx, val in last_10.items():
            print(f"  Bar {df.loc[idx, 'bar_index']}: {val:.6f}")
    else:
        print("❌ ALL NULL - curvature_ratio not being populated!")
else:
    print("❌ curvature_ratio column NOT FOUND")

print()
print("=" * 80)
print("CROSS-VALIDATION: Momentum vs Momentum_ext")
print("=" * 80)

if 'momentum' in df.columns and 'momentum_ext' in df.columns:
    mom = pd.to_numeric(df['momentum'], errors='coerce')
    mom_ext = pd.to_numeric(df['momentum_ext'], errors='coerce')
    
    # These should be identical (both computed from same source)
    diff = abs(mom - mom_ext)
    max_diff = diff.max()
    
    if max_diff > 0.0001:
        print(f"⚠️  WARNING: momentum and momentum_ext differ by up to {max_diff:.6f}")
        print("   These should be identical - may indicate data corruption")
    else:
        print("✅ momentum and momentum_ext match (as expected)")

print()
print("=" * 80)
print("TEMPORAL CONSISTENCY CHECK")
print("=" * 80)

# Check bar_index is sequential
if 'bar_index' in df.columns:
    bar_idx = df['bar_index'].dropna()
    if len(bar_idx) > 1:
        gaps = bar_idx.diff().dropna()
        expected_gap = gaps.mode()[0] if len(gaps) > 0 else 1
        
        unexpected_gaps = gaps[gaps != expected_gap]
        if len(unexpected_gaps) > 0:
            print(f"⚠️  Found {len(unexpected_gaps)} unexpected gaps in bar_index")
            print(f"   Expected gap: {expected_gap}, found gaps: {unexpected_gaps.unique()}")
        else:
            print(f"✅ bar_index is sequential (gap={expected_gap})")

print()
print("=" * 80)
print("SUMMARY")
print("=" * 80)

# Overall assessment
issues = []

# Check for critical missing columns
critical_cols = ['curvature_ratio', 'momentum', 'vpm', 'netflow', 'bull_cross', 'bear_cross']
for col in critical_cols:
    if col not in df.columns:
        issues.append(f"Missing critical column: {col}")
    elif col in df.columns:
        col_data = pd.to_numeric(df[col], errors='coerce') if col not in ['bull_cross', 'bear_cross'] else df[col]
        if col_data.isnull().all():
            issues.append(f"Column {col} is all NULL")

if issues:
    print("❌ ISSUES FOUND:")
    for issue in issues:
        print(f"   - {issue}")
else:
    print("✅ ALL CRITICAL COLUMNS HAVE VALID DATA")

print()
print(f"File: {os.path.basename(latest_file)}")
print(f"Rows: {len(df):,}")
print(f"Columns: {len(df.columns)}")
print(f"Date range: {df['ts_local'].iloc[0]} to {df['ts_local'].iloc[-1]}")
