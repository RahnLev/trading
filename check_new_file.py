import pandas as pd

df = pd.read_csv(r'indicators_log\CBASTestingIndicator3_MNQ 12-25_8958a10cd8634452a5526e72600a14af_2025-11-19_18-24-30.csv')

print(f"Total rows: {len(df)}")
print(f"\nCurvature ratio statistics:")
print(df['curvature_ratio'].describe())

print(f"\nValue range:")
print(f"Min: {df['curvature_ratio'].min()}")
print(f"Max: {df['curvature_ratio'].max()}")

print(f"\nFirst 20 non-zero values:")
non_zero = df[df['curvature_ratio'] != 0]['curvature_ratio'].head(20)
print(non_zero.tolist())

if len(df) > 842:
    print(f"\nRow 843 curvature_ratio: {df.iloc[842]['curvature_ratio']}")
