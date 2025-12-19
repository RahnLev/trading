"""
Import historical bar data from strategy logs into the volatility database.
This bootstraps the database with existing data for immediate use.
"""

import sqlite3
import csv
import os
import glob
from datetime import datetime
from setup_volatility_db import DB_PATH, insert_bar_sample, update_aggregated_stats

LOG_DIR = os.path.join(os.path.dirname(__file__), '..', '..', 'strategy_logs')

def import_from_csv(csv_path, symbol='MNQ'):
    """Import bar data from a strategy log CSV file."""
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()
    
    imported = 0
    skipped = 0
    
    with open(csv_path, 'r', encoding='utf-8') as f:
        reader = csv.DictReader(f)
        
        batch = []
        for row in reader:
            try:
                timestamp = row['timestamp']
                bar_index = int(row['bar'])
                
                # Skip if we already have this bar
                cursor.execute(
                    'SELECT 1 FROM bar_samples WHERE bar_index = ? AND timestamp LIKE ?',
                    (bar_index, timestamp[:10] + '%')
                )
                if cursor.fetchone():
                    skipped += 1
                    continue
                
                open_p = float(row['open'])
                high_p = float(row['high'])
                low_p = float(row['low'])
                close_p = float(row['close'])
                volume = int(row['volume'])
                direction = row['direction']
                action = row.get('action', '')
                pnl = row.get('pnl', '')
                
                in_trade = direction in ('LONG', 'SHORT')
                trade_result = float(pnl) * 4 if pnl and pnl != '' else None  # Convert points to ticks
                
                # Parse timestamp
                try:
                    dt = datetime.strptime(timestamp.split('.')[0], '%Y-%m-%d %H:%M:%S')
                except:
                    continue
                
                hour_of_day = dt.hour
                day_of_week = dt.weekday()
                
                # Calculate metrics
                bar_range = high_p - low_p
                body_size = abs(close_p - open_p)
                
                if close_p >= open_p:
                    upper_wick = high_p - close_p
                    lower_wick = open_p - low_p
                else:
                    upper_wick = high_p - open_p
                    lower_wick = close_p - low_p
                
                range_per_1k_volume = (bar_range / (volume / 1000)) if volume > 0 else 0
                
                batch.append((
                    timestamp, bar_index, symbol, hour_of_day, day_of_week,
                    open_p, high_p, low_p, close_p, volume,
                    bar_range, body_size, upper_wick, lower_wick,
                    range_per_1k_volume, direction, 1 if in_trade else 0, trade_result
                ))
                
                imported += 1
                
                # Batch insert every 1000 rows
                if len(batch) >= 1000:
                    cursor.executemany('''
                        INSERT INTO bar_samples (
                            timestamp, bar_index, symbol, hour_of_day, day_of_week,
                            open_price, high_price, low_price, close_price, volume,
                            bar_range, body_size, upper_wick, lower_wick,
                            range_per_1k_volume, direction, in_trade, trade_result_ticks
                        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    ''', batch)
                    conn.commit()
                    batch = []
                    
            except Exception as e:
                print(f"Error processing row: {e}")
                continue
        
        # Insert remaining batch
        if batch:
            cursor.executemany('''
                INSERT INTO bar_samples (
                    timestamp, bar_index, symbol, hour_of_day, day_of_week,
                    open_price, high_price, low_price, close_price, volume,
                    bar_range, body_size, upper_wick, lower_wick,
                    range_per_1k_volume, direction, in_trade, trade_result_ticks
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ''', batch)
            conn.commit()
    
    conn.close()
    return imported, skipped

def main():
    # Find all strategy log CSVs
    csv_files = glob.glob(os.path.join(LOG_DIR, 'BarsOnTheFlow_MNQ*.csv'))
    csv_files = [f for f in csv_files if 'Opportunities' not in f and 'OutputWindow' not in f]
    
    print(f"Found {len(csv_files)} strategy log files")
    
    total_imported = 0
    total_skipped = 0
    
    for csv_path in csv_files:
        print(f"\nProcessing: {os.path.basename(csv_path)}")
        imported, skipped = import_from_csv(csv_path)
        total_imported += imported
        total_skipped += skipped
        print(f"  Imported: {imported}, Skipped (duplicates): {skipped}")
    
    print(f"\n=== Import Complete ===")
    print(f"Total imported: {total_imported}")
    print(f"Total skipped: {total_skipped}")
    
    # Update aggregated stats
    print("\nUpdating aggregated statistics...")
    update_aggregated_stats()
    
    # Show summary
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()
    
    print("\n=== Stats by Hour ===")
    print(f"{'Hour':<6} {'Samples':<10} {'Avg Range':<12} {'Avg Volume':<12} {'Range/1kVol':<12}")
    print("-" * 52)
    
    cursor.execute('''
        SELECT hour_of_day, sample_count, avg_bar_range, avg_volume, avg_range_per_1k_volume
        FROM volatility_stats
        WHERE day_of_week IS NULL
        ORDER BY hour_of_day
    ''')
    
    for row in cursor.fetchall():
        hour, count, avg_range, avg_vol, range_per_vol = row
        avg_range = avg_range or 0
        avg_vol = avg_vol or 0
        range_per_vol = range_per_vol or 0
        print(f"{hour:02d}:00  {count:<10} {avg_range:>10.2f}  {avg_vol:>10.0f}  {range_per_vol:>10.4f}")
    
    conn.close()

if __name__ == '__main__':
    main()
