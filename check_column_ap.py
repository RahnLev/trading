import pandas as pd

df = pd.read_csv(r'indicators_log\CBASTestingIndicator3_MNQ 12-25_8958a10cd8634452a5526e72600a14af_2025-11-19_18-24-30.csv')

# Excel column AP = column index 41 (0-based)
cols = df.columns.tolist()

print("Total columns:", len(cols))
print("\nColumn indices around 41-42:")
for i in range(39, min(45, len(cols))):
    excel_col = chr(65 + i) if i < 26 else 'A' + chr(65 + i - 26)
    print(f"  Index {i} (Excel {excel_col}): {cols[i]}")

print("\nRow 843 (index 842) values:")
print(f"  Column 40 (momentum_ext): {df.iloc[842, 40]}")
print(f"  Column 41 (curvature_ratio): {df.iloc[842, 41]}")

print("\n\nFirst 10 rows of curvature_ratio column:")
print(df['curvature_ratio'].head(10).tolist())
