import pandas as pd

df = pd.read_csv(r'indicators_log\CBASTestingIndicator3_MNQ 12-25_b35d17b0758a47049a3e6bb7c14649a1_2025-11-19_14-17-42.csv')

print(f'Total rows: {len(df)}')
print(f'Non-zero curvature: {(df["curvature_ratio"] != 0).sum()}')
print(f'Curvature > 0.1: {(df["curvature_ratio"] > 0.1).sum()}')
print(f'Curvature > 1.0: {(df["curvature_ratio"] > 1.0).sum()}')
print(f'Curvature < -0.1: {(df["curvature_ratio"] < -0.1).sum()}')
print(f'Curvature < -1.0: {(df["curvature_ratio"] < -1.0).sum()}')
print(f'\nMax: {df["curvature_ratio"].max():.2f}')
print(f'Min: {df["curvature_ratio"].min():.2f}')
print(f'Mean: {df["curvature_ratio"].mean():.2f}')
print(f'Median: {df["curvature_ratio"].median():.2f}')
print(f'95th percentile: {df["curvature_ratio"].quantile(0.95):.2f}')
print(f'99th percentile: {df["curvature_ratio"].quantile(0.99):.2f}')

print('\nTop 10 highest curvature values:')
print(df.nlargest(10, 'curvature_ratio')[['bar_time_utc', 'curvature_ratio', 'close']])

print('\nTop 10 lowest (most negative) curvature values:')
print(df.nsmallest(10, 'curvature_ratio')[['bar_time_utc', 'curvature_ratio', 'close']])
