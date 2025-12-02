import sqlite3, time, json, os

DB_PATH = os.path.join(os.path.dirname(__file__), '..', 'dashboard', 'dashboard.db')
DB_PATH = os.path.abspath(DB_PATH)

def main():
    conn = sqlite3.connect(DB_PATH)
    cur = conn.cursor()
    ts = time.time()
    action = 'FORENSICS_BAR_2554'
    details = json.dumps({
        'bar': 2554,
        'entry': {'side':'LONG','price':25408.25},
        'exit': {'bar':2556,'price':25392.75,'reason':'VALIDATION_FAILED'},
        'context': {
            'signal_start_bar': 2553,
            'filters': {'ADX':26.90,'RSI':58.9,'Accel':0.2169,'FastGrad':0.229,'SlowGrad':0.540},
            'reversal_next_bar': True
        },
        'pnl_points': -15.5
    })
    reasoning = 'Entry taken on 2-bar confirmation with accel alignment; immediate reversal triggered validation failure and forced exit.'
    diff_summary = 'Captured CSV/log evidence; recommend stronger reversal confirmation.'
    cur.execute("""
        INSERT INTO ai_footprints (ts, action, details, reasoning, diff_summary)
        VALUES (?, ?, ?, ?, ?)
    """, (ts, action, details, reasoning, diff_summary))
    conn.commit()
    print(f"Inserted footprint at {ts} -> {action}\nDB: {DB_PATH}")

if __name__ == '__main__':
    main()
