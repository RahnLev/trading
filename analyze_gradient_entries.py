import csv
from pathlib import Path

# Configuration: adjust if your filename changes
CSV_GLOB = 'strategy_logs/GradientSlope_*.csv'


WEAK_FAST_GRADIENT_THRESHOLD = 0.5  # same as strategy default


def check_entry_rules(row):
    """Return (ok: bool, reason: str) for this ENTRY row based on current C# rules."""
    try:
        close = float(row['Close'])
        fast_ema = float(row['FastEMA'])
        slow_ema = float(row['SlowEMA'])
        fast_grad = float(row['FastGradient'])
        slow_grad = float(row['SlowGradient'])
    except Exception as e:
        return False, f'ParseError: {e}'

    position = row.get('Position', '').upper()

    if position == 'LONG':
        # LONG entry rules: both gradients > 0, close above both EMAs
        if not (fast_grad > 0):
            return False, f'FastGradient<=0 ({fast_grad})'
        if not (slow_grad > 0):
            return False, f'SlowGradient<=0 ({slow_grad})'
        if not (close > fast_ema):
            return False, f'Close<=FastEMA ({close} <= {fast_ema})'
        if not (close > slow_ema):
            return False, f'Close<=SlowEMA ({close} <= {slow_ema})'
        return True, ''

    if position == 'SHORT':
        # SHORT entry rules: both gradients < 0, close below both EMAs
        if not (fast_grad < 0):
            return False, f'FastGradient>=0 ({fast_grad})'
        if not (slow_grad < 0):
            return False, f'SlowGradient>=0 ({slow_grad})'
        if not (close < fast_ema):
            return False, f'Close>=FastEMA ({close} >= {fast_ema})'
        if not (close < slow_ema):
            return False, f'Close>=SlowEMA ({close} >= {slow_ema})'
        return True, ''

    # Not a recognized position type for ENTRY
    return False, f'UnknownPosition: {position}'


def analyze_file(path: Path, max_violations: int = 100):
    print(f'Analyzing {path} ...')
    total_entries = 0
    weak_fast_grad_entries = 0
    violations = []

    with path.open('r', newline='') as f:
        reader = csv.DictReader(f)
        rows = list(reader)

    # Pass 1: ENTRY checks
    for row in rows:
            action = row.get('Action', '').upper()
            if action != 'ENTRY':
                continue

            total_entries += 1
            ok, reason = check_entry_rules(row)
            if not ok:
                violations.append((row, reason))
                if len(violations) >= max_violations:
                    break

            # Track how many entries start with a "weak" fast EMA gradient
            try:
                fast_grad = float(row['FastGradient'])
                if abs(fast_grad) < WEAK_FAST_GRADIENT_THRESHOLD:
                    weak_fast_grad_entries += 1
            except Exception:
                pass

    print(f'Total ENTRY rows checked: {total_entries}')
    print(f'Entries with |FastGradient| < {WEAK_FAST_GRADIENT_THRESHOLD}: {weak_fast_grad_entries}')
    print(f'Violations found: {len(violations)}')

    for row, reason in violations:
        print('-' * 80)
        print(f"Timestamp: {row.get('Timestamp')}  Bar: {row.get('Bar')}  Pos: {row.get('Position')}  Notes: {row.get('Notes')}")
        print(f"Close={row.get('Close')}  FastEMA={row.get('FastEMA')}  SlowEMA={row.get('SlowEMA')}")
        print(f"FastGradient={row.get('FastGradient')}  SlowGradient={row.get('SlowGradient')}")
        print(f'Reason: {reason}')

    # Pass 2: Check if strategy exited when BOTH fast gradient and price met exit rule
    # LONG:   in LONG, fastGradient < 0 AND Close < FastEMA  -> expect EXIT
    # SHORT:  in SHORT, fastGradient > 0 AND Close > FastEMA -> expect EXIT
    missed_exits = []

    for row in rows:
        try:
            fast_grad = float(row['FastGradient'])
            close = float(row['Close'])
            fast_ema = float(row['FastEMA'])
        except Exception:
            continue

        pos = row.get('Position', '').upper()
        action = row.get('Action', '').upper()

        # LONG immediate-exit condition
        if pos == 'LONG' and fast_grad < 0 and close < fast_ema:
            if action != 'EXIT':
                missed_exits.append((row, 'LONG_expected_EXIT_fastGrad<0_and_Close<FastEMA'))

        # SHORT immediate-exit condition
        if pos == 'SHORT' and fast_grad > 0 and close > fast_ema:
            if action != 'EXIT':
                missed_exits.append((row, 'SHORT_expected_EXIT_fastGrad>0_and_Close>FastEMA'))

    print('\nFast-gradient + price exit rule checks while in position:')
    print(f'Missed exits where rule was satisfied: {len(missed_exits)}')
    for row, reason in missed_exits[:50]:
        print('-' * 80)
        print(f"Timestamp: {row.get('Timestamp')}  Bar: {row.get('Bar')}  Pos: {row.get('Position')}  Action: {row.get('Action')}  Notes: {row.get('Notes')}")
        print(f"Close={row.get('Close')}  FastEMA={row.get('FastEMA')}  SlowEMA={row.get('SlowEMA')}")
        print(f"FastGradient={row.get('FastGradient')}  SlowGradient={row.get('SlowGradient')}")
        print(f'Reason: {reason}')


def main():
    root = Path(__file__).parent
    files = sorted(root.glob(CSV_GLOB))
    if not files:
        print('No CSV files matching', CSV_GLOB)
        return

    # Just analyze the latest file
    latest = max(files, key=lambda p: p.stat().st_mtime)
    analyze_file(latest)


if __name__ == '__main__':
    main()
