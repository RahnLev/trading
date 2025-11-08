using System;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;


namespace NinjaTrader.NinjaScript.Indicators
{
    public class CBASTestingIndicator2 : Indicator
    {
        [NinjaScriptProperty]
        [Range(0.5, 10)]
        [Display(Name = "Sensitivity", Order = 1, GroupName = "Parameters")]
        public double Sensitivity { get; set; } = 2.8;

[NinjaScriptProperty]
[Display(Name = "EMA Energy", Order = 2, GroupName = "Parameters")]
public bool EmaEnergy { get; set; } = true;

[NinjaScriptProperty]
[Range(1, int.MaxValue)]
[Display(Name = "Keltner Length", Order = 3, GroupName = "Parameters")]
public int KeltnerLength { get; set; } = 10;

[NinjaScriptProperty]
[Range(1, int.MaxValue)]
[Display(Name = "ATR Length (unused in ST)", Order = 4, GroupName = "Parameters")]
public int AtrLength { get; set; } = 10;

[NinjaScriptProperty]
[Range(2, int.MaxValue)]
[Display(Name = "Range Min Length", Order = 10, GroupName = "Range Detector")]
public int RangeMinLength { get; set; } = 20;

[NinjaScriptProperty]
[Range(0.0, double.MaxValue)]
[Display(Name = "Range Width Mult", Order = 11, GroupName = "Range Detector")]
public double RangeWidthMult { get; set; } = 1.0;

[NinjaScriptProperty]
[Range(1, int.MaxValue)]
[Display(Name = "Range ATR Length", Order = 12, GroupName = "Range Detector")]
public int RangeAtrLen { get; set; } = 500;

private Series<double> superTrend;
private Series<double> upperBandSeries;
private Series<double> lowerBandSeries;

private readonly int[] energyPeriods = new int[] { 9, 12, 15, 18, 21, 24, 27, 30, 33, 36, 39, 42, 45, 48, 51 };
private EMA[] emaHighs;

private EMA ema10Close;
private EMA ema20Close;
private ATR atr30;

private ATR rangeATR;
private SMA rangeSMA;
private Series<int> rangeCountSeries;
private int os = 0;
private double rangeMax = double.NaN;
private double rangeMin = double.NaN;

private int plotSuperTrend;
private int plotUpperBand;
private int plotLowerBand;

private int[] energyPlotIdx;
private int plotRangeTop;
private int plotRangeBottom;
private int plotRangeMid;

protected override void OnStateChange()
{
    if (State == State.SetDefaults)
    {
        Name = "CBASTestingIndicator2";
        IsOverlay = true;
        Calculate = Calculate.OnEachTick;
        DrawOnPricePanel = true;
        IsSuspendedWhileInactive = true;

        AddPlot(Brushes.DarkGray, "SuperTrend");
        AddPlot(Brushes.Transparent, "UpperBand");
        AddPlot(Brushes.Transparent, "LowerBand");

        for (int i = 0; i < 15; i++)
            AddPlot(Brushes.Transparent, $"EnergyEMA{i + 1}");

        AddPlot(Brushes.DodgerBlue, "RangeTop");
        AddPlot(Brushes.DodgerBlue, "RangeBottom");
        AddPlot(Brushes.DodgerBlue, "RangeMid");
    }
    else if (State == State.DataLoaded)
    {
        superTrend = new Series<double>(this);
        upperBandSeries = new Series<double>(this);
        lowerBandSeries = new Series<double>(this);

        ema10Close = EMA(Close, 10);
        ema20Close = EMA(Close, 20);
        atr30 = ATR(30);

        rangeATR = ATR(RangeAtrLen);
        rangeSMA = SMA(Close, RangeMinLength);
        rangeCountSeries = new Series<int>(this);

        emaHighs = new EMA[energyPeriods.Length];
        for (int i = 0; i < energyPeriods.Length; i++)
            emaHighs[i] = EMA(High, energyPeriods[i]);

        plotSuperTrend = 0;
        plotUpperBand = 1;
        plotLowerBand = 2;

        energyPlotIdx = new int[15];
        for (int i = 0; i < 15; i++)
            energyPlotIdx[i] = 3 + i;

        plotRangeTop = 18;
        plotRangeBottom = 19;
        plotRangeMid = 20;
    }
}

protected override void OnBarUpdate()
{
    if (CurrentBar < Math.Max(2, KeltnerLength))
    {
        InitializeFirstBars();
        return;
    }

    double rangeC = 2.0 * (High[0] - Low[0]);

    double upperBandRaw = Close[0] + Sensitivity * rangeC;
    double lowerBandRaw = Close[0] - Sensitivity * rangeC;

    double prevLowerBand = lowerBandSeries[1];
    double prevUpperBand = upperBandSeries[1];

    double lowerBand = (lowerBandRaw > prevLowerBand || Close[1] < prevLowerBand) ? lowerBandRaw : prevLowerBand;
    double upperBand = (upperBandRaw < prevUpperBand || Close[1] > prevUpperBand) ? upperBandRaw : prevUpperBand;

    double prevSuperTrend = superTrend[1];
    int direction;
    if (CurrentBar == 0)
        direction = 1;
    else if (double.IsNaN(2.0 * (High[1] - Low[1])))
        direction = 1;
    else if (ApproximatelyEqual(prevSuperTrend, prevUpperBand))
        direction = Close[0] > upperBand ? -1 : 1;
    else
        direction = Close[0] < lowerBand ? 1 : -1;

    double superTrendNow = (direction == -1) ? lowerBand : upperBand;

    upperBandSeries[0] = upperBand;
    lowerBandSeries[0] = lowerBand;
    superTrend[0] = superTrendNow;

    Values[plotSuperTrend][0] = superTrendNow;
    Values[plotUpperBand][0] = upperBand;
    Values[plotLowerBand][0] = lowerBand;

    bool bull = CrossAbove(Close, superTrend, 1);
    bool bear = CrossBelow(Close, superTrend, 1);

    if (bull)
    {
        double y1 = Low[0] - atr30[0] * 2.0;
        Draw.TriangleUp(this, $"Buy_{CurrentBar}", false, 0, y1, Brushes.LimeGreen);
    }
    if (bear)
    {
        double y2 = High[0] + atr30[0] * 2.0;
        Draw.TriangleDown(this, $"Sell_{CurrentBar}", false, 0, y2, Brushes.Red);
    }

    if (CurrentBar > 0)
        BarBrushes[0] = Close[0] > superTrend[1] ? Brushes.Teal : Brushes.Red;

    for (int i = 0; i < emaHighs.Length; i++)
    {
        double v = emaHighs[i][0];
        int pIdx = energyPlotIdx[i];
        Values[pIdx][0] = v;

        PlotBrushes[pIdx][0] = EmaEnergy
            ? (Close[0] >= v ? Brushes.Teal : Brushes.Red)
            : Brushes.Transparent;
    }

    UpdateRangeDetectorPlots();
}

private void InitializeFirstBars()
{
    double rangeC = 2.0 * (High[0] - Low[0]);
    double upperBandRaw = Close[0] + Sensitivity * rangeC;
    double lowerBandRaw = Close[0] - Sensitivity * rangeC;

    upperBandSeries[0] = upperBandRaw;
    lowerBandSeries[0] = lowerBandRaw;
    superTrend[0] = Close[0];

    Values[plotUpperBand][0] = upperBandRaw;
    Values[plotLowerBand][0] = lowerBandRaw;
    Values[plotSuperTrend][0] = Close[0];

    if (Plots != null)
    {
        for (int i = 3; i < Plots.Length; i++)
            Values[i][0] = double.NaN;
    }
}

private void UpdateRangeDetectorPlots()
{
    if (CurrentBar < RangeMinLength)
    {
        Values[plotRangeTop][0] = double.NaN;
        Values[plotRangeBottom][0] = double.NaN;
        Values[plotRangeMid][0] = double.NaN;
        return;
    }

    double atr = rangeATR[0] * RangeWidthMult;
    double ma = rangeSMA[0];

    int count = 0;
    for (int i = 0; i < RangeMinLength; i++)
        count += Math.Abs(Close[i] - ma) > atr ? 1 : 0;
    rangeCountSeries[0] = count;

    if (count == 0 && (CurrentBar == 0 || rangeCountSeries[1] != 0))
    {
        rangeMax = ma + atr;
        rangeMin = ma - atr;
        os = 0;
    }

    if (!double.IsNaN(rangeMax) && Close[0] > rangeMax)
        os = 1;
    else if (!double.IsNaN(rangeMin) && Close[0] < rangeMin)
        os = -1;

    if (double.IsNaN(rangeMax) || double.IsNaN(rangeMin))
    {
        Values[plotRangeTop][0] = double.NaN;
        Values[plotRangeBottom][0] = double.NaN;
        Values[plotRangeMid][0] = double.NaN;
        return;
    }

    Values[plotRangeTop][0] = rangeMax;
    Values[plotRangeBottom][0] = rangeMin;
    Values[plotRangeMid][0] = (rangeMax + rangeMin) / 2.0;

    Brush css = os == 0 ? Brushes.DodgerBlue : os == 1 ? Brushes.LimeGreen : Brushes.IndianRed;
    PlotBrushes[plotRangeTop][0] = css;
    PlotBrushes[plotRangeBottom][0] = css;
    PlotBrushes[plotRangeMid][0] = css;
}

private bool ApproximatelyEqual(double a, double b, double epsilon = 1e-8)
{
    return Math.Abs(a - b) <= epsilon;
}
}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private CBASTestingIndicator2[] cacheCBASTestingIndicator2;
		public CBASTestingIndicator2 CBASTestingIndicator2(double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen)
		{
			return CBASTestingIndicator2(Input, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen);
		}

