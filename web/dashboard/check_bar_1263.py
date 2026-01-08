#!/usr/bin/env python3
"""
Check bar 1263 to investigate LONG exit with 1 point stop loss
"""
import sqlite3
import os

# Database paths
VOLATILITY_DB = os.path.join(os.path.dirname(__file__), 'volatility.db')
BARS_DB = os.path.join(os.path.dirname(__file__), 'bars.db')

def check_bar_1263():
    """Check bar 1263 data and trade exit"""
    print("=" * 80)
    print("CHECKING BAR 1263 - LONG EXIT INVESTIGATION")
    print("=" * 80)
    
    # Check bar_samples for bar 1263
    if os.path.exists(VOLATILITY_DB):
        print("\n1. BAR SAMPLES DATA (volatility.db):")
        print("-" * 80)
        conn = sqlite3.connect(VOLATILITY_DB)
        cursor = conn.cursor()
        
        cursor.execute("""
            SELECT bar_index, timestamp, open_price, high_price, low_price, close_price,
                   ema_fast_value, ema_slow_value, fast_ema_grad_deg,
                   stop_loss_points, candle_type, trend_up, trend_down,
                   allow_long_this_bar, allow_short_this_bar, entry_reason
            FROM bar_samples
            WHERE bar_index = 1263
            LIMIT 1
        """)
        
        row = cursor.fetchone()
        if row:
            idx, ts, open_p, high_p, low_p, close_p, fast_ema, slow_ema, grad, stop_loss, candle, trend_u, trend_d, allow_l, allow_s, reason = row
            print(f"Bar {idx}:")
            print(f"  Timestamp: {ts}")
            print(f"  OHLC: O={open_p}, H={high_p}, L={low_p}, C={close_p}")
            print(f"  Fast EMA: {fast_ema}, Slow EMA: {slow_ema}")
            print(f"  Gradient: {grad}°")
            print(f"  Stop Loss Points: {stop_loss}")
            print(f"  Candle Type: {candle}")
            print(f"  Trend Up: {trend_u}, Trend Down: {trend_d}")
            print(f"  Allow Long: {allow_l}, Allow Short: {allow_s}")
            print(f"  Entry Reason: {reason}")
        else:
            print("  Bar 1263 not found in bar_samples")
        
        # Check surrounding bars
        print("\n2. SURROUNDING BARS (1261-1265):")
        print("-" * 80)
        cursor.execute("""
            SELECT bar_index, close_price, ema_fast_value, ema_slow_value, 
                   stop_loss_points, candle_type, entry_reason
            FROM bar_samples
            WHERE bar_index BETWEEN 1261 AND 1265
            ORDER BY bar_index
        """)
        
        for row in cursor.fetchall():
            idx, close, fast, slow, stop, candle, reason = row
            print(f"  Bar {idx}: C={close}, FastEMA={fast}, SlowEMA={slow}, Stop={stop}, Candle={candle}, Reason={reason}")
        
        # Check trades table
        print("\n3. TRADES DATA:")
        print("-" * 80)
        cursor.execute("""
            SELECT entry_bar, exit_bar, direction, entry_price, exit_price,
                   realized_points, exit_reason, entry_reason, mfe, mae
            FROM trades
            WHERE entry_bar = 1263 OR exit_bar = 1263
            ORDER BY entry_bar
        """)
        
        trades = cursor.fetchall()
        if trades:
            for trade in trades:
                entry_b, exit_b, dir, entry_p, exit_p, points, exit_r, entry_r, mfe, mae = trade
                print(f"  Entry Bar: {entry_b}, Exit Bar: {exit_b}, Direction: {dir}")
                print(f"    Entry Price: {entry_p}, Exit Price: {exit_p}")
                print(f"    Realized Points: {points}")
                print(f"    Exit Reason: {exit_r}")
                print(f"    Entry Reason: {entry_r}")
                print(f"    MFE: {mfe}, MAE: {mae}")
                
                if exit_b == 1263:
                    print(f"\n  EXIT ANALYSIS:")
                    print(f"    Exit Price: {exit_p}")
                    if dir == "Long":
                        stop_loss_price = entry_p - 1.0  # 1 point stop loss
                        print(f"    Entry Price: {entry_p}")
                        print(f"    Expected Stop Loss (1 point): {stop_loss_price}")
                        print(f"    Actual Exit Price: {exit_p}")
                        print(f"    Difference: {entry_p - exit_p} points")
        else:
            print("  No trades found with entry_bar=1263 or exit_bar=1263")
        
        conn.close()
    
    # Check BarsOnTheFlowStateAndBar for state at bar 1263
    if os.path.exists(BARS_DB):
        print("\n4. STATE DATA (bars.db - BarsOnTheFlowStateAndBar):")
        print("-" * 80)
        conn = sqlite3.connect(BARS_DB)
        cursor = conn.cursor()
        
        cursor.execute("""
            SELECT barIndex, currentBar, positionMarketPosition, positionQuantity,
                   positionAveragePrice, stopLossPoints, calculatedStopPoints,
                   open, high, low, close, fastGradDeg
            FROM BarsOnTheFlowStateAndBar
            WHERE barIndex = 1263 OR currentBar = 1263
            ORDER BY receivedTs DESC
            LIMIT 5
        """)
        
        rows = cursor.fetchall()
        if rows:
            for row in rows:
                bar_idx, curr_bar, pos, qty, avg_price, stop_pts, calc_stop, open_p, high_p, low_p, close_p, grad = row
                print(f"  Bar Index: {bar_idx}, Current Bar: {curr_bar}")
                print(f"    Position: {pos}, Quantity: {qty}, Avg Price: {avg_price}")
                print(f"    Stop Loss Points: {stop_pts}, Calculated Stop Points: {calc_stop}")
                print(f"    OHLC: O={open_p}, H={high_p}, L={low_p}, C={close_p}")
                print(f"    Gradient: {grad}°")
        else:
            print("  No state data found for bar 1263")
        
        conn.close()

if __name__ == "__main__":
    check_bar_1263()
