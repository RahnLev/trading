#!/usr/bin/env python3
"""Summarize what happened on a specific bar from strategy logs.

- Reads the most recent GradientSlope CSV and LOG in `strategy_logs/`.
- Shows all CSV rows for the target bar (default 2612).
- Shows LOG lines that mention the bar number with a small context window.

Usage:
    python analyze_bar_logs.py --bar 2612
    python analyze_bar_logs.py --bar 922
"""
from __future__ import annotations
import argparse
import csv
import glob
import os
import re
from typing import Iterable, List, Tuple

ROOT = os.path.dirname(os.path.abspath(__file__))
LOG_DIR = os.path.join(ROOT, "strategy_logs")


def find_latest(pattern: str, exclude: Iterable[str] = ()) -> str | None:
    """Return the most recent file matching pattern, excluding substrings."""
    candidates = []
    for path in glob.glob(os.path.join(LOG_DIR, pattern)):
        name = os.path.basename(path)
        if any(x in name for x in exclude):
            continue
        try:
            mtime = os.path.getmtime(path)
        except OSError:
            continue
        candidates.append((mtime, path))
    if not candidates:
        return None
    candidates.sort(key=lambda x: x[0], reverse=True)
    return candidates[0][1]


def load_csv_rows(csv_path: str, bar: int) -> List[dict]:
    rows: List[dict] = []
    with open(csv_path, newline="", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for row in reader:
            try:
                if int(row.get("Bar", -1)) == bar:
                    rows.append(row)
            except ValueError:
                continue
    return rows


def summarize_csv(rows: List[dict]) -> None:
    if not rows:
        print("No CSV rows found for that bar.")
        return
    for r in rows:
        print("- CSV", r.get("Timestamp", ""),
              f"Action={r.get('Action','')}",
              f"PrevSignal={r.get('PrevSignal','')}",
              f"NewSignal={r.get('NewSignal','')}",
              f"MyPosition={r.get('MyPosition','')}",
              f"ActualPosition={r.get('ActualPosition','')}",
              f"Notes={r.get('Notes','')}",
              f"SignalStartBar={r.get('SignalStartBar','')}",
              f"EntryBar={r.get('EntryBar','')}",
              f"EntryPrice={r.get('EntryPrice','')}",
              f"TradeMFE={r.get('TradeMFE','')}",
              f"TradeMAE={r.get('TradeMAE','')}",
              f"UnrealizedPoints={r.get('UnrealizedPoints','')}")


def find_log_hits(log_path: str, bar: int, context: int = 2) -> List[Tuple[int, List[str]]]:
    """Find log lines that reference the exact bar number (avoid decimal matches)."""
    hits: List[Tuple[int, List[str]]] = []
    # Match common bar markers, avoid decimals like 25714.2612
    pattern = re.compile(
        rf"(?:Bar[:= ]{bar}\b|BarIndex[:= ]{bar}\b|CurrentBar[:= ]{bar}\b|barIndex[:= ]{bar}\b)"
    )
    with open(log_path, encoding="utf-8", errors="ignore") as f:
        lines = f.readlines()
    for idx, line in enumerate(lines):
        if pattern.search(line):
            start = max(0, idx - context)
            end = min(len(lines), idx + context + 1)
            hits.append((idx + 1, lines[start:end]))
    return hits


def summarize_log(hits: List[Tuple[int, List[str]]]) -> None:
    if not hits:
        print("No LOG lines found for that bar.")
        return
    for lineno, block in hits:
        print(f"- LOG (around line {lineno}):")
        for l in block:
            print("    " + l.rstrip())


def main() -> None:
    ap = argparse.ArgumentParser(description="Analyze a specific bar from strategy logs")
    ap.add_argument("--bar", type=int, default=2612, help="Bar number to analyze (default: 2612)")
    args = ap.parse_args()
    bar = args.bar

    csv_path = find_latest("GradientSlope_MNQ *.csv", exclude=["TRADES", "SUMMARY", "PROPS"])
    log_path = find_latest("GradientSlope_MNQ *.log", exclude=["TRADES", "SUMMARY", "PROPS"])

    print(f"Target bar: {bar}")
    print(f"CSV file: {csv_path or 'NOT FOUND'}")
    print(f"LOG file: {log_path or 'NOT FOUND'}\n")

    if csv_path:
        rows = load_csv_rows(csv_path, bar)
        print("=== CSV rows ===")
        summarize_csv(rows)
        print()
    if log_path:
        hits = find_log_hits(log_path, bar)
        print("=== LOG context ===")
        summarize_log(hits)
        print()

    if not csv_path and not log_path:
        print("No log files found. Make sure strategy logs exist in strategy_logs/.")


if __name__ == "__main__":
    main()
