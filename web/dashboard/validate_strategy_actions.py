#!/usr/bin/env python3
"""
Comprehensive Strategy Action Validation System

This script validates:
1. All strategy actions (entries, exits, stops) are correct
2. Data consistency (bar data, trade data, logs)
3. Actions match expected behavior based on parameters
4. Identifies bugs and inconsistencies
"""
import json
import csv
import os
import sys
import sqlite3
import re
from collections import defaultdict
from datetime import datetime
from typing import Dict, List, Tuple, Optional

# Configure UTF-8 output
if sys.stdout.encoding != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8')

# Paths
CUSTOM_DIR = os.path.dirname(os.path.dirname(os.path.dirname(__file__)))
STRATEGY_LOGS_DIR = os.path.join(CUSTOM_DIR, 'strategy_logs')
VOLATILITY_DB = os.path.join(os.path.dirname(__file__), 'volatility.db')
DASHBOARD_DB = os.path.join(os.path.dirname(__file__), 'dashboard.db')

class StrategyActionValidator:
    """Validates strategy actions and behavior"""
    
    def __init__(self):
        self.params = None
        self.trades = []
        self.bar_data = {}
        self.log_entries = []
        self.issues = []
        self.warnings = []
        self.stats = defaultdict(int)
        
    def load_parameters(self) -> bool:
        """Load the most recent parameters JSON file"""
        json_files = [f for f in os.listdir(STRATEGY_LOGS_DIR) 
                     if f.endswith('_params.json') and 'BarsOnTheFlow' in f]
        if not json_files:
            self.issues.append("ERROR: No parameters JSON file found!")
            return False
        
        json_files.sort(reverse=True)
        latest_json = os.path.join(STRATEGY_LOGS_DIR, json_files[0])
        
        try:
            with open(latest_json, 'r') as f:
                self.params = json.load(f)
            print(f"✓ Loaded parameters from: {json_files[0]}")
            return True
        except Exception as e:
            self.issues.append(f"ERROR: Failed to load parameters: {e}")
            return False
    
    def load_trades(self) -> bool:
        """Load trades from volatility.db"""
        if not os.path.exists(VOLATILITY_DB):
            self.issues.append(f"ERROR: Database not found: {VOLATILITY_DB}")
            return False
        
        try:
            conn = sqlite3.connect(VOLATILITY_DB)
            cursor = conn.cursor()
            
            # Check if entry_time column exists
            cursor.execute("PRAGMA table_info(trades)")
            columns = [col[1] for col in cursor.fetchall()]
            has_entry_time = 'entry_time' in columns
            
            if has_entry_time:
                cursor.execute("""
                    SELECT entry_bar, exit_bar, direction, entry_price, exit_price,
                           realized_points, exit_reason, entry_reason, mfe, mae,
                           fast_ema, fast_ema_grad_deg, entry_time, exit_time, bars_held
                    FROM trades
                    ORDER BY entry_bar
                """)
            else:
                cursor.execute("""
                    SELECT entry_bar, exit_bar, direction, entry_price, exit_price,
                           realized_points, exit_reason, entry_reason, mfe, mae,
                           fast_ema, fast_ema_grad_deg
                    FROM trades
                    ORDER BY entry_bar
                """)
            
            for row in cursor.fetchall():
                trade = {
                    'entry_bar': row[0],
                    'exit_bar': row[1],
                    'direction': row[2],
                    'entry_price': row[3],
                    'exit_price': row[4],
                    'realized_points': row[5],
                    'exit_reason': row[6] or '',
                    'entry_reason': row[7] or '',
                    'mfe': row[8],
                    'mae': row[9],
                    'fast_ema': row[10],
                    'gradient': row[11]
                }
                if has_entry_time:
                    trade['entry_time'] = row[12]
                    trade['exit_time'] = row[13]
                    trade['bars_held'] = row[14]
                self.trades.append(trade)
            
            conn.close()
            print(f"✓ Loaded {len(self.trades)} trades from database")
            return True
        except Exception as e:
            self.issues.append(f"ERROR: Failed to load trades: {e}")
            return False
    
    def load_bar_data(self) -> bool:
        """Load bar data from volatility.db, with fallback to log file"""
        loaded_from_db = False
        if os.path.exists(VOLATILITY_DB):
            try:
                conn = sqlite3.connect(VOLATILITY_DB)
                cursor = conn.cursor()
                
                # Try to load with all columns, but handle missing columns gracefully
                try:
                    cursor.execute("""
                        SELECT bar_index, open_price, high_price, low_price, close_price,
                               ema_fast_value, ema_slow_value, fast_ema_grad_deg,
                               candle_type, bar_pattern, trend_direction
                        FROM bar_samples
                        ORDER BY bar_index
                    """)
                    
                    for row in cursor.fetchall():
                        self.bar_data[row[0]] = {
                            'open': row[1],
                            'high': row[2],
                            'low': row[3],
                            'close': row[4],
                            'ema_fast': row[5],
                            'ema_slow': row[6],
                            'gradient': row[7],
                            'candle_type': row[8],
                            'bar_pattern': row[9] if len(row) > 9 else None,
                            'trend_direction': row[10] if len(row) > 10 else None
                        }
                    
                    conn.close()
                    print(f"✓ Loaded {len(self.bar_data)} bars from database")
                    loaded_from_db = True
                except sqlite3.OperationalError as e:
                    # Missing columns - try with basic columns only
                    conn.close()
                    conn = sqlite3.connect(VOLATILITY_DB)
                    cursor = conn.cursor()
                    try:
                        cursor.execute("""
                            SELECT bar_index, open_price, high_price, low_price, close_price,
                                   ema_fast_value, ema_slow_value, fast_ema_grad_deg, candle_type
                            FROM bar_samples
                            ORDER BY bar_index
                        """)
                        
                        for row in cursor.fetchall():
                            self.bar_data[row[0]] = {
                                'open': row[1],
                                'high': row[2],
                                'low': row[3],
                                'close': row[4],
                                'ema_fast': row[5],
                                'ema_slow': row[6],
                                'gradient': row[7],
                                'candle_type': row[8] if len(row) > 8 else None,
                                'bar_pattern': None,
                                'trend_direction': None
                            }
                        
                        conn.close()
                        print(f"✓ Loaded {len(self.bar_data)} bars from database (basic columns only)")
                        loaded_from_db = True
                    except Exception as e2:
                        self.warnings.append(f"Warning: Failed to load bar data from DB: {e2}")
            except Exception as e:
                self.warnings.append(f"Warning: Failed to load bar data: {e}")
        
        # Fallback: Extract bar data from log file for missing bars
        log_bar_data = self._extract_bar_data_from_logs()
        if log_bar_data:
            # Merge log data into bar_data (log data fills in missing bars and missing fields)
            log_bars_added = 0
            log_fields_filled = 0
            for bar_num, data in log_bar_data.items():
                if bar_num not in self.bar_data:
                    self.bar_data[bar_num] = data
                    log_bars_added += 1
                else:
                    # Fill in missing fields from log data
                    for key, value in data.items():
                        if value is not None and (key not in self.bar_data[bar_num] or self.bar_data[bar_num][key] is None):
                            self.bar_data[bar_num][key] = value
                            log_fields_filled += 1
            
            if log_bars_added > 0:
                print(f"✓ Added {log_bars_added} bars from log file (fallback)")
            if log_fields_filled > 0:
                print(f"✓ Filled {log_fields_filled} missing fields from log file")
        
        return loaded_from_db or len(self.bar_data) > 0
    
    def _extract_bar_data_from_logs(self) -> Dict[int, Dict]:
        """Extract bar data from OutputWindow log file as fallback"""
        csv_files = [f for f in os.listdir(STRATEGY_LOGS_DIR) 
                     if f.endswith('.csv') and 'BarsOnTheFlow' in f and 'OutputWindow' in f]
        if not csv_files:
            return {}
        
        csv_files.sort(reverse=True)
        latest_csv = os.path.join(STRATEGY_LOGS_DIR, csv_files[0])
        bar_data = {}
        
        try:
            with open(latest_csv, 'r', encoding='utf-8', errors='ignore') as f:
                reader = csv.DictReader(f)
                
                for row in reader:
                    message = row.get('message', '') or row.get('Message', '')
                    bar_str = row.get('bar', '') or row.get('Bar', '')
                    
                    # Get bar number
                    bar_num = None
                    if bar_str and bar_str.isdigit():
                        bar_num = int(bar_str)
                    else:
                        bar_match = re.search(r'Bar\s+(\d+)', message)
                        if bar_match:
                            bar_num = int(bar_match.group(1))
                    
                    if bar_num is None:
                        continue
                    
                    if bar_num not in bar_data:
                        bar_data[bar_num] = {}
                    
                    # Extract data from EMA_STOP_CHECK messages (most reliable source)
                    if '[EMA_STOP_CHECK]' in message or '[EMA_STOP_FULL_CANDLE]' in message:
                        # Pattern: "Open=123.45" or "Open: 123.45"
                        for field, patterns in [
                            ('open', [r'Open[=:]?\s*([\d.]+)']),
                            ('close', [r'Close[=:]?\s*([\d.]+)']),
                            ('high', [r'High[=:]?\s*([\d.]+)']),
                            ('low', [r'Low[=:]?\s*([\d.]+)']),
                            ('ema_fast', [r'EMA[=:]?\s*([\d.]+)', r'Fast\s*EMA[=:]?\s*([\d.]+)', r'emaFast[=:]?\s*([\d.]+)']),
                        ]:
                            if field not in bar_data[bar_num] or bar_data[bar_num][field] is None:
                                for pattern in patterns:
                                    match = re.search(pattern, message, re.IGNORECASE)
                                    if match:
                                        try:
                                            val = float(match.group(1))
                                            # Validate it's a reasonable price (not a bar number)
                                            if 1000 < val < 100000:  # Adjust for your instrument
                                                bar_data[bar_num][field] = val
                                                break
                                        except (ValueError, IndexError):
                                            pass
                    
                    # Extract gradient from any message
                    if 'gradient' not in bar_data[bar_num] or bar_data[bar_num].get('gradient') is None:
                        grad_match = re.search(r'grad[=:]?\s*([-\d.]+)|gradient[=:]?\s*([-\d.]+)|GradDeg[=:]?\s*([-\d.]+)', message, re.IGNORECASE)
                        if grad_match:
                            try:
                                bar_data[bar_num]['gradient'] = float(grad_match.group(1) or grad_match.group(2) or grad_match.group(3))
                            except (ValueError, IndexError):
                                pass
                    
                    # Extract candle type
                    if 'candle_type' not in bar_data[bar_num] or bar_data[bar_num].get('candle_type') is None:
                        candle_match = re.search(r'(Good|Bad|Flat)\s*[Cc]andle', message, re.IGNORECASE)
                        if candle_match:
                            bar_data[bar_num]['candle_type'] = candle_match.group(1).upper()
        
        except Exception as e:
            # Silently fail - log extraction is optional
            pass
        
        return bar_data
    
    def load_logs(self) -> bool:
        """Load strategy logs from CSV files"""
        csv_files = [f for f in os.listdir(STRATEGY_LOGS_DIR) 
                     if f.endswith('.csv') and 'BarsOnTheFlow' in f and 'OutputWindow' in f]
        if not csv_files:
            self.warnings.append("Warning: No log CSV files found")
            return False
        
        csv_files.sort(reverse=True)
        latest_csv = os.path.join(STRATEGY_LOGS_DIR, csv_files[0])
        
        try:
            with open(latest_csv, 'r', encoding='utf-8', errors='ignore') as f:
                reader = csv.DictReader(f)
                for row in reader:
                    self.log_entries.append(row)
            
            print(f"✓ Loaded {len(self.log_entries)} log entries from: {csv_files[0]}")
            return True
        except Exception as e:
            self.warnings.append(f"Warning: Failed to load logs: {e}")
            return False
    
    def validate_entry_action(self, trade: Dict) -> List[str]:
        """Validate an entry action"""
        issues = []
        entry_bar = trade['entry_bar']
        direction = trade['direction']
        entry_reason = trade.get('entry_reason', '')
        
        if entry_bar not in self.bar_data:
            # Skip validation if bar data not available (may be from incomplete run)
            return issues
        
        bar = self.bar_data[entry_bar]
        
        # Validate EMA Crossover Filter
        if self.params.get('UseEmaCrossoverFilter'):
            if self.params.get('EmaCrossoverRequireBodyBelow'):
                # Check body is below/above Fast EMA
                body_top = max(bar['open'], bar['close'])
                body_bottom = min(bar['open'], bar['close'])
                fast_ema = bar.get('ema_fast')
                
                if fast_ema:
                    if direction == 'Long':
                        if body_bottom < fast_ema:
                            issues.append(
                                f"Bar {entry_bar}: LONG entry violates BodyBelow requirement - "
                                f"body bottom ({body_bottom:.2f}) < FastEMA ({fast_ema:.2f})"
                            )
                    elif direction == 'Short':
                        if body_top > fast_ema:
                            issues.append(
                                f"Bar {entry_bar}: SHORT entry violates BodyBelow requirement - "
                                f"body top ({body_top:.2f}) > FastEMA ({fast_ema:.2f})"
                            )
        
        # Validate AvoidLongsOnBadCandle / AvoidShortsOnGoodCandle
        candle_type = bar.get('candle_type', '')
        if self.params.get('AvoidLongsOnBadCandle') and direction == 'Long':
            if candle_type == 'Bad':
                issues.append(
                    f"Bar {entry_bar}: LONG entry on bad candle but AvoidLongsOnBadCandle=true"
                )
        if self.params.get('AvoidShortsOnGoodCandle') and direction == 'Short':
            if candle_type == 'Good':
                issues.append(
                    f"Bar {entry_bar}: SHORT entry on good candle but AvoidShortsOnGoodCandle=true"
                )
        
        # Validate Gradient Filter
        if self.params.get('GradientFilterEnabled'):
            gradient = bar.get('gradient')
            if gradient is not None:
                skip_longs = self.params.get('SkipLongsBelowGradient', 0)
                skip_shorts = self.params.get('SkipShortsAboveGradient', 0)
                
                if direction == 'Long' and gradient < skip_longs:
                    issues.append(
                        f"Bar {entry_bar}: LONG entry with gradient {gradient:.2f} < "
                        f"SkipLongsBelowGradient ({skip_longs})"
                    )
                elif direction == 'Short' and gradient > skip_shorts:
                    issues.append(
                        f"Bar {entry_bar}: SHORT entry with gradient {gradient:.2f} > "
                        f"SkipShortsAboveGradient ({skip_shorts})"
                    )
        
        return issues
    
    def validate_exit_action(self, trade: Dict) -> List[str]:
        """Validate an exit action"""
        issues = []
        exit_bar = trade['exit_bar']
        entry_bar = trade['entry_bar']
        direction = trade['direction']
        exit_reason = trade.get('exit_reason', '')
        entry_price = trade['entry_price']
        exit_price = trade['exit_price']
        
        if exit_bar not in self.bar_data:
            # Skip validation if bar data not available (may be from incomplete run)
            return issues
        
        bar = self.bar_data[exit_bar]
        
        # Validate EMA Stop Loss
        if self.params.get('UseEmaTrailingStop'):
            ema_mode = self.params.get('EmaStopTriggerMode', 'CloseOnly')
            fast_ema = bar.get('ema_fast')
            
            if fast_ema:
                if direction == 'Long':
                    # Check if exit matches EMA stop trigger mode
                    if ema_mode == 'FullCandle':
                        # Both high and low should be below EMA
                        if bar['high'] >= fast_ema or bar['low'] >= fast_ema:
                            if 'EMA' not in exit_reason.upper():
                                issues.append(
                                    f"Bar {exit_bar}: LONG exit - FullCandle mode requires both High and Low < EMA, "
                                    f"but High={bar['high']:.2f}, Low={bar['low']:.2f}, EMA={fast_ema:.2f}"
                                )
                    elif ema_mode == 'BodyOnly':
                        body_top = max(bar['open'], bar['close'])
                        body_bottom = min(bar['open'], bar['close'])
                        if body_top >= fast_ema or body_bottom >= fast_ema:
                            if 'EMA' not in exit_reason.upper():
                                issues.append(
                                    f"Bar {exit_bar}: LONG exit - BodyOnly mode requires body < EMA, "
                                    f"but body_top={body_top:.2f}, body_bottom={body_bottom:.2f}, EMA={fast_ema:.2f}"
                                )
                    elif ema_mode == 'CloseOnly':
                        if bar['close'] >= fast_ema:
                            if 'EMA' not in exit_reason.upper():
                                issues.append(
                                    f"Bar {exit_bar}: LONG exit - CloseOnly mode requires Close < EMA, "
                                    f"but Close={bar['close']:.2f}, EMA={fast_ema:.2f}"
                                )
                
                elif direction == 'Short':
                    # Similar checks for shorts
                    if ema_mode == 'FullCandle':
                        if bar['high'] <= fast_ema or bar['low'] <= fast_ema:
                            if 'EMA' not in exit_reason.upper():
                                issues.append(
                                    f"Bar {exit_bar}: SHORT exit - FullCandle mode requires both High and Low > EMA, "
                                    f"but High={bar['high']:.2f}, Low={bar['low']:.2f}, EMA={fast_ema:.2f}"
                                )
                    elif ema_mode == 'BodyOnly':
                        body_top = max(bar['open'], bar['close'])
                        body_bottom = min(bar['open'], bar['close'])
                        if body_top <= fast_ema or body_bottom <= fast_ema:
                            if 'EMA' not in exit_reason.upper():
                                issues.append(
                                    f"Bar {exit_bar}: SHORT exit - BodyOnly mode requires body > EMA, "
                                    f"but body_top={body_top:.2f}, body_bottom={body_bottom:.2f}, EMA={fast_ema:.2f}"
                                )
                    elif ema_mode == 'CloseOnly':
                        if bar['close'] <= fast_ema:
                            if 'EMA' not in exit_reason.upper():
                                issues.append(
                                    f"Bar {exit_bar}: SHORT exit - CloseOnly mode requires Close > EMA, "
                                    f"but Close={bar['close']:.2f}, EMA={fast_ema:.2f}"
                                )
        
        # Validate Retrace Exit
        if 'Retrace' in exit_reason:
            if not self.params.get('ExitOnRetrace'):
                issues.append(
                    f"Bar {exit_bar}: Exit reason is Retrace but ExitOnRetrace=false"
                )
            else:
                # Check if retrace logic is correct
                mfe = trade.get('mfe')
                realized = trade.get('realized_points', exit_price - entry_price if direction == 'Long' else entry_price - exit_price)
                retrace_fraction = self.params.get('TrendRetraceFraction', 0.66)
                
                if mfe and mfe > 0:
                    expected_min_profit = mfe * (1 - retrace_fraction)
                    if realized < expected_min_profit:
                        issues.append(
                            f"Bar {exit_bar}: Retrace exit - realized profit {realized:.2f} < "
                            f"expected minimum {expected_min_profit:.2f} (MFE={mfe:.2f}, fraction={retrace_fraction})"
                        )
        
        # Validate ExitOnTrendBreak
        if 'TrendBreak' in exit_reason:
            if not self.params.get('ExitOnTrendBreak'):
                issues.append(
                    f"Bar {exit_bar}: Exit reason is TrendBreak but ExitOnTrendBreak=false"
                )
        
        return issues
    
    def validate_data_consistency(self) -> List[str]:
        """Validate data consistency across sources"""
        issues = []
        
        # Check trade count consistency
        trade_count_db = len(self.trades)
        self.stats['trades_in_db'] = trade_count_db
        
        # Check for duplicate entries - provide detailed analysis
        entry_bars = [t['entry_bar'] for t in self.trades]
        duplicate_bars = {}
        exact_duplicates = []  # Same entry_time, entry_price, direction
        
        for bar in set(entry_bars):
            count = entry_bars.count(bar)
            if count > 1:
                trades_on_bar = [t for t in self.trades if t['entry_bar'] == bar]
                duplicate_bars[bar] = {
                    'count': count,
                    'trades': trades_on_bar
                }
                
                # Check for exact duplicates (same entry_time, entry_price, direction)
                if 'entry_time' in trades_on_bar[0]:
                    seen = {}
                    for t in trades_on_bar:
                        key = (t.get('entry_time'), t.get('entry_price'), t.get('direction'))
                        if key in seen:
                            exact_duplicates.append({
                                'bar': bar,
                                'trade1': seen[key],
                                'trade2': t
                            })
                        else:
                            seen[key] = t
        
        if exact_duplicates:
            dup_details = []
            for dup in exact_duplicates[:10]:  # Limit to first 10
                bar = dup['bar']
                t1 = dup['trade1']
                t2 = dup['trade2']
                dup_details.append(
                    f"Bar {bar}: EXACT DUPLICATE - {t1['direction']} @ {t1['entry_price']:.2f} "
                    f"(entry_time={t1.get('entry_time')}, exit_bar={t1['exit_bar']} vs {t2['exit_bar']})"
                )
            if len(exact_duplicates) > 10:
                dup_details.append(f"... and {len(exact_duplicates) - 10} more exact duplicates")
            issues.append(f"ERROR: {len(exact_duplicates)} exact duplicate trades found (same entry_time, price, direction) - indicates data corruption:\n" + "\n".join(dup_details))
        
        if duplicate_bars:
            # Analyze duplicates to determine if they're legitimate or suspicious
            suspicious = []
            legitimate = []
            
            for bar, info in duplicate_bars.items():
                trades = info['trades']
                directions = [t['direction'] for t in trades]
                entry_prices = [t['entry_price'] for t in trades]
                
                # Check if same direction multiple times (suspicious)
                long_count = directions.count('Long')
                short_count = directions.count('Short')
                
                if long_count > 1 or short_count > 1:
                    # Same direction multiple times - could be legitimate (multiple contracts) or bug
                    suspicious.append({
                        'bar': bar,
                        'count': info['count'],
                        'longs': long_count,
                        'shorts': short_count,
                        'trades': trades
                    })
                else:
                    # Different directions - likely legitimate (reversal trades)
                    legitimate.append({
                        'bar': bar,
                        'count': info['count'],
                        'trades': trades
                    })
            
            if suspicious:
                suspicious_details = []
                # Show first 5 in detail, then summary
                for item in suspicious[:5]:  # Limit to first 5 for readability
                    bar = item['bar']
                    trades = item['trades']
                    details = f"Bar {bar}: {item['count']} trades (Long: {item['longs']}, Short: {item['shorts']})"
                    for t in trades:
                        entry_reason = t.get('entry_reason', 'N/A')[:50] if t.get('entry_reason') else 'N/A'
                        details += f"\n    - {t['direction']} @ {t['entry_price']:.2f} → Exit {t['exit_bar']} @ {t['exit_price']:.2f} (held {t.get('bars_held', '?')} bars) | {entry_reason}"
                    suspicious_details.append(details)
                
                if len(suspicious) > 5:
                    suspicious_details.append(f"... and {len(suspicious) - 5} more bars with duplicate same-direction entries")
                
                issues.append(f"WARNING: {len(suspicious)} bars with duplicate same-direction entries (may indicate multiple contracts or re-entry bug):\n" + "\n".join(suspicious_details))
            
            if legitimate:
                legitimate_bars = sorted([item['bar'] for item in legitimate])
                if len(legitimate_bars) <= 20:
                    issues.append(f"INFO: {len(legitimate)} bars with multiple entries in different directions (likely legitimate reversals): {legitimate_bars}")
                else:
                    issues.append(f"INFO: {len(legitimate)} bars with multiple entries in different directions (first 20): {legitimate_bars[:20]}...")
        
        # Check bar data completeness (only warn if significant)
        missing_entry_bars = [t['entry_bar'] for t in self.trades if t['entry_bar'] not in self.bar_data]
        missing_exit_bars = [t['exit_bar'] for t in self.trades if t['exit_bar'] not in self.bar_data]
        
        # Report missing bar data with context
        max_bar_available = max(self.bar_data.keys()) if self.bar_data else 0
        max_entry_bar = max(trade['entry_bar'] for trade in self.trades) if self.trades else 0
        max_exit_bar = max(trade['exit_bar'] for trade in self.trades) if self.trades else 0
        
        if missing_entry_bars:
            trades_with_missing_entry = len([t for t in self.trades if t['entry_bar'] not in self.bar_data])
            if len(missing_entry_bars) <= 10:
                issues.append(
                    f"WARNING: Missing entry bar data for {len(missing_entry_bars)} bars "
                    f"(affects {trades_with_missing_entry} trades) - "
                    f"bar data available up to {max_bar_available}, but trades go to {max_entry_bar}. "
                    f"Log file fallback attempted but also limited to bar {max_bar_available}."
                )
            else:
                issues.append(
                    f"WARNING: Missing entry bar data for {len(missing_entry_bars)} bars "
                    f"(affects {trades_with_missing_entry} trades, first 10: {sorted(missing_entry_bars)[:10]}) - "
                    f"bar data available up to {max_bar_available}, but trades go to {max_entry_bar}. "
                    f"Log file fallback attempted but also limited to bar {max_bar_available}."
                )
        
        if missing_exit_bars:
            trades_with_missing_exit = len([t for t in self.trades if t['exit_bar'] not in self.bar_data])
            if len(missing_exit_bars) <= 10:
                issues.append(
                    f"INFO: Missing exit bar data for {len(missing_exit_bars)} bars "
                    f"(affects {trades_with_missing_exit} trades) - "
                    f"incomplete run: bar data available up to {max_bar_available}, but exits go to {max_exit_bar}. "
                    f"Validation skipped for these trades to avoid false errors."
                )
            else:
                issues.append(
                    f"INFO: Missing exit bar data for {len(missing_exit_bars)} bars "
                    f"(affects {trades_with_missing_exit} trades) - "
                    f"incomplete run: bar data available up to {max_bar_available}, but exits go to {max_exit_bar}. "
                    f"Validation skipped for these trades to avoid false errors."
                )
        
        # Check price consistency
        for trade in self.trades:
            entry_bar = trade['entry_bar']
            exit_bar = trade['exit_bar']
            
            if entry_bar in self.bar_data and exit_bar in self.bar_data:
                entry_bar_data = self.bar_data[entry_bar]
                exit_bar_data = self.bar_data[exit_bar]
                
                # Entry price should be within entry bar range
                if not (entry_bar_data['low'] <= trade['entry_price'] <= entry_bar_data['high']):
                    issues.append(
                        f"Bar {entry_bar}: Entry price {trade['entry_price']:.2f} outside bar range "
                        f"[{entry_bar_data['low']:.2f}, {entry_bar_data['high']:.2f}]"
                    )
                
                # Exit price should be within exit bar range
                if not (exit_bar_data['low'] <= trade['exit_price'] <= exit_bar_data['high']):
                    issues.append(
                        f"Bar {exit_bar}: Exit price {trade['exit_price']:.2f} outside bar range "
                        f"[{exit_bar_data['low']:.2f}, {exit_bar_data['high']:.2f}]"
                    )
        
        return issues
    
    def validate_all_actions(self):
        """Validate all strategy actions"""
        print("\n" + "="*80)
        print("VALIDATING STRATEGY ACTIONS")
        print("="*80)
        
        # Validate entries
        print("\n[1/3] Validating Entry Actions...")
        entry_issues = 0
        for trade in self.trades:
            issues = self.validate_entry_action(trade)
            if issues:
                entry_issues += len(issues)
                self.issues.extend(issues)
                self.stats['entry_issues'] += len(issues)
        
        print(f"   Found {entry_issues} entry validation issues")
        
        # Validate exits
        print("\n[2/3] Validating Exit Actions...")
        exit_issues = 0
        for trade in self.trades:
            issues = self.validate_exit_action(trade)
            if issues:
                exit_issues += len(issues)
                self.issues.extend(issues)
                self.stats['exit_issues'] += len(issues)
        
        print(f"   Found {exit_issues} exit validation issues")
        
        # Validate data consistency
        print("\n[3/3] Validating Data Consistency...")
        consistency_issues = self.validate_data_consistency()
        if consistency_issues:
            self.issues.extend(consistency_issues)
            self.warnings.extend([i for i in consistency_issues if 'WARNING' in i])
            self.stats['consistency_issues'] = len(consistency_issues)
        
        print(f"   Found {len(consistency_issues)} data consistency issues")
    
    def generate_report(self) -> str:
        """Generate comprehensive validation report"""
        report = []
        report.append("="*80)
        report.append("STRATEGY ACTION VALIDATION REPORT")
        report.append("="*80)
        report.append(f"Generated: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
        report.append("")
        
        # Summary
        report.append("SUMMARY")
        report.append("-"*80)
        report.append(f"Total Trades Analyzed: {len(self.trades)}")
        report.append(f"Total Issues Found: {len(self.issues)}")
        report.append(f"Total Warnings: {len(self.warnings)}")
        report.append("")
        
        # Statistics
        report.append("STATISTICS")
        report.append("-"*80)
        for key, value in sorted(self.stats.items()):
            report.append(f"  {key}: {value}")
        report.append("")
        
        # Critical Issues
        if self.issues:
            report.append("CRITICAL ISSUES")
            report.append("-"*80)
            for i, issue in enumerate(self.issues, 1):
                report.append(f"{i}. {issue}")
            report.append("")
        else:
            report.append("✓ No critical issues found!")
            report.append("")
        
        # Warnings
        if self.warnings:
            report.append("WARNINGS")
            report.append("-"*80)
            for i, warning in enumerate(self.warnings, 1):
                report.append(f"{i}. {warning}")
            report.append("")
        
        # Parameter Summary
        if self.params:
            report.append("KEY PARAMETERS")
            report.append("-"*80)
            key_params = [
                'UseEmaCrossoverFilter', 'EmaCrossoverRequireBodyBelow',
                'AvoidLongsOnBadCandle', 'AvoidShortsOnGoodCandle',
                'GradientFilterEnabled', 'SkipLongsBelowGradient', 'SkipShortsAboveGradient',
                'UseEmaTrailingStop', 'EmaStopTriggerMode', 'EmaStopProfitProtectionPoints',
                'ExitOnRetrace', 'TrendRetraceFraction', 'ExitOnTrendBreak'
            ]
            for param in key_params:
                value = self.params.get(param, 'N/A')
                report.append(f"  {param}: {value}")
            report.append("")
        
        return "\n".join(report)
    
    def run(self):
        """Run full validation"""
        print("="*80)
        print("STRATEGY ACTION VALIDATION SYSTEM")
        print("="*80)
        
        # Load data
        print("\nLoading data...")
        if not self.load_parameters():
            return False
        
        if not self.load_trades():
            return False
        
        self.load_bar_data()
        self.load_logs()
        
        # Validate
        self.validate_all_actions()
        
        # Generate report
        print("\n" + "="*80)
        print("GENERATING REPORT")
        print("="*80)
        
        report = self.generate_report()
        print(report)
        
        # Save report
        report_file = os.path.join(os.path.dirname(__file__), 
                                   f"strategy_validation_report_{datetime.now().strftime('%Y%m%d_%H%M%S')}.txt")
        with open(report_file, 'w', encoding='utf-8') as f:
            f.write(report)
        
        print(f"\n✓ Report saved to: {report_file}")
        
        return len(self.issues) == 0

if __name__ == '__main__':
    validator = StrategyActionValidator()
    success = validator.run()
    sys.exit(0 if success else 1)
