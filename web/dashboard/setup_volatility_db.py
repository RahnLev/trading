"""
Setup persistent volatility database for dynamic stop loss calculation.

This database persists across strategy runs and accumulates volume/bar size statistics
by hour of day to enable smarter stop loss calculations.

Tables:
- volatility_stats: Aggregated statistics by hour of day
- bar_samples: Individual bar samples for analysis
"""

import sqlite3
import os
from datetime import datetime

DB_PATH = os.path.join(os.path.dirname(__file__), 'volatility.db')

def create_database():
    """Create the volatility tracking database if it doesn't exist."""
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()
    
    # Table for aggregated volatility statistics by quarter hour (15 minutes)
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS volatility_stats (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            hour_of_day INTEGER NOT NULL,  -- 0-23 (Eastern time) - kept for backward compatibility
            quarter_hour INTEGER NOT NULL,  -- 0-95 (0=00:00-00:14, 1=00:15-00:29, ..., 95=23:45-23:59)
            day_of_week INTEGER,           -- 0-6 (Mon-Sun), NULL for all days
            symbol TEXT NOT NULL,
            
            -- Volume statistics
            avg_volume REAL,
            min_volume INTEGER,
            max_volume INTEGER,
            volume_stddev REAL,
            
            -- Bar size statistics (High - Low in points)
            avg_bar_range REAL,
            min_bar_range REAL,
            max_bar_range REAL,
            bar_range_stddev REAL,
            
            -- Correlation: bar_range normalized by volume
            avg_range_per_1k_volume REAL,  -- Average bar range per 1000 volume
            
            -- Sample count for confidence
            sample_count INTEGER DEFAULT 0,
            
            -- Timestamps
            first_sample_time TEXT,
            last_sample_time TEXT,
            last_updated TEXT,
            
            UNIQUE(quarter_hour, day_of_week, symbol)
        )
    ''')
    
    # Add quarter_hour column if it doesn't exist (for existing databases)
    try:
        cursor.execute('ALTER TABLE volatility_stats ADD COLUMN quarter_hour INTEGER')
        cursor.execute('UPDATE volatility_stats SET quarter_hour = hour_of_day * 4 WHERE quarter_hour IS NULL')
    except sqlite3.OperationalError:
        pass  # Column already exists
    
    # Table for individual bar samples (for detailed analysis)
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS bar_samples (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp TEXT NOT NULL,
            bar_index INTEGER,
            symbol TEXT NOT NULL,
            hour_of_day INTEGER NOT NULL,
            quarter_hour INTEGER NOT NULL,  -- 0-95 (0=00:00-00:14, 1=00:15-00:29, ..., 95=23:45-23:59)
            day_of_week INTEGER NOT NULL,
            
            -- Bar data
            open_price REAL,
            high_price REAL,
            low_price REAL,
            close_price REAL,
            volume INTEGER,
            
            -- Calculated metrics
            bar_range REAL,             -- High - Low
            body_size REAL,             -- abs(Close - Open)
            upper_wick REAL,
            lower_wick REAL,
            
            -- Volume-normalized metrics
            range_per_1k_volume REAL,   -- bar_range / (volume / 1000)
            
            -- Context
            direction TEXT,             -- LONG/SHORT/FLAT
            in_trade INTEGER,           -- 1 if in trade, 0 if flat
            trade_result_ticks REAL,    -- If trade exited this bar, the P/L in ticks
            
            created_at TEXT DEFAULT CURRENT_TIMESTAMP
        )
    ''')
    
    # Add quarter_hour column if it doesn't exist (for existing databases)
    try:
        cursor.execute('ALTER TABLE bar_samples ADD COLUMN quarter_hour INTEGER')
        cursor.execute('UPDATE bar_samples SET quarter_hour = hour_of_day * 4 WHERE quarter_hour IS NULL')
    except sqlite3.OperationalError:
        pass  # Column already exists
    
    # Index for fast lookups
    cursor.execute('''
        CREATE INDEX IF NOT EXISTS idx_bar_samples_hour 
        ON bar_samples(symbol, hour_of_day, day_of_week)
    ''')
    
    cursor.execute('''
        CREATE INDEX IF NOT EXISTS idx_bar_samples_quarter_hour 
        ON bar_samples(symbol, quarter_hour, day_of_week)
    ''')
    
    cursor.execute('''
        CREATE INDEX IF NOT EXISTS idx_bar_samples_timestamp 
        ON bar_samples(timestamp DESC)
    ''')
    
    # Table for recommended stop loss by hour (updated periodically)
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS stop_loss_recommendations (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            hour_of_day INTEGER NOT NULL,
            day_of_week INTEGER,        -- NULL for all days
            symbol TEXT NOT NULL,
            
            -- Recommendations (in ticks for MNQ)
            recommended_stop_ticks INTEGER,
            min_stop_ticks INTEGER,
            max_stop_ticks INTEGER,
            
            -- Basis for recommendation
            avg_bar_range_ticks INTEGER,
            volume_condition TEXT,       -- 'LOW', 'NORMAL', 'HIGH'
            confidence_level TEXT,       -- 'LOW', 'MEDIUM', 'HIGH' based on sample count
            
            last_updated TEXT,
            
            UNIQUE(hour_of_day, day_of_week, symbol, volume_condition)
        )
    ''')
    
    conn.commit()
    conn.close()
    print(f"Volatility database created/verified at: {DB_PATH}")

def get_stats_summary():
    """Get a summary of collected statistics."""
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()
    
    # Total bar samples
    cursor.execute('SELECT COUNT(*) FROM bar_samples')
    total_samples = cursor.fetchone()[0]
    
    # Samples by hour
    cursor.execute('''
        SELECT hour_of_day, COUNT(*), AVG(bar_range), AVG(volume)
        FROM bar_samples
        GROUP BY hour_of_day
        ORDER BY hour_of_day
    ''')
    by_hour = cursor.fetchall()
    
    conn.close()
    
    print(f"\n=== Volatility Database Summary ===")
    print(f"Total bar samples: {total_samples}")
    print(f"\nBy Hour (ET):")
    print(f"{'Hour':<6} {'Samples':<10} {'Avg Range':<12} {'Avg Volume':<12}")
    print("-" * 40)
    for row in by_hour:
        hour, count, avg_range, avg_vol = row
        avg_range = avg_range or 0
        avg_vol = avg_vol or 0
        print(f"{hour:02d}:00  {count:<10} {avg_range:>10.2f}  {avg_vol:>10.0f}")

def insert_bar_sample(timestamp, bar_index, symbol, open_p, high_p, low_p, close_p, 
                       volume, direction='FLAT', in_trade=False, trade_result_ticks=None):
    """Insert a single bar sample into the database."""
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()
    
    # Parse timestamp to get hour and day of week
    try:
        dt = datetime.strptime(timestamp, '%Y-%m-%d %H:%M:%S')
    except:
        dt = datetime.strptime(timestamp.split('.')[0], '%Y-%m-%d %H:%M:%S')
    
    hour_of_day = dt.hour
    # Calculate quarter hour: 0-95 (0=00:00-00:14, 1=00:15-00:29, ..., 95=23:45-23:59)
    quarter_hour = hour_of_day * 4 + (dt.minute // 15)
    day_of_week = dt.weekday()
    
    # Calculate metrics
    bar_range = high_p - low_p
    body_size = abs(close_p - open_p)
    
    if close_p >= open_p:  # Green/bullish candle
        upper_wick = high_p - close_p
        lower_wick = open_p - low_p
    else:  # Red/bearish candle
        upper_wick = high_p - open_p
        lower_wick = close_p - low_p
    
    # Volume-normalized metric (avoid division by zero)
    range_per_1k_volume = (bar_range / (volume / 1000)) if volume > 0 else 0
    
    cursor.execute('''
        INSERT INTO bar_samples (
            timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
            open_price, high_price, low_price, close_price, volume,
            bar_range, body_size, upper_wick, lower_wick,
            range_per_1k_volume, direction, in_trade, trade_result_ticks
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    ''', (
        timestamp, bar_index, symbol, hour_of_day, quarter_hour, day_of_week,
        open_p, high_p, low_p, close_p, volume,
        bar_range, body_size, upper_wick, lower_wick,
        range_per_1k_volume, direction, 1 if in_trade else 0, trade_result_ticks
    ))
    
    conn.commit()
    conn.close()

def update_aggregated_stats(symbol='MNQ'):
    """Update the aggregated volatility statistics from bar samples."""
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()
    
    # Calculate stats for each quarter hour (0-95)
    for quarter_hour in range(96):
        cursor.execute('''
            SELECT 
                AVG(volume) as avg_vol,
                MIN(volume) as min_vol,
                MAX(volume) as max_vol,
                AVG(bar_range) as avg_range,
                MIN(bar_range) as min_range,
                MAX(bar_range) as max_range,
                AVG(range_per_1k_volume) as avg_range_per_1k,
                COUNT(*) as sample_count,
                MIN(timestamp) as first_sample,
                MAX(timestamp) as last_sample
            FROM bar_samples
            WHERE quarter_hour = ? AND symbol = ?
        ''', (quarter_hour, symbol))
        
        row = cursor.fetchone()
        if row and row[7] > 0:  # sample_count > 0
            # Calculate hour_of_day from quarter_hour for backward compatibility
            hour_of_day = quarter_hour // 4
            cursor.execute('''
                INSERT OR REPLACE INTO volatility_stats (
                    hour_of_day, quarter_hour, day_of_week, symbol,
                    avg_volume, min_volume, max_volume,
                    avg_bar_range, min_bar_range, max_bar_range,
                    avg_range_per_1k_volume,
                    sample_count, first_sample_time, last_sample_time, last_updated
                ) VALUES (?, ?, NULL, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, datetime('now'))
            ''', (
                hour_of_day, quarter_hour, symbol,
                row[0], row[1], row[2],  # volume stats
                row[3], row[4], row[5],  # range stats
                row[6],                   # range per 1k volume
                row[7], row[8], row[9]   # counts and times
            ))
    
    conn.commit()
    conn.close()
    print("Aggregated statistics updated (by quarter hour).")

def get_recommended_stop(hour_of_day, current_volume, symbol='MNQ'):
    """
    Get recommended stop loss in ticks based on hour and current volume.
    
    Returns tuple: (recommended_ticks, confidence)
    """
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()
    
    # Calculate quarter_hour from hour_of_day and current minute
    # For backward compatibility, if we only have hour_of_day, use the first quarter of that hour
    from datetime import datetime
    now = datetime.now()
    quarter_hour = hour_of_day * 4 + (now.minute // 15)
    
    # Get stats for this quarter hour
    cursor.execute('''
        SELECT avg_bar_range, avg_volume, avg_range_per_1k_volume, sample_count
        FROM volatility_stats
        WHERE quarter_hour = ? AND symbol = ? AND day_of_week IS NULL
    ''', (quarter_hour, symbol))
    
    row = cursor.fetchone()
    conn.close()
    
    if not row or row[3] < 10:  # Need at least 10 samples
        return (16, 'LOW')  # Default 4 points = 16 ticks
    
    avg_range, avg_volume, avg_range_per_vol, sample_count = row
    
    # Determine volume condition
    if current_volume < avg_volume * 0.7:
        volume_condition = 'LOW'
        volume_multiplier = 0.8  # Tighter stops in low volume
    elif current_volume > avg_volume * 1.3:
        volume_condition = 'HIGH'
        volume_multiplier = 1.3  # Wider stops in high volume
    else:
        volume_condition = 'NORMAL'
        volume_multiplier = 1.0
    
    # Calculate recommended stop
    # Base: average bar range for this hour
    # Adjust: multiply by 1.2 to give some buffer
    # Then adjust for current volume
    base_stop_points = avg_range * 1.2 * volume_multiplier
    recommended_ticks = int(base_stop_points * 4)  # 4 ticks per point
    
    # Confidence based on sample count
    if sample_count >= 100:
        confidence = 'HIGH'
    elif sample_count >= 30:
        confidence = 'MEDIUM'
    else:
        confidence = 'LOW'
    
    # Clamp to reasonable range (2-20 points = 8-80 ticks)
    recommended_ticks = max(8, min(80, recommended_ticks))
    
    return (recommended_ticks, confidence)

if __name__ == '__main__':
    create_database()
    get_stats_summary()
