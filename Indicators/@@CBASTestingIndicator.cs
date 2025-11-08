using System;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class CBASTestingIndicator : Indicator
    {
        // Inputs matching Pine
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

    // Present for UI parity; not used in ST math
    [NinjaScriptProperty]
    [Range(1, int.MaxValue)]
    [Display(Name = "ATR Length (unused in ST)", Order = 4, GroupName = "Parameters")]
    public int AtrLength { get; set; } = 10;

    // Range detector inputs
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

    // Internals
    private Series<double> superTrend;
    private Series<double> upperBandSeries;
    private Series<double> lowerBandSeries;

    // EMA Energy
    private readonly int[] energyPeriods = new int[] { 9, 12, 15, 18, 21, 24, 27, 30, 33, 36, 39, 42, 45, 48, 51 };
    private EMA[] emaHighs;

    // Signals and helpers
    private EMA ema10Close;
    private EMA ema20Close;
    private ATR atr30;

    // Range detector internals
    private ATR rangeATR;
    private SMA rangeSMA;
    private Series<int> rangeCountSeries;
    private int os = 0; // 0 unbroken, 1 up, -1 down
    private double rangeMax = double.NaN;
    private double rangeMin = double.NaN;

    // Plot indices
    private int plotSuperTrend;
    private int plotUpperBand;
    private int plotLowerBand;

    private int[] energyPlotIdx;     // 15 energy plots
    private int plotBull;            // triangle up signals for bull
    private int plotBear;            // triangle down signals for bear
    private int plotPullback;        // triangle up for pullback
    private int plotRangeTop;        // range top
    private int plotRangeBottom;     // range bottom
    private int plotRangeMid;        // range midline

    protected override void OnStateChange()
    {
        if (State == State.SetDefaults)
        {
            Name = "CBASTestingIndicator";
            IsOverlay = true;
            Calculate = Calculate.OnBarClose;
            DrawOnPricePanel = true;
            IsSuspendedWhileInactive = true;

            // Core plots
            AddPlot(Brushes.DarkGray, "SuperTrend");
            AddPlot(Brushes.Transparent, "UpperBand");
            AddPlot(Brushes.Transparent, "LowerBand");

            // 15 EMA energy plots (we’ll color dynamically)
            for (int i = 0; i < 15; i++)
                AddPlot(Brushes.Transparent, $"EnergyEMA{i + 1}");

            // Signal plots
            AddPlot(Brushes.Teal, "BullSignal");     // triangle up
            AddPlot(Brushes.Red, "BearSignal");      // triangle down
            AddPlot(Brushes.LimeGreen, "Pullback");  // triangle up

            // Range detector plots
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

            // Plot indices
            plotSuperTrend = 0;
            plotUpperBand = 1;
            plotLowerBand = 2;

            energyPlotIdx = new int[15];
            for (int i = 0; i < 15; i++)
                energyPlotIdx[i] = 3 + i;

            plotBull = 18;      // after 3 + 15 plots
            plotBear = 19;
            plotPullback = 20;

            plotRangeTop = 21;
            plotRangeBottom = 22;
            plotRangeMid = 23;

            // Set styles on the signal plots
            Plots[plotBull].PlotStyle = PlotStyle.TriangleUp;
            Plots[plotBear].PlotStyle = PlotStyle.TriangleDown;
            Plots[plotPullback].PlotStyle = PlotStyle.TriangleUp;
        }
    }

    protected override void OnBarUpdate()
    {
        if (CurrentBar < Math.Max(2, KeltnerLength))
        {
            InitializeFirstBars();
            return;
        }

        // Keltner-like range (upper-lower) = 2*(High-Low) per Pine
        double rangeC = 2.0 * (High[0] - Low[0]);

        // Bands per Pine
        double upperBandRaw = Close[0] + Sensitivity * rangeC;
        double lowerBandRaw = Close[0] - Sensitivity * rangeC;

        double prevLowerBand = lowerBandSeries[1];
        double prevUpperBand = upperBandSeries[1];

        // Carry-forward rules
        double lowerBand = (lowerBandRaw > prevLowerBand || Close[1] < prevLowerBand) ? lowerBandRaw : prevLowerBand;
        double upperBand = (upperBandRaw < prevUpperBand || Close[1] > prevUpperBand) ? upperBandRaw : prevUpperBand;

        // Direction per Pine
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

        // Pine mapping: direction == -1 ? lowerBand : upperBand
        double superTrendNow = (direction == -1) ? lowerBand : upperBand;

        // Store series
        upperBandSeries[0] = upperBand;
        lowerBandSeries[0] = lowerBand;
        superTrend[0] = superTrendNow;

        // Set plots
        Values[plotSuperTrend][0] = superTrendNow;
        Values[plotUpperBand][0] = upperBand;
        Values[plotLowerBand][0] = lowerBand;

        // Bull/Bear signals
        bool bull = CrossAbove(Close, superTrend, 1);
        bool bear = CrossBelow(Close, superTrend, 1);

        // Reset signal plots
        Values[plotBull][0] = double.NaN;
        Values[plotBear][0] = double.NaN;
        Values[plotPullback][0] = double.NaN;

        if (bull)
        {
            double y1 = Low[0] - atr30[0] * 2.0;
            Values[plotBull][0] = y1; // triangle up below bar
        }
        if (bear)
        {
            double y2 = High[0] + atr30[0] * 2.0;
            Values[plotBear][0] = y2; // triangle down above bar
        }

        // Bar coloring by supertrend[1]
        if (CurrentBar > 0)
            BarBrushes[0] = Close[0] > superTrend[1] ? Brushes.Teal : Brushes.Red;

        // EMA Energy ribbons (EMAs on High; color by Close >= EMA)
        for (int i = 0; i < emaHighs.Length; i++)
        {
            double v = emaHighs[i][0];
            int pIdx = energyPlotIdx[i];
            Values[pIdx][0] = v;

            PlotBrushes[pIdx][0] = EmaEnergy
                ? (Close[0] >= v ? Brushes.Teal : Brushes.Red)
                : Brushes.Transparent;
        }

        // Pullback triangles (same bar: crossover(close, EMA10) AND crossover(EMA10, EMA20))
        bool pullback = CrossAbove(Close, ema10Close, 1) && CrossAbove(ema10Close, ema20Close, 1);
        if (pullback)
        {
            double yPB = Low[0] - 2 * TickSize;
            Values[plotPullback][0] = yPB;
        }

        // Range detector (plots only)
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

        // Initialize signal and range plots to NaN
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

        // Start new range when condition first becomes true
        if (count == 0 && (CurrentBar == 0 || rangeCountSeries[1] != 0))
        {
            rangeMax = ma + atr;
            rangeMin = ma - atr;
            os = 0;
        }

        // Breakout state
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

        // Color by state
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

