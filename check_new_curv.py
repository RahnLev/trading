import pandas as pd

df = pd.read_csv(r'indicators_log\CBASTestingIndicator3_MNQ 12-25_NA_2025-11-19_19-20-19.csv')

print(f"Total rows: {len(df)}")
print(f"\nCurvature ratio statistics:")
print(df['curvature_ratio'].describe())

print(f"\nSample values (every 50th row):")
for i in range(0, min(500, len(df)), 50):
    print(f"Row {i}: {df.iloc[i]['curvature_ratio']}")

print(f"\nMin/Max values:")
print(f"Min: {df['curvature_ratio'].min()}")
print(f"Max: {df['curvature_ratio'].max()}")
