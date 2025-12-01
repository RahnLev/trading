import csv
import glob
import os
import statistics as stats
from collections import Counter, defaultdict
from datetime import datetime
from typing import Dict, List, Tuple, Optional

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
# Prefer workspace-local strategy_logs next to this script; fallback to user's Documents path
LOG_DIR_LOCAL = os.path.join(SCRIPT_DIR, "strategy_logs")
LOG_DIR_USER = os.path.join(os.path.expanduser("~"), "Documents", "NinjaTrader 8", "bin", "Custom", "strategy_logs")
LOG_DIR = LOG_DIR_LOCAL if os.path.isdir(LOG_DIR_LOCAL) else LOG_DIR_USER


def newest_file(pattern: str, exclude_substr: str = "") -> str:
    files = glob.glob(os.path.join(LOG_DIR, pattern))
    if exclude_substr:
        files = [f for f in files if exclude_substr not in os.path.basename(f)]
    if not files:
        return ""
    return max(files, key=os.path.getmtime)


def parse_float(s: str, default: float = 0.0) -> float:
    try:
        return float(s)
    except Exception:
        return default


def load_trades_summary(path: str) -> List[Dict[str, str]]:
    # Robust loader: ExitReason may contain commas; reconstruct columns
    # NOTE: The current writer omits the final FastExitThreshShort field (bug in strategy),
    # so we expect only 19 data fields. We'll ignore the missing last field for now.
    header = [
        "EntryTime","EntryBar","Direction","EntryPrice","ExitTime","ExitBar","ExitPrice",
        "BarsHeld","RealizedPoints","MFE","MAE","ExitReason","PendingUsed","ConfirmDelta",
        "MinHoldBars","MinEntryFastGrad","ValidationMinFastGrad","ExitConfirmFastEMADelta",
        "FastExitThreshLong"
    ]
    rows: List[Dict[str, str]] = []
    with open(path, newline="", encoding="utf-8") as f:
        first = f.readline()  # skip header line from file
        for line in f:
            line = line.rstrip("\n")
            if not line:
                continue
            parts = line.split(",")
            if len(parts) < 19:
                # malformed
                continue
            if len(parts) > 19:
                # ExitReason may have commas; rejoin middle into a single field
                fixed = []
                fixed.extend(parts[:11])  # up to MAE
                # actual tail count after ExitReason is 7 fields due to strategy writer bug
                exit_reason = ",".join(parts[11:len(parts)-7])
                fixed.append(exit_reason)
                fixed.extend(parts[-7:])
                parts = fixed
            # Map first 19 fields
            row = {header[i]: parts[i] for i in range(19)}
            rows.append(row)
    return rows


def load_main_csv(path: str) -> List[Dict[str, str]]:
    # Robust loader: Notes may contain commas; reconstruct columns
    header = [
        "Timestamp","Bar","Close","FastEMA","SlowEMA","FastGradient","SlowGradient","PrevSignal","NewSignal",
        "MyPosition","ActualPosition","Action","Notes","InWeakDelay","SignalStartBar","LastTradeBar",
        "PriceGradient","BarsSinceEntry","EntryBar","EntryPrice","ExitPending","ExitPendingSide","ExitPendingAnchorFastEMA",
        "ExitPendingEMADelta","MinHoldBars","InExitCooldown","CooldownSecsLeft","TradeMFE","TradeMAE","UnrealizedPoints"
    ]
    rows: List[Dict[str, str]] = []
    with open(path, newline="", encoding="utf-8") as f:
        first = f.readline()
        for line in f:
            line = line.rstrip("\n")
            if not line:
                continue
            parts = line.split(",")
            if len(parts) < 30:
                continue
            if len(parts) > 30:
                fixed = []
                fixed.extend(parts[:12])  # up to Action
                notes = ",".join(parts[12:len(parts)-17])
                fixed.append(notes)
                fixed.extend(parts[-17:])
                parts = fixed
            row = {header[i]: parts[i] for i in range(30)}
            rows.append(row)
    return rows


def summarize_trades(trades: List[Dict[str, str]]) -> Dict[str, object]:
    realized = [parse_float(t["RealizedPoints"]) for t in trades]
    wins = [x for x in realized if x > 0]
    losses = [x for x in realized if x <= 0]
    mfe = [parse_float(t["MFE"]) for t in trades]
    mae = [parse_float(t["MAE"]) for t in trades]
    bars = [int(t["BarsHeld"]) for t in trades]
    pending_used = [int(t.get("PendingUsed", 0)) for t in trades]

    by_reason = defaultdict(list)
    for t in trades:
        reason = t.get("ExitReason", "").split(":", 1)[0] or "UNKNOWN"
        by_reason[reason].append(parse_float(t["RealizedPoints"]))

    by_bars = defaultdict(list)
    for t in trades:
        bh = int(t["BarsHeld"]) if t.get("BarsHeld") else -1
        by_bars[bh].append(parse_float(t["RealizedPoints"]))

    by_pending = defaultdict(list)
    for t in trades:
        p = int(t.get("PendingUsed", 0))
        by_pending[p].append(parse_float(t["RealizedPoints"]))

    def safe_mean(a: List[float]) -> float:
        return sum(a) / len(a) if a else 0.0

    return {
        "count": len(realized),
        "win_rate": (len(wins) / len(realized) * 100.0) if realized else 0.0,
        "avg_realized": safe_mean(realized),
        "median_realized": (stats.median(realized) if realized else 0.0),
        "avg_mfe": safe_mean(mfe),
        "avg_mae": safe_mean(mae),
        "avg_bars": safe_mean(bars),
        "by_reason": {k: {"n": len(v), "avg": safe_mean(v)} for k, v in by_reason.items()},
        "by_bars": {k: {"n": len(v), "avg": safe_mean(v)} for k, v in sorted(by_bars.items())},
        "by_pending": {k: {"n": len(v), "avg": safe_mean(v)} for k, v in by_pending.items()},
    }


