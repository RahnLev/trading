using System;
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
using NinjaTrader.Gui.Tools;            // NTWindow (if you use it)
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns;   // CBASTerminalWindow
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.SuperDomColumns;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class CBASTestingIndicator4 : Indicator
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
        public bool UseRegimeStability { get; set; } = true; // default: ON for high-conviction

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Regime Stability Bars", Order = 21, GroupName = "Filters")]
        public int RegimeStabilityBars { get; set; } = 15;   // default: stricter

        [NinjaScriptProperty]
        [Display(Name = "Use Scoring Filter", Order = 22, GroupName = "Filters")]
        public bool UseScoringFilter { get; set; } = true;   // default: ON for high-conviction

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Score Threshold", Order = 23, GroupName = "Filters")]
        public int ScoreThreshold { get; set; } = 8;         // default: higher threshold

        [NinjaScriptProperty]
        [Display(Name = "Use Smoothed VPM", Order = 24, GroupName = "Filters")]
        public bool UseSmoothedVpm { get; set; } = true;     // default: ON

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "VPM EMA Span", Order = 25, GroupName = "Filters")]
        public int VpmEmaSpan { get; set; } = 21;            // default: smoother

        // Confirmation thresholds (used by scoring if enabled)
        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Min VPM", Order = 26, GroupName = "Filters")]
        public double MinVpm { get; set; } = 300;            // default: robust activity

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Min ADX", Order = 27, GroupName = "Filters")]
        public int MinAdx { get; set; } = 30;                // default: stronger trend required

        // Momentum lookback (for features and optional scoring)
        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Momentum Lookback", Order = 28, GroupName = "Filters")]
        public int MomentumLookback { get; set; } = 14;

        // Logging extras (appended columns; off by default to avoid changing your CSV)
        [NinjaScriptProperty]
        [Display(Name = "Extended Logging", Order = 29, GroupName = "Logging")]
        public bool ExtendedLogging { get; set; } = false;

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

        // Logging inputs
        [NinjaScriptProperty]
        [Display(Name = "Enable Logging", Order = 1, GroupName = "Logging")]
        public bool EnableLogging { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Log Signals Only", Order = 2, GroupName = "Logging")]
        public bool LogSignalsOnly { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 1000000)]
        [Display(Name = "Heartbeat Every N Bars (0 = off)", Order = 3, GroupName = "Logging")]
        public int HeartbeatEveryNBars { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Log Folder", Order = 4, GroupName = "Logging")]
        public string LogFolder { get; set; } = null;

        // Optional: scale oscillator into price units to be visible on price panel
        [NinjaScriptProperty]
        [Display(Name = "Scale Oscillator To ATR", Order = 40, GroupName = "Plots")]
        public bool ScaleOscillatorToATR { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Osc ATR Mult", Order = 41, GroupName = "Plots")]
        public double OscAtrMult { get; set; } = 0.3;

        // NEW: Ultra-conservative “sure-thing” filters (defaults are strict)
        [NinjaScriptProperty]
        [Display(Name = "Require EMA Slope", Order = 50, GroupName = "HighConviction")]
        public bool RequireEmaSlope { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0.0, 5.0)]
        [Display(Name = "Min Cross ATR Distance", Order = 51, GroupName = "HighConviction")]
        public double MinCrossAtrDistance { get; set; } = 0.25;

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Max Opposing Wick Ratio", Order = 52, GroupName = "HighConviction")]
        public double MaxOpposingWickRatio { get; set; } = 0.30;

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Min Bars Between Signals", Order = 53, GroupName = "HighConviction")]
        public int MinBarsBetweenSignals { get; set; } = 10;

        [NinjaScriptProperty]
        [Display(Name = "Require Range Break and Hold", Order = 54, GroupName = "HighConviction")]
        public bool RequireRangeBreakAndHold { get; set; } = true;
        #endregion

        #region privates
        private int plotAttract;
        private int plotObjection;
        private int plotNetFlow;

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
        private int lastHeartbeatBar = -1;

        private double lastLoggedClosePrice = double.NaN;
        private int lastLoggedBarIndex = -1;

        // New guards to avoid repeat signals
        private int lastLongSignalBar = -1000000;
        private int lastShortSignalBar = -1000000;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                AddPlot(Brushes.SeaGreen, "Attract");
                AddPlot(Brushes.IndianRed, "Objection");
                AddPlot(Brushes.DodgerBlue, "NetFlow");

                InstanceId = string.Empty;
                Description = "CBAS Terminal AddOn";
                Name = "CBASTestingIndicator4";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose; // bar-close only by default (non-repainting signals)
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

                if (string.IsNullOrWhiteSpace(InstanceId))
                    InstanceId = Guid.NewGuid().ToString("N");
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

                plotSuperTrend = FindPlot("SuperTrend");
                plotUpperBand = FindPlot("UpperBand");
                plotLowerBand = FindPlot("LowerBand");

                energyPlotIdx = new int[15];
                for (int i = 0; i < 15; i++)
                    energyPlotIdx[i] = FindPlot($"EnergyEMA{i + 1}");

                plotRangeTop = FindPlot("RangeTop");
                plotRangeBottom = FindPlot("RangeBottom");
                plotRangeMid = FindPlot("RangeMid");

                plotAttract = FindPlot("Attract");
                plotObjection = FindPlot("Objection");
                plotNetFlow = FindPlot("NetFlow");

                if (new[] { plotSuperTrend, plotUpperBand, plotLowerBand, plotRangeTop, plotRangeBottom, plotRangeMid, plotAttract, plotObjection, plotNetFlow }.Any(x => x < 0))
                    Print("[CBASTestingIndicator4] Plot index mapping error: one or more plots not found by name.");

                InitLogger();

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

                // Optional UI AddOn window, kept as-is
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var win = new CBASTerminalWindow();
                    win.Owner = Application.Current.MainWindow as Window;
                    win.Show();
                    win.Activate();
                    // MessageBox intentionally left to original behavior
                    MessageBox.Show("AddOn Active");
                }));
            }
            else if (State == State.Terminated)
            {
                try
                {
                    lock (logLock)
                    {
                        logWriter?.Flush();
                        logWriter?.Dispose();
                        logWriter = null;
                        logInitialized = false;
                    }
                }
                catch { /* ignore */ }
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(2, KeltnerLength))
            {
                InitializeFirstBars();
                return;
            }

            if (EnableLogging && !logInitialized)
                InitLogger();

            // Bands and supertrend (same construction, computed at bar close)
            double rangeC = 2.0 * (High[0] - Low[0]);

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

            // IMPORTANT: Non-repainting cross vs prior bar’s supertrend
            bool bullCrossRaw = (Close[1] <= superTrend[1]) && (Close[0] > superTrend[1]);
            bool bearCrossRaw = (Close[1] >= superTrend[1]) && (Close[0] < superTrend[1]);

            // Start with these raw crosses; we will gate them heavily
            bool bullCandidate = bullCrossRaw;
            bool bearCandidate = bearCrossRaw;

            // Additional high-conviction hard filters
            bool adxOk = adx14 != null && adx14.IsValidDataPoint(0) && adx14[0] >= MinAdx;
            bool adxRising = adx14 != null && adx14.IsValidDataPoint(1) && adx14[0] > adx14[1];

            bool vpmOk = (!double.IsNaN(vpmUse) && vpmUse >= MinVpm);
            bool emaAlignBull = Close[0] > ema10Close[0] && ema10Close[0] > ema20Close[0];
            bool emaAlignBear = Close[0] < ema10Close[0] && ema10Close[0] < ema20Close[0];
            bool emaSlopeBull = ema10Close[0] > ema10Close[1] && ema20Close[0] > ema20Close[1];
            bool emaSlopeBear = ema10Close[0] < ema10Close[1] && ema20Close[0] < ema20Close[1];

            // Opposing wick quality
            double rng = Math.Max(1e-6, High[0] - Low[0]);
            double upperWick = (High[0] - Close[0]) / rng; // 0..1
            double lowerWick = (Close[0] - Low[0]) / rng;  // 0..1
            bool wickOkBull = upperWick <= MaxOpposingWickRatio;
            bool wickOkBear = lowerWick <= MaxOpposingWickRatio;

            // Min ATR distance beyond supertrend
            double distBull = Close[0] - superTrend[1];
            double distBear = superTrend[1] - Close[0];
            bool atrDistOkBull = !double.IsNaN(atr30ValNow) && atr30ValNow > 0 && (distBull >= MinCrossAtrDistance * atr30ValNow);
            bool atrDistOkBear = !double.IsNaN(atr30ValNow) && atr30ValNow > 0 && (distBear >= MinCrossAtrDistance * atr30ValNow);

            // Range break and hold (two bar closes outside the range)
            bool haveRange = !double.IsNaN(rangeMax) && !double.IsNaN(rangeMin);
            bool rangeBreakHoldBull = haveRange && Close[1] > rangeMax && Close[0] > rangeMax;
            bool rangeBreakHoldBear = haveRange && Close[1] < rangeMin && Close[0] < rangeMin;

            // Regime stability (supertrend regime consistent last N bars)
            bool regimeStable = true;
            if (UseRegimeStability)
            {
                int priorDir = 0;
                if (Close[1] > superTrend[1]) priorDir = +1;
                else if (Close[1] < superTrend[1]) priorDir = -1;
                regimeStable = RegimeStable(priorDir, RegimeStabilityBars);
            }

            // Compose strict gating
            if (bullCandidate)
            {
                bool hardPass =
                    adxOk && adxRising &&
                    vpmOk &&
                    emaAlignBull &&
                    (!RequireEmaSlope || emaSlopeBull) &&
                    wickOkBull &&
                    atrDistOkBull &&
                    (!RequireRangeBreakAndHold || rangeBreakHoldBull) &&
                    regimeStable &&
                    (CurrentBar - lastLongSignalBar >= MinBarsBetweenSignals);

                bull = hardPass;
            }
            else bull = false;

            if (bearCandidate)
            {
                bool hardPass =
                    adxOk && adxRising &&  // require trend strengthening; for shorts this still implies directional strength
                    vpmOk &&
                    emaAlignBear &&
                    (!RequireEmaSlope || emaSlopeBear) &&
                    wickOkBear &&
                    atrDistOkBear &&
                    (!RequireRangeBreakAndHold || rangeBreakHoldBear) &&
                    regimeStable &&
                    (CurrentBar - lastShortSignalBar >= MinBarsBetweenSignals);

                bear = hardPass;
            }
            else bear = false;

            // Optional scoring gate on top (conservative)
            if (UseScoringFilter && (bullCandidate || bearCandidate))
            {
                double vpmForScore = UseSmoothedVpm
                    ? (vpmEmaInd != null && vpmEmaInd.IsValidDataPoint(0) ? vpmEmaInd[0] : double.NaN)
                    : vpmSeries[0];

                bool dummy;
                int bullScore = ComputeScore(bullCandidate, false, this.superTrendNow, vpmForScore, out dummy);
                int bearScore = ComputeScore(false, bearCandidate, this.superTrendNow, vpmForScore, out dummy);

                if (bull) bull = bullScore >= ScoreThreshold;
                if (bear) bear = bearScore >= ScoreThreshold;
            }

            // Register last-signal bar (one signal per direction per bar)
            if (bull)
                lastLongSignalBar = CurrentBar;
            if (bear)
                lastShortSignalBar = CurrentBar;

            // Triangles only on passed signals
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

            // Optional exits for visualization
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

            if (CurrentBar > 0)
                BarBrushes[0] = Close[0] > superTrend[1] ? Brushes.Teal : Brushes.Red;

            // Energy EMAs visualization
            for (int i = 0; i < emaHighs.Length; i++)
            {
                double v = emaHighs[i][0];
                int pIdx = energyPlotIdx[i];
                SetPlotVal(pIdx, v);
                SetPlotBrush(pIdx, EmaEnergy ? (Close[0] >= v ? Brushes.Teal : Brushes.Red) : Brushes.Transparent);
            }

            // Attract/Objection/NetFlow
            var ao = ComputeAttractObjection(UseSmoothedVpm);
            double net = ao.attract - ao.objection;

            if (ScaleOscillatorToATR)
            {
                double basePx = stNow;
                double scale = Math.Max(1e-9, atr30[0]) * OscAtrMult;

                double attractScaled = basePx + scale * (ao.attract - 5.0);
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

            if (EnableLogging)
                MaybeLogRow(bull, bear, stNow, upperBand, lowerBand, ao.attract, ao.objection, net);
        }

        private void InitializeFirstBars()
        {
            double rangeC = 2.0 * (High[0] - Low[0]);
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
            double st = this.superTrendNow;

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
            if (adxDelta < 0) objection += 0.5;
            double p2bVal = (priceToBand != null) ? priceToBand[0] : double.NaN;
            if (!double.IsNaN(p2bVal) && p2bVal > 0.9) objection += 1.0;
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

        private double ComputeVpm()
        {
            try
            {
                if (BarsPeriod?.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value > 0)
                {
                    double minutes = BarsPeriod.Value;
                    return minutes > 0 ? Volume[0] / minutes : double.NaN;
                }

                if (CurrentBar > 0)
                {
                    double minutes = (Time[0] - Time[1]).TotalMinutes;
                    return minutes > 0 ? Volume[0] / minutes : double.NaN;
                }
            }
            catch
            {
            }
            return double.NaN;
        }

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

                string fileName = $"{Name}_{(Instrument?.FullName ?? "NA")}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                logPath = Path.Combine(folder, fileName);

                bool isNewFile = !File.Exists(logPath) || new FileInfo(logPath).Length == 0;

                logWriter = new StreamWriter(logPath, append: true, encoding: Encoding.UTF8) { AutoFlush = true };
                logInitialized = true;

                if (isNewFile)
                {
                    var header = string.Join(",",
                        "ts_local",
                        "bar_time_utc",
                        "bar_index",
                        "instrument",
                        "open",
                        "high",
                        "low",
                        "close",
                        "vpm",
                        "attract",
                        "objection",
                        "netflow",
                        "supertrend",
                        "upper_band",
                        "lower_band",
                        "bull_cross",
                        "bear_cross",
                        "regime",
                        "atr30",
                        "ema10",
                        "ema20",
                        "energy_ema_first",
                        "range_max",
                        "range_min",
                        "range_mid",
                        "range_os",
                        "range_count",
                        "param_Sensitivity",
                        "param_KeltnerLength",
                        "param_RangeMinLength",
                        "param_RangeWidthMult",
                        "param_RangeAtrLen",
                        "param_EnableLogging",
                        "param_LogSignalsOnly",
                        "param_HeartbeatEveryNBars",
                        "param_LogFolder"
                    );

                    if (ExtendedLogging)
                    {
                        header += "," + string.Join(",",
                            "vpm_smooth",
                            "score_bull",
                            "score_bear",
                            "adx",
                            "atr_ratio",
                            "ema_spread",
                            "price_to_band",
                            "momentum",
                            "range_break",
                            "exit_long",
                            "exit_short"
                        );
                    }

                    SafeWriteLine(header);
                }

                Print("[CBASTestingIndicator4] Logging to: " + logPath);
            }
            catch (Exception ex)
            {
                Print("[CBASTestingIndicator4] Log init failed: " + ex.Message);
                logInitialized = false;
            }
        }

        private void MaybeLogRow(bool bull, bool bear, double superTrendNow, double upperBand, double lowerBand,
                                 double attract, double objection, double netFlow)
        {
            if (!logInitialized) return;

            if (CurrentBar != lastLoggedBarIndex)
            {
                lastLoggedBarIndex = CurrentBar;
                lastLoggedClosePrice = double.NaN;
            }
            bool closeChanged = double.IsNaN(lastLoggedClosePrice) || !ApproximatelyEqual(Close[0], lastLoggedClosePrice);

            bool inBullRegime = Close[0] > superTrendNow;
            bool inBearRegime = Close[0] < superTrendNow;

            bool wantHeartbeat = HeartbeatEveryNBars > 0
                                 && CurrentBar >= RangeMinLength
                                 && (CurrentBar == 0 || (CurrentBar - lastHeartbeatBar) >= HeartbeatEveryNBars);

            bool baseCondition = bull || bear || (!LogSignalsOnly);
            bool shouldLog = (baseCondition && closeChanged) || wantHeartbeat;
            if (!shouldLog) return;

            if (wantHeartbeat)
                lastHeartbeatBar = CurrentBar;

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
                barTimeUtc: ToBarTimeUtc(Time[0]),
                barTimeLocal: Time[0],
                instrument: Instrument?.FullName ?? "",
                barIndex: CurrentBar,
                open: Open[0],
                high: High[0],
                low: Low[0],
                close: Close[0],
                vpm: vpm,
                attract: attract,
                objection: objection,
                netFlow: netFlow,
                st: superTrendNow,
                ub: upperBand,
                lb: lowerBand,
                bull: bull,
                bear: bear,
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

            lastLoggedClosePrice = Close[0];
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
            double st,
            double ub,
            double lb,
            bool bull,
            bool bear,
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
                CsvEscape(instrument ?? ""),
                CsvNum(open),
                CsvNum(high),
                CsvNum(low),
                CsvNum(close),
                CsvNum(vpm),
                CsvNum(attract),
                CsvNum(objection),
                CsvNum(netFlow),
                CsvNum(st),
                CsvNum(ub),
                CsvNum(lb),
                bull ? "1" : "0",
                bear ? "1" : "0",
                CsvEscape(regime ?? ""),
                CsvNum(atr30),
                CsvNum(ema10),
                CsvNum(ema20),
                CsvNum(energyEmaFirst),
                CsvNum(rMax),
                CsvNum(rMin),
                CsvNum(rMid),
                rOs.ToString(),
                (rCount == int.MinValue ? "" : rCount.ToString()),
                CsvNum(Sensitivity),
                KeltnerLength.ToString(),
                RangeMinLength.ToString(),
                CsvNum(RangeWidthMult),
                RangeAtrLen.ToString(),
                (EnableLogging ? "1" : "0"),
                (LogSignalsOnly ? "1" : "0"),
                HeartbeatEveryNBars.ToString(),
                CsvEscape(string.IsNullOrEmpty(LogFolder) ? Path.Combine(Globals.UserDataDir, "Indicator_logs") : LogFolder)
            );

            if (ExtendedLogging)
            {
                double vpmRaw = vpm;
                double vpmSmooth = (UseSmoothedVpm && vpmEmaInd != null && CurrentBar >= VpmEmaSpan - 1) ? vpmEmaInd[0] : double.NaN;
                double vpmUse = UseSmoothedVpm ? vpmSmooth : vpmRaw;

                bool dummyBull, dummyBear;
                int scBull = ComputeScore(bull, false, this.superTrendNow, vpmUse, out dummyBull);
                int scBear = ComputeScore(false, bear, this.superTrendNow, vpmUse, out dummyBear);

                double adxVal = (adx14 != null && CurrentBar >= 14 - 1) ? adx14[0] : double.NaN;
                double atrRatioVal = (CurrentBar >= 30 - 1) ? atrRatio[0] : double.NaN;
                double emaSpreadVal = (CurrentBar >= 30 - 1) ? emaSpread[0] : double.NaN;
                double priceToBandVal = (priceToBand != null) ? priceToBand[0] : double.NaN;
                double momentumVal = (CurrentBar >= MomentumLookback) ? momentum[0] : double.NaN;

                bool rangeBreak = (!double.IsNaN(rangeMax) && Close[0] > rangeMax)
                               || (!double.IsNaN(rangeMin) && Close[0] < rangeMin);

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
                    CsvNum(momentumVal),
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
                Print($"[CBASTestingIndicator4] Log write failed: {ex.Message}");
            }
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
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private CBASTestingIndicator4[] cacheCBASTestingIndicator4;
		public CBASTestingIndicator4 CBASTestingIndicator4(bool showExitLabels, double exitLabelAtrOffset, bool useRegimeStability, int regimeStabilityBars, bool useScoringFilter, int scoreThreshold, bool useSmoothedVpm, int vpmEmaSpan, double minVpm, int minAdx, int momentumLookback, bool extendedLogging, bool computeExitSignals, double exitProfitAtrMult, string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder, bool scaleOscillatorToATR, double oscAtrMult, bool requireEmaSlope, double minCrossAtrDistance, double maxOpposingWickRatio, int minBarsBetweenSignals, bool requireRangeBreakAndHold)
		{
			return CBASTestingIndicator4(Input, showExitLabels, exitLabelAtrOffset, useRegimeStability, regimeStabilityBars, useScoringFilter, scoreThreshold, useSmoothedVpm, vpmEmaSpan, minVpm, minAdx, momentumLookback, extendedLogging, computeExitSignals, exitProfitAtrMult, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder, scaleOscillatorToATR, oscAtrMult, requireEmaSlope, minCrossAtrDistance, maxOpposingWickRatio, minBarsBetweenSignals, requireRangeBreakAndHold);
		}

		public CBASTestingIndicator4 CBASTestingIndicator4(ISeries<double> input, bool showExitLabels, double exitLabelAtrOffset, bool useRegimeStability, int regimeStabilityBars, bool useScoringFilter, int scoreThreshold, bool useSmoothedVpm, int vpmEmaSpan, double minVpm, int minAdx, int momentumLookback, bool extendedLogging, bool computeExitSignals, double exitProfitAtrMult, string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder, bool scaleOscillatorToATR, double oscAtrMult, bool requireEmaSlope, double minCrossAtrDistance, double maxOpposingWickRatio, int minBarsBetweenSignals, bool requireRangeBreakAndHold)
		{
			if (cacheCBASTestingIndicator4 != null)
				for (int idx = 0; idx < cacheCBASTestingIndicator4.Length; idx++)
					if (cacheCBASTestingIndicator4[idx] != null && cacheCBASTestingIndicator4[idx].ShowExitLabels == showExitLabels && cacheCBASTestingIndicator4[idx].ExitLabelAtrOffset == exitLabelAtrOffset && cacheCBASTestingIndicator4[idx].UseRegimeStability == useRegimeStability && cacheCBASTestingIndicator4[idx].RegimeStabilityBars == regimeStabilityBars && cacheCBASTestingIndicator4[idx].UseScoringFilter == useScoringFilter && cacheCBASTestingIndicator4[idx].ScoreThreshold == scoreThreshold && cacheCBASTestingIndicator4[idx].UseSmoothedVpm == useSmoothedVpm && cacheCBASTestingIndicator4[idx].VpmEmaSpan == vpmEmaSpan && cacheCBASTestingIndicator4[idx].MinVpm == minVpm && cacheCBASTestingIndicator4[idx].MinAdx == minAdx && cacheCBASTestingIndicator4[idx].MomentumLookback == momentumLookback && cacheCBASTestingIndicator4[idx].ExtendedLogging == extendedLogging && cacheCBASTestingIndicator4[idx].ComputeExitSignals == computeExitSignals && cacheCBASTestingIndicator4[idx].ExitProfitAtrMult == exitProfitAtrMult && cacheCBASTestingIndicator4[idx].InstanceId == instanceId && cacheCBASTestingIndicator4[idx].Sensitivity == sensitivity && cacheCBASTestingIndicator4[idx].EmaEnergy == emaEnergy && cacheCBASTestingIndicator4[idx].KeltnerLength == keltnerLength && cacheCBASTestingIndicator4[idx].AtrLength == atrLength && cacheCBASTestingIndicator4[idx].RangeMinLength == rangeMinLength && cacheCBASTestingIndicator4[idx].RangeWidthMult == rangeWidthMult && cacheCBASTestingIndicator4[idx].RangeAtrLen == rangeAtrLen && cacheCBASTestingIndicator4[idx].EnableLogging == enableLogging && cacheCBASTestingIndicator4[idx].LogSignalsOnly == logSignalsOnly && cacheCBASTestingIndicator4[idx].HeartbeatEveryNBars == heartbeatEveryNBars && cacheCBASTestingIndicator4[idx].LogFolder == logFolder && cacheCBASTestingIndicator4[idx].ScaleOscillatorToATR == scaleOscillatorToATR && cacheCBASTestingIndicator4[idx].OscAtrMult == oscAtrMult && cacheCBASTestingIndicator4[idx].RequireEmaSlope == requireEmaSlope && cacheCBASTestingIndicator4[idx].MinCrossAtrDistance == minCrossAtrDistance && cacheCBASTestingIndicator4[idx].MaxOpposingWickRatio == maxOpposingWickRatio && cacheCBASTestingIndicator4[idx].MinBarsBetweenSignals == minBarsBetweenSignals && cacheCBASTestingIndicator4[idx].RequireRangeBreakAndHold == requireRangeBreakAndHold && cacheCBASTestingIndicator4[idx].EqualsInput(input))
						return cacheCBASTestingIndicator4[idx];
			return CacheIndicator<CBASTestingIndicator4>(new CBASTestingIndicator4(){ ShowExitLabels = showExitLabels, ExitLabelAtrOffset = exitLabelAtrOffset, UseRegimeStability = useRegimeStability, RegimeStabilityBars = regimeStabilityBars, UseScoringFilter = useScoringFilter, ScoreThreshold = scoreThreshold, UseSmoothedVpm = useSmoothedVpm, VpmEmaSpan = vpmEmaSpan, MinVpm = minVpm, MinAdx = minAdx, MomentumLookback = momentumLookback, ExtendedLogging = extendedLogging, ComputeExitSignals = computeExitSignals, ExitProfitAtrMult = exitProfitAtrMult, InstanceId = instanceId, Sensitivity = sensitivity, EmaEnergy = emaEnergy, KeltnerLength = keltnerLength, AtrLength = atrLength, RangeMinLength = rangeMinLength, RangeWidthMult = rangeWidthMult, RangeAtrLen = rangeAtrLen, EnableLogging = enableLogging, LogSignalsOnly = logSignalsOnly, HeartbeatEveryNBars = heartbeatEveryNBars, LogFolder = logFolder, ScaleOscillatorToATR = scaleOscillatorToATR, OscAtrMult = oscAtrMult, RequireEmaSlope = requireEmaSlope, MinCrossAtrDistance = minCrossAtrDistance, MaxOpposingWickRatio = maxOpposingWickRatio, MinBarsBetweenSignals = minBarsBetweenSignals, RequireRangeBreakAndHold = requireRangeBreakAndHold }, input, ref cacheCBASTestingIndicator4);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.CBASTestingIndicator4 CBASTestingIndicator4(bool showExitLabels, double exitLabelAtrOffset, bool useRegimeStability, int regimeStabilityBars, bool useScoringFilter, int scoreThreshold, bool useSmoothedVpm, int vpmEmaSpan, double minVpm, int minAdx, int momentumLookback, bool extendedLogging, bool computeExitSignals, double exitProfitAtrMult, string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder, bool scaleOscillatorToATR, double oscAtrMult, bool requireEmaSlope, double minCrossAtrDistance, double maxOpposingWickRatio, int minBarsBetweenSignals, bool requireRangeBreakAndHold)
		{
			return indicator.CBASTestingIndicator4(Input, showExitLabels, exitLabelAtrOffset, useRegimeStability, regimeStabilityBars, useScoringFilter, scoreThreshold, useSmoothedVpm, vpmEmaSpan, minVpm, minAdx, momentumLookback, extendedLogging, computeExitSignals, exitProfitAtrMult, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder, scaleOscillatorToATR, oscAtrMult, requireEmaSlope, minCrossAtrDistance, maxOpposingWickRatio, minBarsBetweenSignals, requireRangeBreakAndHold);
		}

		public Indicators.CBASTestingIndicator4 CBASTestingIndicator4(ISeries<double> input , bool showExitLabels, double exitLabelAtrOffset, bool useRegimeStability, int regimeStabilityBars, bool useScoringFilter, int scoreThreshold, bool useSmoothedVpm, int vpmEmaSpan, double minVpm, int minAdx, int momentumLookback, bool extendedLogging, bool computeExitSignals, double exitProfitAtrMult, string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder, bool scaleOscillatorToATR, double oscAtrMult, bool requireEmaSlope, double minCrossAtrDistance, double maxOpposingWickRatio, int minBarsBetweenSignals, bool requireRangeBreakAndHold)
		{
			return indicator.CBASTestingIndicator4(input, showExitLabels, exitLabelAtrOffset, useRegimeStability, regimeStabilityBars, useScoringFilter, scoreThreshold, useSmoothedVpm, vpmEmaSpan, minVpm, minAdx, momentumLookback, extendedLogging, computeExitSignals, exitProfitAtrMult, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder, scaleOscillatorToATR, oscAtrMult, requireEmaSlope, minCrossAtrDistance, maxOpposingWickRatio, minBarsBetweenSignals, requireRangeBreakAndHold);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.CBASTestingIndicator4 CBASTestingIndicator4(bool showExitLabels, double exitLabelAtrOffset, bool useRegimeStability, int regimeStabilityBars, bool useScoringFilter, int scoreThreshold, bool useSmoothedVpm, int vpmEmaSpan, double minVpm, int minAdx, int momentumLookback, bool extendedLogging, bool computeExitSignals, double exitProfitAtrMult, string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder, bool scaleOscillatorToATR, double oscAtrMult, bool requireEmaSlope, double minCrossAtrDistance, double maxOpposingWickRatio, int minBarsBetweenSignals, bool requireRangeBreakAndHold)
		{
			return indicator.CBASTestingIndicator4(Input, showExitLabels, exitLabelAtrOffset, useRegimeStability, regimeStabilityBars, useScoringFilter, scoreThreshold, useSmoothedVpm, vpmEmaSpan, minVpm, minAdx, momentumLookback, extendedLogging, computeExitSignals, exitProfitAtrMult, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder, scaleOscillatorToATR, oscAtrMult, requireEmaSlope, minCrossAtrDistance, maxOpposingWickRatio, minBarsBetweenSignals, requireRangeBreakAndHold);
		}

		public Indicators.CBASTestingIndicator4 CBASTestingIndicator4(ISeries<double> input , bool showExitLabels, double exitLabelAtrOffset, bool useRegimeStability, int regimeStabilityBars, bool useScoringFilter, int scoreThreshold, bool useSmoothedVpm, int vpmEmaSpan, double minVpm, int minAdx, int momentumLookback, bool extendedLogging, bool computeExitSignals, double exitProfitAtrMult, string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder, bool scaleOscillatorToATR, double oscAtrMult, bool requireEmaSlope, double minCrossAtrDistance, double maxOpposingWickRatio, int minBarsBetweenSignals, bool requireRangeBreakAndHold)
		{
			return indicator.CBASTestingIndicator4(input, showExitLabels, exitLabelAtrOffset, useRegimeStability, regimeStabilityBars, useScoringFilter, scoreThreshold, useSmoothedVpm, vpmEmaSpan, minVpm, minAdx, momentumLookback, extendedLogging, computeExitSignals, exitProfitAtrMult, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder, scaleOscillatorToATR, oscAtrMult, requireEmaSlope, minCrossAtrDistance, maxOpposingWickRatio, minBarsBetweenSignals, requireRangeBreakAndHold);
		}
	}
}

#endregion
