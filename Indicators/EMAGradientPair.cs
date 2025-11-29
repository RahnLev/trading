using System;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;

// Simple indicator to visualize EMA gradients (bar-to-bar change) for fast and slow EMAs
namespace NinjaTrader.NinjaScript.Indicators
{
    public class EMAGradientPair : Indicator
    {
        private EMA fast;
        private EMA slow;

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Fast EMA Period", Order = 1, GroupName = "Parameters")]
        public int FastPeriod { get; set; } = 10;

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Slow EMA Period", Order = 2, GroupName = "Parameters")]
        public int SlowPeriod { get; set; } = 20;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "EMAGradientPair";
                Description = "Plots bar-to-bar gradients of fast/slow EMAs in a separate panel.";
                IsOverlay = false;
                Calculate = Calculate.OnPriceChange;
                IsSuspendedWhileInactive = true;
                AddPlot(Brushes.DodgerBlue, "FastGrad");
                AddPlot(Brushes.OrangeRed, "SlowGrad");
                AddLine(Brushes.Gray, 0, "Zero");
            }
            else if (State == State.Configure)
            {
                fast = EMA(Input, FastPeriod);
                slow = EMA(Input, SlowPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
            {
                Values[0][0] = 0;
                Values[1][0] = 0;
                return;
            }
            double fastGrad = fast[0] - fast[1];
            double slowGrad = slow[0] - slow[1];
            Values[0][0] = fastGrad;
            Values[1][0] = slowGrad;
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private EMAGradientPair[] cacheEMAGradientPair;
		public EMAGradientPair EMAGradientPair(int fastPeriod, int slowPeriod)
		{
			return EMAGradientPair(Input, fastPeriod, slowPeriod);
		}

		public EMAGradientPair EMAGradientPair(ISeries<double> input, int fastPeriod, int slowPeriod)
		{
			if (cacheEMAGradientPair != null)
				for (int idx = 0; idx < cacheEMAGradientPair.Length; idx++)
					if (cacheEMAGradientPair[idx] != null && cacheEMAGradientPair[idx].FastPeriod == fastPeriod && cacheEMAGradientPair[idx].SlowPeriod == slowPeriod && cacheEMAGradientPair[idx].EqualsInput(input))
						return cacheEMAGradientPair[idx];
			return CacheIndicator<EMAGradientPair>(new EMAGradientPair(){ FastPeriod = fastPeriod, SlowPeriod = slowPeriod }, input, ref cacheEMAGradientPair);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.EMAGradientPair EMAGradientPair(int fastPeriod, int slowPeriod)
		{
			return indicator.EMAGradientPair(Input, fastPeriod, slowPeriod);
		}

		public Indicators.EMAGradientPair EMAGradientPair(ISeries<double> input , int fastPeriod, int slowPeriod)
		{
			return indicator.EMAGradientPair(input, fastPeriod, slowPeriod);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.EMAGradientPair EMAGradientPair(int fastPeriod, int slowPeriod)
		{
			return indicator.EMAGradientPair(Input, fastPeriod, slowPeriod);
		}

		public Indicators.EMAGradientPair EMAGradientPair(ISeries<double> input , int fastPeriod, int slowPeriod)
		{
			return indicator.EMAGradientPair(input, fastPeriod, slowPeriod);
		}
	}
}

#endregion
