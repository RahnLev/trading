import pandas as pd

csv_path = r"indicators_log\CBASTestingIndicator3_MNQ 12-25_6880a0e86a4f4916bfc7ed22fd4aabb2_2025-11-19_17-43-40.csv"
df = pd.read_csv(csv_path)

print(f"Total rows: {len(df)}")
print(f"\nCurvature Statistics:")
print(f"  Non-zero: {(df['curvature_ratio'] != 0).sum()}")
print(f"  Min: {df['curvature_ratio'].min():.6f}")
print(f"  Max: {df['curvature_ratio'].max():.6f}")
print(f"  Mean: {df['curvature_ratio'].mean():.6f}")
print(f"  Std: {df['curvature_ratio'].std():.6f}")

print(f"\nValue Distribution:")
print(f"  Above 0.1: {(df['curvature_ratio'] > 0.1).sum()}")
print(f"  Above 0.5: {(df['curvature_ratio'] > 0.5).sum()}")
print(f"  Above 1.0: {(df['curvature_ratio'] > 1.0).sum()}")
print(f"  Below -0.1: {(df['curvature_ratio'] < -0.1).sum()}")
print(f"  Below -0.5: {(df['curvature_ratio'] < -0.5).sum()}")
print(f"  Below -1.0: {(df['curvature_ratio'] < -1.0).sum()}")

print(f"\nFirst 20 non-zero values:")
nonzero = df[df['curvature_ratio'] != 0][['bar_index', 'curvature_ratio', 'close']].head(20)
print(nonzero.to_string(index=False))
