import pandas as pd
import numpy as np
import sys
import json
from pathlib import Path

def analyze_precision(csv_path):
    """Compute precision metrics for a strategy session."""
    df = pd.read_csv(csv_path, on_bad_lines='skip', engine='python')
    
    # Filter to actual bars (exclude duplicate log entries per bar)
    df = df.drop_duplicates(subset=['Bar'], keep='last')
    
    # EMA-Price Distance
    df['ema_price_dist'] = abs(df['Close'] - df['FastEMA'])
    df['ema_price_pct'] = (df['ema_price_dist'] / df['Close']) * 100
    ema_dist_mean = df['ema_price_dist'].mean()
    ema_dist_std = df['ema_price_dist'].std()
    ema_pct_mean = df['ema_price_pct'].mean()
    
    # Gradient Stability
    df['fast_grad_abs'] = df['FastGradient'].abs()
    grad_stability = df['FastGradient'].std()
    grad_mean = df['FastGradient'].mean()
    
    # Whipsaw Rate (signal flips)
    signal_flips = (df['NewSignal'] != df['PrevSignal']).sum()
    whipsaw_rate = (signal_flips / len(df)) * 100 if len(df) > 0 else 0
    
    # ADX Consistency (parse from Notes if available, or use separate column if exists)
    # For now, we'll skip ADX if not in main columns
    adx_mean = 0
    adx_std = 0
    
    # Entry Precision (trades with MFE/MAE data)
    trades = df[df['Action'].str.contains('EXIT', na=False)]
    if len(trades) > 0:
        mfe_mae_ratio = (trades['TradeMFE'].mean() / trades['TradeMAE'].mean()) if trades['TradeMAE'].mean() > 0 else 0
        avg_mfe = trades['TradeMFE'].mean()
        avg_mae = trades['TradeMAE'].mean()
        trade_count = len(trades)
    else:
        mfe_mae_ratio = 0
        avg_mfe = 0
        avg_mae = 0
        trade_count = 0
    
    return {
        'bars': int(len(df)),
        'ema_distance_mean': round(float(ema_dist_mean), 2),
        'ema_distance_std': round(float(ema_dist_std), 2),
        'ema_pct_mean': round(float(ema_pct_mean), 4),
        'gradient_stability': round(float(grad_stability), 4),
        'gradient_mean': round(float(grad_mean), 4),
        'whipsaw_rate_pct': round(float(whipsaw_rate), 2),
        'signal_flips': int(signal_flips),
        'mfe_mae_ratio': round(float(mfe_mae_ratio), 2),
        'avg_mfe': round(float(avg_mfe), 2),
        'avg_mae': round(float(avg_mae), 2),
        'trade_count': int(trade_count),
        'adx_mean': float(adx_mean),
        'adx_std': float(adx_std)
    }

def compare_and_recommend(metrics_30s, metrics_3m):
    """Compare metrics and recommend best timeframe."""
    score_30s = 0
    score_3m = 0
    reasons = []
    
    # Lower EMA distance % is better (tighter following)
    if metrics_30s['ema_pct_mean'] < metrics_3m['ema_pct_mean']:
        score_30s += 1
        reasons.append("30s: Tighter EMA-price tracking")
    else:
        score_3m += 1
        reasons.append("3m: More relaxed EMA-price spacing (reduces noise)")
    
    # Lower gradient stability (stddev) is better (smoother trends)
    if metrics_30s['gradient_stability'] < metrics_3m['gradient_stability']:
        score_30s += 2
        reasons.append("30s: Lower gradient volatility")
    else:
        score_3m += 2
        reasons.append("3m: Smoother gradient (less whipsaw)")
    
    # Lower whipsaw rate is better
    if metrics_30s['whipsaw_rate_pct'] < metrics_3m['whipsaw_rate_pct']:
        score_30s += 3
        reasons.append("30s: Fewer signal flips")
    else:
        score_3m += 3
        reasons.append("3m: Significantly fewer whipsaws")
    
    # Higher MFE/MAE ratio is better (better entry timing)
    if metrics_30s['mfe_mae_ratio'] > metrics_3m['mfe_mae_ratio']:
        score_30s += 2
        reasons.append("30s: Better MFE/MAE capture")
    else:
        score_3m += 2
        reasons.append("3m: Superior MFE/MAE ratio")
    
    if score_3m > score_30s:
        recommendation = "Prefer3m"
        explanation = f"3-minute timeframe wins ({score_3m} vs {score_30s}): " + "; ".join([r for r in reasons if '3m:' in r])
    elif score_30s > score_3m:
        recommendation = "Prefer30s"
        explanation = f"30-second timeframe wins ({score_30s} vs {score_3m}): " + "; ".join([r for r in reasons if '30s:' in r])
    else:
        recommendation = "Keep"
        explanation = "Both timeframes show similar performance; choose based on preference."
    
    return recommendation, explanation, reasons

if __name__ == '__main__':
    # For now, analyze the 3m CSV only
    csv_3m = Path(r"c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\strategy_logs\GradientSlope_MNQ 12-25_2025-12-02_00-32-53.csv")
    
    if not csv_3m.exists():
        print(f"ERROR: CSV not found: {csv_3m}")
        sys.exit(1)
    
    metrics_3m = analyze_precision(csv_3m)
    
    # Analyze 30-second session
    csv_30s = Path(r"c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\strategy_logs\GradientSlope_MNQ 12-25_2025-12-02_00-56-03.csv")
    metrics_30s = None
    
    if csv_30s.exists():
        print("=== 30-Second Timeframe Precision Metrics ===")
        metrics_30s = analyze_precision(csv_30s)
        print(json.dumps(metrics_30s, indent=2))
        print()
    
    print("=== 3-Minute Timeframe Precision Metrics ===")
    print(json.dumps(metrics_3m, indent=2))
    print()
    
    # Compare and recommend
    if metrics_30s:
        print("=== Timeframe Comparison & Recommendation ===")
        recommendation, explanation, reasons = compare_and_recommend(metrics_30s, metrics_3m)
        result = {
            "recommendation": recommendation,
            "explanation": explanation,
            "reasons": reasons,
            "metrics_30s": metrics_30s,
            "metrics_3m": metrics_3m
        }
        print(json.dumps(result, indent=2))