# ---------------- Indicator Snapshot Parsing & Correlation -----------------
def parse_indicator_snapshot(text: str) -> Optional[Dict[str, float]]:
    """Parse pipe-delimited indicator snapshot appended to ENTRY/EXIT notes.
    Accepts extended keys (VOL,VPM) while remaining backward-compatible.
    Returns dict or None if mandatory core keys absent."""
    if 'ATR=' not in text or 'ADX=' not in text:
        return None
    try:
        parts = text.split('|')
        kv_parts = parts if '=' in parts[0] else parts[1:]
        data: Dict[str, float] = {}
        for kv in kv_parts:
            if '=' not in kv:
                continue
            k, v = kv.split('=', 1)
            k = k.strip()
            try:
                data[k] = float(v)
            except Exception:
                continue
        core = ["ATR","ADX","RSI","MACDH","BW","Accel","GradStab"]
        if all(k in data for k in core):
            # Fill optional volume fields if missing (set to 0.0)
            if 'VOL' not in data:
                data['VOL'] = 0.0
            if 'VPM' not in data:
                data['VPM'] = data['VOL']  # fallback
            return data
    except Exception:
        return None
    return None

def correlate_entry_indicators(trades: List[Dict[str,str]], main_rows: List[Dict[str,str]]) -> Dict[str, object]:
    # Map entry bar -> snapshot text from DECISIONS CSV (Action=ENTRY)
    snapshot_by_bar: Dict[int, Dict[str,float]] = {}
    direction_by_bar: Dict[int,str] = {}
    for r in main_rows:
        if r.get('Action') == 'ENTRY':
            try:
                b = int(r.get('Bar', -1))
            except Exception:
                continue
            notes = r.get('Notes','')
            snap = parse_indicator_snapshot(notes)
            if snap:
                snapshot_by_bar[b] = snap
                direction_by_bar[b] = r.get('MyPosition','')
    records: List[Dict[str, float]] = []
    for t in trades:
        try:
            ebar = int(t.get('EntryBar', -1))
        except Exception:
            continue
        if ebar in snapshot_by_bar:
            snap = snapshot_by_bar[ebar]
            rec = snap.copy()
            rec['BarsHeld'] = float(t.get('BarsHeld',0)) if t.get('BarsHeld') else 0.0
            rec['PnL'] = parse_float(t.get('RealizedPoints','0'))
            rec['Dir'] = 1.0 if (t.get('Direction','') == 'LONG') else (-1.0 if t.get('Direction','') == 'SHORT' else 0.0)
            records.append(rec)
    if not records:
        return {'count':0}
    # Compute correlations & quartile stats
    metrics = ['ADX','BW','Accel','GradStab','ATR','RSI','MACDH','VPM','VOL']
    result: Dict[str, object] = {'count': len(records), 'metrics': {}}
    # Precompute arrays
    for m in metrics:
        vals = [r[m] for r in records]
        pnl = [r['PnL'] for r in records]
        held = [r['BarsHeld'] for r in records]
        if not vals:
            continue
        # Pearson correlation with PnL / BarsHeld (simple)
        def pearson(a: List[float], b: List[float]) -> float:
            if len(a) < 3: return 0.0
            ma = sum(a)/len(a); mb = sum(b)/len(b)
            num = sum((x-ma)*(y-mb) for x,y in zip(a,b))
            den_a = sum((x-ma)**2 for x in a)
            den_b = sum((y-mb)**2 for y in b)
            den = (den_a*den_b)**0.5
            return num/den if den != 0 else 0.0
        corr_pnl = pearson(vals, pnl)
        corr_held = pearson(vals, held)
        # Quartiles
        sv = sorted(vals)
        q1 = sv[int(0.25*(len(sv)-1))]
        q3 = sv[int(0.75*(len(sv)-1))]
        low_group = [r for r in records if r[m] <= q1]
        high_group = [r for r in records if r[m] >= q3]
        def avg(a,key):
            return sum(x[key] for x in a)/len(a) if a else 0.0
        suggestion = ''
        # Heuristic suggestions
        if corr_pnl > 0.15 and avg(high_group,'PnL') > avg(low_group,'PnL'):
            suggestion = f"Consider min {m} >= {q1:.2f}" if corr_pnl > 0 else ''
        elif corr_pnl < -0.15 and avg(low_group,'PnL') > avg(high_group,'PnL'):
            suggestion = f"Consider max {m} <= {q3:.2f}" if corr_pnl < 0 else ''
        elif m == 'GradStab':
            # Lower stability (std dev) usually better: filter high variance
            if avg(low_group,'PnL') > avg(high_group,'PnL'):
                suggestion = f"Filter GradStab > {q3:.4f}" 
        elif m == 'Accel':
            # Positive acceleration for LONG, negative for SHORT might help; simplistic global rule
            if corr_pnl > 0 and avg(high_group,'BarsHeld') > avg(low_group,'BarsHeld'):
                suggestion = "Prefer positive Accel near entry" 
        result['metrics'][m] = {
            'corr_pnl': round(corr_pnl,3),
            'corr_bars': round(corr_held,3),
            'q1': q1,
            'q3': q3,
            'avg_low_pnl': round(avg(low_group,'PnL'),3),
            'avg_high_pnl': round(avg(high_group,'PnL'),3),
            'avg_low_bars': round(avg(low_group,'BarsHeld'),2),
            'avg_high_bars': round(avg(high_group,'BarsHeld'),2),
            'suggestion': suggestion
        }
    return result


