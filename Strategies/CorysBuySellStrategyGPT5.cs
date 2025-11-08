#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion



namespace NinjaTrader.NinjaScript.Strategies
{
    public class CorysBuySellStrategyGPT5 : Strategy
    {
        [Range(0.5, 10)]
        [Display(Name = "Sensitivity (Factor)", Order = 0)]
        public double Sensitivity { get; set; } = 2.8;

    [Display(Name = "EMA Energy", Order = 1)]
    public bool EmaEnergy { get; set; } = true;

    [Range(2, 200)]
    [Display(Name = "Keltner Channel Length", Order = 2)]
    public int KeltnerLength { get; set; } = 10;

    [Range(2, 200)]
    [Display(Name = "ATR Length", Order = 3)]
    public int AtrLength { get; set; } = 10;

    [Range(0.1, 10.0)]
    [Display(Name = "Factor", Order = 4)]
    public double Factor { get; set; } = 3.5;

    private Series<double> superTrendSeries;
    private double prevUpperBand = double.NaN;
    private double prevLowerBand = double.NaN;
    private double prevSuperTrend = double.NaN;

    protected override void OnStateChange()
    {
        if (State == State.SetDefaults)
        {
            Description = "Corys Buy and Sell (Ported to NinjaTrader 8).";
            Name = "CorysBuySellStrategyGPT5";
            Calculate = Calculate.OnBarClose;
            IsOverlay = true;
            EntriesPerDirection = 1;
        }
        else if (State == State.DataLoaded)
        {
            superTrendSeries = new Series<double>(this);
        }
    }

    protected override void OnBarUpdate()
    {
        // Need enough bars
        if (CurrentBar < Math.Max(2, KeltnerLength))
            return;

        // 1) Keltner center and range
        double ma = SMA(Close, KeltnerLength)[0];      // center (SMA)
        double range = High[0] - Low[0];

        // 2) Raw bands
        double upperRaw = Close[0] + Factor * range;
        double lowerRaw = Close[0] - Factor * range;

        // 3) Smoothed bands (as in Pine code)
        double upperBand = upperRaw;
        double lowerBand = lowerRaw;

        if (CurrentBar > 1 && !double.IsNaN(prevLowerBand) && !double.IsNaN(prevUpperBand))
        {
            lowerBand = (lowerRaw > prevLowerBand || Close[1] < prevLowerBand) ? lowerRaw : prevLowerBand;
            upperBand = (upperRaw < prevUpperBand || Close[1] > prevUpperBand) ? upperRaw : prevUpperBand;
        }

        // 4) Direction logic
        double direction;
        if (double.IsNaN(range) || double.IsNaN(prevLowerBand) || double.IsNaN(prevUpperBand) || double.IsNaN(prevSuperTrend))
        {
            direction = 1;
        }
        else if (prevSuperTrend == prevUpperBand)
        {
            direction = Close[0] > upperBand ? -1 : 1;
        }
        else
        {
            direction = Close[0] < lowerBand ? 1 : -1;
        }

        double superTrendVal = (direction == -1) ? lowerBand : upperBand;

        // Persist for next bar
        prevLowerBand = lowerBand;
        prevUpperBand = upperBand;
        prevSuperTrend = superTrendVal;

        // 5) Signals
        superTrendSeries[0] = superTrendVal;
        bool bull = CrossAbove(Close, superTrendSeries, 1);
        bool bear = CrossBelow(Close, superTrendSeries, 1);

        // 6) Entries
        if (bull && Position.MarketPosition != MarketPosition.Long)
            EnterLong();
        else if (bear && Position.MarketPosition != MarketPosition.Short)
            EnterShort();
    }
}
}