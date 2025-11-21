import pandas as pd

df = pd.read_csv(r'indicators_log\CBASTestingIndicator3_MNQ 12-25_6880a0e86a4f4916bfc7ed22fd4aabb2_2025-11-19_17-43-40.csv')

print(f"Row 843 (index 842) curvature_ratio: {df.iloc[842]['curvature_ratio']}")
print(f"\nFull row 843:")
print(df.iloc[842].to_string())
print(f"\n\nRows 840-850 curvature values:")
print(df.iloc[839:849][['bar_index', 'curvature_ratio']].to_string())
