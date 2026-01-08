#!/usr/bin/env python3
"""
Analyze strategy logs to find inconsistencies between parameters and actual behavior
"""
import json
import csv
import os
import sys
from collections import defaultdict

# Paths - strategy_logs is at Custom/strategy_logs, not in web/dashboard
CUSTOM_DIR = os.path.dirname(os.path.dirname(os.path.dirname(__file__)))
STRATEGY_LOGS_DIR = os.path.join(CUSTOM_DIR, 'strategy_logs')
VOLATILITY_DB = os.path.join(os.path.dirname(__file__), 'volatility.db')

def load_parameters():
    """Load the most recent parameters JSON file"""
    json_files = [f for f in os.listdir(STRATEGY_LOGS_DIR) if f.endswith('_params.json') and 'BarsOnTheFlow' in f]
    if not json_files:
        print("No parameters JSON file found!")
        return None
    
    # Get most recent
    json_files.sort(reverse=True)
    latest_json = os.path.join(STRATEGY_LOGS_DIR, json_files[0])
    
    with open(latest_json, 'r') as f:
        params = json.load(f)
    
    print(f"Loaded parameters from: {json_files[0]}")
    return params

def analyze_trades_from_db():
    """Analyze trades from volatility.db"""
    import sqlite3
    
    if not os.path.exists(VOLATILITY_DB):
        print(f"Database not found: {VOLATILITY_DB}")
        return []
    
    conn = sqlite3.connect(VOLATILITY_DB)
    cursor = conn.cursor()
    
    cursor.execute("""
        SELECT entry_bar, exit_bar, direction, entry_price, exit_price,
               realized_points, exit_reason, entry_reason, mfe, mae
        FROM trades
        ORDER BY entry_bar
    """)
    
    trades = []
    for row in cursor.fetchall():
        trades.append({
            'entry_bar': row[0],
            'exit_bar': row[1],
            'direction': row[2],
            'entry_price': row[3],
            'exit_price': row[4],
            'realized_points': row[5],
            'exit_reason': row[6],
            'entry_reason': row[7],
            'mfe': row[8],
            'mae': row[9]
        })
    
    conn.close()
    return trades

def check_entry_consistency(trade, params, bar_data):
    """Check if entry matches parameters"""
    issues = []
    entry_reason = trade.get('entry_reason', '')
    direction = trade['direction']
    entry_bar = trade['entry_bar']
    
    # Check EmaCrossoverRequireBodyBelow
    if params.get('EmaCrossoverRequireBodyBelow') == True and params.get('UseEmaCrossoverFilter') == True:
        if 'EMACrossoverOK' in entry_reason:
            # Should verify body was below/above Fast EMA
            if entry_bar in bar_data:
                bar = bar_data[entry_bar]
                open_p = bar.get('open_price')
                close_p = bar.get('close_price')
                fast_ema = bar.get('ema_fast_value')
                
                if open_p and close_p and fast_ema:
                    body_top = max(open_p, close_p)
                    body_bottom = min(open_p, close_p)
                    
                    if direction == 'Long':
                        # Body should be above Fast EMA
                        if body_bottom < fast_ema:
                            issues.append(f"Bar {entry_bar}: LONG entry with BodyBelow=true, but body bottom ({body_bottom}) < FastEMA ({fast_ema})")
                    elif direction == 'Short':
                        # Body should be below Fast EMA
                        if body_top > fast_ema:
                            issues.append(f"Bar {entry_bar}: SHORT entry with BodyBelow=true, but body top ({body_top}) > FastEMA ({fast_ema})")
    
    # Check AvoidLongsOnBadCandle / AvoidShortsOnGoodCandle
    if entry_bar in bar_data:
        bar = bar_data[entry_bar]
        candle_type = bar.get('candle_type', '')
        
        if direction == 'Long' and params.get('AvoidLongsOnBadCandle') == True:
            if candle_type == 'bad':
                issues.append(f"Bar {entry_bar}: LONG entry on bad candle, but AvoidLongsOnBadCandle=true")
        
        if direction == 'Short' and params.get('AvoidShortsOnGoodCandle') == True:
            if candle_type == 'good':
                issues.append(f"Bar {entry_bar}: SHORT entry on good candle, but AvoidShortsOnGoodCandle=true")
    
    return issues