def simulate_filter_grid(trades: List[Dict[str,str]], main_rows: List[Dict[str,str]]) -> List[Dict[str, object]]:
    """Counterfactual experiment: evaluate PnL if certain entry filters had been applied
    at the time of ENTRY. Uses the indicator snapshot parsed from the ENTRY row in DECISIONS CSV.
    Returns a ranked list of scenarios with count and avg PnL."""
    # Map entry bar -> (snapshot, fastGrad)
    snap_by_bar: Dict[int, Dict[str,float]] = {}
    fg_by_bar: Dict[int, float] = {}
    dir_by_bar: Dict[int, str] = {}
    for r in main_rows:
        if r.get('Action') != 'ENTRY':
            continue
        try:
            b = int(r.get('Bar', -1))
        except Exception:
            continue
        snap = parse_indicator_snapshot(r.get('Notes',''))
        if snap:
            snap_by_bar[b] = snap
            fg_by_bar[b] = parse_float(r.get('FastGradient','0'))
            dir_by_bar[b] = r.get('MyPosition','')
    # Join with trade outcomes
    entries: List[Dict[str, object]] = []
    for t in trades:
        try:
            eb = int(t.get('EntryBar','-1'))
        except Exception:
            continue
        if eb in snap_by_bar:
            rec = {
                'bar': eb,
                'pnl': parse_float(t.get('RealizedPoints','0')),
                'fg': fg_by_bar.get(eb, 0.0),
            }
            rec.update(snap_by_bar[eb])
            entries.append(rec)
    if not entries:
        return []
    # Grid of scenarios
    import itertools
    max_gradstab_vals = [None, 1.85, 1.46, 1.40]
    min_rsi_vals = [None, 46.0, 50.0]
    max_atr_vals = [None, 13.57, 12.00]
    min_adx_vals = [None, 12.0, 15.0, 18.0]
    min_fastgrad_vals = [None, 0.45, 0.50, 0.60]
    scenarios = []
    for mg, rsi, atr, adx_min, fg_min in itertools.product(max_gradstab_vals, min_rsi_vals, max_atr_vals, min_adx_vals, min_fastgrad_vals):
        label_parts = []
        if mg is not None: label_parts.append(f"GradStab<= {mg}")
        if rsi is not None: label_parts.append(f"RSI>= {rsi}")
        if atr is not None: label_parts.append(f"ATR<= {atr}")
        if adx_min is not None: label_parts.append(f"ADX>= {adx_min}")
        if fg_min is not None: label_parts.append(f"|FastGrad|>= {fg_min}")
        label = ", ".join(label_parts) if label_parts else "No extra filters"
        # Apply selection
        sel = []
        for e in entries:
            if mg is not None and e.get('GradStab', 0.0) > mg: continue
            if rsi is not None and e.get('RSI', 0.0) < rsi: continue
            if atr is not None and e.get('ATR', 0.0) > atr: continue
            if adx_min is not None and e.get('ADX', 0.0) < adx_min: continue
            if fg_min is not None and abs(e.get('fg',0.0)) < fg_min: continue
            sel.append(e)
        if not sel:
            continue
        pnl = [x['pnl'] for x in sel]
        avg = sum(pnl)/len(pnl) if pnl else 0.0
        wr = sum(1 for x in pnl if x>0)/len(pnl)*100.0 if pnl else 0.0
        scenarios.append({'label': label, 'n': len(sel), 'avg': round(avg,3), 'win_rate': round(wr,1)})
    # Rank: prioritize average PnL then count
    scenarios.sort(key=lambda s: (s['avg'], s['n']), reverse=True)
    return scenarios


def analyze_validation_fails(main_rows: List[Dict[str, str]]) -> Dict[str, object]:
    # Parse notes for validation failures to estimate better validation thresholds
    # We only consider borderline cases in the "same" direction (weak trend):
    #  - LONG: fastGrad > 0 but small -> collect grad
    #  - SHORT: fastGrad < 0 but small in magnitude -> collect |grad|
    long_pos_grads, short_neg_abs = [], []
    total_val_fail = 0
    for r in main_rows:
        if r.get("Action") != "EXIT":
            continue
        notes = r.get("Notes", "")
        if not notes.startswith("VALIDATION_FAILED"):
            continue
        total_val_fail += 1
        # Expect notes like: VALIDATION_FAILED: FastGrad<=X(need>0.25) OR FastGrad>=X(need<-0.25)
        # Extract the number after the comparator
        seg = notes.split(":", 1)[-1]
        # find pattern like FastGrad<=-0.0123 or FastGrad>=0.0345
        grad = None
        for tok in seg.replace(",", " ").split():
            if tok.startswith("FastGrad<") or tok.startswith("FastGrad>"):
                # split by =
                if "=" in tok:
                    try:
                        grad = float(tok.split("=")[-1])
                    except Exception:
                        pass
                break
        if grad is None:
            # fallback: try to find first parenthesis content
            try:
                inside = seg.split("(")[0]
                gtxt = inside.split("=")[-1]
                grad = float(gtxt)
            except Exception:
                continue
        # Decide LONG/SHORT by position at the time and collect borderline magnitudes
        pos = r.get("MyPosition", "")
        if pos == "LONG" and grad is not None:
            if grad > 0:
                long_pos_grads.append(grad)
        elif pos == "SHORT" and grad is not None:
            if grad < 0:
                short_neg_abs.append(abs(grad))

    def percentile(a: List[float], p: float) -> float:
        if not a:
            return 0.0
        a2 = sorted(a)
        k = (len(a2) - 1) * p
        f = int(k)
        c = min(f + 1, len(a2) - 1)
        if f == c:
            return a2[int(k)]
        d0 = a2[f] * (c - k)
        d1 = a2[c] * (k - f)
        return d0 + d1

    # Suggest a softer validation threshold = max of 60th percentile magnitudes (rounded to 0.01)
    # So if most validation exits occurred when gradient ~0.15, we suggest 0.15 as candidate instead of 0.25
    long_sugg = percentile(long_pos_grads, 0.6)
    short_sugg = percentile(short_neg_abs, 0.6)
    combined_sugg = max(long_sugg, short_sugg)
    combined_sugg = round(combined_sugg, 2)

    return {
        "total_val_fail": total_val_fail,
        "long_samples": len(long_pos_grads),
        "short_samples": len(short_neg_abs),
        "long_median": (stats.median(long_pos_grads) if long_pos_grads else 0.0),
        "short_median": (stats.median(short_neg_abs) if short_neg_abs else 0.0),
        "suggest_validation_min_abs": combined_sugg,
    }


