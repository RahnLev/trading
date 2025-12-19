#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Media;
#endregion

// Simple bar-quality trend strategy: good bars are Close > Open.
// Trend triggers on 4 good bars or 3 good + 1 bad with net positive sum of (Close-Open) over the last 4 completed bars.
// Enters long on trend; optionally exits when the trend condition is not present.
namespace NinjaTrader.NinjaScript.Strategies
{
    public class BarsOnTheFlow : Strategy
    {
        [NinjaScriptProperty]
        [Display(Name = "Contracts", Order = 1, GroupName = "BarsOnTheFlow")]
        public int Contracts { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "ExitOnTrendBreak", Order = 2, GroupName = "BarsOnTheFlow")]
        public bool ExitOnTrendBreak { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "ExitOnRetrace", Order = 3, GroupName = "BarsOnTheFlow")]
        public bool ExitOnRetrace { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "TrendRetraceFraction", Order = 4, GroupName = "BarsOnTheFlow")]
        public double TrendRetraceFraction { get; set; } = 0.66; // end trend visuals when profit gives back 66% of MFE

        [NinjaScriptProperty]
        [Display(Name = "EnableTrendOverlay", Order = 5, GroupName = "BarsOnTheFlow")]
        public bool EnableTrendOverlay { get; set; } = true; // draw semi-transparent overlay for active trend

        [NinjaScriptProperty]
        [Display(Name = "EnableShorts", Order = 6, GroupName = "BarsOnTheFlow")]
        public bool EnableShorts { get; set; } = true; // trade shorts symmetrically to longs

        [NinjaScriptProperty]
        [Display(Name = "AvoidShortsOnGoodCandle", Order = 7, GroupName = "BarsOnTheFlow")]
        public bool AvoidShortsOnGoodCandle { get; set; } = true; // block shorts that trigger off an up-close bar and restart counting

        [NinjaScriptProperty]
        [Display(Name = "AvoidLongsOnBadCandle", Order = 8, GroupName = "BarsOnTheFlow")]
        public bool AvoidLongsOnBadCandle { get; set; } = true; // block longs that trigger off a down-close bar and restart counting

        [NinjaScriptProperty]
        [Display(Name = "Show Bar Index Labels", Order = 9, GroupName = "BarsOnTheFlow")]
        public bool ShowBarIndexLabels
        {
            get { return showBarIndexLabels; }
            set { showBarIndexLabels = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Show Fast Grad Labels", Order = 10, GroupName = "BarsOnTheFlow")]
        public bool ShowFastGradLabels
        {
            get { return showFastGradLabels; }
            set { showFastGradLabels = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Use Chart-Scaled FastGrad Deg", Order = 11, GroupName = "BarsOnTheFlow")]
        public bool UseChartScaledFastGradDeg { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Enable FastGrad Debug", Order = 12, GroupName = "BarsOnTheFlow")]
        public bool EnableFastGradDebug { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "FastGrad Debug Start", Order = 13, GroupName = "BarsOnTheFlow")]
        public int FastGradDebugStart { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "FastGrad Debug End", Order = 14, GroupName = "BarsOnTheFlow")]
        public int FastGradDebugEnd { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "FastGrad Debug Log To CSV", Order = 15, GroupName = "BarsOnTheFlow")]
        public bool FastGradDebugLogToCsv { get; set; } = false;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Fast EMA Period", Order = 16, GroupName = "BarsOnTheFlow")]
        public int FastEmaPeriod { get; set; } = 10;

        [NinjaScriptProperty]
        [Range(2, int.MaxValue)]
        [Display(Name = "FastGradLookbackBars", Order = 17, GroupName = "BarsOnTheFlow")]
        public int FastGradLookbackBars { get; set; } = 2; // gradient lookback - use 2 for immediate visual slope

        [NinjaScriptProperty]
        [Display(Name = "ExitIfEntryBarOpposite", Order = 18, GroupName = "BarsOnTheFlow", Description = "Exit immediately if the entry bar closes opposite to trade direction (e.g., long entry bar closes down). Helps avoid positions that show immediate weakness.")]
        public bool ExitIfEntryBarOpposite { get; set; } = true; // exit if the entry bar closes opposite to trade direction

        [NinjaScriptProperty]
        [Display(Name = "EnableDashboardDiagnostics", Order = 19, GroupName = "BarsOnTheFlow")]
        public bool EnableDashboardDiagnostics { get; set; } = false; // when true, post compact diags (incl. gradient) to dashboard

        [NinjaScriptProperty]
        [Display(Name = "DashboardBaseUrl", Order = 20, GroupName = "BarsOnTheFlow")]
        public string DashboardBaseUrl { get; set; } = "http://localhost:51888";

        [NinjaScriptProperty]
        [Display(Name = "GradientFilterEnabled", Order = 21, GroupName = "BarsOnTheFlow")]
        public bool GradientFilterEnabled { get; set; } = false; // enable gradient-based entry filtering

        [NinjaScriptProperty]
        [Display(Name = "SkipShortsAboveGradient", Order = 22, GroupName = "BarsOnTheFlow")]
        public double SkipShortsAboveGradient { get; set; } = -7.0; // skip shorts when EMA gradient > this degrees

        [NinjaScriptProperty]
        [Display(Name = "SkipLongsBelowGradient", Order = 23, GroupName = "BarsOnTheFlow")]
        public double SkipLongsBelowGradient { get; set; } = 7.0; // skip longs when EMA gradient < this degrees

        [NinjaScriptProperty]
        [Display(Name = "ReverseOnTrendBreak", Order = 24, GroupName = "BarsOnTheFlow")]
        public bool ReverseOnTrendBreak { get; set; } = false; // reverse position instead of just exiting on trend break

        [NinjaScriptProperty]
        [Range(3, 10)]
        [Display(Name = "TrendLookbackBars", Order = 25, GroupName = "BarsOnTheFlow", Description = "Number of recent bars to analyze for trend detection (3-10). Independent of minimum required matching bars.")]
        public int TrendLookbackBars { get; set; } = 5; // lookback window for trend analysis

        [NinjaScriptProperty]
        [Range(2, 10)]
        [Display(Name = "MinConsecutiveBars", Order = 26, GroupName = "BarsOnTheFlow", Description = "Minimum number of good/bad bars required within the lookback window to trigger a trend (2-10).")]
        public int MinConsecutiveBars { get; set; } = 3; // minimum matching bars required

        [NinjaScriptProperty]
        [Display(Name = "UsePnLTiebreaker", Order = 27, GroupName = "BarsOnTheFlow", Description = "When enabled, allows trend entry when minimum bars not met but net PnL supports the direction. When disabled, strictly requires minimum consecutive bars.")]
        public bool UsePnLTiebreaker { get; set; } = false; // PnL tiebreaker for marginal patterns

        [NinjaScriptProperty]
        [Display(Name = "AllowMidBarGradientEntry", Order = 28, GroupName = "BarsOnTheFlow")]
        public bool AllowMidBarGradientEntry { get; set; } = false; // allow entry mid-bar when gradient crosses threshold

        [NinjaScriptProperty]
        [Display(Name = "AllowMidBarGradientExit", Order = 29, GroupName = "BarsOnTheFlow")]
        public bool AllowMidBarGradientExit { get; set; } = false; // allow exit mid-bar when gradient crosses unfavorable threshold

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Stop Loss Points", Order = 30, GroupName = "BarsOnTheFlow")]
        public int StopLossPoints { get; set; } = 20; // stop loss in points (0 = disabled)

        [NinjaScriptProperty]
        [Display(Name = "UseTrailingStop", Order = 31, GroupName = "BarsOnTheFlow", Description = "When enabled, stop loss trails price dynamically. When disabled, stop loss is static at entry price minus StopLossPoints.")]
        public bool UseTrailingStop { get; set; } = false; // use trailing stop instead of static stop

        [NinjaScriptProperty]
        [Display(Name = "UseDynamicStopLoss", Order = 32, GroupName = "BarsOnTheFlow", Description = "When enabled, stop loss is calculated based on volatility data. When disabled, uses fixed StopLossPoints value.")]
        public bool UseDynamicStopLoss { get; set; } = false; // calculate stop loss from volatility data

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "DynamicStopLookback", Order = 33, GroupName = "BarsOnTheFlow", Description = "Number of recent candles to average for dynamic stop loss calculation (fallback when API unavailable).")]
        public int DynamicStopLookback { get; set; } = 5; // number of bars to average for dynamic stop

        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "DynamicStopMultiplier", Order = 34, GroupName = "BarsOnTheFlow", Description = "Multiplier for average candle range (e.g., 1.0 = 1x average range, 1.5 = 1.5x average range).")]
        public double DynamicStopMultiplier { get; set; } = 1.0; // multiplier for average range

        [NinjaScriptProperty]
        [Display(Name = "UseVolumeAwareStop", Order = 35, GroupName = "BarsOnTheFlow", Description = "Query volatility database for hour/volume adjusted stop loss. Requires server running.")]
        public bool UseVolumeAwareStop { get; set; } = true; // use API for volume-aware stops

        [NinjaScriptProperty]
        [Display(Name = "EnableOpportunityLog", Order = 36, GroupName = "BarsOnTheFlow")]
        public bool EnableOpportunityLog { get; set; } = true; // log every bar with opportunity analysis

        private readonly Queue<bool> recentGood = new Queue<bool>(10);
        private readonly Queue<double> recentPnl = new Queue<double>(10);

        private double lastFastEmaSlope = double.NaN;

        // Optional bar index labels (copied from BareOhlcLogger)
        private bool showBarIndexLabels = true;
        private bool showFastGradLabels = true;
        private StreamWriter fastGradDebugWriter;
        private string fastGradDebugPath;

        // Logging
        private StreamWriter logWriter;
        private string logFilePath;
        private bool logInitialized;
        
        // Opportunity analysis logging
        private StreamWriter opportunityLogWriter;
        private string opportunityLogPath;
        private bool opportunityLogInitialized;
        
        // Output window logging
        private StreamWriter outputLogWriter;
        private string outputLogPath;
        private bool outputLogInitialized;

        // Deferred execution logs waiting for bar-close OHLC
        private readonly List<PendingLogEntry> pendingLogs = new List<PendingLogEntry>();

        // Fast EMA for context
        private EMA fastEma;

        // Trend visualization + retrace tracking
        private int trendStartBar = -1;
        private string trendRectTag;
        private string trendLineTag;
        private Brush trendBrush;
        private MarketPosition trendSide = MarketPosition.Flat;
        private double trendEntryPrice = double.NaN;
        private double trendMaxProfit = double.NaN;

        // Track the bar and direction of the most recent entry to police entry-bar closes
        private int lastEntryBarIndex = -1;
        private MarketPosition lastEntryDirection = MarketPosition.Flat;
        
        // Track intended position to avoid re-entry with UniqueEntries mode
        private MarketPosition intendedPosition = MarketPosition.Flat;

        private double lastFastEmaGradDeg = double.NaN;

        // Bar navigation panel
        private System.Windows.Controls.Grid barNavPanel;
        private System.Windows.Controls.TextBox barNavTextBox;
        private System.Windows.Controls.Button barNavButton;
        
        // Stop loss controls
        private System.Windows.Controls.TextBox stopLossTextBox;
        private System.Windows.Controls.Button stopLossPlusButton;
        private System.Windows.Controls.Button stopLossMinusButton;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "BarsOnTheFlow";
                Calculate = Calculate.OnEachTick; // evaluate at first tick of new bar using the completed bar
                IsOverlay = true; // draw labels and trend visuals on the price panel
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = false;
                ExitOnSessionCloseSeconds = 30;
                BarsRequiredToTrade = 4;
            }
            else if (State == State.DataLoaded)
            {
                fastEma = EMA(FastEmaPeriod);
                InitializeLog();
                
                // Export strategy state for API queries
                ExportStrategyState();
                
                // Initialize bar navigation panel on UI thread
                if (ChartControl != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() => CreateBarNavPanel());
                }
            }
            else if (State == State.Terminated)
            {
                if (logWriter != null)
                {
                    try
                    {
                        logWriter.Flush();
                        logWriter.Dispose();
                    }
                    catch { }
                    logWriter = null;
                }
                if (fastGradDebugWriter != null)
                {
                    try
                    {
                        fastGradDebugWriter.Flush();
                        fastGradDebugWriter.Dispose();
                    }
                    catch { }
                    fastGradDebugWriter = null;
                }
                if (opportunityLogWriter != null)
                {
                    try
                    {
                        opportunityLogWriter.Flush();
                        opportunityLogWriter.Dispose();
                    }
                    catch { }
                    opportunityLogWriter = null;
                }
                if (outputLogWriter != null)
                {
                    try
                    {
                        outputLogWriter.Flush();
                        outputLogWriter.Dispose();
                    }
                    catch { }
                    outputLogWriter = null;
                }
                logInitialized = false;
                opportunityLogInitialized = false;
                outputLogInitialized = false;
                
                // Clean up bar navigation panel
                if (ChartControl != null && barNavPanel != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() =>
                    {
                        if (ChartControl != null && ChartControl.Parent is System.Windows.Controls.Grid)
                        {
                            var parent = ChartControl.Parent as System.Windows.Controls.Grid;
                            parent.Children.Remove(barNavPanel);
                        }
                    });
                }
            }
        }

        private bool pendingShortFromGood;
        private bool pendingLongFromBad;
        private bool pendingExitLongOnGood;  // postpone long exit if next bar is good
        private bool pendingExitShortOnBad;  // postpone short exit if next bar is bad
        
        // Mid-bar gradient entry tracking
        private bool waitingForLongGradient;  // all conditions met except gradient for long
        private bool waitingForShortGradient; // all conditions met except gradient for short
        
        // Mid-bar gradient exit tracking
        private bool waitingToExitLongOnGradient;  // all exit conditions met except gradient for long exit
        private bool waitingToExitShortOnGradient; // all exit conditions met except gradient for short exit

        protected override void OnBarUpdate()
        {
            if (IsFirstTickOfBar && pendingLogs.Count > 0)
            {
                int justClosedBar = CurrentBar - 1;
                if (justClosedBar >= 0)
                {
                    double finalOpen = Open[1];
                    double finalHigh = High[1];
                    double finalLow = Low[1];
                    double finalClose = Close[1];
                    string finalCandleType = GetCandleType(finalOpen, finalClose);

                    for (int i = pendingLogs.Count - 1; i >= 0; i--)
                    {
                        var p = pendingLogs[i];
                        if (p.BarIndex == justClosedBar)
                        {
                            double finalRange = finalHigh - finalLow;
                            double finalBodyPct = finalRange > 0 ? (finalClose - finalOpen) / finalRange : 0;
                            double finalUpperWick = finalHigh - Math.Max(finalOpen, finalClose);
                            double finalLowerWick = Math.Min(finalOpen, finalClose) - finalLow;
                            
                            string line = string.Format(
                                "{0},{1},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11},{12},{13},{14},{15:F6},{16:F4},{17:F4},{18},{19},{20},{21},{22},{23},{24:F4},{25:F4},{26},{27},{28},{29},{30},{31},{32},{33},{34}",
                                p.Timestamp,
                                p.BarIndex,
                                p.Direction,
                                p.OpenAtExec,
                                p.HighAtExec,
                                p.LowAtExec,
                                p.CloseAtExec,
                                finalOpen,
                                finalHigh,
                                finalLow,
                                finalClose,
                                finalCandleType,
                                p.FastEmaStr,
                                p.FastEmaGradDeg.ToString("F4"),
                                p.Volume,
                                finalBodyPct,
                                finalUpperWick,
                                finalLowerWick,
                                p.Action,
                                p.OrderName,
                                p.Quantity,
                                p.Price.ToString("F4"),
                                p.PnlStr,
                                p.Reason,
                                p.PrevOpen,
                                p.PrevClose,
                                p.PrevCandleType,
                                p.AllowLongThisBar,
                                p.AllowShortThisBar,
                                p.TrendUpAtDecision,
                                p.TrendDownAtDecision,
                                p.DecisionBarIndex,
                                p.PendingShortFromGood,
                                p.PendingLongFromBad,
                                p.BarPattern);
                            LogLine(line);
                            pendingLogs.RemoveAt(i);
                        }
                    }
                }
            }

            if (CurrentBar < 1)
                return; // need a completed bar to score

            // Handle mid-bar gradient entry and exit checks
            if (!IsFirstTickOfBar && GradientFilterEnabled)
            {
                if (AllowMidBarGradientEntry)
                    CheckMidBarGradientEntry();
                if (AllowMidBarGradientExit)
                    CheckMidBarGradientExit();
                return;
            }

            if (!IsFirstTickOfBar)
                return; // only score once per bar using the just-closed bar
            
            // Update strategy state for external API queries
            UpdateStrategyState();
            
            // Reset mid-bar gradient waiting flags at start of new bar
            waitingForLongGradient = false;
            waitingForShortGradient = false;
            waitingToExitLongOnGradient = false;
            waitingToExitShortOnGradient = false;

            double prevOpen = Open[1];
            double prevClose = Close[1];
            RecordCompletedBar(prevOpen, prevClose);

            bool prevGood = prevClose > prevOpen;
            bool prevBad = prevClose < prevOpen;
            
            // Debug: Show which bar we're analyzing
            if (CurrentBar >= 2653 && CurrentBar <= 2670)
            {
                Print($"[BAR_DATA_DEBUG] CurrentBar={CurrentBar}, Time={Time[0]:HH:mm}, Open[1]={Open[1]:F2}, Close[1]={Close[1]:F2}, prevGood={prevGood}, prevBad={prevBad}");
            }
            bool trendUp = IsTrendUp();
            bool trendDown = EnableShorts && IsTrendDown();
            bool allowShortThisBar = !(AvoidShortsOnGoodCandle && prevGood);
            bool allowLongThisBar = !(AvoidLongsOnBadCandle && prevBad);
            
            // Check if 4-bar PnL supports current position direction
            double fourBarPnl = recentPnl.Count == 4 ? recentPnl.Sum() : 0;
            int goodCount = recentGood.Count >= 4 ? recentGood.Count(g => g) : 0;
            int badCount = recentGood.Count >= 4 ? recentGood.Count(g => !g) : 0;
            bool isMarginalTrend = (goodCount == 2 && badCount == 2);
            
            if (CurrentBar == 7)
            {
                Print($"[Trend Debug] Bar {CurrentBar}: trendUp={trendUp}, trendDown={trendDown}, EnableShorts={EnableShorts}, IsTrendDown()={IsTrendDown()}");
                Print($"[Trend Debug] Bar {CurrentBar}: recentGood queue: {string.Join(",", recentGood.Select(g => g ? "good" : "bad"))}");
                Print($"[Trend Debug] Bar {CurrentBar}: recentPnl queue: {string.Join(",", recentPnl.Select(p => p.ToString("F2")))}");
            }

            // Compute a stable gradient of the fast EMA using linear regression over a configurable window, expressed in degrees.
            int gradWindow = Math.Max(2, FastGradLookbackBars);
                        double regDeg;
                        lastFastEmaSlope = ComputeFastEmaGradient(gradWindow, out regDeg);

                        double chartDeg = double.NaN;
                        if (UseChartScaledFastGradDeg)
                        {
                            chartDeg = ComputeChartScaledFastEmaDeg(gradWindow);
                            if (double.IsNaN(chartDeg) && EnableFastGradDebug)
                                Print($"[FastGradDebug] chartDeg NaN at bar {CurrentBar}, falling back to regDeg {regDeg:F3}");
                        }

                        lastFastEmaGradDeg = !double.IsNaN(chartDeg) ? chartDeg : regDeg;

                        if (EnableFastGradDebug && CurrentBar >= FastGradDebugStart && CurrentBar <= FastGradDebugEnd && !double.IsNaN(lastFastEmaGradDeg))
                        {
                                var sb = new StringBuilder();
                                sb.Append("[FastGradDebug] bar=").Append(CurrentBar)
                                    .Append(" slope=").Append(lastFastEmaSlope.ToString("F6"))
                                    .Append(" regDeg=").Append(regDeg.ToString("F3"))
                                    .Append(" chartDeg=").Append(chartDeg.ToString("F3"))
                                    .Append(" usedDeg=").Append(lastFastEmaGradDeg.ToString("F3"))
                                    .Append(" window=").Append(gradWindow);
                                if (CurrentBar >= gradWindow)
                                {
                                    sb.Append(" emaOld=").Append(fastEma[gradWindow].ToString("F5"))
                                        .Append(" emaRecent=").Append(fastEma[1].ToString("F5"));
                                }
                                Print(sb.ToString());

                if (FastGradDebugLogToCsv)
                {
                    EnsureFastGradDebugWriter();
                    if (fastGradDebugWriter != null)
                    {
                        string timeStr = Time[1].ToString("yyyy-MM-dd HH:mm:ss.fff");
                        string line = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{0},{1},{2:F6},{3:F3},{4},{5:F5},{6:F5}",
                            timeStr,
                            CurrentBar,
                            lastFastEmaSlope,
                            lastFastEmaGradDeg,
                            gradWindow,
                            fastEma[gradWindow],
                            fastEma[1]);
                        fastGradDebugWriter.WriteLine(line);
                    }
                }
                        }

            // Optionally stream diagnostics (incl. gradient) to dashboard
            if (EnableDashboardDiagnostics)
            {
                SendDashboardDiag(allowLongThisBar, allowShortThisBar);
            }

            // Record bar sample for volatility database (non-blocking)
            if (UseVolumeAwareStop && CurrentBar > 10)
            {
                RecordBarSample();
            }

            // Optional guard: if the bar we entered on closes opposite to our position, exit immediately on the next bar open.
            if (ExitIfEntryBarOpposite && lastEntryBarIndex == CurrentBar - 1 && Position.MarketPosition != MarketPosition.Flat)
            {
                bool entryBarWasGood = Close[1] > Open[1];
                bool entryBarWasBad = Close[1] < Open[1];

                bool longExits = Position.MarketPosition == MarketPosition.Long && entryBarWasBad;
                bool shortExits = Position.MarketPosition == MarketPosition.Short && entryBarWasGood;

                if (longExits)
                {
                    Print($"[EXIT_DEBUG] Bar {CurrentBar}: ExitIfEntryBarOpposite triggering LONG exit - entry bar was BAD (O:{Open[1]:F2}, C:{Close[1]:F2})");
                    CaptureDecisionContext(Open[1], Close[1], true, true, trendUp, trendDown);
                    ExitLong("BarsOnTheFlowEntryBarOpp", "BarsOnTheFlowLong");
                    intendedPosition = MarketPosition.Flat;
                    return;
                }

                if (shortExits)
                {
                    Print($"[EXIT_DEBUG] Bar {CurrentBar}: ExitIfEntryBarOpposite triggering SHORT exit - entry bar was GOOD (O:{Open[1]:F2}, C:{Close[1]:F2})");
                    CaptureDecisionContext(Open[1], Close[1], true, true, trendUp, trendDown);
                    ExitShort("BarsOnTheFlowEntryBarOppS", "BarsOnTheFlowShort");
                    intendedPosition = MarketPosition.Flat;
                    return;
                }
            }

            MarketPosition currentPos = Position.MarketPosition;

            // Handle pending exit decisions from previous bar
            if (pendingExitShortOnBad && currentPos == MarketPosition.Short)
            {
                if (prevGood)
                {
                    Print($"[EXIT_DEBUG] Bar {CurrentBar}: pendingExitShortOnBad resolving - prevGood, exiting SHORT (O:{prevOpen:F2}, C:{prevClose:F2})");
                    // Previous bar was good, confirms reversal - exit now
                    CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                    ExitShort("BarsOnTheFlowExitS", "BarsOnTheFlowShort");
                    intendedPosition = MarketPosition.Flat;
                    pendingExitShortOnBad = false;
                    // Don't return here - allow reversal logic to potentially enter long
                }
                else if (prevBad)
                {
                    Print($"[EXIT_DEBUG] Bar {CurrentBar}: pendingExitShortOnBad cancelling - prevBad, trend continues (O:{prevOpen:F2}, C:{prevClose:F2})");
                    // Previous bar was bad, we now have 3 bad bars - trend continues, cancel exit
                    pendingExitShortOnBad = false;
                }
            }
            
            if (pendingExitLongOnGood && currentPos == MarketPosition.Long)
            {
                if (prevBad)
                {
                    Print($"[EXIT_DEBUG] Bar {CurrentBar}: pendingExitLongOnGood resolving - prevBad, exiting LONG (O:{prevOpen:F2}, C:{prevClose:F2})");
                    // Previous bar was bad, confirms reversal - exit now
                    CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                    ExitLong("BarsOnTheFlowExit", "BarsOnTheFlowLong");
                    intendedPosition = MarketPosition.Flat;
                    pendingExitLongOnGood = false;
                    // Don't return here - allow reversal logic to potentially enter short
                }
                else if (prevGood)
                {
                    Print($"[EXIT_DEBUG] Bar {CurrentBar}: pendingExitLongOnGood cancelling - prevGood, trend continues (O:{prevOpen:F2}, C:{prevClose:F2})");
                    // Previous bar was good, we now have 3 good bars - trend continues, cancel exit
                    pendingExitLongOnGood = false;
                }
            }

            bool placedEntry = false;

            // Resolve pending deferred shorts that were blocked by a prior good bar.
            if (pendingShortFromGood && currentPos == MarketPosition.Flat)
            {
                PrintAndLog($"[PENDING_SHORT_DEBUG] Bar {CurrentBar}: Resolving pendingShortFromGood | trendUp={trendUp}, trendDown={trendDown}, prevGood={prevGood}, prevBad={prevBad}, gradient={lastFastEmaGradDeg:F2}", "DEBUG");
                
                if (trendUp)
                {
                    bool skipDueToGradient = ShouldSkipLongDueToGradient(lastFastEmaGradDeg);
                    PrintAndLog($"[PENDING_SHORT_DEBUG] Bar {CurrentBar}: TrendUp detected, reversing to LONG | skipDueToGradient={skipDueToGradient}", "DEBUG");
                    if (!skipDueToGradient)
                    {
                        if (intendedPosition == MarketPosition.Long)
                        {
                            PrintAndLog($"[Entry Skip] Bar {CurrentBar}: Already intendedPosition=Long, skipping pendingShortFromGood entry", "DEBUG");
                        }
                        else
                        {
                            CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                            PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering LONG from pendingShortFromGood, CurrentPos={Position.Quantity}, Contracts={Contracts}", "ENTRY");
                            EnterLong(Math.Max(1, Contracts), "BarsOnTheFlowLong");
                            ApplyStopLoss("BarsOnTheFlowLong");
                            lastEntryBarIndex = CurrentBar;
                            lastEntryDirection = MarketPosition.Long;
                            intendedPosition = MarketPosition.Long;
                            placedEntry = true;
                        }
                        pendingShortFromGood = false;
                        pendingLongFromBad = false;
                    }
                    else
                    {
                        PrintAndLog($"[PENDING_SHORT_DEBUG] Bar {CurrentBar}: Gradient blocked LONG, clearing pending flag", "DEBUG");
                        pendingShortFromGood = false;
                    }
                }
                else if (trendDown && prevBad)
                {
                    bool skipDueToGradient = ShouldSkipShortDueToGradient(lastFastEmaGradDeg);
                    PrintAndLog($"[PENDING_SHORT_DEBUG] Bar {CurrentBar}: TrendDown + prevBad, executing pending SHORT | skipDueToGradient={skipDueToGradient}", "DEBUG");
                    if (!skipDueToGradient)
                    {
                        if (intendedPosition == MarketPosition.Short)
                        {
                            PrintAndLog($"[Entry Skip] Bar {CurrentBar}: Already intendedPosition=Short, skipping pendingShortFromGood short entry", "DEBUG");
                        }
                        else
                        {
                            CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                            PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering SHORT from pendingShortFromGood, CurrentPos={Position.Quantity}, Contracts={Contracts}", "ENTRY");
                            EnterShort(Math.Max(1, Contracts), "BarsOnTheFlowShort");
                            ApplyStopLoss("BarsOnTheFlowShort");
                            lastEntryBarIndex = CurrentBar;
                            lastEntryDirection = MarketPosition.Short;
                            intendedPosition = MarketPosition.Short;
                            placedEntry = true;
                        }
                        pendingShortFromGood = false;
                        pendingLongFromBad = false;
                    }
                    else if (AllowMidBarGradientEntry)
                    {
                        PrintAndLog($"[PENDING_SHORT_DEBUG] Bar {CurrentBar}: Gradient blocked SHORT, waiting for mid-bar gradient", "DEBUG");
                        waitingForShortGradient = true;
                    }
                    else
                    {
                        PrintAndLog($"[PENDING_SHORT_DEBUG] Bar {CurrentBar}: Gradient blocked SHORT, clearing pending flag", "DEBUG");
                        pendingShortFromGood = false;
                    }
                }
                else if (!trendDown)
                {
                    PrintAndLog($"[PENDING_SHORT_DEBUG] Bar {CurrentBar}: TrendDown LOST, clearing pendingShortFromGood flag", "DEBUG");
                    pendingShortFromGood = false;
                }
                else
                {
                    PrintAndLog($"[PENDING_SHORT_DEBUG] Bar {CurrentBar}: Waiting for prevBad - trendDown={trendDown}, prevGood={prevGood}, prevBad={prevBad}, keeping pending flag", "DEBUG");
                }
            }

            // Resolve pending deferred longs that were blocked by a prior bad bar.
            if (!placedEntry && pendingLongFromBad && currentPos == MarketPosition.Flat)
            {
                PrintAndLog($"[PENDING_LONG_DEBUG] Bar {CurrentBar}: Resolving pendingLongFromBad | trendUp={trendUp}, trendDown={trendDown}, prevGood={prevGood}, prevBad={prevBad}, gradient={lastFastEmaGradDeg:F2}", "DEBUG");
                
                if (trendDown)
                {
                    bool skipDueToGradient = ShouldSkipShortDueToGradient(lastFastEmaGradDeg);
                    PrintAndLog($"[PENDING_LONG_DEBUG] Bar {CurrentBar}: TrendDown detected, reversing to SHORT | skipDueToGradient={skipDueToGradient}", "DEBUG");
                    if (!skipDueToGradient)
                    {
                        if (intendedPosition == MarketPosition.Short)
                        {
                            PrintAndLog($"[Entry Skip] Bar {CurrentBar}: Already intendedPosition=Short, skipping pendingLongFromBad short entry", "DEBUG");
                        }
                        else
                        {
                            CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                            PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering SHORT from pendingLongFromBad, CurrentPos={Position.Quantity}, Contracts={Contracts}");
                            EnterShort(Math.Max(1, Contracts), "BarsOnTheFlowShort");
                            ApplyStopLoss("BarsOnTheFlowShort");
                            lastEntryBarIndex = CurrentBar;
                            lastEntryDirection = MarketPosition.Short;
                            intendedPosition = MarketPosition.Short;
                            placedEntry = true;
                        }
                        pendingLongFromBad = false;
                        pendingShortFromGood = false;
                    }
                    else if (AllowMidBarGradientEntry)
                    {
                        PrintAndLog($"[PENDING_LONG_DEBUG] Bar {CurrentBar}: Gradient blocked SHORT, waiting for mid-bar gradient", "DEBUG");
                        waitingForShortGradient = true;
                    }
                    else
                    {
                        PrintAndLog($"[PENDING_LONG_DEBUG] Bar {CurrentBar}: Gradient blocked SHORT, clearing pending flag", "DEBUG");
                        pendingLongFromBad = false;
                    }
                }
                else if (trendUp && prevGood)
                {
                    bool skipDueToGradient = ShouldSkipLongDueToGradient(lastFastEmaGradDeg);
                    PrintAndLog($"[PENDING_LONG_DEBUG] Bar {CurrentBar}: TrendUp + prevGood, executing pending LONG | skipDueToGradient={skipDueToGradient}", "DEBUG");
                    if (!skipDueToGradient)
                    {
                        if (intendedPosition == MarketPosition.Long)
                        {
                            PrintAndLog($"[Entry Skip] Bar {CurrentBar}: Already intendedPosition=Long, skipping pendingLongFromBad entry", "DEBUG");
                        }
                        else
                        {
                            CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                            PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering LONG from pendingLongFromBad, CurrentPos={Position.Quantity}, Contracts={Contracts}");
                            EnterLong(Math.Max(1, Contracts), "BarsOnTheFlowLong");
                            ApplyStopLoss("BarsOnTheFlowLong");
                            lastEntryBarIndex = CurrentBar;
                            lastEntryDirection = MarketPosition.Long;
                            intendedPosition = MarketPosition.Long;
                            placedEntry = true;
                        }
                        pendingLongFromBad = false;
                        pendingShortFromGood = false;
                    }
                    else if (AllowMidBarGradientEntry)
                    {
                        PrintAndLog($"[PENDING_LONG_DEBUG] Bar {CurrentBar}: Gradient blocked LONG, waiting for mid-bar gradient", "DEBUG");
                        waitingForLongGradient = true;
                    }
                    else
                    {
                        PrintAndLog($"[PENDING_LONG_DEBUG] Bar {CurrentBar}: Gradient blocked LONG, clearing pending flag", "DEBUG");
                        pendingLongFromBad = false;
                    }
                }
                else if (!trendUp)
                {
                    PrintAndLog($"[PENDING_LONG_DEBUG] Bar {CurrentBar}: TrendUp LOST, clearing pendingLongFromBad flag", "DEBUG");
                    pendingLongFromBad = false;
                }
                else
                {
                    PrintAndLog($"[PENDING_LONG_DEBUG] Bar {CurrentBar}: Waiting for prevGood - trendUp={trendUp}, prevGood={prevGood}, prevBad={prevBad}, keeping pending flag", "DEBUG");
                }
            }

            // Fresh signals with deferral logic.
            if (trendUp)
            {
                if (currentPos == MarketPosition.Short)
                {
                    CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                    if (ReverseOnTrendBreak && allowLongThisBar)
                    {
                        // Use the same entry validation logic as normal long entries
                        if (AvoidLongsOnBadCandle && prevBad)
                        {
                            Print($"[EXIT_DEBUG] Bar {CurrentBar}: TrendUp break - exiting SHORT (no reverse due to bad candle)");
                            // Don't reverse if we would avoid longs on bad candles
                            ExitShort();
                        }
                        else
                        {
                            Print($"[EXIT_DEBUG] Bar {CurrentBar}: TrendUp break - exiting SHORT and REVERSING to LONG");
                            // ReverseOnTrendBreak overrides gradient filter
                            ExitShort();
                            if (intendedPosition != MarketPosition.Long)
                            {
                                PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering LONG (reverse from short), CurrentPos={Position.Quantity}, Contracts={Contracts}");
                                EnterLong(Math.Max(1, Contracts), "BarsOnTheFlowLong");
                                // Don't apply stop loss during reversal - let NT handle the transition
                                lastEntryBarIndex = CurrentBar;
                                lastEntryDirection = MarketPosition.Long;
                                intendedPosition = MarketPosition.Long;
                                placedEntry = true;
                            }
                            else
                            {
                                PrintAndLog($"[Entry Skip] Bar {CurrentBar}: Already intendedPosition=Long, skipping reversal entry", "DEBUG");
                            }
                        }
                    }
                    else
                    {
                        Print($"[EXIT_DEBUG] Bar {CurrentBar}: TrendUp break - exiting SHORT (no reverse, ReverseOnTrendBreak={ReverseOnTrendBreak})");
                        ExitShort();
                    }
                }

                if (!placedEntry && currentPos == MarketPosition.Flat)
                {
                    if (AvoidLongsOnBadCandle && prevBad)
                    {
                        Print($"[Entry Block] Bar {CurrentBar}: Deferring LONG - bar closed BAD (O:{prevOpen:F2}, C:{prevClose:F2})");
                        pendingLongFromBad = true;
                        pendingShortFromGood = false;
                    }
                    else if (allowLongThisBar)
                    {
                        // Double-check: don't enter long if bar just closed bad
                        if (AvoidLongsOnBadCandle && prevBad)
                        {
                            Print($"[Entry Block] Bar {CurrentBar}: BLOCKED LONG - bar closed BAD despite allowLongThisBar=true (O:{prevOpen:F2}, C:{prevClose:F2})");
                            pendingLongFromBad = true;
                            pendingShortFromGood = false;
                        }
                        else
                        {
                            // Check gradient filter: skip longs if EMA gradient is below threshold
                            bool skipDueToGradient = ShouldSkipLongDueToGradient(lastFastEmaGradDeg);
                            if (!skipDueToGradient)
                            {
                                // Don't re-enter if already long - check both actual and intended position
                                if (Position.MarketPosition != MarketPosition.Long && intendedPosition != MarketPosition.Long)
                                {
                                    CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                                    PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering LONG (fresh signal), CurrentPos={Position.Quantity}, Contracts={Contracts}");
                                    EnterLong(Math.Max(1, Contracts), "BarsOnTheFlowLong");
                                    ApplyStopLoss("BarsOnTheFlowLong");
                                    lastEntryBarIndex = CurrentBar;
                                    lastEntryDirection = MarketPosition.Long;
                                    intendedPosition = MarketPosition.Long;
                                    placedEntry = true;
                                    pendingLongFromBad = false;
                                    pendingShortFromGood = false;
                                }
                                else
                                {
                                    Print($"[Entry Skip] Bar {CurrentBar}: Already LONG (actual={Position.MarketPosition}, intended={intendedPosition}), not re-entering");
                                }
                            }
                            else if (AllowMidBarGradientEntry)
                            {
                                // All conditions met except gradient - wait for mid-bar cross
                                waitingForLongGradient = true;
                            }
                        }
                    }
                }
            }
            else if (trendDown)
            {
                if (currentPos == MarketPosition.Long)
                {
                    CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                    
                    Print($"[Reverse Debug] Bar {CurrentBar}: ReverseOnTrendBreak={ReverseOnTrendBreak}, allowShortThisBar={allowShortThisBar}, AvoidShortsOnGoodCandle={AvoidShortsOnGoodCandle}, prevGood={prevGood}, prevOpen={prevOpen:F2}, prevClose={prevClose:F2}");
                    
                    if (ReverseOnTrendBreak && allowShortThisBar)
                    {
                        // Use the same entry validation logic as normal short entries
                        if (AvoidShortsOnGoodCandle && prevGood)
                        {
                            // Don't reverse if we would avoid shorts on good candles
                            Print($"[EXIT_DEBUG] Bar {CurrentBar}: TrendDown break - exiting LONG (no reverse due to good candle)");
                            Print($"[Reverse Debug] Bar {CurrentBar}: Blocked by AvoidShortsOnGoodCandle");
                            ExitLong("BarsOnTheFlowExit", "BarsOnTheFlowLong");
                        }
                        else
                        {
                            // ReverseOnTrendBreak overrides gradient filter
                            Print($"[EXIT_DEBUG] Bar {CurrentBar}: TrendDown break - exiting LONG and REVERSING to SHORT");
                            Print($"[Reverse Debug] Bar {CurrentBar}: REVERSING to short! (gradient filter overridden)");
                            ExitLong("BarsOnTheFlowExit", "BarsOnTheFlowLong");
                            if (intendedPosition != MarketPosition.Short)
                            {
                                PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering SHORT (reverse from long), CurrentPos={Position.Quantity}, Contracts={Contracts}");
                                EnterShort(Math.Max(1, Contracts), "BarsOnTheFlowShort");
                                // Don't apply stop loss during reversal - let NT handle the transition
                                lastEntryBarIndex = CurrentBar;
                                lastEntryDirection = MarketPosition.Short;
                                intendedPosition = MarketPosition.Short;
                                placedEntry = true;
                            }
                            else
                            {
                                PrintAndLog($"[Entry Skip] Bar {CurrentBar}: Already intendedPosition=Short, skipping TrendDown reversal", "DEBUG");
                            }
                        }
                    }
                    else
                    {
                        Print($"[EXIT_DEBUG] Bar {CurrentBar}: TrendDown break - exiting LONG (no reverse, ReverseOnTrendBreak={ReverseOnTrendBreak})");
                        Print($"[Reverse Debug] Bar {CurrentBar}: Not reversing - ReverseOnTrendBreak={ReverseOnTrendBreak}, allowShortThisBar={allowShortThisBar}");
                        ExitLong("BarsOnTheFlowExit", "BarsOnTheFlowLong");
                    }
                }

                if (!placedEntry && currentPos == MarketPosition.Flat)
                {
                    if (AvoidShortsOnGoodCandle && prevGood)
                    {
                        Print($"[Entry Block] Bar {CurrentBar}: Deferring SHORT - bar closed GOOD (O:{prevOpen:F2}, C:{prevClose:F2})");
                        pendingShortFromGood = true;
                        pendingLongFromBad = false;
                    }
                    else if (allowShortThisBar)
                    {
                        // Double-check: don't enter short if bar just closed good
                        if (AvoidShortsOnGoodCandle && prevGood)
                        {
                            Print($"[Entry Block] Bar {CurrentBar}: BLOCKED SHORT - bar closed GOOD despite allowShortThisBar=true (O:{prevOpen:F2}, C:{prevClose:F2})");
                            pendingShortFromGood = true;
                            pendingLongFromBad = false;
                        }
                        else
                        {
                            // Check gradient filter: skip shorts if EMA gradient is above threshold
                            bool skipDueToGradient = ShouldSkipShortDueToGradient(lastFastEmaGradDeg);
                            if (!skipDueToGradient)
                            {
                                // Don't re-enter if already short - check both actual and intended position
                                if (Position.MarketPosition != MarketPosition.Short && intendedPosition != MarketPosition.Short)
                                {
                                    CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                                    PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering SHORT (fresh signal), CurrentPos={Position.Quantity}, Contracts={Contracts}");
                                    EnterShort(Math.Max(1, Contracts), "BarsOnTheFlowShort");
                                    ApplyStopLoss("BarsOnTheFlowShort");
                                    lastEntryBarIndex = CurrentBar;
                                    lastEntryDirection = MarketPosition.Short;
                                    intendedPosition = MarketPosition.Short;
                                    placedEntry = true;
                                    pendingShortFromGood = false;
                                    pendingLongFromBad = false;
                                }
                                else
                                {
                                    Print($"[Entry Skip] Bar {CurrentBar}: Already SHORT (actual={Position.MarketPosition}, intended={intendedPosition}), not re-entering");
                                }
                            }
                            else if (AllowMidBarGradientEntry)
                            {
                                // All conditions met except gradient - wait for mid-bar cross
                                waitingForShortGradient = true;
                            }
                        }
                    }
                }
            }
            else if (ExitOnTrendBreak && currentPos == MarketPosition.Long)
            {
                // Check if this is a marginal trend (2 good, 2 bad) with net positive PnL
                if (isMarginalTrend && fourBarPnl > 0 && !pendingExitLongOnGood)
                {
                    // Postpone exit decision - wait to see if next bar is bad
                    Print($"[Postpone Exit] Bar {CurrentBar}: Long trend marginal (2g/2b), fourBarPnl={fourBarPnl:F2} > 0, postponing exit");
                    pendingExitLongOnGood = true;
                }
                else if (!pendingExitLongOnGood)
                {
                    CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                    Print($"[Reverse Debug] Bar {CurrentBar}: ExitOnTrendBreak for Long, trendDown={trendDown}, ReverseOnTrendBreak={ReverseOnTrendBreak}");
                    
                    // Check if we should reverse to short when exiting long position
                    if (ReverseOnTrendBreak && trendDown && allowShortThisBar)
                    {
                        if (AvoidShortsOnGoodCandle && prevGood)
                        {
                            Print($"[EXIT_DEBUG] Bar {CurrentBar}: ExitOnTrendBreak - exiting LONG (no reverse due to good candle)");
                            Print($"[Reverse Debug] Bar {CurrentBar}: Blocked by AvoidShortsOnGoodCandle");
                            ExitLong("BarsOnTheFlowExit", "BarsOnTheFlowLong");
                        }
                        else
                        {
                            // ReverseOnTrendBreak overrides gradient filter
                            Print($"[EXIT_DEBUG] Bar {CurrentBar}: ExitOnTrendBreak - exiting LONG and REVERSING to SHORT");
                            Print($"[Reverse Debug] Bar {CurrentBar}: REVERSING to short! (gradient filter overridden)");
                            ExitLong("BarsOnTheFlowExit", "BarsOnTheFlowLong");
                            if (intendedPosition != MarketPosition.Short)
                            {
                                PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering SHORT (ExitOnTrendBreak reversal), CurrentPos={Position.Quantity}, Contracts={Contracts}");
                                EnterShort(Math.Max(1, Contracts), "BarsOnTheFlowShort");
                                // Don't apply stop loss during reversal - let NT handle the transition
                                lastEntryBarIndex = CurrentBar;
                                lastEntryDirection = MarketPosition.Short;
                                intendedPosition = MarketPosition.Short;
                                placedEntry = true;
                            }
                            else
                            {
                                PrintAndLog($"[Entry Skip] Bar {CurrentBar}: Already intendedPosition=Short, skipping ExitOnTrendBreak reversal", "DEBUG");
                            }
                        }
                    }
                    else
                    {
                        Print($"[EXIT_DEBUG] Bar {CurrentBar}: ExitOnTrendBreak - exiting LONG (no reverse, ReverseOnTrendBreak={ReverseOnTrendBreak})");
                        Print($"[Reverse Debug] Bar {CurrentBar}: Just exiting, no reversal");
                        ExitLong("BarsOnTheFlowExit", "BarsOnTheFlowLong");
                    }
                }
            }
            else if (ExitOnTrendBreak && currentPos == MarketPosition.Short)
            {
                // Check if this is a marginal trend (2 good, 2 bad) with net negative PnL
                if (isMarginalTrend && fourBarPnl < 0 && !pendingExitShortOnBad)
                {
                    // Postpone exit decision - wait to see if next bar is good
                    Print($"[Postpone Exit] Bar {CurrentBar}: Short trend marginal (2g/2b), fourBarPnl={fourBarPnl:F2} < 0, postponing exit");
                    pendingExitShortOnBad = true;
                }
                else if (!pendingExitShortOnBad)
                {
                    CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                    Print($"[Reverse Debug] Bar {CurrentBar}: ExitOnTrendBreak for Short, trendUp={trendUp}, ReverseOnTrendBreak={ReverseOnTrendBreak}");
                    
                    // Check if we should reverse to long when exiting short position
                    if (ReverseOnTrendBreak && trendUp && allowLongThisBar)
                    {
                        if (AvoidLongsOnBadCandle && prevBad)
                        {
                            Print($"[EXIT_DEBUG] Bar {CurrentBar}: ExitOnTrendBreak - exiting SHORT (no reverse due to bad candle)");
                            Print($"[Reverse Debug] Bar {CurrentBar}: Blocked by AvoidLongsOnBadCandle");
                            ExitShort("BarsOnTheFlowExitS", "BarsOnTheFlowShort");
                        }
                        else
                        {
                            // ReverseOnTrendBreak overrides gradient filter
                            Print($"[EXIT_DEBUG] Bar {CurrentBar}: ExitOnTrendBreak - exiting SHORT and REVERSING to LONG");
                            Print($"[Reverse Debug] Bar {CurrentBar}: REVERSING to long! (gradient filter overridden)");
                            ExitShort("BarsOnTheFlowExitS", "BarsOnTheFlowShort");
                            if (intendedPosition != MarketPosition.Long)
                            {
                                PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering LONG (ExitOnTrendBreak reversal), CurrentPos={Position.Quantity}, Contracts={Contracts}");
                                EnterLong(Math.Max(1, Contracts), "BarsOnTheFlowLong");
                                // Don't apply stop loss during reversal - let NT handle the transition
                                lastEntryBarIndex = CurrentBar;
                                lastEntryDirection = MarketPosition.Long;
                                intendedPosition = MarketPosition.Long;
                                placedEntry = true;
                            }
                            else
                            {
                                PrintAndLog($"[Entry Skip] Bar {CurrentBar}: Already intendedPosition=Long, skipping ExitOnTrendBreak reversal", "DEBUG");
                            }
                        }
                    }
                    else
                    {
                        Print($"[EXIT_DEBUG] Bar {CurrentBar}: ExitOnTrendBreak - exiting SHORT (no reverse, ReverseOnTrendBreak={ReverseOnTrendBreak})");
                        Print($"[Reverse Debug] Bar {CurrentBar}: Just exiting, no reversal");
                        ExitShort("BarsOnTheFlowExitS", "BarsOnTheFlowShort");
                    }
                }
            }

            UpdateTrendLifecycle(currentPos);

            if (showBarIndexLabels && CurrentBar >= 0)
            {
                string tag = "BarLabel_" + CurrentBar;
                double yPosition = High[0] + (6 * TickSize);
                Draw.Text(this, tag, CurrentBar.ToString(), 0, yPosition, Brushes.Black);
            }

            if (showFastGradLabels && CurrentBar >= 1 && !double.IsNaN(lastFastEmaGradDeg))
            {
                // Draw gradient label on the PREVIOUS bar (barsAgo=1) since we calculate slope from bar N-1 to N
                // The gradient at bar 156 should show the slope INTO bar 156, so label goes on bar 156 (which is barsAgo=1 when CurrentBar=157)
                string gradTag = "FastGradLabel_" + (CurrentBar - 1);
                double gradY = High[1] + (18 * TickSize); // above the previous bar, higher to avoid overlap with bar number
                string gradText = lastFastEmaGradDeg.ToString("F1");
                Draw.Text(this, gradTag, gradText, 1, gradY, Brushes.Black); // barsAgo = 1
            }

            // Write a per-bar snapshot even when no orders fire
            LogBarSnapshot(1, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
            
            // Log opportunity analysis
            if (EnableOpportunityLog && opportunityLogInitialized)
            {
                LogOpportunityAnalysis(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown, placedEntry);
            }
            
            // Debug output for mid-bar gradient waiting
            if (waitingForLongGradient)
            {
                Print($"[MidBar Wait] Bar {CurrentBar}: Waiting for LONG gradient to cross {SkipLongsBelowGradient:F2}° (current: {lastFastEmaGradDeg:F2}°)");
            }
            if (waitingForShortGradient)
            {
                Print($"[MidBar Wait] Bar {CurrentBar}: Waiting for SHORT gradient to cross {SkipShortsAboveGradient:F2}° (current: {lastFastEmaGradDeg:F2}°)");
            }
            if (waitingToExitLongOnGradient)
            {
                Print($"[MidBar Exit Wait] Bar {CurrentBar}: Waiting to EXIT LONG when gradient drops below {SkipLongsBelowGradient:F2}° (current: {lastFastEmaGradDeg:F2}°)");
            }
            if (waitingToExitShortOnGradient)
            {
                Print($"[MidBar Exit Wait] Bar {CurrentBar}: Waiting to EXIT SHORT when gradient rises above {SkipShortsAboveGradient:F2}° (current: {lastFastEmaGradDeg:F2}°)");
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            base.OnExecutionUpdate(execution, executionId, price, quantity, marketPosition, orderId, time);

            if (execution == null)
                return;

            Order order = execution.Order;
            if (order == null)
                return;

            if (execution.Order.OrderState != OrderState.Filled && execution.Order.OrderState != OrderState.PartFilled)
                return;

            // Ensure log ready
            if (!logInitialized)
                InitializeLog();

            var action = order.OrderAction;
            bool isEntry = action == OrderAction.Buy || action == OrderAction.SellShort;
            bool isExit = action == OrderAction.Sell || action == OrderAction.BuyToCover;

            // Reset intendedPosition when exits fill
            if (isExit)
            {
                Print($"[EXIT_FILL_DEBUG] Bar {CurrentBar}: Exit filled - {action}, resetting intendedPosition from {intendedPosition} to Flat");
                intendedPosition = MarketPosition.Flat;
            }
            // Update intendedPosition when entries fill
            else if (isEntry)
            {
                if (action == OrderAction.Buy)
                {
                    Print($"[ENTRY_FILL_DEBUG] Bar {CurrentBar}: Long entry filled, setting intendedPosition=Long");
                    intendedPosition = MarketPosition.Long;
                }
                else if (action == OrderAction.SellShort)
                {
                    Print($"[ENTRY_FILL_DEBUG] Bar {CurrentBar}: Short entry filled, setting intendedPosition=Short");
                    intendedPosition = MarketPosition.Short;
                }
            }

            string reason = GetOrderReason(order, isEntry, isExit);

            string candleType = GetCandleType(Open[0], Close[0]);
            double fastEmaVal = double.NaN;
            if (fastEma != null && CurrentBar >= 0)
            {
                fastEmaVal = fastEma[0];
            }

            string directionAtExec = "FLAT";
            if (Position.MarketPosition == MarketPosition.Short || action == OrderAction.SellShort)
                directionAtExec = "SHORT";
            else if (Position.MarketPosition == MarketPosition.Long || action == OrderAction.Buy)
                directionAtExec = "LONG";

            string ts = time.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string fastEmaStr = double.IsNaN(fastEmaVal) ? string.Empty : fastEmaVal.ToString("F4");
            double pnlVal = double.NaN;
            if (isExit)
            {
                try
                {
                    var trades = SystemPerformance.AllTrades;
                    if (trades != null && trades.Count > 0)
                    {
                        var last = trades[trades.Count - 1];
                        if (last != null && last.Exit != null)
                            pnlVal = last.ProfitCurrency;
                    }
                }
                catch { }
            }

            pendingLogs.Add(new PendingLogEntry
            {
                BarIndex = CurrentBar,
                Timestamp = ts,
                Direction = directionAtExec,
                OpenAtExec = Open[0],
                HighAtExec = High[0],
                LowAtExec = Low[0],
                CloseAtExec = Close[0],
                CandleType = candleType,
                FastEmaStr = fastEmaStr,
                FastEmaGradDeg = lastFastEmaGradDeg,
                Volume = Volume[0],
                Action = isEntry ? "ENTRY" : (isExit ? "EXIT" : string.Empty),
                OrderName = order.Name,
                Quantity = execution.Quantity,
                Price = execution.Price,
                Pnl = pnlVal,
                Reason = reason,
                PrevOpen = lastPrevOpen,
                PrevClose = lastPrevClose,
                PrevCandleType = lastPrevCandleType,
                AllowLongThisBar = lastAllowLongThisBar,
                AllowShortThisBar = lastAllowShortThisBar,
                TrendUpAtDecision = lastTrendUp,
                TrendDownAtDecision = lastTrendDown,
                DecisionBarIndex = lastDecisionBar,
                PendingShortFromGood = lastPendingShortFromGood,
                PendingLongFromBad = lastPendingLongFromBad,
                BarPattern = isEntry ? GetBarSequencePattern() : string.Empty
            });
        }

        private void LogStrategyParameters()
        {
            try
            {
                if (logWriter == null)
                    return;
                
                // Log header with timestamp
                logWriter.WriteLine($"# BarsOnTheFlow Strategy Parameters - Run Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                logWriter.WriteLine($"# Instrument: {Instrument.FullName}");
                logWriter.WriteLine("#");
                
                // Core parameters
                logWriter.WriteLine($"# Contracts={Contracts}");
                logWriter.WriteLine($"# ExitOnTrendBreak={ExitOnTrendBreak}");
                logWriter.WriteLine($"# ExitOnRetrace={ExitOnRetrace}");
                logWriter.WriteLine($"# TrendRetraceFraction={TrendRetraceFraction:F2}");
                logWriter.WriteLine($"# EnableTrendOverlay={EnableTrendOverlay}");
                logWriter.WriteLine($"# EnableShorts={EnableShorts}");
                logWriter.WriteLine($"# AvoidShortsOnGoodCandle={AvoidShortsOnGoodCandle}");
                logWriter.WriteLine($"# AvoidLongsOnBadCandle={AvoidLongsOnBadCandle}");
                
                // EMA and gradient parameters
                logWriter.WriteLine($"# FastEmaPeriod={FastEmaPeriod}");
                logWriter.WriteLine($"# FastGradLookbackBars={FastGradLookbackBars}");
                logWriter.WriteLine($"# UseChartScaledFastGradDeg={UseChartScaledFastGradDeg}");
                
                // Gradient filter parameters
                logWriter.WriteLine($"# GradientFilterEnabled={GradientFilterEnabled}");
                logWriter.WriteLine($"# SkipShortsAboveGradient={SkipShortsAboveGradient:F2}");
                logWriter.WriteLine($"# SkipLongsBelowGradient={SkipLongsBelowGradient:F2}");
                
                // Entry/Exit parameters
                logWriter.WriteLine($"# MinConsecutiveBars={MinConsecutiveBars}");
                logWriter.WriteLine($"# ReverseOnTrendBreak={ReverseOnTrendBreak}");
                logWriter.WriteLine($"# ExitIfEntryBarOpposite={ExitIfEntryBarOpposite}");
                logWriter.WriteLine($"# StopLossPoints={StopLossPoints}");
                logWriter.WriteLine($"# UseTrailingStop={UseTrailingStop}");
                logWriter.WriteLine($"# UseDynamicStopLoss={UseDynamicStopLoss}");
                logWriter.WriteLine($"# DynamicStopLookback={DynamicStopLookback}");
                logWriter.WriteLine($"# DynamicStopMultiplier={DynamicStopMultiplier}");
                
                // Mid-bar parameters
                logWriter.WriteLine($"# AllowMidBarGradientEntry={AllowMidBarGradientEntry}");
                logWriter.WriteLine($"# AllowMidBarGradientExit={AllowMidBarGradientExit}");
                
                // Debug/Display parameters
                logWriter.WriteLine($"# ShowBarIndexLabels={ShowBarIndexLabels}");
                logWriter.WriteLine($"# ShowFastGradLabels={ShowFastGradLabels}");
                logWriter.WriteLine($"# EnableFastGradDebug={EnableFastGradDebug}");
                logWriter.WriteLine($"# EnableDashboardDiagnostics={EnableDashboardDiagnostics}");
                logWriter.WriteLine($"# DashboardBaseUrl={DashboardBaseUrl}");
                
                logWriter.WriteLine("#");
                logWriter.WriteLine("# --- End Parameters ---");
                logWriter.WriteLine("#");
            }
            catch (Exception ex)
            {
                Print($"[BarsOnTheFlow] Failed to log parameters: {ex.Message}");
            }
        }

        private void WriteParametersJsonFile()
        {
            try
            {
                if (string.IsNullOrEmpty(logFilePath))
                    return;

                // Derive JSON filename from CSV: BarsOnTheFlow_MNQZ24_2024-12-13_12-30-45-123.csv
                // becomes: BarsOnTheFlow_MNQZ24_2024-12-13_12-30-45-123_params.json
                string jsonPath = Path.Combine(
                    Path.GetDirectoryName(logFilePath),
                    Path.GetFileNameWithoutExtension(logFilePath) + "_params.json"
                );

                // Build JSON with all parameters
                var paramsDict = new System.Collections.Generic.Dictionary<string, object>
                {
                    // Core parameters
                    { "Contracts", Contracts },
                    { "ExitOnTrendBreak", ExitOnTrendBreak },
                    { "ExitOnRetrace", ExitOnRetrace },
                    { "TrendRetraceFraction", TrendRetraceFraction },
                    { "EnableTrendOverlay", EnableTrendOverlay },
                    { "EnableShorts", EnableShorts },
                    { "AvoidShortsOnGoodCandle", AvoidShortsOnGoodCandle },
                    { "AvoidLongsOnBadCandle", AvoidLongsOnBadCandle },
                    
                    // EMA and gradient parameters
                    { "FastEmaPeriod", FastEmaPeriod },
                    { "FastGradLookbackBars", FastGradLookbackBars },
                    { "UseChartScaledFastGradDeg", UseChartScaledFastGradDeg },
                    
                    // Gradient filter parameters
                    { "GradientFilterEnabled", GradientFilterEnabled },
                    { "SkipShortsAboveGradient", SkipShortsAboveGradient },
                    { "SkipLongsBelowGradient", SkipLongsBelowGradient },
                    
                    // Entry/Exit parameters
                    { "TrendLookbackBars", TrendLookbackBars },
                    { "MinConsecutiveBars", MinConsecutiveBars },
                    { "UsePnLTiebreaker", UsePnLTiebreaker },
                    { "ReverseOnTrendBreak", ReverseOnTrendBreak },
                    { "ExitIfEntryBarOpposite", ExitIfEntryBarOpposite },
                    { "StopLossPoints", StopLossPoints },
                    { "UseTrailingStop", UseTrailingStop },
                    { "UseDynamicStopLoss", UseDynamicStopLoss },
                    { "DynamicStopLookback", DynamicStopLookback },
                    { "DynamicStopMultiplier", DynamicStopMultiplier },
                    
                    // Mid-bar parameters
                    { "AllowMidBarGradientEntry", AllowMidBarGradientEntry },
                    { "AllowMidBarGradientExit", AllowMidBarGradientExit },
                    
                    // Debug/Display parameters
                    { "ShowBarIndexLabels", ShowBarIndexLabels },
                    { "ShowFastGradLabels", ShowFastGradLabels },
                    { "EnableFastGradDebug", EnableFastGradDebug },
                    { "EnableDashboardDiagnostics", EnableDashboardDiagnostics },
                    { "DashboardBaseUrl", DashboardBaseUrl },
                    { "EnableOpportunityLog", EnableOpportunityLog },
                    
                    // Metadata
                    { "Instrument", Instrument.FullName },
                    { "StartTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") }
                };

                // Serialize to JSON using simple manual approach (no external dependencies)
                var json = new System.Text.StringBuilder();
                json.AppendLine("{");
                
                var items = new System.Collections.Generic.List<string>();
                foreach (var kvp in paramsDict)
                {
                    string value;
                    if (kvp.Value is bool)
                        value = kvp.Value.ToString().ToLower();
                    else if (kvp.Value is string)
                        value = "\"" + kvp.Value.ToString().Replace("\"", "\\\"") + "\"";
                    else
                        value = kvp.Value.ToString();
                    
                    items.Add($"  \"{kvp.Key}\": {value}");
                }
                
                json.Append(string.Join(",\n", items));
                json.AppendLine("\n}");

                // Write to file
                File.WriteAllText(jsonPath, json.ToString());
                Print($"[BarsOnTheFlow] Parameters written to: {jsonPath}");
            }
            catch (Exception ex)
            {
                Print($"[BarsOnTheFlow] Failed to write parameters JSON: {ex.Message}");
            }
        }

        /// <summary>
        /// Exports current strategy state to a JSON file for external API queries.
        /// This allows external tools (like Copilot) to query strategy state without asking the user.
        /// </summary>
        private void ExportStrategyState()
        {
            try
            {
                string stateDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "bin", "Custom", "strategy_state");
                Directory.CreateDirectory(stateDir);
                string statePath = Path.Combine(stateDir, "BarsOnTheFlow_state.json");

                var state = new System.Text.StringBuilder();
                state.AppendLine("{");
                state.AppendLine($"  \"timestamp\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",");
                state.AppendLine($"  \"strategyName\": \"BarsOnTheFlow\",");
                state.AppendLine($"  \"isRunning\": true,");
                state.AppendLine($"  \"currentBar\": {CurrentBar},");
                state.AppendLine($"  \"contracts\": {Contracts},");
                state.AppendLine($"  \"positionMarketPosition\": \"{Position.MarketPosition}\",");
                state.AppendLine($"  \"positionQuantity\": {Position.Quantity},");
                state.AppendLine($"  \"intendedPosition\": \"{intendedPosition}\",");
                state.AppendLine($"  \"stopLossPoints\": {StopLossPoints},");
                state.AppendLine($"  \"useTrailingStop\": {UseTrailingStop.ToString().ToLower()},");
                state.AppendLine($"  \"useDynamicStopLoss\": {UseDynamicStopLoss.ToString().ToLower()},");
                state.AppendLine($"  \"dynamicStopLookback\": {DynamicStopLookback},");
                state.AppendLine($"  \"dynamicStopMultiplier\": {DynamicStopMultiplier},");
                
                // Calculate and export the actual dynamic stop loss value
                int calcStopTicks = CalculateStopLossTicks();
                double calcStopPoints = calcStopTicks / 4.0;
                state.AppendLine($"  \"calculatedStopTicks\": {calcStopTicks},");
                state.AppendLine($"  \"calculatedStopPoints\": {calcStopPoints:F2},");
                
                state.AppendLine($"  \"enableShorts\": {EnableShorts.ToString().ToLower()},");
                state.AppendLine($"  \"avoidLongsOnBadCandle\": {AvoidLongsOnBadCandle.ToString().ToLower()},");
                state.AppendLine($"  \"avoidShortsOnGoodCandle\": {AvoidShortsOnGoodCandle.ToString().ToLower()},");
                state.AppendLine($"  \"exitOnTrendBreak\": {ExitOnTrendBreak.ToString().ToLower()},");
                state.AppendLine($"  \"reverseOnTrendBreak\": {ReverseOnTrendBreak.ToString().ToLower()},");
                state.AppendLine($"  \"fastEmaPeriod\": {FastEmaPeriod},");
                state.AppendLine($"  \"skipLongsBelowGradient\": {SkipLongsBelowGradient},");
                state.AppendLine($"  \"skipShortsAboveGradient\": {SkipShortsAboveGradient},");
                state.AppendLine($"  \"pendingLongFromBad\": {pendingLongFromBad.ToString().ToLower()},");
                state.AppendLine($"  \"pendingShortFromGood\": {pendingShortFromGood.ToString().ToLower()}");
                state.AppendLine("}");

                File.WriteAllText(statePath, state.ToString());
            }
            catch (Exception ex)
            {
                Print($"[BarsOnTheFlow] Failed to export strategy state: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the strategy state file - call periodically or on significant events
        /// </summary>
        private void UpdateStrategyState()
        {
            // Only update every 10 bars to avoid excessive I/O
            if (CurrentBar % 10 == 0)
            {
                ExportStrategyState();
            }
        }

        private void InitializeLog()
        {
            if (logInitialized)
                return;

            try
            {
                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NinjaTrader 8", "bin", "Custom", "strategy_logs");
                Directory.CreateDirectory(logDir);

                string ts = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
                logFilePath = Path.Combine(logDir, $"BarsOnTheFlow_{Instrument.FullName}_{ts}.csv");
                logWriter = new StreamWriter(logFilePath, false) { AutoFlush = true };
                logInitialized = true;
                
                // CSV header (parameters now logged to separate JSON file)
                logWriter.WriteLine("timestamp,bar,direction,open,high,low,close,openFinal,highFinal,lowFinal,closeFinal,candleType,fastEma,fastEmaGradDeg,volume,bodyPct,upperWick,lowerWick,action,orderName,quantity,price,pnl,reason,prevOpen,prevClose,prevCandleType,allowLongThisBar,allowShortThisBar,trendUpAtDecision,trendDownAtDecision,decisionBarIndex,pendingShortFromGood,pendingLongFromBad,barPattern");
                
                // Write parameters to separate JSON file for dashboard
                WriteParametersJsonFile();
                
                // Initialize opportunity analysis log
                if (EnableOpportunityLog)
                {
                    InitializeOpportunityLog(logDir, ts);
                }
                
                // Initialize output window log
                InitializeOutputLog(logDir, ts);
            }
            catch (Exception ex)
            {
                Print($"[BarsOnTheFlow] Failed to initialize log: {ex.Message}");
                logWriter = null;
                logInitialized = false;
            }
        }

        private void InitializeOpportunityLog(string logDir, string ts)
        {
            if (opportunityLogInitialized)
                return;

            try
            {
                opportunityLogPath = Path.Combine(logDir, $"BarsOnTheFlow_Opportunities_{Instrument.FullName}_{ts}.csv");
                opportunityLogWriter = new StreamWriter(opportunityLogPath, false) { AutoFlush = true };
                opportunityLogInitialized = true;
                
                // CSV header for opportunity analysis
                opportunityLogWriter.WriteLine("timestamp,bar,open,high,low,close,candleType,volume,fastEma,fastEmaGradDeg," +
                    "trendUpSignal,trendDownSignal,goodCount,badCount,netPnl,barPattern," +
                    "currentPosition,entryBar,entryPrice,unrealizedPnL," +
                    "allowLongThisBar,allowShortThisBar," +
                    "gradientFilterLong,gradientFilterShort,gradientValue,gradientLongThreshold,gradientShortThreshold," +
                    "pendingLongFromBad,pendingShortFromGood," +
                    "actionTaken,blockReason,opportunityType");
            }
            catch (Exception ex)
            {
                Print($"[BarsOnTheFlow] Failed to initialize opportunity log: {ex.Message}");
                opportunityLogWriter = null;
                opportunityLogInitialized = false;
            }
        }

        private void InitializeOutputLog(string logDir, string ts)
        {
            if (outputLogInitialized)
                return;

            try
            {
                outputLogPath = Path.Combine(logDir, $"BarsOnTheFlow_OutputWindow_{Instrument.FullName}_{ts}.csv");
                outputLogWriter = new StreamWriter(outputLogPath, false) { AutoFlush = true };
                outputLogInitialized = true;
                
                // CSV header for output window log
                outputLogWriter.WriteLine("timestamp,bar,logType,message");
                LogToOutput("INFO", "Output window logging initialized");
            }
            catch (Exception ex)
            {
                Print($"[BarsOnTheFlow] Failed to initialize output log: {ex.Message}");
                outputLogWriter = null;
                outputLogInitialized = false;
            }
        }
        
        private void LogToOutput(string logType, string message)
        {
            if (!outputLogInitialized || outputLogWriter == null)
                return;
                
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                int bar = CurrentBar >= 0 ? CurrentBar : -1;
                string escapedMessage = message.Replace("\"", "\"\""); // Escape quotes for CSV
                outputLogWriter.WriteLine($"{timestamp},{bar},{logType},\"{escapedMessage}\"");
            }
            catch { }
        }
        
        // Wrapper for Print() that also logs to output file
        private void PrintAndLog(string message, string logType = "INFO")
        {
            base.Print(message);  // Call base Print to avoid infinite recursion
            LogToOutput(logType, message);
        }
        
        // Override Print to automatically log all messages
        protected new void Print(string message)
        {
            base.Print(message);
            LogToOutput("INFO", message);
        }

        private void EnsureFastGradDebugWriter()
        {
            if (!FastGradDebugLogToCsv)
                return;

            if (fastGradDebugWriter != null)
                return;

            try
            {
                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NinjaTrader 8", "bin", "Custom", "strategy_logs");
                Directory.CreateDirectory(logDir);

                string ts = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
                fastGradDebugPath = Path.Combine(logDir, $"BarsOnTheFlow_FastGradDebug_{Instrument.FullName}_{ts}.csv");
                fastGradDebugWriter = new StreamWriter(fastGradDebugPath, false) { AutoFlush = true };
                fastGradDebugWriter.WriteLine("timestamp,bar,slope,deg,window,emaOld,emaRecent");
            }
            catch (Exception ex)
            {
                Print($"[BarsOnTheFlow] Failed to init fast-grad debug log: {ex.Message}");
                fastGradDebugWriter = null;
            }
        }

        private void LogLine(string message)
        {
            try
            {
                if (logWriter != null)
                {
                    logWriter.WriteLine(message);
                }
            }
            catch (Exception ex)
            {
                Print($"[BarsOnTheFlow] Log error: {ex.Message}");
            }
        }

        private void LogBarSnapshot(int barsAgo, bool allowLongThisBar, bool allowShortThisBar, bool trendUp, bool trendDown)
        {
            // Log a completed bar even when no orders fired. Use barsAgo=1 for the most recently closed bar.
            if (barsAgo < 1)
                return;

            int barIndex = CurrentBar - barsAgo;
            if (barIndex < 0)
                return;

            try
            {
                if (logWriter == null)
                    return;

                double open = Open[barsAgo];
                double high = High[barsAgo];
                double low = Low[barsAgo];
                double close = Close[barsAgo];
                double volume = Volume[barsAgo];

                string candleType = GetCandleType(open, close);

                double fastEmaVal = (fastEma != null && CurrentBar >= barsAgo) ? fastEma[barsAgo] : double.NaN;
                string fastEmaStr = double.IsNaN(fastEmaVal) ? string.Empty : fastEmaVal.ToString("F4");

                string gradStr = double.IsNaN(lastFastEmaGradDeg) ? string.Empty : lastFastEmaGradDeg.ToString("F4");
                
                double range = high - low;
                double bodyPct = range > 0 ? (close - open) / range : 0;
                double upperWick = high - Math.Max(open, close);
                double lowerWick = Math.Min(open, close) - low;

                MarketPosition pos = Position.MarketPosition;
                string direction = pos == MarketPosition.Long ? "LONG" : (pos == MarketPosition.Short ? "SHORT" : "FLAT");
                int qty = Position.Quantity;
                double avgPrice = Position.AveragePrice;

                string reason = pos == MarketPosition.Flat ? "Flat" : "InTrade";

                string ts = Time[barsAgo].ToString("yyyy-MM-dd HH:mm:ss.fff");

                // Get bar pattern for snapshot (empty for non-entry bars)
                string barPattern = string.Empty;

                // Reuse the existing CSV schema; mark action as BAR snapshot
                string line = string.Format(
                    "{0},{1},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11},{12},{13},{14},{15:F6},{16:F4},{17:F4},{18},{19},{20},{21},{22},{23},{24},{25},{26},{27},{28},{29},{30},{31},{32},{33},{34}",
                    ts, // 0 timestamp
                    barIndex, // 1 bar
                    direction, // 2 direction (position at bar close)
                    open, // 3 open
                    high, // 4 high
                    low, // 5 low
                    close, // 6 close
                    open, // 7 openFinal
                    high, // 8 highFinal
                    low, // 9 lowFinal
                    close, // 10 closeFinal
                    candleType, // 11 candleType
                    fastEmaStr, // 12 fastEma
                    gradStr, // 13 fastEmaGradDeg
                    volume, // 14 volume
                    bodyPct, // 15 bodyPct
                    upperWick, // 16 upperWick
                    lowerWick, // 17 lowerWick
                    "BAR", // 18 action
                    string.Empty, // 19 orderName
                    qty, // 20 quantity (position size)
                    avgPrice, // 21 price (avg price if in trade)
                    string.Empty, // 22 pnl (not computed per-bar)
                    reason, // 23 reason
                    string.Empty, // 24 prevOpen
                    string.Empty, // 25 prevClose
                    string.Empty, // 26 prevCandleType
                    allowLongThisBar, // 27 allowLongThisBar
                    allowShortThisBar, // 28 allowShortThisBar
                    trendUp, // 29 trendUpAtDecision
                    trendDown, // 30 trendDownAtDecision
                    barIndex, // 31 decisionBarIndex (same as bar for snapshot)
                    pendingShortFromGood, // 32 pendingShortFromGood
                    pendingLongFromBad, // 33 pendingLongFromBad
                    barPattern // 34 barPattern
                );

                logWriter.WriteLine(line);
            }
            catch (Exception ex)
            {
                Print($"[BarsOnTheFlow] Log snapshot error: {ex.Message}");
            }
        }

        private string GetOrderReason(Order order, bool isEntry, bool isExit)
        {
            if (order == null)
                return string.Empty;

            if (isEntry)
            {
                if (order.Name == "BarsOnTheFlowLong")
                    return "TrendUp";
                if (order.Name == "BarsOnTheFlowShort")
                    return "TrendDown";
            }

            if (isExit)
            {
                if (order.Name == "BarsOnTheFlowRetrace" || order.Name == "BarsOnTheFlowRetraceS")
                    return "Retrace";
                if (order.Name == "BarsOnTheFlowExit" || order.Name == "BarsOnTheFlowExitS")
                    return "TrendBreak";
                if (order.Name == "BarsOnTheFlowEntryBarOpp" || order.Name == "BarsOnTheFlowEntryBarOppS")
                    return "EntryBarOpposite";
            }

            return order.Name ?? string.Empty;
        }

        private string GetCandleType(double open, double close)
        {
            if (close > open)
                return "good";
            if (close < open)
                return "bad";
            return "good_and_bad"; // doji/flat bar
        }

        private void RecordCompletedBar(double open, double close)
        {
            bool isGood = close > open;
            double pnl = close - open;

            recentGood.Enqueue(isGood);
            if (recentGood.Count > 5)
                recentGood.Dequeue();

            recentPnl.Enqueue(pnl);
            if (recentPnl.Count > 5)
                recentPnl.Dequeue();
        }

        private string GetBarSequencePattern()
        {
            // Use TrendLookbackBars to show the full lookback window analyzed for the trend
            int lookback = Math.Min(TrendLookbackBars, recentGood.Count);
            if (lookback < 1)
                return string.Empty;

            // Take the last 'lookback' bars (the window we analyzed for trend detection)
            var allBars = recentGood.ToArray();
            var bars = allBars.Skip(Math.Max(0, allBars.Length - lookback)).ToList();
            
            // Count consecutive goods and bads for compact notation
            var result = new System.Text.StringBuilder();
            int i = 0;
            while (i < bars.Count)
            {
                bool current = bars[i];
                int count = 1;
                while (i + count < bars.Count && bars[i + count] == current)
                    count++;
                
                if (count > 1)
                {
                    result.Append(count);
                    result.Append(current ? "G" : "B");
                }
                else
                {
                    result.Append(current ? "G" : "B");
                }
                i += count;
            }
            
            return result.ToString();
        }

        private bool IsTrendUp()
        {
            // Ensure we have enough data in the lookback window
            int lookback = Math.Min(TrendLookbackBars, recentGood.Count);
            if (lookback < MinConsecutiveBars)
                return false;

            // Get the last 'lookback' bars
            var lastBars = recentGood.ToArray().Skip(Math.Max(0, recentGood.Count - lookback)).ToArray();
            var lastPnls = recentPnl.ToArray().Skip(Math.Max(0, recentPnl.Count - lookback)).ToArray();
            
            int goodCount = lastBars.Count(g => g);
            double netPnl = lastPnls.Sum();
            
            // Primary condition: minimum consecutive bars met
            if (goodCount >= MinConsecutiveBars)
                return true;
            
            // Secondary condition: PnL tiebreaker (if enabled and we're close to minimum)
            if (UsePnLTiebreaker && goodCount >= (MinConsecutiveBars - 1) && netPnl > 0)
                return true;
            
            return false;
        }

        private bool IsTrendDown()
        {
            // Ensure we have enough data in the lookback window
            int lookback = Math.Min(TrendLookbackBars, recentGood.Count);
            if (lookback < MinConsecutiveBars)
                return false;

            // Get the last 'lookback' bars
            var lastBars = recentGood.ToArray().Skip(Math.Max(0, recentGood.Count - lookback)).ToArray();
            var lastPnls = recentPnl.ToArray().Skip(Math.Max(0, recentPnl.Count - lookback)).ToArray();
            
            int badCount = lastBars.Count(g => !g);
            double netPnl = lastPnls.Sum();
            
            // Primary condition: minimum consecutive bars met
            if (badCount >= MinConsecutiveBars)
                return true;
            
            // Secondary condition: PnL tiebreaker (if enabled and we're close to minimum)
            if (UsePnLTiebreaker && badCount >= (MinConsecutiveBars - 1) && netPnl < 0)
                return true;
            
            return false;
        }

        private void UpdateTrendLifecycle(MarketPosition currentPos)
        {
            if (currentPos == MarketPosition.Long && trendSide != MarketPosition.Long)
            {
                StartTrendTracking(MarketPosition.Long);
            }
            else if (currentPos == MarketPosition.Short && trendSide != MarketPosition.Short)
            {
                StartTrendTracking(MarketPosition.Short);
            }
            else if (currentPos == MarketPosition.Flat && trendSide != MarketPosition.Flat)
            {
                ResetTrendVisuals();
            }

            if (trendSide == MarketPosition.Long || trendSide == MarketPosition.Short)
            {
                UpdateTrendProgress();
                UpdateTrendOverlay();
            }
        }

        private void StartTrendTracking(MarketPosition side)
        {
            trendSide = side;
            trendStartBar = CurrentBar;
            trendEntryPrice = Position.AveragePrice;
            trendMaxProfit = 0;
            trendRectTag = $"BOTF_Rect_{CurrentBar}";
            trendLineTag = $"BOTF_Line_{CurrentBar}";
            bool isLong = side == MarketPosition.Long;
            trendBrush = CreateTrendBrush(isLong ? Colors.LimeGreen : Colors.Red, 0.4);

            Draw.VerticalLine(this, trendLineTag, 0, isLong ? Brushes.DarkGreen : Brushes.Red);
        }

        private void UpdateTrendProgress()
        {
            if (double.IsNaN(trendEntryPrice))
                return;

            double barMfe = trendSide == MarketPosition.Long
                ? High[0] - trendEntryPrice
                : trendEntryPrice - Low[0];
            if (double.IsNaN(trendMaxProfit) || barMfe > trendMaxProfit)
                trendMaxProfit = barMfe;

            double currentProfit = trendSide == MarketPosition.Long
                ? Close[0] - trendEntryPrice
                : trendEntryPrice - Close[0];
            double clampedRetraceFraction = Math.Max(0.0, Math.Min(TrendRetraceFraction, 0.99));
            double triggerProfit = trendMaxProfit * (1.0 - clampedRetraceFraction);

            bool shouldEndTrend = trendMaxProfit > 0 && currentProfit <= triggerProfit;

            if (shouldEndTrend)
            {
                if (ExitOnRetrace)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong("BarsOnTheFlowRetrace", "BarsOnTheFlowLong");
                    else if (Position.MarketPosition == MarketPosition.Short)
                        ExitShort("BarsOnTheFlowRetraceS", "BarsOnTheFlowShort");
                }

                ResetTrendVisuals();
            }
        }

        private void UpdateTrendOverlay()
        {
            if (!EnableTrendOverlay || trendSide == MarketPosition.Flat || trendStartBar < 0)
                return;

            int startBarsAgo = CurrentBar - trendStartBar;
            if (startBarsAgo < 0)
                return;

            double highest = High[startBarsAgo];
            double lowest = Low[startBarsAgo];

            for (int i = 0; i <= startBarsAgo && i < Count; i++)
            {
                highest = Math.Max(highest, High[i]);
                lowest = Math.Min(lowest, Low[i]);
            }

            Draw.Rectangle(this, trendRectTag, false, startBarsAgo, highest, 0, lowest, null, trendBrush, 1);
        }

        private void ResetTrendVisuals()
        {
            if (!string.IsNullOrEmpty(trendRectTag))
                RemoveDrawObject(trendRectTag);
            if (!string.IsNullOrEmpty(trendLineTag))
                RemoveDrawObject(trendLineTag);

            trendStartBar = -1;
            trendRectTag = null;
            trendLineTag = null;
            trendBrush = null;
            trendSide = MarketPosition.Flat;
            trendEntryPrice = double.NaN;
            trendMaxProfit = double.NaN;
        }

        private Brush CreateTrendBrush(Color color, double opacity)
        {
            byte alpha = (byte)(Math.Max(0.0, Math.Min(opacity, 1.0)) * 255);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        private class PendingLogEntry
        {
            public int BarIndex { get; set; }
            public string Timestamp { get; set; }
            public string Direction { get; set; }
            public double OpenAtExec { get; set; }
            public double HighAtExec { get; set; }
            public double LowAtExec { get; set; }
            public double CloseAtExec { get; set; }
            public string CandleType { get; set; }
            public string FastEmaStr { get; set; }
            public double FastEmaGradDeg { get; set; }
            public double Volume { get; set; }
            public string Action { get; set; }
            public string OrderName { get; set; }
            public int Quantity { get; set; }
            public double Price { get; set; }
            public double Pnl { get; set; }
            public string Reason { get; set; }
            public double PrevOpen { get; set; }
            public double PrevClose { get; set; }
            public string PrevCandleType { get; set; }
            public bool AllowLongThisBar { get; set; }
            public bool AllowShortThisBar { get; set; }
            public bool TrendUpAtDecision { get; set; }
            public bool TrendDownAtDecision { get; set; }
            public int DecisionBarIndex { get; set; }
            public bool PendingShortFromGood { get; set; }
            public bool PendingLongFromBad { get; set; }
            public string BarPattern { get; set; }
            public string PnlStr => double.IsNaN(Pnl) ? string.Empty : Pnl.ToString("F2");
        }

        // Captured decision context for richer execution logging
        private double lastPrevOpen = double.NaN;
        private double lastPrevClose = double.NaN;
        private string lastPrevCandleType = string.Empty;
        private bool lastAllowLongThisBar;
        private bool lastAllowShortThisBar;
        private bool lastTrendUp;
        private bool lastTrendDown;
        private int lastDecisionBar = -1;
        private bool lastPendingShortFromGood;
        private bool lastPendingLongFromBad;
        private double lastFastGradDegForDecision = double.NaN;

        // Dashboard diagnostics (optional)
        private static System.Net.Http.HttpClient sharedClient;
        private static readonly object clientLock = new object();

        private void CreateBarNavPanel()
        {
            if (ChartControl == null || ChartControl.Parent == null)
                return;

            try
            {
                // Create main container grid with 2 rows
                barNavPanel = new System.Windows.Controls.Grid
                {
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top,
                    Margin = new System.Windows.Thickness(0, 10, 100, 0), // Offset left by 100px
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 30, 30, 30)),
                    Width = 240
                };

                // Add rounded corners
                barNavPanel.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = System.Windows.Media.Colors.Black,
                    Direction = 320,
                    ShadowDepth = 3,
                    Opacity = 0.5,
                    BlurRadius = 5
                };

                // Create row definitions
                barNavPanel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                barNavPanel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

                // Create column definitions for first row (bar navigation)
                barNavPanel.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                barNavPanel.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

                // ===== ROW 0: BAR NAVIGATION =====
                
                // Create TextBox for bar number input
                barNavTextBox = new System.Windows.Controls.TextBox
                {
                    Text = "",
                    FontSize = 14,
                    VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                    Padding = new System.Windows.Thickness(8, 5, 8, 5),
                    Margin = new System.Windows.Thickness(5, 5, 5, 5),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 40, 40)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(96, 165, 250)),
                    BorderThickness = new System.Windows.Thickness(1),
                    ToolTip = "Paste bar number, click Go",
                    Focusable = true,
                    IsReadOnly = false,
                    AcceptsReturn = false
                };
                System.Windows.Controls.Grid.SetColumn(barNavTextBox, 0);
                System.Windows.Controls.Grid.SetRow(barNavTextBox, 0);
                
                // Give the textbox focus when clicked
                barNavTextBox.GotFocus += (sender, e) =>
                {
                    barNavTextBox.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 50));
                };
                
                barNavTextBox.LostFocus += (sender, e) =>
                {
                    barNavTextBox.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 40, 40));
                };

                // Create Go button
                barNavButton = new System.Windows.Controls.Button
                {
                    Content = "Go",
                    FontSize = 12,
                    FontWeight = System.Windows.FontWeights.Bold,
                    Width = 45,
                    Margin = new System.Windows.Thickness(0, 5, 5, 5),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(96, 165, 250)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new System.Windows.Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Navigate to bar"
                };
                System.Windows.Controls.Grid.SetColumn(barNavButton, 1);
                System.Windows.Controls.Grid.SetRow(barNavButton, 0);

                // Handle button click
                barNavButton.Click += (sender, e) => NavigateToBar();

                // ===== ROW 1: STOP LOSS CONTROLS =====
                
                // Create a horizontal StackPanel for stop loss controls
                var stopLossPanel = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new System.Windows.Thickness(5, 0, 5, 5)
                };
                System.Windows.Controls.Grid.SetColumn(stopLossPanel, 0);
                System.Windows.Controls.Grid.SetRow(stopLossPanel, 1);
                System.Windows.Controls.Grid.SetColumnSpan(stopLossPanel, 2); // Span both columns
                
                // Create minus button
                stopLossMinusButton = new System.Windows.Controls.Button
                {
                    Content = "−",
                    FontSize = 16,
                    FontWeight = System.Windows.FontWeights.Bold,
                    Width = 30,
                    Height = 30,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new System.Windows.Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Decrease stop loss by 5 points"
                };
                stopLossMinusButton.Click += (sender, e) => AdjustStopLoss(-5);
                
                // Create stop loss display textbox
                stopLossTextBox = new System.Windows.Controls.TextBox
                {
                    Text = StopLossPoints.ToString(),
                    FontSize = 13,
                    FontWeight = System.Windows.FontWeights.SemiBold,
                    VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                    HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                    Padding = new System.Windows.Thickness(6, 5, 6, 5),
                    Margin = new System.Windows.Thickness(3, 0, 3, 0),
                    Width = 55,
                    Height = 30,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 40, 40)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)),
                    BorderThickness = new System.Windows.Thickness(1),
                    ToolTip = "Current stop loss in points",
                    IsReadOnly = true,
                    Focusable = false
                };
                
                // Create plus button
                stopLossPlusButton = new System.Windows.Controls.Button
                {
                    Content = "+",
                    FontSize = 16,
                    FontWeight = System.Windows.FontWeights.Bold,
                    Width = 30,
                    Height = 30,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new System.Windows.Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Increase stop loss by 5 points"
                };
                stopLossPlusButton.Click += (sender, e) => AdjustStopLoss(5);
                
                // Add controls to stop loss panel
                stopLossPanel.Children.Add(stopLossMinusButton);
                stopLossPanel.Children.Add(stopLossTextBox);
                stopLossPanel.Children.Add(stopLossPlusButton);

                // Add all controls to main panel
                barNavPanel.Children.Add(barNavTextBox);
                barNavPanel.Children.Add(barNavButton);
                barNavPanel.Children.Add(stopLossPanel);

                // Add panel to chart
                if (ChartControl.Parent is System.Windows.Controls.Grid)
                {
                    var parent = ChartControl.Parent as System.Windows.Controls.Grid;
                    parent.Children.Add(barNavPanel);
                }
            }
            catch (Exception ex)
            {
                Print($"[BarsOnTheFlow] Failed to create bar navigation panel: {ex.Message}");
            }
        }

        private void AdjustStopLoss(int delta)
        {
            try
            {
                // Update the stop loss value
                int newStopLoss = Math.Max(0, StopLossPoints + delta);
                StopLossPoints = newStopLoss;
                
                // Update the display
                if (stopLossTextBox != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() =>
                    {
                        stopLossTextBox.Text = newStopLoss.ToString();
                    });
                }
                
                // Update stop loss for any active position
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    string orderName = Position.MarketPosition == MarketPosition.Long ? "BarsOnTheFlowLong" : "BarsOnTheFlowShort";
                    if (newStopLoss > 0)
                    {
                        if (UseTrailingStop)
                        {
                            SetTrailStop(orderName, CalculationMode.Ticks, newStopLoss * 4, false);
                            Print($"[Stop Loss] Updated active {Position.MarketPosition} position trailing stop to {newStopLoss} points");
                        }
                        else
                        {
                            SetStopLoss(orderName, CalculationMode.Ticks, newStopLoss * 4, false);
                            Print($"[Stop Loss] Updated active {Position.MarketPosition} position stop loss to {newStopLoss} points");
                        }
                    }
                    else
                    {
                        Print($"[Stop Loss] Warning: Stop loss disabled (0 points) - existing stop not removed");
                    }
                }
                
                Print($"[Stop Loss] Adjusted to {newStopLoss} points (change: {(delta > 0 ? "+" : "")}{delta})");
            }
            catch (Exception ex)
            {
                Print($"[Stop Loss] Failed to adjust stop loss: {ex.Message}");
            }
        }

        private void NavigateToBar()
        {
            if (barNavTextBox == null)
                return;

            try
            {
                string input = barNavTextBox.Text.Trim();
                if (string.IsNullOrEmpty(input))
                {
                    Print("[Bar Nav] No bar number entered.");
                    return;
                }

                if (int.TryParse(input, out int targetBar))
                {
                    // Validate bar number
                    if (targetBar < 0)
                    {
                        Print($"[Bar Nav] Invalid bar number: {targetBar}. Must be >= 0.");
                        return;
                    }

                    if (targetBar > CurrentBar)
                    {
                        Print($"[Bar Nav] Bar {targetBar} is beyond current bar {CurrentBar}.");
                        return;
                    }

                    Print($"[Bar Nav] Attempting to navigate to bar {targetBar}...");

                    // Calculate barsAgo from current bar
                    int barsAgo = CurrentBar - targetBar;
                    
                    // Get the time of the target bar
                    if (barsAgo >= 0 && barsAgo < Time.Count)
                    {
                        DateTime targetTime = Time[barsAgo];
                        
                        // Use ChartControl to scroll - need to set the slider position
                        if (ChartControl != null && ChartBars != null)
                        {
                            ChartControl.Dispatcher.InvokeAsync(() =>
                            {
                                try
                                {
                                    // Get the chart's scrollbar and set position
                                    // The Properties.SlotsPainted controls how many bars are visible
                                    // We need to adjust the scroll position
                                    int slotsVisible = ChartControl.SlotsPainted;
                                    
                                    // Calculate where to scroll - put target bar roughly in center
                                    int scrollToSlot = Math.Max(0, targetBar - slotsVisible / 2);
                                    
                                    // Use reflection or property to scroll
                                    // ChartControl has a scrollbar we need to manipulate
                                    var chartWindowType = ChartControl.OwnerChart.GetType();
                                    var scrollMethod = chartWindowType.GetMethod("ScrollToBar", 
                                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                                    
                                    if (scrollMethod != null)
                                    {
                                        scrollMethod.Invoke(ChartControl.OwnerChart, new object[] { targetBar });
                                        Print($"[Bar Nav] Scrolled to bar {targetBar} via ScrollToBar");
                                    }
                                    else
                                    {
                                        // Fallback - try to manipulate the horizontal scrollbar directly
                                        // Find the scrollbar in the visual tree
                                        var scrollBar = FindVisualChild<System.Windows.Controls.Primitives.ScrollBar>(ChartControl.OwnerChart as System.Windows.DependencyObject);
                                        if (scrollBar != null)
                                        {
                                            double scrollPercent = (double)targetBar / CurrentBar;
                                            scrollBar.Value = scrollBar.Maximum * scrollPercent;
                                            Print($"[Bar Nav] Adjusted scrollbar to {scrollPercent:P0} for bar {targetBar}");
                                        }
                                        else
                                        {
                                            Print($"[Bar Nav] Could not find scroll mechanism. Target bar {targetBar} at time {targetTime}");
                                        }
                                    }
                                    
                                    ForceRefresh();
                                }
                                catch (Exception ex)
                                {
                                    Print($"[Bar Nav] Navigation error: {ex.Message}");
                                }
                            });
                        }
                    }
                    else
                    {
                        Print($"[Bar Nav] BarsAgo {barsAgo} out of range for Time array (Count={Time.Count})");
                    }
                }
                else
                {
                    Print($"[Bar Nav] Invalid input: '{input}'. Please enter a number.");
                }
            }
            catch (Exception ex)
            {
                Print($"[Bar Nav] Error: {ex.Message}");
            }
        }
        
        private static T FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
        {
            if (parent == null) return null;
            
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;
                    
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }

        private void CaptureDecisionContext(double prevOpen, double prevClose, bool allowLongThisBar, bool allowShortThisBar, bool trendUp, bool trendDown)
        {
            lastDecisionBar = CurrentBar;
            lastPrevOpen = prevOpen;
            lastPrevClose = prevClose;
            lastPrevCandleType = GetCandleType(prevOpen, prevClose);
            lastAllowLongThisBar = allowLongThisBar;
            lastAllowShortThisBar = allowShortThisBar;
            lastTrendUp = trendUp;
            lastTrendDown = trendDown;
            lastPendingShortFromGood = pendingShortFromGood;
            lastPendingLongFromBad = pendingLongFromBad;
            lastFastGradDegForDecision = lastFastEmaGradDeg;
        }

        private void ApplyStopLoss(string orderName)
        {
            int stopLossTicks = CalculateStopLossTicks();
            Print($"[ApplyStopLoss] Called for {orderName}, StopLossTicks={stopLossTicks}, Position={Position.MarketPosition}, UseTrailingStop={UseTrailingStop}, UseDynamicStopLoss={UseDynamicStopLoss}");
            
            if (stopLossTicks > 0 && Position.MarketPosition == MarketPosition.Flat)
            {
                if (UseTrailingStop)
                {
                    SetTrailStop(orderName, CalculationMode.Ticks, stopLossTicks, false);
                    Print($"[ApplyStopLoss] Set TRAILING stop loss: {stopLossTicks / 4.0:F2} points ({stopLossTicks} ticks)");
                }
                else
                {
                    SetStopLoss(orderName, CalculationMode.Ticks, stopLossTicks, false);
                    Print($"[ApplyStopLoss] Set STATIC stop loss: {stopLossTicks / 4.0:F2} points ({stopLossTicks} ticks)");
                }
            }
            else
            {
                Print($"[ApplyStopLoss] NOT setting stop loss - StopLossTicks={stopLossTicks}, Position={Position.MarketPosition}");
            }
        }

        // Cache for volume-aware stop to avoid repeated API calls
        private int cachedVolumeAwareStopTicks = 0;
        private int cachedVolumeAwareStopHour = -1;
        private DateTime cachedVolumeAwareStopTime = DateTime.MinValue;

        private int CalculateStopLossTicks()
        {
            if (!UseDynamicStopLoss || CurrentBar < DynamicStopLookback)
            {
                // Use fixed stop loss
                return StopLossPoints * 4; // Convert points to ticks
            }

            // Try volume-aware stop from API first
            if (UseVolumeAwareStop)
            {
                int volumeAwareStop = GetVolumeAwareStopTicks();
                if (volumeAwareStop > 0)
                {
                    // Apply multiplier
                    int adjustedStop = (int)Math.Round(volumeAwareStop * DynamicStopMultiplier);
                    Print($"[DynamicStopLoss] Volume-aware stop: {volumeAwareStop} ticks * {DynamicStopMultiplier} = {adjustedStop} ticks");
                    return Math.Max(adjustedStop, 4); // Minimum 1 point
                }
            }

            // Fallback: Calculate average range of recent candles
            double totalRange = 0;
            int barsToCheck = Math.Min(DynamicStopLookback, CurrentBar);
            
            for (int i = 1; i <= barsToCheck; i++)
            {
                double candleRange = High[i] - Low[i];
                totalRange += candleRange;
            }

            double averageRange = totalRange / barsToCheck;
            double stopLossPrice = averageRange * DynamicStopMultiplier;
            
            // Convert price to ticks (4 ticks per point for MNQ)
            int stopLossTicks = (int)Math.Round(stopLossPrice * 4);
            
            Print($"[DynamicStopLoss] Fallback: Avg range of last {barsToCheck} bars: {averageRange:F2}, Multiplier: {DynamicStopMultiplier}, Stop: {stopLossPrice:F2} ({stopLossTicks} ticks)");
            
            return Math.Max(stopLossTicks, 4); // Minimum 1 point (4 ticks)
        }

        private int GetVolumeAwareStopTicks()
        {
            try
            {
                // Cache for 1 minute per hour to avoid spamming API
                int currentHour = Time[0].Hour;
                if (cachedVolumeAwareStopTicks > 0 && 
                    cachedVolumeAwareStopHour == currentHour &&
                    (DateTime.Now - cachedVolumeAwareStopTime).TotalMinutes < 1)
                {
                    return cachedVolumeAwareStopTicks;
                }

                EnsureHttpClient();
                
                // Get current volume from the last completed bar
                long currentVolume = (long)Volume[1];
                
                string url = $"http://localhost:51888/api/volatility/recommended-stop?hour={currentHour}&volume={currentVolume}&symbol=MNQ";
                
                var task = sharedClient.GetStringAsync(url);
                if (!task.Wait(200)) // 200ms timeout
                {
                    Print("[VolumeAwareStop] API timeout");
                    return 0;
                }
                
                string json = task.Result;
                
                // Simple JSON parsing (avoiding Newtonsoft dependency)
                // Look for "recommended_stop_ticks": N
                int startIdx = json.IndexOf("\"recommended_stop_ticks\":");
                if (startIdx < 0) return 0;
                
                startIdx += "\"recommended_stop_ticks\":".Length;
                int endIdx = json.IndexOfAny(new[] { ',', '}' }, startIdx);
                if (endIdx < 0) return 0;
                
                string ticksStr = json.Substring(startIdx, endIdx - startIdx).Trim();
                if (int.TryParse(ticksStr, out int ticks))
                {
                    // Extract volume condition for logging
                    string volumeCondition = "NORMAL";
                    int vcStart = json.IndexOf("\"volume_condition\":\"");
                    if (vcStart >= 0)
                    {
                        vcStart += "\"volume_condition\":\"".Length;
                        int vcEnd = json.IndexOf("\"", vcStart);
                        if (vcEnd > vcStart)
                            volumeCondition = json.Substring(vcStart, vcEnd - vcStart);
                    }
                    
                    // Extract confidence
                    string confidence = "UNKNOWN";
                    int confStart = json.IndexOf("\"confidence\":\"");
                    if (confStart >= 0)
                    {
                        confStart += "\"confidence\":\"".Length;
                        int confEnd = json.IndexOf("\"", confStart);
                        if (confEnd > confStart)
                            confidence = json.Substring(confStart, confEnd - confStart);
                    }
                    
                    Print($"[VolumeAwareStop] Hour={currentHour}, Volume={currentVolume}, Condition={volumeCondition}, Confidence={confidence}, Stop={ticks} ticks");
                    
                    // Cache the result
                    cachedVolumeAwareStopTicks = ticks;
                    cachedVolumeAwareStopHour = currentHour;
                    cachedVolumeAwareStopTime = DateTime.Now;
                    
                    return ticks;
                }
            }
            catch (Exception ex)
            {
                Print($"[VolumeAwareStop] Error: {ex.Message}");
            }
            
            return 0;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private bool ShouldSkipLongDueToGradient(double gradDeg)
        {
            return GradientFilterEnabled && !double.IsNaN(gradDeg) && gradDeg < SkipLongsBelowGradient;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private bool ShouldSkipShortDueToGradient(double gradDeg)
        {
            return GradientFilterEnabled && !double.IsNaN(gradDeg) && gradDeg > SkipShortsAboveGradient;
        }

        // Rate limit bar sample recording
        private int lastRecordedBarSample = -1;

        private void RecordBarSample()
        {
            // Only record once per bar
            if (CurrentBar == lastRecordedBarSample) return;
            lastRecordedBarSample = CurrentBar;

            try
            {
                EnsureHttpClient();
                
                // Fire and forget - don't wait for response
                var content = new StringContent(
                    $"{{\"timestamp\":\"{Time[1]:yyyy-MM-dd HH:mm:ss}\"," +
                    $"\"bar_index\":{CurrentBar}," +
                    $"\"symbol\":\"MNQ\"," +
                    $"\"open\":{Open[1]}," +
                    $"\"high\":{High[1]}," +
                    $"\"low\":{Low[1]}," +
                    $"\"close\":{Close[1]}," +
                    $"\"volume\":{(long)Volume[1]}," +
                    $"\"direction\":\"{Position.MarketPosition}\"," +
                    $"\"in_trade\":{(Position.MarketPosition != MarketPosition.Flat).ToString().ToLower()}}}",
                    System.Text.Encoding.UTF8, "application/json");
                
                // Fire and forget with no wait
                sharedClient.PostAsync("http://localhost:51888/api/volatility/record-bar", content);
            }
            catch
            {
                // Silently ignore - recording bar samples is optional
            }
        }

        private void EnsureHttpClient()
        {
            if (sharedClient != null)
                return;

            lock (clientLock)
            {
                if (sharedClient == null)
                {
                    sharedClient = new HttpClient();
                    sharedClient.Timeout = TimeSpan.FromMilliseconds(300);
                    sharedClient.DefaultRequestHeaders.ConnectionClose = false;
                }
            }
        }

        private void SendDashboardDiag(bool allowLongThisBar, bool allowShortThisBar)
        {
            try
            {
                EnsureHttpClient();

                int barIdx = CurrentBar;
                DateTime ts = Time[1];
                double open = Open[1];
                double high = High[1];
                double low = Low[1];
                double close = Close[1];
                double fastEmaVal = fastEma != null ? fastEma[1] : double.NaN;

                var ci = System.Globalization.CultureInfo.InvariantCulture;
                var json = new StringBuilder();
                json.Append("{");
                json.Append("\"barIndex\":").Append(barIdx).Append(',');
                json.Append("\"time\":\"").Append(ts.ToString("o")).Append("\",");
                json.Append("\"open\":").Append(open.ToString(ci)).Append(',');
                json.Append("\"high\":").Append(high.ToString(ci)).Append(',');
                json.Append("\"low\":").Append(low.ToString(ci)).Append(',');
                json.Append("\"close\":").Append(close.ToString(ci)).Append(',');
                if (!double.IsNaN(fastEmaVal))
                    json.Append("\"fastEMA\":").Append(fastEmaVal.ToString(ci)).Append(',');
                if (!double.IsNaN(lastFastEmaSlope))
                    json.Append("\"fastGrad\":").Append(lastFastEmaSlope.ToString(ci)).Append(',');
                if (!double.IsNaN(lastFastEmaGradDeg))
                    json.Append("\"fastGradDeg\":").Append(lastFastEmaGradDeg.ToString(ci)).Append(',');
                json.Append("\"allowLongThisBar\":").Append(allowLongThisBar ? "true" : "false").Append(',');
                json.Append("\"allowShortThisBar\":").Append(allowShortThisBar ? "true" : "false");
                json.Append("}");

                var content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");
                var cts = new CancellationTokenSource(300);
                var url = DashboardBaseUrl.TrimEnd('/') + "/diag";
                sharedClient.PostAsync(url, content, cts.Token).ContinueWith(t => { /* dashboard send is best-effort */ });
            }
            catch
            {
                // Dashboard diagnostics are optional; swallow exceptions to avoid impacting the strategy.
            }
        }

        private void CheckMidBarGradientEntry()
        {
            // Only check if we're waiting for gradient and currently flat
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                waitingForLongGradient = false;
                waitingForShortGradient = false;
                return;
            }
            
            // Recompute current gradient using CURRENT tick's EMA value (barsAgo=0)
            // This gives us the real-time gradient as the bar forms
            if (fastEma == null || CurrentBar < 1)
                return;
            
            // Calculate current real-time gradient: EMA[0] (current tick) vs EMA[1] (previous bar close)
            double currentGradDeg = double.NaN;
            
            if (UseChartScaledFastGradDeg && ChartControl != null && ChartBars != null && ChartPanel != null)
            {
                // Chart-scaled: use current EMA[0] vs EMA[1]
                double emaRecent = fastEma[0];  // Current tick value
                double emaOld = fastEma[1];     // Previous bar close
                
                // Get pixel spacing between bars using ChartControl API
                double xRecent = ChartControl.GetXByBarIndex(ChartBars, CurrentBar);
                double xOld = ChartControl.GetXByBarIndex(ChartBars, CurrentBar - 1);
                double dx = Math.Abs(xRecent - xOld);
                
                double panelHeight = ChartPanel.H;
                double priceMax = ChartPanel.MaxValue;
                double priceMin = ChartPanel.MinValue;
                double priceRange = priceMax - priceMin;
                
                if (priceRange > 0 && panelHeight > 0 && dx >= 1e-6)
                {
                    // Convert price to Y pixel (screen Y increases downward)
                    double yRecent = panelHeight * (priceMax - emaRecent) / priceRange;
                    double yOld = panelHeight * (priceMax - emaOld) / priceRange;
                    double dyPixels = Math.Abs(yRecent - yOld);
                    
                    double angleRad = Math.Atan2(dyPixels, dx);
                    double angleMagnitude = angleRad * (180.0 / Math.PI);
                    
                    // Apply sign based on price movement direction
                    double priceDelta = emaRecent - emaOld;
                    currentGradDeg = priceDelta >= 0 ? angleMagnitude : -angleMagnitude;
                }
            }
            
            // Fallback to simple gradient if chart-scaled failed or not enabled
            if (double.IsNaN(currentGradDeg) && fastEma != null)
            {
                // Simple gradient: current EMA vs previous bar's EMA
                double emaRecent = fastEma[0];
                double emaOld = fastEma[1];
                double slope = emaRecent - emaOld;
                double angleRad = Math.Atan(slope);
                currentGradDeg = angleRad * (180.0 / Math.PI);
            }
            
            if (double.IsNaN(currentGradDeg))
                return;
            
            // Check if gradient now meets threshold for long entry
            if (waitingForLongGradient && currentGradDeg >= SkipLongsBelowGradient)
            {
                Print($"[MidBar Entry] Bar {CurrentBar}: Long gradient crossed! {currentGradDeg:F2}° >= {SkipLongsBelowGradient:F2}°");
                if (intendedPosition != MarketPosition.Long)
                {
                    PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering LONG (mid-bar gradient), CurrentPos={Position.Quantity}, Contracts={Contracts}");
                    EnterLong(Math.Max(1, Contracts), "BarsOnTheFlowLong");
                    ApplyStopLoss("BarsOnTheFlowLong");
                    lastEntryBarIndex = CurrentBar;
                    lastEntryDirection = MarketPosition.Long;
                    intendedPosition = MarketPosition.Long;
                }
                else
                {
                    PrintAndLog($"[Entry Skip] Bar {CurrentBar}: Already intendedPosition=Long, skipping mid-bar gradient entry", "DEBUG");
                }
                waitingForLongGradient = false;
            }
            // Check if gradient now meets threshold for short entry
            else if (waitingForShortGradient && currentGradDeg <= SkipShortsAboveGradient)
            {
                Print($"[MidBar Entry] Bar {CurrentBar}: Short gradient crossed! {currentGradDeg:F2}° <= {SkipShortsAboveGradient:F2}°");
                if (intendedPosition != MarketPosition.Short)
                {
                    PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering SHORT (mid-bar gradient), CurrentPos={Position.Quantity}, Contracts={Contracts}");
                    EnterShort(Math.Max(1, Contracts), "BarsOnTheFlowShort");
                    ApplyStopLoss("BarsOnTheFlowShort");
                    lastEntryBarIndex = CurrentBar;
                    lastEntryDirection = MarketPosition.Short;
                    intendedPosition = MarketPosition.Short;
                }
                else
                {
                    PrintAndLog($"[Entry Skip] Bar {CurrentBar}: Already intendedPosition=Short, skipping mid-bar gradient short entry", "DEBUG");
                }
                waitingForShortGradient = false;
            }
        }

        private void CheckMidBarGradientExit()
        {
            // Only process if we have waiting exit flags set
            if (!waitingToExitLongOnGradient && !waitingToExitShortOnGradient)
                return;
            
            if (Position.MarketPosition == MarketPosition.Flat)
                return;
            
            // Recompute current gradient using CURRENT tick's EMA value (barsAgo=0)
            if (fastEma == null || CurrentBar < 1)
                return;
            
            // Calculate current real-time gradient: EMA[0] (current tick) vs EMA[1] (previous bar close)
            double currentGradDeg = double.NaN;
            
            if (UseChartScaledFastGradDeg && ChartControl != null && ChartBars != null && ChartPanel != null)
            {
                // Chart-scaled: use current EMA[0] vs EMA[1]
                double emaRecent = fastEma[0];  // Current tick value
                double emaOld = fastEma[1];     // Previous bar close
                
                // Get pixel spacing between bars using ChartControl API
                double xRecent = ChartControl.GetXByBarIndex(ChartBars, CurrentBar);
                double xOld = ChartControl.GetXByBarIndex(ChartBars, CurrentBar - 1);
                double dx = Math.Abs(xRecent - xOld);
                
                double panelHeight = ChartPanel.H;
                double priceMax = ChartPanel.MaxValue;
                double priceMin = ChartPanel.MinValue;
                double priceRange = priceMax - priceMin;
                
                if (priceRange > 0 && panelHeight > 0 && dx >= 1e-6)
                {
                    // Convert price to Y pixel (screen Y increases downward)
                    double yRecent = panelHeight * (priceMax - emaRecent) / priceRange;
                    double yOld = panelHeight * (priceMax - emaOld) / priceRange;
                    double dyPixels = Math.Abs(yRecent - yOld);
                    
                    double angleRad = Math.Atan2(dyPixels, dx);
                    double angleMagnitude = angleRad * (180.0 / Math.PI);
                    
                    // Apply sign based on price movement direction
                    double priceDelta = emaRecent - emaOld;
                    currentGradDeg = priceDelta >= 0 ? angleMagnitude : -angleMagnitude;
                }
            }
            
            // Fallback to simple gradient if chart-scaled failed or not enabled
            if (double.IsNaN(currentGradDeg) && fastEma != null)
            {
                // Simple gradient: current EMA vs previous bar's EMA
                double emaRecent = fastEma[0];
                double emaOld = fastEma[1];
                double slope = emaRecent - emaOld;
                double angleRad = Math.Atan(slope);
                currentGradDeg = angleRad * (180.0 / Math.PI);
            }
            
            if (double.IsNaN(currentGradDeg))
                return;
            
            // Exit long if gradient drops below threshold (and we were waiting for it)
            if (waitingToExitLongOnGradient && Position.MarketPosition == MarketPosition.Long && currentGradDeg < SkipLongsBelowGradient)
            {
                Print($"[EXIT_DEBUG] Bar {CurrentBar}: waitingToExitLongOnGradient resolving - gradient dropped {currentGradDeg:F2}° < {SkipLongsBelowGradient:F2}° - Exiting LONG");
                Print($"[MidBar Exit] Bar {CurrentBar}: Long gradient dropped! {currentGradDeg:F2}° < {SkipLongsBelowGradient:F2}° - Exiting");
                ExitLong("BarsOnTheFlowGradExit", "BarsOnTheFlowLong");
                waitingToExitLongOnGradient = false;
            }
            // Exit short if gradient rises above threshold (and we were waiting for it)
            else if (waitingToExitShortOnGradient && Position.MarketPosition == MarketPosition.Short && currentGradDeg > SkipShortsAboveGradient)
            {
                Print($"[EXIT_DEBUG] Bar {CurrentBar}: waitingToExitShortOnGradient resolving - gradient rose {currentGradDeg:F2}° > {SkipShortsAboveGradient:F2}° - Exiting SHORT");
                Print($"[MidBar Exit] Bar {CurrentBar}: Short gradient rose! {currentGradDeg:F2}° > {SkipShortsAboveGradient:F2}° - Exiting");
                ExitShort("BarsOnTheFlowGradExitS", "BarsOnTheFlowShort");
                waitingToExitShortOnGradient = false;
            }
        }

        private double ComputeFastEmaGradient(int lookback, out double angleDeg)
        {
            angleDeg = double.NaN;

            if (fastEma == null)
                return double.NaN;

            int window = Math.Max(2, lookback);
            // Use only completed bars (skip the forming bar at index 0) for a stable slope
            if (CurrentBar < window)
                return double.NaN;

            // Simple linear regression slope of EMA over the window using completed bars
            // x increases with time (oldest bar has smallest x, most recent completed has largest x)
            double sumX = 0.0;
            double sumY = 0.0;
            double sumXY = 0.0;
            double sumXX = 0.0;
            for (int i = 0; i < window; i++)
            {
                double x = i;
                // y uses only completed bars: i=0 -> oldest (barsAgo = window), i=window-1 -> most recent completed (barsAgo = 1)
                int barsAgo = window - i;
                double y = fastEma[barsAgo];
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumXX += x * x;
            }

            double denom = (window * sumXX) - (sumX * sumX);
            if (Math.Abs(denom) < 1e-8)
                return double.NaN;

            double slope = ((window * sumXY) - (sumX * sumY)) / denom;

            // Convert slope (price units per bar) to degrees for interpretability
            double angleRad = Math.Atan(slope);
            angleDeg = angleRad * (180.0 / Math.PI);
            return slope;
        }

        private double ComputeChartScaledFastEmaDeg(int lookback)
        {
            if (fastEma == null || ChartControl == null || ChartBars == null || ChartPanel == null)
                return double.NaN;

            // For immediate visual slope, use 2 most recent completed bars (barsAgo 1 and 2)
            // This matches what you see when you put a protractor on the EMA line at that bar
            if (CurrentBar < 2)
                return double.NaN;

            int barRecent = CurrentBar - 1;        // most recent completed bar
            int barOld = CurrentBar - 2;           // one bar before that

            double emaRecent = fastEma[1];          // EMA at barRecent
            double emaOld = fastEma[2];             // EMA at barOld

            double xRecent = ChartControl.GetXByBarIndex(ChartBars, barRecent);
            double xOld = ChartControl.GetXByBarIndex(ChartBars, barOld);

            double panelHeight = ChartPanel.H;
            double priceMax = ChartPanel.MaxValue;
            double priceMin = ChartPanel.MinValue;
            double priceRange = priceMax - priceMin;

            if (priceRange <= 0 || panelHeight <= 0)
                return double.NaN;

            // Convert price to Y pixel (screen Y increases downward, so higher price = lower Y)
            double yRecent = panelHeight * (priceMax - emaRecent) / priceRange;
            double yOld = panelHeight * (priceMax - emaOld) / priceRange;

            double dx = Math.Abs(xRecent - xOld);
            if (dx < 1e-6)
                return double.NaN;

            double dyPixels = Math.Abs(yRecent - yOld);

            double angleRad = Math.Atan2(dyPixels, dx);
            double angleMagnitude = angleRad * (180.0 / Math.PI);

            double priceDelta = emaRecent - emaOld;
            double angleDeg = priceDelta >= 0 ? angleMagnitude : -angleMagnitude;

            if (EnableFastGradDebug && CurrentBar >= FastGradDebugStart && CurrentBar <= FastGradDebugEnd)
            {
                Print($"[ChartScale] bar={CurrentBar} emaOld[2]={emaOld:F2} emaRecent[1]={emaRecent:F2} " +
                      $"priceDelta={priceDelta:F2} dx={dx:F1} dyPx={dyPixels:F1} deg={angleDeg:F1}");
            }

            return angleDeg;
        }

        private void LogOpportunityAnalysis(double prevOpen, double prevClose, bool allowLongThisBar, bool allowShortThisBar, 
            bool trendUp, bool trendDown, bool placedEntry)
        {
            try
            {
                if (opportunityLogWriter == null || CurrentBar < 1)
                    return;

                // Bar context
                string timestamp = Time[1].ToString("yyyy-MM-dd HH:mm:ss.fff");
                int bar = CurrentBar - 1;
                double open = Open[1];
                double high = High[1];
                double low = Low[1];
                double close = Close[1];
                string candleType = GetCandleType(open, close);
                double volume = Volume[1];

                // EMA state
                double emaVal = fastEma != null && CurrentBar >= 1 ? fastEma[1] : double.NaN;
                double gradDeg = lastFastEmaGradDeg;

                // Pattern state
                int goodCount = recentGood.Count >= MinConsecutiveBars ? recentGood.Count(g => g) : 0;
                int badCount = recentGood.Count >= MinConsecutiveBars ? recentGood.Count(g => !g) : 0;
                double netPnl = recentPnl.Count >= MinConsecutiveBars ? recentPnl.Sum() : 0;
                string barPattern = GetBarSequencePattern();

                // Position state
                string currentPos = Position.MarketPosition.ToString();
                int entryBarNum = lastEntryBarIndex;
                double entryPx = Position.MarketPosition != MarketPosition.Flat ? Position.AveragePrice : double.NaN;
                double unrealizedPnl = Position.MarketPosition != MarketPosition.Flat ? Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, close) : 0;

                // Filter results
                bool gradFilterBlocksLong = GradientFilterEnabled && ShouldSkipLongDueToGradient(gradDeg);
                bool gradFilterBlocksShort = GradientFilterEnabled && ShouldSkipShortDueToGradient(gradDeg);

                // Determine action and block reason
                string actionTaken = "";
                string blockReason = "";
                string opportunityType = "";

                if (placedEntry)
                {
                    actionTaken = Position.MarketPosition == MarketPosition.Long ? "ENTERED_LONG" : "ENTERED_SHORT";
                    opportunityType = "TAKEN";
                }
                else if (Position.MarketPosition != MarketPosition.Flat)
                {
                    actionTaken = "ALREADY_IN_POSITION";
                    blockReason = $"Already {Position.MarketPosition}";
                    opportunityType = trendUp || trendDown ? "BLOCKED" : "NONE";
                }
                else if (trendUp)
                {
                    opportunityType = "LONG_SIGNAL";
                    if (!allowLongThisBar)
                    {
                        actionTaken = "SKIPPED_LONG";
                        blockReason = "AvoidLongsOnBadCandle";
                    }
                    else if (gradFilterBlocksLong)
                    {
                        actionTaken = "SKIPPED_LONG";
                        blockReason = $"GradientFilter ({gradDeg:F2}° < {SkipLongsBelowGradient:F2}°)";
                    }
                    else if (pendingLongFromBad)
                    {
                        actionTaken = "DEFERRED_LONG";
                        blockReason = "Waiting for good candle confirmation";
                    }
                    else
                    {
                        actionTaken = "MISSED_LONG";
                        blockReason = "Unknown - should have entered";
                    }
                }
                else if (trendDown)
                {
                    opportunityType = "SHORT_SIGNAL";
                    if (!allowShortThisBar)
                    {
                        actionTaken = "SKIPPED_SHORT";
                        blockReason = "AvoidShortsOnGoodCandle";
                    }
                    else if (gradFilterBlocksShort)
                    {
                        actionTaken = "SKIPPED_SHORT";
                        blockReason = $"GradientFilter ({gradDeg:F2}° > {SkipShortsAboveGradient:F2}°)";
                    }
                    else if (pendingShortFromGood)
                    {
                        actionTaken = "DEFERRED_SHORT";
                        blockReason = "Waiting for bad candle confirmation";
                    }
                    else
                    {
                        actionTaken = "MISSED_SHORT";
                        blockReason = "Unknown - should have entered";
                    }
                }
                else
                {
                    actionTaken = "NO_SIGNAL";
                    opportunityType = "NONE";
                    blockReason = $"Trend requirements not met (G:{goodCount}/B:{badCount}, PnL:{netPnl:F2})";
                }

                // Write CSV line
                string line = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0},{1},{2:F4},{3:F4},{4:F4},{5:F4},{6},{7},{8},{9:F2}," +
                    "{10},{11},{12},{13},{14:F2},{15}," +
                    "{16},{17},{18},{19:F2}," +
                    "{20},{21}," +
                    "{22},{23},{24:F2},{25:F2},{26:F2}," +
                    "{27},{28}," +
                    "{29},{30},{31}",
                    timestamp, bar, open, high, low, close, candleType, volume, 
                    double.IsNaN(emaVal) ? "" : emaVal.ToString("F4"), gradDeg,
                    trendUp, trendDown, goodCount, badCount, netPnl, barPattern,
                    currentPos, entryBarNum, double.IsNaN(entryPx) ? "" : entryPx.ToString("F4"), unrealizedPnl,
                    allowLongThisBar, allowShortThisBar,
                    gradFilterBlocksLong, gradFilterBlocksShort, gradDeg, SkipLongsBelowGradient, SkipShortsAboveGradient,
                    pendingLongFromBad, pendingShortFromGood,
                    actionTaken, blockReason, opportunityType);

                opportunityLogWriter.WriteLine(line);
            }
            catch (Exception ex)
            {
                Print($"[BarsOnTheFlow] Error logging opportunity analysis: {ex.Message}");
            }
        }
    }
}
