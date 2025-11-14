using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
//using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Windows;                   // MessageBox
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
//using Newtonsoft.Json.Linq;
using NinjaTrader.Core;
//using static System.Reflection.Metadata.BlobBuilder;
using NinjaTrader.Custom.CBASTerminal;
using NinjaTrader.Custom.CBASTerminal;
using NinjaTrader.Data;
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

        [NinjaScriptProperty]
        [Display(Name = "Instance Id", Order = 99, GroupName = "Logging")]
        public string InstanceId { get; set; } = null;
        // =========================
        // Original inputs (first 7)
        // =========================
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

        // =========================
        // Logging inputs (added 4 -> total 11)
        // =========================
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
		public string LogFolder { get; set; } = null; // null -> use Globals.UserDataDir\Indicator_logs

        // Scoring filter properties
        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Min VPM", Order = 5, GroupName = "Logging")]
        public double MinVpm { get; set; } = 300;

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Min ADX", Order = 6, GroupName = "Logging")]
        public int MinAdx { get; set; } = 30;

        #endregion
        #region privates

        private string instrumentName;


        // =========================
        // Internals
        // =========================
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

        private ADX adx14;
        private SMA volSma;

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

        // Logging runtime
        private StreamWriter logWriter;
        private readonly object logLock = new object();
        private bool logInitialized = false;
        private string logPath = null;
        private int lastHeartbeatBar = -1;

        #endregion


