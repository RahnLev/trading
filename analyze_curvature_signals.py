#!/usr/bin/env python3
"""
Curvature Signal Analyzer
Analyzes NinjaTrader indicator logs to find optimal curvature parameters for signal generation.

This script:
1. Reads CSV logs from the indicator
2. Tests different curvature thresholds and signal rules
3. Analyzes signal quality based on price movement after signals
4. Provides recommendations for optimal parameters
"""

import pandas as pd
import numpy as np
import matplotlib.pyplot as plt
from pathlib import Path
from datetime import datetime
import argparse
from typing import Dict, List, Tuple, Optional


class CurvatureSignalAnalyzer:
    def __init__(self, csv_path: str):
        """Initialize analyzer with CSV log file."""
        self.csv_path = csv_path
        self.df = None
        self.results = {}
        
    def load_data(self) -> bool:
        """Load and validate CSV data."""
        try:
            print(f"Loading data from: {self.csv_path}")
            self.df = pd.read_csv(self.csv_path)
            
            # Check for required columns
            required_cols = ['close', 'curvature_ratio']
            missing = [col for col in required_cols if col not in self.df.columns]
            if missing:
                print(f"ERROR: Missing required columns: {missing}")
                print(f"Available columns: {list(self.df.columns)}")
                return False
            
            # Convert curvature_ratio to numeric, handling any errors
            self.df['curvature_ratio'] = pd.to_numeric(self.df['curvature_ratio'], errors='coerce')
            
            # Remove rows with NaN curvature
            initial_len = len(self.df)
            self.df = self.df.dropna(subset=['curvature_ratio'])
            dropped = initial_len - len(self.df)
            if dropped > 0:
                print(f"Dropped {dropped} rows with NaN curvature values")
            
            print(f"Loaded {len(self.df)} data points")
            print(f"Curvature range: [{self.df['curvature_ratio'].min():.4f}, {self.df['curvature_ratio'].max():.4f}]")
            print(f"Curvature mean: {self.df['curvature_ratio'].mean():.4f}, std: {self.df['curvature_ratio'].std():.4f}")
            
            return True
            
        except FileNotFoundError:
            print(f"ERROR: File not found: {self.csv_path}")
            return False
        except Exception as e:
            print(f"ERROR loading data: {e}")
            return False
    
    def generate_signals_with_rules(self, 
                                   bull_threshold: float = 1.5,
                                   bear_threshold: float = -1.5,
                                   reset_threshold: float = 0.0,
                                   tolerance_bars: int = 1,
                                   force_signal_threshold: Optional[float] = None) -> pd.DataFrame:
        """
        Generate bull/bear signals based on curvature with sophisticated rules.
        
        Args:
            bull_threshold: Curvature above this triggers bull signal
            bear_threshold: Curvature below this triggers bear signal
            reset_threshold: Signal resets if curvature crosses this
            tolerance_bars: Number of bars curvature can stay below reset before killing signal
            force_signal_threshold: If curvature exceeds this, force signal regardless of state
        
        Returns:
            DataFrame with signal columns added
        """
        df = self.df.copy()
        
        # Initialize signal tracking
        df['bull_signal'] = False
        df['bear_signal'] = False
        df['signal_strength'] = 0.0  # Track signal confidence
        
        # State tracking
        bull_active = False
        bear_active = False
        bars_below_reset_bull = 0
        bars_above_reset_bear = 0
        
        for i in range(len(df)):
            curv = df.iloc[i]['curvature_ratio']
            
            # BULL SIGNAL LOGIC
            # Force bull signal if curvature is extremely high
            if force_signal_threshold and curv >= force_signal_threshold:
                bull_active = True
                bars_below_reset_bull = 0
                df.iloc[i, df.columns.get_loc('signal_strength')] = abs(curv)
            
            # Start bull signal if crossing threshold from below
            elif curv >= bull_threshold and not bull_active:
                bull_active = True
                bars_below_reset_bull = 0
                df.iloc[i, df.columns.get_loc('signal_strength')] = abs(curv)
            
            # Maintain bull signal with tolerance
            elif bull_active:
                if curv < reset_threshold:
                    bars_below_reset_bull += 1
                    # Kill signal if exceeds tolerance
                    if bars_below_reset_bull > tolerance_bars:
                        bull_active = False
                        bars_below_reset_bull = 0
                        df.iloc[i, df.columns.get_loc('signal_strength')] = 0.0
                    else:
                        # Still within tolerance
                        df.iloc[i, df.columns.get_loc('signal_strength')] = abs(curv) * (1 - bars_below_reset_bull / (tolerance_bars + 1))
                else:
                    # Back above reset threshold, reset counter
                    bars_below_reset_bull = 0
                    df.iloc[i, df.columns.get_loc('signal_strength')] = abs(curv)
            
            # BEAR SIGNAL LOGIC
            # Force bear signal if curvature is extremely negative
            if force_signal_threshold and curv <= -force_signal_threshold:
                bear_active = True
                bars_above_reset_bear = 0
                df.iloc[i, df.columns.get_loc('signal_strength')] = abs(curv)
            
            # Start bear signal if crossing threshold from above
            elif curv <= bear_threshold and not bear_active:
                bear_active = True
                bars_above_reset_bear = 0
                df.iloc[i, df.columns.get_loc('signal_strength')] = abs(curv)
            
            # Maintain bear signal with tolerance
            elif bear_active:
                if curv > reset_threshold:
                    bars_above_reset_bear += 1
                    # Kill signal if exceeds tolerance
                    if bars_above_reset_bear > tolerance_bars:
                        bear_active = False
                        bars_above_reset_bear = 0
                        df.iloc[i, df.columns.get_loc('signal_strength')] = 0.0
                    else:
                        # Still within tolerance
                        df.iloc[i, df.columns.get_loc('signal_strength')] = abs(curv) * (1 - bars_above_reset_bear / (tolerance_bars + 1))
                else:
                    # Back below reset threshold, reset counter
                    bars_above_reset_bear = 0
                    df.iloc[i, df.columns.get_loc('signal_strength')] = abs(curv)
            
            # Set signal flags
            df.iloc[i, df.columns.get_loc('bull_signal')] = bull_active
            df.iloc[i, df.columns.get_loc('bear_signal')] = bear_active
        
        return df
    
    def evaluate_signal_quality(self, df: pd.DataFrame, lookahead_bars: int = 10) -> Dict:
        """
        Evaluate signal quality by measuring price movement after signals.
        
        Args:
            df: DataFrame with signal columns
            lookahead_bars: How many bars ahead to measure price movement
        
        Returns:
            Dictionary with quality metrics
        """
        metrics = {
            'total_bull_signals': 0,
            'total_bear_signals': 0,
            'bull_win_rate': 0.0,
            'bear_win_rate': 0.0,
            'bull_avg_move': 0.0,
            'bear_avg_move': 0.0,
            'bull_trades': [],
            'bear_trades': []
        }
        
        # Find signal entries (transitions from False to True)
        df['bull_entry'] = (df['bull_signal'] == True) & (df['bull_signal'].shift(1) == False)
        df['bear_entry'] = (df['bear_signal'] == True) & (df['bear_signal'].shift(1) == False)
        
        # Analyze bull signals
        bull_entries = df[df['bull_entry']].index.tolist()
        for entry_idx in bull_entries:
            # Get integer position
            entry_pos = df.index.get_loc(entry_idx)
            if entry_pos + lookahead_bars >= len(df):
                continue
            
            entry_price = df.loc[entry_idx, 'close']
            future_prices = df.iloc[entry_pos:entry_pos+lookahead_bars]['close']
            max_price = future_prices.max()
            price_move = (max_price - entry_price) / entry_price * 100  # Percent move
            
            metrics['bull_trades'].append({
                'index': entry_idx,
                'entry_price': entry_price,
                'max_price': max_price,
                'move_pct': price_move,
                'won': price_move > 0.1  # Consider >0.1% a win
            })
        
        # Analyze bear signals
        bear_entries = df[df['bear_entry']].index.tolist()
        for entry_idx in bear_entries:
            # Get integer position
            entry_pos = df.index.get_loc(entry_idx)
            if entry_pos + lookahead_bars >= len(df):
                continue
            
            entry_price = df.loc[entry_idx, 'close']
            future_prices = df.iloc[entry_pos:entry_pos+lookahead_bars]['close']
            min_price = future_prices.min()
            price_move = (entry_price - min_price) / entry_price * 100  # Percent move (inverted for bear)
            
            metrics['bear_trades'].append({
                'index': entry_idx,
                'entry_price': entry_price,
                'min_price': min_price,
                'move_pct': price_move,
                'won': price_move > 0.1  # Consider >0.1% a win
            })
        
        # Calculate summary metrics
        if metrics['bull_trades']:
            metrics['total_bull_signals'] = len(metrics['bull_trades'])
            metrics['bull_win_rate'] = sum(1 for t in metrics['bull_trades'] if t['won']) / len(metrics['bull_trades']) * 100
            metrics['bull_avg_move'] = np.mean([t['move_pct'] for t in metrics['bull_trades']])
        
        if metrics['bear_trades']:
            metrics['total_bear_signals'] = len(metrics['bear_trades'])
            metrics['bear_win_rate'] = sum(1 for t in metrics['bear_trades'] if t['won']) / len(metrics['bear_trades']) * 100
            metrics['bear_avg_move'] = np.mean([t['move_pct'] for t in metrics['bear_trades']])
        
        return metrics
    
    def optimize_parameters(self,
                          bull_thresholds: List[float] = [0.3, 0.5, 0.75, 1.0, 1.5],
                          tolerance_bars: List[int] = [0, 1, 2],
                          force_thresholds: List[Optional[float]] = [None, 2.0, 3.0]) -> pd.DataFrame:
        """
        Test multiple parameter combinations to find optimal settings.
        
        Returns:
            DataFrame with results for each parameter combination
        """
        results = []
        
        total_tests = len(bull_thresholds) * len(tolerance_bars) * len(force_thresholds)
        test_num = 0
        
        for bull_thresh in bull_thresholds:
            bear_thresh = -bull_thresh  # Mirror threshold
            
            for tol_bars in tolerance_bars:
                for force_thresh in force_thresholds:
                    test_num += 1
                    print(f"Testing combination {test_num}/{total_tests}: "
                          f"bull={bull_thresh:.1f}, bear={bear_thresh:.1f}, tol={tol_bars}, force={force_thresh}")
                    
                    # Generate signals with these parameters
                    df_signals = self.generate_signals_with_rules(
                        bull_threshold=bull_thresh,
                        bear_threshold=bear_thresh,
                        reset_threshold=0.0,
                        tolerance_bars=tol_bars,
                        force_signal_threshold=force_thresh
                    )
                    
                    # Evaluate signal quality
                    metrics = self.evaluate_signal_quality(df_signals)
                    
                    # Store results
                    results.append({
                        'bull_threshold': bull_thresh,
                        'bear_threshold': bear_thresh,
                        'tolerance_bars': tol_bars,
                        'force_threshold': force_thresh if force_thresh else 'None',
                        'total_bull_signals': metrics['total_bull_signals'],
                        'total_bear_signals': metrics['total_bear_signals'],
                        'bull_win_rate': metrics['bull_win_rate'],
                        'bear_win_rate': metrics['bear_win_rate'],
                        'bull_avg_move': metrics['bull_avg_move'],
                        'bear_avg_move': metrics['bear_avg_move'],
                        'combined_win_rate': (metrics['bull_win_rate'] + metrics['bear_win_rate']) / 2
                    })
        
        results_df = pd.DataFrame(results)
        results_df = results_df.sort_values('combined_win_rate', ascending=False)
        
        return results_df
    
    def plot_curvature_analysis(self, df: pd.DataFrame, output_path: str = None):
        """Create visualization of curvature and signals."""
        fig, axes = plt.subplots(3, 1, figsize=(15, 10), sharex=True)
        
        # Plot 1: Price
        axes[0].plot(df.index, df['close'], label='Close Price', linewidth=1)
        
        # Mark bull signal entries
        bull_entries = df[df['bull_signal'] & ~df['bull_signal'].shift(1).fillna(False)].index
        axes[0].scatter(bull_entries, df.loc[bull_entries, 'close'], 
                       color='green', marker='^', s=100, label='Bull Entry', zorder=5)
        
        # Mark bear signal entries
        bear_entries = df[df['bear_signal'] & ~df['bear_signal'].shift(1).fillna(False)].index
        axes[0].scatter(bear_entries, df.loc[bear_entries, 'close'], 
                       color='red', marker='v', s=100, label='Bear Entry', zorder=5)
        
        axes[0].set_ylabel('Price')
        axes[0].set_title('Price with Signal Entries')
        axes[0].legend()
        axes[0].grid(True, alpha=0.3)
        
        # Plot 2: Curvature Ratio
        axes[1].plot(df.index, df['curvature_ratio'], label='Curvature Ratio', linewidth=1, color='blue')
        axes[1].axhline(y=0, color='black', linestyle='--', linewidth=0.5)
        axes[1].fill_between(df.index, 0, df['curvature_ratio'], 
                            where=(df['curvature_ratio'] > 0), color='green', alpha=0.3)
        axes[1].fill_between(df.index, 0, df['curvature_ratio'], 
                            where=(df['curvature_ratio'] < 0), color='red', alpha=0.3)
        axes[1].set_ylabel('Curvature Ratio')
        axes[1].set_title('EMA Curvature Ratio')
        axes[1].legend()
        axes[1].grid(True, alpha=0.3)
        
        # Plot 3: Active Signals
        bull_signal_plot = df['bull_signal'].astype(int)
        bear_signal_plot = -df['bear_signal'].astype(int)
        
        axes[2].fill_between(df.index, 0, bull_signal_plot, 
                            where=(bull_signal_plot > 0), color='green', alpha=0.5, label='Bull Signal Active')
        axes[2].fill_between(df.index, 0, bear_signal_plot, 
                            where=(bear_signal_plot < 0), color='red', alpha=0.5, label='Bear Signal Active')
        axes[2].set_ylabel('Signal State')
        axes[2].set_xlabel('Bar Index')
        axes[2].set_title('Active Signals')
        axes[2].set_ylim(-1.5, 1.5)
        axes[2].legend()
        axes[2].grid(True, alpha=0.3)
        
        plt.tight_layout()
        
        if output_path:
            plt.savefig(output_path, dpi=150, bbox_inches='tight')
            print(f"Chart saved to: {output_path}")
        else:
            plt.show()
        
        plt.close()


