using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.NinjaScript;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Gui;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class CorysSupertrendBands : Indicator
    {
        [NinjaScriptProperty]
        [Range(0.5, 10)]
        [Display(Name = "Sensitivity", Order = 1, GroupName = "Parameters")]
        public double Sensitivity { get; set; } = 0.8;

    [NinjaScriptProperty]
    [Display(Name = "Show Upper/Lower Bands", Order = 2, GroupName = "Visualization")]
    public bool ShowBands { get; set; } = true;

    [NinjaScriptProperty]
    [Display(Name = "Show Active Band Only", Order = 3, GroupName = "Visualization")]
    public bool ShowActiveBandOnly { get; set; } = true;

    [NinjaScriptProperty]
    [Range(1, 8)]
    [Display(Name = "SuperTrend Line Width", Order = 4, GroupName = "Visualization")]
    public int LineWidth { get; set; } = 2;

    // Strategy toggles this at runtime
    [Browsable(false)]
    public bool Enabled { get; set; } = true;

    private Series<double> st, ub, lb;
    private int lastDir; // -1 bear, +1 bull

    protected override void OnStateChange()
    {
        if (State == State.SetDefaults)
        {
            Name = "CorysSupertrendBands";
            Calculate = Calculate.OnBarClose;
            IsOverlay = true;
            IsSuspendedWhileInactive = true;

            AddPlot(Brushes.DodgerBlue, "SuperTrend");
            AddPlot(Brushes.Gray, "UpperBand");
            AddPlot(Brushes.Gray, "LowerBand");

            Plots[0].Width = LineWidth;           // ST width
            Plots[1].DashStyleHelper = DashStyleHelper.Dash;
            Plots[2].DashStyleHelper = DashStyleHelper.Dash;
            Plots[1].Opacity = 90;
            Plots[2].Opacity = 90;
        }
        else if (State == State.Configure)
        {
            // Keep width synced with parameter
            Plots[0].Width = LineWidth;
        }
        else if (State == State.DataLoaded)
        {
            st = new Series<double>(this);
            ub = new Series<double>(this);
            lb = new Series<double>(this);
            lastDir = 0;
        }
    }

    protected override void OnBarUpdate()
    {
        if (CurrentBar < 2)
        {
            InitializeFirstBars();
            PlotValues();
            return;
        }

        double barRange = High[0] - Low[0];
        if (barRange <= TickSize) barRange = TickSize;
        double rangeC = 2.0 * barRange;
        double factor = Sensitivity;

        double upperRaw = Close[0] + factor * rangeC;
        double lowerRaw = Close[0] - factor * rangeC;

        double prevLower = lb[1];
        double prevUpper = ub[1];

        // Band stickiness
        double lower = (lowerRaw > prevLower || Close[1] < prevLower) ? lowerRaw : prevLower;
        double upper = (upperRaw < prevUpper || Close[1] > prevUpper) ? upperRaw : prevUpper;

        // Direction logic (same as strategy)
        double prevSt = st[1];
        int direction;
        if (ApproximatelyEqual(prevSt, prevUpper))
            direction = Close[0] > upper ? 1 : -1;
        else
            direction = Close[0] < lower ? -1 : 1;

        double stNow = (direction == -1) ? lower : upper;

        ub[0] = upper;
        lb[0] = lower;
        st[0] = stNow;
        lastDir = direction;

        PlotValues(direction);
    }

    private void PlotValues(int? direction = null)
    {
        // Toggle visibility from strategy
        if (!Enabled)
        {
            Values[0][0] = double.NaN;
            Values[1][0] = double.NaN;
            Values[2][0] = double.NaN;
            return;
        }

        // SuperTrend main line
        Values[0][0] = st[0];
        int dir = direction ?? lastDir;
        PlotBrushes[0][0] = dir > 0 ? Brushes.LimeGreen : Brushes.Red;

        // Bands
        if (!ShowBands)
        {
            Values[1][0] = double.NaN;
            Values[2][0] = double.NaN;
        }
        else if (ShowActiveBandOnly)
        {
            // In bull, ST equals lower band; in bear, ST equals upper band
            Values[1][0] = (dir < 0) ? ub[0] : double.NaN; // show upper in bear
            Values[2][0] = (dir > 0) ? lb[0] : double.NaN; // show lower in bull
        }
        else
        {
            Values[1][0] = ub[0];
            Values[2][0] = lb[0];
        }

        // Keep ST width synced if user changes it live
        if (Plots[0].Width != LineWidth) Plots[0].Width = LineWidth;
    }

    private void InitializeFirstBars()
    {
        double barRange = High[0] - Low[0];
        if (barRange <= TickSize) barRange = TickSize;
        double rangeC = 2.0 * barRange;
        double factor = Sensitivity;

        double upperRaw = Close[0] + factor * rangeC;
        double lowerRaw = Close[0] - factor * rangeC;

        ub[0] = upperRaw;
        lb[0] = lowerRaw;
        st[0] = Close[0];
        lastDir = 0;
    }

    private bool ApproximatelyEqual(double a, double b, double eps = 1e-8)
    {
        return Math.Abs(a - b) <= eps;
    }

    // Optional: expose series
    [Browsable(false)]
    [XmlIgnore]
    public Series<double> SuperTrend => st;

    [Browsable(false)]
    [XmlIgnore]
    public Series<double> UpperBand => ub;

    [Browsable(false)]
    [XmlIgnore]
    public Series<double> LowerBand => lb;
}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private CorysSupertrendBands[] cacheCorysSupertrendBands;
		public CorysSupertrendBands CorysSupertrendBands(double sensitivity, bool showBands, bool showActiveBandOnly, int lineWidth)
		{
			return CorysSupertrendBands(Input, sensitivity, showBands, showActiveBandOnly, lineWidth);
		}

		public CorysSupertrendBands CorysSupertrendBands(ISeries<double> input, double sensitivity, bool showBands, bool showActiveBandOnly, int lineWidth)
		{
			if (cacheCorysSupertrendBands != null)
				for (int idx = 0; idx < cacheCorysSupertrendBands.Length; idx++)
					if (cacheCorysSupertrendBands[idx] != null && cacheCorysSupertrendBands[idx].Sensitivity == sensitivity && cacheCorysSupertrendBands[idx].ShowBands == showBands && cacheCorysSupertrendBands[idx].ShowActiveBandOnly == showActiveBandOnly && cacheCorysSupertrendBands[idx].LineWidth == lineWidth && cacheCorysSupertrendBands[idx].EqualsInput(input))
						return cacheCorysSupertrendBands[idx];
			return CacheIndicator<CorysSupertrendBands>(new CorysSupertrendBands(){ Sensitivity = sensitivity, ShowBands = showBands, ShowActiveBandOnly = showActiveBandOnly, LineWidth = lineWidth }, input, ref cacheCorysSupertrendBands);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.CorysSupertrendBands CorysSupertrendBands(double sensitivity, bool showBands, bool showActiveBandOnly, int lineWidth)
		{
			return indicator.CorysSupertrendBands(Input, sensitivity, showBands, showActiveBandOnly, lineWidth);
		}

		public Indicators.CorysSupertrendBands CorysSupertrendBands(ISeries<double> input , double sensitivity, bool showBands, bool showActiveBandOnly, int lineWidth)
		{
			return indicator.CorysSupertrendBands(input, sensitivity, showBands, showActiveBandOnly, lineWidth);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.CorysSupertrendBands CorysSupertrendBands(double sensitivity, bool showBands, bool showActiveBandOnly, int lineWidth)
		{
			return indicator.CorysSupertrendBands(Input, sensitivity, showBands, showActiveBandOnly, lineWidth);
		}

		public Indicators.CorysSupertrendBands CorysSupertrendBands(ISeries<double> input , double sensitivity, bool showBands, bool showActiveBandOnly, int lineWidth)
		{
			return indicator.CorysSupertrendBands(input, sensitivity, showBands, showActiveBandOnly, lineWidth);
		}
	}
}

#endregion
