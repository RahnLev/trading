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
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.Custom.Strategies
{
    public class CoryBuySell : Strategy
    {
        /* === USER PARAMETERS (same defaults as Pine) ===================== */
        [NinjaScriptProperty] public double Sensitivity   { get; set; } = 0.5;   // = “factor” in the Pine code
        [NinjaScriptProperty] public int    KelLength     { get; set; } = 8;
        [NinjaScriptProperty] public double Factor        { get; set; } = 3.5;

    /* === WORKING FIELDS ============================================= */
    private SMA   smaKel;          // Keltner centre line
    private double prevUpperBand = double.NaN;
    private double prevLowerBand = double.NaN;
    private double prevSuperTrend = double.NaN;

    protected override void OnStateChange()
    {
        if (State == State.SetDefaults)
        {
            Name               = "Cory Buy/Sell";
            Calculate          = Calculate.OnBarClose;      // exactly like Pine which works on completed bars
            IsInstantiatedOnEachOptimizationIteration = false;
        }
        else if (State == State.DataLoaded)
        {
            smaKel = SMA(Close, KelLength);                // Uses Close series like the original
        }
    }

    protected override void OnBarUpdate()
    {
        if (CurrentBar < KelLength + 2)          // wait until we have enough history
            return;

        /* === 1. KELTNER COMPONENTS ================================== */
        double rangeC        = High[0] - Low[0];            // same “rangec” definition
        double ma            = smaKel[0];
        double upperKeltner  = ma + rangeC;
        double lowerKeltner  = ma - rangeC;
        double rangeKC       = upperKeltner - lowerKeltner;

        /* === 2. MODIFIED SUPERTREND BANDS =========================== */
        double upperBand = Close[0] + Factor * rangeKC;
        double lowerBand = Close[0] - Factor * rangeKC;

        if (!double.IsNaN(prevLowerBand) &&
            !(lowerBand > prevLowerBand || Close[1] < prevLowerBand))
            lowerBand = prevLowerBand;                      // carry over previous value

        if (!double.IsNaN(prevUpperBand) &&
            !(upperBand < prevUpperBand || Close[1] > prevUpperBand))
            upperBand = prevUpperBand;

        /* === 3. SUPERTREND DIRECTION ================================ */
        int direction;
        if (double.IsNaN(prevSuperTrend))                   // very first bar
            direction = 1;
        else if (prevSuperTrend == prevUpperBand)
            direction = Close[0] > upperBand ? -1 : 1;
        else
            direction = Close[0] < lowerBand ?  1 : -1;

        double superTrend = (direction == -1) ? lowerBand : upperBand;

        /* === 4. CROSS-DETECTION  (1-bar look-back like ta.cross*) ==== */
        bool bull = (Close[1] < prevSuperTrend) && (Close[0] > superTrend);
        bool bear = (Close[1] > prevSuperTrend) && (Close[0] < superTrend);

        /* === 5. SIGNALS / ORDERS ==================================== */
        if (bull)
            EnterLong("BUY");
        if (bear)
            EnterShort("SELL");

        /* === 6. HOUSEKEEPING ======================================== */
        prevUpperBand  = upperBand;
        prevLowerBand  = lowerBand;
        prevSuperTrend = superTrend;
    }
}
}