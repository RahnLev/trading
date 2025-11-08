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

namespace NinjaTrader.NinjaScript.Strategies
{
    public class CoryBuySellV2 : Strategy
    {
        /* =========  USER–CONFIGURABLE INPUTS  ========================= */
        [NinjaScriptProperty]
        [Range(0.5, 10)]
        [Display(Name = "Sensitivity (Pine: input.float)", Order = 1)]
        public double Sensitivity   { get; set; } = 2.8;          // Pine: “sensitivity”

        [NinjaScriptProperty]
        [Display(Name = "Keltner length", Order = 2)]
        public int    KelLength     { get; set; } = 10;           // Pine: keltnerLength

        [NinjaScriptProperty]
        [Display(Name = "Factor", Order = 3)]
        public double Factor        { get; set; } = 3.5;          // Pine: factor

        [NinjaScriptProperty]
        [Display(Name = "Order quantity", Order = 4)]
        public int    Qty           { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Fixed Stop (Ticks, 0 = off)", Order = 5)]
        public int    StopTicks     { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Fixed Target (Ticks, 0 = off)", Order = 6)]
        public int    TargetTicks   { get; set; } = 0;

        /* =========  PRIVATE FIELDS  =================================== */
        private double prevUpperBand = double.NaN;
        private double prevLowerBand = double.NaN;
        private double prevSuperTrend = double.NaN;
        private int    prevDirection  =  1;

        private NinjaTrader.NinjaScript.Indicators.SMA smaKel;

        /* =========  NT LIFECYCLE  ===================================== */
        protected override void OnStateChange()
        {
			
			if (State == State.SetDefaults)
    		{
       			 AddPlot(Brushes.Orange, "SuperTrend");
    		}
			
			
            if (State == State.SetDefaults)
            {
                Name               = "Cory Buy/Sell V2 (faithful)";
                Calculate          = Calculate.OnBarClose;  // same as Pine
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.Configure)
            {
                if (StopTicks   > 0) SetStopLoss(   CalculationMode.Ticks, StopTicks);
                if (TargetTicks > 0) SetProfitTarget(CalculationMode.Ticks, TargetTicks);
            }
            else if (State == State.DataLoaded)
            {
                smaKel = SMA(Close, KelLength);
            }
        }

        /* =========  MAIN LOOP  ======================================== */
        protected override void OnBarUpdate()
        {
			 
			
            if (CurrentBar < KelLength + 2)
                return;                         // wait for enough history

            /* ----- 1.  KELTNER RANGE (identical to Pine) -------------- */
            double rangeC       = High[0] - Low[0];
            double ma           = smaKel[0];
            double upperKel     = ma + rangeC;
            double lowerKel     = ma - rangeC;
            double rangeKC      = upperKel - lowerKel;

            /* ----- 2.  BANDS ------------------------------------------ */
            double upperBand = Close[0] + Factor * rangeKC;
            double lowerBand = Close[0] - Factor * rangeKC;

            // “Stickiness” rules
            if (!double.IsNaN(prevLowerBand) &&
                !(lowerBand > prevLowerBand || Close[1] < prevLowerBand))
                lowerBand = prevLowerBand;

            if (!double.IsNaN(prevUpperBand) &&
                !(upperBand < prevUpperBand || Close[1] > prevUpperBand))
                upperBand = prevUpperBand;

            /* ----- 3.  DIRECTION & SUPERTREND ------------------------- */
            int direction;
            if (double.IsNaN(prevSuperTrend))
                direction = 1;
            else if (prevSuperTrend == prevUpperBand)
                direction = Close[0] > upperBand ? -1 : 1;
            else
                direction = Close[0] < lowerBand ? 1 : -1;

            double superTrend = direction == -1 ? lowerBand : upperBand;

			Values[0][0] = superTrend;   // <- now the line is visible
			Print(Time[1].ToString("yyyy-MM-dd HH:mm") +   $"  O={Open[1]} H={High[1]} L={Low[1]} C={Close[1]} (prev)");
			Print(Time[0].ToString("yyyy-MM-dd HH:mm") +   $"  O={Open[0]} H={High[0]} L={Low[0]} C={Close[0]} (curr)");
			
			
            /* ----- 4.  CROSS-OVER / CROSS-UNDER (exact Pine test) ----- */
            bool bull = (Close[1] <= prevSuperTrend) && (Close[0] >  superTrend);
            bool bear = (Close[1] >= prevSuperTrend) && (Close[0] <  superTrend);
			
			

            /* ----- 5.  ORDERS ----------------------------------------- */
            if (bull)
            {
                EnterLong (Qty, "BUY");
            }
            if (bear)
            {
                EnterShort(Qty, "SELL");
            }

            /* ----- 6.  SHIFT STATE FOR NEXT BAR ----------------------- */
            prevUpperBand  = upperBand;
            prevLowerBand  = lowerBand;
            prevSuperTrend = superTrend;
            prevDirection  = direction;
        }
    }
}
