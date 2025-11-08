using System;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    public class CBASTesting : Strategy
    {
        // Inputs (aligned with your Pine script)
        [NinjaScriptProperty]
        [Range(0.5, 10)]
        [Display(Name = "Sensitivity", Order = 1, GroupName = "Parameters")]
        public double Sensitivity { get; set; } = 2.8;

[NinjaScriptProperty]
[Display(Name = "EMA Energy (unused)", Order = 2, GroupName = "Parameters")]
public bool EmaEnergy { get; set; } = true;

[NinjaScriptProperty]
[Range(1, int.MaxValue)]
[Display(Name = "Keltner Length", Order = 3, GroupName = "Parameters")]
public int KeltnerLength { get; set; } = 10;

[NinjaScriptProperty]
[Range(1, int.MaxValue)]
[Display(Name = "ATR Length (parity, unused in this Supertrend)", Order = 4, GroupName = "Parameters")]
public int AtrLength { get; set; } = 10;

[NinjaScriptProperty]
[Display(Name = "Factor", Order = 5, GroupName = "Parameters")]
public double Factor { get; set; } = 3.5;

// Optional filter from your "pullback" logic
[NinjaScriptProperty]
[Display(Name = "Use Pullback Filter (Close x EMA10 and EMA10 x EMA20)", Order = 6, GroupName = "Parameters")]
public bool UsePullbackFilter { get; set; } = false;

// Internals
private Series<double> superTrend;
private Series<double> upperBandSeries;
private Series<double> lowerBandSeries;

private EMA ema10;
private EMA ema20;
private SMA smaKelt; // For completeness; your Pine used SMA(src, length) in keltner_channel, src was close.

protected override void OnStateChange()
{
    if (State == State.SetDefaults)
    {
        Name = "CBASTesting";
        Calculate = Calculate.OnBarClose;
        EntriesPerDirection = 1;
        EntryHandling = EntryHandling.AllEntries;
        IsExitOnSessionCloseStrategy = false;
        IncludeCommission = false;
        IsInstantiatedOnEachOptimizationIteration = false;
    }
    else if (State == State.Configure)
    {
        // Nothing special
    }
    else if (State == State.DataLoaded)
    {
        superTrend = new Series<double>(this);
        upperBandSeries = new Series<double>(this);
        lowerBandSeries = new Series<double>(this);

        ema10 = EMA(Close, 10);
        ema20 = EMA(Close, 20);
        smaKelt = SMA(Close, KeltnerLength);
    }
}

protected override void OnBarUpdate()
{
    if (CurrentBar < Math.Max(2, KeltnerLength))
    {
        // Initialize bands and supertrend as soon as we can
        InitializeSupertrendFirstBars();
        return;
    }

    // Keltner-like channel from your Pine:
    // ma = sma(src, length), rangec = upperK - lowerK, where upperK = ma + (high - low), lowerK = ma - (high - low)
    // => rangec = 2 * (High - Low)
    double ma = smaKelt[0]; // Used only to mirror structure; not needed for rangec
    double rangeC = 2.0 * (High[0] - Low[0]);

    // Upper/Lower raw bands based on your modified Supertrend logic
    double upperBandRaw = Close[0] + Factor * rangeC;
    double lowerBandRaw = Close[0] - Factor * rangeC;

    double prevLowerBand = lowerBandSeries[1];
    double prevUpperBand = upperBandSeries[1];

    // Band persistence rules (translated from Pine)
    double lowerBand = (lowerBandRaw > prevLowerBand || Close[1] < prevLowerBand) ? lowerBandRaw : prevLowerBand;
    double upperBand = (upperBandRaw < prevUpperBand || Close[1] > prevUpperBand) ? upperBandRaw : prevUpperBand;

    // Direction determination
    double prevSuperTrend = superTrend[1];
    int direction;

    // In Pine: if na(rangec[1]) direction := 1
    // Here we guard early bars already; treat first computed step similarly
    if (double.IsNaN(2.0 * (High[1] - Low[1])))
    {
        direction = 1;
    }
    else if (ApproximatelyEqual(prevSuperTrend, prevUpperBand))
    {
        // If previous supertrend was upper band
        direction = Close[0] > upperBand ? -1 : 1;
    }
    else
    {
        // If previous supertrend was lower band
        direction = Close[0] < lowerBand ? 1 : -1;
    }

    double superTrendNow = (direction == -1) ? lowerBand : upperBand;

    // Store series
    upperBandSeries[0] = upperBand;
    lowerBandSeries[0] = lowerBand;
    superTrend[0] = superTrendNow;

    // Signals
    bool bull = CrossAbove(Close, superTrend, 1);
    bool bear = CrossBelow(Close, superTrend, 1);

    // Optional pullback filter: crossover(close, EMA10) AND crossover(EMA10, EMA20)
    bool pullbackOk = true;
    if (UsePullbackFilter)
    {
        bool closeCrossEma10 = CrossAbove(Close, ema10, 1);
        bool ema10CrossEma20 = CrossAbove(ema10, ema20, 1);
        pullbackOk = closeCrossEma10 && ema10CrossEma20;
    }

    // Basic execution logic: one position, reverse on opposite signal
    if (bull && pullbackOk)
    {
        if (Position.MarketPosition == MarketPosition.Short)
            ExitShort();

        if (Position.MarketPosition == MarketPosition.Flat)
            EnterLong(1, "BUY");
    }
    else if (bear)
    {
        if (Position.MarketPosition == MarketPosition.Long)
            ExitLong();

        if (Position.MarketPosition == MarketPosition.Flat)
            EnterShort(1, "SELL");
    }
}

private void InitializeSupertrendFirstBars()
{
    // Provide sane initial values to avoid NaNs
    double rangeC = 2.0 * (High[0] - Low[0]);
    double upperBandRaw = Close[0] + Factor * rangeC;
    double lowerBandRaw = Close[0] - Factor * rangeC;

    upperBandSeries[0] = upperBandRaw;
    lowerBandSeries[0] = lowerBandRaw;
    superTrend[0] = Close[0];
}

private bool ApproximatelyEqual(double a, double b, double epsilon = 1e-8)
{
    return Math.Abs(a - b) <= epsilon;
}
}
}