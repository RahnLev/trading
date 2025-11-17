#!/usr/bin/env python3
"""Analyze close-open behaviour against indicator metrics.
Generates oc_diff_analysis.csv with correlation and filter performance stats."""
import csv
from pathlib import Path

LOG_FILE = Path('CBASTestingIndicator3_MNQ 12-25_NA.csv')
OUTPUT_FILE = Path('oc_diff_analysis.csv')
FLAT_TOLERANCE = 0.25  # treat |close-open| <= 0.25 as flat

KEY_METRICS = [
    'netflow', 'objection', 'ema_color', 'momentum', 'momentum_ext',
    'price_to_band', 'attract', 'score_bull', 'score_bear',
    'range_break', 'range_os', 'vpm_smooth'
]


def parse_float(value, default=None):
    if value in (None, '', 'nan', 'NaN'):
        return default
    try:
        return float(value)
    except ValueError:
        return default


def load_records(path: Path):
    records = []
    with path.open() as f:
        reader = csv.DictReader(f)
        for row in reader:
            open_v = parse_float(row.get('open'))
            close_v = parse_float(row.get('close'))
            if open_v is None or close_v is None:
                continue
            record = {
                'oc_diff': close_v - open_v,
                'netflow': parse_float(row.get('netflow'), 0.0),
                'objection': parse_float(row.get('objection'), 0.0),
                'ema_color': parse_float(row.get('ema_color'), 0.0),
                'momentum': parse_float(row.get('momentum'), 0.0),
                'momentum_ext': parse_float(row.get('momentum_ext'), 0.0),
                'price_to_band': parse_float(row.get('price_to_band')),
                'attract': parse_float(row.get('attract'), 0.0),
                'score_bull': parse_float(row.get('score_bull'), 0.0),
                'score_bear': parse_float(row.get('score_bear'), 0.0),
                'range_break': parse_float(row.get('range_break'), 0.0),
                'range_os': parse_float(row.get('range_os'), 0.0),
                'vpm_smooth': parse_float(row.get('vpm_smooth')),
            }
            records.append(record)
    return records


def correlation(xs, ys):
    pairs = [(x, y) for x, y in zip(xs, ys) if x is not None and y is not None]
    if len(pairs) < 3:
        return float('nan')
    xs, ys = zip(*pairs)
    mean_x = sum(xs) / len(xs)
    mean_y = sum(ys) / len(ys)
    num = sum((x - mean_x) * (y - mean_y) for x, y in pairs)
    den_x = sum((x - mean_x) ** 2 for x in xs) ** 0.5
    den_y = sum((y - mean_y) ** 2 for y in ys) ** 0.5
    if den_x == 0 or den_y == 0:
        return float('nan')
    return num / (den_x * den_y)


def rule_match(value, bounds):
    if value is None:
        return False
    min_val, max_val = bounds
    if min_val is not None and value < min_val:
        return False
    if max_val is not None and value > max_val:
        return False
    return True


def evaluate_filters(records, filters, target_positive=True):
    rows = []
    for name, rules in filters:
        tp = fp = tn = fn = 0
        for rec in records:
            diff = rec['oc_diff']
            cond = True
            for field, bounds in rules.items():
                if not rule_match(rec.get(field), bounds):
                    cond = False
                    break
            if target_positive:
                if diff > FLAT_TOLERANCE:
                    if cond:
                        tp += 1
                    else:
                        fn += 1
                elif diff < -FLAT_TOLERANCE:
                    if cond:
                        fp += 1
                    else:
                        tn += 1
            else:  # target negatives
                if diff < -FLAT_TOLERANCE:
                    if cond:
                        tp += 1
                    else:
                        fn += 1
                elif diff > FLAT_TOLERANCE:
                    if cond:
                        fp += 1
                    else:
                        tn += 1
        precision = tp / (tp + fp) if (tp + fp) else 0.0
        recall = tp / (tp + fn) if (tp + fn) else 0.0
        rows.append({
            'filter': name,
            'tp': tp,
            'fp': fp,
            'tn': tn,
            'fn': fn,
            'precision': round(precision, 3),
            'recall': round(recall, 3)
        })
    return rows


def compute_flat_stats(records):
    flat_hits = sum(1 for rec in records if abs(rec['oc_diff']) <= FLAT_TOLERANCE)
    total = len(records)
    pct = flat_hits / total if total else 0.0
    return {
        'flat_count': flat_hits,
        'total': total,
        'flat_pct': round(pct, 3)
    }