		public CBASTestingIndicator2 CBASTestingIndicator2(ISeries<double> input, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen)
		{
			if (cacheCBASTestingIndicator2 != null)
				for (int idx = 0; idx < cacheCBASTestingIndicator2.Length; idx++)
					if (cacheCBASTestingIndicator2[idx] != null && cacheCBASTestingIndicator2[idx].Sensitivity == sensitivity && cacheCBASTestingIndicator2[idx].EmaEnergy == emaEnergy && cacheCBASTestingIndicator2[idx].KeltnerLength == keltnerLength && cacheCBASTestingIndicator2[idx].AtrLength == atrLength && cacheCBASTestingIndicator2[idx].RangeMinLength == rangeMinLength && cacheCBASTestingIndicator2[idx].RangeWidthMult == rangeWidthMult && cacheCBASTestingIndicator2[idx].RangeAtrLen == rangeAtrLen && cacheCBASTestingIndicator2[idx].EqualsInput(input))
						return cacheCBASTestingIndicator2[idx];
			return CacheIndicator<CBASTestingIndicator2>(new CBASTestingIndicator2(){ Sensitivity = sensitivity, EmaEnergy = emaEnergy, KeltnerLength = keltnerLength, AtrLength = atrLength, RangeMinLength = rangeMinLength, RangeWidthMult = rangeWidthMult, RangeAtrLen = rangeAtrLen }, input, ref cacheCBASTestingIndicator2);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.CBASTestingIndicator2 CBASTestingIndicator2(double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen)
		{
			return indicator.CBASTestingIndicator2(Input, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen);
		}

		public Indicators.CBASTestingIndicator2 CBASTestingIndicator2(ISeries<double> input , double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen)
		{
			return indicator.CBASTestingIndicator2(input, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.CBASTestingIndicator2 CBASTestingIndicator2(double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen)
		{
			return indicator.CBASTestingIndicator2(Input, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen);
		}

		public Indicators.CBASTestingIndicator2 CBASTestingIndicator2(ISeries<double> input , double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen)
		{
			return indicator.CBASTestingIndicator2(input, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen);
		}
	}
}

#endregion