def analyze_exit_pending(main_rows: List[Dict[str, str]]) -> Dict[str, object]:
    # Look at EXIT_PENDING confirmations vs waits deltas
    wait_deltas = []
    confirm_deltas = []
    for r in main_rows:
        if r.get("Action") == "EXIT_PENDING":
            notes = r.get("Notes", "")
            if "WAIT:FastEMAΔ=" in notes:
                try:
                    part = notes.split("WAIT:FastEMAΔ=")[-1]
                    val = float(part.split("<")[0])
                    wait_deltas.append(val)
                except Exception:
                    pass
            elif notes.startswith("INIT:"):
                pass
        elif r.get("Action") == "EXIT":
            notes = r.get("Notes", "")
            if notes.startswith("CONFIRMED_") or "CONFIRMED:" in notes:
                # Also available in summary as ConfirmDelta; but take from per-bar if present
                try:
                    if "Δ" in notes:
                        part = notes.split("Δ")[-1]
                        # may start like {emaDrop} or {emaRise}
                        # strip non-number prefixes
                        num = "".join(ch for ch in part if (ch.isdigit() or ch in ".-"))
                        if num:
                            confirm_deltas.append(float(num))
                except Exception:
                    pass
    def safe_mean(a: List[float]) -> float:
        return sum(a) / len(a) if a else 0.0
    # Suggest a confirm delta slightly below the 40th percentile of confirmed deltas, but above mean of waits
    def percentile(a: List[float], p: float) -> float:
        if not a:
            return 0.0
        a2 = sorted(a)
        k = (len(a2) - 1) * p
        f = int(k)
        c = min(f + 1, len(a2) - 1)
        if f == c:
            return a2[int(k)]
        d0 = a2[f] * (c - k)
        d1 = a2[c] * (k - f)
        return d0 + d1

    sugg = percentile(confirm_deltas, 0.4)
    sugg = max(sugg, safe_mean(wait_deltas))
    sugg = round(sugg, 2)

    return {
        "wait_n": len(wait_deltas),
        "wait_mean": round(safe_mean(wait_deltas), 3),
        "confirm_n": len(confirm_deltas),
        "confirm_mean": round(safe_mean(confirm_deltas), 3),
        "suggest_exit_confirm_delta": sugg,
    }


def analyze_entry_gradient_effects(trades: List[Dict[str, str]], main_rows: List[Dict[str, str]]):
    # Map EntryBar -> FastGradient at ENTRY action
    entry_grad_by_bar: Dict[int, float] = {}
    for r in main_rows:
        if r.get("Action") == "ENTRY":
            try:
                bar = int(r.get("Bar", -1))
                fg = parse_float(r.get("FastGradient", "0"))
                entry_grad_by_bar[bar] = fg
            except Exception:
                continue
    pairs: List[Tuple[float, float]] = []
    for t in trades:
        try:
            ebar = int(t.get("EntryBar", -1))
            if ebar in entry_grad_by_bar:
                fg = entry_grad_by_bar[ebar]
                pnl = parse_float(t.get("RealizedPoints", "0"))
                pairs.append((fg, pnl))
        except Exception:
            continue
    if not pairs:
        return None
    # Bucket by fast gradient magnitude
    buckets = {"<0.45": [], "0.45-0.60": [], ">=0.60": []}
    for fg, pnl in pairs:
        af = abs(fg)
        if af < 0.45:
            buckets["<0.45"].append(pnl)
        elif af < 0.60:
            buckets["0.45-0.60"].append(pnl)
        else:
            buckets[">=0.60"].append(pnl)
    def safe_mean(a: List[float]) -> float:
        return sum(a) / len(a) if a else 0.0
    summary = {k: {"n": len(v), "avg": safe_mean(v)} for k, v in buckets.items()}
    # Suggest threshold at the lowest bucket edge whose avg >= overall avg
    overall_avg = safe_mean([p for _, p in pairs])
    suggestion = 0.45
    if summary[">=0.60"]["n"] >= 5 and summary[">=0.60"]["avg"] > overall_avg:
        suggestion = 0.60
    elif summary["0.45-0.60"]["n"] >= 5 and summary["0.45-0.60"]["avg"] > overall_avg:
        suggestion = 0.50
    return {"buckets": summary, "overall_avg": overall_avg, "suggest_min_entry_fast_grad": suggestion}


def find_longest_trades(trades: List[Dict[str,str]], top_n: int = 10) -> List[Dict[str,str]]:
    def key(t: Dict[str,str]) -> int:
        try:
            return int(t.get("BarsHeld", -1))
        except Exception:
            return -1
    return sorted(trades, key=key, reverse=True)[:top_n]


def trade_gradient_tolerance(main_rows: List[Dict[str,str]], entry_bar: int, exit_bar: int,
                             near_thr: float = 0.20) -> Tuple[float, int]:
    if entry_bar < 0 or exit_bar < entry_bar:
        return (0.0, 0)
    min_abs_fg = None
    near_cnt = 0
    for r in main_rows:
        try:
            b = int(r.get("Bar", -1))
        except Exception:
            continue
        if b < entry_bar or b > exit_bar:
            continue
        fg = parse_float(r.get("FastGradient","0"))
        af = abs(fg)
        if min_abs_fg is None or af < min_abs_fg:
            min_abs_fg = af
        if af <= near_thr:
            near_cnt += 1
    return ((min_abs_fg if min_abs_fg is not None else 0.0), near_cnt)


