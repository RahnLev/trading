with open(r'indicators_log\CBASTestingIndicator3_MNQ 12-25_8958a10cd8634452a5526e72600a14af_2025-11-19_18-24-30.csv', 'r') as f:
    lines = f.readlines()
    
header = lines[0].strip().split(',')
row843 = lines[843].strip().split(',')

print(f"Header columns: {len(header)}")
print(f"Row 843 columns: {len(row843)}")
print(f"\nMismatch: {len(header) != len(row843)}")

print("\nColumns 38-44 (around curvature_ratio):")
print("Index | Header            | Row 843 Value")
print("-" * 60)
for i in range(38, min(44, len(header))):
    header_val = header[i] if i < len(header) else "MISSING"
    data_val = row843[i] if i < len(row843) else "MISSING"
    print(f"{i:5d} | {header_val:17s} | {data_val}")