protected override void OnStateChange()
        {
			
            if (State == State.SetDefaults)
            {
				InstanceId = string.Empty; // not null
				 Description = "CBAS Terminal AddOn";
                Name = "CBASTestingIndicator3";
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

                plotSuperTrend = 0;
                plotUpperBand = 1;
                plotLowerBand = 2;

                energyPlotIdx = new int[15];
                for (int i = 0; i < 15; i++)
                    energyPlotIdx[i] = 3 + i;

                plotRangeTop = 18;
                plotRangeBottom = 19;
                plotRangeMid = 20;

                InitLogger();

                instrumentName = Instrument?.FullName ?? "N/A";
                
                // Initialize indicators for scoring
                adx14 = ADX(14);
                volSma = SMA(Volume, 50);
                
                // Register this instance
                //CBASTerminalRegistry.Register(InstanceId, this);

                // Subscribe to terminal commands directed to this instance
                //CBASTerminalBus.OnCommand += HandleTerminalCommand;
            }
			else if (State == State.Active)
		    {
		        // Ensure the menu item appears even if AddOn loaded after startup
		        CBASTerminalAddOn.EnsureMenuForAllControlCenters();
		
		        // Optionally open the terminal window now and show a message
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
                    lock (logLock)
                    {
                        logWriter?.Flush();
                        logWriter?.Dispose();
                        logWriter = null;
                        logInitialized = false;
                       // CBASTerminalBus.OnCommand -= HandleTerminalCommand;
                        //CBASTerminalRegistry.Unregister(InstanceId);
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

            // Bands and supertrend (original behavior preserved)
            double rangeC = 2.0 * (High[0] - Low[0]);

            double upperBandRaw = Close[0] + Sensitivity * rangeC;
            double lowerBandRaw = Close[0] - Sensitivity * rangeC;

            double prevLowerBand = lowerBandSeries[1];
            double prevUpperBand = upperBandSeries[1];

            // Important: compare with Close[1], not Close[0]
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

            // Signals and arrows
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

            // Original: bar color based on prior superTrend
            if (CurrentBar > 0)
                BarBrushes[0] = Close[0] > superTrend[1] ? Brushes.Teal : Brushes.Red;

            // Energy EMAs visualization
            for (int i = 0; i < emaHighs.Length; i++)
            {
                double v = emaHighs[i][0];
                int pIdx = energyPlotIdx[i];
                Values[pIdx][0] = v;
                PlotBrushes[pIdx][0] = EmaEnergy
                    ? (Close[0] >= v ? Brushes.Teal : Brushes.Red)
                    : Brushes.Transparent;
            }

            // Range detector updates
            UpdateRangeDetectorPlots();

    //
    if (EnableLogging)
    {
        //PublishLog($"HB/Row: st={superTrendNow:F2}, ub={upperBand:F2}, lb={lowerBand:F2}");
        // CSV logging
        MaybeLogRow(bull, bear, superTrendNow, upperBand, lowerBand);
     }
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

        private int CurrentRegimeDir(double stNow)
        {
            if (Close[0] > stNow) return +1;
            if (Close[0] < stNow) return -1;
            return 0;
        }

        private bool RegimeStable(int dir, int bars)
        {
            int need = Math.Min(bars, CurrentBar + 1);
            for (int i = 0; i < need; i++)
            {
                double st = superTrend[i];
                int d = (Close[i] > st) ? +1 : (Close[i] < st ? -1 : 0);
                if (d != dir) return false;
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

        #region Logging
        // =========================
        // Logging
        // =========================
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

                logWriter = new StreamWriter(logPath, append: true, encoding: Encoding.UTF8) { AutoFlush = true };
                logInitialized = true;

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
                SafeWriteLine(header);
            }
            catch (Exception ex)
            {
                Print($"[CBASTestingIndicator3] Log init failed: {ex.Message}");
                logInitialized = false;
            }
        }

        private void MaybeLogRow(bool bull, bool bear, double superTrendNow, double upperBand, double lowerBand)
        {
            if (!logInitialized) return;

            bool inBullRegime = Close[0] > superTrendNow;
            bool inBearRegime = Close[0] < superTrendNow;

            bool wantHeartbeat = HeartbeatEveryNBars > 0
                                 && CurrentBar >= RangeMinLength
                                 && (CurrentBar == 0 || (CurrentBar - lastHeartbeatBar) >= HeartbeatEveryNBars);

            bool shouldLog = bull || bear || (!LogSignalsOnly) || wantHeartbeat;
            if (!shouldLog) return;

            if (wantHeartbeat)
                lastHeartbeatBar = CurrentBar;

            double atr30Val = atr30[0];
            double ema10 = ema10Close[0];
            double ema20 = ema20Close[0];
            double energy0 = (emaHighs != null && emaHighs.Length > 0) ? emaHighs[0][0] : double.NaN;
            // Volume per minute
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
        private double ComputeVpm()
        {
            try
            {
                // Prefer fixed minutes for Minute bars
                if (BarsPeriod?.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value > 0)
                {
                    double minutes = BarsPeriod.Value;
                    return minutes > 0 ? Volume[0] / minutes : double.NaN;
                }

                // Otherwise, use actual elapsed time between current and prior bar opens (historical and most realtime cases)
                if (CurrentBar > 0)
                {
                    double minutes = (Time[0] - Time[1]).TotalMinutes;
                    return minutes > 0 ? Volume[0] / minutes : double.NaN;
                }
            }
            catch
            {
                // ignore and fall through
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
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private CBASTestingIndicator3[] cacheCBASTestingIndicator3;
		public CBASTestingIndicator3 CBASTestingIndicator3(string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder)
		{
			return CBASTestingIndicator3(Input, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder);
		}

		public CBASTestingIndicator3 CBASTestingIndicator3(ISeries<double> input, string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder)
		{
			if (cacheCBASTestingIndicator3 != null)
				for (int idx = 0; idx < cacheCBASTestingIndicator3.Length; idx++)
					if (cacheCBASTestingIndicator3[idx] != null && cacheCBASTestingIndicator3[idx].InstanceId == instanceId && cacheCBASTestingIndicator3[idx].Sensitivity == sensitivity && cacheCBASTestingIndicator3[idx].EmaEnergy == emaEnergy && cacheCBASTestingIndicator3[idx].KeltnerLength == keltnerLength && cacheCBASTestingIndicator3[idx].AtrLength == atrLength && cacheCBASTestingIndicator3[idx].RangeMinLength == rangeMinLength && cacheCBASTestingIndicator3[idx].RangeWidthMult == rangeWidthMult && cacheCBASTestingIndicator3[idx].RangeAtrLen == rangeAtrLen && cacheCBASTestingIndicator3[idx].EnableLogging == enableLogging && cacheCBASTestingIndicator3[idx].LogSignalsOnly == logSignalsOnly && cacheCBASTestingIndicator3[idx].HeartbeatEveryNBars == heartbeatEveryNBars && cacheCBASTestingIndicator3[idx].LogFolder == logFolder && cacheCBASTestingIndicator3[idx].EqualsInput(input))
						return cacheCBASTestingIndicator3[idx];
			return CacheIndicator<CBASTestingIndicator3>(new CBASTestingIndicator3(){ InstanceId = instanceId, Sensitivity = sensitivity, EmaEnergy = emaEnergy, KeltnerLength = keltnerLength, AtrLength = atrLength, RangeMinLength = rangeMinLength, RangeWidthMult = rangeWidthMult, RangeAtrLen = rangeAtrLen, EnableLogging = enableLogging, LogSignalsOnly = logSignalsOnly, HeartbeatEveryNBars = heartbeatEveryNBars, LogFolder = logFolder }, input, ref cacheCBASTestingIndicator3);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.CBASTestingIndicator3 CBASTestingIndicator3(string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder)
		{
			return indicator.CBASTestingIndicator3(Input, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder);
		}

		public Indicators.CBASTestingIndicator3 CBASTestingIndicator3(ISeries<double> input , string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder)
		{
			return indicator.CBASTestingIndicator3(input, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.CBASTestingIndicator3 CBASTestingIndicator3(string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder)
		{
			return indicator.CBASTestingIndicator3(Input, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder);
		}

		public Indicators.CBASTestingIndicator3 CBASTestingIndicator3(ISeries<double> input , string instanceId, double sensitivity, bool emaEnergy, int keltnerLength, int atrLength, int rangeMinLength, double rangeWidthMult, int rangeAtrLen, bool enableLogging, bool logSignalsOnly, int heartbeatEveryNBars, string logFolder)
		{
			return indicator.CBASTestingIndicator3(input, instanceId, sensitivity, emaEnergy, keltnerLength, atrLength, rangeMinLength, rangeWidthMult, rangeAtrLen, enableLogging, logSignalsOnly, heartbeatEveryNBars, logFolder);
		}
	}
}

#endregion