def find_longest_trend_segments(main_rows: List[Dict[str,str]], min_len: int = 30, top_n: int = 10) -> List[Dict[str,object]]:
    # Define a "trend" bar when fastEMA and slowEMA are aligned and fastGradient sign matches direction
    bars = []
    for r in main_rows:
        try:
            b = int(r.get("Bar", -1))
        except Exception:
            continue
        fe = parse_float(r.get("FastEMA","0")); se = parse_float(r.get("SlowEMA","0")); fg = parse_float(r.get("FastGradient","0"))
        if fe > se and fg > 0:
            bars.append((b, 1, abs(fg)))  # bull
        elif fe < se and fg < 0:
            bars.append((b, -1, abs(fg))) # bear
        else:
            bars.append((b, 0, 0.0))
    # Group contiguous segments by direction
    segs: List[Dict[str,object]] = []
    i = 0
    n = len(bars)
    while i < n:
        start_i = i
        _, d, _ = bars[i]
        if d == 0:
            i += 1
            continue
        sum_abs = 0.0; count = 0
        start_bar = bars[i][0]
        while i < n and bars[i][1] == d:
            sum_abs += bars[i][2]; count += 1; i += 1
        end_bar = bars[i-1][0]
        length = count
        if length >= min_len:
            segs.append({
                "dir": ("LONG" if d == 1 else "SHORT"),
                "start_bar": start_bar,
                "end_bar": end_bar,
                "length": length,
                "mean_abs_fast": (sum_abs/length if length else 0.0)
            })
    segs.sort(key=lambda s: s["length"], reverse=True)
    return segs[:top_n]


def infer_miss_reason(main_rows: List[Dict[str,str]], seg_start_bar: int, seg_end_bar: int) -> Optional[str]:
    # Look around segment start for explicit skip/cancel reasons
    window_start = seg_start_bar - 3
    window_end = seg_start_bar + 5
    reasons = []
    for r in main_rows:
        try:
            b = int(r.get("Bar", -1))
        except Exception:
            continue
        if b < window_start or b > window_end:
            continue
        act = r.get("Action","")
        if act in ("ENTRY_CANCELLED","ENTRY_SKIPPED","EXIT_SUPPRESSED","EXIT_PENDING"):
            reasons.append(r.get("Notes",""))
    # Prioritize specific reasons
    for kw in ("EntryFastGradTooHigh", "Fast gradient too weak", "COOLDOWN", "WEAK REVERSAL", "TradeAlreadyThisBar", "SignalStartBar", "ENTRY_DECISION"):
        for s in reasons:
            if kw in s:
                return s
    # Fallback: return first reason if any
    return reasons[0] if reasons else None

def export_longest_trades_csv(trades: List[Dict[str,str]], main_rows: List[Dict[str,str]], out_path: str, top_n: int = 50) -> int:
    sel = find_longest_trades(trades, top_n=top_n)
    header = [
        "EntryTime","EntryBar","Direction","EntryPrice","ExitTime","ExitBar","ExitPrice",
        "BarsHeld","RealizedPoints","MFE","MAE","MAE_Pct","MinAbsFastGrad","NearThresholdTouches","ExitReason"
    ]
    rows_out: List[List[str]] = []
    for t in sel:
        try:
            ebar = int(t.get("EntryBar", -1)) if t.get("EntryBar") else -1
            xbar = int(t.get("ExitBar", -1)) if t.get("ExitBar") else -1
        except Exception:
            ebar, xbar = -1, -1
        epx = parse_float(t.get("EntryPrice","0"))
        mae = abs(parse_float(t.get("MAE","0")))
        tol_pct = (mae/epx*100.0) if epx else 0.0
        min_abs_fg, near_cnt = trade_gradient_tolerance(main_rows, ebar, xbar)
        rows_out.append([
            t.get("EntryTime",""), t.get("EntryBar",""), t.get("Direction",""), t.get("EntryPrice",""),
            t.get("ExitTime",""), t.get("ExitBar",""), t.get("ExitPrice",""), t.get("BarsHeld",""),
            t.get("RealizedPoints",""), t.get("MFE",""), t.get("MAE",""), f"{tol_pct:.4f}", f"{min_abs_fg:.6f}", str(near_cnt),
            t.get("ExitReason", "")
        ])
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(header)
        w.writerows(rows_out)
    return len(rows_out)