def main():
    parser = argparse.ArgumentParser(description='Analyze curvature signals from NinjaTrader CSV log')
    parser.add_argument('csv_file', nargs='?', help='Path to CSV log file (optional if in indicators_log folder)')
    parser.add_argument('--optimize', action='store_true', help='Run parameter optimization')
    parser.add_argument('--bull-threshold', type=float, default=1.5, help='Bull curvature threshold (default: 1.5)')
    parser.add_argument('--bear-threshold', type=float, default=-1.5, help='Bear curvature threshold (default: -1.5)')
    parser.add_argument('--tolerance', type=int, default=1, help='Tolerance bars for reset (default: 1)')
    parser.add_argument('--force-threshold', type=float, help='Force signal threshold (optional)')
    parser.add_argument('--lookahead', type=int, default=10, help='Bars to look ahead for evaluation (default: 10)')
    parser.add_argument('--plot', action='store_true', help='Generate visualization plot')
    
    args = parser.parse_args()
    
    # Find CSV file
    csv_path = args.csv_file
    if not csv_path:
        # Look for most recent CSV in Indicator_logs folder (try both variants)
        log_dir = Path(__file__).parent / 'Indicator_logs'
        if not log_dir.exists():
            log_dir = Path(__file__).parent / 'indicators_log'
        if log_dir.exists():
            csv_files = list(log_dir.glob('CBASTestingIndicator3_*.csv'))
            if csv_files:
                csv_path = str(max(csv_files, key=lambda p: p.stat().st_mtime))
                print(f"Using most recent CSV: {csv_path}")
            else:
                print("No CSV files found in indicators_log folder")
                return
        else:
            print("Please provide CSV file path or ensure indicators_log folder exists")
            return
    
    # Initialize analyzer
    analyzer = CurvatureSignalAnalyzer(csv_path)
    
    if not analyzer.load_data():
        return
    
    print("\n" + "="*80)
    
    if args.optimize:
        # Run optimization
        print("Running parameter optimization...")
        print("This will test multiple combinations to find optimal settings.\n")
        
        results_df = analyzer.optimize_parameters()
        
        print("\n" + "="*80)
        print("TOP 10 PARAMETER COMBINATIONS:")
        print("="*80)
        print(results_df.head(10).to_string(index=False))
        
        # Save full results
        output_file = f"curvature_optimization_{datetime.now().strftime('%Y%m%d_%H%M%S')}.csv"
        results_df.to_csv(output_file, index=False)
        print(f"\nFull results saved to: {output_file}")
        
        # Use best parameters for visualization
        best = results_df.iloc[0]
        print(f"\n" + "="*80)
        print("RECOMMENDED PARAMETERS:")
        print("="*80)
        print(f"Bull Threshold: {best['bull_threshold']:.1f}")
        print(f"Bear Threshold: {best['bear_threshold']:.1f}")
        print(f"Tolerance Bars: {best['tolerance_bars']}")
        print(f"Force Threshold: {best['force_threshold']}")
        print(f"Combined Win Rate: {best['combined_win_rate']:.1f}%")
        
        if args.plot:
            df_signals = analyzer.generate_signals_with_rules(
                bull_threshold=best['bull_threshold'],
                bear_threshold=best['bear_threshold'],
                tolerance_bars=int(best['tolerance_bars']),
                force_signal_threshold=None if best['force_threshold'] == 'None' else best['force_threshold']
            )
            output_plot = f"curvature_analysis_{datetime.now().strftime('%Y%m%d_%H%M%S')}.png"
            analyzer.plot_curvature_analysis(df_signals, output_plot)
    
    else:
        # Single parameter test
        print(f"Testing parameters:")
        print(f"  Bull Threshold: {args.bull_threshold}")
        print(f"  Bear Threshold: {args.bear_threshold}")
        print(f"  Tolerance Bars: {args.tolerance}")
        print(f"  Force Threshold: {args.force_threshold}")
        print()
        
        df_signals = analyzer.generate_signals_with_rules(
            bull_threshold=args.bull_threshold,
            bear_threshold=args.bear_threshold,
            reset_threshold=0.0,
            tolerance_bars=args.tolerance,
            force_signal_threshold=args.force_threshold
        )
        
        metrics = analyzer.evaluate_signal_quality(df_signals, lookahead_bars=args.lookahead)
        
        print("="*80)
        print("SIGNAL QUALITY METRICS:")
        print("="*80)
        print(f"Bull Signals: {metrics['total_bull_signals']}")
        print(f"Bull Win Rate: {metrics['bull_win_rate']:.1f}%")
        print(f"Bull Avg Move: {metrics['bull_avg_move']:.2f}%")
        print()
        print(f"Bear Signals: {metrics['total_bear_signals']}")
        print(f"Bear Win Rate: {metrics['bear_win_rate']:.1f}%")
        print(f"Bear Avg Move: {metrics['bear_avg_move']:.2f}%")
        
        if args.plot:
            output_plot = f"curvature_analysis_{datetime.now().strftime('%Y%m%d_%H%M%S')}.png"
            analyzer.plot_curvature_analysis(df_signals, output_plot)


if __name__ == '__main__':
    main()
