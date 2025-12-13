// Shows seconds remaining (or elapsed) in the current time-based bar.
// Mirrors the TickCounter indicator styling and options.

#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators
{
    public class SecondsLeftOnBar : Indicator
    {
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description         = "Displays seconds remaining (or elapsed) in the current bar.";
                Name                = "SecondsLeftOnBar";
                Calculate           = Calculate.OnEachTick;
                CountDown           = true;
                DisplayInDataBox    = false;
                DrawOnPricePanel    = false;
                IsChartOnly         = true;
                IsOverlay           = true;
                ShowPercent         = false;
                TextPositionFine    = TextPositionFine.BottomRight;
            }
        }

        protected override void OnBarUpdate()
        {
            double periodSeconds = GetPeriodSeconds();
            if (double.IsNaN(periodSeconds) || periodSeconds <= 0)
            {
                Draw.TextFixedFine(this, "SecondsLeftOnBarInfo", "Bar type not time-based", TextPositionFine, ChartControl.Properties.ChartText, ChartControl.Properties.LabelFont, Brushes.Transparent, Brushes.Transparent, 0);
                return;
            }

            double portion = CountDown ? 1 - Bars.PercentComplete : Bars.PercentComplete;
            portion = Math.Min(Math.Max(portion, 0), 1);

            double secondsValue = ShowPercent
                ? portion
                : periodSeconds * portion;

            string secondsMsg = ShowPercent
                ? secondsValue.ToString("P0")
                : secondsValue.ToString("F0", CultureInfo.InvariantCulture);

            string label = CountDown
                ? $"Seconds remaining: {secondsMsg}"
                : $"Seconds elapsed: {secondsMsg}";

            Draw.TextFixedFine(this, "SecondsLeftOnBarInfo", label, TextPositionFine, ChartControl.Properties.ChartText, ChartControl.Properties.LabelFont, Brushes.Transparent, Brushes.Transparent, 0);
        }

        private double GetPeriodSeconds()
        {
            switch (BarsPeriod.BarsPeriodType)
            {
                case BarsPeriodType.Second:
                    return BarsPeriod.Value;
                case BarsPeriodType.Minute:
                    return BarsPeriod.Value * 60.0;
                case BarsPeriodType.Day:
                    return BarsPeriod.Value * 60.0 * 24.0;
                case BarsPeriodType.Week:
                    return BarsPeriod.Value * 60.0 * 24.0 * 7.0;
                case BarsPeriodType.Month:
                    // Approximate a month as 30 days for countdown display
                    return BarsPeriod.Value * 60.0 * 24.0 * 30.0;
                case BarsPeriodType.Year:
                    // Approximate a year as 365 days for countdown display
                    return BarsPeriod.Value * 60.0 * 24.0 * 365.0;
                default:
                    return double.NaN;
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "CountDown", Order = 1, GroupName = "NinjaScriptParameters")]
        public bool CountDown { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ShowPercent", Order = 2, GroupName = "NinjaScriptParameters")]
        public bool ShowPercent { get; set; }

        [Display(Name = "Text Position", GroupName = "PropertyCategoryVisual", Order = 70)]
        public TextPositionFine TextPositionFine { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private SecondsLeftOnBar[] cacheSecondsLeftOnBar;
		public SecondsLeftOnBar SecondsLeftOnBar(bool countDown, bool showPercent)
		{
			return SecondsLeftOnBar(Input, countDown, showPercent);
		}

		public SecondsLeftOnBar SecondsLeftOnBar(ISeries<double> input, bool countDown, bool showPercent)
		{
			if (cacheSecondsLeftOnBar != null)
				for (int idx = 0; idx < cacheSecondsLeftOnBar.Length; idx++)
					if (cacheSecondsLeftOnBar[idx] != null && cacheSecondsLeftOnBar[idx].CountDown == countDown && cacheSecondsLeftOnBar[idx].ShowPercent == showPercent && cacheSecondsLeftOnBar[idx].EqualsInput(input))
						return cacheSecondsLeftOnBar[idx];
			return CacheIndicator<SecondsLeftOnBar>(new SecondsLeftOnBar(){ CountDown = countDown, ShowPercent = showPercent }, input, ref cacheSecondsLeftOnBar);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.SecondsLeftOnBar SecondsLeftOnBar(bool countDown, bool showPercent)
		{
			return indicator.SecondsLeftOnBar(Input, countDown, showPercent);
		}

		public Indicators.SecondsLeftOnBar SecondsLeftOnBar(ISeries<double> input , bool countDown, bool showPercent)
		{
			return indicator.SecondsLeftOnBar(input, countDown, showPercent);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.SecondsLeftOnBar SecondsLeftOnBar(bool countDown, bool showPercent)
		{
			return indicator.SecondsLeftOnBar(Input, countDown, showPercent);
		}

		public Indicators.SecondsLeftOnBar SecondsLeftOnBar(ISeries<double> input , bool countDown, bool showPercent)
		{
			return indicator.SecondsLeftOnBar(input, countDown, showPercent);
		}
	}
}

#endregion
