using System;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class TicksPerMinute : Indicator
    {
        // Inputs
        [NinjaScriptProperty]
        [Display(Name = "Count Trade Ticks (true) vs Price Range Ticks (false)", GroupName = "Parameters", Order = 0)]
        public bool CountTradeTicks { get; set; } = true;

    [NinjaScriptProperty]
    [Display(Name = "Show Label On Chart", GroupName = "Parameters", Order = 1)]
    public bool ShowLabel { get; set; } = true;

    [NinjaScriptProperty]
    [Range(0, 1)]
    [Display(Name = "Label Corner (0=TopLeft, 1=TopRight)", GroupName = "Parameters", Order = 2)]
    public int LabelCorner { get; set; } = 0;

    // Internals
    private int tradeCountThisMinute;
    private int lastMinuteValue;

    protected override void OnStateChange()
    {
        if (State == State.SetDefaults)
        {
            Name = "TicksPerMinute";
            Description = "Shows ticks per minute: trade count (requires Tick Replay for historical) or price range in ticks.";
            Calculate = Calculate.OnEachTick;
            IsOverlay = false;            // separate panel
            DrawOnPricePanel = false;
            DisplayInDataBox = true;
            PaintPriceMarkers = false;
            IsSuspendedWhileInactive = true;

            AddPlot(Brushes.DodgerBlue, "TicksPerMinute");
        }
        else if (State == State.Configure)
        {
            // 1-minute series for per-minute aggregation
            AddDataSeries(BarsPeriodType.Minute, 1);
        }
        else if (State == State.DataLoaded)
        {
            tradeCountThisMinute = 0;
            lastMinuteValue = 0;
        }
    }

    protected override void OnMarketData(MarketDataEventArgs e)
    {
        if (!CountTradeTicks)
            return;

        if (e.MarketDataType == MarketDataType.Last)
            tradeCountThisMinute++;
    }

    protected override void OnBarUpdate()
    {
        // BIP 1 = our 1-minute series: finalize value for the elapsed minute
        if (BarsInProgress == 1)
        {
            if (CurrentBar < 1)
                return;

            if (CountTradeTicks)
            {
                lastMinuteValue = tradeCountThisMinute;
                tradeCountThisMinute = 0;
            }
            else
            {
                double range = Highs[1][0] - Lows[1][0];
                double ticks = range / Instrument.MasterInstrument.TickSize;
                lastMinuteValue = (int)Math.Round(ticks, MidpointRounding.AwayFromZero);
            }
            return;
        }

        // BIP 0 = primary series: plot latest minute's value
        if (BarsInProgress == 0)
        {
            Values[0][0] = lastMinuteValue;

            if (ShowLabel)
            {
                var corner = LabelCorner == 0 ? TextPosition.TopLeft : TextPosition.TopRight;
                // Use the simple 4-arg overload compatible with your NT build
                Draw.TextFixed(this, "TPM_LABEL", CountTradeTicks
                    ? $"Trade ticks/min: {lastMinuteValue}"
                    : $"Price ticks/min: {lastMinuteValue}",
                    corner);
            }
        }
    }
}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private TicksPerMinute[] cacheTicksPerMinute;
		public TicksPerMinute TicksPerMinute(bool countTradeTicks, bool showLabel, int labelCorner)
		{
			return TicksPerMinute(Input, countTradeTicks, showLabel, labelCorner);
		}

		public TicksPerMinute TicksPerMinute(ISeries<double> input, bool countTradeTicks, bool showLabel, int labelCorner)
		{
			if (cacheTicksPerMinute != null)
				for (int idx = 0; idx < cacheTicksPerMinute.Length; idx++)
					if (cacheTicksPerMinute[idx] != null && cacheTicksPerMinute[idx].CountTradeTicks == countTradeTicks && cacheTicksPerMinute[idx].ShowLabel == showLabel && cacheTicksPerMinute[idx].LabelCorner == labelCorner && cacheTicksPerMinute[idx].EqualsInput(input))
						return cacheTicksPerMinute[idx];
			return CacheIndicator<TicksPerMinute>(new TicksPerMinute(){ CountTradeTicks = countTradeTicks, ShowLabel = showLabel, LabelCorner = labelCorner }, input, ref cacheTicksPerMinute);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TicksPerMinute TicksPerMinute(bool countTradeTicks, bool showLabel, int labelCorner)
		{
			return indicator.TicksPerMinute(Input, countTradeTicks, showLabel, labelCorner);
		}

		public Indicators.TicksPerMinute TicksPerMinute(ISeries<double> input , bool countTradeTicks, bool showLabel, int labelCorner)
		{
			return indicator.TicksPerMinute(input, countTradeTicks, showLabel, labelCorner);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TicksPerMinute TicksPerMinute(bool countTradeTicks, bool showLabel, int labelCorner)
		{
			return indicator.TicksPerMinute(Input, countTradeTicks, showLabel, labelCorner);
		}

		public Indicators.TicksPerMinute TicksPerMinute(ISeries<double> input , bool countTradeTicks, bool showLabel, int labelCorner)
		{
			return indicator.TicksPerMinute(input, countTradeTicks, showLabel, labelCorner);
		}
	}
}

#endregion
