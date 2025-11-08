
#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;   // for Display, Range
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class SuperTrendReversalTrailingStrategy : Strategy
    {
				 // Exit label visualization
        [NinjaScriptProperty]
        [Display(Name = "Show Exit Labels", Order = 32, GroupName = "Filters")]
        public bool ShowExitLabels { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Exit Label ATR Offset", Order = 33, GroupName = "Filters")]
        public double ExitLabelAtrOffset { get; set; } = 0.6;
		
        #region Inputs
        [NinjaScriptProperty]
        [Display(Name = "Order Quantity", GroupName = "Parameters", Order = 0)]
        [Range(1, int.MaxValue)]
        public int OrderQuantity { get; set; } = 1;

        [NinjaScriptProperty]
        [Range(1.0, 5.0)]
        [Display(Name = "TP Multiplier (relative to ST distance)", GroupName = "Parameters", Order = 1)]
        public double TargetMultiple { get; set; } = 2.0;

        // Indicator mirror params
        [NinjaScriptProperty]
        [Range(0.5, 10)]
        [Display(Name = "ST Sensitivity", GroupName = "Indicator", Order = 10)]
        public double StSensitivity { get; set; } = 2.8;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ST Keltner Length", GroupName = "Indicator", Order = 11)]
        public int StKeltnerLength { get; set; } = 10;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ST Range Min Length", GroupName = "Indicator", Order = 12)]
        public int StRangeMinLen { get; set; } = 20;

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ST Range Width Mult", GroupName = "Indicator", Order = 13)]
        public double StRangeWidthMult { get; set; } = 1.0;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ST Range ATR Len", GroupName = "Indicator", Order = 14)]
        public int StRangeAtrLen { get; set; } = 500;

        // Real-time only controls
        [NinjaScriptProperty]
        [Display(Name = "Trade Real-time Only", GroupName = "Realtime Control", Order = 100)]
        public bool TradeRealtimeOnly { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 600)]
        [Display(Name = "Realtime Grace Seconds", GroupName = "Realtime Control", Order = 101)]
        public int RealtimeGraceSeconds { get; set; } = 5;
        #endregion
		#region private vars
        private CBASTestingIndicator3 stInd;

        private const string LongSignal = "L";
        private const string ShortSignal = "S";

        // Real-time gating state
        private DateTime realtimeEnteredUtc = DateTime.MinValue;
        private bool inRealtime = false;
		#endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "SuperTrendReversalTrailingStrategy";
                Calculate = Calculate.OnEachTick; // trail intrabar
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsUnmanaged = false;
                IsAdoptAccountPositionAware = false;
                TraceOrders = false;
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.DataLoaded)
            {
                stInd = CBASTestingIndicator3(
    Input,
    showExitLabels: ShowExitLabels,                 // bool
    exitLabelAtrOffset: ExitLabelAtrOffset,         // double
    useRegimeStability: UseRegimeStability,
    regimeStabilityBars: RegimeStabilityBars,
    useScoringFilter: UseScoringFilter,
    scoreThreshold: ScoreThreshold,
    useSmoothedVpm: UseSmoothedVpm,
    vpmEmaSpan: VpmEmaSpan,
    minVpm: MinVpm,
    minAdx: MinAdx,
    momentumLookback: MomentumLookback,
    extendedLogging: ExtendedLogging,
    computeExitSignals: ComputeExitSignals,
    exitProfitAtrMult: ExitProfitAtrMult,
    instanceId: InstanceId,                         // string
    sensitivity: Sensitivity,                       // double
    emaEnergy: EmaEnergy,                           // bool
    keltnerLength: KeltnerLength,
    atrLength: AtrLength,
    rangeMinLength: RangeMinLength,
    rangeWidthMult: RangeWidthMult,                 // double
    rangeAtrLen: RangeAtrLen,
    enableLogging: EnableLogging,
    logSignalsOnly: LogSignalsOnly,
    heartbeatEveryNBars: HeartbeatEveryNBars,
    logFolder: LogFolder                            // string
);

            }
            else if (State == State.Historical)
            {
                // Reset real-time state when starting historical processing
                inRealtime = false;
                realtimeEnteredUtc = DateTime.MinValue;
            }
            else if (State == State.Realtime)
            {
                // Transition to real-time
                inRealtime = true;
                realtimeEnteredUtc = DateTime.UtcNow;
            }
        }

        protected override void OnBarUpdate()
        {
            // Trade only live, with grace period
            if (TradeRealtimeOnly)
            {
                if (!inRealtime) return;
                if (RealtimeGraceSeconds > 0 &&
                    DateTime.UtcNow < realtimeEnteredUtc.AddSeconds(RealtimeGraceSeconds))
                    return;
            }

            if (CurrentBar < 2)
                return;

            // SuperTrend value from your indicator [first plot is "SuperTrend"]
            double st = stInd[0];
            if (double.IsNaN(st) || double.IsInfinity(st))
                return;

            bool crossUp = CrossAbove(Close, stInd, 1);
            bool crossDn = CrossBelow(Close, stInd, 1);

            // Reversal logic on crosses
            if (crossUp && Position.MarketPosition != MarketPosition.Long)
            {
                EnterLong(OrderQuantity, LongSignal);
            }
            else if (crossDn && Position.MarketPosition != MarketPosition.Short)
            {
                EnterShort(OrderQuantity, ShortSignal);
            }

            // Trailing exits [update each tick]
            if (Position.MarketPosition == MarketPosition.Long)
            {
                // Stop exactly on the SuperTrend line
                double stopPrice = RoundToTick(st);

                // Target at 2x ST distance [both trailing off current ST]
                // Distance = Close - ST; Target = ST + TargetMultiple * [Close - ST]
                double dist = Math.Max(TickSize, Close[0] - st);
                double targetPrice = RoundToTick(st + TargetMultiple * dist);

                // Submit/update OCO exits tied to the long entry signal
                ExitLongStopMarket(0, true, Position.Quantity, stopPrice, LongSignal + "_STP", LongSignal);
                ExitLongLimit(0, true, Position.Quantity, targetPrice, LongSignal + "_LMT", LongSignal);
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                // Stop exactly on the SuperTrend line
                double stopPrice = RoundToTick(st);

                // Target at 2x ST distance [both trailing off current ST]
                // Distance = ST - Close; Target = ST - TargetMultiple * [ST - Close]
                double dist = Math.Max(TickSize, st - Close[0]);
                double targetPrice = RoundToTick(st - TargetMultiple * dist);

                // Submit/update OCO exits tied to the short entry signal
                ExitShortStopMarket(0, true, Position.Quantity, stopPrice, ShortSignal + "_STP", ShortSignal);
                ExitShortLimit(0, true, Position.Quantity, targetPrice, ShortSignal + "_LMT", ShortSignal);
            }
        }

        private double RoundToTick(double price)
        {
            return Instrument != null
                ? Instrument.MasterInstrument.RoundToTickSize(price)
                : Math.Round(price / TickSize) * TickSize;
        }
    }
}