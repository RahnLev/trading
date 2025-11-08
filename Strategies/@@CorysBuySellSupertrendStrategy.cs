using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;

namespace NinjaTrader.NinjaScript.Strategies
{
    public enum OrientationMode
    {
        Inverted = 0,   // Matches your original: more trade flips
        Classic = 1     // Textbook: bull=lower band, bear=upper band
    }

public enum SignalMode
{
    CrossClose = 0,     // Close crosses ST
    CrossHighLow = 1,   // High/Low crosses ST (more signals)
    DirectionFlip = 2   // When ST direction flips
}

public class CorysBuySellSupertrendStrategy : Strategy
{
    // Parameters
    [NinjaScriptProperty]
    [Range(0.5, 10)]
    [Display(Name = "Sensitivity (Supertrend factor)", Order = 1, GroupName = "Parameters")]
    public double Sensitivity { get; set; } = 0.8;

    [NinjaScriptProperty]
    [Display(Name = "EMA Energy (unused)", Order = 2, GroupName = "Parameters")]
    public bool EmaEnergy { get; set; } = true;

    [NinjaScriptProperty]
    [Range(1, int.MaxValue)]
    [Display(Name = "Keltner Length (unused)", Order = 3, GroupName = "Parameters")]
    public int KeltnerLength { get; set; } = 10;

    [NinjaScriptProperty]
    [Range(1, int.MaxValue)]
    [Display(Name = "ATR Length (unused)", Order = 4, GroupName = "Parameters")]
    public int AtrLength { get; set; } = 10;

    [NinjaScriptProperty]
    [Display(Name = "Factor (unused)", Order = 5, GroupName = "Parameters")]
    public double Factor { get; set; } = 3.5;

    [NinjaScriptProperty]
    [Display(Name = "Use Pullback Filter (Close>EMA10 and EMA10>EMA20 cross on same bar)", Order = 6, GroupName = "Parameters")]
    public bool UsePullbackFilter { get; set; } = false;

    [NinjaScriptProperty]
    [Display(Name = "Orientation", Order = 7, GroupName = "Signals")]
    public OrientationMode Orientation { get; set; } = OrientationMode.Inverted; // default to match your original

    [NinjaScriptProperty]
    [Display(Name = "Signal Generation", Order = 8, GroupName = "Signals")]
    public SignalMode SignalGeneration { get; set; } = SignalMode.CrossClose;

    [NinjaScriptProperty]
    [Range(1, 10)]
    [Display(Name = "Cross Lookback (bars)", Order = 9, GroupName = "Signals")]
    public int CrossLookback { get; set; } = 1;

    // Risk
    [NinjaScriptProperty]
    [Display(Name = "Use Stops/Targets", Order = 10, GroupName = "Risk")]
    public bool UseStops { get; set; } = true;

    [NinjaScriptProperty]
    [Range(1, int.MaxValue)]
    [Display(Name = "Stop Loss (Ticks)", Order = 11, GroupName = "Risk")]
    public int StopLossTicks { get; set; } = 40;

    [NinjaScriptProperty]
    [Range(0, int.MaxValue)]
    [Display(Name = "Profit Target (Ticks, 0 = disabled)", Order = 12, GroupName = "Risk")]
    public int ProfitTargetTicks { get; set; } = 0;

    [NinjaScriptProperty]
    [Display(Name = "Use Trailing Stop", Order = 20, GroupName = "Risk")]
    public bool UseTrailingStop { get; set; } = true;

    [NinjaScriptProperty]
    [Range(1, int.MaxValue)]
    [Display(Name = "Trailing Stop (Ticks)", Order = 21, GroupName = "Risk")]
    public int TrailingStopTicks { get; set; } = 100;

    // Diagnostics
    [NinjaScriptProperty]
    [Display(Name = "Debug Prints", Order = 30, GroupName = "Diagnostics")]
    public bool DebugPrints { get; set; } = true;

    // Internals
    private Series<double> superTrend;
    private Series<double> upperBandSeries;
    private Series<double> lowerBandSeries;
    private Series<int> dirSeries;

    private EMA ema10;
    private EMA ema20;

    protected override void OnStateChange()
    {
        if (State == State.SetDefaults)
        {
            Name = "CorysBuySellSupertrendStrategy";
            Calculate = Calculate.OnBarClose;
            EntriesPerDirection = 1;
            EntryHandling = EntryHandling.AllEntries;
            IsExitOnSessionCloseStrategy = true;
            IncludeCommission = false;
            IsInstantiatedOnEachOptimizationIteration = false;
            DefaultQuantity = 4;
            TraceOrders = true;
        }
        else if (State == State.Configure)
        {
            if (UseTrailingStop)
            {
                SetTrailStop(CalculationMode.Ticks, TrailingStopTicks);
                if (UseStops && ProfitTargetTicks > 0)
                    SetProfitTarget(CalculationMode.Ticks, ProfitTargetTicks);
            }
            else if (UseStops)
            {
                SetStopLoss(CalculationMode.Ticks, StopLossTicks);
                if (ProfitTargetTicks > 0)
                    SetProfitTarget(CalculationMode.Ticks, ProfitTargetTicks);
            }
        }
        else if (State == State.DataLoaded)
        {
            superTrend = new Series<double>(this);
            upperBandSeries = new Series<double>(this);
            lowerBandSeries = new Series<double>(this);
            dirSeries = new Series<int>(this);

            ema10 = EMA(Close, 10);
            ema20 = EMA(Close, 20);
        }
    }