def export_missed_trends_csv(main_rows: List[Dict[str,str]], trades: List[Dict[str,str]], out_path: str, min_len: int = 12) -> int:
    # Build trade spans and bar -> timestamp map
    trade_spans: List[Tuple[int,int]] = []
    for t in trades:
        try:
            ebar = int(t.get("EntryBar", -1)); xbar = int(t.get("ExitBar", -1))
            if ebar >= 0 and xbar >= ebar:
                trade_spans.append((ebar, xbar))
        except Exception:
            pass
    bar_time: Dict[int,str] = {}
    for r in main_rows:
        try:
            b = int(r.get("Bar", -1))
        except Exception:
            continue
        if b >= 0 and "Timestamp" in r:
            bar_time[b] = r["Timestamp"]

    # Build all trend segments >= min_len
    segments: List[Dict[str,object]] = []
    cur_dir = 0; cur_start = None; acc_abs = 0.0; acc_count = 0; cur_min_abs = None
    prev_bar = None
    for r in main_rows:
        try:
            b = int(r.get("Bar", -1))
        except Exception:
            continue
        fe = parse_float(r.get("FastEMA","0")); se = parse_float(r.get("SlowEMA","0")); fg = parse_float(r.get("FastGradient","0"))
        d = 1 if (fe > se and fg > 0) else (-1 if (fe < se and fg < 0) else 0)
        af = abs(fg) if d != 0 else 0.0
        if d == 0 or (cur_dir != 0 and d != cur_dir) or (prev_bar is not None and b != prev_bar + 1):
            # flush current segment
            if cur_dir != 0 and acc_count >= min_len and cur_start is not None and prev_bar is not None:
                segments.append({
                    "dir": ("LONG" if cur_dir == 1 else "SHORT"),
                    "start_bar": cur_start,
                    "end_bar": prev_bar,
                    "length": acc_count,
                    "mean_abs_fast": (acc_abs/acc_count if acc_count else 0.0),
                    "min_abs_fast": (cur_min_abs if cur_min_abs is not None else 0.0)
                })
            # reset
            cur_dir = 0; cur_start = None; acc_abs = 0.0; acc_count = 0; cur_min_abs = None
        if d != 0:
            if cur_dir == 0:
                cur_dir = d; cur_start = b; acc_abs = 0.0; acc_count = 0; cur_min_abs = None
            acc_abs += af; acc_count += 1
            cur_min_abs = af if (cur_min_abs is None or af < cur_min_abs) else cur_min_abs
        prev_bar = b
    # flush tail
    if cur_dir != 0 and acc_count >= min_len and cur_start is not None and prev_bar is not None:
        segments.append({
            "dir": ("LONG" if cur_dir == 1 else "SHORT"),
            "start_bar": cur_start,
            "end_bar": prev_bar,
            "length": acc_count,
            "mean_abs_fast": (acc_abs/acc_count if acc_count else 0.0),
            "min_abs_fast": (cur_min_abs if cur_min_abs is not None else 0.0)
        })

    # Filter to missed segments and write CSV
    header = [
        "Direction","StartBar","EndBar","Bars","TrendBarsMissed",
        "StartTime","EndTime","MeanAbsFastGrad","MinAbsFastGrad","MissReason"
    ]
    out_rows: List[List[str]] = []
    for s in segments:
        a = s["start_bar"]; b = s["end_bar"]
        overlap = any(not (b < ta or a > tb) for ta, tb in trade_spans)
        if overlap:
            continue
        reason = infer_miss_reason(main_rows, a, b) or ""
        out_rows.append([
            s["dir"], str(a), str(b), str(s["length"]), str(s["length"]),
            bar_time.get(a, ""), bar_time.get(b, ""),
            f"{s['mean_abs_fast']:.6f}", f"{s['min_abs_fast']:.6f}", reason
        ])
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(header)
        w.writerows(out_rows)
    return len(out_rows)


def detect_missed_entries(main_rows: List[Dict[str,str]], trades: List[Dict[str,str]], lookahead: int = 20,
                          min_fast_grad_abs: float = 0.45, min_favorable_move: float = 10.0) -> List[Dict[str,object]]:
    """Scan DECISIONS rows for ENTRY_FILTER_BLOCKED lines that meet alignment & gradient criteria
    and evaluate subsequent favorable move within a lookahead window. Currently uses Close-only
    price changes (points) as proxy. Marks an opportunity as 'MISSED_WIN' if favorable move >= min_favorable_move
    and no actual ENTRY occurred within first 3 bars after block.
    Returns list of dicts for CSV export."""
    # Build quick set of actual entry bars for direction to exclude near blocks
    entry_bars_long = set(); entry_bars_short = set()
    for r in main_rows:
        if r.get('Action') == 'ENTRY':
            try:
                b = int(r.get('Bar', -1))
            except Exception:
                continue
            pos = r.get('MyPosition','')
            if pos == 'LONG': entry_bars_long.add(b)
            elif pos == 'SHORT': entry_bars_short.add(b)
    # Index rows by bar for fast forward lookup
    rows_by_bar: Dict[int,Dict[str,str]] = {}
    ordered_bars: List[int] = []
    for r in main_rows:
        try:
            b = int(r.get('Bar','-1'))
        except Exception:
            continue
        if b < 0: continue
        rows_by_bar[b] = r
        ordered_bars.append(b)
    ordered_bars.sort()
    results: List[Dict[str,object]] = []
    for r in main_rows:
        if r.get('Action') != 'ENTRY_FILTER_BLOCKED':
            continue
        try:
            bar = int(r.get('Bar','-1'))
        except Exception:
            continue
        fg = parse_float(r.get('FastGradient','0'))
        fe = parse_float(r.get('FastEMA','0'))
        se = parse_float(r.get('SlowEMA','0'))
        close_px = parse_float(r.get('Close','0'))
        dir_candidate = None
        if fg > 0 and fe > se: dir_candidate = 'LONG'
        elif fg < 0 and fe < se: dir_candidate = 'SHORT'
        if dir_candidate is None:  # misaligned, skip
            continue
        if abs(fg) < min_fast_grad_abs:
            continue
        # If we took an entry immediately after (within 3 bars) in same direction, skip (not truly missed)
        took_entry = False
        for ahead in range(1, 4):
            nb = bar + ahead
            if dir_candidate == 'LONG' and nb in entry_bars_long:
                took_entry = True; break
            if dir_candidate == 'SHORT' and nb in entry_bars_short:
                took_entry = True; break
        if took_entry:
            continue
        # Evaluate favorable move over lookahead window
        max_fav = 0.0; bars_to_peak = 0
        for ahead in range(1, lookahead+1):
            nb = bar + ahead
            if nb not in rows_by_bar: break
            future_close = parse_float(rows_by_bar[nb].get('Close','0'))
            move = (future_close - close_px) if dir_candidate == 'LONG' else (close_px - future_close)
            if move > max_fav:
                max_fav = move; bars_to_peak = ahead
        status = 'MISSED_WIN' if max_fav >= min_favorable_move else 'MISSED_FLAT'
        notes = r.get('Notes','')
        # Extract simple reason tokens from notes (after FILTERS: prefix)
        reason = ''
        if 'FILTERS:' in notes:
            reason = notes.split('FILTERS:',1)[-1].strip()
        results.append({
            'Timestamp': r.get('Timestamp',''),
            'Bar': bar,
            'Direction': dir_candidate,
            'FastGradient': fg,
            'FastEMA': fe,
            'SlowEMA': se,
            'Close': close_px,
            'MaxFavorableMove': round(max_fav,2),
            'BarsToPeak': bars_to_peak,
            'BlockedReason': reason,
            'Status': status
        })
    return results

