using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;                   // MessageBox
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using NinjaTrader.Core;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;            // NTWindow (if you use it)
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns;   // CBASTerminalWindow
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.SuperDomColumns;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class CBASTestingIndicator3 : Indicator
    {
        #region Inputs
        // Exit label visualization
        [NinjaScriptProperty]
        [Display(Name = "Show Exit Labels", Order = 32, GroupName = "Filters")]
        public bool ShowExitLabels { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Exit Label ATR Offset", Order = 33, GroupName = "Filters")]
        public double ExitLabelAtrOffset { get; set; } = 0.6;

        // Filters and scoring
        [NinjaScriptProperty]
        [Display(Name = "Use Regime Stability", Order = 20, GroupName = "Filters")]
        public bool UseRegimeStability { get; set; } = false;

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Regime Stability Bars", Order = 21, GroupName = "Filters")]
        public int RegimeStabilityBars { get; set; } = 10;

        [NinjaScriptProperty]
        [Display(Name = "Use Scoring Filter", Order = 22, GroupName = "Filters")]
        public bool UseScoringFilter { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Score Threshold", Order = 23, GroupName = "Filters")]
        public int ScoreThreshold { get; set; } = 6;

        [NinjaScriptProperty]
        [Display(Name = "Use Smoothed VPM", Order = 24, GroupName = "Filters")]
        public bool UseSmoothedVpm { get; set; } = false;

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "VPM EMA Span", Order = 25, GroupName = "Filters")]
        public int VpmEmaSpan { get; set; } = 5;

        // Confirmation thresholds (used by scoring if enabled)
        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Min VPM", Order = 26, GroupName = "Filters")]
        public double MinVpm { get; set; } = 300;

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Min ADX", Order = 27, GroupName = "Filters")]
        public int MinAdx { get; set; } = 25;

        // Momentum lookback (for features and optional scoring)
        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Momentum Lookback", Order = 28, GroupName = "Filters")]
        public int MomentumLookback { get; set; } = 14;

        // Logging extras (appended columns; off by default to avoid changing your CSV)
        [NinjaScriptProperty]
        [Display(Name = "Extended Logging", Order = 29, GroupName = "Logging")]
        public bool ExtendedLogging { get; set; } = true;

        // Optional exit logic computation only (no plots/draws to preserve interface)
        [NinjaScriptProperty]
        [Display(Name = "Compute Exit Signals", Order = 30, GroupName = "Filters")]
        public bool ComputeExitSignals { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0.0, 1000.0)]
        [Display(Name = "Exit Profit ATR Mult", Order = 31, GroupName = "Filters")]
        public double ExitProfitAtrMult { get; set; } = 3.0;

        [NinjaScriptProperty]
        [Display(Name = "Instance Id", Order = 99, GroupName = "Logging")]
        public string InstanceId { get; set; } = null;

        // Original inputs
        [NinjaScriptProperty]
        [Range(0.5, 10)]
        [Display(Name = "Sensitivity", Order = 1, GroupName = "Parameters")]
        public double Sensitivity { get; set; } = 3.4;

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

        // Logging inputs
        [NinjaScriptProperty]
        [Display(Name = "Enable Logging", Order = 1, GroupName = "Logging")]
        public bool EnableLogging { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Log Signals Only", Order = 2, GroupName = "Logging")]
        public bool LogSignalsOnly { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0, 1000000)]
        [Display(Name = "Heartbeat Every N Bars (0 = off)", Order = 3, GroupName = "Logging")]
        public int HeartbeatEveryNBars { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Log Folder", Order = 4, GroupName = "Logging")]
        public string LogFolder { get; set; } = null;

        // Optional: scale oscillator into price units to be visible on price panel
        [NinjaScriptProperty]
        [Display(Name = "Scale Oscillator To ATR", Order = 40, GroupName = "Plots")]
        public bool ScaleOscillatorToATR { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Osc ATR Mult", Order = 41, GroupName = "Plots")]
        public double OscAtrMult { get; set; } = 0.3;

        [NinjaScriptProperty]
        [Display(Name = "Log Drawn Signals", Order = 31, GroupName = "Logging")]
        public bool LogDrawnSignals { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Enable Debug Prints", Order = 32, GroupName = "Logging")]
        public bool EnableDebugPrints { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Color Bars By Trend", Order = 42, GroupName = "Visual")]
        public bool ColorBarsByTrend { get; set; } = false;

        // Realtime filter thresholds derived from log analysis
        [NinjaScriptProperty]
        [Display(Name = "Bull NetFlow Min", Order = 1, GroupName = "Realtime Filters")]
        public double RealtimeBullNetflowMin { get; set; } = 1.0;

        [NinjaScriptProperty]
        [Display(Name = "Bull Objection Max", Order = 2, GroupName = "Realtime Filters")]
        public double RealtimeBullObjectionMax { get; set; } = 3.0;

        [NinjaScriptProperty]
        [Display(Name = "Bull EMA Color Min", Order = 3, GroupName = "Realtime Filters")]
        public double RealtimeBullEmaColorMin { get; set; } = 8.0;

        [NinjaScriptProperty]
        [Display(Name = "Use Bull Attract Filter", Order = 4, GroupName = "Realtime Filters")]
        public bool RealtimeBullUseAttract { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Bull Attract Min", Order = 5, GroupName = "Realtime Filters")]
        public double RealtimeBullAttractMin { get; set; } = 4.5;

        [NinjaScriptProperty]
        [Display(Name = "Bull Score Min (0 = ignore)", Order = 6, GroupName = "Realtime Filters")]
        public double RealtimeBullScoreMin { get; set; } = 0.0;

        [NinjaScriptProperty]
        [Display(Name = "Bear NetFlow Max", Order = 7, GroupName = "Realtime Filters")]
        public double RealtimeBearNetflowMax { get; set; } = -0.5;

        [NinjaScriptProperty]
        [Display(Name = "Bear Objection Min", Order = 8, GroupName = "Realtime Filters")]
        public double RealtimeBearObjectionMin { get; set; } = 4.0;

        [NinjaScriptProperty]
        [Display(Name = "Bear EMA Color Max", Order = 9, GroupName = "Realtime Filters")]
        public double RealtimeBearEmaColorMax { get; set; } = 6.0;

        [NinjaScriptProperty]
        [Display(Name = "Use Bear Price/Band Filter", Order = 10, GroupName = "Realtime Filters")]
        public bool RealtimeBearUsePriceToBand { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Bear Price/Band Max", Order = 11, GroupName = "Realtime Filters")]
        public double RealtimeBearPriceToBandMax { get; set; } = 0.3;

        [NinjaScriptProperty]
        [Display(Name = "Bear Score Min (0 = ignore)", Order = 12, GroupName = "Realtime Filters")]
        public double RealtimeBearScoreMin { get; set; } = 0.0;

        [NinjaScriptProperty]
        [Display(Name = "Flat Tolerance (points)", Order = 13, GroupName = "Realtime Filters")]
        public double RealtimeFlatTolerance { get; set; } = 0.5;

        [NinjaScriptProperty]
        [Display(Name = "Show Realtime State Plot", Order = 14, GroupName = "Realtime Filters")]
        public bool ShowRealtimeStatePlot { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Plot Realtime Signals", Order = 15, GroupName = "Realtime Filters")]
        public bool PlotRealtimeSignals { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "Flip Confirmation Bars", Order = 16, GroupName = "Realtime Filters")]
        public int FlipConfirmationBars { get; set; } = 1;
        #endregion

        #region privates
        private int plotAttract;
        private int plotObjection;
        private int plotNetFlow;
        private int plotRealtimeState;

        private bool bull;
        private bool bear;
        private double superTrendNow;
        private ADX adx14;
        private SMA volSma;
        private Series<double> vpmSeries;
        private EMA vpmEmaInd;

        private Series<double> momentum;
        private Series<double> atrRatio;
        private Series<double> emaSpread;
        private Series<double> priceToBand;

        private double lastEntryPrice = double.NaN;
        private string instrumentName;

        private Series<double> superTrend;
        private Series<double> upperBandSeries;
        private Series<double> lowerBandSeries;

        private readonly int[] energyPeriods = new int[] { 9, 12, 15, 18, 21, 24, 27, 30, 33, 36, 39, 42, 45, 48, 51 };
        private EMA[] emaHighs;

        private double lastEmaColorCount = double.NaN;
        private int emaColorPrevValue = -1;
        private int emaColorPrevPrevValue = -1;
        private int emaColorBullPeak = -1;
        private int emaColorBearDropPrev = -1;
        private int emaColorBearDropCount = 0;
        private enum RealtimeSignalState { Flat = 0, Bull = 1, Bear = -1 }
        private RealtimeSignalState currentRealtimeState = RealtimeSignalState.Flat;
        private string currentRealtimeReason = string.Empty;
        private int currentBullScore = 0;
        private int currentBearScore = 0;
        private bool currentBullRangeBreak = false;
        private bool currentBearRangeBreak = false;

        private enum LastSignalType { None, Bull, Bear }
        private LastSignalType lastPlottedSignal = LastSignalType.None;
        private int consecutiveBullBars = 0;
        private int consecutiveBearBars = 0;
        private int pendingBearReversalBars = 0;
        private int pendingBullReversalBars = 0;

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

        private StreamWriter logWriter;
        private readonly object logLock = new object();
        private bool logInitialized = false;
        private string logPath = null;
        private int lastLoggedBullBar = -1;
        private int lastLoggedBearBar = -1;
        private int lastLoggedBarClose = -1;
        private StreamWriter signalDrawWriter;
        private readonly object signalDrawLock = new object();
        private bool signalDrawInitialized = false;
        private string signalDrawPath = null;
        private StreamWriter debugLogWriter;
        private bool debugLogInitialized = false;
        private readonly object debugLogLock = new object();
        private string debugLogPath = null;

        // Logging gate for "only on price change"
        private int cachedBarIndex = -1;
        private bool hasCachedBar = false;
        private double cachedBarOpen = double.NaN;
        private double cachedBarHigh = double.NaN;
        private double cachedBarLow = double.NaN;
        private double cachedBarClose = double.NaN;
        private double cachedSuperTrend = double.NaN;
        private double cachedUpperBand = double.NaN;
        private double cachedLowerBand = double.NaN;
        private double cachedAttract = double.NaN;
        private double cachedObjection = double.NaN;
        private double cachedNetFlow = double.NaN;
        private double cachedMomentumVal = double.NaN;
        private double cachedEmaColorValue = double.NaN;
        private bool cachedBullSignal = false;
        private bool cachedBearSignal = false;
        private bool cachedBullRaw = false;
        private bool cachedBearRaw = false;
        private string cachedBullReason = string.Empty;
        private string cachedBearReason = string.Empty;
        private RealtimeSignalState cachedRealtimeState = RealtimeSignalState.Flat;
        private string cachedRealtimeReason = string.Empty;
        private int cachedBullScore = 0;
        private int cachedBearScore = 0;
        private bool cachedBullRangeBreak = false;
        private bool cachedBearRangeBreak = false;
        private int lastLoggedBullSignalBar = -1;
        private int lastLoggedBearSignalBar = -1;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                // Order of AddPlot no longer matters; indices resolved by name.
                AddPlot(new Stroke(Brushes.DodgerBlue, DashStyleHelper.Solid, 2), PlotStyle.Line, "Attract");
                AddPlot(new Stroke(Brushes.Crimson, DashStyleHelper.Solid, 2), PlotStyle.Line, "Objection");
                AddPlot(new Stroke(Brushes.DodgerBlue, DashStyleHelper.DashDot, 2), PlotStyle.Line, "NetFlow");

                InstanceId = string.Empty;
                Description = "CBAS Terminal AddOn";
                Name = "CBASTestingIndicator3";
                IsOverlay = true;
                Calculate = Calculate.OnEachTick;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = true;

                AddPlot(new Stroke(Brushes.Gray, DashStyleHelper.Solid, 2), PlotStyle.Line, "SuperTrend");
                AddPlot(Brushes.Transparent, "UpperBand");
                AddPlot(Brushes.Transparent, "LowerBand");

                for (int i = 0; i < 15; i++)
                    AddPlot(Brushes.Transparent, $"EnergyEMA{i + 1}");

                AddPlot(Brushes.DodgerBlue, "RangeTop");
                AddPlot(Brushes.DodgerBlue, "RangeBottom");
                AddPlot(Brushes.DodgerBlue, "RangeMid");
                AddPlot(Brushes.Transparent, "RealtimeState");

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

                // Map plot indices by name
                plotSuperTrend = FindPlot("SuperTrend");
                plotUpperBand = FindPlot("UpperBand");
                plotLowerBand = FindPlot("LowerBand");

                energyPlotIdx = new int[15];
                for (int i = 0; i < 15; i++)
                    energyPlotIdx[i] = FindPlot($"EnergyEMA{i + 1}");

                plotRangeTop = FindPlot("RangeTop");
                plotRangeBottom = FindPlot("RangeBottom");
                plotRangeMid = FindPlot("RangeMid");
                plotRealtimeState = FindPlot("RealtimeState");

                plotAttract = FindPlot("Attract");
                plotObjection = FindPlot("Objection");
                plotNetFlow = FindPlot("NetFlow");

                if (new[] { plotSuperTrend, plotUpperBand, plotLowerBand, plotRangeTop, plotRangeBottom, plotRangeMid, plotRealtimeState, plotAttract, plotObjection, plotNetFlow }.Any(x => x < 0))
                    Print("[CBASTestingIndicator3] Plot index mapping error: one or more plots not found by name.");

                InitLogger();
                if (LogDrawnSignals)
                    InitSignalDrawLogger();
                if (EnableDebugPrints)
                    InitDebugLogger();

                instrumentName = Instrument?.FullName ?? "N/A";
                adx14 = ADX(14);
                volSma = SMA(Volume, 50);

                vpmSeries = new Series<double>(this);
                vpmEmaInd = EMA(vpmSeries, VpmEmaSpan);

                momentum = new Series<double>(this);
                atrRatio = new Series<double>(this);
                emaSpread = new Series<double>(this);
                priceToBand = new Series<double>(this);
            }
            else if (State == State.Active)
            {
                CBASTerminalAddOn.EnsureMenuForAllControlCenters();

                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var win = new CBASTerminalWindow();
                    win.Owner = Application.Current.MainWindow as Window;
                    win.Show();
                    win.Activate();

                    MessageBox.Show("AddOn Active");
                }));
            }
            else if (State == State.Terminated)
            {
                try
                {
                    if (EnableLogging && logInitialized)
                        FlushCachedBar();

                    lock (logLock)
                    {
                        logWriter?.Flush();
                        logWriter?.Dispose();
                        logWriter = null;
                        logInitialized = false;
                    }
                    lock (signalDrawLock)
                    {
                        signalDrawWriter?.Flush();
                        signalDrawWriter?.Dispose();
                        signalDrawWriter = null;
                        signalDrawInitialized = false;
                    }
                    lock (debugLogLock)
                    {
                        debugLogWriter?.Flush();
                        debugLogWriter?.Dispose();
                        debugLogWriter = null;
                        debugLogInitialized = false;
                    }
                }
                catch { /* ignore */ }
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar != cachedBarIndex)
            {
                if (hasCachedBar && EnableLogging)
                    FlushCachedBar();

                cachedBarIndex = CurrentBar;
                cachedBarOpen = Open[0];
                cachedBarHigh = High[0];
                cachedBarLow = Low[0];
                cachedBarClose = Close[0];
                cachedBullSignal = false;
                cachedBearSignal = false;
                cachedBullRaw = false;
                cachedBearRaw = false;
                cachedBullReason = string.Empty;
                cachedBearReason = string.Empty;
                hasCachedBar = true;

                if (EnableDebugPrints)
                    Print($"[CACHE-RESET] bar={CurrentBar} open={cachedBarOpen} high={cachedBarHigh} low={cachedBarLow}");
            }
            else
            {
                cachedBarHigh = Math.Max(cachedBarHigh, High[0]);
                cachedBarLow = Math.Min(cachedBarLow, Low[0]);
                cachedBarClose = Close[0];

                if (EnableDebugPrints)
                    Print($"[CACHE-UPDATE] bar={CurrentBar} high={cachedBarHigh} low={cachedBarLow}");
            }

            if (CurrentBar < Math.Max(2, KeltnerLength))
            {
                InitializeFirstBars(cachedBarOpen, cachedBarHigh, cachedBarLow);
                return;
            }

            if (EnableLogging && !logInitialized)
                InitLogger();

            if (LogDrawnSignals && !signalDrawInitialized)
                InitSignalDrawLogger();

            // Bands and supertrend
            double rangeC = 2.0 * (cachedBarHigh - cachedBarLow);

            double upperBandRaw = Close[0] + Sensitivity * rangeC;
            double lowerBandRaw = Close[0] - Sensitivity * rangeC;

            double prevLowerBand = lowerBandSeries[1];
            double prevUpperBand = upperBandSeries[1];

            double lowerBand = (lowerBandRaw > prevLowerBand || Close[1] < prevLowerBand) ? lowerBandRaw : prevLowerBand;
            double upperBand = (upperBandRaw < prevUpperBand || Close[1] > prevUpperBand) ? upperBandRaw : prevUpperBand;

            int direction;
            bool havePrev = CurrentBar > 0
                         && !double.IsNaN(upperBandSeries[1])
                         && !double.IsNaN(lowerBandSeries[1])
                         && !double.IsNaN(superTrend[1]);

            if (!havePrev)
                direction = +1;
            else if (ApproximatelyEqual(superTrend[1], upperBandSeries[1]))
                direction = (Close[0] > upperBand) ? +1 : -1;
            else
                direction = (Close[0] < lowerBand) ? -1 : +1;

            double stNow = (direction == +1) ? lowerBand : upperBand;

            upperBandSeries[0] = upperBand;
            lowerBandSeries[0] = lowerBand;
            superTrend[0] = stNow;

            SetPlotVal(plotSuperTrend, stNow);
            SetPlotVal(plotUpperBand, upperBand);
            SetPlotVal(plotLowerBand, lowerBand);

            this.superTrendNow = stNow;

            double vpmRaw = ComputeVpm();
            vpmSeries[0] = vpmRaw;
            double vpmSmooth = vpmEmaInd != null && vpmEmaInd.IsValidDataPoint(0) ? vpmEmaInd[0] : double.NaN;
            double vpmUse = UseSmoothedVpm ? vpmSmooth : vpmRaw;

            // Feature series
            double atr30ValNow = atr30[0];
            atrRatio[0] = Close[0] != 0 ? (atr30ValNow / Close[0]) : double.NaN;
            emaSpread[0] = atr30ValNow != 0 ? ((ema10Close[0] - ema20Close[0]) / atr30ValNow) : double.NaN;
            double bandWidth = (upperBand - lowerBand);
            priceToBand[0] = bandWidth != 0 ? ((Close[0] - lowerBand) / bandWidth) : double.NaN;

            // Momentum
            if (CurrentBar >= MomentumLookback)
            {
                double basePx = Close[MomentumLookback];
                momentum[0] = basePx != 0 ? ((Close[0] - basePx) / basePx) : double.NaN;
            }
            else
            {
                momentum[0] = double.NaN;
            }

            // Attract/Objection/NetFlow computed early so signal logging has values
            double emaColorCount = ComputeEmaColorCount();
            var ao = ComputeAttractObjection(UseSmoothedVpm);
            double net = ao.attract - ao.objection;
            double momentumVal = (momentum != null && !double.IsNaN(momentum[0])) ? momentum[0] : double.NaN;
            int emaColorInt = (int)Math.Round(emaColorCount);
            int prevEmaColorInt = emaColorPrevValue < 0 ? emaColorInt : emaColorPrevValue;
            int prevPrevEmaColorInt = emaColorPrevPrevValue < 0 ? prevEmaColorInt : emaColorPrevPrevValue;
            if (emaColorBullPeak < 0)
                emaColorBullPeak = emaColorInt;
            if (emaColorInt >= emaColorBullPeak)
                emaColorBullPeak = emaColorInt;
            else if (emaColorBullPeak - emaColorInt > 6)
                emaColorBullPeak = emaColorInt;

            bool bullFromEmaColor = false;
            bool bearFromEmaColor = false;
            bool forceBullSignal = false;
            bool forceBearSignal = false;
            string debugBullReason = string.Empty;
            string debugBearReason = string.Empty;
            void AddReason(ref string target, string reason)
            {
                if (string.IsNullOrEmpty(reason))
                    return;
                if (!string.IsNullOrEmpty(target))
                    target += "|";
                target += reason;
            }

            bool bullCrossRaw = CrossAbove(Close, superTrend, 1);
            bool bearCrossRaw = CrossBelow(Close, superTrend, 1);

            bull = bullCrossRaw;
            bear = bearCrossRaw;
            if (bullCrossRaw)
                AddReason(ref debugBullReason, "[CROSS]");
            if (bearCrossRaw)
                AddReason(ref debugBearReason, "[CROSS]");

            if (emaColorInt == 15)
            {
                if (prevEmaColorInt <= 0 || emaColorBullPeak == 15)
                {
                    bullFromEmaColor = true;
                    if (prevEmaColorInt <= 0)
                    {
                        forceBullSignal = true;
                        AddReason(ref debugBullReason, "[BULL-FORCE-EMA15]");
                        if (EnableDebugPrints)
                            Print($"[BULL-FORCE-EMA15] bar={CurrentBar} emaColorPrev={prevEmaColorInt}");
                    }
                    else
                        AddReason(ref debugBullReason, "[EMA15-STABLE]");
                }

                if (CurrentBar >= 2 && Close[0] < Close[1] && Close[1] < Close[2])
                    bearFromEmaColor = true;

                emaColorBearDropCount = 0;
                emaColorBearDropPrev = emaColorInt;
            }
            else
            {
                if (prevEmaColorInt == 15 && emaColorInt <= 14)
                {
                    emaColorBearDropCount = 1;
                    emaColorBearDropPrev = emaColorInt;
                }
                else if (emaColorBearDropCount > 0)
                {
                    if (emaColorInt <= 14)
                    {
                        if (emaColorInt <= emaColorBearDropPrev)
                            emaColorBearDropCount++;
                        else if (emaColorInt - emaColorBearDropPrev > 6)
                            emaColorBearDropCount = 0;

                        emaColorBearDropPrev = emaColorInt;
                    }
                    else
                    {
                        if (emaColorInt - emaColorBearDropPrev > 6 || emaColorInt >= 15)
                            emaColorBearDropCount = 0;
                        emaColorBearDropPrev = emaColorInt;
                    }

                    if (emaColorBearDropCount >= 2 && emaColorInt <= 14)
                        bearFromEmaColor = true;
                }

                if (!bearFromEmaColor && prevEmaColorInt >= 14 && emaColorInt <= 1)
                {
                    bearFromEmaColor = true;
                    forceBearSignal = true;
                    AddReason(ref debugBearReason, "[BEAR-FORCE-EMA0]");
                    if (EnableDebugPrints)
                        Print($"[BEAR-FORCE-EMA0] bar={CurrentBar} emaColorPrev={prevEmaColorInt}");
                }
            }

            bool canCheckHistory = CurrentBar >= 2;
            bool priceUpNow = canCheckHistory ? Close[0] > Close[1] : true;
            bool priceUpTwo = canCheckHistory ? (Close[0] > Close[1] && Close[1] >= Close[2]) : true;
            bool priceDownNow = canCheckHistory ? Close[0] < Close[1] : true;
            bool priceDownTwo = canCheckHistory ? (Close[0] < Close[1] && Close[1] <= Close[2]) : true;

            double netFlowBullThreshold = 1.0;
            double netFlowBearThreshold = -1.0;
            bool netflowSupportsBull = net >= netFlowBullThreshold;
            bool netflowSupportsBear = net <= netFlowBearThreshold;

            bool earlyBullTrigger = lastPlottedSignal != LastSignalType.Bull
                                    && prevEmaColorInt <= 5
                                    && emaColorInt >= 12
                                    && priceUpTwo;

            bool earlyBearTrigger = lastPlottedSignal != LastSignalType.Bear
                                     && prevEmaColorInt >= 10
                                     && emaColorInt <= 3
                                     && priceDownTwo;

            bool netflowBullTrigger = lastPlottedSignal != LastSignalType.Bull
                                       && emaColorInt <= 2
                                       && netflowSupportsBull
                                       && (priceUpTwo || (priceUpNow && net > 0));

            bool netflowBearTrigger = lastPlottedSignal != LastSignalType.Bear
                                        && emaColorInt >= 13
                                        && netflowSupportsBear
                                        && (priceDownTwo || (priceDownNow && net < 0));

            if (earlyBullTrigger)
            {
                bullFromEmaColor = true;
                forceBullSignal = true;
                AddReason(ref debugBullReason, $"[EARLY-BULL] emaColor={emaColorInt} net={net:F2}");
                if (EnableDebugPrints)
                    Print($"[EARLY-BULL] bar={CurrentBar} emaColor={emaColorInt} net={net:F2}");
            }

            if (earlyBearTrigger)
            {
                bearFromEmaColor = true;
                forceBearSignal = true;
                AddReason(ref debugBearReason, $"[EARLY-BEAR] emaColor={emaColorInt} net={net:F2}");
                if (EnableDebugPrints)
                    Print($"[EARLY-BEAR] bar={CurrentBar} emaColor={emaColorInt} net={net:F2}");
            }

            if (netflowBullTrigger)
            {
                bullFromEmaColor = true;
                forceBullSignal = true;
                AddReason(ref debugBullReason, $"[NETFLOW-BULL] emaColor={emaColorInt} net={net:F2}");
                if (EnableDebugPrints)
                    Print($"[NETFLOW-BULL] bar={CurrentBar} emaColor={emaColorInt} net={net:F2}");
            }

            if (netflowBearTrigger)
            {
                bearFromEmaColor = true;
                forceBearSignal = true;
                AddReason(ref debugBearReason, $"[NETFLOW-BEAR] emaColor={emaColorInt} net={net:F2}");
                if (EnableDebugPrints)
                    Print($"[NETFLOW-BEAR] bar={CurrentBar} emaColor={emaColorInt} net={net:F2}");
            }

            bool extremeBearSwitch = prevPrevEmaColorInt >= 15 && prevEmaColorInt <= 10 && emaColorInt <= 0;
            if (extremeBearSwitch)
            {
                bearFromEmaColor = true;
                forceBearSignal = true;
                if (EnableDebugPrints && debugBearReason == null)
                    debugBearReason = "[EXTREME-BEAR] emaColor 15→≤10→≤0";
            }

            bool extremeBullSwitch = prevPrevEmaColorInt <= 0 && prevEmaColorInt >= 10 && emaColorInt >= 15;
            if (extremeBullSwitch)
            {
                bullFromEmaColor = true;
                forceBullSignal = true;
                if (EnableDebugPrints && debugBullReason == null)
                    debugBullReason = "[EXTREME-BULL] emaColor 0→≥10→≥15";
            }

            bool redAfterGreen = canCheckHistory && Close[1] > Open[1] && Close[0] < Open[0];
            bool greenAfterRed = canCheckHistory && Close[1] < Open[1] && Close[0] > Open[0];
            double candleDrop = Open[0] - Close[0];
            double candleRise = Close[0] - Open[0];
            bool redCandle = Close[0] < Open[0];
            bool greenCandle = Close[0] > Open[0];
            bool trendingBear = lastPlottedSignal == LastSignalType.Bear || superTrendNow < superTrend[1];
            bool trendingBull = lastPlottedSignal == LastSignalType.Bull || superTrendNow > superTrend[1];
            double bearThreshold = trendingBear ? Math.Max(3.0, superTrendNow - Close[0]) : 3.0;
            double bullThreshold = trendingBull ? Math.Max(3.0, Close[0] - superTrendNow) : 3.0;

            if (emaColorInt >= 15 && (!redCandle || candleDrop < bearThreshold))
            {
                bearFromEmaColor = false;
                forceBearSignal = false;
                AddReason(ref debugBearReason, "[GUARD-EMA15]");
                if (EnableDebugPrints)
                    Print($"[GUARD-EMA15] bar={CurrentBar} drop={candleDrop:F2} thresh={bearThreshold:F2}");
            }

            if (emaColorInt <= 0 && (!greenCandle || candleRise < bullThreshold))
            {
                bullFromEmaColor = false;
                forceBullSignal = false;
                AddReason(ref debugBullReason, "[GUARD-EMA0]");
                if (EnableDebugPrints)
                    Print($"[GUARD-EMA0] bar={CurrentBar} rise={candleRise:F2} thresh={bullThreshold:F2}");
            }

            if (bearFromEmaColor && emaColorInt >= 15 && (!redCandle || candleDrop < bearThreshold || net > -0.5))
            {
                bearFromEmaColor = false;
                AddReason(ref debugBearReason, "[GUARD-NETFLOW]");
            }

            if (bullFromEmaColor && emaColorInt <= 0 && (!greenCandle || candleRise < bullThreshold || net < 0.5))
            {
                bullFromEmaColor = false;
                AddReason(ref debugBullReason, "[GUARD-NETFLOW]");
            }

            if (bear && emaColorInt >= 15 && (!redCandle || candleDrop < bearThreshold || net > -0.5))
            {
                bear = false;
                AddReason(ref debugBearReason, "[GUARD-EMA15-FINAL]");
            }

            if (bull && emaColorInt <= 0 && (!greenCandle || candleRise < bullThreshold || net < 0.5))
            {
                bull = false;
                AddReason(ref debugBullReason, "[GUARD-EMA0-FINAL]");
            }

            if (bullFromEmaColor)
                bull = true;
            if (bearFromEmaColor)
                bear = true;

            emaColorPrevPrevValue = emaColorPrevValue;
            emaColorPrevValue = emaColorInt;

            bool bullRangeBreakScore;
            int bullScoreValue = ComputeScore(bullCrossRaw, false, stNow, vpmUse, out bullRangeBreakScore);
            bool bearRangeBreakScore;
            int bearScoreValue = ComputeScore(false, bearCrossRaw, stNow, vpmUse, out bearRangeBreakScore);
            currentBullScore = bullScoreValue;
            currentBearScore = bearScoreValue;
            currentBullRangeBreak = bullRangeBreakScore;
            currentBearRangeBreak = bearRangeBreakScore;

            double priceToBandVal = priceToBand != null ? priceToBand[0] : double.NaN;
            double ocDiff = Close[0] - Open[0];
            var realtimeState = EvaluateRealtimeState(net, ao.objection, emaColorInt, priceToBandVal, ao.attract, bullScoreValue, bearScoreValue, ocDiff);
            currentRealtimeState = realtimeState.State;
            currentRealtimeReason = realtimeState.Reason;

            if (UseScoringFilter)
            {
                if (bull) bull = bullScoreValue >= ScoreThreshold;
                if (bear) bear = bearScoreValue >= ScoreThreshold;

                if (bullCrossRaw || bearCrossRaw)
                    Print($"[SC] Bar {CurrentBar} bullScore={bullScoreValue} bearScore={bearScoreValue} thr={ScoreThreshold} -> bull={bull} bear={bear}");
            }

            bool realtimeBullState = currentRealtimeState == RealtimeSignalState.Bull;
            bool realtimeBearState = currentRealtimeState == RealtimeSignalState.Bear;

            if (PlotRealtimeSignals)
            {
                if (bull && !realtimeBullState)
                    AddReason(ref debugBullReason, currentRealtimeState == RealtimeSignalState.Bear ? "[RT-BEAR]" : "[RT-FLAT]");
                if (bear && !realtimeBearState)
                    AddReason(ref debugBearReason, currentRealtimeState == RealtimeSignalState.Bull ? "[RT-BULL]" : "[RT-FLAT]");

                bull = realtimeBullState;
                bear = realtimeBearState;

                if (bull)
                {
                    AddReason(ref debugBullReason, "[RT-BULL]");
                    AddReason(ref debugBullReason, "[RT-PLOT]");
                    forceBullSignal = true;
                }

                if (bear)
                {
                    AddReason(ref debugBearReason, "[RT-BEAR]");
                    AddReason(ref debugBearReason, "[RT-PLOT]");
                    forceBearSignal = true;
                }
            }
            else
            {
                if (bull && !realtimeBullState)
                {
                    AddReason(ref debugBullReason, realtimeBearState ? "[RT-BEAR]" : "[RT-FLAT]");
                    bull = false;
                }

                if (bear && !realtimeBearState)
                {
                    AddReason(ref debugBearReason, realtimeBullState ? "[RT-BULL]" : "[RT-FLAT]");
                    bear = false;
                }
            }

            int prospectiveBullCount = bull ? consecutiveBullBars + 1 : 0;
            int prospectiveBearCount = bear ? consecutiveBearBars + 1 : 0;

            if (bull)
            {
                int requiredBars = lastPlottedSignal == LastSignalType.Bear ? Math.Max(0, FlipConfirmationBars) : 1;
                if (forceBullSignal)
                    prospectiveBullCount = Math.Max(prospectiveBullCount, requiredBars);
                bool allowBull = lastPlottedSignal != LastSignalType.Bull && (forceBullSignal || prospectiveBullCount >= requiredBars);
                if (lastPlottedSignal == LastSignalType.Bull || !allowBull)
                {
                    AddReason(ref debugBullReason, forceBullSignal ? "[THROTTLE-BYPASS]" : "[THROTTLE]");
                    bull = false;
                }
            }

            if (bear)
            {
                int requiredBars = lastPlottedSignal == LastSignalType.Bull ? Math.Max(0, FlipConfirmationBars) : 1;
                if (forceBearSignal)
                    prospectiveBearCount = Math.Max(prospectiveBearCount, requiredBars);
                bool allowBear = lastPlottedSignal != LastSignalType.Bear && (forceBearSignal || prospectiveBearCount >= requiredBars);
                if (lastPlottedSignal == LastSignalType.Bear || !allowBear)
                {
                    AddReason(ref debugBearReason, forceBearSignal ? "[THROTTLE-BYPASS]" : "[THROTTLE]");
                    bear = false;
                }
            }

            if (bull && bear)
            {
                if (lastPlottedSignal == LastSignalType.Bull)
                    bear = false;
                else if (lastPlottedSignal == LastSignalType.Bear)
                    bull = false;
                else
                {
                    if (emaColorInt >= 10 || prospectiveBullCount > prospectiveBearCount || forceBullSignal)
                        bear = false;
                    else
                        bull = false;
                }
            }

            consecutiveBullBars = bull ? prospectiveBullCount : 0;
            consecutiveBearBars = bear ? prospectiveBearCount : 0;

            if (bull)
            {
                double y1 = Low[0] - atr30[0] * 2.0;
                Draw.TriangleUp(this, $"Buy_{CurrentBar}", false, 0, y1, Brushes.LimeGreen);
                double labelOffset = atr30[0] * 0.5;
                Draw.Text(this,
                    $"BullLabel_{CurrentBar}",
                    false,
                    $"Bar {CurrentBar}",
                    0,
                    y1 - labelOffset,
                    0,
                    Brushes.LimeGreen,
                    new SimpleFont("Arial", 12),
                    TextAlignment.Center,
                    Brushes.Transparent,
                    Brushes.Transparent,
                    0);
                LogDrawnSignal("BULL", Close[0], stNow, net);
            }
            if (bear)
            {
                double y2 = High[0] + atr30[0] * 2.0;
                Draw.TriangleDown(this, $"Sell_{CurrentBar}", false, 0, y2, Brushes.Red);
                double labelOffset = atr30[0] * 0.5;
                Draw.Text(this,
                    $"BearLabel_{CurrentBar}",
                    false,
                    $"Bar {CurrentBar}",
                    0,
                    y2 + labelOffset,
                    0,
                    Brushes.Red,
                    new SimpleFont("Arial", 12),
                    TextAlignment.Center,
                    Brushes.Transparent,
                    Brushes.Transparent,
                    0);
                LogDrawnSignal("BEAR", Close[0], stNow, net);
            }

            if (bull)
                lastPlottedSignal = LastSignalType.Bull;
            else if (bear)
                lastPlottedSignal = LastSignalType.Bear;

            bool exitLong = false;
            bool exitShort = false;
            if (ComputeExitSignals)
            {
                if (bull) lastEntryPrice = Close[0];
                if (bear) lastEntryPrice = Close[0];

                double profitAbs = (!double.IsNaN(lastEntryPrice)) ? Math.Abs(Close[0] - lastEntryPrice) : 0.0;
                double profitAtr = atr30[0] > 0 ? (profitAbs / atr30[0]) : 0.0;

                exitLong = bear || (Close[0] < ema20Close[0]) || (profitAtr > ExitProfitAtrMult);
                exitShort = bull || (Close[0] > ema20Close[0]) || (profitAtr > ExitProfitAtrMult);
            }

            if (ComputeExitSignals && ShowExitLabels)
            {
                double atr = Math.Max(1e-6, atr30[0]);
                double yLong = Low[0] - atr * ExitLabelAtrOffset;
                double yShort = High[0] + atr * ExitLabelAtrOffset;

                if (exitLong)
                    Draw.Text(this, $"ExitLong_{CurrentBar}", "Exit Long", 0, yLong, Brushes.ForestGreen);

                if (exitShort)
                    Draw.Text(this, $"ExitShort_{CurrentBar}", "Exit Short", 0, yShort, Brushes.IndianRed);
            }

            if (string.IsNullOrEmpty(debugBullReason))
                debugBullReason = bull ? "[PLOT]" : (bullCrossRaw ? "[SUPPRESSED]" : string.Empty);
            if (string.IsNullOrEmpty(debugBearReason))
                debugBearReason = bear ? "[PLOT]" : (bearCrossRaw ? "[SUPPRESSED]" : string.Empty);

            if (plotRealtimeState >= 0)
            {
                double plotValue = double.NaN;
                if (ShowRealtimeStatePlot)
                {
                    switch (currentRealtimeState)
                    {
                        case RealtimeSignalState.Bull:
                            plotValue = 1.0;
                            PlotBrushes[plotRealtimeState][0] = Brushes.LimeGreen;
                            break;
                        case RealtimeSignalState.Bear:
                            plotValue = -1.0;
                            PlotBrushes[plotRealtimeState][0] = Brushes.Red;
                            break;
                        default:
                            plotValue = 0.0;
                            PlotBrushes[plotRealtimeState][0] = Brushes.Gray;
                            break;
                    }
                }
                else
                {
                    PlotBrushes[plotRealtimeState][0] = Brushes.Transparent;
                }

                SetPlotVal(plotRealtimeState, ShowRealtimeStatePlot ? plotValue : double.NaN);
            }

            cachedRealtimeState = currentRealtimeState;
            cachedRealtimeReason = currentRealtimeReason;
            cachedBullScore = currentBullScore;
            cachedBearScore = currentBearScore;
            cachedBullRangeBreak = currentBullRangeBreak;
            cachedBearRangeBreak = currentBearRangeBreak;
            cachedSuperTrend = stNow;
            cachedUpperBand = upperBand;
            cachedLowerBand = lowerBand;
            cachedAttract = ao.attract;
            cachedObjection = ao.objection;
            cachedNetFlow = net;
            cachedMomentumVal = momentumVal;
            cachedEmaColorValue = emaColorCount;
            cachedBullSignal = bull;
            cachedBearSignal = bear;
            cachedBullRaw = bullCrossRaw;
            cachedBearRaw = bearCrossRaw;
            cachedBullReason = debugBullReason;
            cachedBearReason = debugBearReason;
            cachedBarClose = Close[0];

            if (ColorBarsByTrend && CurrentBar > 0)
                BarBrushes[0] = Close[0] > superTrend[1] ? Brushes.Teal : Brushes.Red;

            // Energy EMAs visualization
            for (int i = 0; i < emaHighs.Length; i++)
            {
                double v = emaHighs[i][0];
                int pIdx = energyPlotIdx[i];
                SetPlotVal(pIdx, v);
                SetPlotBrush(pIdx, EmaEnergy ? (Close[0] >= v ? Brushes.Teal : Brushes.Red) : Brushes.Transparent);
            }

            // Attract/Objection/NetFlow visualisation

            if (ScaleOscillatorToATR)
            {
                // Scale around supertrend using ATR so it's visible on price panel
                double basePx = double.IsNaN(stNow) ? Close[0] : stNow;
                double scale = Math.Max(1e-9, atr30[0]) * OscAtrMult;

                double attractScaled = basePx + scale * (ao.attract - 5.0);     // center around base
                double objectionScaled = basePx + scale * (ao.objection - 5.0);
                double netScaled = basePx + scale * net;

                SetPlotVal(plotAttract, attractScaled);
                SetPlotVal(plotObjection, objectionScaled);
                SetPlotVal(plotNetFlow, netScaled);
            }
            else
            {
                SetPlotVal(plotAttract, ao.attract);
                SetPlotVal(plotObjection, ao.objection);
                SetPlotVal(plotNetFlow, net);
            }

            SetPlotBrush(plotNetFlow, net >= 0 ? Brushes.DodgerBlue : Brushes.DarkOrange);

            // Range detector updates
            UpdateRangeDetectorPlots();

        }

        private void InitializeFirstBars(double barOpen, double barHigh, double barLow)
        {
            double rangeC = 2.0 * (barHigh - barLow);
            double upperBandRaw = Close[0] + Sensitivity * rangeC;
            double lowerBandRaw = Close[0] - Sensitivity * rangeC;

            if (Plots != null)
            {
                for (int i = 0; i < Plots.Length; i++)
                    SetPlotVal(i, double.NaN);
            }

            upperBandSeries[0] = upperBandRaw;
            lowerBandSeries[0] = lowerBandRaw;
            superTrend[0] = Close[0];

            SetPlotVal(plotUpperBand, upperBandRaw);
            SetPlotVal(plotLowerBand, lowerBandRaw);
            SetPlotVal(plotSuperTrend, Close[0]);

        }

        private (double attract, double objection) ComputeAttractObjection(bool useSmoothedVpm = true)
        {
            double st = superTrend[0];
            if (double.IsNaN(st))
                st = this.superTrendNow;
            if (double.IsNaN(st))
                st = Close[0];

            bool bullRegime = !double.IsNaN(st) && Close[0] > st;
            bool bearRegime = !double.IsNaN(st) && Close[0] < st;

            double spread = (emaSpread != null) ? emaSpread[0] : double.NaN; // (EMA10-EMA20)/ATR30
            double spreadDelta = (CurrentBar > 0 && emaSpread != null && !double.IsNaN(emaSpread[1]))
                                 ? (emaSpread[0] - emaSpread[1]) : 0.0;

            double adxVal = (adx14 != null && adx14.IsValidDataPoint(0)) ? adx14[0] : double.NaN;
            double adxDelta = (adx14 != null && CurrentBar > 0 && adx14.IsValidDataPoint(1))
                              ? (adx14[0] - adx14[1]) : 0.0;

            double vpmRaw = (vpmSeries != null) ? vpmSeries[0] : double.NaN;
            double vpmSm = (vpmEmaInd != null && vpmEmaInd.IsValidDataPoint(0)) ? vpmEmaInd[0] : double.NaN;
            double vpmUse = useSmoothedVpm ? vpmSm : vpmRaw;
            double vpmDelta = (useSmoothedVpm && CurrentBar > 0 && vpmEmaInd != null && vpmEmaInd.IsValidDataPoint(1))
                              ? (vpmEmaInd[0] - vpmEmaInd[1]) : 0.0;

            double p2b = (priceToBand != null) ? priceToBand[0] : double.NaN; // 0..1
            double mom = (momentum != null && CurrentBar >= MomentumLookback) ? momentum[0] : double.NaN;

            bool rangeUpBreak = (!double.IsNaN(rangeMax) && Close[0] > rangeMax);
            bool rangeDnBreak = (!double.IsNaN(rangeMin) && Close[0] < rangeMin);
            int osState = os;

            double rng = Math.Max(1e-6, High[0] - Low[0]);
            double upperWick = (High[0] - Close[0]) / rng; // 0..1
            double lowerWick = (Close[0] - Low[0]) / rng;  // 0..1

            double attract = 0.0;
            attract += bullRegime ? 2.0 : 0.0;
            if (!double.IsNaN(spread)) attract += Math.Max(0, Math.Min(2.0, 3.0 * spread));
            if (spreadDelta > 0) attract += 0.5;
            if (!double.IsNaN(adxVal) && adxVal > MinAdx) attract += 1.0;
            if (adxDelta > 0) attract += 0.5;
            if (!double.IsNaN(vpmUse) && vpmUse > MinVpm) attract += 1.0;
            if (vpmDelta > 0) attract += 0.5;
            if (osState == +1 || rangeUpBreak) attract += 1.5;
            if (!double.IsNaN(p2b) && p2b >= 0.6) attract += 0.5;
            if (!double.IsNaN(mom) && mom > 0) attract += 0.5;
            attract = Math.Max(0, Math.Min(10, attract));

            double objection = 0.0;
            if (upperWick > 0.5) objection += 2.0;
            if (!double.IsNaN(spread) && spread < 0) objection += 1.0;
            if (spreadDelta < 0) objection += 0.5;
            if (adxDelta < 0) objection += 0.5;
            if (!double.IsNaN(p2b) && p2b > 0.9) objection += 1.0;
            if (osState == 0) objection += 0.5;
            if (rangeDnBreak) objection += 2.0;
            if (bearRegime) objection += 1.0;
            objection = Math.Max(0, Math.Min(10, objection));

            return (attract, objection);
        }

        private void UpdateRangeDetectorPlots()
        {
            if (CurrentBar < RangeMinLength)
            {
                SetPlotVal(plotRangeTop, double.NaN);
                SetPlotVal(plotRangeBottom, double.NaN);
                SetPlotVal(plotRangeMid, double.NaN);
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
                SetPlotVal(plotRangeTop, double.NaN);
                SetPlotVal(plotRangeBottom, double.NaN);
                SetPlotVal(plotRangeMid, double.NaN);
                return;
            }

            SetPlotVal(plotRangeTop, rangeMax);
            SetPlotVal(plotRangeBottom, rangeMin);
            SetPlotVal(plotRangeMid, (rangeMax + rangeMin) / 2.0);

            Brush css = os == 0 ? Brushes.DodgerBlue : os == 1 ? Brushes.LimeGreen : Brushes.IndianRed;
            SetPlotBrush(plotRangeTop, css);
            SetPlotBrush(plotRangeBottom, css);
            SetPlotBrush(plotRangeMid, css);
        }

        private bool ApproximatelyEqual(double a, double b, double epsilon = 1e-8)
        {
            return Math.Abs(a - b) <= epsilon;
        }

        private int CurrentRegimeDir(double stNow)
        {
            if (Close[0] > stNow) return +1;
            if (Close[0] < stNow) return -1;
            return 0;
        }

        private bool RegimeStable(int priorDir, int bars)
        {
            int need = Math.Min(bars, CurrentBar);
            if (need <= 0)
                return false;

            for (int i = 1; i <= need; i++)
            {
                double st = superTrend[i];
                double ub = upperBandSeries[i];
                double lb = lowerBandSeries[i];

                if (double.IsNaN(st) || double.IsNaN(ub) || double.IsNaN(lb))
                    return false;

                int dirAtI;
                if (ApproximatelyEqual(st, ub))
                    dirAtI = -1;
                else if (ApproximatelyEqual(st, lb))
                    dirAtI = +1;
                else
                    return false;

                if (dirAtI != priorDir)
                    return false;
            }
            return true;
        }

        private (RealtimeSignalState State, string Reason) EvaluateRealtimeState(
            double net,
            double objection,
            int emaColor,
            double priceToBandVal,
            double attractVal,
            int bullScore,
            int bearScore,
            double ocDiff)
        {
            var bullReasons = new List<string>();
            var bullFailures = new List<string>();
            bool bullCandidate = true;

            if (!double.IsNaN(net) && net >= RealtimeBullNetflowMin)
                bullReasons.Add($"net>={RealtimeBullNetflowMin:F2}");
            else
            {
                bullCandidate = false;
                bullFailures.Add($"net<{RealtimeBullNetflowMin:F2}");
            }

            if (!double.IsNaN(objection) && objection <= RealtimeBullObjectionMax)
                bullReasons.Add($"obj<={RealtimeBullObjectionMax:F2}");
            else
            {
                bullCandidate = false;
                bullFailures.Add($"obj>{RealtimeBullObjectionMax:F2}");
            }

            if (emaColor >= RealtimeBullEmaColorMin)
                bullReasons.Add($"ema>={RealtimeBullEmaColorMin:F0}");
            else
            {
                bullCandidate = false;
                bullFailures.Add($"ema<{RealtimeBullEmaColorMin:F0}");
            }

            if (RealtimeBullUseAttract)
            {
                if (!double.IsNaN(attractVal) && attractVal >= RealtimeBullAttractMin)
                    bullReasons.Add($"att>={RealtimeBullAttractMin:F2}");
                else
                {
                    bullCandidate = false;
                    bullFailures.Add($"att<{RealtimeBullAttractMin:F2}");
                }
            }

            if (RealtimeBullScoreMin > 0.0)
            {
                if (bullScore >= RealtimeBullScoreMin)
                    bullReasons.Add($"score>={RealtimeBullScoreMin:F0}");
                else
                {
                    bullCandidate = false;
                    bullFailures.Add($"score<{RealtimeBullScoreMin:F0}");
                }
            }

            if (bullCandidate)
            {
                string reason = bullReasons.Count > 0 ? string.Join("|", bullReasons) : "criteria";
                return (RealtimeSignalState.Bull, $"BULL|{reason}");
            }

            var bearReasons = new List<string>();
            var bearFailures = new List<string>();
            bool bearCandidate = true;

            if (!double.IsNaN(net) && net <= RealtimeBearNetflowMax)
                bearReasons.Add($"net<={RealtimeBearNetflowMax:F2}");
            else
            {
                bearCandidate = false;
                bearFailures.Add($"net>{RealtimeBearNetflowMax:F2}");
            }

            if (!double.IsNaN(objection) && objection >= RealtimeBearObjectionMin)
                bearReasons.Add($"obj>={RealtimeBearObjectionMin:F2}");
            else
            {
                bearCandidate = false;
                bearFailures.Add($"obj<{RealtimeBearObjectionMin:F2}");
            }

            if (emaColor <= RealtimeBearEmaColorMax)
                bearReasons.Add($"ema<={RealtimeBearEmaColorMax:F0}");
            else
            {
                bearCandidate = false;
                bearFailures.Add($"ema>{RealtimeBearEmaColorMax:F0}");
            }

            if (RealtimeBearUsePriceToBand)
            {
                if (!double.IsNaN(priceToBandVal) && priceToBandVal <= RealtimeBearPriceToBandMax)
                    bearReasons.Add($"ptb<={RealtimeBearPriceToBandMax:F2}");
                else
                {
                    bearCandidate = false;
                    bearFailures.Add($"ptb>{RealtimeBearPriceToBandMax:F2}");
                }
            }

            if (RealtimeBearScoreMin > 0.0)
            {
                if (bearScore >= RealtimeBearScoreMin)
                    bearReasons.Add($"score>={RealtimeBearScoreMin:F0}");
                else
                {
                    bearCandidate = false;
                    bearFailures.Add($"score<{RealtimeBearScoreMin:F0}");
                }
            }

            if (bearCandidate)
            {
                string reason = bearReasons.Count > 0 ? string.Join("|", bearReasons) : "criteria";
                return (RealtimeSignalState.Bear, $"BEAR|{reason}");
            }

            var flatParts = new List<string>();
            if (bullFailures.Count > 0)
                flatParts.Add("BULL_FAIL:" + string.Join("&", bullFailures));
            if (bearFailures.Count > 0)
                flatParts.Add("BEAR_FAIL:" + string.Join("&", bearFailures));
            string failures = flatParts.Count > 0 ? string.Join("|", flatParts) : string.Empty;

            if (RealtimeFlatTolerance >= 0 && Math.Abs(ocDiff) <= RealtimeFlatTolerance)
                return (RealtimeSignalState.Flat, $"FLAT|diff={ocDiff:F2}{(failures.Length > 0 ? "|" + failures : string.Empty)}");

            string dirTag = ocDiff > 0 ? "NONE_UP" : "NONE_DOWN";
            return (RealtimeSignalState.Flat, $"{dirTag}|diff={ocDiff:F2}{(failures.Length > 0 ? "|" + failures : string.Empty)}");
        }

        private static string GetRealtimeStateLabel(RealtimeSignalState state)
        {
            switch (state)
            {
                case RealtimeSignalState.Bull:
                    return "BULL";
                case RealtimeSignalState.Bear:
                    return "BEAR";
                default:
                    return "FLAT";
            }
        }

        private int ComputeScore(bool bullCross, bool bearCross, double stNow, double vpmUse, out bool rangeBreak)
        {
            int score = 0;
            rangeBreak = (!double.IsNaN(rangeMax) && Close[0] > rangeMax)
                     || (!double.IsNaN(rangeMin) && Close[0] < rangeMin);
            if (bullCross || bearCross) score += 2;
            bool emaStackBull = Close[0] > ema10Close[0] && ema10Close[0] > ema20Close[0];
            bool emaStackBear = Close[0] < ema10Close[0] && ema10Close[0] < ema20Close[0];
            if (emaStackBull || emaStackBear) score += 2;
            if (!double.IsNaN(vpmUse) && vpmUse > MinVpm) score += 1;
            if (adx14 != null && adx14.IsValidDataPoint(0) && adx14[0] > MinAdx) score += 1;
            if (volSma != null && volSma.IsValidDataPoint(0) && Volume[0] > volSma[0]) score += 1;
            if (rangeBreak) score += 2;
            return score;
        }

        private double BarsSinceCrossPrice() => Close[0];

        #region Logging
        private void InitLogger()
        {
            if (logInitialized || !EnableLogging)
                return;

            try
            {
                string folder = string.IsNullOrWhiteSpace(LogFolder)
                    ? Path.Combine(Globals.UserDataDir, "Indicator_logs")
                    : LogFolder;

                Directory.CreateDirectory(folder);

                string fileName = BuildLogFileBase() + ".csv";
                logPath = Path.Combine(folder, fileName);

                bool fileExists = File.Exists(logPath);
                bool isEmpty = !fileExists || new FileInfo(logPath).Length == 0;

                logWriter = new StreamWriter(logPath, append: true, encoding: Encoding.UTF8) { AutoFlush = true };
                logInitialized = true;

                if (isEmpty)
                {
                    var header = string.Join(",",
                        "ts_local",
                        "bar_time_utc",
                        "bar_index",
                        //"instrument",
                        "open",
                        "high",
                        "low",
                        "close",
                        "vpm",
                        "attract",
                        "objection",
                        "netflow",
                        "ema_color",
                        "momentum",
                        "supertrend",
                        "upper_band",
                        "lower_band",
                        "bull_cross",
                        "bear_cross",
                        "bull_cross_raw",
                        "bear_cross_raw",
                        "bull_reason",
                        "bear_reason",
                        "realtime_state",
                        "realtime_reason",
                        "regime",
                        "atr30",
                        "ema10",
                        "ema20",
                        "energy_ema_first",
                        "range_max",
                        "range_min",
                        "range_mid",
                        "range_os",
                        "range_count"
                        //"param_Sensitivity",
                        //"param_KeltnerLength",
                        //"param_RangeMinLength",
                        //"param_RangeWidthMult",
                        //"param_RangeAtrLen",
                        //"param_EnableLogging",
                        //"param_LogSignalsOnly",
                        //"param_HeartbeatEveryNBars"
                        //"param_LogFolder"
                    );

                    if (ExtendedLogging)
                    {
                        header += "," + string.Join(",",
                            //"vpm_smooth",
                            "score_bull",
                            "score_bear",
                            "adx",
                            "atr_ratio",
                            "ema_spread",
                            "price_to_band",
                            "momentum_ext",
                            "range_break",
                            "exit_long",
                            "exit_short"
                        );
                    }

                    SafeWriteLine(header);
                }

                Print("[CBASTestingIndicator3] Logging to: " + logPath);
            }
            catch (Exception ex)
            {
                Print("[CBASTestingIndicator3] Log init failed: " + ex.Message);
                logInitialized = false;
            }
        }

        private void FlushCachedBar()
        {
            if (!EnableLogging || !logInitialized || !hasCachedBar)
                return;

            bool includeRow = !LogSignalsOnly || cachedBullSignal || cachedBearSignal;
            if (!includeRow)
                return;

            int barsAgo = CurrentBar - cachedBarIndex;
            if (barsAgo < 0)
                barsAgo = 0;
            if (barsAgo > CurrentBar)
                barsAgo = CurrentBar;

            DateTime barTimeLocal = Time[barsAgo];

            if (EnableDebugPrints)
                WriteDebugCsv(cachedBarIndex, cachedBarOpen, cachedBarHigh, cachedBarLow, cachedBarClose);

            MaybeLogRow(cachedBarIndex,
                        barTimeLocal,
                        cachedBarOpen,
                        cachedBarHigh,
                        cachedBarLow,
                        cachedBarClose,
                        cachedBullSignal,
                        cachedBearSignal,
                        cachedBullRaw,
                        cachedBearRaw,
                        cachedBullReason,
                        cachedBearReason,
                        GetRealtimeStateLabel(cachedRealtimeState),
                        cachedRealtimeReason,
                        cachedBullScore,
                        cachedBearScore,
                        cachedBullRangeBreak,
                        cachedBearRangeBreak,
                        cachedSuperTrend,
                        cachedUpperBand,
                        cachedLowerBand,
                        cachedAttract,
                        cachedObjection,
                        cachedNetFlow,
                        cachedMomentumVal,
                        cachedEmaColorValue);
        }

        private void InitSignalDrawLogger()
        {
            if (signalDrawInitialized || !LogDrawnSignals)
                return;

            try
            {
                string folder = string.IsNullOrWhiteSpace(LogFolder)
                    ? Path.Combine(Globals.UserDataDir, "Indicator_logs")
                    : LogFolder;

                Directory.CreateDirectory(folder);

                string fileName = BuildLogFileBase() + "_signals.csv";
                signalDrawPath = Path.Combine(folder, fileName);

                bool fileExists = File.Exists(signalDrawPath);
                bool isEmpty = !fileExists || new FileInfo(signalDrawPath).Length == 0;

                signalDrawWriter = new StreamWriter(signalDrawPath, append: true, encoding: Encoding.UTF8)
                {
                    AutoFlush = true
                };
                signalDrawInitialized = true;

                if (isEmpty)
                {
                    lock (signalDrawLock)
                    {
                        signalDrawWriter.WriteLine("ts_local,bar_time_utc,bar_index,instrument,signal_type,price,supertrend,volume,netflow");
                    }
                }

                Print($"[CBASTestingIndicator3] Signal draw logging to: {signalDrawPath}");
            }
            catch (Exception ex)
            {
                Print($"[CBASTestingIndicator3] Signal draw log init failed: {ex.Message}");
                signalDrawInitialized = false;
            }
        }

        private void InitDebugLogger()
        {
            if (debugLogInitialized || !EnableDebugPrints)
                return;

            try
            {
                string folder = string.IsNullOrWhiteSpace(LogFolder)
                    ? Path.Combine(Globals.UserDataDir, "Indicator_logs")
                    : LogFolder;

                Directory.CreateDirectory(folder);

                debugLogPath = Path.Combine(folder, $"{Name}_{Instrument?.FullName}_debug.csv");
                bool fileExists = File.Exists(debugLogPath);
                debugLogWriter = new StreamWriter(debugLogPath, append: true, encoding: Encoding.UTF8)
                {
                    AutoFlush = true
                };
                debugLogInitialized = true;

                if (!fileExists)
                {
                    lock (debugLogLock)
                    {
                        debugLogWriter.WriteLine("bar_index,bar_open,bar_high,bar_low,bar_close");
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"[CBASTestingIndicator3] Debug log init failed: {ex.Message}");
                debugLogInitialized = false;
            }
        }

        private void WriteDebugCsv(int barIndex, double barOpen, double barHigh, double barLow, double barClose)
        {
            if (!EnableDebugPrints || !debugLogInitialized || debugLogWriter == null)
                return;

            try
            {
                lock (debugLogLock)
                {
                    debugLogWriter.WriteLine($"{barIndex},{CsvNum(barOpen)},{CsvNum(barHigh)},{CsvNum(barLow)},{CsvNum(barClose)}");
                }
            }
            catch (Exception ex)
            {
                Print($"[CBASTestingIndicator3] Debug log write failed: {ex.Message}");
            }
        }

        private void MaybeLogRow(int barIndex, DateTime barTimeLocal, double barOpen, double barHigh, double barLow, double barClose,
                                 bool bull, bool bear, bool bullRaw, bool bearRaw,
                                 string bullReason, string bearReason,
                                 string realtimeState, string realtimeReason,
                                 int bullScore, int bearScore,
                                 bool bullRangeBreak, bool bearRangeBreak,
                                 double superTrendNow, double upperBand, double lowerBand,
                                 double attract, double objection, double netFlow, double momentumVal, double emaColorValue)
        {
            if (!logInitialized) return;

            bool inBullRegime = !double.IsNaN(superTrendNow) && barClose > superTrendNow;
            bool inBearRegime = !double.IsNaN(superTrendNow) && barClose < superTrendNow;

            double atr30Val = atr30[0];
            double ema10 = ema10Close[0];
            double ema20 = ema20Close[0];
            double energy0 = (emaHighs != null && emaHighs.Length > 0) ? emaHighs[0][0] : double.NaN;
            double vpm = ComputeVpm();

            double rngMax = double.IsNaN(rangeMax) ? double.NaN : rangeMax;
            double rngMin = double.IsNaN(rangeMin) ? double.NaN : rangeMin;
            double rngMid = (double.IsNaN(rngMax) || double.IsNaN(rngMin)) ? double.NaN : (rngMax + rngMin) / 2.0;
            int rngCount = (rangeCountSeries != null && CurrentBar >= RangeMinLength - 1) ? rangeCountSeries[0] : int.MinValue;

            WriteCsv(
                barTimeUtc: ToBarTimeUtc(barTimeLocal),
                barTimeLocal: barTimeLocal,
                instrument: Instrument?.FullName ?? "",
                barIndex: barIndex,
                open: barOpen,
                high: barHigh,
                low: barLow,
                close: barClose,
                vpm: vpm,
                attract: attract,
                objection: objection,
                netFlow: netFlow,
                emaColorValue: emaColorValue,
                momentumVal: momentumVal,
                st: superTrendNow,
                ub: upperBand,
                lb: lowerBand,
                bull: bull,
                bear: bear,
                bullRaw: bullRaw,
                bearRaw: bearRaw,
                bullReason: bullReason,
                bearReason: bearReason,
                realtimeState: realtimeState,
                realtimeReason: realtimeReason,
                bullScore: bullScore,
                bearScore: bearScore,
                bullRangeBreak: bullRangeBreak,
                bearRangeBreak: bearRangeBreak,
                regime: inBullRegime ? "BULL" : inBearRegime ? "BEAR" : "FLAT",
                atr30: atr30Val,
                ema10: ema10,
                ema20: ema20,
                energyEmaFirst: energy0,
                rMax: rngMax,
                rMin: rngMin,
                rMid: rngMid,
                rOs: os,
                rCount: rngCount
            );
        }

        private void WriteCsv(
            DateTime barTimeUtc,
            DateTime barTimeLocal,
            string instrument,
            int barIndex,
            double open,
            double high,
            double low,
            double close,
            double vpm,
            double attract,
            double objection,
            double netFlow,
            double emaColorValue,
            double momentumVal,
            double st,
            double ub,
            double lb,
            bool bull,
            bool bear,
            bool bullRaw,
            bool bearRaw,
            string bullReason,
            string bearReason,
            string realtimeState,
            string realtimeReason,
            int bullScore,
            int bearScore,
            bool bullRangeBreak,
            bool bearRangeBreak,
            string regime,
            double atr30,
            double ema10,
            double ema20,
            double energyEmaFirst,
            double rMax,
            double rMin,
            double rMid,
            int rOs,
            int rCount
        )
        {
            if (!logInitialized) return;

            string line = string.Join(",",
                CsvEscape(barTimeLocal.ToString("yyyy-MM-dd HH:mm:ss.fff")),
                CsvEscape(barTimeUtc.ToString("o")),
                barIndex.ToString(),
                //CsvEscape(instrument ?? ""),
                CsvNum(open),
                CsvNum(high),
                CsvNum(low),
                CsvNum(close),
                CsvNum(vpm),
                CsvNum(attract),
                CsvNum(objection),
                CsvNum(netFlow),
                CsvNum(emaColorValue),
                CsvNum(momentumVal),
                CsvNum(st),
                CsvNum(ub),
                CsvNum(lb),
                bull ? "1" : "0",
                bear ? "1" : "0",
                bullRaw ? "1" : "0",
                bearRaw ? "1" : "0",
                CsvEscape(bullReason ?? ""),
                CsvEscape(bearReason ?? ""),
                CsvEscape(realtimeState ?? ""),
                CsvEscape(realtimeReason ?? ""),
                CsvEscape(regime ?? ""),
                CsvNum(atr30),
                CsvNum(ema10),
                CsvNum(ema20),
                CsvNum(energyEmaFirst),
                CsvNum(rMax),
                CsvNum(rMin),
                CsvNum(rMid),
                rOs.ToString(),
                (rCount == int.MinValue ? "" : rCount.ToString())
                //CsvNum(Sensitivity),
                //KeltnerLength.ToString(),
                //RangeMinLength.ToString(),
                //CsvNum(RangeWidthMult),
                //RangeAtrLen.ToString(),
                //(EnableLogging ? "1" : "0"),
                //(LogSignalsOnly ? "1" : "0"),
                //HeartbeatEveryNBars.ToString()
                //CsvEscape(string.IsNullOrEmpty(LogFolder) ? Path.Combine(Globals.UserDataDir, "Indicator_logs") : LogFolder)
            );

            if (ExtendedLogging)
            {
                double vpmRaw = vpm;
                double vpmSmooth = (UseSmoothedVpm && vpmEmaInd != null && CurrentBar >= VpmEmaSpan - 1) ? vpmEmaInd[0] : double.NaN;
                double vpmUse = UseSmoothedVpm ? vpmSmooth : vpmRaw;

                int scBull = bullScore;
                int scBear = bearScore;

                double adxVal = (adx14 != null && CurrentBar >= 14 - 1) ? adx14[0] : double.NaN;
                double atrRatioVal = (CurrentBar >= 30 - 1) ? atrRatio[0] : double.NaN;
                double emaSpreadVal = (CurrentBar >= 30 - 1) ? emaSpread[0] : double.NaN;
                double priceToBandVal = (priceToBand != null) ? priceToBand[0] : double.NaN;
                double momentumValExt = (CurrentBar >= MomentumLookback) ? momentum[0] : double.NaN;
                double emaColorExt = ComputeEmaColorCount();

                bool rangeBreak = bullRangeBreak || bearRangeBreak;

                bool exitLong = false, exitShort = false;
                if (ComputeExitSignals)
                {
                    exitLong = bear || (Close[0] < ema20Close[0]);
                    exitShort = bull || (Close[0] > ema20Close[0]);
                }

                line += "," + string.Join(",",
                    CsvNum(vpmSmooth),
                    scBull.ToString(),
                    scBear.ToString(),
                    CsvNum(adxVal),
                    CsvNum(atrRatioVal),
                    CsvNum(emaSpreadVal),
                    CsvNum(priceToBandVal),
                    CsvNum(momentumValExt),
                    (rangeBreak ? "1" : "0"),
                    (exitLong ? "1" : "0"),
                    (exitShort ? "1" : "0")
                );
            }

            SafeWriteLine(line);
        }

        private void SafeWriteLine(string line)
        {
            if (!logInitialized || logWriter == null) return;
            try
            {
                lock (logLock)
                {
                    logWriter.WriteLine(line);
                }
            }
            catch (Exception ex)
            {
                Print($"[CBASTestingIndicator3] Log write failed: {ex.Message}");
            }
        }

        private void LogDrawnSignal(string signalType, double price, double superTrendValue, double netFlow)
        {
            if (!LogDrawnSignals || !signalDrawInitialized || signalDrawWriter == null)
                return;

            if (string.Equals(signalType, "BULL", StringComparison.OrdinalIgnoreCase))
            {
                if (lastLoggedBullSignalBar == CurrentBar)
                    return;
            }
            else if (string.Equals(signalType, "BEAR", StringComparison.OrdinalIgnoreCase))
            {
                if (lastLoggedBearSignalBar == CurrentBar)
                    return;
            }

            try
            {
                string line = string.Join(",",
                    CsvEscape(Time[0].ToString("yyyy-MM-dd HH:mm:ss.fff")),
                    CsvEscape(ToBarTimeUtc(Time[0]).ToString("o")),
                    CurrentBar.ToString(),
                    CsvEscape(Instrument?.FullName ?? string.Empty),
                    CsvEscape(signalType),
                    CsvNum(price),
                    CsvNum(superTrendValue),
                    CsvNum(Volume[0]),
                    CsvNum(netFlow));

                lock (signalDrawLock)
                {
                    signalDrawWriter.WriteLine(line);
                }

                if (string.Equals(signalType, "BULL", StringComparison.OrdinalIgnoreCase))
                    lastLoggedBullSignalBar = CurrentBar;
                else if (string.Equals(signalType, "BEAR", StringComparison.OrdinalIgnoreCase))
                    lastLoggedBearSignalBar = CurrentBar;
            }
            catch (Exception ex)
            {
                Print($"[CBASTestingIndicator3] Signal draw log write failed: {ex.Message}");
            }
        }

        private string BuildLogFileBase()
        {
            string nameSegment = MakeSafeFileSegment(Name ?? "Indicator");
            string instrumentSegment = MakeSafeFileSegment(Instrument?.FullName ?? "NA");
            string instanceSegment = MakeSafeFileSegment(string.IsNullOrWhiteSpace(InstanceId) ? null : InstanceId);

            string baseName = $"{nameSegment}_{instrumentSegment}";
            if (!string.IsNullOrEmpty(instanceSegment))
                baseName += $"_{instanceSegment}";

            return baseName;
        }

        private static string MakeSafeFileSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "NA";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                sb.Append(invalid.Contains(c) ? '_' : c);
            }
            return sb.ToString();
        }

        private static string CsvEscape(string s)
        {
            if (s == null) return "";
            bool needs = s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r");
            if (!needs) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        private static string CsvNum(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "";
            return v.ToString("G17");
        }
        private double ComputeVpm(int barsAgo = 0)
        {
            try
            {
                if (barsAgo < 0)
                    barsAgo = 0;

                if (BarsPeriod?.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value > 0)
                {
                    double minutes = BarsPeriod.Value;
                    return minutes > 0 && CurrentBar >= barsAgo
                        ? Volume[barsAgo] / minutes
                        : double.NaN;
                }

                int idxNext = barsAgo + 1;
                if (CurrentBar >= idxNext)
                {
                    double minutes = (Time[barsAgo] - Time[idxNext]).TotalMinutes;
                    return minutes > 0 ? Volume[barsAgo] / minutes : double.NaN;
                }
            }
            catch
            {
            }
            return double.NaN;
        }
        private DateTime ToBarTimeUtc(DateTime barTimeLocal)
        {
            try
            {
                var tz = Bars?.TradingHours?.TimeZoneInfo ?? TimeZoneInfo.Utc;
                if (tz.Id == TimeZoneInfo.Utc.Id) return barTimeLocal;
                return TimeZoneInfo.ConvertTimeToUtc(barTimeLocal, tz);
            }
            catch
            {
                return barTimeLocal.ToUniversalTime();
            }
        }
        #endregion

        #region Helpers
        private int FindPlot(string name)
        {
            for (int i = 0; i < Plots.Length; i++)
                if (string.Equals(Plots[i].Name, name, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        private void SetPlotVal(int idx, double v)
        {
            if (idx >= 0)
                Values[idx][0] = v;
        }

        private void SetPlotBrush(int idx, Brush b)
        {
            if (idx >= 0)
                PlotBrushes[idx][0] = b;
        }

        private double ComputeEmaColorCount()
        {
            if (emaHighs == null || emaHighs.Length == 0)
                return double.IsNaN(lastEmaColorCount) ? 0.0 : lastEmaColorCount;

            bool allValid = true;
            int greenCount = 0;

            for (int i = 0; i < emaHighs.Length; i++)
            {
                var ema = emaHighs[i];
                if (ema == null || double.IsNaN(ema[0]))
                {
                    allValid = false;
                    break;
                }

                double emaValue = ema[0];
                if (!double.IsNaN(emaValue) && Close[0] >= emaValue)
                    greenCount++;
            }

            if (allValid)
            {
                lastEmaColorCount = greenCount;
                return greenCount;
            }

            return double.IsNaN(lastEmaColorCount) ? greenCount : lastEmaColorCount;
        }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private CBASTestingIndicator3[] cacheCBASTestingIndicator3;
		public CBASTestingIndicator3 CBASTestingIndicator3(bool showExitLabels, double exitLabelAtrOffset, bool useRegimeStability, int regimeStabilityBars, bool useScoringFilter, int scoreThreshold, bool useSmoothedVpm, int vpmEmaSpan, double minVpm, int minAdx, int momentumLookback, bool extendedLogging, bool computeExitSignals, double exitProfitAtrMult, string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder, bool scaleOscillatorToATR, double oscAtrMult, bool logDrawnSignals, bool enableDebugPrints, bool colorBarsByTrend, double realtimeBullNetflowMin, double realtimeBullObjectionMax, double realtimeBullEmaColorMin, bool realtimeBullUseAttract, double realtimeBullAttractMin, double realtimeBullScoreMin, double realtimeBearNetflowMax, double realtimeBearObjectionMin, double realtimeBearEmaColorMax, bool realtimeBearUsePriceToBand, double realtimeBearPriceToBandMax, double realtimeBearScoreMin, double realtimeFlatTolerance, bool showRealtimeStatePlot, bool plotRealtimeSignals, int flipConfirmationBars)
		{
			return CBASTestingIndicator3(Input, showExitLabels, exitLabelAtrOffset, useRegimeStability, regimeStabilityBars, useScoringFilter, scoreThreshold, useSmoothedVpm, vpmEmaSpan, minVpm, minAdx, momentumLookback, extendedLogging, computeExitSignals, exitProfitAtrMult, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder, scaleOscillatorToATR, oscAtrMult, logDrawnSignals, enableDebugPrints, colorBarsByTrend, realtimeBullNetflowMin, realtimeBullObjectionMax, realtimeBullEmaColorMin, realtimeBullUseAttract, realtimeBullAttractMin, realtimeBullScoreMin, realtimeBearNetflowMax, realtimeBearObjectionMin, realtimeBearEmaColorMax, realtimeBearUsePriceToBand, realtimeBearPriceToBandMax, realtimeBearScoreMin, realtimeFlatTolerance, showRealtimeStatePlot, plotRealtimeSignals, flipConfirmationBars);
		}

		public CBASTestingIndicator3 CBASTestingIndicator3(ISeries<double> input, bool showExitLabels, double exitLabelAtrOffset, bool useRegimeStability, int regimeStabilityBars, bool useScoringFilter, int scoreThreshold, bool useSmoothedVpm, int vpmEmaSpan, double minVpm, int minAdx, int momentumLookback, bool extendedLogging, bool computeExitSignals, double exitProfitAtrMult, string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder, bool scaleOscillatorToATR, double oscAtrMult, bool logDrawnSignals, bool enableDebugPrints, bool colorBarsByTrend, double realtimeBullNetflowMin, double realtimeBullObjectionMax, double realtimeBullEmaColorMin, bool realtimeBullUseAttract, double realtimeBullAttractMin, double realtimeBullScoreMin, double realtimeBearNetflowMax, double realtimeBearObjectionMin, double realtimeBearEmaColorMax, bool realtimeBearUsePriceToBand, double realtimeBearPriceToBandMax, double realtimeBearScoreMin, double realtimeFlatTolerance, bool showRealtimeStatePlot, bool plotRealtimeSignals, int flipConfirmationBars)
		{
			if (cacheCBASTestingIndicator3 != null)
				for (int idx = 0; idx < cacheCBASTestingIndicator3.Length; idx++)
					if (cacheCBASTestingIndicator3[idx] != null && cacheCBASTestingIndicator3[idx].ShowExitLabels == showExitLabels && cacheCBASTestingIndicator3[idx].ExitLabelAtrOffset == exitLabelAtrOffset && cacheCBASTestingIndicator3[idx].UseRegimeStability == useRegimeStability && cacheCBASTestingIndicator3[idx].RegimeStabilityBars == regimeStabilityBars && cacheCBASTestingIndicator3[idx].UseScoringFilter == useScoringFilter && cacheCBASTestingIndicator3[idx].ScoreThreshold == scoreThreshold && cacheCBASTestingIndicator3[idx].UseSmoothedVpm == useSmoothedVpm && cacheCBASTestingIndicator3[idx].VpmEmaSpan == vpmEmaSpan && cacheCBASTestingIndicator3[idx].MinVpm == minVpm && cacheCBASTestingIndicator3[idx].MinAdx == minAdx && cacheCBASTestingIndicator3[idx].MomentumLookback == momentumLookback && cacheCBASTestingIndicator3[idx].ExtendedLogging == extendedLogging && cacheCBASTestingIndicator3[idx].ComputeExitSignals == computeExitSignals && cacheCBASTestingIndicator3[idx].ExitProfitAtrMult == exitProfitAtrMult && cacheCBASTestingIndicator3[idx].InstanceId == instanceId && cacheCBASTestingIndicator3[idx].Sensitivity == sensitivity && cacheCBASTestingIndicator3[idx].EmaEnergy == emaEnergy && cacheCBASTestingIndicator3[idx].KeltnerLength == keltnerLength && cacheCBASTestingIndicator3[idx].AtrLength == atrLength && cacheCBASTestingIndicator3[idx].RangeMinLength == rangeMinLength && cacheCBASTestingIndicator3[idx].RangeWidthMult == rangeWidthMult && cacheCBASTestingIndicator3[idx].RangeAtrLen == rangeAtrLen && cacheCBASTestingIndicator3[idx].EnableLogging == enableLogging && cacheCBASTestingIndicator3[idx].LogSignalsOnly == logSignalsOnly && cacheCBASTestingIndicator3[idx].HeartbeatEveryNBars == heartbeatEveryNBars && cacheCBASTestingIndicator3[idx].LogFolder == logFolder && cacheCBASTestingIndicator3[idx].ScaleOscillatorToATR == scaleOscillatorToATR && cacheCBASTestingIndicator3[idx].OscAtrMult == oscAtrMult && cacheCBASTestingIndicator3[idx].LogDrawnSignals == logDrawnSignals && cacheCBASTestingIndicator3[idx].EnableDebugPrints == enableDebugPrints && cacheCBASTestingIndicator3[idx].ColorBarsByTrend == colorBarsByTrend && cacheCBASTestingIndicator3[idx].RealtimeBullNetflowMin == realtimeBullNetflowMin && cacheCBASTestingIndicator3[idx].RealtimeBullObjectionMax == realtimeBullObjectionMax && cacheCBASTestingIndicator3[idx].RealtimeBullEmaColorMin == realtimeBullEmaColorMin && cacheCBASTestingIndicator3[idx].RealtimeBullUseAttract == realtimeBullUseAttract && cacheCBASTestingIndicator3[idx].RealtimeBullAttractMin == realtimeBullAttractMin && cacheCBASTestingIndicator3[idx].RealtimeBullScoreMin == realtimeBullScoreMin && cacheCBASTestingIndicator3[idx].RealtimeBearNetflowMax == realtimeBearNetflowMax && cacheCBASTestingIndicator3[idx].RealtimeBearObjectionMin == realtimeBearObjectionMin && cacheCBASTestingIndicator3[idx].RealtimeBearEmaColorMax == realtimeBearEmaColorMax && cacheCBASTestingIndicator3[idx].RealtimeBearUsePriceToBand == realtimeBearUsePriceToBand && cacheCBASTestingIndicator3[idx].RealtimeBearPriceToBandMax == realtimeBearPriceToBandMax && cacheCBASTestingIndicator3[idx].RealtimeBearScoreMin == realtimeBearScoreMin && cacheCBASTestingIndicator3[idx].RealtimeFlatTolerance == realtimeFlatTolerance && cacheCBASTestingIndicator3[idx].ShowRealtimeStatePlot == showRealtimeStatePlot && cacheCBASTestingIndicator3[idx].PlotRealtimeSignals == plotRealtimeSignals && cacheCBASTestingIndicator3[idx].FlipConfirmationBars == flipConfirmationBars && cacheCBASTestingIndicator3[idx].EqualsInput(input))
						return cacheCBASTestingIndicator3[idx];
			return CacheIndicator<CBASTestingIndicator3>(new CBASTestingIndicator3(){ ShowExitLabels = showExitLabels, ExitLabelAtrOffset = exitLabelAtrOffset, UseRegimeStability = useRegimeStability, RegimeStabilityBars = regimeStabilityBars, UseScoringFilter = useScoringFilter, ScoreThreshold = scoreThreshold, UseSmoothedVpm = useSmoothedVpm, VpmEmaSpan = vpmEmaSpan, MinVpm = minVpm, MinAdx = minAdx, MomentumLookback = momentumLookback, ExtendedLogging = extendedLogging, ComputeExitSignals = computeExitSignals, ExitProfitAtrMult = exitProfitAtrMult, InstanceId = instanceId, Sensitivity = sensitivity, EmaEnergy = emaEnergy, KeltnerLength = keltnerLength, AtrLength = atrLength, RangeMinLength = rangeMinLength, RangeWidthMult = rangeWidthMult, RangeAtrLen = rangeAtrLen, EnableLogging = enableLogging, LogSignalsOnly = logSignalsOnly, HeartbeatEveryNBars = heartbeatEveryNBars, LogFolder = logFolder, ScaleOscillatorToATR = scaleOscillatorToATR, OscAtrMult = oscAtrMult, LogDrawnSignals = logDrawnSignals, EnableDebugPrints = enableDebugPrints, ColorBarsByTrend = colorBarsByTrend, RealtimeBullNetflowMin = realtimeBullNetflowMin, RealtimeBullObjectionMax = realtimeBullObjectionMax, RealtimeBullEmaColorMin = realtimeBullEmaColorMin, RealtimeBullUseAttract = realtimeBullUseAttract, RealtimeBullAttractMin = realtimeBullAttractMin, RealtimeBullScoreMin = realtimeBullScoreMin, RealtimeBearNetflowMax = realtimeBearNetflowMax, RealtimeBearObjectionMin = realtimeBearObjectionMin, RealtimeBearEmaColorMax = realtimeBearEmaColorMax, RealtimeBearUsePriceToBand = realtimeBearUsePriceToBand, RealtimeBearPriceToBandMax = realtimeBearPriceToBandMax, RealtimeBearScoreMin = realtimeBearScoreMin, RealtimeFlatTolerance = realtimeFlatTolerance, ShowRealtimeStatePlot = showRealtimeStatePlot, PlotRealtimeSignals = plotRealtimeSignals, FlipConfirmationBars = flipConfirmationBars }, input, ref cacheCBASTestingIndicator3);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.CBASTestingIndicator3 CBASTestingIndicator3(bool showExitLabels, double exitLabelAtrOffset, bool useRegimeStability, int regimeStabilityBars, bool useScoringFilter, int scoreThreshold, bool useSmoothedVpm, int vpmEmaSpan, double minVpm, int minAdx, int momentumLookback, bool extendedLogging, bool computeExitSignals, double exitProfitAtrMult, string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder, bool scaleOscillatorToATR, double oscAtrMult, bool logDrawnSignals, bool enableDebugPrints, bool colorBarsByTrend, double realtimeBullNetflowMin, double realtimeBullObjectionMax, double realtimeBullEmaColorMin, bool realtimeBullUseAttract, double realtimeBullAttractMin, double realtimeBullScoreMin, double realtimeBearNetflowMax, double realtimeBearObjectionMin, double realtimeBearEmaColorMax, bool realtimeBearUsePriceToBand, double realtimeBearPriceToBandMax, double realtimeBearScoreMin, double realtimeFlatTolerance, bool showRealtimeStatePlot, bool plotRealtimeSignals, int flipConfirmationBars)
		{
			return indicator.CBASTestingIndicator3(Input, showExitLabels, exitLabelAtrOffset, useRegimeStability, regimeStabilityBars, useScoringFilter, scoreThreshold, useSmoothedVpm, vpmEmaSpan, minVpm, minAdx, momentumLookback, extendedLogging, computeExitSignals, exitProfitAtrMult, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder, scaleOscillatorToATR, oscAtrMult, logDrawnSignals, enableDebugPrints, colorBarsByTrend, realtimeBullNetflowMin, realtimeBullObjectionMax, realtimeBullEmaColorMin, realtimeBullUseAttract, realtimeBullAttractMin, realtimeBullScoreMin, realtimeBearNetflowMax, realtimeBearObjectionMin, realtimeBearEmaColorMax, realtimeBearUsePriceToBand, realtimeBearPriceToBandMax, realtimeBearScoreMin, realtimeFlatTolerance, showRealtimeStatePlot, plotRealtimeSignals, flipConfirmationBars);
		}

		public Indicators.CBASTestingIndicator3 CBASTestingIndicator3(ISeries<double> input , bool showExitLabels, double exitLabelAtrOffset, bool useRegimeStability, int regimeStabilityBars, bool useScoringFilter, int scoreThreshold, bool useSmoothedVpm, int vpmEmaSpan, double minVpm, int minAdx, int momentumLookback, bool extendedLogging, bool computeExitSignals, double exitProfitAtrMult, string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder, bool scaleOscillatorToATR, double oscAtrMult, bool logDrawnSignals, bool enableDebugPrints, bool colorBarsByTrend, double realtimeBullNetflowMin, double realtimeBullObjectionMax, double realtimeBullEmaColorMin, bool realtimeBullUseAttract, double realtimeBullAttractMin, double realtimeBullScoreMin, double realtimeBearNetflowMax, double realtimeBearObjectionMin, double realtimeBearEmaColorMax, bool realtimeBearUsePriceToBand, double realtimeBearPriceToBandMax, double realtimeBearScoreMin, double realtimeFlatTolerance, bool showRealtimeStatePlot, bool plotRealtimeSignals, int flipConfirmationBars)
		{
			return indicator.CBASTestingIndicator3(input, showExitLabels, exitLabelAtrOffset, useRegimeStability, regimeStabilityBars, useScoringFilter, scoreThreshold, useSmoothedVpm, vpmEmaSpan, minVpm, minAdx, momentumLookback, extendedLogging, computeExitSignals, exitProfitAtrMult, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder, scaleOscillatorToATR, oscAtrMult, logDrawnSignals, enableDebugPrints, colorBarsByTrend, realtimeBullNetflowMin, realtimeBullObjectionMax, realtimeBullEmaColorMin, realtimeBullUseAttract, realtimeBullAttractMin, realtimeBullScoreMin, realtimeBearNetflowMax, realtimeBearObjectionMin, realtimeBearEmaColorMax, realtimeBearUsePriceToBand, realtimeBearPriceToBandMax, realtimeBearScoreMin, realtimeFlatTolerance, showRealtimeStatePlot, plotRealtimeSignals, flipConfirmationBars);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.CBASTestingIndicator3 CBASTestingIndicator3(bool showExitLabels, double exitLabelAtrOffset, bool useRegimeStability, int regimeStabilityBars, bool useScoringFilter, int scoreThreshold, bool useSmoothedVpm, int vpmEmaSpan, double minVpm, int minAdx, int momentumLookback, bool extendedLogging, bool computeExitSignals, double exitProfitAtrMult, string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder, bool scaleOscillatorToATR, double oscAtrMult, bool logDrawnSignals, bool enableDebugPrints, bool colorBarsByTrend, double realtimeBullNetflowMin, double realtimeBullObjectionMax, double realtimeBullEmaColorMin, bool realtimeBullUseAttract, double realtimeBullAttractMin, double realtimeBullScoreMin, double realtimeBearNetflowMax, double realtimeBearObjectionMin, double realtimeBearEmaColorMax, bool realtimeBearUsePriceToBand, double realtimeBearPriceToBandMax, double realtimeBearScoreMin, double realtimeFlatTolerance, bool showRealtimeStatePlot, bool plotRealtimeSignals, int flipConfirmationBars)
		{
			return indicator.CBASTestingIndicator3(Input, showExitLabels, exitLabelAtrOffset, useRegimeStability, regimeStabilityBars, useScoringFilter, scoreThreshold, useSmoothedVpm, vpmEmaSpan, minVpm, minAdx, momentumLookback, extendedLogging, computeExitSignals, exitProfitAtrMult, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder, scaleOscillatorToATR, oscAtrMult, logDrawnSignals, enableDebugPrints, colorBarsByTrend, realtimeBullNetflowMin, realtimeBullObjectionMax, realtimeBullEmaColorMin, realtimeBullUseAttract, realtimeBullAttractMin, realtimeBullScoreMin, realtimeBearNetflowMax, realtimeBearObjectionMin, realtimeBearEmaColorMax, realtimeBearUsePriceToBand, realtimeBearPriceToBandMax, realtimeBearScoreMin, realtimeFlatTolerance, showRealtimeStatePlot, plotRealtimeSignals, flipConfirmationBars);
		}

		public Indicators.CBASTestingIndicator3 CBASTestingIndicator3(ISeries<double> input , bool showExitLabels, double exitLabelAtrOffset, bool useRegimeStability, int regimeStabilityBars, bool useScoringFilter, int scoreThreshold, bool useSmoothedVpm, int vpmEmaSpan, double minVpm, int minAdx, int momentumLookback, bool extendedLogging, bool computeExitSignals, double exitProfitAtrMult, string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder, bool scaleOscillatorToATR, double oscAtrMult, bool logDrawnSignals, bool enableDebugPrints, bool colorBarsByTrend, double realtimeBullNetflowMin, double realtimeBullObjectionMax, double realtimeBullEmaColorMin, bool realtimeBullUseAttract, double realtimeBullAttractMin, double realtimeBullScoreMin, double realtimeBearNetflowMax, double realtimeBearObjectionMin, double realtimeBearEmaColorMax, bool realtimeBearUsePriceToBand, double realtimeBearPriceToBandMax, double realtimeBearScoreMin, double realtimeFlatTolerance, bool showRealtimeStatePlot, bool plotRealtimeSignals, int flipConfirmationBars)
		{
			return indicator.CBASTestingIndicator3(input, showExitLabels, exitLabelAtrOffset, useRegimeStability, regimeStabilityBars, useScoringFilter, scoreThreshold, useSmoothedVpm, vpmEmaSpan, minVpm, minAdx, momentumLookback, extendedLogging, computeExitSignals, exitProfitAtrMult, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder, scaleOscillatorToATR, oscAtrMult, logDrawnSignals, enableDebugPrints, colorBarsByTrend, realtimeBullNetflowMin, realtimeBullObjectionMax, realtimeBullEmaColorMin, realtimeBullUseAttract, realtimeBullAttractMin, realtimeBullScoreMin, realtimeBearNetflowMax, realtimeBearObjectionMin, realtimeBearEmaColorMax, realtimeBearUsePriceToBand, realtimeBearPriceToBandMax, realtimeBearScoreMin, realtimeFlatTolerance, showRealtimeStatePlot, plotRealtimeSignals, flipConfirmationBars);
		}
	}
}

#endregion