    protected override void OnBarUpdate()
    {
        if (CurrentBar < 2)
        {
            InitializeSupertrendFirstBars();
            dirSeries[0] = 0;
            return;
        }

        // Range and raw bands (your original Keltner-like approach)
        double rangeC = 2.0 * (High[0] - Low[0]);
        double factorForCalc = Sensitivity;

        double upperBandRaw = Close[0] + factorForCalc * rangeC;
        double lowerBandRaw = Close[0] - factorForCalc * rangeC;

        double prevLowerBand = lowerBandSeries[1];
        double prevUpperBand = upperBandSeries[1];

        // Stickiness
        double lowerBand = (lowerBandRaw > prevLowerBand || Close[1] < prevLowerBand) ? lowerBandRaw : prevLowerBand;
        double upperBand = (upperBandRaw < prevUpperBand || Close[1] > prevUpperBand) ? upperBandRaw : prevUpperBand;

        // Direction from prior ST location
        double prevSuperTrend = superTrend[1];
int direction = dirSeries[1];      // default to previous direction
if (ApproximatelyEqual(prevSuperTrend, prevUpperBand))
    direction = Close[0] > upperBand ? 1 : -1;
else if (ApproximatelyEqual(prevSuperTrend, prevLowerBand))
    direction = Close[0] < lowerBand ? -1 : 1;


        // Select the plotted ST according to chosen orientation
        double superTrendNow;
        if (Orientation == OrientationMode.Inverted)
        {
            // Matches your original code (more trades)
            superTrendNow = (direction == -1) ? lowerBand : upperBand;
        }
        else
        {
            // Classic orientation
            superTrendNow = (direction == 1) ? lowerBand : upperBand;
        }

        // Save
        upperBandSeries[0] = upperBand;
        lowerBandSeries[0] = lowerBand;
        superTrend[0] = superTrendNow;
        dirSeries[0] = direction;

        // Signals
        bool bull = false;
        bool bear = false;

        switch (SignalGeneration)
        {
            default:
            case SignalMode.CrossClose:
                bull = CrossAbove(Close, superTrend, CrossLookback);
                bear = CrossBelow(Close, superTrend, CrossLookback);
                break;

            case SignalMode.CrossHighLow:
                bull = CrossAbove(High, superTrend, CrossLookback);
                bear = CrossBelow(Low, superTrend, CrossLookback);
                break;

            case SignalMode.DirectionFlip:
                bull = (dirSeries[1] == -1 && dirSeries[0] == 1);
                bear = (dirSeries[1] == 1 && dirSeries[0] == -1);
                break;
        }

        if (DebugPrints)
            Print($"{Time[0]:yyyy-MM-dd HH:mm:ss} | Close={Close[0]:F2} ST={superTrend[0]:F2} dir={direction} | bull={bull} bear={bear} | Pos={Position.MarketPosition}");

        // Entries
        if (bull && PullbackOk())
            EnterLong("BUY");
        else if (bear)
            EnterShort("SELL");
    }

    private bool PullbackOk()
    {
        if (!UsePullbackFilter)
            return true;

        bool closeCrossEma10 = CrossAbove(Close, ema10, 1);
        bool ema10CrossEma20 = CrossAbove(ema10, ema20, 1);
        return closeCrossEma10 && ema10CrossEma20;
    }

    private void InitializeSupertrendFirstBars()
    {
        double rangeC = 2.0 * (High[0] - Low[0]);
        double factorForCalc = Sensitivity;

        double upperBandRaw = Close[0] + factorForCalc * rangeC;
        double lowerBandRaw = Close[0] - factorForCalc * rangeC;

        upperBandSeries[0] = upperBandRaw;
        lowerBandSeries[0] = lowerBandRaw;
        // Initialize to a side to avoid insta-cross
        superTrend[0] = (Close[0] >= Open[0]) ? lowerBandRaw : upperBandRaw;
    }

    private bool ApproximatelyEqual(double a, double b, double epsilon = 1e-8)
    {
        return Math.Abs(a - b) <= epsilon;
    }

    // Legacy signature
    protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
    {
        if (!DebugPrints) return;
        Print($"{time:yyyy-MM-dd HH:mm:ss} | Exec | Pos={marketPosition} | qty={quantity} @ {price} | execId={executionId} orderId={orderId}");
    }
}
}