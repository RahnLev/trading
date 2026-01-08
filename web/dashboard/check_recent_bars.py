import sqlite3

conn = sqlite3.connect('volatility.db')
cursor = conn.cursor()

# Get max bar
cursor.execute('SELECT MAX(bar_index) FROM bar_samples')
max_bar = cursor.fetchone()[0]
print(f'Max bar_index in database: {max_bar}')

# Get last 10 bars
cursor.execute('''
    SELECT bar_index, candle_type, trend_up, trend_down, 
           allow_long_this_bar, allow_short_this_bar,
           pending_long_from_bad, pending_short_from_good
    FROM bar_samples 
    WHERE bar_index >= ? 
    ORDER BY bar_index DESC 
    LIMIT 10
''', (max_bar - 9 if max_bar else 0,))

rows = cursor.fetchall()
print('\nLast 10 bars:')
print('Bar | Candle | TrendUp | TrendDown | Allow L | Allow S | Pending L | Pending S')
print('-' * 80)
for r in rows:
    print(f'{r[0]:<4} | {str(r[1] or "NULL"):<6} | {r[2]:<7} | {r[3]:<8} | {r[4]:<7} | {r[5]:<7} | {r[6]:<9} | {r[7]:<9}')

conn.close()