def main():
    if not LOG_FILE.exists():
        print(f"Log file {LOG_FILE} not found.")
        return
    records = load_records(LOG_FILE)
    if not records:
        print("No records loaded.")
        return

    oc_diffs = [rec['oc_diff'] for rec in records]
    corr_rows = []
    for metric in KEY_METRICS:
        series = [rec.get(metric) for rec in records]
        corr_rows.append({
            'metric': metric,
            'corr_with_oc_diff': round(correlation(series, oc_diffs), 3)
        })

    bull_filters = [
        ('net>2 & obj<2.5 & ema>=9', {
            'netflow': (2.0, None),
            'objection': (None, 2.5),
            'ema_color': (9.0, None),
        }),
        ('baseline + price_to_band>=0.5', {
            'netflow': (2.0, None),
            'objection': (None, 2.5),
            'ema_color': (9.0, None),
            'price_to_band': (0.5, None),
        }),
        ('baseline + attract>=4.5', {
            'netflow': (2.0, None),
            'objection': (None, 2.5),
            'ema_color': (9.0, None),
            'attract': (4.5, None),
        }),
        ('baseline + score_bull>=3', {
            'netflow': (2.0, None),
            'objection': (None, 2.5),
            'ema_color': (9.0, None),
            'score_bull': (3.0, None),
        }),
        ('baseline + range_break==1', {
            'netflow': (2.0, None),
            'objection': (None, 2.5),
            'ema_color': (9.0, None),
            'range_break': (1.0, 1.0),
        }),
        ('baseline + score_bull>=3 + range_break==1', {
            'netflow': (2.0, None),
            'objection': (None, 2.5),
            'ema_color': (9.0, None),
            'score_bull': (3.0, None),
            'range_break': (1.0, 1.0),
        }),
        ('baseline + vpm_smooth>=baseline', {
            'netflow': (2.0, None),
            'objection': (None, 2.5),
            'ema_color': (9.0, None),
            'vpm_smooth': (0.0, None),  # placeholder, re-evaluated per record
        }),
    ]

    # Adjust vpm_smooth filter: treat as >= median of available values
    vpm_vals = [rec['vpm_smooth'] for rec in records if rec['vpm_smooth'] is not None]
    if vpm_vals:
        vpm_vals.sort()
        median_vpm = vpm_vals[len(vpm_vals)//2]
        for flt in bull_filters:
            if 'vpm_smooth' in flt[1]:
                flt[1]['vpm_smooth'] = (median_vpm, None)

    bear_filters = [
        ('net<-1 & ema<=4 & obj>5', {
            'netflow': (None, -1.0),
            'ema_color': (None, 4.0),
            'objection': (5.0, None),
        }),
        ('baseline + price_to_band<=0.3', {
            'netflow': (None, -1.0),
            'ema_color': (None, 4.0),
            'objection': (5.0, None),
            'price_to_band': (None, 0.3),
        }),
        ('baseline + attract<=4.0', {
            'netflow': (None, -1.0),
            'ema_color': (None, 4.0),
            'objection': (5.0, None),
            'attract': (None, 4.0),
        }),
        ('baseline + score_bear>=3', {
            'netflow': (None, -1.0),
            'ema_color': (None, 4.0),
            'objection': (5.0, None),
            'score_bear': (3.0, None),
        }),
        ('baseline + range_break==1', {
            'netflow': (None, -1.0),
            'ema_color': (None, 4.0),
            'objection': (5.0, None),
            'range_break': (1.0, 1.0),
        }),
        ('baseline + score_bear>=3 + range_break==1', {
            'netflow': (None, -1.0),
            'ema_color': (None, 4.0),
            'objection': (5.0, None),
            'score_bear': (3.0, None),
            'range_break': (1.0, 1.0),
        }),
    ]

    bull_rows = evaluate_filters(records, bull_filters, target_positive=True)
    bear_rows = evaluate_filters(records, bear_filters, target_positive=False)
    flat_stats = compute_flat_stats(records)

    with OUTPUT_FILE.open('w', newline='') as f:
        writer = csv.writer(f)
        writer.writerow(['section', 'metric', 'value', 'extra'])
        for row in corr_rows:
            writer.writerow(['correlation', row['metric'], row['corr_with_oc_diff'], ''])
        for row in bull_rows:
            writer.writerow(['bull_filter', row['filter'], row['precision'], f"recall={row['recall']} tp={row['tp']} fp={row['fp']} tn={row['tn']} fn={row['fn']}"])
        for row in bear_rows:
            writer.writerow(['bear_filter', row['filter'], row['precision'], f"recall={row['recall']} tp={row['tp']} fp={row['fp']} tn={row['tn']} fn={row['fn']}"])
        writer.writerow(['flat_summary', 'flat_pct', flat_stats['flat_pct'], f"flat_count={flat_stats['flat_count']} total={flat_stats['total']}"])
    print(f"Analysis written to {OUTPUT_FILE}")

if __name__ == '__main__':
    main()