def check_exit_consistency(trade, params, bar_data):
    """Check if exit matches parameters"""
    issues = []
    exit_reason = trade.get('exit_reason', '')
    exit_bar = trade['exit_bar']
    direction = trade['direction']
    
    # Check EMA stop trigger mode
    if 'BarsOnTheFlowEmaStop' in exit_reason:
        trigger_mode = params.get('EmaStopTriggerMode', 'CloseOnly')
        
        if exit_bar in bar_data:
            bar = bar_data[exit_bar]
            high = bar.get('high_price')
            low = bar.get('low_price')
            close_p = bar.get('close_price')
            open_p = bar.get('open_price')
            fast_ema = bar.get('ema_fast_value')
            
            if high and low and fast_ema:
                if trigger_mode == 'FullCandle':
                    if direction == 'Long':
                        # Both High and Low should be below Fast EMA
                        if high >= fast_ema:
                            issues.append(f"Bar {exit_bar}: LONG exit with FullCandle mode, but High ({high}) >= FastEMA ({fast_ema})")
                        if low >= fast_ema:
                            issues.append(f"Bar {exit_bar}: LONG exit with FullCandle mode, but Low ({low}) >= FastEMA ({fast_ema})")
                    elif direction == 'Short':
                        # Both High and Low should be above Fast EMA
                        if high <= fast_ema:
                            issues.append(f"Bar {exit_bar}: SHORT exit with FullCandle mode, but High ({high}) <= FastEMA ({fast_ema})")
                        if low <= fast_ema:
                            issues.append(f"Bar {exit_bar}: SHORT exit with FullCandle mode, but Low ({low}) <= FastEMA ({fast_ema})")
                
                elif trigger_mode == 'BodyOnly':
                    if open_p and close_p:
                        body_top = max(open_p, close_p)
                        body_bottom = min(open_p, close_p)
                        
                        if direction == 'Long':
                            if body_top >= fast_ema:
                                issues.append(f"Bar {exit_bar}: LONG exit with BodyOnly mode, but body top ({body_top}) >= FastEMA ({fast_ema})")
                        elif direction == 'Short':
                            if body_bottom <= fast_ema:
                                issues.append(f"Bar {exit_bar}: SHORT exit with BodyOnly mode, but body bottom ({body_bottom}) <= FastEMA ({fast_ema})")
    
    return issues

def get_bar_data():
    """Get bar data from database"""
    import sqlite3
    
    if not os.path.exists(VOLATILITY_DB):
        return {}
    
    conn = sqlite3.connect(VOLATILITY_DB)
    cursor = conn.cursor()
    
    cursor.execute("""
        SELECT bar_index, open_price, high_price, low_price, close_price,
               ema_fast_value, ema_slow_value, candle_type
        FROM bar_samples
        ORDER BY bar_index
    """)
    
    bar_data = {}
    for row in cursor.fetchall():
        bar_data[row[0]] = {
            'open_price': row[1],
            'high_price': row[2],
            'low_price': row[3],
            'close_price': row[4],
            'ema_fast_value': row[5],
            'ema_slow_value': row[6],
            'candle_type': row[7]
        }
    
    conn.close()
    return bar_data

def main():
    print("=" * 80)
    print("STRATEGY CONSISTENCY ANALYSIS")
    print("=" * 80)
    
    # Load parameters
    params = load_parameters()
    if not params:
        return
    
    print("\nKEY PARAMETERS:")
    print(f"  UseEmaTrailingStop: {params.get('UseEmaTrailingStop')}")
    print(f"  EmaStopTriggerMode: {params.get('EmaStopTriggerMode')}")
    print(f"  UseEmaCrossoverFilter: {params.get('UseEmaCrossoverFilter')}")
    print(f"  EmaCrossoverRequireBodyBelow: {params.get('EmaCrossoverRequireBodyBelow')}")
    print(f"  AvoidLongsOnBadCandle: {params.get('AvoidLongsOnBadCandle')}")
    print(f"  AvoidShortsOnGoodCandle: {params.get('AvoidShortsOnGoodCandle')}")
    print(f"  EnableBarsOnTheFlowTrendDetection: {params.get('EnableBarsOnTheFlowTrendDetection')}")
    print(f"  ExitOnTrendBreak: {params.get('ExitOnTrendBreak')}")
    
    # Get trades and bar data
    print("\nLoading trades and bar data...")
    trades = analyze_trades_from_db()
    bar_data = get_bar_data()
    
    print(f"Found {len(trades)} trades")
    print(f"Found {len(bar_data)} bars")
    
    # Analyze each trade
    print("\n" + "=" * 80)
    print("ANALYZING TRADES FOR INCONSISTENCIES")
    print("=" * 80)
    
    all_issues = []
    for trade in trades:
        entry_issues = check_entry_consistency(trade, params, bar_data)
        exit_issues = check_exit_consistency(trade, params, bar_data)
        
        if entry_issues or exit_issues:
            print(f"\nTrade: {trade['direction']} Entry Bar {trade['entry_bar']}, Exit Bar {trade['exit_bar']}")
            print(f"  Entry Reason: {trade.get('entry_reason', 'N/A')}")
            print(f"  Exit Reason: {trade.get('exit_reason', 'N/A')}")
            
            for issue in entry_issues:
                print(f"  [ENTRY ISSUE] {issue}")
                all_issues.append(('ENTRY', trade['entry_bar'], issue))
            
            for issue in exit_issues:
                print(f"  [EXIT ISSUE] {issue}")
                all_issues.append(('EXIT', trade['exit_bar'], issue))
    
    # Summary
    print("\n" + "=" * 80)
    print("SUMMARY")
    print("=" * 80)
    print(f"Total issues found: {len(all_issues)}")
    
    if all_issues:
        entry_issues = [i for i in all_issues if i[0] == 'ENTRY']
        exit_issues = [i for i in all_issues if i[0] == 'EXIT']
        print(f"  Entry issues: {len(entry_issues)}")
        print(f"  Exit issues: {len(exit_issues)}")
    else:
        print("✅ No inconsistencies found!")

if __name__ == "__main__":
    main()
