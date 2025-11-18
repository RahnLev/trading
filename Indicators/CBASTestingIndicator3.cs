using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;                   // MessageBox
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using NinjaTrader.Core;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
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
        [Display(Name = "Debug: log SignalCheck", Order = 32, GroupName = "Logging")]
        public bool DebugLogSignalCheck { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Color Bars By Trend", Order = 42, GroupName = "Visual")]
        public bool ColorBarsByTrend { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Spread Filter", Order = 43, GroupName = "Visual")]
        public bool UseEmaSpreadFilter { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Min EMA Spread (ATR ratio)", Order = 44, GroupName = "Visual")]
        public double MinEmaSpread { get; set; } = 0.0005;

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
        private bool lastRenderWasPricePanel = true;
        private int plotRealtimeState;

        private bool bull;
        private bool bear;
        
        // Cache the FINAL signal state that is returned to strategies and drawn on chart
        // This ensures chart triangles and strategy signals are always in perfect sync
        private bool cachedFinalBullSignal = false;
        private bool cachedFinalBearSignal = false;
        
        // Expose bull/bear signals for strategy access - ALWAYS return the cached final state
        public bool BullSignal => cachedFinalBullSignal;
        public bool BearSignal => cachedFinalBearSignal;
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

        // Debug throttles for SignalCheck logging
        private bool dbgLastBull = false, dbgLastBear = false, dbgLastFirstTick = false;
        private int dbgLastBar = -1;
        private DateTime dbgLastPrintUtc = DateTime.MinValue;

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
        private string runTimestamp = string.Empty; // Timestamp for this run to make logs unique
        private readonly object signalDrawLock = new object();
        private bool signalDrawInitialized = false;
        private string signalDrawPath = null;
        
        // Debug logging for signal plot/log tracking
        private StreamWriter debugSignalLogWriter;
        private string debugSignalLogPath = null;
        private bool debugSignalLogInitialized = false;
        private readonly object debugSignalLogLock = new object();
        
        // Debug logging
        private StreamWriter debugLogWriter;
        private bool debugLogInitialized = false;
        private readonly object debugLogLock = new object();
        private bool firstBarLogged = false;

        // Previous bar values for trend comparison when both BULL and BEAR are true
        private double prevBarClose = double.NaN;
        private double prevBarAttract = double.NaN;
        private double prevBarNetFlow = double.NaN;
        private int prevBarEmaColor = -1;

        // Prevent multiple draws on same bar (per OnBarUpdate call)
        private int lastDrawnBarIndex = -1;
        private bool bullDrawnThisBar = false;
        private bool bearDrawnThisBar = false;
        private bool signalDecidedThisBar = false;
        private bool decidedBullSignal = false;
        private bool decidedBearSignal = false;
        private int lastBreakpointLoggedBar = -1;

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
        private bool signalLoggedThisBar = false; // Track if we've logged a signal on this bar to prevent both BULL and BEAR
        
        // Store signals during bar to log only after confirmation (1-2 bars later)
        private bool pendingBullSignal = false;
        private bool pendingBearSignal = false;
        private int pendingBullSignalBar = -1;
        private int pendingBearSignalBar = -1;
        private double pendingBullPrice = double.NaN;
        private double pendingBearPrice = double.NaN;
        private double pendingBullSuperTrend = double.NaN;
        private double pendingBearSuperTrend = double.NaN;
        private double pendingBullNetFlow = double.NaN;
        private double pendingBearNetFlow = double.NaN;
        private string pendingBullEmaColorHistory = string.Empty;
        private string pendingBearEmaColorHistory = string.Empty;
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
                {
                    // One or more plot indices are invalid
                }

                // DON'T initialize logging here - properties may not be set correctly when used from strategy
                // Logging will be initialized in OnBarUpdate when properties are guaranteed to be set

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
                        
                        debugLogWriter?.Flush();
                        debugLogWriter?.Dispose();
                        debugLogWriter = null;
                        debugLogInitialized = false;
                    }
                    lock (signalDrawLock)
                    {
                        signalDrawWriter?.Flush();
                        signalDrawWriter?.Dispose();
                        signalDrawWriter = null;
                        signalDrawInitialized = false;
                    }
                    lock (debugSignalLogLock)
                    {
                        debugSignalLogWriter?.Flush();
                        debugSignalLogWriter?.Dispose();
                        debugSignalLogWriter = null;
                        debugSignalLogInitialized = false;
                    }
                }
                catch { /* ignore */ }
            }
        }

        protected override void OnBarUpdate()
        {
            // Initialize logging as early as possible (before any early returns)
            if (EnableLogging && !logInitialized)
            {
                InitLogger();
            }

            if (LogDrawnSignals && !signalDrawInitialized)
            {
                InitSignalDrawLogger();
            }

            // Log property values once to verify they're set correctly
            if (!firstBarLogged)
            {
                firstBarLogged = true;
                var propertiesOutput = new System.Text.StringBuilder();
                propertiesOutput.AppendLine("[CBASTestingIndicator3] OnBarUpdate FIRST BAR: ========== COMPREHENSIVE PROPERTY VERIFICATION ==========");
                propertiesOutput.AppendLine("[CBASTestingIndicator3] OnBarUpdate FIRST BAR: --- Parameters ---");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: Sensitivity = {Sensitivity}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: EmaEnergy = {EmaEnergy}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: KeltnerLength = {KeltnerLength}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: AtrLength = {AtrLength}");
                propertiesOutput.AppendLine("[CBASTestingIndicator3] OnBarUpdate FIRST BAR: --- Range Detector ---");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: RangeMinLength = {RangeMinLength}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: RangeWidthMult = {RangeWidthMult}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: RangeAtrLen = {RangeAtrLen}");
                propertiesOutput.AppendLine("[CBASTestingIndicator3] OnBarUpdate FIRST BAR: --- Filters ---");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: UseRegimeStability = {UseRegimeStability}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: RegimeStabilityBars = {RegimeStabilityBars}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: UseScoringFilter = {UseScoringFilter}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: ScoreThreshold = {ScoreThreshold}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: UseSmoothedVpm = {UseSmoothedVpm}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: VpmEmaSpan = {VpmEmaSpan}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: MinVpm = {MinVpm}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: MinAdx = {MinAdx}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: MomentumLookback = {MomentumLookback}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: ComputeExitSignals = {ComputeExitSignals}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: ExitProfitAtrMult = {ExitProfitAtrMult}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: ShowExitLabels = {ShowExitLabels}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: ExitLabelAtrOffset = {ExitLabelAtrOffset}");
                propertiesOutput.AppendLine("[CBASTestingIndicator3] OnBarUpdate FIRST BAR: --- Realtime Filters ---");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: RealtimeBullNetflowMin = {RealtimeBullNetflowMin}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: RealtimeBullObjectionMax = {RealtimeBullObjectionMax}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: RealtimeBullEmaColorMin = {RealtimeBullEmaColorMin}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: RealtimeBullUseAttract = {RealtimeBullUseAttract}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: RealtimeBullAttractMin = {RealtimeBullAttractMin}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: RealtimeBullScoreMin = {RealtimeBullScoreMin}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: RealtimeBearNetflowMax = {RealtimeBearNetflowMax}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: RealtimeBearObjectionMin = {RealtimeBearObjectionMin}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: RealtimeBearEmaColorMax = {RealtimeBearEmaColorMax}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: RealtimeBearUsePriceToBand = {RealtimeBearUsePriceToBand}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: RealtimeBearPriceToBandMax = {RealtimeBearPriceToBandMax}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: RealtimeBearScoreMin = {RealtimeBearScoreMin}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: RealtimeFlatTolerance = {RealtimeFlatTolerance}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: ShowRealtimeStatePlot = {ShowRealtimeStatePlot}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: PlotRealtimeSignals = {PlotRealtimeSignals}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: FlipConfirmationBars = {FlipConfirmationBars}");
                propertiesOutput.AppendLine("[CBASTestingIndicator3] OnBarUpdate FIRST BAR: --- Plots ---");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: ScaleOscillatorToATR = {ScaleOscillatorToATR}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: OscAtrMult = {OscAtrMult}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: ColorBarsByTrend = {ColorBarsByTrend}");
                propertiesOutput.AppendLine("[CBASTestingIndicator3] OnBarUpdate FIRST BAR: --- Logging ---");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: EnableLogging = {EnableLogging}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: LogSignalsOnly = {LogSignalsOnly}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: HeartbeatEveryNBars = {HeartbeatEveryNBars}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: LogFolder = {LogFolder ?? "null"}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: LogDrawnSignals = {LogDrawnSignals}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: DebugLogSignalCheck = {DebugLogSignalCheck}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: ExtendedLogging = {ExtendedLogging}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: InstanceId = {InstanceId ?? "null"}");
                propertiesOutput.AppendLine("[CBASTestingIndicator3] OnBarUpdate FIRST BAR: --- Internal State ---");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: logInitialized = {logInitialized}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: signalDrawInitialized = {signalDrawInitialized}");
                propertiesOutput.AppendLine($"[CBASTestingIndicator3] OnBarUpdate FIRST BAR: Globals.UserDataDir = {Globals.UserDataDir ?? "null"}");
                propertiesOutput.AppendLine("[CBASTestingIndicator3] OnBarUpdate FIRST BAR: ==========================================");
                
                // Print to output window
                Print(propertiesOutput.ToString());
                
                // Save to file
                try
                {
                    string folder = @"c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\indicators_log";
                    Directory.CreateDirectory(folder);
                    string filePath = Path.Combine(folder, "indicator_properties.txt");
                    File.WriteAllText(filePath, propertiesOutput.ToString());
                }
                catch (Exception ex)
                {
                    Print($"[CBASTestingIndicator3] Failed to save properties to file: {ex.Message}");
                }
            }

            // Initialize logging as early as possible (before any early returns)
            if (EnableLogging && !logInitialized)
            {
                Print($"[CBASTestingIndicator3] OnBarUpdate: Calling InitLogger. CurrentBar={CurrentBar}, EnableLogging={EnableLogging}");
                InitLogger();  // STEP INTO HERE
                // After InitLogger returns, check:
                // - logInitialized (should be true)
                // - logPath (should not be null)
                // - logWriter (should not be null)
                if (!logInitialized)
                {
                    Print($"[CBASTestingIndicator3] OnBarUpdate: ERROR - InitLogger returned but logInitialized is still false!");
                }
            }
            // Safety check: if EnableLogging is true but logInitialized is still false after InitLogger, try again periodically
            if (EnableLogging && !logInitialized && CurrentBar % 100 == 0 && CurrentBar > 0)
            {
                Print($"[CBASTestingIndicator3] OnBarUpdate: WARNING - EnableLogging=true but logInitialized=false after InitLogger, attempting to re-initialize. CurrentBar={CurrentBar}");
                InitLogger();
            }
            // Safety check: if logWriter is null but logInitialized is true, something went wrong - try to re-initialize
            if (EnableLogging && logInitialized && logWriter == null && CurrentBar % 100 == 0 && CurrentBar > 0)
            {
                Print($"[CBASTestingIndicator3] OnBarUpdate: WARNING - logInitialized=true but logWriter=null, attempting to re-initialize. CurrentBar={CurrentBar}");
                logInitialized = false; // Reset flag to allow re-initialization
                InitLogger();
            }

            if (LogDrawnSignals && !signalDrawInitialized)
            {
                Print($"[CBASTestingIndicator3] OnBarUpdate: Calling InitSignalDrawLogger. CurrentBar={CurrentBar}, LogDrawnSignals={LogDrawnSignals}");
                InitSignalDrawLogger();
            }
            // Safety check: if LogDrawnSignals is true but signalDrawInitialized is still false after InitSignalDrawLogger, try again periodically
            if (LogDrawnSignals && !signalDrawInitialized && CurrentBar % 100 == 0 && CurrentBar > 0)
            {
                Print($"[CBASTestingIndicator3] OnBarUpdate: WARNING - LogDrawnSignals=true but signalDrawInitialized=false after InitSignalDrawLogger, attempting to re-initialize. CurrentBar={CurrentBar}");
                InitSignalDrawLogger();
            }

            // Initialize debug signal log early if enabled
            if (LogDrawnSignals && !debugSignalLogInitialized)
            {
                Print($"[CBASTestingIndicator3] OnBarUpdate: Initializing debug signal log. CurrentBar={CurrentBar}");
                InitDebugSignalLog();
            }

            // Periodic diagnostic: print logging state every 100 bars to track if logging stops
            if (CurrentBar % 100 == 0 && CurrentBar > 0)
            {
                Print($"[CBASTestingIndicator3] OnBarUpdate Bar {CurrentBar}: EnableLogging={EnableLogging}, logInitialized={logInitialized}, hasCachedBar={hasCachedBar}, cachedBarIndex={cachedBarIndex}, LogSignalsOnly={LogSignalsOnly}");
            }

            if (CurrentBar != cachedBarIndex)
            {
                if (hasCachedBar && EnableLogging)
                {
                    // Diagnostic: print before flushing to confirm it's being called
                    if (CurrentBar % 100 == 0 || CurrentBar < 10)
                    {
                        Print($"[CBASTestingIndicator3] OnBarUpdate Bar {CurrentBar}: About to flush cached bar {cachedBarIndex}, logInitialized={logInitialized}");
                    }
                    FlushCachedBar();
                }
                else if (hasCachedBar && !EnableLogging && CurrentBar % 100 == 0)
                {
                    Print($"[CBASTestingIndicator3] OnBarUpdate Bar {CurrentBar}: WARNING - hasCachedBar=true but EnableLogging=false, skipping flush");
                }
                else if (!hasCachedBar && EnableLogging && CurrentBar % 100 == 0)
                {
                    Print($"[CBASTestingIndicator3] OnBarUpdate Bar {CurrentBar}: WARNING - EnableLogging=true but hasCachedBar=false, skipping flush");
                }

                // Log pending signals directly without complex confirmation logic
                // The chart triangles represent the real intent - log them as-is
                if (pendingBullSignal)
                {
                    // Always log BULL signals that were plotted
                    LogDrawnSignal("BULL", pendingBullPrice, pendingBullSuperTrend, pendingBullNetFlow, pendingBullEmaColorHistory);
                    pendingBullSignal = false;
                }
                if (pendingBearSignal)
                {
                    // Always log BEAR signals that were plotted
                    LogDrawnSignal("BEAR", pendingBearPrice, pendingBearSuperTrend, pendingBearNetFlow, pendingBearEmaColorHistory);
                    pendingBearSignal = false;
                }

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
                cachedRealtimeState = RealtimeSignalState.Flat;
                cachedRealtimeReason = string.Empty;
                cachedBullScore = 0;
                cachedBearScore = 0;
                cachedBullRangeBreak = false;
                cachedBearRangeBreak = false;
                cachedSuperTrend = double.NaN;
                cachedUpperBand = double.NaN;
                cachedLowerBand = double.NaN;
                cachedAttract = double.NaN;
                cachedObjection = double.NaN;
                cachedNetFlow = double.NaN;
                cachedMomentumVal = double.NaN;
                cachedEmaColorValue = double.NaN;
                hasCachedBar = true;
                
                // Reset signal logging flags when moving to a new bar
                signalLoggedThisBar = false;
                // NOTE: Do NOT reset lastLoggedBullSignalBar and lastLoggedBearSignalBar here!
                // They track which bar the signal was logged on for the entire session
                // Only reset when we need to clear them (never, they're permanent tracking)

            }
            else
            {
                cachedBarHigh = Math.Max(cachedBarHigh, High[0]);
                cachedBarLow = Math.Min(cachedBarLow, Low[0]);
                cachedBarClose = Close[0];

            }

            if (CurrentBar < Math.Max(2, KeltnerLength))
            {
                if (CurrentBar == 0 || CurrentBar % 5 == 0)
                {
                    Print($"[CBASTestingIndicator3] Waiting for bars: CurrentBar={CurrentBar}, Required={Math.Max(2, KeltnerLength)}, KeltnerLength={KeltnerLength}");
                }
                InitializeFirstBars(cachedBarOpen, cachedBarHigh, cachedBarLow);
                // Initialize oscillator plots to NaN on early bars
                if (ScaleOscillatorToATR)
                {
                    SetPlotVal(plotAttract, double.NaN);
                    SetPlotVal(plotObjection, double.NaN);
                    SetPlotVal(plotNetFlow, double.NaN);
                }
                // Always initialize RealtimeState plot to NaN to prevent it showing near zero
                if (plotRealtimeState >= 0 && !ShowRealtimeStatePlot)
                {
                    SetPlotVal(plotRealtimeState, double.NaN);
                }
                return;
            }

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
            }

            if (earlyBearTrigger)
            {
                bearFromEmaColor = true;
                forceBearSignal = true;
                AddReason(ref debugBearReason, $"[EARLY-BEAR] emaColor={emaColorInt} net={net:F2}");
            }

            if (netflowBullTrigger)
            {
                bullFromEmaColor = true;
                forceBullSignal = true;
                AddReason(ref debugBullReason, $"[NETFLOW-BULL] emaColor={emaColorInt} net={net:F2}");
            }

            if (netflowBearTrigger)
            {
                bearFromEmaColor = true;
                forceBearSignal = true;
                AddReason(ref debugBearReason, $"[NETFLOW-BEAR] emaColor={emaColorInt} net={net:F2}");
            }

            bool extremeBearSwitch = prevPrevEmaColorInt >= 15 && prevEmaColorInt <= 10 && emaColorInt <= 0;
            if (extremeBearSwitch)
            {
                bearFromEmaColor = true;
                forceBearSignal = true;
            }

            bool extremeBullSwitch = prevPrevEmaColorInt <= 0 && prevEmaColorInt >= 10 && emaColorInt >= 15;
            if (extremeBullSwitch)
            {
                bullFromEmaColor = true;
                forceBullSignal = true;
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
            }

            if (emaColorInt <= 0 && (!greenCandle || candleRise < bullThreshold))
            {
                bullFromEmaColor = false;
                forceBullSignal = false;
                AddReason(ref debugBullReason, "[GUARD-EMA0]");
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

            // EMA SPREAD FILTER: Block signals when EMAs are too close together (choppy/ranging market)
            if (UseEmaSpreadFilter && emaSpread != null && CurrentBar >= 0)
            {
                double currentSpread = Math.Abs(emaSpread[0]);
                if (!double.IsNaN(currentSpread) && currentSpread < MinEmaSpread)
                {
                    if (bull)
                    {
                        bull = false;
                        AddReason(ref debugBullReason, $"[EMA-SPREAD-FILTER: {currentSpread:F6} < {MinEmaSpread:F6}]");
                    }
                    if (bear)
                    {
                        bear = false;
                        AddReason(ref debugBearReason, $"[EMA-SPREAD-FILTER: {currentSpread:F6} < {MinEmaSpread:F6}]");
                    }
                }
            }

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
                // CONFLICT RESOLUTION: When both signals are true, choose based on trend indicators
                Print($"\n========== CONFLICT DETECTED Bar {CurrentBar} ==========");
                Print($"  bull={bull}, bear={bear}");
                Print($"  Close[0]={Close[0]:F2}, prevBarClose={prevBarClose:F2}");
                Print($"  ao.attract={ao.attract:F2}, prevBarAttract={prevBarAttract:F2}");
                Print($"  net={net:F2}, prevBarNetFlow={prevBarNetFlow:F2}");
                Print($"  emaColorInt={emaColorInt}, prevEmaColorInt={prevEmaColorInt}");
                
                bool priceRose = Close[0] > prevBarClose;
                bool attractRose = ao.attract > prevBarAttract;
                bool netflowRose = net > prevBarNetFlow;
                bool emaColorRose = emaColorInt > prevBarEmaColor;
                
                int bullScore = (priceRose ? 1 : 0) + (attractRose ? 1 : 0) + (netflowRose ? 1 : 0) + (emaColorRose ? 1 : 0);
                int bearScore = (!priceRose ? 1 : 0) + (!attractRose ? 1 : 0) + (!netflowRose ? 1 : 0) + (!emaColorRose ? 1 : 0);
                
                Print($"[CBASTestingIndicator3] CONFLICT Bar {CurrentBar}: bull=true AND bear=true | Trend Analysis:");
                Print($"  priceRose={priceRose} (cur:{Close[0]:F2} vs prev:{prevBarClose:F2})");
                Print($"  attractRose={attractRose} (cur:{ao.attract:F2} vs prev:{prevBarAttract:F2})");
                Print($"  netflowRose={netflowRose} (cur:{net:F2} vs prev:{prevBarNetFlow:F2})");
                Print($"  emaColorRose={emaColorRose} (cur:{emaColorInt} vs prev:{prevBarEmaColor})");
                Print($"  bullScore={bullScore} vs bearScore={bearScore}");
                
                // Choose based on higher trend score
                if (bullScore > bearScore)
                {
                    bear = false;
                    AddReason(ref debugBearReason, $"[TREND-LOSS-bullScore={bullScore}vs{bearScore}]");
                    Print($"  RESOLUTION: BULL wins (score {bullScore} > {bearScore})");
                }
                else if (bearScore > bullScore)
                {
                    bull = false;
                    AddReason(ref debugBullReason, $"[TREND-LOSS-bearScore={bearScore}vs{bullScore}]");
                    Print($"  RESOLUTION: BEAR wins (score {bearScore} > {bullScore})");
                }
                else
                {
                    // Tie-breaker: if scores equal, prefer based on lastPlottedSignal
                    Print($"  TIEBREAKER: scores equal, using lastPlottedSignal={lastPlottedSignal}");
                    if (lastPlottedSignal == LastSignalType.Bull)
                    {
                        bear = false;
                        Print($"  RESOLUTION: BULL wins (last was BULL)");
                    }
                    else if (lastPlottedSignal == LastSignalType.Bear)
                    {
                        bull = false;
                        Print($"  RESOLUTION: BEAR wins (last was BEAR)");
                    }
                    else
                    {
                        // Ultimate tie-breaker: use emaColorInt
                        Print($"  TIEBREAKER2: emaColorInt={emaColorInt}");
                        if (emaColorInt >= 10)
                        {
                            bear = false;
                            Print($"  RESOLUTION: BULL wins (emaColor >= 10)");
                        }
                        else
                        {
                            bull = false;
                            Print($"  RESOLUTION: BEAR wins (emaColor < 10)");
                        }
                    }
                }
            }

            consecutiveBullBars = bull ? prospectiveBullCount : 0;
            consecutiveBearBars = bear ? prospectiveBearCount : 0;

            // Debug SignalCheck logging (similar to strategy)
            if (DebugLogSignalCheck)
            {
                bool stateChanged = bull != dbgLastBull
                    || bear != dbgLastBear
                    || IsFirstTickOfBar != dbgLastFirstTick
                    || (IsFirstTickOfBar && CurrentBar != dbgLastBar);
                bool throttle = (DateTime.UtcNow - dbgLastPrintUtc).TotalMilliseconds >= 250;

                if ((IsFirstTickOfBar || bull || bear || stateChanged) && throttle)
                {
                    Print($"[CBASTestingIndicator3] SignalCheck Bar={CurrentBar} Time={Time[0]} bull={bull} bear={bear} firstTick={IsFirstTickOfBar} emaColor={emaColorInt} netFlow={net:F2} realtimeState={currentRealtimeState} bullReason={debugBullReason} bearReason={debugBearReason}");
                    dbgLastPrintUtc = DateTime.UtcNow;
                }

                dbgLastBull = bull;
                dbgLastBear = bear;
                dbgLastFirstTick = IsFirstTickOfBar;
                dbgLastBar = CurrentBar;
            }

            // CRITICAL: Cache the FINAL bull/bear state BEFORE drawing triangles
            // This ensures the chart triangles and the BullSignal/BearSignal properties are always in sync
            bool finalBullSignal = bull;
            bool finalBearSignal = bear;
            
            // SAFETY CHECK: If both are still true after conflict resolution, force one to false
            if (finalBullSignal && finalBearSignal)
            {
                Print($"[CBASTestingIndicator3] ERROR: Both finalBullSignal AND finalBearSignal are true on Bar {CurrentBar}! Forcing BEAR to false.");
                finalBearSignal = false; // Default to BULL in case of conflict
            }

            // DEBUG: Log the final state before drawing
            if (finalBullSignal || finalBearSignal)
            {
                Print($"[CBASTestingIndicator3] DRAW Bar {CurrentBar}: finalBullSignal={finalBullSignal}, finalBearSignal={finalBearSignal}, bull={bull}, bear={bear}, emaColor={emaColorInt}, netflow={net:F2}, IsFirstTickOfBar={IsFirstTickOfBar}");
            }
            
            // DEBUG: Log why signals might not be triggering
            if (!finalBullSignal && !finalBearSignal && CurrentBar % 50 == 0)
            {
                Print($"[CBASTestingIndicator3] NO SIGNAL Bar {CurrentBar}: bull={bull}, bear={bear}, bullCrossRaw={bullCrossRaw}, bearCrossRaw={bearCrossRaw}, emaColor={emaColorInt}, netflow={net:F2}, currentRealtimeState={currentRealtimeState}, PlotRealtimeSignals={PlotRealtimeSignals}");
            }

            // LAST-TICK LOCK: Accumulate signals during bar, draw on first tick of NEXT bar
            bool isNewBar = CurrentBar != lastDrawnBarIndex;
            if (isNewBar)
            {
                // First tick of NEW bar: draw any signals that were detected during PREVIOUS bar
                lastDrawnBarIndex = CurrentBar;
                bullDrawnThisBar = false;
                bearDrawnThisBar = false;
            }
            else
            {
                // Mid-bar: accumulate signal decisions (don't draw yet)
                if (finalBullSignal)
                    decidedBullSignal = true;
                if (finalBearSignal)
                    decidedBearSignal = true;
            }

            // On new bar, use accumulated decisions from previous bar
            bool useBullSignal = isNewBar ? decidedBullSignal : false;
            bool useBearSignal = isNewBar ? decidedBearSignal : false;
            
            // After drawing, reset accumulated decisions for new bar
            if (isNewBar)
            {
                decidedBullSignal = false;
                decidedBearSignal = false;
            }

            // Draw signals once per bar: on first tick of next bar using previous bar's accumulated decision
            bool shouldDrawThisTick = isNewBar && !bullDrawnThisBar && !bearDrawnThisBar && (useBullSignal || useBearSignal);
            
            if (!shouldDrawThisTick)
            {
                // Not first tick OR already drawn - skip drawing
                // Continue to the end of OnBarUpdate for other processing
            }
            // CRITICAL: Draw BULL first if true, this will set bullDrawnThisBar=true and prevent BEAR from drawing
            else if (useBullSignal)
            {
                Print($"[CBASTestingIndicator3] *** PLOTTING BULL on Bar {CurrentBar} ***");
                bullDrawnThisBar = true;
                    
                    // CRITICAL: Update cached signals ONLY when drawing - ensures strategy sees same signals as chart
                    cachedFinalBullSignal = true;
                    cachedFinalBearSignal = false;
                    
                    double y1 = Low[0] - atr30[0] * 2.0;
                string emaColorHistory = FormatEmaColorHistory(prevPrevEmaColorInt, prevEmaColorInt, emaColorInt);
                Draw.TriangleUp(this, $"Buy_{CurrentBar}", false, 0, y1, Brushes.LimeGreen);
                // Initialize debug log on first plot if needed
                if (!debugSignalLogInitialized && LogDrawnSignals)
                    InitDebugSignalLog();
                // DEBUG: Track when signal is plotted
                if (debugSignalLogInitialized)
                    WriteDebugSignalEvent("PLOT", "BULL", "Triangle drawn on chart", $"netflow={net:F2}");
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
                // Store signal to log after confirmation (1-2 bars later with price confirmation)
                if (!pendingBullSignal) // Only set if not already pending (prevents overwriting)
                {
                    pendingBullSignal = true;
                    pendingBullSignalBar = CurrentBar;
                    pendingBullPrice = Close[0];
                    pendingBullSuperTrend = stNow;
                    pendingBullNetFlow = net;
                    pendingBullEmaColorHistory = emaColorHistory;
                }
            }
            else if (useBearSignal)
            {
                Print($"[CBASTestingIndicator3] *** PLOTTING BEAR on Bar {CurrentBar} ***");
                bearDrawnThisBar = true;
                
                // CRITICAL: Update cached signals ONLY when drawing - ensures strategy sees same signals as chart
                cachedFinalBullSignal = false;
                cachedFinalBearSignal = true;
                
                double y2 = High[0] + atr30[0] * 2.0;
                string emaColorHistory = FormatEmaColorHistory(prevPrevEmaColorInt, prevEmaColorInt, emaColorInt);
                Draw.TriangleDown(this, $"Sell_{CurrentBar}", false, 0, y2, Brushes.Red);
                // Initialize debug log on first plot if needed
                if (!debugSignalLogInitialized && LogDrawnSignals)
                    InitDebugSignalLog();
                // DEBUG: Track when signal is plotted
                if (debugSignalLogInitialized)
                    WriteDebugSignalEvent("PLOT", "BEAR", "Triangle drawn on chart", $"netflow={net:F2}");
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
                // Store signal to log after confirmation (1-2 bars later with price confirmation)
                if (!pendingBearSignal) // Only set if not already pending (prevents overwriting)
                {
                    pendingBearSignal = true;
                    pendingBearSignalBar = CurrentBar;
                    pendingBearPrice = Close[0];
                    pendingBearSuperTrend = stNow;
                    pendingBearNetFlow = net;
                    pendingBearEmaColorHistory = emaColorHistory;
                }
            }
            else
            {
                // Neither signal plotting on this bar
                if (bull || bear)
                {
                    Print($"[CBASTestingIndicator3] NO PLOT Bar {CurrentBar}: bull={bull}, bear={bear}, finalBull={finalBullSignal}, finalBear={finalBearSignal}, IsFirstTickOfBar={IsFirstTickOfBar}");
                }
                
                // CRITICAL: If we're on first tick and not drawing, ensure cached signals are false
                // This keeps strategy signals aligned with chart (no signal = false for both)
                if (IsFirstTickOfBar && !bullDrawnThisBar && !bearDrawnThisBar)
                {
                    cachedFinalBullSignal = false;
                    cachedFinalBearSignal = false;
                }
            }

            if (useBullSignal)
                lastPlottedSignal = LastSignalType.Bull;
            else if (useBearSignal)
                lastPlottedSignal = LastSignalType.Bear;
            
            // Update strategy-facing properties to match what was drawn
            // CRITICAL: These must reflect DRAWN signals, not detected signals
            if (isNewBar)
            {
                cachedFinalBullSignal = useBullSignal;
                cachedFinalBearSignal = useBearSignal;
            }

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
                // Always set RealtimeState plot to NaN when not shown to prevent it appearing near zero
                if (ShowRealtimeStatePlot)
                {
                    double plotValue = double.NaN;
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
                    SetPlotVal(plotRealtimeState, plotValue);
                }
                else
                {
                    // Explicitly hide the plot by setting to NaN and making it transparent
                    SetPlotVal(plotRealtimeState, double.NaN);
                    PlotBrushes[plotRealtimeState][0] = Brushes.Transparent;
                }
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

            // BREAKPOINT LOG: Last tick of bar - log all variables once per bar
            if (lastBreakpointLoggedBar != CurrentBar)
            {
                Print($"[BREAKPOINT] Bar {CurrentBar}: attract={ao.attract,5:F2}, objection={ao.objection,5:F2}, netflow={net,5:F2}, ema_color={emaColorInt,2}, bull_cross={(bull && bullCrossRaw ? 1 : 0)}, bear_cross={(bear && bearCrossRaw ? 1 : 0)}, bull_cross_raw={(bullCrossRaw ? 1 : 0)}, bear_cross_raw={(bearCrossRaw ? 1 : 0)}, realtime_state={currentRealtimeState}");
                lastBreakpointLoggedBar = CurrentBar;
            }

            if (ColorBarsByTrend && CurrentBar > 0 && superTrend != null && superTrend.IsValidDataPoint(1))
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

            double plotAttractVal = double.NaN;
            double plotObjectionVal = double.NaN;
            double plotNetFlowVal = double.NaN;
            double barRange = 0;
            double scale = 0;
            double scalingBasePx = 0;
            double attractScaled = 0;
            double objectionScaled = 0;
            double netScaled = 0;

            if (ScaleOscillatorToATR)
            {
                // Always scale relative to price using ATR, regardless of panel
                // This ensures all plots are visible and consistent
                double atr30Val = (atr30 != null && CurrentBar >= 30 - 1) ? atr30[0] : double.NaN;
                scalingBasePx = Close[0];
                
                if (double.IsNaN(atr30Val) || atr30Val <= 0)
                {
                    // Fallback to bar range if ATR not available
                    barRange = Math.Max(1e-6, High[0] - Low[0]);
                    scale = barRange * OscAtrMult * 10.0; // Use 10x multiplier for bar range fallback
                }
                else
                {
                    // Use ATR with moderate multiplier - when IsOverlay=true, plots need visible offset
                    // Price is ~25,000, so use ATR-based scaling that creates reasonable spread
                    scale = atr30Val * OscAtrMult * 10.0; // 10x ATR creates moderate spread
                    barRange = High[0] - Low[0]; // Still log bar range for debugging
                }
                
                // Scale attract/objection (0-10 range) to oscillate around price
                // Normalize to -1 to +1 range, then apply scale
                attractScaled = scalingBasePx + scale * (ao.attract - 5.0) / 5.0;
                // Objection typically runs 0-5, so shift midpoint lower to allow it to go above price
                // Use 2.5 as midpoint instead of 5.0 so objection can go above price when > 2.5
                objectionScaled = scalingBasePx + scale * (ao.objection - 2.5) / 5.0;
                // Scale netflow (typically -5 to +5) - use different offset to ensure it's always distinct
                // Offset netflow by 1.5x scale to separate it from attract/objection
                netScaled = scalingBasePx + scale * 1.5 * net / 5.0;
                
                plotAttractVal = attractScaled;
                plotObjectionVal = objectionScaled;
                plotNetFlowVal = netScaled;

                // Explicitly set all three plots with scaled values
                // Ensure all plots are set even if values are at base price
                if (plotAttract >= 0)
                {
                SetPlotVal(plotAttract, attractScaled);
                }
                else
                {
                    // plotAttract index is invalid
                }
                if (plotObjection >= 0)
                {
                SetPlotVal(plotObjection, objectionScaled);
                }
                else
                {
                    // plotObjection index is invalid
                }
                if (plotNetFlow >= 0)
                {
                SetPlotVal(plotNetFlow, netScaled);
            }
            else
            {
                    // plotNetFlow index is invalid
                }
            }
            else
            {
                plotAttractVal = ao.attract;
                plotObjectionVal = ao.objection;
                plotNetFlowVal = net;
                
                // Explicitly set all three plots with raw values
                if (plotAttract >= 0)
                SetPlotVal(plotAttract, ao.attract);
                if (plotObjection >= 0)
                SetPlotVal(plotObjection, ao.objection);
                if (plotNetFlow >= 0)
                SetPlotVal(plotNetFlow, net);
            }

            SetPlotBrush(plotNetFlow, net >= 0 ? Brushes.DodgerBlue : Brushes.DarkOrange);

            // Range detector updates
            UpdateRangeDetectorPlots();

            // Ensure cached bar is flushed at bar close if logging is enabled
            // This is a safety check to ensure bars are logged even if OnBarUpdate is called inconsistently
            if (EnableLogging && logInitialized && hasCachedBar && cachedBarIndex >= 0 && cachedBarIndex < CurrentBar)
            {
                // If we've moved past the cached bar index, flush it
                // This can happen if OnBarUpdate wasn't called for a bar transition
                if (CurrentBar % 100 == 0 || CurrentBar < 10)
                {
                    Print($"[CBASTestingIndicator3] OnBarUpdate END: Flushing stale cached bar {cachedBarIndex} (CurrentBar={CurrentBar})");
                }
                FlushCachedBar();
            }

            // Track previous bar values for trend analysis when both signals are true
            prevBarClose = Close[0];
            prevBarAttract = ao.attract;
            prevBarNetFlow = net;
            prevBarEmaColor = emaColorInt;

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
            {
                LogDebug("InitLogger_Skipped", new Dictionary<string, string>
                {
                    { "logInitialized", logInitialized.ToString() },
                    { "EnableLogging", EnableLogging.ToString() }
                });
                return;
            }
            
            LogDebug("InitLogger_Starting", new Dictionary<string, string>
            {
                { "EnableLogging", EnableLogging.ToString() },
                { "LogFolder", LogFolder ?? "null" }
            });

            try
            {
                // Set run timestamp on first logger initialization (used for both main log and signals log)
                if (string.IsNullOrEmpty(runTimestamp))
                {
                    runTimestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                }

                string folder = string.IsNullOrWhiteSpace(LogFolder)
                    ? @"c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\indicators_log"
                    : LogFolder;
                
                // Determine OS type once for use throughout the method
                // Also check if Globals.UserDataDir contains Mac/Home which indicates macOS even if OS detection fails
                bool isMacOS = Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX;
                // Only treat as macOS if OS reports macOS/Unix OR the configured folder already appears to be a macOS-style path
                if (!isMacOS && (Globals.UserDataDir?.Contains("Mac\\Home") == true || Globals.UserDataDir?.Contains("Mac/Home") == true))
                {
                    // If the target folder already looks like a macOS path (starts with '/' or contains '/Users/'), treat as macOS.
                    if ((folder != null && (folder.StartsWith("/") || folder.Contains("/Users/") || folder.StartsWith("C:/Mac/Home/"))))
                    {
                        Print($"[CBASTestingIndicator3] InitLogger: OS detection says Windows but path contains Mac/Home and folder looks macOS-style - treating as macOS");
                        isMacOS = true;
                    }
                }
                
                // CRITICAL TEST: Write a test file immediately to verify path works
                // This helps us isolate path issues from logging logic issues
                try
                {
                    Print($"[CBASTestingIndicator3] InitLogger: Testing path write capability...");
                    Print($"[CBASTestingIndicator3] InitLogger: Original LogFolder={LogFolder ?? "null"}");
                    Print($"[CBASTestingIndicator3] InitLogger: Globals.UserDataDir={Globals.UserDataDir ?? "null"}");
                    
                    // Normalize path first
                    string testFolder = folder;
                    
                    if (isMacOS)
                    {
                        testFolder = testFolder.Replace('\\', '/');
                        // Handle C:\Mac\Home\... paths (Windows path format on macOS network share)
                        if (testFolder.StartsWith("C:/Mac/Home/"))
                            testFolder = testFolder.Replace("C:/Mac/Home/", "/Users/mm/");
                        if (testFolder.StartsWith("//Mac/Home/") || testFolder.StartsWith("/Mac/Home/"))
                            testFolder = testFolder.Replace("//Mac/Home/", "/Users/mm/").Replace("/Mac/Home/", "/Users/mm/");
                        if (testFolder.StartsWith("C:/Users/"))
                            testFolder = testFolder.Replace("C:/Users/", "/Users/");
                        while (testFolder.Contains("//") && !testFolder.StartsWith("//"))
                            testFolder = testFolder.Replace("//", "/");
                    }
                    
                    Print($"[CBASTestingIndicator3] InitLogger: Normalized path: {testFolder}");
                    
                    // Try to create directory
                    Directory.CreateDirectory(testFolder);
                    Print($"[CBASTestingIndicator3] InitLogger: Directory created successfully");
                }
                catch (Exception testEx)
                {
                    Print($"[CBASTestingIndicator3] InitLogger: Path normalization failed - {testEx.GetType().Name}: {testEx.Message}");
                    // Don't return here - continue to try the actual logging setup
                }

                // Normalize path separators for the current OS
                // CRITICAL: Only apply macOS path normalization if OS actually reports Unix/macOS
                // Do NOT apply it based on path content heuristics (can break Windows paths)
                if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
                {
                    // First replace all backslashes with forward slashes
                    folder = folder.Replace('\\', '/');
                    
                    // Handle Windows-style network paths: \\Mac\Home\Documents\... -> /Users/mm/Documents/...
                    if (folder.StartsWith("//Mac/Home/") || folder.StartsWith("/Mac/Home/"))
                    {
                        folder = folder.Replace("//Mac/Home/", "/Users/mm/").Replace("/Mac/Home/", "/Users/mm/");
                    }
                    // Handle C:\Mac\Home\... paths (Windows path on macOS network share)
                    if (folder.StartsWith("C:/Mac/Home/"))
                    {
                        folder = folder.Replace("C:/Mac/Home/", "/Users/mm/");
                    }
                    // Handle C:\Users\... paths
                    if (folder.StartsWith("C:/Users/"))
                    {
                        folder = folder.Replace("C:/Users/", "/Users/");
                    }
                    // Remove any double slashes (except at the beginning for absolute paths)
                    while (folder.Contains("//") && !folder.StartsWith("//"))
                    {
                        folder = folder.Replace("//", "/");
                    }
                    
                    // On macOS, don't use Path.GetFullPath as it may convert paths incorrectly
                    // Use the normalized path directly
                    Print($"[CBASTestingIndicator3] InitLogger: macOS OS detected, using normalized path: {folder}");
                }
                else
                {
                    // On Windows, use Path.GetFullPath to resolve relative paths
                    try
                    {
                        folder = Path.GetFullPath(folder);
                    }
                    catch
                    {
                        Print($"[CBASTestingIndicator3] Path.GetFullPath failed, using path as-is: {folder}");
                    }
                }
                
                Print($"[CBASTestingIndicator3] InitLogger: Final folder path: {folder}");
                Directory.CreateDirectory(folder);

                string fileName = BuildLogFileBase() + ".csv";
                if (isMacOS)
                {
                    // On macOS, use Path.Combine but don't call GetFullPath
                    logPath = folder + "/" + fileName;
                }
                else
                {
                    logPath = Path.GetFullPath(Path.Combine(folder, fileName));
                }

                bool fileExists = File.Exists(logPath);
                bool isEmpty = !fileExists || new FileInfo(logPath).Length == 0;

                // Verify directory exists before creating file
                try
                {
                    if (!Directory.Exists(folder))
                    {
                        Print($"[CBASTestingIndicator3] InitLogger: Directory does not exist, creating: {folder}");
                        Directory.CreateDirectory(folder);
                    }
                    
                    // Verify we can write to the directory by creating a test file
                    string testWritePath = isMacOS 
                        ? folder + "/WRITE_TEST_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt"
                        : Path.Combine(folder, "WRITE_TEST_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                    
                    File.WriteAllText(testWritePath, "Write test");
                    if (File.Exists(testWritePath))
                    {
                        File.Delete(testWritePath);
                        Print($"[CBASTestingIndicator3] InitLogger: Directory write test PASSED");
                    }
                    else
                    {
                        Print($"[CBASTestingIndicator3] InitLogger: WARNING - Directory write test FAILED - file not found after write");
                    }
                }
                catch (Exception dirEx)
                {
                    Print($"[CBASTestingIndicator3] InitLogger: ERROR - Cannot write to directory {folder}: {dirEx.Message}");
                    throw; // Re-throw to prevent creating StreamWriter with invalid path
                }
                
                logWriter = new StreamWriter(logPath, append: true, encoding: Encoding.UTF8) { AutoFlush = true };
                logInitialized = true;
                
                // Verify file was actually created
                if (File.Exists(logPath))
                {
                    Print($"[CBASTestingIndicator3] InitLogger: ✅ Log file confirmed to exist: {logPath}");
                }
                else
                {
                    Print($"[CBASTestingIndicator3] InitLogger: ⚠️ WARNING - StreamWriter created but file does not exist: {logPath}");
                }

                LogDebug("InitLogger_Success", new Dictionary<string, string>
                {
                    { "logPath", logPath },
                    { "fileExists", fileExists.ToString() },
                    { "isEmpty", isEmpty.ToString() }
                });

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
                Print($"[CBASTestingIndicator3] InitLogger SUCCESS: logPath={logPath}, logInitialized={logInitialized}");
                
                // Write log path to a file in the Custom folder for easy access
                try
                {
                    string pathInfoFile = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".", "CBASTestingIndicator3_LogPath.txt");
                    string actualLogPath = File.Exists(logPath) ? logPath : "FILE NOT FOUND";
                    File.WriteAllText(pathInfoFile, $"Log Path: {logPath}\nActual File Exists: {File.Exists(logPath)}\nFolder: {folder}\nFolder Exists: {Directory.Exists(folder)}\nGlobals.UserDataDir: {Globals.UserDataDir ?? "null"}\nIsMacOS: {isMacOS}\nTimestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\nNOTE: If file doesn't exist, check the Output window for error messages.");
                    Print($"[CBASTestingIndicator3] Log path written to: {pathInfoFile}");
                    Print($"[CBASTestingIndicator3] Log file exists check: {File.Exists(logPath)}");
                    Print($"[CBASTestingIndicator3] Log directory exists check: {Directory.Exists(folder)}");
                }
                catch (Exception pathEx)
                {
                    Print($"[CBASTestingIndicator3] Failed to write path info file: {pathEx.Message}");
                }
            }
            catch (Exception ex)
            {
                // Always print errors, even if debug logger isn't initialized
                Print($"[CBASTestingIndicator3] InitLogger ERROR: {ex.Message}");
                Print($"[CBASTestingIndicator3] InitLogger StackTrace: {ex.StackTrace ?? "null"}");
                
                // Try to log to debug logger if available
                if (debugLogInitialized)
                {
                    LogDebug("InitLogger_Error", new Dictionary<string, string>
                    {
                        { "Error", ex.Message },
                        { "StackTrace", ex.StackTrace ?? "null" }
                    });
                }
                // Attempt fallback: write a minimal log to system temp folder so we can confirm writes
                try
                {
                    string fallbackFolder = Path.Combine(Path.GetTempPath(), "CBASTestingIndicator3_logs");
                    Directory.CreateDirectory(fallbackFolder);
                    string fallbackFile = Path.GetFullPath(Path.Combine(fallbackFolder, BuildLogFileBase() + "_FALLBACK.csv"));
                    using (var fw = new StreamWriter(fallbackFile, append: true, encoding: Encoding.UTF8))
                    {
                        fw.WriteLine($"FALLBACK,{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff},InitLoggerError,{ex.GetType().Name},{ex.Message}");
                        fw.Flush();
                    }
                    Print($"[CBASTestingIndicator3] InitLogger: Fallback log created at {fallbackFile}");
                    try
                    {
                        string pathInfoFile = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".", "CBASTestingIndicator3_LogPath.txt");
                        File.WriteAllText(pathInfoFile, $"Primary InitLogger ERROR: {ex.Message}\nFallback log: {fallbackFile}\nTimestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        Print($"[CBASTestingIndicator3] Log path written to: {pathInfoFile}");
                    }
                    catch { }
                }
                catch (Exception fbEx)
                {
                    Print($"[CBASTestingIndicator3] InitLogger: Fallback failed: {fbEx.GetType().Name}: {fbEx.Message}");
                }

                logInitialized = false;
            }
        }

        private void FlushCachedBar()
        {
            LogDebug("FlushCachedBar_Called", new Dictionary<string, string>
            {
                { "EnableLogging", EnableLogging.ToString() },
                { "logInitialized", logInitialized.ToString() },
                { "hasCachedBar", hasCachedBar.ToString() },
                { "LogSignalsOnly", LogSignalsOnly.ToString() },
                { "cachedBarIndex", cachedBarIndex.ToString() },
                { "CurrentBar", CurrentBar.ToString() }
            });
            
            if (!EnableLogging || !logInitialized || !hasCachedBar)
            {
                LogDebug("FlushCachedBar_Skipped", new Dictionary<string, string>
                {
                    { "EnableLogging", EnableLogging.ToString() },
                    { "logInitialized", logInitialized.ToString() },
                    { "hasCachedBar", hasCachedBar.ToString() }
                });
                return;
            }

            // Log every bar if LogSignalsOnly is false, otherwise only log bars with signals
            bool includeRow = !LogSignalsOnly || cachedBullSignal || cachedBearSignal;
            if (!includeRow)
            {
                LogDebug("FlushCachedBar_Skipped_NoSignals", new Dictionary<string, string>
                {
                    { "LogSignalsOnly", LogSignalsOnly.ToString() },
                    { "cachedBullSignal", cachedBullSignal.ToString() },
                    { "cachedBearSignal", cachedBearSignal.ToString() }
                });
                return;
            }
            
            LogDebug("FlushCachedBar_Logging", new Dictionary<string, string>
            {
                { "cachedBarIndex", cachedBarIndex.ToString() }
            });

            // Diagnostic: print when flushing cached bar to confirm it's being called
            if (CurrentBar < 10 || CurrentBar % 100 == 0)
            {
                Print($"[CBASTestingIndicator3] FlushCachedBar: Flushing bar {cachedBarIndex}, CurrentBar={CurrentBar}, logInitialized={logInitialized}, LogSignalsOnly={LogSignalsOnly}, cachedBullSignal={cachedBullSignal}, cachedBearSignal={cachedBearSignal}");
            }

            int barsAgo = CurrentBar - cachedBarIndex;
            if (barsAgo < 0)
                barsAgo = 0;
            if (barsAgo > CurrentBar)
                barsAgo = CurrentBar;

            DateTime barTimeLocal = Time[barsAgo];


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
            {
                LogDebug("InitSignalDrawLogger_Skipped", new Dictionary<string, string>
                {
                    { "signalDrawInitialized", signalDrawInitialized.ToString() },
                    { "LogDrawnSignals", LogDrawnSignals.ToString() }
                });
                return;
            }
            
            LogDebug("InitSignalDrawLogger_Starting", new Dictionary<string, string>
            {
                { "LogDrawnSignals", LogDrawnSignals.ToString() },
                { "LogFolder", LogFolder ?? "null" }
            });

            try
            {
                // Set run timestamp on first logger initialization (used for both main log and signals log)
                if (string.IsNullOrEmpty(runTimestamp))
                {
                    runTimestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                }

                string folder = string.IsNullOrWhiteSpace(LogFolder)
                    ? @"c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\indicators_log"
                    : LogFolder;

                // Determine if we're on actual macOS/Unix for path construction
                bool isMacOS = Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX;
                
                // CRITICAL: Only apply macOS path normalization if OS actually reports Unix/macOS
                // Do NOT apply it based on path content heuristics (can break Windows paths)
                if (isMacOS)
                {
                    // First replace all backslashes with forward slashes
                    folder = folder.Replace('\\', '/');
                    
                    // Handle C:\Mac\Home\... paths (Windows path format on macOS network share)
                    if (folder.StartsWith("C:/Mac/Home/"))
                    {
                        folder = folder.Replace("C:/Mac/Home/", "/Users/mm/");
                    }
                    // Handle Windows-style network paths: \\Mac\Home\Documents\... -> /Users/mm/Documents/...
                    if (folder.StartsWith("//Mac/Home/") || folder.StartsWith("/Mac/Home/"))
                    {
                        folder = folder.Replace("//Mac/Home/", "/Users/mm/").Replace("/Mac/Home/", "/Users/mm/");
                    }
                    // Handle C:\Users\... paths
                    if (folder.StartsWith("C:/Users/"))
                    {
                        folder = folder.Replace("C:/Users/", "/Users/");
                    }
                    // Remove any double slashes (except at the beginning for absolute paths)
                    while (folder.Contains("//") && !folder.StartsWith("//"))
                    {
                        folder = folder.Replace("//", "/");
                    }
                    
                    // On macOS, don't use Path.GetFullPath as it may convert paths incorrectly
                    // Use the normalized path directly
                    Print($"[CBASTestingIndicator3] InitSignalDrawLogger: macOS detected, using normalized path: {folder}");
                }
                else
                {
                    // On Windows, use Path.GetFullPath to resolve relative paths
                    try
                    {
                        folder = Path.GetFullPath(folder);
                    }
                    catch
                    {
                        Print($"[CBASTestingIndicator3] InitSignalDrawLogger: Path.GetFullPath failed, using path as-is: {folder}");
                    }
                }
                
                Print($"[CBASTestingIndicator3] InitSignalDrawLogger: Final folder path: {folder}");
                Directory.CreateDirectory(folder);

                string fileName = BuildLogFileBase() + "_signals.csv";
                if (isMacOS)
                {
                    // On macOS, use Path.Combine but don't call GetFullPath
                    signalDrawPath = folder + "/" + fileName;
                }
                else
                {
                    signalDrawPath = Path.GetFullPath(Path.Combine(folder, fileName));
                }

                bool fileExists = File.Exists(signalDrawPath);
                bool isEmpty = !fileExists || new FileInfo(signalDrawPath).Length == 0;

                signalDrawWriter = new StreamWriter(signalDrawPath, append: true, encoding: Encoding.UTF8)
                {
                    AutoFlush = true
                };
                signalDrawInitialized = true;
                
                LogDebug("InitSignalDrawLogger_Success", new Dictionary<string, string>
                {
                    { "signalDrawPath", signalDrawPath },
                    { "fileExists", fileExists.ToString() },
                    { "isEmpty", isEmpty.ToString() }
                });

                if (isEmpty)
                {
                    lock (signalDrawLock)
                    {
                        signalDrawWriter.WriteLine("ts_local,bar_time_utc,bar_index,instrument,signal_type,price,supertrend,volume,netflow,ema_color_hist");
                    }
                }

                Print($"[CBASTestingIndicator3] Signal draw logging to: {signalDrawPath}");
                Print($"[CBASTestingIndicator3] InitSignalDrawLogger SUCCESS: signalDrawPath={signalDrawPath}, signalDrawInitialized={signalDrawInitialized}");
            }
            catch (Exception ex)
            {
                // Always print errors, even if debug logger isn't initialized
                Print($"[CBASTestingIndicator3] InitSignalDrawLogger ERROR: {ex.Message}");
                Print($"[CBASTestingIndicator3] InitSignalDrawLogger StackTrace: {ex.StackTrace ?? "null"}");
                
                // Try to log to debug logger if available
                if (debugLogInitialized)
                {
                    LogDebug("InitSignalDrawLogger_Error", new Dictionary<string, string>
                    {
                        { "Error", ex.Message },
                        { "StackTrace", ex.StackTrace ?? "null" }
                    });
                }
                signalDrawInitialized = false;
            }
        }
        
        private void InitDebugSignalLog()
        {
            if (debugSignalLogInitialized)
                return;

            try
            {
                string folder = string.IsNullOrWhiteSpace(LogFolder)
                    ? @"c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\indicators_log"
                    : LogFolder;

                // Ensure folder exists
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                // Set run timestamp if not already set
                if (string.IsNullOrEmpty(runTimestamp))
                    runTimestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

                string nameSegment = MakeSafeFileSegment(Name ?? "Indicator");
                string instrumentSegment = MakeSafeFileSegment(Instrument?.FullName ?? "NA");
                string instanceSegment = MakeSafeFileSegment(string.IsNullOrWhiteSpace(InstanceId) ? null : InstanceId);

                string baseFileName = BuildLogFileBase();
                debugSignalLogPath = Path.Combine(folder, $"{baseFileName}_signal_debug.csv");

                Print($"[CBASTestingIndicator3] InitDebugSignalLog: About to create file: {debugSignalLogPath}");
                Print($"[CBASTestingIndicator3] InitDebugSignalLog: Folder exists: {Directory.Exists(folder)}");

                debugSignalLogWriter = new StreamWriter(debugSignalLogPath, append: true, encoding: System.Text.Encoding.UTF8, bufferSize: 65536)
                {
                    AutoFlush = false
                };

                // Write header if file is new
                FileInfo fileInfo = new FileInfo(debugSignalLogPath);
                if (fileInfo.Length == 0)
                {
                    debugSignalLogWriter.WriteLine("ts_local,event_type,bar_index,signal_type,action_reason,notes");
                    debugSignalLogWriter.Flush();
                }

                debugSignalLogInitialized = true;
                Print($"[CBASTestingIndicator3] Debug signal log initialized SUCCESS: {debugSignalLogPath}");
            }
            catch (Exception ex)
            {
                Print($"[CBASTestingIndicator3] Failed to initialize debug signal log: {ex.Message}");
                Print($"[CBASTestingIndicator3] StackTrace: {ex.StackTrace}");
                debugSignalLogInitialized = false;
            }
        }
        
        private void InitDebugLogger()
        {
            if (debugLogInitialized)
                return;

            try
            {
                string folder = string.IsNullOrWhiteSpace(LogFolder)
                    ? @"c:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\indicators_log"
                    : LogFolder;

                // Normalize path separators for the current OS
                // Determine if we're on actual macOS/Unix for path construction
                bool isMacOS = Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX;
                
                // CRITICAL: Only apply macOS path normalization if OS actually reports Unix/macOS
                // Do NOT apply it based on path content heuristics (can break Windows paths)
                if (isMacOS)
                {
                    // First replace all backslashes with forward slashes
                    folder = folder.Replace('\\', '/');
                    
                    // Handle C:\Mac\Home\... paths (Windows path format on macOS network share)
                    if (folder.StartsWith("C:/Mac/Home/"))
                    {
                        folder = folder.Replace("C:/Mac/Home/", "/Users/mm/");
                    }
                    // Handle Windows-style network paths: \\Mac\Home\Documents\... -> /Users/mm/Documents/...
                    if (folder.StartsWith("//Mac/Home/") || folder.StartsWith("/Mac/Home/"))
                    {
                        folder = folder.Replace("//Mac/Home/", "/Users/mm/").Replace("/Mac/Home/", "/Users/mm/");
                    }
                    // Handle C:\Users\... paths
                    if (folder.StartsWith("C:/Users/"))
                    {
                        folder = folder.Replace("C:/Users/", "/Users/");
                    }
                    // Remove any double slashes (except at the beginning for absolute paths)
                    while (folder.Contains("//") && !folder.StartsWith("//"))
                    {
                        folder = folder.Replace("//", "/");
                    }
                    
                    // On macOS, don't use Path.GetFullPath as it may convert paths incorrectly
                    // Use the normalized path directly
                    Print($"[CBASTestingIndicator3] InitDebugLogger: macOS detected, using normalized path: {folder}");
                }
                else
                {
                    // On Windows, use Path.GetFullPath to resolve relative paths
                    try
                    {
                        folder = Path.GetFullPath(folder);
                    }
                    catch
                    {
                        Print($"[CBASTestingIndicator3] InitDebugLogger: Path.GetFullPath failed, using path as-is: {folder}");
                    }
                }
                
                Print($"[CBASTestingIndicator3] InitDebugLogger: Final folder path: {folder}");
                Directory.CreateDirectory(folder);

                string debugFileName = $"CBASTestingIndicator3_DEBUG_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv";
                string debugLogPath;
                if (isMacOS)
                {
                    // On macOS, use Path.Combine but don't call GetFullPath
                    debugLogPath = folder + "/" + debugFileName;
                }
                else
                {
                    debugLogPath = Path.GetFullPath(Path.Combine(folder, debugFileName));
                }

                Print($"[CBASTestingIndicator3] InitDebugLogger: Creating debug log: {debugLogPath}");

                debugLogWriter = new StreamWriter(debugLogPath, append: false, encoding: Encoding.UTF8)
                {
                    AutoFlush = true
                };
                debugLogInitialized = true;

                // Write header
                debugLogWriter.WriteLine("timestamp,event,CurrentBar,EnableLogging,LogDrawnSignals,LogFolder,logInitialized,signalDrawInitialized,State,Instrument,AdditionalInfo");
                
                // Log initial state
                LogDebug("InitDebugLogger_Success", new Dictionary<string, string>
                {
                    { "debugLogPath", debugLogPath },
                    { "LogFolder", LogFolder ?? "null" },
                    { "EnableLogging", EnableLogging.ToString() },
                    { "LogDrawnSignals", LogDrawnSignals.ToString() }
                });
                
                Print($"[CBASTestingIndicator3] Debug logger initialized successfully");
            }
            catch (Exception ex)
            {
                Print($"[CBASTestingIndicator3] Debug log init failed: {ex.Message}");
                Print($"[CBASTestingIndicator3] StackTrace: {ex.StackTrace}");
                debugLogInitialized = false;
            }
        }

        private void LogDebug(string eventName, Dictionary<string, string> data = null)
        {
            if (!debugLogInitialized || debugLogWriter == null)
                return;

            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string currentBar = CurrentBar >= 0 ? CurrentBar.ToString() : "N/A";
                string enableLogging = EnableLogging.ToString();
                string logDrawnSignals = LogDrawnSignals.ToString();
                string logFolder = LogFolder ?? "null";
                string logInit = logInitialized.ToString();
                string signalInit = signalDrawInitialized.ToString();
                string state = State.ToString();
                string instrument = Instrument?.FullName ?? "null";
                
                string additionalInfo = "{}";
                if (data != null && data.Count > 0)
                {
                    additionalInfo = "{" + string.Join("; ", data.Select(kvp => $"{kvp.Key}={kvp.Value}")) + "}";
                }

                string line = string.Join(",",
                    CsvEscape(timestamp),
                    CsvEscape(eventName),
                    CsvEscape(currentBar),
                    CsvEscape(enableLogging),
                    CsvEscape(logDrawnSignals),
                    CsvEscape(logFolder),
                    CsvEscape(logInit),
                    CsvEscape(signalInit),
                    CsvEscape(state),
                    CsvEscape(instrument),
                    CsvEscape(additionalInfo));

                lock (debugLogLock)
                {
                    debugLogWriter.WriteLine(line);
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
                //CsvEscape(string.IsNullOrEmpty(LogFolder) ? Path.Combine(Globals.UserDataDir, "indicators_log") : LogFolder)
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
            if (!logInitialized || logWriter == null)
            {
                // Print warning very rarely to avoid spamming (only every 1000 bars if logging fails)
                if (CurrentBar < 10 && !logInitialized)
                {
                    Print($"[CBASTestingIndicator3] SafeWriteLine SKIPPED: logInitialized={logInitialized}, logWriter={(logWriter == null ? "null" : "not null")}, EnableLogging={EnableLogging}");
                }
                return;
            }
            try
            {
                lock (logLock)
                {
                    logWriter.WriteLine(line);
                    // Don't flush every write — let OS buffer writes for performance
                    // Only flush on first few bars and every 500 bars
                    if (CurrentBar < 5 || CurrentBar % 500 == 0)
                    {
                        logWriter.Flush();
                    }
                    // Remove expensive File.Exists and FileInfo checks — they block on disk I/O
                    // Disable detailed diagnostics by default (only enable if troubleshooting)
                    if (false && (CurrentBar < 10 || CurrentBar % 100 == 0)) // Disabled by default
                    {
                        long fileSize = 0;
                        bool fileExists = false;
                        try
                        {
                            if (File.Exists(logPath))
                            {
                                fileExists = true;
                                fileSize = new FileInfo(logPath).Length;
                            }
                        }
                        catch { }
                        Print($"[CBASTestingIndicator3] SafeWriteLine: Wrote line for bar {CurrentBar}, logInitialized={logInitialized}, logPath={logPath ?? "null"}, fileExists={fileExists}, fileSize={fileSize} bytes");
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"[CBASTestingIndicator3] Log write failed: {ex.Message}");
                Print($"[CBASTestingIndicator3] Log write StackTrace: {ex.StackTrace ?? "null"}");
            }
        }

        private void WriteDebugSignalEvent(string eventType, string signalType, string reason, string notes = "")
        {
            if (!debugSignalLogInitialized || debugSignalLogWriter == null)
                return;

            try
            {
                lock (debugSignalLogLock)
                {
                    string line = string.Join(",",
                        CsvEscape(Time[0].ToString("yyyy-MM-dd HH:mm:ss.fff")),
                        CsvEscape(eventType),
                        CurrentBar.ToString(),
                        CsvEscape(signalType ?? string.Empty),
                        CsvEscape(reason ?? string.Empty),
                        CsvEscape(notes ?? string.Empty));

                    debugSignalLogWriter.WriteLine(line);
                    debugSignalLogWriter.Flush();
                }
            }
            catch (Exception ex)
            {
                Print($"[CBASTestingIndicator3] Debug signal log write failed: {ex.Message}");
            }
        }

        private void LogDrawnSignal(string signalType, double price, double superTrendValue, double netFlow, string emaColorHistory)
        {
            if (!LogDrawnSignals || !signalDrawInitialized || signalDrawWriter == null)
                return;

            // CRITICAL FIX: Prevent OPPOSITE signals (BULL vs BEAR) on the same bar
            // Only allow ONE signal per bar to prevent conflicting signals
            if (string.Equals(signalType, "BULL", StringComparison.OrdinalIgnoreCase))
            {
                if (lastLoggedBullSignalBar == CurrentBar)
                    return; // Already logged BULL for this bar
                if (lastLoggedBearSignalBar == CurrentBar)
                {
                    // DEBUG: Track when opposite signal is rejected
                    if (debugSignalLogInitialized)
                        WriteDebugSignalEvent("REJECT", "BULL", "BEAR already logged this bar", "");
                    return; // Already logged BEAR for this bar - reject BULL
                }
            }
            else if (string.Equals(signalType, "BEAR", StringComparison.OrdinalIgnoreCase))
            {
                if (lastLoggedBearSignalBar == CurrentBar)
                    return; // Already logged BEAR for this bar
                if (lastLoggedBullSignalBar == CurrentBar)
                {
                    // DEBUG: Track when opposite signal is rejected
                    if (debugSignalLogInitialized)
                        WriteDebugSignalEvent("REJECT", "BEAR", "BULL already logged this bar", "");
                    return; // Already logged BULL for this bar - reject BEAR
                }
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
                    CsvNum(netFlow),
                    CsvEscape(emaColorHistory ?? string.Empty));

                lock (signalDrawLock)
                {
                    signalDrawWriter.WriteLine(line);
                }
                
                // DEBUG: Track when signal is actually logged
                if (debugSignalLogInitialized)
                    WriteDebugSignalEvent("LOGGED", signalType, "Written to signals CSV", $"price={price:F2},netflow={netFlow:F2}");
                if (CurrentBar < 10 || CurrentBar % 100 == 0)
                    Print($"[CBASTestingIndicator3] DEBUG LOGGED: Bar {CurrentBar} {signalType} LOGGED to file");

                if (string.Equals(signalType, "BULL", StringComparison.OrdinalIgnoreCase))
                {
                    lastLoggedBullSignalBar = CurrentBar;
                    signalLoggedThisBar = true; // Mark that a signal was logged this bar (prevents opposite signal)
                }
                else if (string.Equals(signalType, "BEAR", StringComparison.OrdinalIgnoreCase))
                {
                    lastLoggedBearSignalBar = CurrentBar;
                    signalLoggedThisBar = true; // Mark that a signal was logged this bar (prevents opposite signal)
                }
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
            
            // Add timestamp to make each run unique (prevents overwriting previous logs)
            if (!string.IsNullOrEmpty(runTimestamp))
                baseName += $"_{runTimestamp}";

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

        private static int ClampEmaColor(int value)
        {
            if (value < 0) return 0;
            if (value > 15) return 15;
            return value;
        }

        private static string FormatEmaColorHistory(int prevPrev, int prev, int current)
        {
            int cc = ClampEmaColor(current);
            int bb = ClampEmaColor(prev >= 0 ? prev : current);
            int aaSource = prevPrev >= 0 ? prevPrev : (prev >= 0 ? prev : current);
            int aa = ClampEmaColor(aaSource);
            return $"{aa}_{bb}_{cc}";
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

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (chartControl != null && chartScale != null)
            {
                try
                {
                    ChartPanel panel = chartControl.ChartPanels[chartScale.PanelIndex];
                    bool? reflected = null;
                    if (panel != null)
                    {
                        PropertyInfo prop = panel.GetType().GetProperty("IsPricePanel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (prop != null && prop.PropertyType == typeof(bool))
                        {
                            reflected = (bool)prop.GetValue(panel);
                        }
                    }

                    if (reflected.HasValue)
                        lastRenderWasPricePanel = reflected.Value;
                    else
                        lastRenderWasPricePanel = (chartScale.PanelIndex == 0 && DrawOnPricePanel);
                }
                catch
                {
                    lastRenderWasPricePanel = chartScale.PanelIndex == 0 && DrawOnPricePanel;
                }
            }

            base.OnRender(chartControl, chartScale);
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private CBASTestingIndicator3[] cacheCBASTestingIndicator3;
		public CBASTestingIndicator3 CBASTestingIndicator3(bool showExitLabels, double exitLabelAtrOffset, bool useRegimeStability, int regimeStabilityBars, bool useScoringFilter, int scoreThreshold, bool useSmoothedVpm, int vpmEmaSpan, double minVpm, int minAdx, int momentumLookback, bool extendedLogging, bool computeExitSignals, double exitProfitAtrMult, string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder, bool scaleOscillatorToATR, double oscAtrMult, bool logDrawnSignals, bool debugLogSignalCheck, bool colorBarsByTrend, bool useEmaSpreadFilter, double minEmaSpread, double realtimeBullNetflowMin, double realtimeBullObjectionMax, double realtimeBullEmaColorMin, bool realtimeBullUseAttract, double realtimeBullAttractMin, double realtimeBullScoreMin, double realtimeBearNetflowMax, double realtimeBearObjectionMin, double realtimeBearEmaColorMax, bool realtimeBearUsePriceToBand, double realtimeBearPriceToBandMax, double realtimeBearScoreMin, double realtimeFlatTolerance, bool showRealtimeStatePlot, bool plotRealtimeSignals, int flipConfirmationBars)
		{
			return CBASTestingIndicator3(Input, showExitLabels, exitLabelAtrOffset, useRegimeStability, regimeStabilityBars, useScoringFilter, scoreThreshold, useSmoothedVpm, vpmEmaSpan, minVpm, minAdx, momentumLookback, extendedLogging, computeExitSignals, exitProfitAtrMult, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder, scaleOscillatorToATR, oscAtrMult, logDrawnSignals, debugLogSignalCheck, colorBarsByTrend, useEmaSpreadFilter, minEmaSpread, realtimeBullNetflowMin, realtimeBullObjectionMax, realtimeBullEmaColorMin, realtimeBullUseAttract, realtimeBullAttractMin, realtimeBullScoreMin, realtimeBearNetflowMax, realtimeBearObjectionMin, realtimeBearEmaColorMax, realtimeBearUsePriceToBand, realtimeBearPriceToBandMax, realtimeBearScoreMin, realtimeFlatTolerance, showRealtimeStatePlot, plotRealtimeSignals, flipConfirmationBars);
		}

		public CBASTestingIndicator3 CBASTestingIndicator3(ISeries<double> input, bool showExitLabels, double exitLabelAtrOffset, bool useRegimeStability, int regimeStabilityBars, bool useScoringFilter, int scoreThreshold, bool useSmoothedVpm, int vpmEmaSpan, double minVpm, int minAdx, int momentumLookback, bool extendedLogging, bool computeExitSignals, double exitProfitAtrMult, string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder, bool scaleOscillatorToATR, double oscAtrMult, bool logDrawnSignals, bool debugLogSignalCheck, bool colorBarsByTrend, bool useEmaSpreadFilter, double minEmaSpread, double realtimeBullNetflowMin, double realtimeBullObjectionMax, double realtimeBullEmaColorMin, bool realtimeBullUseAttract, double realtimeBullAttractMin, double realtimeBullScoreMin, double realtimeBearNetflowMax, double realtimeBearObjectionMin, double realtimeBearEmaColorMax, bool realtimeBearUsePriceToBand, double realtimeBearPriceToBandMax, double realtimeBearScoreMin, double realtimeFlatTolerance, bool showRealtimeStatePlot, bool plotRealtimeSignals, int flipConfirmationBars)
		{
			if (cacheCBASTestingIndicator3 != null)
				for (int idx = 0; idx < cacheCBASTestingIndicator3.Length; idx++)
					if (cacheCBASTestingIndicator3[idx] != null && cacheCBASTestingIndicator3[idx].ShowExitLabels == showExitLabels && cacheCBASTestingIndicator3[idx].ExitLabelAtrOffset == exitLabelAtrOffset && cacheCBASTestingIndicator3[idx].UseRegimeStability == useRegimeStability && cacheCBASTestingIndicator3[idx].RegimeStabilityBars == regimeStabilityBars && cacheCBASTestingIndicator3[idx].UseScoringFilter == useScoringFilter && cacheCBASTestingIndicator3[idx].ScoreThreshold == scoreThreshold && cacheCBASTestingIndicator3[idx].UseSmoothedVpm == useSmoothedVpm && cacheCBASTestingIndicator3[idx].VpmEmaSpan == vpmEmaSpan && cacheCBASTestingIndicator3[idx].MinVpm == minVpm && cacheCBASTestingIndicator3[idx].MinAdx == minAdx && cacheCBASTestingIndicator3[idx].MomentumLookback == momentumLookback && cacheCBASTestingIndicator3[idx].ExtendedLogging == extendedLogging && cacheCBASTestingIndicator3[idx].ComputeExitSignals == computeExitSignals && cacheCBASTestingIndicator3[idx].ExitProfitAtrMult == exitProfitAtrMult && cacheCBASTestingIndicator3[idx].InstanceId == instanceId && cacheCBASTestingIndicator3[idx].Sensitivity == sensitivity && cacheCBASTestingIndicator3[idx].EmaEnergy == emaEnergy && cacheCBASTestingIndicator3[idx].KeltnerLength == keltnerLength && cacheCBASTestingIndicator3[idx].AtrLength == atrLength && cacheCBASTestingIndicator3[idx].RangeMinLength == rangeMinLength && cacheCBASTestingIndicator3[idx].RangeWidthMult == rangeWidthMult && cacheCBASTestingIndicator3[idx].RangeAtrLen == rangeAtrLen && cacheCBASTestingIndicator3[idx].EnableLogging == enableLogging && cacheCBASTestingIndicator3[idx].LogSignalsOnly == logSignalsOnly && cacheCBASTestingIndicator3[idx].HeartbeatEveryNBars == heartbeatEveryNBars && cacheCBASTestingIndicator3[idx].LogFolder == logFolder && cacheCBASTestingIndicator3[idx].ScaleOscillatorToATR == scaleOscillatorToATR && cacheCBASTestingIndicator3[idx].OscAtrMult == oscAtrMult && cacheCBASTestingIndicator3[idx].LogDrawnSignals == logDrawnSignals && cacheCBASTestingIndicator3[idx].DebugLogSignalCheck == debugLogSignalCheck && cacheCBASTestingIndicator3[idx].ColorBarsByTrend == colorBarsByTrend && cacheCBASTestingIndicator3[idx].UseEmaSpreadFilter == useEmaSpreadFilter && cacheCBASTestingIndicator3[idx].MinEmaSpread == minEmaSpread && cacheCBASTestingIndicator3[idx].RealtimeBullNetflowMin == realtimeBullNetflowMin && cacheCBASTestingIndicator3[idx].RealtimeBullObjectionMax == realtimeBullObjectionMax && cacheCBASTestingIndicator3[idx].RealtimeBullEmaColorMin == realtimeBullEmaColorMin && cacheCBASTestingIndicator3[idx].RealtimeBullUseAttract == realtimeBullUseAttract && cacheCBASTestingIndicator3[idx].RealtimeBullAttractMin == realtimeBullAttractMin && cacheCBASTestingIndicator3[idx].RealtimeBullScoreMin == realtimeBullScoreMin && cacheCBASTestingIndicator3[idx].RealtimeBearNetflowMax == realtimeBearNetflowMax && cacheCBASTestingIndicator3[idx].RealtimeBearObjectionMin == realtimeBearObjectionMin && cacheCBASTestingIndicator3[idx].RealtimeBearEmaColorMax == realtimeBearEmaColorMax && cacheCBASTestingIndicator3[idx].RealtimeBearUsePriceToBand == realtimeBearUsePriceToBand && cacheCBASTestingIndicator3[idx].RealtimeBearPriceToBandMax == realtimeBearPriceToBandMax && cacheCBASTestingIndicator3[idx].RealtimeBearScoreMin == realtimeBearScoreMin && cacheCBASTestingIndicator3[idx].RealtimeFlatTolerance == realtimeFlatTolerance && cacheCBASTestingIndicator3[idx].ShowRealtimeStatePlot == showRealtimeStatePlot && cacheCBASTestingIndicator3[idx].PlotRealtimeSignals == plotRealtimeSignals && cacheCBASTestingIndicator3[idx].FlipConfirmationBars == flipConfirmationBars && cacheCBASTestingIndicator3[idx].EqualsInput(input))
						return cacheCBASTestingIndicator3[idx];
			return CacheIndicator<CBASTestingIndicator3>(new CBASTestingIndicator3(){ ShowExitLabels = showExitLabels, ExitLabelAtrOffset = exitLabelAtrOffset, UseRegimeStability = useRegimeStability, RegimeStabilityBars = regimeStabilityBars, UseScoringFilter = useScoringFilter, ScoreThreshold = scoreThreshold, UseSmoothedVpm = useSmoothedVpm, VpmEmaSpan = vpmEmaSpan, MinVpm = minVpm, MinAdx = minAdx, MomentumLookback = momentumLookback, ExtendedLogging = extendedLogging, ComputeExitSignals = computeExitSignals, ExitProfitAtrMult = exitProfitAtrMult, InstanceId = instanceId, Sensitivity = sensitivity, EmaEnergy = emaEnergy, KeltnerLength = keltnerLength, AtrLength = atrLength, RangeMinLength = rangeMinLength, RangeWidthMult = rangeWidthMult, RangeAtrLen = rangeAtrLen, EnableLogging = enableLogging, LogSignalsOnly = logSignalsOnly, HeartbeatEveryNBars = heartbeatEveryNBars, LogFolder = logFolder, ScaleOscillatorToATR = scaleOscillatorToATR, OscAtrMult = oscAtrMult, LogDrawnSignals = logDrawnSignals, DebugLogSignalCheck = debugLogSignalCheck, ColorBarsByTrend = colorBarsByTrend, UseEmaSpreadFilter = useEmaSpreadFilter, MinEmaSpread = minEmaSpread, RealtimeBullNetflowMin = realtimeBullNetflowMin, RealtimeBullObjectionMax = realtimeBullObjectionMax, RealtimeBullEmaColorMin = realtimeBullEmaColorMin, RealtimeBullUseAttract = realtimeBullUseAttract, RealtimeBullAttractMin = realtimeBullAttractMin, RealtimeBullScoreMin = realtimeBullScoreMin, RealtimeBearNetflowMax = realtimeBearNetflowMax, RealtimeBearObjectionMin = realtimeBearObjectionMin, RealtimeBearEmaColorMax = realtimeBearEmaColorMax, RealtimeBearUsePriceToBand = realtimeBearUsePriceToBand, RealtimeBearPriceToBandMax = realtimeBearPriceToBandMax, RealtimeBearScoreMin = realtimeBearScoreMin, RealtimeFlatTolerance = realtimeFlatTolerance, ShowRealtimeStatePlot = showRealtimeStatePlot, PlotRealtimeSignals = plotRealtimeSignals, FlipConfirmationBars = flipConfirmationBars }, input, ref cacheCBASTestingIndicator3);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.CBASTestingIndicator3 CBASTestingIndicator3(bool showExitLabels, double exitLabelAtrOffset, bool useRegimeStability, int regimeStabilityBars, bool useScoringFilter, int scoreThreshold, bool useSmoothedVpm, int vpmEmaSpan, double minVpm, int minAdx, int momentumLookback, bool extendedLogging, bool computeExitSignals, double exitProfitAtrMult, string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder, bool scaleOscillatorToATR, double oscAtrMult, bool logDrawnSignals, bool debugLogSignalCheck, bool colorBarsByTrend, bool useEmaSpreadFilter, double minEmaSpread, double realtimeBullNetflowMin, double realtimeBullObjectionMax, double realtimeBullEmaColorMin, bool realtimeBullUseAttract, double realtimeBullAttractMin, double realtimeBullScoreMin, double realtimeBearNetflowMax, double realtimeBearObjectionMin, double realtimeBearEmaColorMax, bool realtimeBearUsePriceToBand, double realtimeBearPriceToBandMax, double realtimeBearScoreMin, double realtimeFlatTolerance, bool showRealtimeStatePlot, bool plotRealtimeSignals, int flipConfirmationBars)
		{
			return indicator.CBASTestingIndicator3(Input, showExitLabels, exitLabelAtrOffset, useRegimeStability, regimeStabilityBars, useScoringFilter, scoreThreshold, useSmoothedVpm, vpmEmaSpan, minVpm, minAdx, momentumLookback, extendedLogging, computeExitSignals, exitProfitAtrMult, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder, scaleOscillatorToATR, oscAtrMult, logDrawnSignals, debugLogSignalCheck, colorBarsByTrend, useEmaSpreadFilter, minEmaSpread, realtimeBullNetflowMin, realtimeBullObjectionMax, realtimeBullEmaColorMin, realtimeBullUseAttract, realtimeBullAttractMin, realtimeBullScoreMin, realtimeBearNetflowMax, realtimeBearObjectionMin, realtimeBearEmaColorMax, realtimeBearUsePriceToBand, realtimeBearPriceToBandMax, realtimeBearScoreMin, realtimeFlatTolerance, showRealtimeStatePlot, plotRealtimeSignals, flipConfirmationBars);
		}

		public Indicators.CBASTestingIndicator3 CBASTestingIndicator3(ISeries<double> input , bool showExitLabels, double exitLabelAtrOffset, bool useRegimeStability, int regimeStabilityBars, bool useScoringFilter, int scoreThreshold, bool useSmoothedVpm, int vpmEmaSpan, double minVpm, int minAdx, int momentumLookback, bool extendedLogging, bool computeExitSignals, double exitProfitAtrMult, string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder, bool scaleOscillatorToATR, double oscAtrMult, bool logDrawnSignals, bool debugLogSignalCheck, bool colorBarsByTrend, bool useEmaSpreadFilter, double minEmaSpread, double realtimeBullNetflowMin, double realtimeBullObjectionMax, double realtimeBullEmaColorMin, bool realtimeBullUseAttract, double realtimeBullAttractMin, double realtimeBullScoreMin, double realtimeBearNetflowMax, double realtimeBearObjectionMin, double realtimeBearEmaColorMax, bool realtimeBearUsePriceToBand, double realtimeBearPriceToBandMax, double realtimeBearScoreMin, double realtimeFlatTolerance, bool showRealtimeStatePlot, bool plotRealtimeSignals, int flipConfirmationBars)
		{
			return indicator.CBASTestingIndicator3(input, showExitLabels, exitLabelAtrOffset, useRegimeStability, regimeStabilityBars, useScoringFilter, scoreThreshold, useSmoothedVpm, vpmEmaSpan, minVpm, minAdx, momentumLookback, extendedLogging, computeExitSignals, exitProfitAtrMult, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder, scaleOscillatorToATR, oscAtrMult, logDrawnSignals, debugLogSignalCheck, colorBarsByTrend, useEmaSpreadFilter, minEmaSpread, realtimeBullNetflowMin, realtimeBullObjectionMax, realtimeBullEmaColorMin, realtimeBullUseAttract, realtimeBullAttractMin, realtimeBullScoreMin, realtimeBearNetflowMax, realtimeBearObjectionMin, realtimeBearEmaColorMax, realtimeBearUsePriceToBand, realtimeBearPriceToBandMax, realtimeBearScoreMin, realtimeFlatTolerance, showRealtimeStatePlot, plotRealtimeSignals, flipConfirmationBars);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.CBASTestingIndicator3 CBASTestingIndicator3(bool showExitLabels, double exitLabelAtrOffset, bool useRegimeStability, int regimeStabilityBars, bool useScoringFilter, int scoreThreshold, bool useSmoothedVpm, int vpmEmaSpan, double minVpm, int minAdx, int momentumLookback, bool extendedLogging, bool computeExitSignals, double exitProfitAtrMult, string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder, bool scaleOscillatorToATR, double oscAtrMult, bool logDrawnSignals, bool debugLogSignalCheck, bool colorBarsByTrend, bool useEmaSpreadFilter, double minEmaSpread, double realtimeBullNetflowMin, double realtimeBullObjectionMax, double realtimeBullEmaColorMin, bool realtimeBullUseAttract, double realtimeBullAttractMin, double realtimeBullScoreMin, double realtimeBearNetflowMax, double realtimeBearObjectionMin, double realtimeBearEmaColorMax, bool realtimeBearUsePriceToBand, double realtimeBearPriceToBandMax, double realtimeBearScoreMin, double realtimeFlatTolerance, bool showRealtimeStatePlot, bool plotRealtimeSignals, int flipConfirmationBars)
		{
			return indicator.CBASTestingIndicator3(Input, showExitLabels, exitLabelAtrOffset, useRegimeStability, regimeStabilityBars, useScoringFilter, scoreThreshold, useSmoothedVpm, vpmEmaSpan, minVpm, minAdx, momentumLookback, extendedLogging, computeExitSignals, exitProfitAtrMult, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder, scaleOscillatorToATR, oscAtrMult, logDrawnSignals, debugLogSignalCheck, colorBarsByTrend, useEmaSpreadFilter, minEmaSpread, realtimeBullNetflowMin, realtimeBullObjectionMax, realtimeBullEmaColorMin, realtimeBullUseAttract, realtimeBullAttractMin, realtimeBullScoreMin, realtimeBearNetflowMax, realtimeBearObjectionMin, realtimeBearEmaColorMax, realtimeBearUsePriceToBand, realtimeBearPriceToBandMax, realtimeBearScoreMin, realtimeFlatTolerance, showRealtimeStatePlot, plotRealtimeSignals, flipConfirmationBars);
		}

		public Indicators.CBASTestingIndicator3 CBASTestingIndicator3(ISeries<double> input , bool showExitLabels, double exitLabelAtrOffset, bool useRegimeStability, int regimeStabilityBars, bool useScoringFilter, int scoreThreshold, bool useSmoothedVpm, int vpmEmaSpan, double minVpm, int minAdx, int momentumLookback, bool extendedLogging, bool computeExitSignals, double exitProfitAtrMult, string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder, bool scaleOscillatorToATR, double oscAtrMult, bool logDrawnSignals, bool debugLogSignalCheck, bool colorBarsByTrend, bool useEmaSpreadFilter, double minEmaSpread, double realtimeBullNetflowMin, double realtimeBullObjectionMax, double realtimeBullEmaColorMin, bool realtimeBullUseAttract, double realtimeBullAttractMin, double realtimeBullScoreMin, double realtimeBearNetflowMax, double realtimeBearObjectionMin, double realtimeBearEmaColorMax, bool realtimeBearUsePriceToBand, double realtimeBearPriceToBandMax, double realtimeBearScoreMin, double realtimeFlatTolerance, bool showRealtimeStatePlot, bool plotRealtimeSignals, int flipConfirmationBars)
		{
			return indicator.CBASTestingIndicator3(input, showExitLabels, exitLabelAtrOffset, useRegimeStability, regimeStabilityBars, useScoringFilter, scoreThreshold, useSmoothedVpm, vpmEmaSpan, minVpm, minAdx, momentumLookback, extendedLogging, computeExitSignals, exitProfitAtrMult, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder, scaleOscillatorToATR, oscAtrMult, logDrawnSignals, debugLogSignalCheck, colorBarsByTrend, useEmaSpreadFilter, minEmaSpread, realtimeBullNetflowMin, realtimeBullObjectionMax, realtimeBullEmaColorMin, realtimeBullUseAttract, realtimeBullAttractMin, realtimeBullScoreMin, realtimeBearNetflowMax, realtimeBearObjectionMin, realtimeBearEmaColorMax, realtimeBearUsePriceToBand, realtimeBearPriceToBandMax, realtimeBearScoreMin, realtimeFlatTolerance, showRealtimeStatePlot, plotRealtimeSignals, flipConfirmationBars);
		}
	}
}

#endregion