def export_missed_entries_csv(rows: List[Dict[str,object]], out_path: str) -> int:
    header = ['Timestamp','Bar','Direction','FastGradient','FastEMA','SlowEMA','Close','MaxFavorableMove','BarsToPeak','BlockedReason','Status']
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path,'w',newline='',encoding='utf-8') as f:
        w = csv.writer(f)
        w.writerow(header)
        for r in rows:
            w.writerow([r['Timestamp'], r['Bar'], r['Direction'], f"{r['FastGradient']:.4f}", f"{r['FastEMA']:.2f}", f"{r['SlowEMA']:.2f}", f"{r['Close']:.2f}", f"{r['MaxFavorableMove']:.2f}", r['BarsToPeak'], r['BlockedReason'], r['Status']])
    return len(rows)

def main():
    if not os.path.isdir(LOG_DIR):
        print(f"No strategy_logs directory found at {LOG_DIR}")
        return

    # Exclude derivative exports like LONGEST_TRADES_TOLERANCE when selecting base trades summary
    summary_csv = newest_file("GradientSlope_TRADES_SUMMARY_*.csv", exclude_substr="__LONGEST_TRADES_TOLERANCE")
    main_csv_candidate = newest_file("GradientSlope_*.csv", exclude_substr="TRADES")
    if main_csv_candidate:
        base = os.path.basename(main_csv_candidate)
        # Avoid derivative diagnostic exports as decision source
        if ('__MISSED_ENTRIES' in base) or ('__MISSED_TRENDS' in base) or ('__LONGEST_TRADES_TOLERANCE' in base):
            # Find an earlier non-derivative file
            all_files = glob.glob(os.path.join(LOG_DIR, "GradientSlope_*.csv"))
            normal = [f for f in all_files if ('__MISSED' not in os.path.basename(f) and '__LONGEST_TRADES_TOLERANCE' not in os.path.basename(f) and 'TRADES_SUMMARY' not in os.path.basename(f))]
            if normal:
                main_csv_candidate = max(normal, key=os.path.getmtime)
    main_csv = main_csv_candidate

    if not summary_csv:
        print("No TRADES_SUMMARY CSV found. Run the strategy to generate logs.")
        return
    print(f"Using TRADES_SUMMARY: {os.path.basename(summary_csv)}")
    if main_csv:
        print(f"Using DECISIONS CSV: {os.path.basename(main_csv)}")

    trades = load_trades_summary(summary_csv)
    ts = summarize_trades(trades)

    print("\n=== Overall ===")
    print(f"Trades: {ts['count']} | WinRate: {ts['win_rate']:.1f}% | AvgPnL: {ts['avg_realized']:.3f} | MedPnL: {ts['median_realized']:.3f}")
    print(f"Avg MFE: {ts['avg_mfe']:.3f} | Avg MAE: {ts['avg_mae']:.3f} | Avg BarsHeld: {ts['avg_bars']:.2f}")

    print("\n=== By ExitReason (avg PnL) ===")
    for k, v in sorted(ts["by_reason"].items(), key=lambda kv: kv[0]):
        print(f"{k:28s}  n={v['n']:3d}  avg={v['avg']:.3f}")

    print("\n=== By BarsHeld (avg PnL) ===")
    for k, v in ts["by_bars"].items():
        print(f"Bars={k:2d}: n={v['n']:3d} avg={v['avg']:.3f}")

    print("\n=== Pending Used (avg PnL) ===")
    for k, v in ts["by_pending"].items():
        print(f"PendingUsed={k}: n={v['n']:3d} avg={v['avg']:.3f}")

    # Deep dive: validation fails & exit pending deltas
    if main_csv and os.path.exists(main_csv):
        rows = load_main_csv(main_csv)
        vf = analyze_validation_fails(rows)
        ep = analyze_exit_pending(rows)
        eg = analyze_entry_gradient_effects(trades, rows)
        print("\n=== Validation Failures ===")
        print(f"Total EXITs due to validation: {vf['total_val_fail']} | LongSamples={vf['long_samples']} ShortSamples={vf['short_samples']}")
        print(f"Median |FastGrad| at validation exit -> LONG: {vf['long_median']:.3f} SHORT: {vf['short_median']:.3f}")
        print(f"Suggested ValidationMinFastGradientAbs: {vf['suggest_validation_min_abs']:.2f}")

        print("\n=== Exit Pending Deltas ===")
        print(f"EXIT_PENDING waits: n={ep['wait_n']} meanΔ={ep['wait_mean']}")
        print(f"Confirmed exits: n={ep['confirm_n']} meanΔ={ep['confirm_mean']}")
        if ep['suggest_exit_confirm_delta'] > 0:
            print(f"Suggested ExitConfirmFastEMADelta: {ep['suggest_exit_confirm_delta']:.2f}")
        if eg:
            print("\n=== Entry FastGradient vs PnL ===")
            print(f"Overall ENTRY avg PnL: {eg['overall_avg']:.3f}")
            for k, v in eg["buckets"].items():
                print(f"{k:>8s}  n={v['n']:3d} avg={v['avg']:.3f}")
            print(f"Suggested MinEntryFastGradientAbs: {eg['suggest_min_entry_fast_grad']:.2f}")
        # New: indicator snapshot correlations
        ind_corr = correlate_entry_indicators(trades, rows)
        if ind_corr.get('count',0) > 0:
            print("\n=== Entry Indicator Correlations ===")
            print(f"Snapshots parsed: {ind_corr['count']}")
            for m, info in ind_corr['metrics'].items():
                print(f"{m:7s} corrPnL={info['corr_pnl']:+.3f} corrBars={info['corr_bars']:+.3f} Q1={info['q1']:.3f} Q3={info['q3']:.3f} PnL_low={info['avg_low_pnl']:+.2f} PnL_high={info['avg_high_pnl']:+.2f} Bars_low={info['avg_low_bars']:.2f} Bars_high={info['avg_high_bars']:.2f} {info['suggestion']}")
        # New: what-if filter grid at ENTRY based on snapshots
        sims = simulate_filter_grid(trades, rows)
        if sims:
            print("\n=== What-If Entry Filters (counterfactual) ===")
            for s in sims[:12]:
                print(f"{s['label']:<50s}  n={s['n']:3d}  avg={s['avg']:+.3f}  win%={s['win_rate']:.1f}")
        # New: longest trades and longest trends analysis
        print("\n=== Longest Trades (tolerance) ===")
        top = find_longest_trades(trades, top_n=10)
        for t in top:
            tb = int(t["BarsHeld"]) if t.get("BarsHeld") else -1
            dirn = t.get("Direction","?")
            ebar = int(t.get("EntryBar", -1)) if t.get("EntryBar") else -1
            xbar = int(t.get("ExitBar", -1)) if t.get("ExitBar") else -1
            epx = parse_float(t.get("EntryPrice","0"))
            mae = abs(parse_float(t.get("MAE","0")))
            mfe = parse_float(t.get("MFE","0"))
            tol_pct = (mae/epx*100.0) if epx else 0.0
            pnl = parse_float(t.get("RealizedPoints","0"))
            reason = (t.get("ExitReason","") or "").split(":",1)[0]
            # In-trade min |FastGrad| and near-threshold counts
            min_abs_fg, near_cnt = trade_gradient_tolerance(rows, ebar, xbar)
            print(f"Bars={tb:3d}  {dirn:5s}  EntryBar={ebar:6d} ExitBar={xbar:6d}  PnL={pnl:+.2f}  MFE={mfe:.2f}  MAE={mae:.2f} ({tol_pct:.2f}%)  min|FastGrad|={min_abs_fg:.3f}  nearThr={near_cnt}  Exit={reason}")
        # Export tolerance table (top 50 by BarsHeld)
        tol_base = os.path.splitext(os.path.basename(summary_csv))[0]
        tol_path = os.path.join(LOG_DIR, f"{tol_base}__LONGEST_TRADES_TOLERANCE.csv")
        try:
            tol_rows = export_longest_trades_csv(trades, rows, tol_path, top_n=50)
            print(f"\nExported tolerance table: {tol_path} (rows={tol_rows})")
        except Exception as ex:
            print(f"\n[WARN] Failed to export tolerance CSV: {ex}")

        print("\n=== Longest Trend Segments (caught vs missed) ===")
        min_len = 12
        segs = find_longest_trend_segments(rows, min_len=min_len, top_n=20)
        # Build trade intervals for overlap checks
        trade_spans = []
        for t in trades:
            try:
                ebar = int(t.get("EntryBar", -1)); xbar = int(t.get("ExitBar", -1))
                if ebar >= 0 and xbar >= ebar:
                    trade_spans.append((ebar, xbar))
            except Exception:
                pass
        for s in segs:
            overlap = any(not (s["end_bar"] < a or s["start_bar"] > b) for a, b in trade_spans)
            status = "CAUGHT" if overlap else "MISSED"
            why = ""
            if not overlap:
                why = infer_miss_reason(rows, s["start_bar"], s["end_bar"]) or "Unknown guard/condition"
            print(f"{s['dir']:5s}  Bars={s['length']:3d}  Range=[{s['start_bar']},{s['end_bar']}]  mean|F|={s['mean_abs_fast']:.3f}  status={status}{(' -> ' + why) if why else ''}")
        # Export missed trends diagnostics CSV
        base_for_name = os.path.splitext(os.path.basename(main_csv or summary_csv))[0]
        missed_name = f"{base_for_name}__MISSED_TRENDS_MIN{min_len}.csv"
        missed_path = os.path.join(LOG_DIR, missed_name)
        try:
            n_missed = export_missed_trends_csv(rows, trades, missed_path, min_len=min_len)
            print(f"\nExported missed trends: {missed_path} (rows={n_missed})")
        except Exception as ex:
            print(f"\n[WARN] Failed to export missed trends CSV: {ex}")
        # Missed entry opportunities (filter-blocked)
        me_rows = detect_missed_entries(rows, trades)
        me_base = os.path.splitext(os.path.basename(main_csv or summary_csv))[0]
        me_path = os.path.join(LOG_DIR, f"{me_base}__MISSED_ENTRIES.csv")
        try:
            me_n = export_missed_entries_csv(me_rows, me_path)
            win_like = sum(1 for x in me_rows if x['Status']=='MISSED_WIN')
            print(f"\nMissed entry opportunities: {me_n} (potential winners={win_like}) -> {me_path}")
        except Exception as ex:
            print(f"\n[WARN] Failed to export missed entries CSV: {ex}")
    else:
        print("\nDecision CSV not found; skipping validation/exit-pending analysis.")

    # Heuristic suggestions for MinHoldBars based on BarsHeld performance
    bh_perf = ts["by_bars"]
    if bh_perf:
        # If earliest holds (1 bar) have strongly negative avg PnL, consider increasing MinHoldBars to 2
        one_bar = bh_perf.get(1, {"avg": 0, "n": 0})
        two_bar = bh_perf.get(2, {"avg": 0, "n": 0})
        if one_bar["n"] >= 5 and one_bar["avg"] < 0 and two_bar["avg"] > one_bar["avg"]:
            print("\nSuggested MinHoldBars: 2 (1-bar exits underperform)")

    # Entry gate suggestion: if many ENTRY_PHASE_VALIDATION_FAILED exits exist and overall PnL negative for those, consider raising entry gradient
    failed_entries = ts["by_reason"].get("ENTRY_PHASE_VALIDATION_FAILED")
    if failed_entries and failed_entries["n"] >= 3 and failed_entries["avg"] <= 0:
        print("Suggested MinEntryFastGradientAbs: consider raising by 0.05–0.10 to filter weak entries")


if __name__ == "__main__":
    main()
