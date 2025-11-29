#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using System.IO;
using System.Net.Http;
using System.Globalization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class GradientSlopeStrategy : Strategy
    {
        #region Variables
        
        // EMA indicators
        private EMA fastEMA;
        private EMA slowEMA;
        
        // EMA tracking for gradient calculation
        private double prevFastEMA = 0;
        private double prevSlowEMA = 0;
        
        // Signal tracking
        private string currentSignal = "FLAT";
        private int signalStartBar = -1;
        private int entryBar = -1;
        private int lastTradeBar = -1;  // Guard: track last bar we placed an order
        
        // Internal position tracking (separate from NinjaTrader Position object)
        private string myPosition = "FLAT";  // Track our own position: "FLAT", "LONG", "SHORT"
        
        // CSV Logging
        private StreamWriter csvWriter;
        private string csvFilePath;
        private bool csvHeaderWritten = false;

        // Trades-only Logging (entries/exits only)
        private StreamWriter tradesWriter;
        private string tradesFilePath;
        private bool tradesHeaderWritten = false;

        // Trades summary logging (entry->exit outcome)
        private StreamWriter tradesSummaryWriter;
        private string tradesSummaryFilePath;
        private bool tradesSummaryHeaderWritten = false;
        
        // Log file output
        private StreamWriter logWriter;
        private string logFilePath;
        
        // Weak reversal tracking
        private bool inWeakReversalDelay = false;
        private int weakReversalDelayBars = 0;
        
        // Exit cooldown tracking - prevent consecutive trades
        private DateTime lastExitTime = DateTime.MinValue;
        private int exitCooldownSeconds = 30;  // Wait 30 seconds after exit before allowing new entry
        private bool inExitCooldown = false;  // Flag to prevent signal updates during cooldown
        
        // Min-hold to avoid same-bar churn
        private int minHoldBars = 2; // Minimum bars to hold a position before allowing exits (0 = allow same-bar exits)

        // Exit confirmation (two-step) state
        private bool exitPending = false;            // true when an exit is staged awaiting confirmation
        private string exitPendingSide = "";        // "LONG" or "SHORT"
        private int exitPendingStartBar = -1;        // bar index when pending was set
        private double exitPendingAnchorFastEMA = 0; // fast EMA value when pending started
        private string exitPendingReason = "";      // reason that triggered pending
        private double exitConfirmFastEMADelta = 0.5; // required EMA move (points) to confirm exit

        // Per-trade tracking for analysis
        private double entryPrice = 0.0;
        private DateTime entryTime = DateTime.MinValue;
        private double tradeMFE = 0.0; // favorable move in points (>=0)
        private double tradeMAE = 0.0; // adverse move in points (<=0)
        
        // Heartbeat tracking
        private DateTime lastHeartbeatTime = DateTime.MinValue;
        private int heartbeatIntervalSeconds = 1;

        // Visualization (disabled by default to keep things simple)
        private bool showChartAnnotations = false;   // Draw entry/exit markers
        private bool showHud = true;                 // Show status HUD (default ON)
        private TextPosition hudPosition = TextPosition.BottomLeft; // Default away from chart header
        private SimpleFont hudSimpleFont = null;     // HUD font (created at DataLoaded)
        private int cooldownStartBar = -1;          // Track bar when cooldown started
        private bool autoAddGradientPanel = true;   // Auto-add EMAGradientPair panel
        private bool gradientPanelAdded = false;    // Ensure we add only once
        private int hudFontSize = 10;               // HUD font size
            private int hudVerticalOffset = 1;          // Number of blank lines to insert before HUD (TopLeft only)
        private bool savePropertiesOnStart = true;  // Dump all public properties to file at startup
        private bool dumpPropertiesNow = false;     // Manual trigger to dump properties mid-run
        // Diagnosis overlay UI (interactive buttons instead of properties)
        private System.Windows.Controls.Border diagPanel;      // container border
        private System.Windows.Controls.StackPanel diagStack;  // vertical layout
        private System.Windows.Controls.TextBlock diagText;    // shows results
        private System.Windows.Controls.Button btnDiagCurrent; // diagnose CurrentBar
        private System.Windows.Controls.Button btnDiagCursor;  // diagnose last hovered bar
        private System.Windows.Controls.Button btnApplySave;   // apply suggestions and save template
        private System.Windows.Controls.Button btnDiagToggle;  // collapse/expand panel
        private System.Windows.Controls.Button btnCopyClipboard; // copy diagnosis text to clipboard
        private System.Windows.Controls.Button btnSuggestTarget; // cycle suggestion target (Auto/Long/Short/Exit)
        private System.Windows.Controls.Button btnSaveProps;    // save properties dump
        private System.Windows.Controls.Button btnLoadProps;    // load properties dump
        private int lastHoverBar = -1;                          // updated via mouse move handler
        private bool hoverDebug = false;                        // optional debug prints for hover mapping
        private System.Windows.Controls.TextBlock hoverInfoText; // live hover bar index
        private bool diagCollapsed = false;                     // collapse state
        private int lockedDiagBar = -1;                          // left-click locked bar for diagnosis
        private double lastHoverX = double.NaN;                  // last X used to update hover (stabilization)
        private string suggestionTarget = "AUTO";               // Suggestion target: AUTO|LONG|SHORT|EXIT
        // Manual forced eligibility (user overrides via diagnosis panel)
        private System.Collections.Generic.HashSet<int> forcedLongEligibility = new System.Collections.Generic.HashSet<int>();
        private System.Collections.Generic.HashSet<int> forcedShortEligibility = new System.Collections.Generic.HashSet<int>();
        private System.Windows.Controls.Button btnForceEligible; // Toggle forced eligibility for hovered/locked bar
        
        // Parameters
        private int quantity = 1;
        private int fastEMAPeriod = 10;
        private int slowEMAPeriod = 20;
        private int entryBarDelay = 1;
        private int initialBarsWait = 5;  // Bars to wait after strategy starts before allowing any trades
        private bool enableLogging = true;
        private double weakGradientThreshold = 0.5;  // Threshold for "weak" gradient
        private int weakReversalDelayPeriod = 3;  // Bars to wait after weak reversal
        private double minEntryFastGradientAbs = 0.50; // Minimum absolute fast gradient required for entry only (tunable)
        private double validationMinFastGradientAbs = 0.15; // Minimum absolute fast gradient required to stay in position
        private double maxEntryFastGradientAbs = 0.60; // Upper cap: disallow entries when fast gradient magnitude is too large (overextension)

        // --- Adaptive Entry Gradient Thresholds ---
        private bool enableAdaptiveEntryGradient = true;      // adapt entry threshold based on context
        private double adaptiveNearZeroMultiplier = 0.85;       // scale down near recent zero-cross
        private double adaptiveDeepLegMultiplier = 0.75;        // scale down after deep opposite leg
        private double adaptiveDeepLegPoints = 6.0;             // points depth threshold for deep leg
        private int adaptiveLookbackBars = 20;                  // bars to scan for context
        private double adaptiveMinFloor = 0.30;                 // never drop below this absolute floor
        
        // --- Indicator instrumentation (Phase 1) ---
        private ATR atr;
        private ADX adx;
        private RSI rsi;
        private MACD macd;
        private double prevFastGradient = 0.0;
        private int gradientStabilityPeriod = 10;
        private Queue<double> fastGradientHistory = new Queue<double>();
        private double currentGradientStability = 0.0; // rolling std dev of fast gradient
        private double currentFastGradientAcceleration = 0.0; // fastGradient - prevFastGradient

        // --- Entry filter thresholds (Phase 2) ---
        private double minAdxForEntry = 18.0;              // Trend strength filter (default tightened)
        private double maxGradientStabilityForEntry = 1.46; // Rolling std dev cap (default tightened)
        private double minBandwidthForEntry = 0.000;        // EMA separation lower bound (broad)
        private double maxBandwidthForEntry = 0.100;        // EMA separation upper bound (broad)
        private bool requireAccelAlignment = true;          // Prefer reinforcing acceleration
        private bool disableEntryFilters = false;           // When true, bypass indicator-based filters (for data gathering)
        // Optional caps (disabled by default)
        private bool enableATRCap = true;
        private double maxATRForEntry = 13.57;
        private bool enableRSIFloor = true;
        private double minRSIForEntry = 50.0;

        // --- Runtime Overrides (fetched from dashboard) ---
        private DateTime lastOverridesFetch = DateTime.MinValue;
        private int overridesFetchIntervalSeconds = 10; // poll every 10s
        private static HttpClient overridesHttpClient = new HttpClient();
        private bool logOverridesApplied = true; // toggle logging for applied overrides

        private void PollOverridesIfNeeded()
        {
            if ((DateTime.UtcNow - lastOverridesFetch).TotalSeconds < overridesFetchIntervalSeconds)
                return;
            lastOverridesFetch = DateTime.UtcNow;
            string url = "http://127.0.0.1:5001/overrides";
            try
            {
                var json = overridesHttpClient.GetStringAsync(url).Result;
                bool foundMinAdx = ApplyOverride(json, "MinAdxForEntry", ref minAdxForEntry);
                bool foundMaxStab = ApplyOverride(json, "MaxGradientStabilityForEntry", ref maxGradientStabilityForEntry);
                bool foundMinFastGrad = ApplyOverride(json, "MinEntryFastGradientAbs", ref minEntryFastGradientAbs);
                bool foundMaxBandwidth = ApplyOverride(json, "MaxBandwidthForEntry", ref maxBandwidthForEntry);
                // Adaptive overrides
                bool foundAdaptiveFloor = ApplyOverride(json, "AdaptiveMinFloor", ref adaptiveMinFloor);
                bool foundAdaptiveNearZero = ApplyOverride(json, "AdaptiveNearZeroMultiplier", ref adaptiveNearZeroMultiplier);

                // If an override was previously applied but now missing -> revert to defaults
                RevertIfMissing(foundMinAdx, 18.0, ref minAdxForEntry, "MinAdxForEntry");
                RevertIfMissing(foundMaxStab, 1.46, ref maxGradientStabilityForEntry, "MaxGradientStabilityForEntry");
                RevertIfMissing(foundMinFastGrad, 0.50, ref minEntryFastGradientAbs, "MinEntryFastGradientAbs");
                RevertIfMissing(foundMaxBandwidth, 0.100, ref maxBandwidthForEntry, "MaxBandwidthForEntry");
                RevertIfMissing(foundAdaptiveFloor, 0.30, ref adaptiveMinFloor, "AdaptiveMinFloor");
                RevertIfMissing(foundAdaptiveNearZero, 0.85, ref adaptiveNearZeroMultiplier, "AdaptiveNearZeroMultiplier");
            }
            catch (Exception ex)
            {
                if (enableLogging)
                    Print("[OVERRIDES] Fetch failed: " + ex.Message);
            }
        }

        private bool ApplyOverride(string json, string key, ref double field)
        {
            try
            {
                int idx = json.IndexOf("\"" + key + "\"");
                if (idx < 0) return false;
                int colon = json.IndexOf(':', idx);
                if (colon < 0) return false;
                int start = colon + 1;
                while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
                int end = start;
                while (end < json.Length && "0123456789+-.eE".IndexOf(json[end]) >= 0) end++;
                string numStr = json.Substring(start, end - start).Trim();
                if (string.IsNullOrEmpty(numStr)) return true; // key present but empty
                double val;
                if (double.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out val))
                {
                    if (Math.Abs(field - val) > 0.00001)
                    {
                        double old = field;
                        field = val;
                        if (logOverridesApplied)
                            LogOutput($"OVERRIDE_APPLIED {key} {old:F4}->{field:F4}");
                    }
                }
                return true;
            }
            catch { return false; }
        }

        private void RevertIfMissing(bool found, double defaultVal, ref double field, string key)
        {
            if (!found && Math.Abs(field - defaultVal) > 0.00001)
            {
                double old = field;
                field = defaultVal;
                if (logOverridesApplied)
                    LogOutput($"OVERRIDE_REMOVED {key} {old:F4}->DEFAULT({field:F4})");
            }
        }

        private string BuildIndicatorSnapshot(double fastEMAValue, double slowEMAValue, double fastGradient)
        {
            double bandwidth = Math.Abs(fastEMAValue - slowEMAValue) / (slowEMAValue != 0 ? slowEMAValue : 1.0);
            double macdHist = macd != null ? (macd.Default[0] - macd.Avg[0]) : 0.0;
            double accel = currentFastGradientAcceleration;
            // Volume per minute (approx) using time delta between current and previous bar
            double vpm = 0.0;
            double vol = Volume[0];
            if (CurrentBar >= 1)
            {
                double minutes = (Time[0] - Time[1]).TotalMinutes;
                if (minutes <= 0) minutes = 1.0;
                vpm = vol / minutes;
            }
            else
            {
                vpm = vol; // first bar fallback
            }
            return $"ATR={ (atr!=null?atr[0]:0):F2}|ADX={ (adx!=null?adx[0]:0):F2}|RSI={ (rsi!=null?rsi[0]:0):F1}|MACDH={macdHist:F3}|BW={bandwidth:F4}|Accel={accel:F4}|GradStab={currentGradientStability:F4}|VOL={vol:F0}|VPM={vpm:F2}";
        }
        
        // Debug options
        private bool showBarIndexLabels = true;  // Show bar index labels on chart for debugging
        
        // Exit conditions
        private bool exitOnFastEMAGradient = true;  // Exit when fast EMA gradient reverses
        private bool exitOnSlowEMAGradient = false;  // Exit when slow EMA gradient reverses
        private bool exitOnCloseBelowFastEMA = true;  // Exit when close below fast EMA
        private bool exitOnCloseBelowSlowEMA = false;  // Exit when close below slow EMA (BOTH EMAs required)
        
        // Fast EMA exit guards
        private double fastEMAGradientExitThreshold = -0.25;  // LONG exit: gradient must be <= this value (default: -0.25)
        private double fastEMAGradientExitThresholdShort = 0.25;  // SHORT exit: gradient must be >= this value (default: 0.25)
        
        // Strict dual EMA exit guard: when enabled, LONG exits only allowed if the bar OPEN is below BOTH EMAs;
        // SHORT exits only allowed if the bar CLOSE is above BOTH EMAs. Prevents premature exits on minor noise.
        private bool enableDualEMAStrictExitGuard = true;

        // Sign-flip exit simplification: when enabled, ignore magnitude thresholds (-0.25 / 0.25) and
        // allow exit when fast EMA gradient crosses near zero (with tolerance) plus dual EMA guard.
        private bool useSignFlipExit = true;
        private double signFlipTolerance = 0.02; // Accept gradient within +/- tolerance of zero as flip

        // Streaming diagnostics (dashboard auto-feed)
        private bool streamBarDiagnostics = true; // default ON: send compact diag each bar

        // --- Early Reversal Entry (optional) ---
        private bool enableReversalEarlyEntry = true; // master toggle
        private int reversalLookbackBars = 12;          // bars to scan back for bear/bull context
        private int minBearBarsForReversal = 3;         // consecutive bear bars required before bullish reversal entry
        private int minBullBarsForReversal = 3;         // consecutive bull bars required before bearish reversal entry (future use)
        private double minReversalDepthPoints = 8.0;    // minimum depth below fast EMA (or above for bearish) during prior trend
        private double minAccelForReversal = 0.30;      // minimum acceleration on zero-cross bar
        private double maxReversalStability = 2.20;     // allow slightly noisy turns but cap instability
        private double minReversalAdx = 12.0;           // ADX floor for reversal early entry (can be lower than normal entry)
        private int reversalHoldGuardBars = 2;          // minimum bars to hold before validation exit check
        private double reversalStopBuffer = 6.0;        // not placed (logging only) distance beyond trough for hypothetical protective stop
        
        #endregion
        
        #region Fast EMA Exit Logic
        
        /// <summary>
        /// Check if LONG position should exit based on Fast EMA conditions
        /// Core rule: Exit when fast EMA gradient is negative AND below threshold AND price is below fast EMA
        /// </summary>
        private bool ShouldExitLongOnFastEMA(double fastGradient, double currentClose, double fastEMAValue, out string exitReason)
        {
            exitReason = "";
            double slowEMACurrent = slowEMA[0];
            // Strict guard: only allow evaluating exit if current bar OPEN below both EMAs
            if (enableDualEMAStrictExitGuard)
            {
                bool guardSatisfied = Open[0] < fastEMAValue && Open[0] < slowEMACurrent;
                if (!guardSatisfied)
                {
                    return false; // Guard blocks any LONG exit consideration
                }
            }
            // Sign-flip mode: exit when gradient crosses near/below zero (tolerance) AND price context satisfied
            if (useSignFlipExit)
            {
                bool gradientFlip = fastGradient <= signFlipTolerance; // Allow slight positive up to tolerance
                bool closeBelowFastEMA = currentClose < fastEMAValue;
                if (gradientFlip && closeBelowFastEMA)
                {
                    exitReason = $"SignFlipFastGrad={fastGradient:F4}<=Tol({signFlipTolerance:F4}),Close<FastEMA";
                    return true;
                }
                return false;
            }

            // Core Fast EMA exit rule for LONG (magnitude-based):
            bool gradientNegative = fastGradient < 0;
            bool gradientBelowThreshold = fastGradient <= fastEMAGradientExitThreshold;
            bool closeBelowFastEMAStd = currentClose < fastEMAValue;
            bool shouldExitStd = gradientNegative && gradientBelowThreshold && closeBelowFastEMAStd;
            if (shouldExitStd)
            {
                exitReason = $"FastEMAGrad={fastGradient:F4}(<={fastEMAGradientExitThreshold}),Close<FastEMA";
            }
            return shouldExitStd;
        }
        
        /// <summary>
        /// Check if SHORT position should exit based on Fast EMA conditions
        /// Core rule: Exit when fast EMA gradient is positive AND above threshold AND price is above fast EMA
        /// </summary>
        private bool ShouldExitShortOnFastEMA(double fastGradient, double currentClose, double fastEMAValue, out string exitReason)
        {
            exitReason = "";
            double slowEMACurrent = slowEMA[0];
            // Strict guard: only allow evaluating exit if current bar CLOSE above both EMAs
            if (enableDualEMAStrictExitGuard)
            {
                bool guardSatisfied = Close[0] > fastEMAValue && Close[0] > slowEMACurrent;
                if (!guardSatisfied)
                {
                    return false; // Guard blocks any SHORT exit consideration
                }
            }
            // Sign-flip mode: exit when gradient crosses near/above zero (tolerance) AND price context satisfied
            if (useSignFlipExit)
            {
                bool gradientFlip = fastGradient >= -signFlipTolerance; // Allow slight negative up to -tolerance
                bool closeAboveFastEMA = currentClose > fastEMAValue;
                if (gradientFlip && closeAboveFastEMA)
                {
                    exitReason = $"SignFlipFastGrad={fastGradient:F4}>=-Tol({signFlipTolerance:F4}),Close>FastEMA";
                    return true;
                }
                return false;
            }

            // Core Fast EMA exit rule for SHORT (magnitude-based):
            bool gradientPositiveStd = fastGradient > 0;
            bool gradientAboveThresholdStd = fastGradient >= fastEMAGradientExitThresholdShort;
            bool closeAboveFastEMAStd = currentClose > fastEMAValue;
            bool shouldExitStd = gradientPositiveStd && gradientAboveThresholdStd && closeAboveFastEMAStd;
            if (shouldExitStd)
            {
                exitReason = $"FastEMAGrad={fastGradient:F4}(>={fastEMAGradientExitThresholdShort}),Close>FastEMA";
            }
            return shouldExitStd;
        }
        
        #endregion
        
        #region OnStateChange
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Gradient Slope Strategy - Enters when both EMAs trend in same direction with price positioned correctly. Entry bar delay configurable (1=immediate, 2=second bar, 3=third bar, etc).";
                Name = "GradientSlopeStrategy";
                Calculate = Calculate.OnPriceChange;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                // Use infinite lookback so diagnosis can access full historical bar/indicator values
                MaximumBarsLookBack = MaximumBarsLookBack.Infinite;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                IsInstantiatedOnEachOptimizationIteration = true;
                
                // Add plots for visualization
                AddPlot(Brushes.Transparent, "Signal");
                AddPlot(Brushes.DodgerBlue, "FastEMAPlot");
                AddPlot(Brushes.OrangeRed, "SlowEMAPlot");
                
                // Clear output tab on startup
                ClearOutputWindow();
            }
            else if (State == State.Configure)
            {
                // Initialize EMAs
                fastEMA = EMA(Close, fastEMAPeriod);
                slowEMA = EMA(Close, slowEMAPeriod);
            }
            else if (State == State.DataLoaded)
            {
                // Initialize tracking variables
                prevFastEMA = 0;
                prevSlowEMA = 0;
                currentSignal = "FLAT";
                signalStartBar = -1;
                entryBar = -1;
                myPosition = "FLAT";

                // Initialize Phase 1 additional indicators (only once)
                atr = ATR(14);
                adx = ADX(14);
                rsi = RSI(14, 3);
                macd = MACD(12, 26, 9);
                Print("[GRAD] Indicators initialized -> ATR(14), ADX(14), RSI(14,3), MACD(12,26,9)");
                // Auto add gradient panel for visualization
                if (autoAddGradientPanel && !gradientPanelAdded)
                {
                    try
                    {
                        // Use NinjaTrader factory to ensure proper Input binding and caching
                        var gp = EMAGradientPair(fastEMAPeriod, slowEMAPeriod);
                        AddChartIndicator(gp);
                        gradientPanelAdded = true;
                        Print("[GRAD] EMAGradientPair panel added via factory method.");
                    }
                    catch (Exception ex)
                    {
                        Print("[GRAD] Failed to add EMAGradientPair panel: " + ex.Message);
                    }
                }
                
                // Initialize CSV logging
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                    "NinjaTrader 8", "bin", "Custom", "strategy_logs");
                Directory.CreateDirectory(logDir);
                csvFilePath = Path.Combine(logDir, $"GradientSlope_{Instrument.FullName}_{timestamp}.csv");
                csvWriter = new StreamWriter(csvFilePath, false);
                csvHeaderWritten = false;
                Print(string.Format("[GRAD] CSV Log: {0}", csvFilePath));
                
                // Initialize dedicated log output file
                logFilePath = Path.Combine(logDir, $"GradientSlope_{Instrument.FullName}_{timestamp}.log");
                logWriter = new StreamWriter(logFilePath, false);
                logWriter.AutoFlush = true;  // Auto-flush for real-time viewing
                LogOutput("=" .PadRight(80, '='));
                LogOutput($"GradientSlope Strategy Log Started: {DateTime.Now}");
                LogOutput($"Instrument: {Instrument.FullName}");
                LogOutput($"Fast EMA: {fastEMAPeriod}, Slow EMA: {slowEMAPeriod}, Entry Delay: {entryBarDelay}");
                LogOutput($"Weak Gradient Threshold: {weakGradientThreshold}, Delay Period: {weakReversalDelayPeriod}");
                LogOutput($"Defaults -> MinEntryFastGrad:{minEntryFastGradientAbs:F2} ValidationMinFastGrad:{validationMinFastGradientAbs:F2} MaxEntryFastGrad:{maxEntryFastGradientAbs:F2}");
                LogOutput($"AdaptiveEntry -> Enabled:{enableAdaptiveEntryGradient} NearZeroMult:{adaptiveNearZeroMultiplier:F2} DeepLegMult:{adaptiveDeepLegMultiplier:F2} DeepLegPts>={adaptiveDeepLegPoints:F1} Lookback:{adaptiveLookbackBars} Floor:{adaptiveMinFloor:F2}");
                LogOutput($"Filters -> MinADX:{minAdxForEntry:F0} MaxGradStab:{maxGradientStabilityForEntry:F2} BW:[{minBandwidthForEntry:F3},{maxBandwidthForEntry:F3}] AccelAlign:{requireAccelAlignment}");
                LogOutput($"Optional -> ATRCap:{(enableATRCap ? ("<= " + maxATRForEntry.ToString("F2")) : "Off")} RSIFloor:{(enableRSIFloor ? (">= " + minRSIForEntry.ToString("F1")) : "Off")} DisableEntryFilters:{disableEntryFilters}");
                LogOutput($"Debug -> ShowBarIndexLabels:{showBarIndexLabels} ShowHud:{showHud} HudPos:{hudPosition} HudFont:{hudFontSize} AutoAddGradientPanel:{autoAddGradientPanel}");
                LogOutput($"ExitGuard -> DualEMA:{enableDualEMAStrictExitGuard}");
                LogOutput($"ExitMode -> SignFlip:{useSignFlipExit} Tol:{signFlipTolerance:F4}");
                LogOutput($"Reversal -> Enable:{enableReversalEarlyEntry} Lookback:{reversalLookbackBars} MinBearBars:{minBearBarsForReversal} Depth>={minReversalDepthPoints:F1} Accel>={minAccelForReversal:F2} MaxStab<={maxReversalStability:F2} MinADX>={minReversalAdx:F1} HoldGuard:{reversalHoldGuardBars} StopBuf:{reversalStopBuffer:F1}");
                LogOutput("=".PadRight(80, '='));
                Print(string.Format("[GRAD] Log File: {0}", logFilePath));

                // Optional property dump for auditing settings
                if (savePropertiesOnStart)
                {
                    SaveStrategyProperties();
                }

                // Initialize trades-only CSV logging
                tradesFilePath = Path.Combine(logDir, $"GradientSlope_TRADES_{Instrument.FullName}_{timestamp}.csv");
                tradesWriter = new StreamWriter(tradesFilePath, false);
                tradesHeaderWritten = false;
                Print(string.Format("[GRAD] Trades Log: {0}", tradesFilePath));

                // Initialize trades summary CSV
                tradesSummaryFilePath = Path.Combine(logDir, $"GradientSlope_TRADES_SUMMARY_{Instrument.FullName}_{timestamp}.csv");
                tradesSummaryWriter = new StreamWriter(tradesSummaryFilePath, false);
                tradesSummaryHeaderWritten = false;
                Print(string.Format("[GRAD] Trades Summary Log: {0}", tradesSummaryFilePath));
                // Create interactive diagnosis UI
                TryCreateDiagnosisUI();
            }
            else if (State == State.Terminated)
            {
                // Close CSV file
                if (csvWriter != null)
                {
                    csvWriter.Flush();
                    csvWriter.Close();
                    csvWriter = null;
                }
                
                // Close log file
                if (logWriter != null)
                {
                    LogOutput("=".PadRight(80, '='));
                    LogOutput($"GradientSlope Strategy Log Ended: {DateTime.Now}");
                    LogOutput("=".PadRight(80, '='));
                    logWriter.Flush();
                    logWriter.Close();
                    logWriter = null;
                }

                // Close trades-only CSV
                if (tradesWriter != null)
                {
                    tradesWriter.Flush();
                    tradesWriter.Close();
                    tradesWriter = null;
                }
                if (tradesSummaryWriter != null)
                {
                    tradesSummaryWriter.Flush();
                    tradesSummaryWriter.Close();
                    tradesSummaryWriter = null;
                }
                // Remove diagnosis UI if present
                TryRemoveDiagnosisUI();
            }
        }
        
        #endregion
        
        #region OnBarUpdate
        
        protected override void OnBarUpdate()
        {
            // Manual property dump trigger (auto-resets)
            if (dumpPropertiesNow)
            {
                SaveStrategyProperties();
                dumpPropertiesNow = false;
                LogOutput("MANUAL_PROPERTIES_DUMPED");
            }
            // Diagnosis now runs only via chart buttons (no property trigger)
            // Ensure we have enough bars
            if (CurrentBar < BarsRequiredToTrade)
                return;
            
            // Wait for initial bars before allowing any trades
            if (CurrentBar < BarsRequiredToTrade + initialBarsWait)
            {
                if (enableLogging && IsFirstTickOfBar)
                {
                    LogOutput($"WAITING FOR INITIAL BARS -> CurrentBar:{CurrentBar} RequiredBars:{BarsRequiredToTrade} WaitBars:{initialBarsWait} TotalNeeded:{BarsRequiredToTrade + initialBarsWait}");
                }
                return;
            }
            
            if (fastEMA == null || slowEMA == null)
                return;

            // Periodic poll for dashboard overrides (lightweight, throttled)
            PollOverridesIfNeeded();
            
            // CHECK EXIT COOLDOWN - stay flat for 30 seconds after any exit
            if (inExitCooldown && lastExitTime != DateTime.MinValue)
            {
                double secondsSinceExit = (Time[0] - lastExitTime).TotalSeconds;
                if (secondsSinceExit >= exitCooldownSeconds)
                {
                    // Cooldown expired
                    inExitCooldown = false;
                    Print($"[GRAD] {Time[0]} | EXIT COOLDOWN EXPIRED - Ready to trade again");
                }
                else
                {
                    // Still in cooldown - stay flat
                    currentSignal = "FLAT";
                    signalStartBar = -1;
                    if (showChartAnnotations)
                    {
                        // Color bars during cooldown for clarity
                        try { BarBrushes[0] = Brushes.DimGray; CandleOutlineBrushes[0] = Brushes.Gray; } catch {}
                    }
                    
                    if (showHud)
                    {
                        var secsLeft = Math.Max(0, exitCooldownSeconds - (int)((Time[0] - lastExitTime).TotalSeconds));
                        string cooldownPrefix = (hudPosition == TextPosition.TopLeft ? HudOffsetPrefix() : string.Empty);
                        Draw.TextFixed(this, "HUD_Cooldown", cooldownPrefix + $"COOLDOWN: {secsLeft}s left", hudPosition);
                    }
                    return;
                }
            }

            // PERFORMANCE SAFETY: Heavy logic & logging only on first tick of bar
            // Intrabar (non-first ticks): perform ONLY immediate exit checks (no logging) then return.
            // This reduces disk I/O and CPU when Calculate.OnPriceChange produces many updates.
            if (!IsFirstTickOfBar)
            {
                // Suppress intrabar exits during minimum hold period to avoid same-bar churn
                if (myPosition != "FLAT" && entryBar >= 0)
                {
                    int barsSinceEntryIntrabar = CurrentBar - entryBar;
                    if (barsSinceEntryIntrabar < minHoldBars)
                        return;
                }

                // Quick intrabar exit scan
                double fastEMAValueIntrabar = fastEMA[0];
                double fastGradientIntrabar = 0;
                if (CurrentBar >= 1)
                    fastGradientIntrabar = fastEMAValueIntrabar - fastEMA[1];

                // Intrabar: stage exits only (no actual exit). We confirm on next bar.
                if (myPosition == "LONG")
                {
                    string reason;
                    if (ShouldExitLongOnFastEMA(fastGradientIntrabar, Close[0], fastEMAValueIntrabar, out reason))
                    {
                        if (!exitPending || exitPendingSide != "LONG")
                        {
                            exitPending = true;
                            exitPendingSide = "LONG";
                            exitPendingStartBar = CurrentBar;
                            exitPendingAnchorFastEMA = fastEMAValueIntrabar;
                            exitPendingReason = reason;
                        }
                        return; // wait for first tick of next bar to confirm
                    }
                    else if (exitPending && exitPendingSide == "LONG")
                    {
                        // Cancel pending if condition relaxed intrabar
                        exitPending = false;
                        exitPendingSide = "";
                        exitPendingStartBar = -1;
                        exitPendingAnchorFastEMA = 0;
                        exitPendingReason = "";
                    }
                }
                else if (myPosition == "SHORT")
                {
                    string reason;
                    if (ShouldExitShortOnFastEMA(fastGradientIntrabar, Close[0], fastEMAValueIntrabar, out reason))
                    {
                        if (!exitPending || exitPendingSide != "SHORT")
                        {
                            exitPending = true;
                            exitPendingSide = "SHORT";
                            exitPendingStartBar = CurrentBar;
                            exitPendingAnchorFastEMA = fastEMAValueIntrabar;
                            exitPendingReason = reason;
                        }
                        return; // wait for first tick of next bar to confirm
                    }
                    else if (exitPending && exitPendingSide == "SHORT")
                    {
                        // Cancel pending if condition relaxed intrabar
                        exitPending = false;
                        exitPendingSide = "";
                        exitPendingStartBar = -1;
                        exitPendingAnchorFastEMA = 0;
                        exitPendingReason = "";
                    }
                }
                // Skip remainder of processing until first tick of next bar
                return;
            }
            
            // Get current EMA values
            double fastEMAValue = fastEMA[0];
            double slowEMAValue = slowEMA[0];
            double currentClose = Close[0];

            // CORE DEBUG SNAPSHOT AT TOP OF BAR
            if (enableLogging && IsFirstTickOfBar)
            {
                LogOutput($"BAR SNAPSHOT -> Time:{Time[0]} Bar:{CurrentBar} Close:{currentClose:F2} FastEMA:{fastEMAValue:F2} SlowEMA:{slowEMAValue:F2} MyPos:{myPosition} NTPos:{Position.MarketPosition} CurrSignal:{currentSignal} InWeakDelay:{inWeakReversalDelay} LastTradeBar:{lastTradeBar}");
            }

            // Keep internal myPosition aligned with actual NinjaTrader position
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (myPosition != "FLAT") myPosition = "FLAT";
            }
            else if (Position.MarketPosition == MarketPosition.Long)
            {
                if (myPosition != "LONG") myPosition = "LONG";
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (myPosition != "SHORT") myPosition = "SHORT";
            }

            // Guard: ensure only one trade (entry or exit) per bar
            bool tradeAlreadyPlacedThisBar = (CurrentBar == lastTradeBar);
            
            // Calculate gradients using current bar EMA values for real-time response
            // Use EMA[0] (current bar) and EMA[1] (previous bar) for gradient
            double fastGradient = 0;
            double slowGradient = 0;
            
            if (CurrentBar >= 1)
            {
                fastGradient = fastEMAValue - fastEMA[1];
                slowGradient = slowEMAValue - slowEMA[1];
            }

            // --- Gradient instrumentation (acceleration & stability) ---
            currentFastGradientAcceleration = fastGradient - prevFastGradient;
            fastGradientHistory.Enqueue(fastGradient);
            while (fastGradientHistory.Count > gradientStabilityPeriod)
                fastGradientHistory.Dequeue();
            // Compute rolling std dev
            if (fastGradientHistory.Count >= 2)
            {
                double mean = 0.0; foreach (var g in fastGradientHistory) mean += g; mean /= fastGradientHistory.Count;
                double var = 0.0; foreach (var g in fastGradientHistory) { double d = g - mean; var += d*d; }
                var /= fastGradientHistory.Count; // population variance
                currentGradientStability = Math.Sqrt(var);
            }
            else
            {
                currentGradientStability = 0.0;
            }
            prevFastGradient = fastGradient;

            if (enableLogging && IsFirstTickOfBar)
            {
                LogOutput($"GRADIENTS -> FastGrad:{fastGradient:+0.000;-0.000} SlowGrad:{slowGradient:+0.000;-0.000} FastEMA[0]:{fastEMAValue:F4} FastEMA[1]:{fastEMA[1]:F4}");
            }
            
            // Determine current signal
            string newSignal = "FLAT";
            
            // LONG Signal: Both gradients positive AND price above both EMAs
            if (fastGradient > 0 && slowGradient > 0 && 
                currentClose > fastEMAValue && currentClose > slowEMAValue)
            {
                newSignal = "LONG";
            }
            // SHORT Signal: Both gradients negative AND price below both EMAs
            else if (fastGradient < 0 && slowGradient < 0 && 
                     currentClose < fastEMAValue && currentClose < slowEMAValue)
            {
                newSignal = "SHORT";
            }
            
            // Log signal details once per bar (first update only) to avoid spam on OnPriceChange
            if ((enableLogging || CurrentBar < BarsRequiredToTrade + 5) && IsFirstTickOfBar)
            {
                Print(string.Format("[GRAD] {0} | Bar {1} | Close: {2:F2} | FastEMA: {3:F2} (grad: {4:+0.000;-0.000}) | SlowEMA: {5:F2} (grad: {6:+0.000;-0.000}) | Signal: {7} | Prev: {8}",
                    Time[0], CurrentBar, currentClose, fastEMAValue, fastGradient, slowEMAValue, slowGradient, newSignal, currentSignal));
            }
            
            // ============================================================================
            // EXIT LOGIC ORDER (as executed):
            // 1. IMMEDIATE EXITS (below) - Fast EMA gradient threshold + price position
            // 2. SIGNAL CHANGE EXITS (later) - When signal changes from LONG/SHORT to different state
            // ============================================================================
            // IMMEDIATE EXIT CONDITIONS - Check BEFORE signal logic
            // Core rule (always enforced):
            //  LONG  -> exit when fastGradient <= -0.25 AND Close < fastEMA
            //  SHORT -> exit when fastGradient >= +0.25 AND Close > fastEMA
            // ============================================================================
            
            // Exit LONG position when fast EMA conditions are met
            if (myPosition == "LONG" && !tradeAlreadyPlacedThisBar)
            {
                // Enforce minimum hold before evaluating exits
                if (entryBar >= 0)
                {
                    int barsSinceEntry = CurrentBar - entryBar;
                    if (barsSinceEntry < minHoldBars)
                    {
                        LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                            currentSignal, currentSignal, "LONG", "EXIT_SUPPRESSED", $"MIN_HOLD:{barsSinceEntry}/{minHoldBars}");
                        // Skip exit logic for this bar
                        goto AfterLongExitChecks;
                    }
                }

                // --- VALIDATION CHECK: Exit if entry conditions no longer valid ---
                bool validationFailed = false;
                string validationReason = "";
                
                // Check fast gradient threshold (validation uses its own threshold)
                if (fastGradient <= validationMinFastGradientAbs)
                {
                    validationFailed = true;
                    validationReason = $"FastGrad<={fastGradient:F4}(need>{validationMinFastGradientAbs:F2})";
                }
                // Check both gradients positive
                else if (fastGradient <= 0 || slowGradient <= 0)
                {
                    validationFailed = true;
                    validationReason = $"GradientsNotPositive(F:{fastGradient:F4},S:{slowGradient:F4})";
                }
                // Check price above both EMAs
                else if (currentClose <= fastEMAValue || currentClose <= slowEMAValue)
                {
                    validationFailed = true;
                    validationReason = $"PriceBelowEMAs(Close:{currentClose:F2},F:{fastEMAValue:F2},S:{slowEMAValue:F2})";
                }
                
                if (validationFailed)
                {
                    LogOutput($"EXIT LONG (VALIDATION FAILED IN POSITION) -> {validationReason}");
                    LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                        currentSignal, newSignal, "LONG", "EXIT", $"VALIDATION_FAILED: {validationReason}");
                    
                    ExitLong("EnterLong");
                    // Trades-only log
                    string exitSnapshot_longVal = BuildIndicatorSnapshot(fastEMAValue, slowEMAValue, fastGradient);
                    LogTrade(Time[0], CurrentBar, "EXIT", "LONG", currentClose, quantity, $"VALIDATION_FAILED:{validationReason}|{exitSnapshot_longVal}");
                    LogTradeSummary(Time[0], CurrentBar, "LONG", currentClose, $"VALIDATION_FAILED:{validationReason}", 0.0);
                    lastTradeBar = CurrentBar;
                    lastExitTime = Time[0];
                    inExitCooldown = true;
                    cooldownStartBar = CurrentBar;
                    myPosition = "FLAT";
                    currentSignal = "FLAT";
                    signalStartBar = -1;
                    entryBar = -1; entryPrice = 0.0; entryTime = DateTime.MinValue; tradeMFE = 0; tradeMAE = 0;
                    if (showChartAnnotations)
                    {
                        Draw.Diamond(this, $"ExitLong_Val_{CurrentBar}", false, 0, Close[0], Brushes.Gold);
                        Draw.Text(this, $"ExitLong_Val_T_{CurrentBar}", "EXIT (VAL)", 0, Close[0] + 4*TickSize, Brushes.Gold);
                    }
                    LogOutput($"[GRAD] {Time[0]} | EXIT COOLDOWN STARTED - No trading for {exitCooldownSeconds} seconds");
                    return; // Skip rest of logic
                }
                
                string fastEMAExitReason = "";
                bool fastEMAExit = ShouldExitLongOnFastEMA(fastGradient, currentClose, fastEMAValue, out fastEMAExitReason);
                
                // Log only when gradient crosses threshold or price crosses Fast EMA
                bool gradientCrossedThreshold = (fastGradient <= fastEMAGradientExitThreshold);
                bool priceCrossedFastEMA = (currentClose < fastEMAValue);
                
                if (gradientCrossedThreshold || priceCrossedFastEMA)
                {
                    string reason = "";
                    if (gradientCrossedThreshold && !priceCrossedFastEMA)
                        reason = $"Gradient at/below {fastEMAGradientExitThreshold} BUT price still above FastEMA";
                    else if (!gradientCrossedThreshold && priceCrossedFastEMA)
                        reason = $"Price below FastEMA BUT gradient above {fastEMAGradientExitThreshold}";
                    else if (gradientCrossedThreshold && priceCrossedFastEMA)
                        reason = $"BOTH conditions met: Gradient<={fastEMAGradientExitThreshold} AND Price<FastEMA";
                    
                    string exitAnalysis = $"LONG_EXIT_CHECK: FastEMAExit={fastEMAExit} Reason={reason} FastGrad={fastGradient:F4} Close={currentClose:F4} FastEMA={fastEMAValue:F4}";
                    LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                        currentSignal, currentSignal, myPosition, "EXIT_ANALYSIS", exitAnalysis);
                }
                
                bool shouldExit = fastEMAExit;
                string exitReason = fastEMAExitReason;
                
                // If fast EMA exit not triggered, check optional configured conditions
                // NOTE: Fallback conditions still respect gradient thresholds to maintain consistency
                if (!fastEMAExit)
                {
                    bool gradientCondition = true;
                    bool closeCondition = true;
                    exitReason = "";

                    if (exitOnFastEMAGradient)
                    {
                        // Respect threshold: must be <= threshold (e.g., -0.25 or more negative)
                        gradientCondition = gradientCondition && (fastGradient < 0 && fastGradient <= fastEMAGradientExitThreshold);
                        if (fastGradient < 0 && fastGradient <= fastEMAGradientExitThreshold)
                            exitReason = $"FastEMAGrad<0(<={fastEMAGradientExitThreshold})";
                    }

                    if (exitOnSlowEMAGradient)
                    {
                        gradientCondition = gradientCondition && (slowGradient < 0);
                        if (slowGradient < 0)
                            exitReason += (exitReason.Length > 0 ? "," : "") + "SlowEMAGrad<0";
                    }

                    if (exitOnCloseBelowFastEMA)
                    {
                        closeCondition = closeCondition && (currentClose < fastEMAValue);
                        if (currentClose < fastEMAValue)
                            exitReason += (exitReason.Length > 0 ? "," : "") + "Close<FastEMA";
                    }

                    if (exitOnCloseBelowSlowEMA)
                    {
                        closeCondition = closeCondition && (currentClose < slowEMAValue);
                        if (currentClose < slowEMAValue)
                            exitReason += (exitReason.Length > 0 ? "," : "") + "Close<SlowEMA";
                    }

                    shouldExit = gradientCondition && closeCondition && !string.IsNullOrEmpty(exitReason);
                }
                
                // Two-step exit confirmation: stage exit if criteria met; confirm with additional EMA drop
                if (shouldExit)
                {
                    string decisionDetails = $"EXIT_DECISION: FastEMAExit={fastEMAExit} Threshold={fastEMAGradientExitThreshold} FastGrad={fastGradient:F4} SlowGrad={slowGradient:F4} Close={currentClose:F2} FastEMA={fastEMAValue:F2} SlowEMA={slowEMAValue:F2}";
                    LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                        currentSignal, newSignal, "LONG", "EXIT_DECISION", decisionDetails);

                    if (!exitPending || exitPendingSide != "LONG")
                    {
                        exitPending = true;
                        exitPendingSide = "LONG";
                        exitPendingStartBar = CurrentBar;
                        exitPendingAnchorFastEMA = fastEMAValue;
                        exitPendingReason = exitReason;
                        LogOutput($"EXIT PENDING (LONG) -> Anchor FastEMA:{exitPendingAnchorFastEMA:F2} Reason:{exitReason}");
                        LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                            currentSignal, newSignal, "LONG", "EXIT_PENDING", $"INIT:{exitReason}");
                    }
                    else
                    {
                        double emaDrop = exitPendingAnchorFastEMA - fastEMAValue; // drop since pending
                        if (emaDrop >= exitConfirmFastEMADelta)
                        {
                            ExitLong("EnterLong");
                            string exitSnapshot_longConf = BuildIndicatorSnapshot(fastEMAValue, slowEMAValue, fastGradient);
                            LogTrade(Time[0], CurrentBar, "EXIT", "LONG", currentClose, quantity, $"CONFIRMED:{exitPendingReason};FastEMAΔ={emaDrop:F2}>= {exitConfirmFastEMADelta:F2}|{exitSnapshot_longConf}");
                            LogTradeSummary(Time[0], CurrentBar, "LONG", currentClose, $"CONFIRMED:{exitPendingReason}", emaDrop);
                            lastTradeBar = CurrentBar;
                            lastExitTime = Time[0];
                            inExitCooldown = true;
                            cooldownStartBar = CurrentBar;
                            myPosition = "FLAT";
                            currentSignal = "FLAT";
                            signalStartBar = -1;
                            inWeakReversalDelay = true;
                            weakReversalDelayBars = CurrentBar + weakReversalDelayPeriod;
                            LogOutput($"<<< EXIT LONG CONFIRMED at {currentClose:F2} | FastEMA drop {emaDrop:F2}>= {exitConfirmFastEMADelta:F2}");
                            LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                                currentSignal, newSignal, "LONG", "EXIT", $"CONFIRMED_{exitPendingReason}_Δ{emaDrop:F2}");
                            if (showChartAnnotations)
                            {
                                Draw.Diamond(this, $"ExitLong_Conf_{CurrentBar}", false, 0, Close[0], Brushes.Orange);
                                Draw.Text(this, $"ExitLong_Conf_T_{CurrentBar}", "EXIT (CONF)", 0, Close[0] + 4*TickSize, Brushes.Orange);
                            }
                            // Reset pending
                            exitPending = false; exitPendingSide = ""; exitPendingStartBar = -1; exitPendingAnchorFastEMA = 0; exitPendingReason = "";
                            // Reset entry tracking
                            entryBar = -1; entryPrice = 0.0; entryTime = DateTime.MinValue; tradeMFE = 0; tradeMAE = 0;

                            // Weak reversal delay if weak context
                            double priceGradient3Bar = 0;
                            if (CurrentBar >= 3) priceGradient3Bar = (Close[0] - Close[3]) / 3.0;
                            if (Math.Abs(priceGradient3Bar) < weakGradientThreshold && Math.Abs(fastGradient) < weakGradientThreshold)
                            {
                                inWeakReversalDelay = true;
                                weakReversalDelayBars = CurrentBar + weakReversalDelayPeriod;
                                LogOutput($"    ⏸ Setting weak reversal delay");
                            }
                        }
                        else
                        {
                            LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                                currentSignal, newSignal, "LONG", "EXIT_PENDING", $"WAIT:FastEMAΔ={emaDrop:F2}<{exitConfirmFastEMADelta:F2}");
                        }
                    }
                }
                else if (exitPending && exitPendingSide == "LONG")
                {
                    // Cancel pending if exit conditions are no longer present
                    exitPending = false; exitPendingSide = ""; exitPendingStartBar = -1; exitPendingAnchorFastEMA = 0; exitPendingReason = "";
                    LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                        currentSignal, newSignal, "LONG", "EXIT_PENDING_CANCELLED", "Conditions improved");
                    LogOutput("EXIT PENDING CANCELLED (LONG) -> Conditions improved");
                }
            }
        AfterLongExitChecks:
            
            // Exit SHORT position when fast EMA conditions are met
            if (myPosition == "SHORT" && !tradeAlreadyPlacedThisBar)
            {
                // Enforce minimum hold before evaluating exits
                if (entryBar >= 0)
                {
                    int barsSinceEntry = CurrentBar - entryBar;
                    if (barsSinceEntry < minHoldBars)
                    {
                        LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                            currentSignal, currentSignal, "SHORT", "EXIT_SUPPRESSED", $"MIN_HOLD:{barsSinceEntry}/{minHoldBars}");
                        // Skip exit logic for this bar
                        goto AfterShortExitChecks;
                    }
                }

                // --- VALIDATION CHECK: Exit if entry conditions no longer valid ---
                bool validationFailed = false;
                string validationReason = "";
                
                // Check fast gradient threshold (validation uses its own threshold)
                if (fastGradient >= -validationMinFastGradientAbs)
                {
                    validationFailed = true;
                    validationReason = $"FastGrad>={fastGradient:F4}(need<-{validationMinFastGradientAbs:F2})";
                }
                // Check both gradients negative
                else if (fastGradient >= 0 || slowGradient >= 0)
                {
                    validationFailed = true;
                    validationReason = $"GradientsNotNegative(F:{fastGradient:F4},S:{slowGradient:F4})";
                }
                // Check price below both EMAs
                else if (currentClose >= fastEMAValue || currentClose >= slowEMAValue)
                {
                    validationFailed = true;
                    validationReason = $"PriceAboveEMAs(Close:{currentClose:F2},F:{fastEMAValue:F2},S:{slowEMAValue:F2})";
                }
                
                if (validationFailed)
                {
                    LogOutput($"EXIT SHORT (VALIDATION FAILED IN POSITION) -> {validationReason}");
                    LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                        currentSignal, newSignal, "SHORT", "EXIT", $"VALIDATION_FAILED: {validationReason}");
                    
                    ExitShort("EnterShort");
                    // Trades-only log
                    string exitSnapshot_shortVal = BuildIndicatorSnapshot(fastEMAValue, slowEMAValue, fastGradient);
                    LogTrade(Time[0], CurrentBar, "EXIT", "SHORT", currentClose, quantity, $"VALIDATION_FAILED:{validationReason}|{exitSnapshot_shortVal}");
                    LogTradeSummary(Time[0], CurrentBar, "SHORT", currentClose, $"VALIDATION_FAILED:{validationReason}", 0.0);
                    lastTradeBar = CurrentBar;
                    lastExitTime = Time[0];
                    inExitCooldown = true;
                    cooldownStartBar = CurrentBar;
                    myPosition = "FLAT";
                    currentSignal = "FLAT";
                    signalStartBar = -1;
                    entryBar = -1; entryPrice = 0.0; entryTime = DateTime.MinValue; tradeMFE = 0; tradeMAE = 0;
                    if (showChartAnnotations)
                    {
                        Draw.Diamond(this, $"ExitShort_Val_{CurrentBar}", false, 0, Close[0], Brushes.Gold);
                        Draw.Text(this, $"ExitShort_Val_T_{CurrentBar}", "EXIT (VAL)", 0, Close[0] - 6*TickSize, Brushes.Gold);
                    }
                    LogOutput($"[GRAD] {Time[0]} | EXIT COOLDOWN STARTED - No trading for {exitCooldownSeconds} seconds");
                    return; // Skip rest of logic
                }
                
                string fastEMAExitReason = "";
                bool fastEMAExit = ShouldExitShortOnFastEMA(fastGradient, currentClose, fastEMAValue, out fastEMAExitReason);
                
                // Log only when gradient crosses threshold or price crosses Fast EMA
                bool gradientCrossedThreshold = (fastGradient >= fastEMAGradientExitThresholdShort);
                bool priceCrossedFastEMA = (currentClose > fastEMAValue);
                
                if (gradientCrossedThreshold || priceCrossedFastEMA)
                {
                    string reason = "";
                    if (gradientCrossedThreshold && !priceCrossedFastEMA)
                        reason = $"Gradient at/above {fastEMAGradientExitThresholdShort} BUT price still below FastEMA";
                    else if (!gradientCrossedThreshold && priceCrossedFastEMA)
                        reason = $"Price above FastEMA BUT gradient below {fastEMAGradientExitThresholdShort}";
                    else if (gradientCrossedThreshold && priceCrossedFastEMA)
                        reason = $"BOTH conditions met: Gradient>={fastEMAGradientExitThresholdShort} AND Price>FastEMA";
                    
                    string exitAnalysis = $"SHORT_EXIT_CHECK: FastEMAExit={fastEMAExit} Reason={reason} FastGrad={fastGradient:F4} Close={currentClose:F4} FastEMA={fastEMAValue:F4}";
                    LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                        currentSignal, currentSignal, myPosition, "EXIT_ANALYSIS", exitAnalysis);
                }
                
                bool shouldExit = fastEMAExit;
                string exitReason = fastEMAExitReason;
                
                // If fast EMA exit not triggered, check optional configured conditions
                // NOTE: Fallback conditions still respect gradient thresholds to maintain consistency
                if (!fastEMAExit)
                {
                    bool gradientCondition = true;
                    bool closeCondition = true;
                    exitReason = "";

                    if (exitOnFastEMAGradient)
                    {
                        // Respect threshold: must be >= threshold (e.g., 0.25 or more positive)
                        gradientCondition = gradientCondition && (fastGradient > 0 && fastGradient >= fastEMAGradientExitThresholdShort);
                        if (fastGradient > 0 && fastGradient >= fastEMAGradientExitThresholdShort)
                            exitReason = $"FastEMAGrad>0(>={fastEMAGradientExitThresholdShort})";
                    }

                    if (exitOnSlowEMAGradient)
                    {
                        gradientCondition = gradientCondition && (slowGradient > 0);
                        if (slowGradient > 0)
                            exitReason += (exitReason.Length > 0 ? "," : "") + "SlowEMAGrad>0";
                    }

                    if (exitOnCloseBelowFastEMA)
                    {
                        closeCondition = closeCondition && (currentClose > fastEMAValue);
                        if (currentClose > fastEMAValue)
                            exitReason += (exitReason.Length > 0 ? "," : "") + "Close>FastEMA";
                    }

                    if (exitOnCloseBelowSlowEMA)
                    {
                        closeCondition = closeCondition && (currentClose > slowEMAValue);
                        if (currentClose > slowEMAValue)
                            exitReason += (exitReason.Length > 0 ? "," : "") + "Close>SlowEMA";
                    }

                    shouldExit = gradientCondition && closeCondition && !string.IsNullOrEmpty(exitReason);
                }
                
                if (shouldExit)
                {
                    // Log decision details to CSV
                    string decisionDetails = $"EXIT_DECISION: FastEMAExit={fastEMAExit} Threshold={fastEMAGradientExitThresholdShort} FastGrad={fastGradient:F4} SlowGrad={slowGradient:F4} Close={currentClose:F2} FastEMA={fastEMAValue:F2} SlowEMA={slowEMAValue:F2}";
                    LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                        currentSignal, newSignal, "SHORT", "EXIT_DECISION", decisionDetails);

                    if (!exitPending || exitPendingSide != "SHORT")
                    {
                        exitPending = true;
                        exitPendingSide = "SHORT";
                        exitPendingStartBar = CurrentBar;
                        exitPendingAnchorFastEMA = fastEMAValue;
                        exitPendingReason = exitReason;
                        LogOutput($"EXIT PENDING (SHORT) -> Anchor FastEMA:{exitPendingAnchorFastEMA:F2} Reason:{exitReason}");
                        LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                            currentSignal, newSignal, "SHORT", "EXIT_PENDING", $"INIT:{exitReason}");
                    }
                    else
                    {
                        double emaRise = fastEMAValue - exitPendingAnchorFastEMA; // rise since pending
                        if (emaRise >= exitConfirmFastEMADelta)
                        {
                            ExitShort("EnterShort");
                            string exitSnapshot_shortConf = BuildIndicatorSnapshot(fastEMAValue, slowEMAValue, fastGradient);
                            LogTrade(Time[0], CurrentBar, "EXIT", "SHORT", currentClose, quantity, $"CONFIRMED:{exitPendingReason};FastEMAΔ={emaRise:F2}>= {exitConfirmFastEMADelta:F2}|{exitSnapshot_shortConf}");
                            LogTradeSummary(Time[0], CurrentBar, "SHORT", currentClose, $"CONFIRMED:{exitPendingReason}", emaRise);
                            lastTradeBar = CurrentBar;
                            lastExitTime = Time[0];
                            inExitCooldown = true;
                            cooldownStartBar = CurrentBar;
                            myPosition = "FLAT";
                            currentSignal = "FLAT";
                            signalStartBar = -1;
                            inWeakReversalDelay = true;
                            weakReversalDelayBars = CurrentBar + weakReversalDelayPeriod;
                            LogOutput($"<<< EXIT SHORT CONFIRMED at {currentClose:F2} | FastEMA rise {emaRise:F2}>= {exitConfirmFastEMADelta:F2}");
                            LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                                currentSignal, newSignal, "SHORT", "EXIT", $"CONFIRMED_{exitPendingReason}_Δ{emaRise:F2}");
                            if (showChartAnnotations)
                            {
                                Draw.Diamond(this, $"ExitShort_Conf_{CurrentBar}", false, 0, Close[0], Brushes.Orange);
                                Draw.Text(this, $"ExitShort_Conf_T_{CurrentBar}", "EXIT (CONF)", 0, Close[0] - 6*TickSize, Brushes.Orange);
                            }
                            // Reset pending
                            exitPending = false; exitPendingSide = ""; exitPendingStartBar = -1; exitPendingAnchorFastEMA = 0; exitPendingReason = "";
                            // Reset entry tracking
                            entryBar = -1; entryPrice = 0.0; entryTime = DateTime.MinValue; tradeMFE = 0; tradeMAE = 0;

                            // Weak reversal delay if weak context
                            double priceGradient3Bar = 0;
                            if (CurrentBar >= 3) priceGradient3Bar = (Close[0] - Close[3]) / 3.0;
                            if (Math.Abs(priceGradient3Bar) < weakGradientThreshold && Math.Abs(fastGradient) < weakGradientThreshold)
                            {
                                inWeakReversalDelay = true;
                                weakReversalDelayBars = CurrentBar + weakReversalDelayPeriod;
                                LogOutput($"    ⏸ Setting weak reversal delay");
                            }
                        }
                        else
                        {
                            LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                                currentSignal, newSignal, "SHORT", "EXIT_PENDING", $"WAIT:FastEMAΔ={emaRise:F2}<{exitConfirmFastEMADelta:F2}");
                        }
                    }
                }
                else if (exitPending && exitPendingSide == "SHORT")
                {
                    // Cancel pending if exit conditions are no longer present
                    exitPending = false; exitPendingSide = ""; exitPendingStartBar = -1; exitPendingAnchorFastEMA = 0; exitPendingReason = "";
                    LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                        currentSignal, newSignal, "SHORT", "EXIT_PENDING_CANCELLED", "Conditions improved");
                    LogOutput("EXIT PENDING CANCELLED (SHORT) -> Conditions improved");
                }
            }
            AfterShortExitChecks:
            
            // ============================================================================
            // TRADE LOGIC - CONFIGURABLE ENTRY DELAY
            // ============================================================================
            
            // Calculate price gradient with no smoothing (current bar vs previous bar)
            double priceGradient = 0;
            if (CurrentBar >= 1)
            {
                priceGradient = Close[0] - Close[1];
            }

            if (enableLogging && IsFirstTickOfBar)
            {
                LogOutput($"PRICE GRADIENT -> PriceGrad:{priceGradient:+0.000;-0.000} Close0:{Close[0]:F2} Close1:{(CurrentBar >= 1 ? Close[1] : 0):F2}");
            }
            
            // Signal changed from previous bar
            if (newSignal != currentSignal)
            {
                // Debug: Always log position status (both NinjaTrader and our internal tracking)
                Print(string.Format("[GRAD] {0} | SIGNAL CHANGE: {1} -> {2} | NT Position: {3} (Qty: {4}) | My Position: {5}", 
                    Time[0], currentSignal, newSignal, Position.MarketPosition, Position.Quantity, myPosition));
                LogOutput("");
                LogOutput($"SIGNAL CHANGE: {currentSignal} -> {newSignal} | Position: {myPosition}");
                
                // Log to CSV
                LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                    currentSignal, newSignal, myPosition, "SIGNAL_CHANGE", $"PriceGrad:{priceGradient:F2}");
                
                // Check if this is a WEAK reversal when we have a position
                // IMPORTANT: Check for weakness on ANY exit (to FLAT or to opposite direction)
                bool isWeakReversal = false;
                if (myPosition != "FLAT")
                {
                    // Weak if: price gradient AND fast EMA gradient are both weak (below threshold)
                    isWeakReversal = (Math.Abs(priceGradient) < weakGradientThreshold && 
                                     Math.Abs(fastGradient) < weakGradientThreshold);
                }
                
                // DISABLED: Exit on signal change - only exit via Fast EMA gradient threshold check
                // Exit existing position when signal changes
                if (false && myPosition != "FLAT" && !tradeAlreadyPlacedThisBar)
                {
                    LogOutput($"SIGNAL CHANGE EXIT CHECK -> MyPos:{myPosition} CurrSignal:{currentSignal} NewSignal:{newSignal} TradeAlreadyThisBar:{tradeAlreadyPlacedThisBar} WeakRev:{isWeakReversal} PriceGrad:{priceGradient:+0.000;-0.000} FastGrad:{fastGradient:+0.000;-0.000}");
                    
                    // Log decision details to CSV before exit
                    string decisionDetails = $"SIGNAL_EXIT_DECISION: OldSignal={currentSignal} NewSignal={newSignal} WeakReversal={isWeakReversal} PriceGrad={priceGradient:F4} FastGrad={fastGradient:F4}";
                    LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                        currentSignal, newSignal, myPosition, "EXIT_DECISION", decisionDetails);
                    
                    if (myPosition == "LONG")
                    {
                        ExitLong("ExitLong");
                        lastTradeBar = CurrentBar;
                        string exitType = isWeakReversal ? "WEAK_REVERSAL" : "STRONG_REVERSAL";
                        Print(string.Format("[GRAD] {0} | EXIT LONG at {1:F2} ({2}) | PriceGrad: {3:F2} | FastGrad: {4:F2}", 
                            Time[0], currentClose, exitType, priceGradient, fastGradient));
                        LogOutput($"<<< EXIT LONG at {currentClose:F2} ({exitType}) | PriceGrad: {priceGradient:F2} | FastGrad: {fastGradient:F2}");
                        LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                            currentSignal, newSignal, "LONG", "EXIT", exitType);
                        myPosition = "FLAT";
                        
                        // Set delay if weak reversal
                        if (isWeakReversal)
                        {
                            inWeakReversalDelay = true;
                            weakReversalDelayBars = CurrentBar + weakReversalDelayPeriod;
                            Print(string.Format("[GRAD] {0} | WEAK REVERSAL - Delaying entry until bar {1}", 
                                Time[0], weakReversalDelayBars));
                            LogOutput($"    ⏸ WEAK REVERSAL - Delaying entry for {weakReversalDelayPeriod} bars");
                        }
                    }
                    else if (myPosition == "SHORT")
                    {
                        ExitShort("ExitShort");
                        lastTradeBar = CurrentBar;
                        string exitType = isWeakReversal ? "WEAK_REVERSAL" : "STRONG_REVERSAL";
                        Print(string.Format("[GRAD] {0} | EXIT SHORT at {1:F2} ({2}) | PriceGrad: {3:F2} | FastGrad: {4:F2}", 
                            Time[0], currentClose, exitType, priceGradient, fastGradient));
                        LogOutput($"<<< EXIT SHORT at {currentClose:F2} ({exitType}) | PriceGrad: {priceGradient:F2} | FastGrad: {fastGradient:F2}");
                        LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                            currentSignal, newSignal, "SHORT", "EXIT", exitType);
                        myPosition = "FLAT";
                        
                        // Set delay if weak reversal
                        if (isWeakReversal)
                        {
                            inWeakReversalDelay = true;
                            weakReversalDelayBars = CurrentBar + weakReversalDelayPeriod;
                            Print(string.Format("[GRAD] {0} | WEAK REVERSAL - Delaying entry until bar {1}", 
                                Time[0], weakReversalDelayBars));
                            LogOutput($"    ⏸ WEAK REVERSAL - Delaying entry for {weakReversalDelayPeriod} bars");
                        }
                    }
                }
                else
                {
                    Print(string.Format("[GRAD] {0} | My Position is FLAT - no exit needed", Time[0]));
                }
                
                // Record when new signal starts
                // CRITICAL: Don't set signalStartBar on same bar as exit - wait for next bar
                if (newSignal != "FLAT")
                {
                    // Don't start new signal on same bar we just exited
                    // This prevents same-bar reversals
                    signalStartBar = -1;  // Will be set on NEXT bar
                    if (enableLogging)
                    {
                        Print(string.Format("[GRAD] {0} | NEW {1} SIGNAL at bar {2} | FastGrad: {3:+0.000;-0.000} | SlowGrad: {4:+0.000;-0.000}",
                            Time[0], newSignal, CurrentBar, fastGradient, slowGradient));
                    }
                    LogOutput($"    🔔 NEW {newSignal} SIGNAL | FastGrad: {fastGradient:+0.000;-0.000} | SlowGrad: {slowGradient:+0.000;-0.000}");
                }
                else
                {
                    signalStartBar = -1;  // Reset when signal goes FLAT
                }
                
                // Update current signal
                currentSignal = newSignal;
            }
            
            // Set signalStartBar if we have a NON-FLAT signal but no start bar set
            // This allows entry on the NEXT bar after signal change, but ONLY while
            // the strategy has an active LONG/SHORT signal (never while FLAT)
            // CRITICAL: Don't set signalStartBar on same bar as a trade (exit or entry)
            if (currentSignal != "FLAT" && signalStartBar == -1 && myPosition == "FLAT" && !inWeakReversalDelay && !tradeAlreadyPlacedThisBar)
            {
                signalStartBar = CurrentBar;
                LogOutput($"SIGNAL START -> CurrSignal:{currentSignal} StartBar:{signalStartBar} MyPos:{myPosition} InWeakDelay:{inWeakReversalDelay} TradeAlreadyThisBar:{tradeAlreadyPlacedThisBar}");
            }
            
            // Check if weak reversal delay has expired
            if (inWeakReversalDelay && CurrentBar >= weakReversalDelayBars)
            {
                inWeakReversalDelay = false;
                Print(string.Format("[GRAD] {0} | WEAK REVERSAL DELAY EXPIRED - Ready to enter", Time[0]));
                LogOutput("    ▶ WEAK REVERSAL DELAY EXPIRED - Ready to enter");
            }
            
            // Attempt early reversal entries (LONG/SHORT) before standard signal-based entry
            // Executes only when FLAT and no trade this bar.
            if (enableReversalEarlyEntry && myPosition == "FLAT" && Position.MarketPosition == MarketPosition.Flat && !tradeAlreadyPlacedThisBar)
            {
                string revReason;
                if (TryEarlyReversalLong(out revReason))
                {
                    tradeAlreadyPlacedThisBar = true; // prevent duplicate logic later in bar
                    LogOutput($"EARLY_REVERSAL_LONG ENTRY -> {revReason}");
                }
                else if (TryEarlyReversalShort(out revReason))
                {
                    tradeAlreadyPlacedThisBar = true; // prevent duplicate logic later in bar
                    LogOutput($"EARLY_REVERSAL_SHORT ENTRY -> {revReason}");
                }
            }

            // Check if we should enter based on entry delay (use our internal position tracking)
            // IMPORTANT: Never enter while strategy is FLAT (no LONG/SHORT signal).
            // SKIP ENTRY if we're in weak reversal delay period.
            if (currentSignal != "FLAT" && myPosition == "FLAT" && signalStartBar >= 0 && !inWeakReversalDelay && !tradeAlreadyPlacedThisBar)
            {
                int barsInSignal = CurrentBar - signalStartBar + 1;
                
                if (enableLogging)
                {
                    Print(string.Format("[GRAD] {0} | WAITING: {1} signal, bar {2} of {3} required", 
                        Time[0], currentSignal, barsInSignal, entryBarDelay));
                }
                
                // Enter when we reach the specified bar of the signal
                if (barsInSignal == entryBarDelay)
                {
                    LogOutput($"ENTRY DECISION -> CurrSignal:{currentSignal} BarsInSignal:{barsInSignal} EntryDelay:{entryBarDelay} MyPos:{myPosition} InWeakDelay:{inWeakReversalDelay} TradeAlreadyThisBar:{tradeAlreadyPlacedThisBar} NTPos:{Position.MarketPosition} SignalStartBar:{signalStartBar}");
                    
                    // VALIDATION: Re-check if conditions are still valid before entering
                    bool conditionsStillValid = false;
                    bool shouldReverse = false;
                    bool priceRising = false;
                    string invalidationReason = "";
                    
                    if (CurrentBar >= 1)
                    {
                        priceRising = (Close[0] > Close[1]);
                    }
                    
                    if (currentSignal == "LONG")
                    {
                        // LONG validation: Both gradients must be positive, fast gradient strong (adaptive), price above both EMAs
                        bool gradientsPositive = (fastGradient > 0 && slowGradient > 0);
                        double entryThrLong = GetEntryFastGradientThreshold(true);
                        bool fastGradientStrong = (fastGradient > entryThrLong);
                        bool priceAboveEMAs = (currentClose > fastEMAValue && currentClose > slowEMAValue);

                        conditionsStillValid = gradientsPositive && fastGradientStrong && priceAboveEMAs;

                        // --- Additional Indicator-Based Filters (LONG) ---
                        if (conditionsStillValid)
                        {
                            List<string> filterFailures = new List<string>();
                            double adxVal = adx != null ? adx[0] : 0.0;
                            double atrVal = atr != null ? atr[0] : 0.0;
                            double rsiVal = rsi != null ? rsi[0] : 0.0;
                            double bandwidth = Math.Abs(fastEMAValue - slowEMAValue) / (slowEMAValue != 0 ? slowEMAValue : 1.0);
                            double accel = currentFastGradientAcceleration;
                            LogOutput($"FILTER_STATS(LONG) FastGrad:{fastGradient:+0.000;-0.000} ADX:{adxVal:F1} ATR:{atrVal:F2} RSI:{rsiVal:F1} BW:{bandwidth:F4} GradStab:{currentGradientStability:F3} Accel:{accel:+0.000;-0.000}");
                            if (!disableEntryFilters)
                            {
                                if (adxVal < minAdxForEntry) filterFailures.Add($"ADX<{minAdxForEntry:F0}({adxVal:F1})");
                                if (currentGradientStability > maxGradientStabilityForEntry) filterFailures.Add($"GradStab>{maxGradientStabilityForEntry:F2}({currentGradientStability:F3})");
                                if (bandwidth < minBandwidthForEntry || bandwidth > maxBandwidthForEntry) filterFailures.Add($"BW{bandwidth:F4}Out[{minBandwidthForEntry:F3},{maxBandwidthForEntry:F3}]");
                                if (requireAccelAlignment && fastGradient > 0 && accel < 0) filterFailures.Add($"AccelMisAligned({accel:+0.000;-0.000})");
                                if (enableATRCap && atrVal > maxATRForEntry) filterFailures.Add($"ATR>{maxATRForEntry:F2}({atrVal:F2})");
                                if (enableRSIFloor && rsiVal < minRSIForEntry) filterFailures.Add($"RSI<{minRSIForEntry:F1}({rsiVal:F1})");
                            }

                            if (!disableEntryFilters && filterFailures.Count > 0)
                            {
                                conditionsStillValid = false;
                                invalidationReason = "FILTERS:" + string.Join("|", filterFailures) +
                                    $" ADX:{adxVal:F1} ATR:{atrVal:F2} RSI:{rsiVal:F1} GradStab:{currentGradientStability:F3} BW:{bandwidth:F4} Accel:{accel:+0.000;-0.000}";
                            }
                            else if (disableEntryFilters && filterFailures.Count > 0)
                            {
                                // Log that filters were bypassed (still enter)
                                LogOutput($"ENTRY_FILTERS_BYPASSED(LONG) -> {string.Join("|", filterFailures)}");
                            }
                        }

                        // Reversal logic remains disabled; compute value but not used
                        bool oppositeGradientsNegative = (fastGradient < 0 && slowGradient < 0);
                        bool oppositeFastGradientStrong = (fastGradient < -entryThrLong);
                        bool oppositePriceBelowEMAs = (currentClose < fastEMAValue && currentClose < slowEMAValue);
                        shouldReverse = oppositeGradientsNegative && oppositeFastGradientStrong && oppositePriceBelowEMAs;

                        if (!conditionsStillValid)
                        {
                            if (!gradientsPositive)
                                invalidationReason = "Gradients turned negative";
                            else if (!fastGradientStrong)
                                invalidationReason = $"Fast gradient too weak ({fastGradient:F4} <= thr {entryThrLong:F2}{(enableAdaptiveEntryGradient?" (adaptive)":"")})";
                            else if (!priceAboveEMAs)
                                invalidationReason = "Price dropped below EMAs";
                        }
                    }
                    else if (currentSignal == "SHORT")
                    {
                        // SHORT validation: Both gradients negative, fast gradient strong enough (adaptive), price below both EMAs
                        bool gradientsNegative = (fastGradient < 0 && slowGradient < 0);
                        double entryThrShort = GetEntryFastGradientThreshold(false);
                        bool fastGradientStrong = (fastGradient < -entryThrShort);
                        bool priceBelowEMAs = (currentClose < fastEMAValue && currentClose < slowEMAValue);

                        conditionsStillValid = gradientsNegative && fastGradientStrong && priceBelowEMAs;

                        // --- Additional Indicator-Based Filters (SHORT) ---
                        if (conditionsStillValid)
                        {
                            List<string> filterFailures = new List<string>();
                            double adxVal = adx != null ? adx[0] : 0.0;
                            double atrVal = atr != null ? atr[0] : 0.0;
                            double rsiVal = rsi != null ? rsi[0] : 0.0;
                            double bandwidth = Math.Abs(fastEMAValue - slowEMAValue) / (slowEMAValue != 0 ? slowEMAValue : 1.0);
                            double accel = currentFastGradientAcceleration;
                            LogOutput($"FILTER_STATS(SHORT) FastGrad:{fastGradient:+0.000;-0.000} ADX:{adxVal:F1} ATR:{atrVal:F2} RSI:{rsiVal:F1} BW:{bandwidth:F4} GradStab:{currentGradientStability:F3} Accel:{accel:+0.000;-0.000}");
                            if (!disableEntryFilters)
                            {
                                if (adxVal < minAdxForEntry) filterFailures.Add($"ADX<{minAdxForEntry:F0}({adxVal:F1})");
                                if (currentGradientStability > maxGradientStabilityForEntry) filterFailures.Add($"GradStab>{maxGradientStabilityForEntry:F2}({currentGradientStability:F3})");
                                if (bandwidth < minBandwidthForEntry || bandwidth > maxBandwidthForEntry) filterFailures.Add($"BW{bandwidth:F4}Out[{minBandwidthForEntry:F3},{maxBandwidthForEntry:F3}]");
                                if (requireAccelAlignment && fastGradient < 0 && accel > 0) filterFailures.Add($"AccelMisAligned({accel:+0.000;-0.000})");
                                if (enableATRCap && atrVal > maxATRForEntry) filterFailures.Add($"ATR>{maxATRForEntry:F2}({atrVal:F2})");
                                if (enableRSIFloor && rsiVal < minRSIForEntry) filterFailures.Add($"RSI<{minRSIForEntry:F1}({rsiVal:F1})");
                            }

                            if (!disableEntryFilters && filterFailures.Count > 0)
                            {
                                conditionsStillValid = false;
                                invalidationReason = "FILTERS:" + string.Join("|", filterFailures) +
                                    $" ADX:{adxVal:F1} ATR:{atrVal:F2} RSI:{rsiVal:F1} GradStab:{currentGradientStability:F3} BW:{bandwidth:F4} Accel:{accel:+0.000;-0.000}";
                            }
                            else if (disableEntryFilters && filterFailures.Count > 0)
                            {
                                LogOutput($"ENTRY_FILTERS_BYPASSED(SHORT) -> {string.Join("|", filterFailures)}");
                            }
                        }

                        // Reversal logic remains disabled; compute value but not used
                        bool oppositeGradientsPositive = (fastGradient > 0 && slowGradient > 0);
                        bool oppositeFastGradientStrong = (fastGradient > entryThrShort);
                        bool oppositePriceAboveEMAs = (currentClose > fastEMAValue && currentClose > slowEMAValue);
                        shouldReverse = oppositeGradientsPositive && oppositeFastGradientStrong && oppositePriceAboveEMAs;

                        if (!conditionsStillValid)
                        {
                            if (!gradientsNegative)
                                invalidationReason = "Gradients turned positive";
                            else if (!fastGradientStrong)
                                invalidationReason = $"Fast gradient too weak ({fastGradient:F4} >= -thr {entryThrShort:F2}{(enableAdaptiveEntryGradient?" (adaptive)":"")})";
                            else if (!priceBelowEMAs)
                                invalidationReason = "Price rose above EMAs";
                        }
                    }
                    
                    // Log detailed entry decision to CSV
                    bool fastGradPositive = fastGradient > 0;
                    bool slowGradPositive = slowGradient > 0;
                    bool closeAboveFastEMA = currentClose > fastEMAValue;
                    bool closeAboveSlowEMA = currentClose > slowEMAValue;
                    string entryConditions = $"ENTRY_DECISION: Signal={currentSignal} Bar={barsInSignal}/{entryBarDelay} Valid={conditionsStillValid} ShouldReverse={shouldReverse} PriceRising={priceRising} FastGrad={fastGradient:F4}({(fastGradPositive?"POS":"NEG")}) SlowGrad={slowGradient:F4}({(slowGradPositive?"POS":"NEG")}) Close={currentClose:F2} FastEMA={fastEMAValue:F2}({(closeAboveFastEMA?"ABOVE":"BELOW")}) SlowEMA={slowEMAValue:F2}({(closeAboveSlowEMA?"ABOVE":"BELOW")}) Thr={(currentSignal=="LONG"?GetEntryFastGradientThreshold(true):GetEntryFastGradientThreshold(false)):F2}{(enableAdaptiveEntryGradient?"(adaptive)":"")}";
                    LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                        currentSignal, currentSignal, myPosition, "ENTRY_DECISION", entryConditions);
                    
                    // DISABLED REVERSAL LOGIC - Just exit and go flat, don't reverse
                    if (false && shouldReverse)
                    {
                        // This reversal logic is DISABLED
                    }
                    
                    if (!conditionsStillValid)
                    {
                        // Conditions invalidated - EXIT position if any, then restart delay
                        
                        // Exit existing position before restarting delay
                        if (myPosition == "LONG")
                        {
                            ExitLong("EnterLong");
                            Print(string.Format("[GRAD] {0} | EXIT LONG (VALIDATION FAILED) at {1:F2} - {2}", Time[0], currentClose, invalidationReason));
                            LogOutput($"<<< EXIT LONG (VALIDATION FAILED) at {currentClose:F2} - {invalidationReason}");
                            LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                                currentSignal, currentSignal, "LONG", "EXIT", $"VALIDATION_FAILED_{invalidationReason}");
                            string exitSnapshot_entryPhaseLong = BuildIndicatorSnapshot(fastEMAValue, slowEMAValue, fastGradient);
                            LogTrade(Time[0], CurrentBar, "EXIT", "LONG", currentClose, quantity, $"ENTRY_PHASE_VALIDATION_FAILED:{invalidationReason}|{exitSnapshot_entryPhaseLong}");
                            LogTradeSummary(Time[0], CurrentBar, "LONG", currentClose, $"ENTRY_PHASE_VALIDATION_FAILED:{invalidationReason}", 0.0);
                            myPosition = "FLAT";
                            lastTradeBar = CurrentBar;
                            lastExitTime = Time[0];
                            inExitCooldown = true;
                            currentSignal = "FLAT";
                            signalStartBar = -1;
                            entryBar = -1; entryPrice = 0.0; entryTime = DateTime.MinValue; tradeMFE = 0; tradeMAE = 0;
                            // Prevent immediate re-entry or reversal in same tick
                            return;
                        }
                        else if (myPosition == "SHORT")
                        {
                            ExitShort("EnterShort");
                            Print(string.Format("[GRAD] {0} | EXIT SHORT (VALIDATION FAILED) at {1:F2} - {2}", Time[0], currentClose, invalidationReason));
                            LogOutput($"<<< EXIT SHORT (VALIDATION FAILED) at {currentClose:F2} - {invalidationReason}");
                            LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                                currentSignal, currentSignal, "SHORT", "EXIT", $"VALIDATION_FAILED_{invalidationReason}");
                            string exitSnapshot_entryPhaseShort = BuildIndicatorSnapshot(fastEMAValue, slowEMAValue, fastGradient);
                            LogTrade(Time[0], CurrentBar, "EXIT", "SHORT", currentClose, quantity, $"ENTRY_PHASE_VALIDATION_FAILED:{invalidationReason}|{exitSnapshot_entryPhaseShort}");
                            LogTradeSummary(Time[0], CurrentBar, "SHORT", currentClose, $"ENTRY_PHASE_VALIDATION_FAILED:{invalidationReason}", 0.0);
                            myPosition = "FLAT";
                            lastTradeBar = CurrentBar;
                            lastExitTime = Time[0];
                            inExitCooldown = true;
                            currentSignal = "FLAT";
                            signalStartBar = -1;
                            entryBar = -1; entryPrice = 0.0; entryTime = DateTime.MinValue; tradeMFE = 0; tradeMAE = 0;
                            // Prevent immediate re-entry or reversal in same tick
                            return;
                        }
                        else
                        {
                            // Not in position - just restart delay counter
                            string cancelAction = invalidationReason.StartsWith("FILTERS:") ? "ENTRY_FILTER_BLOCKED" : "ENTRY_CANCELLED";
                            Print(string.Format("[GRAD] {0} | {1}: {2} - Restarting delay counter", Time[0], cancelAction, invalidationReason));
                            LogOutput($"    ⚠ {cancelAction}: {invalidationReason} - Restarting delay counter");
                            LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                                currentSignal, currentSignal, myPosition, cancelAction, invalidationReason);
                            signalStartBar = CurrentBar; // Restart the delay from current bar
                        }
                    }
                    else if (currentSignal == "LONG" && myPosition == "FLAT" && Position.MarketPosition == MarketPosition.Flat && !inExitCooldown)
                    {
                        double absFast = Math.Abs(fastGradient);
                        bool forcedLongEntry = forcedLongEligibility.Contains(CurrentBar);
                        if (!forcedLongEntry && absFast > maxEntryFastGradientAbs)
                        {
                            string entryInvalidationReason = $"EntryFastGradTooHigh(|F|={absFast:F3}>Max{maxEntryFastGradientAbs:F2})";
                            Print(string.Format("[GRAD] {0} | ENTRY CANCELLED: {1} - Restarting delay counter", Time[0], entryInvalidationReason));
                            LogOutput($"    ⚠ ENTRY CANCELLED: {entryInvalidationReason} - Restarting delay counter");
                            LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                                currentSignal, currentSignal, myPosition, "ENTRY_CANCELLED", entryInvalidationReason);
                            signalStartBar = CurrentBar; // Restart delay
                        }
                        else
                        {
                            // Reset any pending exit from prior state
                            exitPending = false; exitPendingSide = ""; exitPendingStartBar = -1; exitPendingAnchorFastEMA = 0; exitPendingReason = "";
                            if (forcedLongEntry)
                            {
                                LogOutput($"FORCED LONG ENTRY override applied at bar {CurrentBar}");
                                Print($"[GRAD] FORCED LONG ENTRY override applied (FastGrad {fastGradient:+0.000;-0.000})");
                            }
                            EnterLong(quantity, "EnterLong");
                            lastTradeBar = CurrentBar;
                            myPosition = "LONG";
                            entryBar = CurrentBar;
                            entryPrice = currentClose; entryTime = Time[0]; tradeMFE = 0.0; tradeMAE = 0.0;
                            Print(string.Format("[GRAD] {0} | ENTER LONG at {1:F2} on bar #{2} of signal | FastGrad: {3:+0.000;-0.000} | SlowGrad: {4:+0.000;-0.000}",
                                Time[0], currentClose, barsInSignal, fastGradient, slowGradient));
                            LogOutput($">>> ENTER LONG at {currentClose:F2} (Bar {barsInSignal}/{entryBarDelay}) | FastGrad: {fastGradient:+0.000;-0.000} | SlowGrad: {slowGradient:+0.000;-0.000}");
                            string indicatorSnapshot = BuildIndicatorSnapshot(fastEMAValue, slowEMAValue, fastGradient);
                            LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                                currentSignal, currentSignal, "LONG", "ENTRY", $"EnterLong_Bar{barsInSignal}|{indicatorSnapshot}");
                            LogTrade(Time[0], CurrentBar, "ENTRY", "LONG", currentClose, quantity, $"EnterLong_Bar{barsInSignal}|{indicatorSnapshot}");
                            if (disableEntryFilters)
                            {
                                LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                                    currentSignal, currentSignal, "LONG", "ENTRY_FILTERS_BYPASSED", $"Bypassed|ADX>={minAdxForEntry:F0}|GradStab<={maxGradientStabilityForEntry:F2}|BW[{minBandwidthForEntry:F3},{maxBandwidthForEntry:F3}] AccelAlign={requireAccelAlignment}");
                            }
                            // Optional: record entry filters snapshot to trades summary when exiting later
                            if (showChartAnnotations)
                            {
                                Draw.ArrowUp(this, $"EnterLong_{CurrentBar}", false, 0, Low[0] - 2*TickSize, Brushes.LimeGreen);
                                Draw.Text(this, $"EnterLong_T_{CurrentBar}", "LONG", 0, Low[0] - 6*TickSize, Brushes.LimeGreen);
                            }
                        }
                    }
                    else if (currentSignal == "SHORT" && myPosition == "FLAT" && Position.MarketPosition == MarketPosition.Flat && !inExitCooldown)
                    {
                        double absFast = Math.Abs(fastGradient);
                        bool forcedShortEntry = forcedShortEligibility.Contains(CurrentBar);
                        if (!forcedShortEntry && absFast > maxEntryFastGradientAbs)
                        {
                            string entryInvalidationReason = $"EntryFastGradTooHigh(|F|={absFast:F3}>Max{maxEntryFastGradientAbs:F2})";
                            Print(string.Format("[GRAD] {0} | ENTRY CANCELLED: {1} - Restarting delay counter", Time[0], entryInvalidationReason));
                            LogOutput($"    ⚠ ENTRY CANCELLED: {entryInvalidationReason} - Restarting delay counter");
                            LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                                currentSignal, currentSignal, myPosition, "ENTRY_CANCELLED", entryInvalidationReason);
                            signalStartBar = CurrentBar; // Restart delay
                        }
                        else
                        {
                            // Reset any pending exit from prior state
                            exitPending = false; exitPendingSide = ""; exitPendingStartBar = -1; exitPendingAnchorFastEMA = 0; exitPendingReason = "";
                            if (forcedShortEntry)
                            {
                                LogOutput($"FORCED SHORT ENTRY override applied at bar {CurrentBar}");
                                Print($"[GRAD] FORCED SHORT ENTRY override applied (FastGrad {fastGradient:+0.000;-0.000})");
                            }
                            EnterShort(quantity, "EnterShort");
                            lastTradeBar = CurrentBar;
                            myPosition = "SHORT";
                            entryBar = CurrentBar;
                            entryPrice = currentClose; entryTime = Time[0]; tradeMFE = 0.0; tradeMAE = 0.0;
                            Print(string.Format("[GRAD] {0} | ENTER SHORT at {1:F2} on bar #{2} of signal | FastGrad: {3:+0.000;-0.000} | SlowGrad: {4:+0.000;-0.000}",
                                Time[0], currentClose, barsInSignal, fastGradient, slowGradient));
                            LogOutput($">>> ENTER SHORT at {currentClose:F2} (Bar {barsInSignal}/{entryBarDelay}) | FastGrad: {fastGradient:+0.000;-0.000} | SlowGrad: {slowGradient:+0.000;-0.000}");
                            string indicatorSnapshot = BuildIndicatorSnapshot(fastEMAValue, slowEMAValue, fastGradient);
                            LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                                currentSignal, currentSignal, "SHORT", "ENTRY", $"EnterShort_Bar{barsInSignal}|{indicatorSnapshot}");
                            LogTrade(Time[0], CurrentBar, "ENTRY", "SHORT", currentClose, quantity, $"EnterShort_Bar{barsInSignal}|{indicatorSnapshot}");
                            if (disableEntryFilters)
                            {
                                LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                                    currentSignal, currentSignal, "SHORT", "ENTRY_FILTERS_BYPASSED", $"Bypassed|ADX>={minAdxForEntry:F0}|GradStab<={maxGradientStabilityForEntry:F2}|BW[{minBandwidthForEntry:F3},{maxBandwidthForEntry:F3}] AccelAlign={requireAccelAlignment}");
                            }
                            // Optional: record entry filters snapshot to trades summary when exiting later
                            if (showChartAnnotations)
                            {
                                Draw.ArrowDown(this, $"EnterShort_{CurrentBar}", false, 0, High[0] + 2*TickSize, Brushes.IndianRed);
                                Draw.Text(this, $"EnterShort_T_{CurrentBar}", "SHORT", 0, High[0] + 6*TickSize, Brushes.IndianRed);
                            }
                        }
                    }
                    else if (conditionsStillValid)
                    {
                        // We had a valid setup but did not enter due to guards
                        string skipReason = $"SKIP ENTRY: Guards -> MyPos:{myPosition} NTPos:{Position.MarketPosition} InCooldown:{inExitCooldown} TradeThisBar:{tradeAlreadyPlacedThisBar} SignalStartBar:{signalStartBar} LastTradeBar:{lastTradeBar}";
                        LogOutput(skipReason);
                        LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                            currentSignal, currentSignal, myPosition, "ENTRY_SKIPPED", skipReason);
                    }
                }
            }
            
            // Update plots for visualization
            // Signal plot: 1 = LONG, -1 = SHORT, 0 = FLAT
            if (currentSignal == "LONG")
                Values[0][0] = 1;
            else if (currentSignal == "SHORT")
                Values[0][0] = -1;
            else
                Values[0][0] = 0;

            // EMA plots
            Values[1][0] = fastEMAValue;
            Values[2][0] = slowEMAValue;

            // Update MFE/MAE on first tick of bar for current open trade
            if (IsFirstTickOfBar && myPosition != "FLAT" && entryBar >= 0)
            {
                if (myPosition == "LONG")
                {
                    double fav = High[0] - entryPrice; if (fav > tradeMFE) tradeMFE = fav;
                    double adv = Low[0] - entryPrice; if (adv < tradeMAE) tradeMAE = adv; // more negative is worse
                }
                else if (myPosition == "SHORT")
                {
                    double fav = entryPrice - Low[0]; if (fav > tradeMFE) tradeMFE = fav;
                    double adv = entryPrice - High[0]; if (adv < tradeMAE) tradeMAE = adv; // more negative is worse
                }
            }
            
            // Note: Gradients now calculated from EMA[1] and EMA[2] (previous bar closes)
            // This provides stable gradient values throughout the current bar
            
            // Heartbeat logging - log state every second to CSV
            if ((DateTime.Now - lastHeartbeatTime).TotalSeconds >= heartbeatIntervalSeconds)
            {
                lastHeartbeatTime = DateTime.Now;
                string heartbeatInfo = $"HEARTBEAT: Signal={currentSignal} MyPos={myPosition} NTPos={Position.MarketPosition} FastGrad={fastGradient:F4} SlowGrad={slowGradient:F4} Close={currentClose:F2} FastEMA={fastEMAValue:F2} SlowEMA={slowEMAValue:F2} InWeakDelay={inWeakReversalDelay} SignalStartBar={signalStartBar} LastTradeBar={lastTradeBar}";
                LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                    currentSignal, currentSignal, myPosition, "HEARTBEAT", heartbeatInfo);
                if (showHud)
                {
                    int secsLeft = 0;
                    if (inExitCooldown && lastExitTime != DateTime.MinValue)
                        secsLeft = Math.Max(0, exitCooldownSeconds - (int)((Time[0] - lastExitTime).TotalSeconds));
                    string hud = $"Sig:{currentSignal}  Pos:{myPosition}/{Position.MarketPosition}  FastG:{fastGradient:+0.000;-0.000}  SlowG:{slowGradient:+0.000;-0.000}  Close:{currentClose:F2}  Cooldown:{(inExitCooldown?secsLeft:0)}s";
                    if (hudSimpleFont == null)
                    {
                        hudSimpleFont = new SimpleFont("Segoe UI", hudFontSize);
                    }
                    string hudPrefix = (hudPosition == TextPosition.TopLeft ? HudOffsetPrefix() : string.Empty);
                    Draw.TextFixed(this, "HUD_Main", hudPrefix + hud, hudPosition, Brushes.Black, hudSimpleFont, Brushes.Transparent, Brushes.Transparent, 0);
                }
            }
            
            // Debug: Draw bar index labels on chart (place above bar, high-contrast)
            if (showBarIndexLabels && IsFirstTickOfBar)
            {
                Draw.Text(this, "BarLabel_" + CurrentBar, CurrentBar.ToString(), 0, High[0] + (6 * TickSize), Brushes.Black);
            }
            
            // Stream compact diagnosis to web server on every completed bar
            if (IsFirstTickOfBar)
            {
                StreamCompactDiagnosis();
            }
        }
        
        #endregion
        
        

        #region Helper Methods
        private string HudOffsetPrefix()
        {
            if (hudVerticalOffset <= 0) return string.Empty;
            return new string('\n', hudVerticalOffset);
        }
        private double GetEntryFastGradientThreshold(bool isLong)
        {
            double thr = minEntryFastGradientAbs;
            if (!enableAdaptiveEntryGradient) return thr;
            // Recent fast gradient sign flip (near zero-cross)
            bool zeroFlip = false;
            if (CurrentBar >= 2)
            {
                double fg0 = SafeGet(fastEMA, 0) - SafeGet(fastEMA, 1);
                double fg1 = SafeGet(fastEMA, 1) - SafeGet(fastEMA, 2);
                zeroFlip = (Math.Sign(fg0) != Math.Sign(fg1));
            }
            if (zeroFlip)
                thr *= adaptiveNearZeroMultiplier;

            // Deep opposite leg context
            double maxDepth = 0.0;
            int limit = Math.Min(adaptiveLookbackBars, CurrentBar);
            for (int b = 1; b <= limit; b++)
            {
                double fg = SafeGet(fastEMA, b) - SafeGet(fastEMA, b + 1);
                double c = SafeGet(Close, b);
                double fe = SafeGet(fastEMA, b);
                bool opposite = isLong ? (fg < 0 && c < fe) : (fg > 0 && c > fe);
                if (opposite)
                {
                    double depth = isLong ? (fe - c) : (c - fe);
                    if (depth > maxDepth) maxDepth = depth;
                }
                else break;
            }
            if (maxDepth >= adaptiveDeepLegPoints)
                thr *= adaptiveDeepLegMultiplier;

            thr = Math.Max(adaptiveMinFloor, thr);
            return thr;
        }
        // Early reversal detection and entry (LONG side)
        private bool TryEarlyReversalLong(out string reason)
        {
            reason = "";
            if (!enableReversalEarlyEntry) return false;
            if (CurrentBar < reversalLookbackBars) return false;
            if (myPosition != "FLAT" || Position.MarketPosition != MarketPosition.Flat) return false;

            double fastGrad = fastEMA[0] - fastEMA[1];
            double slowGrad = slowEMA[0] - slowEMA[1];
            double accel = fastGrad - prevFastGradient;
            double closeVal = Close[0];
            double fastVal = fastEMA[0];

            // Require fast gradient positive + price above fast EMA
            if (!(fastGrad > 0 && closeVal > fastVal)) return false;

            // Acceleration requirement
            if (accel < minAccelForReversal) return false;

            // Bear context scan
            int bearCount = 0; double maxDepth = 0.0;
            int limit = Math.Min(reversalLookbackBars, CurrentBar);
            for (int b = 1; b <= limit; b++)
            {
                double fg = fastEMA[b] - fastEMA[b + 1];
                double c = Close[b];
                double fe = fastEMA[b];
                if (fg < 0 && c < fe)
                {
                    bearCount++;
                    double depth = fe - c;
                    if (depth > maxDepth) maxDepth = depth;
                }
                else break;
            }
            if (bearCount < minBearBarsForReversal) return false;
            if (maxDepth < minReversalDepthPoints) return false;

            // Filters
            double adxVal = adx != null ? adx[0] : 0.0;
            if (adxVal < minReversalAdx) return false;
            if (currentGradientStability > maxReversalStability) return false;

            // Reason string
            reason = $"FastGrad:{fastGrad:+0.000;-0.000} Accel:{accel:+0.000;-0.000} BearBars:{bearCount} Depth:{maxDepth:F1} ADX:{adxVal:F1} GradStab:{currentGradientStability:F2}";

            try
            {
                EnterLong(quantity, "EARLYREV_LONG");
                myPosition = "LONG";
                entryBar = CurrentBar;
                lastTradeBar = CurrentBar;
                entryPrice = closeVal;
                entryTime = Time[0];
                tradeMFE = 0.0; tradeMAE = 0.0;
                LogOutput($">>> EARLY REVERSAL LONG ENTRY @ {closeVal:F2} | {reason}");
                Print($"[GRAD] {Time[0]} | EARLY REVERSAL LONG ENTRY @ {closeVal:F2} | {reason}");
            }
            catch (Exception ex)
            {
                LogOutput($"EARLY REVERSAL LONG ENTRY FAILED: {ex.Message}");
                return false;
            }
            return true;
        }
        // Early reversal detection and entry (SHORT side)
        private bool TryEarlyReversalShort(out string reason)
        {
            reason = "";
            if (!enableReversalEarlyEntry) return false;
            if (CurrentBar < reversalLookbackBars) return false;
            if (myPosition != "FLAT" || Position.MarketPosition != MarketPosition.Flat) return false;

            double fastGrad = fastEMA[0] - fastEMA[1];
            double slowGrad = slowEMA[0] - slowEMA[1];
            double accel = fastGrad - prevFastGradient;
            double closeVal = Close[0];
            double fastVal = fastEMA[0];

            // Require fast gradient negative + price below fast EMA
            if (!(fastGrad < 0 && closeVal < fastVal)) return false;

            // Acceleration requirement (accelerating down)
            if (accel > -minAccelForReversal) return false;

            // Bull context scan
            int bullCount = 0; double maxDepth = 0.0;
            int limit = Math.Min(reversalLookbackBars, CurrentBar);
            for (int b = 1; b <= limit; b++)
            {
                double fg = fastEMA[b] - fastEMA[b + 1];
                double c = Close[b];
                double fe = fastEMA[b];
                if (fg > 0 && c > fe)
                {
                    bullCount++;
                    double depth = c - fe;
                    if (depth > maxDepth) maxDepth = depth;
                }
                else break;
            }
            // Reuse minimum bars threshold symmetrically
            if (bullCount < minBearBarsForReversal) return false;
            if (maxDepth < minReversalDepthPoints) return false;

            // Filters
            double adxVal = adx != null ? adx[0] : 0.0;
            if (adxVal < minReversalAdx) return false;
            if (currentGradientStability > maxReversalStability) return false;

            // Reason string
            reason = $"FastGrad:{fastGrad:+0.000;-0.000} Accel:{accel:+0.000;-0.000} BullBars:{bullCount} Depth:{maxDepth:F1} ADX:{adxVal:F1} GradStab:{currentGradientStability:F2}";

            try
            {
                EnterShort(quantity, "EARLYREV_SHORT");
                myPosition = "SHORT";
                entryBar = CurrentBar;
                lastTradeBar = CurrentBar;
                entryPrice = closeVal;
                entryTime = Time[0];
                tradeMFE = 0.0; tradeMAE = 0.0;
                LogOutput($">>> EARLY REVERSAL SHORT ENTRY @ {closeVal:F2} | {reason}");
                Print($"[GRAD] {Time[0]} | EARLY REVERSAL SHORT ENTRY @ {closeVal:F2} | {reason}");
            }
            catch (Exception ex)
            {
                LogOutput($"EARLY REVERSAL SHORT ENTRY FAILED: {ex.Message}");
                return false;
            }
            return true;
        }
        private string SaveStrategyProperties()
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8", "bin", "Custom", "strategy_logs");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"GradientSlope_PROPS_{Instrument.FullName}_{timestamp}.txt");
                using (var sw = new StreamWriter(path, false))
                {
                    sw.WriteLine($"GradientSlopeStrategy Properties Dump {DateTime.Now:O}");
                    sw.WriteLine($"Instrument={Instrument.FullName}");
                    foreach (var pi in GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        if (!pi.CanRead) continue;
                        object val = null;
                        try { val = pi.GetValue(this); } catch { }
                        if (val is Series<double>) continue; // skip dynamic series objects
                        sw.WriteLine($"{pi.Name}={val}");
                    }
                }
                Print("[GRAD] Properties saved: " + path);
                LogOutput("PROPERTIES_FILE: " + path);
                return path;
            }
            catch (Exception ex)
            {
                Print("[GRAD] Failed to save properties: " + ex.Message);
                return string.Empty;
            }
        }
        private void LoadStrategyPropertiesFromFile()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8", "bin", "Custom", "strategy_logs");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var dlg = new Microsoft.Win32.OpenFileDialog();
                dlg.InitialDirectory = dir;
                dlg.Filter = "Property Dumps (*.txt)|*.txt|All Files (*.*)|*.*";
                dlg.Title = "Select GradientSlope properties dump";
                bool? result = dlg.ShowDialog();
                if (result != true)
                {
                    UpdateDiagText("Load props cancelled.");
                    return;
                }
                int applied = 0, skipped = 0, errors = 0;
                foreach (var line in File.ReadAllLines(dlg.FileName))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (!line.Contains("=")) continue;
                    var parts = line.Split(new char[]{'='},2);
                    if (parts.Length != 2) continue;
                    string name = parts[0].Trim();
                    string sval = parts[1].Trim();
                    if (name == "GradientSlopeStrategy Properties Dump" || name == "Instrument") continue;
                    var pi = GetType().GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (pi == null || !pi.CanWrite) { skipped++; continue; }
                    if (typeof(Series<double>).IsAssignableFrom(pi.PropertyType)) { skipped++; continue; }
                    try
                    {
                        object converted;
                        if (pi.PropertyType == typeof(int)) converted = int.Parse(sval, System.Globalization.CultureInfo.InvariantCulture);
                        else if (pi.PropertyType == typeof(double)) converted = double.Parse(sval, System.Globalization.CultureInfo.InvariantCulture);
                        else if (pi.PropertyType == typeof(bool)) converted = bool.Parse(sval);
                        else if (pi.PropertyType.IsEnum) converted = Enum.Parse(pi.PropertyType, sval);
                        else converted = Convert.ChangeType(sval, pi.PropertyType, System.Globalization.CultureInfo.InvariantCulture);
                        pi.SetValue(this, converted);
                        applied++;
                    }
                    catch { errors++; }
                }
                string snapshot = SaveStrategyProperties();
                UpdateDiagText($"Loaded properties from file:\n{Path.GetFileName(dlg.FileName)}\nApplied:{applied} Skipped:{skipped} Errors:{errors}\nSnapshot:{snapshot}");
                LogOutput($"LOAD_PROPS File:{dlg.FileName} Applied:{applied} Skipped:{skipped} Errors:{errors} Snapshot:{snapshot}");
            }
            catch (Exception ex)
            {
                UpdateDiagText("Load props error: " + ex.Message);
            }
        }
        private string SaveAsStrategyTemplate(string templateName)
        {
            try
            {
                string userDir = NinjaTrader.Core.Globals.UserDataDir;
                if (string.IsNullOrEmpty(userDir))
                    userDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8");
                string dir = Path.Combine(userDir, "templates", "Strategy");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, $"{Name}_{templateName}.xml");

                var settings = new System.Xml.XmlWriterSettings { Indent = true, NewLineOnAttributes = false };
                using (var xw = System.Xml.XmlWriter.Create(file, settings))
                {
                    xw.WriteStartElement("NinjaScript");
                    xw.WriteElementString("Name", Name);
                    xw.WriteElementString("Type", "Strategy");
                    xw.WriteStartElement("Parameters");
                    var props = GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    foreach (var pi in props)
                    {
                        if (!pi.CanRead || !pi.CanWrite) continue;
                        // Only include properties explicitly marked for NinjaScript
                        var hasNsAttr = pi.GetCustomAttributes(typeof(NinjaTrader.NinjaScript.NinjaScriptPropertyAttribute), true)?.Length > 0;
                        if (!hasNsAttr) continue;
                        object val = null;
                        try { val = pi.GetValue(this); } catch { }
                        if (val is Series<double>) continue;
                        string sval = val == null ? string.Empty : Convert.ToString(val, System.Globalization.CultureInfo.InvariantCulture);
                        xw.WriteStartElement("Parameter");
                        xw.WriteAttributeString("name", pi.Name);
                        xw.WriteAttributeString("value", sval);
                        xw.WriteEndElement();
                    }
                    xw.WriteEndElement(); // Parameters
                    xw.WriteEndElement(); // NinjaScript
                }
                Print($"[GRAD] Strategy template saved: {file}");
                LogOutput("TEMPLATE_FILE: " + file);
                return file;
            }
            catch (Exception ex)
            {
                Print("[GRAD] SaveAsStrategyTemplate failed: " + ex.Message);
                return string.Empty;
            }
        }
        
        private void LogOutput(string message)
        {
            if (logWriter != null)
            {
                try
                {
                    string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                    logWriter.WriteLine($"[{timestamp}] {message}");
                }
                catch (Exception ex)
                {
                    Print($"[GRAD] Log Error: {ex.Message}");
                }
            }
        }
        
        private void LogToCSV(DateTime time, int bar, double close, double fastEma, double slowEma,
            double fastGrad, double slowGrad, string prevSignal, string newSignal, 
            string position, string action, string notes)
        {
            if (csvWriter == null) return;
            
            try
            {
                // Write header if first time
                if (!csvHeaderWritten)
                {
                    csvWriter.WriteLine("Timestamp,Bar,Close,FastEMA,SlowEMA,FastGradient,SlowGradient,PrevSignal,NewSignal,MyPosition,ActualPosition,Action,Notes,InWeakDelay,SignalStartBar,LastTradeBar,PriceGradient,BarsSinceEntry,EntryBar,EntryPrice,ExitPending,ExitPendingSide,ExitPendingAnchorFastEMA,ExitPendingEMADelta,MinHoldBars,InExitCooldown,CooldownSecsLeft,TradeMFE,TradeMAE,UnrealizedPoints,Volume,VolumePerMinute");
                    csvHeaderWritten = true;
                }
                
                // Capture actual NinjaTrader position for logging (brokerage-level position)
                string actualPos = Position.MarketPosition.ToString();

                // Derived metrics
                double priceGradient = 0.0;
                if (CurrentBar >= 1)
                    priceGradient = Close[0] - Close[1];
                int barsSinceEntry = (entryBar >= 0 && bar >= entryBar) ? (bar - entryBar) : -1;
                int cooldownSecsLeft = 0;
                if (inExitCooldown && lastExitTime != DateTime.MinValue)
                    cooldownSecsLeft = Math.Max(0, exitCooldownSeconds - (int)((time - lastExitTime).TotalSeconds));
                double exitPendingDelta = 0.0;
                if (exitPending)
                {
                    if (exitPendingSide == "LONG")
                        exitPendingDelta = exitPendingAnchorFastEMA - fastEma;
                    else if (exitPendingSide == "SHORT")
                        exitPendingDelta = fastEma - exitPendingAnchorFastEMA;
                }
                double unrealizedPts = 0.0;
                if (myPosition == "LONG") unrealizedPts = close - entryPrice;
                else if (myPosition == "SHORT") unrealizedPts = entryPrice - close;
                
                // Write data row
                double vol = Volume[0];
                double vpm = 0.0; if (CurrentBar >= 1) { double minutes = (Time[0] - Time[1]).TotalMinutes; if (minutes <= 0) minutes = 1.0; vpm = vol / minutes; } else { vpm = vol; }
                csvWriter.WriteLine(string.Format("{0:yyyy-MM-dd HH:mm:ss},{1},{2:F2},{3:F2},{4:F2},{5:F4},{6:F4},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16:F4},{17},{18},{19:F2},{20},{21},{22:F2},{23:F2},{24},{25},{26},{27:F2},{28:F2},{29:F2},{30:F0},{31:F2}",
                    time, bar, close, fastEma, slowEma, fastGrad, slowGrad, prevSignal, newSignal, position, actualPos, action, notes,
                    inWeakReversalDelay ? "1" : "0", signalStartBar, lastTradeBar,
                    priceGradient, barsSinceEntry, entryBar, entryPrice, exitPending ? 1 : 0, exitPendingSide, exitPendingAnchorFastEMA, exitPendingDelta,
                    minHoldBars, inExitCooldown ? 1 : 0, cooldownSecsLeft, tradeMFE, Math.Abs(tradeMAE), unrealizedPts, vol, vpm));
                csvWriter.Flush();
            }
            catch (Exception ex)
            {
                Print(string.Format("[GRAD] CSV Error: {0}", ex.Message));
            }
        }

        private void LogTradeSummary(DateTime exitTime, int exitBar, string direction, double exitPrice, string exitReason, double confirmDelta)
        {
            if (tradesSummaryWriter == null) return;
            try
            {
                if (!tradesSummaryHeaderWritten)
                {
                    tradesSummaryWriter.WriteLine("EntryTime,EntryBar,Direction,EntryPrice,ExitTime,ExitBar,ExitPrice,BarsHeld,RealizedPoints,MFE,MAE,ExitReason,PendingUsed,ConfirmDelta,MinHoldBars,MinEntryFastGrad,ValidationMinFastGrad,ExitConfirmFastEMADelta,FastExitThreshLong,FastExitThreshShort");
                    tradesSummaryHeaderWritten = true;
                }
                int barsHeld = (entryBar >= 0 && exitBar >= entryBar) ? (exitBar - entryBar + 1) : 0;
                double realizedPts = 0.0;
                if (direction == "LONG") realizedPts = exitPrice - entryPrice; else realizedPts = entryPrice - exitPrice;
                int pendingUsed = (confirmDelta > 0.0) ? 1 : 0;
                tradesSummaryWriter.WriteLine(string.Format("{0:yyyy-MM-dd HH:mm:ss},{1},{2},{3:F2},{4:yyyy-MM-dd HH:mm:ss},{5},{6:F2},{7},{8:F2},{9:F2},{10:F2},{11},{12},{13:F2},{14},{15:F2},{16:F2},{17:F2},{18:F2}",
                    entryTime == DateTime.MinValue ? exitTime : entryTime, entryBar, direction, entryPrice,
                    exitTime, exitBar, exitPrice, barsHeld, realizedPts, tradeMFE, Math.Abs(tradeMAE), exitReason, pendingUsed, confirmDelta,
                    minHoldBars, minEntryFastGradientAbs, validationMinFastGradientAbs, exitConfirmFastEMADelta, fastEMAGradientExitThreshold, fastEMAGradientExitThresholdShort));
                tradesSummaryWriter.Flush();
            }
            catch (Exception ex)
            {
                Print($"[GRAD] Trades Summary CSV Error: {ex.Message}");
            }
        }

        private void LogTrade(DateTime time, int bar, string action, string direction, double price, int qty, string notes)
        {
            if (tradesWriter == null) return;
            try
            {
                if (!tradesHeaderWritten)
                {
                    tradesWriter.WriteLine("Timestamp,Bar,Action,Direction,Price,Quantity,Notes");
                    tradesHeaderWritten = true;
                }
                tradesWriter.WriteLine(string.Format("{0:yyyy-MM-dd HH:mm:ss},{1},{2},{3},{4:F2},{5},{6}",
                    time, bar, action, direction, price, qty, notes));
                tradesWriter.Flush();
                // Mirror key trade events into the main .log for convenience
                LogOutput($"TRADE {action}: {direction} {qty}@{price:F2} | Bar:{bar} | {notes}");
            }
            catch (Exception ex)
            {
                Print(string.Format("[GRAD] Trades CSV Error: {0}", ex.Message));
            }
        }

        // ===== DIAGNOSIS ENGINE =====
        private class EntryDiagnosisResult
        {
            public int BarIndex;
            public int Offset;
            public bool SignalEligibleLong;
            public bool SignalEligibleShort;
            public int SignalStreakLong;
            public int SignalStreakShort;
            public double FastEMA;
            public double SlowEMA;
            public double Close;
            public double FastGrad;
            public double SlowGrad;
            public double Accel;
            public double GradStab;
            public double ATR;
            public double ADX;
            public double RSI;
            public double Bandwidth;
            public List<string> BlockersLong = new List<string>();
            public List<string> BlockersShort = new List<string>();
            public List<string> DetailLines = new List<string>();
            public List<string> SuggestedFixLines = new List<string>();
            public string HeaderLine;
        }

        private void TryCreateDiagnosisUI()
        {
            if (ChartControl == null || diagPanel != null) return;
            try
            {
                ChartControl.Dispatcher.Invoke(() =>
                {
                    diagPanel = new System.Windows.Controls.Border
                    {
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 30, 30, 30)),
                        BorderBrush = System.Windows.Media.Brushes.DimGray,
                        BorderThickness = new System.Windows.Thickness(1),
                        CornerRadius = new System.Windows.CornerRadius(3),
                        Padding = new System.Windows.Thickness(4),
                        Margin = new System.Windows.Thickness(4)
                    };
                    diagStack = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Vertical, MinWidth = 250 };
                    var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(0,0,0,4) };
                    btnDiagCurrent = new System.Windows.Controls.Button { Content = "Diag Current", Padding = new System.Windows.Thickness(4,2,4,2), Margin = new System.Windows.Thickness(0,0,4,0), FontSize = 12 };
                    btnDiagCursor = new System.Windows.Controls.Button { Content = "Diag Cursor", Padding = new System.Windows.Thickness(4,2,4,2), FontSize = 12 };
                    btnApplySave  = new System.Windows.Controls.Button { Content = "Apply+Save", Padding = new System.Windows.Thickness(4,2,4,2), Margin = new System.Windows.Thickness(4,0,0,0), FontSize = 12 };
                    btnSuggestTarget = new System.Windows.Controls.Button { Content = "Target: Auto", Padding = new System.Windows.Thickness(4,2,4,2), Margin = new System.Windows.Thickness(4,0,0,0), FontSize = 12 };
                    btnCopyClipboard = new System.Windows.Controls.Button { Content = "Copy", Padding = new System.Windows.Thickness(4,2,4,2), Margin = new System.Windows.Thickness(4,0,0,0), FontSize = 12 };
                    btnDiagToggle = new System.Windows.Controls.Button { Content = "Hide", Padding = new System.Windows.Thickness(4,2,4,2), Margin = new System.Windows.Thickness(4,0,0,0), FontSize = 12 };
                    btnForceEligible = new System.Windows.Controls.Button { Content = "ForceElig", Padding = new System.Windows.Thickness(4,2,4,2), Margin = new System.Windows.Thickness(4,0,0,0), FontSize = 12 };
                    btnSaveProps = new System.Windows.Controls.Button { Content = "SaveProps", Padding = new System.Windows.Thickness(4,2,4,2), Margin = new System.Windows.Thickness(4,0,0,0), FontSize = 12 };
                    btnLoadProps = new System.Windows.Controls.Button { Content = "LoadProps", Padding = new System.Windows.Thickness(4,2,4,2), Margin = new System.Windows.Thickness(4,0,0,0), FontSize = 12 };
                    btnDiagCurrent.Click += (s,e) => RunDiagnosisForBar(CurrentBar);
                    btnDiagCursor.Click += (s,e) => {
                        int target = lockedDiagBar >= 0 ? lockedDiagBar : lastHoverBar;
                        if (target >= 0) RunDiagnosisForBar(target);
                    };
                    btnForceEligible.Click += (s,e) => {
                        int target = lockedDiagBar >= 0 ? lockedDiagBar : (lastHoverBar >= 0 ? lastHoverBar : CurrentBar);
                        try
                        {
                            var diag = DiagnoseEntryAtBar(target);
                            string side;
                            switch (suggestionTarget)
                            {
                                case "LONG":
                                    if (forcedLongEligibility.Contains(target)) { forcedLongEligibility.Remove(target); side = "LONG (removed)"; }
                                    else { forcedLongEligibility.Add(target); side = "LONG (added)"; }
                                    break;
                                case "SHORT":
                                    if (forcedShortEligibility.Contains(target)) { forcedShortEligibility.Remove(target); side = "SHORT (removed)"; }
                                    else { forcedShortEligibility.Add(target); side = "SHORT (added)"; }
                                    break;
                                case "EXIT":
                                    side = "EXIT (no force)"; // do nothing
                                    break;
                                case "AUTO":
                                default:
                                    // AUTO: keep heuristic based on fewer blockers
                                    bool preferLong = diag.BlockersLong.Count <= diag.BlockersShort.Count;
                                    if (preferLong)
                                    {
                                        if (forcedLongEligibility.Contains(target)) { forcedLongEligibility.Remove(target); side = "AUTO->LONG (removed)"; }
                                        else { forcedLongEligibility.Add(target); side = "AUTO->LONG (added)"; }
                                    }
                                    else
                                    {
                                        if (forcedShortEligibility.Contains(target)) { forcedShortEligibility.Remove(target); side = "AUTO->SHORT (removed)"; }
                                        else { forcedShortEligibility.Add(target); side = "AUTO->SHORT (added)"; }
                                    }
                                    break;
                            }
                            // Re-run diagnosis to reflect forced state
                            diag = DiagnoseEntryAtBar(target);
                            UpdateDiagText($"ForceElig target={suggestionTarget} -> {side}\n" + diag.HeaderLine + "\n" + string.Join("\n", diag.DetailLines));
                        }
                        catch (Exception ex)
                        {
                            UpdateDiagText("ForceElig error: " + ex.Message);
                        }
                    };
                    btnApplySave.Click += (s,e) => {
                        int target = lockedDiagBar >= 0 ? lockedDiagBar : (lastHoverBar >= 0 ? lastHoverBar : CurrentBar);
                        try
                        {
                            var diag = DiagnoseEntryAtBar(target);
                            ApplyDiagnosisSuggestions(diag);
                            SaveStrategyProperties();
                            string tname = $"Diag_{target}_{DateTime.Now:yyyyMMdd_HHmmss}";
                            string path = SaveAsStrategyTemplate(tname);
                            UpdateDiagText($"Applied suggestions and saved template:\n{path}\n\n" + diag.HeaderLine);
                        }
                        catch (Exception ex)
                        {
                            UpdateDiagText("Apply+Save error: " + ex.Message);
                        }
                    };
                    btnSaveProps.Click += (s,e) => {
                        try
                        {
                            string path = SaveStrategyProperties();
                            UpdateDiagText("Properties saved -> \n" + path);
                        }
                        catch (Exception ex)
                        {
                            UpdateDiagText("Save props error: " + ex.Message);
                        }
                    };
                    btnLoadProps.Click += (s,e) => {
                        LoadStrategyPropertiesFromFile();
                    };
                    btnCopyClipboard.Click += (s,e) => {
                        try
                        {
                            string text = diagText != null ? diagText.Text : string.Empty;
                            if (string.IsNullOrWhiteSpace(text)) text = "(No diagnosis text)";
                            System.Windows.Clipboard.SetText(text);
                            if (hoverDebug) Print("[GRAD][DIAG] Copied diagnosis text to clipboard.");
                        }
                        catch (Exception ex)
                        {
                            if (hoverDebug) Print("[GRAD][DIAG] Clipboard copy failed: " + ex.Message);
                        }
                    };
                    btnSuggestTarget.Click += (s,e) => {
                        // Cycle: AUTO -> LONG -> SHORT -> EXIT -> AUTO
                        suggestionTarget = suggestionTarget == "AUTO" ? "LONG" : suggestionTarget == "LONG" ? "SHORT" : suggestionTarget == "SHORT" ? "EXIT" : "AUTO";
                        string label = suggestionTarget.Substring(0,1) + suggestionTarget.Substring(1).ToLower();
                        (btnSuggestTarget.Content) = $"Target: {label}";
                    };
                    btnDiagToggle.Click += (s,e) => {
                        if (!diagCollapsed)
                        {
                            diagText.Visibility = System.Windows.Visibility.Collapsed;
                            hoverInfoText.Visibility = System.Windows.Visibility.Collapsed;
                            btnDiagToggle.Content = "Show";
                            diagCollapsed = true;
                        }
                        else
                        {
                            hoverInfoText.Visibility = System.Windows.Visibility.Visible;
                            diagText.Visibility = System.Windows.Visibility.Visible;
                            btnDiagToggle.Content = "Hide";
                            diagCollapsed = false;
                        }
                    };
                    btnPanel.Children.Add(btnDiagCurrent);
                    btnPanel.Children.Add(btnDiagCursor);
                    btnPanel.Children.Add(btnApplySave);
                    btnPanel.Children.Add(btnSuggestTarget);
                    btnPanel.Children.Add(btnCopyClipboard);
                    btnPanel.Children.Add(btnForceEligible);
                    btnPanel.Children.Add(btnSaveProps);
                    btnPanel.Children.Add(btnLoadProps);
                    btnPanel.Children.Add(btnDiagToggle);
                    // Live hover info line
                    hoverInfoText = new System.Windows.Controls.TextBlock { Text = "Hover: -", Foreground = System.Windows.Media.Brushes.Gainsboro, FontSize = 13, Margin = new System.Windows.Thickness(0,0,0,3) };
                    diagText = new System.Windows.Controls.TextBlock { Text = "Diagnosis panel ready.", Foreground = System.Windows.Media.Brushes.LightGray, FontSize = 13, TextWrapping = System.Windows.TextWrapping.Wrap };
                    diagStack.Children.Add(btnPanel);
                    diagStack.Children.Add(hoverInfoText);
                    diagStack.Children.Add(diagText);
                    diagPanel.Child = diagStack;
                    // Attach to ChartControl root grid
                    var root = ChartControl.Parent as System.Windows.FrameworkElement;
                    if (root is System.Windows.Controls.Grid grid)
                    {
                        grid.Children.Add(diagPanel);
                        System.Windows.Controls.Grid.SetRow(diagPanel, 0);
                        System.Windows.Controls.Grid.SetColumn(diagPanel, 0);
                        diagPanel.VerticalAlignment = System.Windows.VerticalAlignment.Top;
                        diagPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
                    }
                    // Hook mouse events for cursor bar tracking (capture even when handled by overlays)
                    ChartControl.AddHandler(System.Windows.UIElement.MouseMoveEvent,
                        new System.Windows.Input.MouseEventHandler(ChartControl_MouseMove), true);
                    ChartControl.AddHandler(System.Windows.UIElement.MouseDownEvent,
                        new System.Windows.Input.MouseButtonEventHandler(ChartControl_MouseDown), true); // for lock/unlock
                });
            }
            catch (Exception ex)
            {
                Print("[GRAD] Diagnosis UI creation failed: " + ex.Message);
            }
        }

        private void TryRemoveDiagnosisUI()
        {
            if (ChartControl == null || diagPanel == null) return;
            try
            {
                ChartControl.Dispatcher.Invoke(() =>
                {
                    ChartControl.RemoveHandler(System.Windows.UIElement.MouseMoveEvent,
                        new System.Windows.Input.MouseEventHandler(ChartControl_MouseMove));
                    ChartControl.RemoveHandler(System.Windows.UIElement.MouseDownEvent,
                        new System.Windows.Input.MouseButtonEventHandler(ChartControl_MouseDown));
                    var root = ChartControl.Parent as System.Windows.FrameworkElement;
                    if (root is System.Windows.Controls.Grid grid)
                    {
                        grid.Children.Remove(diagPanel);
                    }
                });
            }
            catch { }
            finally
            {
                diagPanel = null; diagStack = null; diagText = null; btnDiagCurrent = null; btnDiagCursor = null;
            }
        }

        private void ChartControl_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                // Use price panel-relative coordinates (ChartPanels[0]) to align with GetXByBarIndex bar centers
                System.Windows.FrameworkElement pricePanel = null;
                if (ChartControl != null && ChartControl.ChartPanels.Count > 0)
                    pricePanel = ChartControl.ChartPanels[0] as System.Windows.FrameworkElement;
                var posPanel = pricePanel != null ? e.GetPosition(pricePanel) : e.GetPosition(ChartControl);
                double panelX = posPanel.X;
                // Stabilize: only react to meaningful horizontal movement (>=1px) to avoid vertical jitter changing mapping
                if (double.IsNaN(lastHoverX)) lastHoverX = panelX;
                double deltaX = Math.Abs(panelX - lastHoverX);
                bool shouldUpdate = deltaX >= 1.0; // threshold

                if (lockedDiagBar >= 0)
                {
                    if (hoverInfoText != null)
                        hoverInfoText.Text = $"Hover: {lastHoverBar} (LOCKED:{lockedDiagBar})";
                    if (shouldUpdate) lastHoverX = panelX; // advance baseline without changing locked bar
                    UpdateHoverIndexHud();
                }
                else if (shouldUpdate)
                {
                    int idx = GetBarIndexFromMouseX(panelX, pricePanel);
                    if (idx != lastHoverBar)
                    {
                        lastHoverBar = idx;
                        lastHoverX = panelX;
                        if (hoverInfoText != null)
                            hoverInfoText.Text = $"Hover: {lastHoverBar}";
                        if (hoverDebug)
                            Print($"[GRAD][HOVER] panelX={panelX:F1} dX={deltaX:F1} -> Bar {lastHoverBar}");
                        UpdateHoverIndexHud();
                    }
                }
            }
            catch { }
        }

        private void ChartControl_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                {
                    // Lock current hover bar
                    if (lastHoverBar >= 0)
                    {
                        lockedDiagBar = lastHoverBar;
                        if (hoverInfoText != null)
                            hoverInfoText.Text = $"Hover: {lastHoverBar} (LOCKED:{lockedDiagBar})";
                        if (hoverDebug)
                            Print($"[GRAD][HOVER-LOCK] Locked bar {lockedDiagBar}");
                        UpdateHoverIndexHud();
                    }
                }
                else if (e.ChangedButton == System.Windows.Input.MouseButton.Right)
                {
                    // Unlock
                    int prev = lockedDiagBar;
                    lockedDiagBar = -1;
                    if (hoverInfoText != null)
                        hoverInfoText.Text = $"Hover: {lastHoverBar}";
                    if (hoverDebug && prev >= 0)
                        Print($"[GRAD][HOVER-LOCK] Unlocked bar {prev}");
                    UpdateHoverIndexHud();
                }
            }
            catch { }
        }

        private void UpdateHoverIndexHud()
        {
            try
            {
                string text = (lockedDiagBar >= 0)
                    ? $"Hover: {lastHoverBar} (LOCKED:{lockedDiagBar})"
                    : $"Hover: {lastHoverBar}";
                Draw.TextFixed(this, "HOVER_BAR_HUD", text, TextPosition.TopRight);
            }
            catch { }
        }

        private int GetBarIndexFromMouseX(double x, System.Windows.FrameworkElement panel)
        {
            // Proportional mapping based on panel-relative X against visible bar range
            try
            {
                if (ChartControl == null || ChartBars == null)
                    return -1;

                int firstVisible = ChartBars.FromIndex;
                int lastVisible = ChartBars.ToIndex;
                if (firstVisible < 0) firstVisible = 0;
                if (lastVisible < firstVisible) lastVisible = firstVisible;

                double width = 0.0;
                if (panel != null)
                    width = panel.ActualWidth;
                if (width <= 0 && ChartControl != null)
                    width = ChartControl.ActualWidth;
                if (width <= 0)
                    return firstVisible;

                if (x < 0) x = 0;
                if (x > width) x = width;

                int count = lastVisible - firstVisible;
                if (count <= 0)
                    return firstVisible;

                double t = x / width; // 0..1
                int mapped = firstVisible + (int)Math.Round(t * count);
                if (mapped < firstVisible) mapped = firstVisible;
                if (mapped > lastVisible) mapped = lastVisible;

                if (hoverDebug)
                    Print($"[GRAD][HOVER-MAP-PROP] x={x:F1}/{width:F1} first={firstVisible} last={lastVisible} mapped={mapped}");
                return mapped;
            }
            catch { return -1; }
        }

        private void RunDiagnosisForBar(int barIndex)
        {
            if (barIndex < 0 || barIndex > CurrentBar)
            {
                UpdateDiagText($"Bar {barIndex} out of range (0..{CurrentBar}).");
                return;
            }
            try
            {
                // Ensure series/indicator reads occur on the NinjaScript thread
                TriggerCustomEvent(stateObj =>
                {
                    try
                    {
                        int idx = barIndex;
                        var diag = DiagnoseEntryAtBar(idx);
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine(diag.HeaderLine);
                        foreach (var line in diag.DetailLines)
                            sb.AppendLine(line);
                        UpdateDiagText(sb.ToString());
                        // Emit to local dashboard
                        SendDiagnosisToWeb(diag, idx);
                    }
                    catch (Exception exInner)
                    {
                        UpdateDiagText("Diagnosis error: " + exInner.Message);
                    }
                }, null);
            }
            catch (Exception ex)
            {
                UpdateDiagText("Diagnosis scheduling error: " + ex.Message);
            }
        }

        private static System.Net.Http.HttpClient sharedClient;
        private static readonly object clientLock = new object();
        private int diagSentCount = 0;
        private int diagFailCount = 0;
        private readonly System.Collections.Generic.List<string> compactBuffer = new System.Collections.Generic.List<string>(64);
        private DateTime lastCompactFlush = DateTime.MinValue;

        [Display(Name = "EnableDiagLogging", Order = 990)]
        public bool EnableDiagLogging { get; set; } = true;

        private void EnsureHttpClient()
        {
            if (sharedClient == null)
            {
                lock (clientLock)
                {
                    if (sharedClient == null)
                    {
                        sharedClient = new System.Net.Http.HttpClient();
                        sharedClient.Timeout = TimeSpan.FromMilliseconds(300);
                        sharedClient.DefaultRequestHeaders.ConnectionClose = false;
                    }
                }
            }
        }

        private void SendDiagnosisToWeb(dynamic diag, int idx)
        {
            try
            {
                EnsureHttpClient();
                double fe = fastEMA != null ? fastEMA[idx] : 0.0;
                double se = slowEMA != null ? slowEMA[idx] : 0.0;
                double fg = 0.0, sg = 0.0;
                // Attempt to parse from diag header lines when available
                // Fallback: use current computed values
                fg = currentFastGradientAcceleration + prevFastGradient; // rough placeholder if needed
                string timeIso = Time[idx].ToString("o");
                double close = Close[idx];
                double adxVal = adx != null ? adx[idx] : 0.0;
                double bw = Math.Abs(fe - se) / (se != 0 ? se : 1.0);
                string blockersLong = string.Join("|", diag.BlockersLong);
                string blockersShort = string.Join("|", diag.BlockersShort);
                // Build JSON manually
                string json = "{" +
                    "\"barIndex\":" + idx + "," +
                    "\"time\":\"" + timeIso + "\"," +
                    "\"close\":" + close.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    "\"fastEMA\":" + fe.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    "\"slowEMA\":" + se.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    "\"fastGrad\":" + fg.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    "\"slowGrad\":" + sg.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    "\"adx\":" + adxVal.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    "\"bandwidth\":" + bw.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    "\"blockersLong\":\"" + blockersLong.Replace("\"","'") + "\"," +
                    "\"blockersShort\":\"" + blockersShort.Replace("\"","'") + "\"" +
                    "}";
                var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var cts = new System.Threading.CancellationTokenSource(300);
                sharedClient.PostAsync("http://127.0.0.1:5001/diag", content, cts.Token)
                    .ContinueWith(t =>
                    {
                        if (t.IsCompleted && !t.IsFaulted && !t.IsCanceled && t.Result.IsSuccessStatusCode)
                        {
                            diagSentCount++;
                            if (EnableDiagLogging && (diagSentCount % 50 == 0))
                                Print($"[GRAD] Diag sent ok. total={diagSentCount}");
                        }
                        else
                        {
                            diagFailCount++;
                            if (EnableDiagLogging && (diagFailCount % 10 == 0))
                            {
                                string msg = t.IsFaulted ? (t.Exception?.GetBaseException()?.Message ?? "faulted") : ($"HTTP {(int)(t.Result?.StatusCode ?? 0)}");
                                Print($"[GRAD] Diag send fail. totalFail={diagFailCount} msg={msg}");
                            }
                        }
                    });
            }
            catch (Exception ex)
            {
                diagFailCount++;
                if (EnableDiagLogging)
                    Print("[GRAD] Diag send exception: " + ex.Message);
            }
        }

        private void StreamCompactDiagnosis()
        {
            if (!streamBarDiagnostics) return;
            try
            {
                int idx = CurrentBar;
                double fe = fastEMA != null ? fastEMA[0] : 0.0;
                double se = slowEMA != null ? slowEMA[0] : 0.0;
                double fg = fe - (fastEMA != null && CurrentBar >= 1 ? fastEMA[1] : fe);
                double sg = se - (slowEMA != null && CurrentBar >= 1 ? slowEMA[1] : se);
                double accelVal = fg - (fastEMA != null && CurrentBar >= 1 ? (fastEMA[0] - fastEMA[1]) - (CurrentBar >= 2 ? (fastEMA[1] - fastEMA[2]) : 0) : 0);
                double gradStabVal = Math.Abs(fg - sg);
                double adxVal = adx != null ? adx[0] : 0.0;
                double atrVal = atr != null ? atr[0] : 0.0;
                double rsiVal = rsi != null ? rsi[0] : 0.0;
                double bw = Math.Abs(fe - se) / (se != 0 ? se : 1.0);
                string sig = currentSignal;
                
                // Include blockers info
                var blockersL = new System.Collections.Generic.List<string>();
                var blockersS = new System.Collections.Generic.List<string>();
                // Quick lightweight blocker check (don't repeat full logic, just key ones)
                if (Math.Abs(fg) < minEntryFastGradientAbs) { blockersL.Add("FastGradMin"); blockersS.Add("FastGradMin"); }
                if (requireAccelAlignment && fg * accelVal < 0) blockersL.Add("AccelAlign");
                if (requireAccelAlignment && fg * accelVal > 0) blockersS.Add("AccelAlign");
                if (adxVal < minAdxForEntry) { blockersL.Add("ADXMin"); blockersS.Add("ADXMin"); }
                if (bw > maxBandwidthForEntry) { blockersL.Add("Bandwidth"); blockersS.Add("Bandwidth"); }
                
                string json = "{" +
                    "\"barIndex\":" + idx + "," +
                    "\"time\":\"" + Time[0].ToString("o") + "\"," +
                    "\"close\":" + Close[0].ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    "\"fastEMA\":" + fe.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    "\"slowEMA\":" + se.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    "\"fastGrad\":" + fg.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    "\"slowGrad\":" + sg.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    "\"accel\":" + accelVal.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    "\"gradStab\":" + gradStabVal.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    "\"adx\":" + adxVal.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    "\"atr\":" + atrVal.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    "\"rsi\":" + rsiVal.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    "\"bandwidth\":" + bw.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    "\"signal\":\"" + sig + "\"," +
                    "\"blockersLong\":[" + string.Join(",", blockersL.Select(b => "\"" + b + "\"")) + "]," +
                    "\"blockersShort\":[" + string.Join(",", blockersS.Select(b => "\"" + b + "\"")) + "]" +
                    "}";
                // Buffer compact diags and flush in batches to reduce connections
                compactBuffer.Add(json);
                var now = DateTime.UtcNow;
                bool timeDue = (lastCompactFlush == DateTime.MinValue) || ((now - lastCompactFlush).TotalMilliseconds >= 1000);
                if (compactBuffer.Count >= 20 || timeDue)
                {
                    FlushCompactBuffer();
                    lastCompactFlush = now;
                }
            }
            catch (Exception ex)
            {
                diagFailCount++;
                if (EnableDiagLogging)
                    Print("[GRAD] Compact diag exception: " + ex.Message);
            }
        }

        private void FlushCompactBuffer()
        {
            if (compactBuffer.Count == 0) return;
            try
            {
                EnsureHttpClient();
                string payload = "[" + string.Join(",", compactBuffer) + "]";
                compactBuffer.Clear();
                var content = new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                var cts = new System.Threading.CancellationTokenSource(400);
                sharedClient.PostAsync("http://127.0.0.1:5001/diag", content, cts.Token)
                    .ContinueWith(t =>
                    {
                        if (!(t.IsCompleted && !t.IsFaulted && !t.IsCanceled) || !(t.Result?.IsSuccessStatusCode ?? false))
                        {
                            diagFailCount++;
                            if (EnableDiagLogging && (diagFailCount % 10 == 0))
                            {
                                string msg = t.IsFaulted ? (t.Exception?.GetBaseException()?.Message ?? "faulted") : ($"HTTP {(int)(t.Result?.StatusCode ?? 0)}");
                                Print($"[GRAD] Batch diag send fail. totalFail={diagFailCount} msg={msg}");
                            }
                        }
                        else
                        {
                            diagSentCount++;
                            if (EnableDiagLogging && (diagSentCount % 10 == 0))
                                Print($"[GRAD] Batch diag sent ok. total={diagSentCount}");
                        }
                    });
            }
            catch (Exception ex)
            {
                diagFailCount++;
                if (EnableDiagLogging)
                    Print("[GRAD] Batch diag exception: " + ex.Message);
            }
        }

        private void UpdateDiagText(string text)
        {
            if (diagText == null)
            {
                Print("[GRAD] DIAG: " + text);
                return;
            }
            try
            {
                ChartControl.Dispatcher.Invoke(() => { diagText.Text = text; });
            }
            catch { }
        }

        private double GetSeriesValueAtAbsolute(ISeries<double> series, int absoluteIndex)
        {
            if (series == null) return double.NaN;
            int barsAgo = CurrentBar - absoluteIndex;
            if (barsAgo < 0) return double.NaN;
            try { return series[barsAgo]; } catch { return double.NaN; }
        }
        private DateTime GetTimeAtAbsolute(int absoluteIndex)
        {
            int barsAgo = CurrentBar - absoluteIndex;
            if (barsAgo < 0) return DateTime.MinValue;
            try { return Time[barsAgo]; } catch { return DateTime.MinValue; }
        }

        private EntryDiagnosisResult DiagnoseEntryAtBar(int barIndex)
        {
            if (barIndex < 0)
                throw new ArgumentException("Bar index must be >= 0");
            if (CurrentBar < BarsRequiredToTrade)
                throw new InvalidOperationException("Not enough bars to diagnose.");
            if (barIndex > CurrentBar)
                throw new ArgumentException($"Bar {barIndex} is in the future relative to CurrentBar {CurrentBar}.");

            var res = new EntryDiagnosisResult { BarIndex = barIndex, Offset = CurrentBar - barIndex };

            // Absolute value retrieval (independent of lookback) with fallback to barsAgo indexing
            int barsAgoIdx = res.Offset;
            double fast = GetSeriesValueAtAbsolute(fastEMA, barIndex);
            if (double.IsNaN(fast)) fast = SafeGet(fastEMA, barsAgoIdx);
            double slow = GetSeriesValueAtAbsolute(slowEMA, barIndex);
            if (double.IsNaN(slow)) slow = SafeGet(slowEMA, barsAgoIdx);
            double close = GetSeriesValueAtAbsolute(Close, barIndex);
            if (double.IsNaN(close)) close = SafeGet(Close, barsAgoIdx);
            double fastPrev = barIndex > 0 ? GetSeriesValueAtAbsolute(fastEMA, barIndex - 1) : fast;
            if (double.IsNaN(fastPrev)) fastPrev = SafeGet(fastEMA, barsAgoIdx + 1);
            double slowPrev = barIndex > 0 ? GetSeriesValueAtAbsolute(slowEMA, barIndex - 1) : slow;
            if (double.IsNaN(slowPrev)) slowPrev = SafeGet(slowEMA, barsAgoIdx + 1);
            double fastPrev2 = barIndex > 1 ? GetSeriesValueAtAbsolute(fastEMA, barIndex - 2) : fastPrev;
            if (double.IsNaN(fastPrev2)) fastPrev2 = SafeGet(fastEMA, barsAgoIdx + 2);

            bool fastPrevInvalid = double.IsNaN(fastPrev) || (fastPrev == 0.0 && fast != 0.0);
            bool slowPrevInvalid = double.IsNaN(slowPrev) || (slowPrev == 0.0 && slow != 0.0);
            if (fastPrevInvalid) fastPrev = fast;
            if (slowPrevInvalid) slowPrev = slow;

            double fastGrad = (!double.IsNaN(fast) && !double.IsNaN(fastPrev)) ? (fast - fastPrev) : 0.0;
            double slowGrad = (!double.IsNaN(slow) && !double.IsNaN(slowPrev)) ? (slow - slowPrev) : 0.0;
            double prevFastGrad = (!double.IsNaN(fastPrev) && !double.IsNaN(fastPrev2)) ? (fastPrev - fastPrev2) : fastGrad;
            double accel = fastGrad - prevFastGrad;

            double atrVal = atr != null ? GetSeriesValueAtAbsolute(atr, barIndex) : 0.0;
            if (double.IsNaN(atrVal) && atr != null) atrVal = SafeGet(atr, barsAgoIdx);
            double adxVal = adx != null ? GetSeriesValueAtAbsolute(adx, barIndex) : 0.0;
            if (double.IsNaN(adxVal) && adx != null) adxVal = SafeGet(adx, barsAgoIdx);
            double rsiVal = rsi != null ? GetSeriesValueAtAbsolute(rsi, barIndex) : 0.0;
            if (double.IsNaN(rsiVal) && rsi != null) rsiVal = SafeGet(rsi, barsAgoIdx);
            double bandwidth = !double.IsNaN(slow) && Math.Abs(slow) > 0 ? Math.Abs(fast - slow) / Math.Abs(slow) : 0.0;

            double gradStab = ComputeFastGradientStdDev(CurrentBar - barIndex, gradientStabilityPeriod);

            res.FastEMA = fast;
            res.SlowEMA = slow;
            res.Close = close;
            res.FastGrad = fastGrad;
            res.SlowGrad = slowGrad;
            res.Accel = accel;
            res.GradStab = gradStab;
            res.ATR = atrVal;
            res.ADX = adxVal;
            res.RSI = rsiVal;
            res.Bandwidth = bandwidth;

            if (barIndex < BarsRequiredToTrade + initialBarsWait)
            {
                res.BlockersLong.Add($"InitialBarsWait: bar<{BarsRequiredToTrade + initialBarsWait}");
                res.BlockersShort.Add($"InitialBarsWait: bar<{BarsRequiredToTrade + initialBarsWait}");
            }

            int barsAgo = res.Offset;
            int streakLong = CountConsecutiveDirectionalBars(barsAgo, true);
            int streakShort = CountConsecutiveDirectionalBars(barsAgo, false);
            res.SignalStreakLong = streakLong;
            res.SignalStreakShort = streakShort;
            res.SignalEligibleLong = streakLong >= entryBarDelay;
            res.SignalEligibleShort = streakShort >= entryBarDelay;

            bool priceAboveEMAs = close > fast && close > slow;
            bool priceBelowEMAs = close < fast && close < slow;
            double diagThrLong = GetEntryFastGradientThreshold(true);
            double diagThrShort = GetEntryFastGradientThreshold(false);
            bool fastStrongForEntryLong = Math.Abs(fastGrad) >= diagThrLong && fastGrad > 0;
            bool fastStrongForEntryShort = Math.Abs(fastGrad) >= diagThrShort && fastGrad < 0;
            bool notOverextended = Math.Abs(fastGrad) <= maxEntryFastGradientAbs;

            if (!res.SignalEligibleLong) res.BlockersLong.Add($"EntryBarDelay: streak={streakLong} < {entryBarDelay}");
            if (!(fastGrad > 0 && slowGrad > 0)) res.BlockersLong.Add("GradientDirection: fast>0 && slow>0 required");
            if (!priceAboveEMAs) res.BlockersLong.Add("PricePosition: Close>FastEMA && Close>SlowEMA required");
            if (!fastStrongForEntryLong) res.BlockersLong.Add($"FastGradMin: |fastGrad|={Math.Abs(fastGrad):F4} < EntryThr={diagThrLong:F4}{(enableAdaptiveEntryGradient?" (adaptive)":"")}");
            if (!notOverextended) res.BlockersLong.Add($"FastGradMax: |fastGrad|={Math.Abs(fastGrad):F4} > MaxEntryFastGradientAbs={maxEntryFastGradientAbs:F4}");

            if (!disableEntryFilters)
            {
                if (adxVal < minAdxForEntry) res.BlockersLong.Add($"ADX: {adxVal:F2} < MinAdxForEntry={minAdxForEntry:F2}");
                if (gradStab > maxGradientStabilityForEntry) res.BlockersLong.Add($"GradStab: {gradStab:F4} > MaxGradientStabilityForEntry={maxGradientStabilityForEntry:F4}");
                if (bandwidth < minBandwidthForEntry || bandwidth > maxBandwidthForEntry) res.BlockersLong.Add($"Bandwidth: {bandwidth:F4} not in [{minBandwidthForEntry:F3},{maxBandwidthForEntry:F3}]");
                if (requireAccelAlignment && fastGrad > 0 && accel < 0) res.BlockersLong.Add($"AccelAlign: accel {accel:F4} opposes fastGrad");
                if (enableATRCap && atrVal > maxATRForEntry) res.BlockersLong.Add($"ATR: {atrVal:F2} > MaxATRForEntry={maxATRForEntry:F2}");
                if (enableRSIFloor && rsiVal < minRSIForEntry) res.BlockersLong.Add($"RSI: {rsiVal:F1} < MinRSIForEntry={minRSIForEntry:F1}");
            }

            if (!res.SignalEligibleShort) res.BlockersShort.Add($"EntryBarDelay: streak={streakShort} < {entryBarDelay}");
            if (!(fastGrad < 0 && slowGrad < 0)) res.BlockersShort.Add("GradientDirection: fast<0 && slow<0 required");
            if (!priceBelowEMAs) res.BlockersShort.Add("PricePosition: Close<FastEMA && Close<SlowEMA required");
            if (!fastStrongForEntryShort) res.BlockersShort.Add($"FastGradMin: |fastGrad|={Math.Abs(fastGrad):F4} < EntryThr={diagThrShort:F4}{(enableAdaptiveEntryGradient?" (adaptive)":"")}");
            if (!notOverextended) res.BlockersShort.Add($"FastGradMax: |fastGrad|={Math.Abs(fastGrad):F4} > MaxEntryFastGradientAbs={maxEntryFastGradientAbs:F4}");

            if (!disableEntryFilters)
            {
                if (adxVal < minAdxForEntry) res.BlockersShort.Add($"ADX: {adxVal:F2} < MinAdxForEntry={minAdxForEntry:F2}");
                if (gradStab > maxGradientStabilityForEntry) res.BlockersShort.Add($"GradStab: {gradStab:F4} > MaxGradientStabilityForEntry={maxGradientStabilityForEntry:F4}");
                if (bandwidth < minBandwidthForEntry || bandwidth > maxBandwidthForEntry) res.BlockersShort.Add($"Bandwidth: {bandwidth:F4} not in [{minBandwidthForEntry:F3},{maxBandwidthForEntry:F3}]");
                if (requireAccelAlignment && fastGrad < 0 && accel > 0) res.BlockersShort.Add($"AccelAlign: accel {accel:F4} opposes fastGrad");
                if (enableATRCap && atrVal > maxATRForEntry) res.BlockersShort.Add($"ATR: {atrVal:F2} > MaxATRForEntry={maxATRForEntry:F2}");
                if (enableRSIFloor && rsiVal < minRSIForEntry) res.BlockersShort.Add($"RSI: {rsiVal:F1} < MinRSIForEntry={minRSIForEntry:F1}");
            }

            DateTime t = GetTimeAtAbsolute(barIndex);
            if (t == DateTime.MinValue) t = SafeTimeAt(barsAgoIdx);
            // Apply forced eligibility overrides (clear blockers for chosen side)
            if (forcedLongEligibility.Contains(barIndex))
            {
                res.BlockersLong.Clear();
                res.DetailLines.Add("(FORCED LONG ELIGIBLE) Overrides all Long blockers") ;
            }
            if (forcedShortEligibility.Contains(barIndex))
            {
                res.BlockersShort.Clear();
                res.DetailLines.Add("(FORCED SHORT ELIGIBLE) Overrides all Short blockers") ;
            }
            res.HeaderLine = $"DIAG_BAR {barIndex} @ {(t==DateTime.MinValue?"N/A":t.ToString())} | Close={(double.IsNaN(close)?0.0:close):F2} FastEMA={(double.IsNaN(fast)?0.0:fast):F2} SlowEMA={(double.IsNaN(slow)?0.0:slow):F2} FastGrad={fastGrad:F4} SlowGrad={slowGrad:F4} Accel={accel:F4} GradStab={gradStab:F4} ADX={(double.IsNaN(adxVal)?0.0:adxVal):F2} ATR={(double.IsNaN(atrVal)?0.0:atrVal):F2} RSI={(double.IsNaN(rsiVal)?0.0:rsiVal):F1} BW={bandwidth:F4}";
            res.DetailLines.Add($"- EntryBarDelay: required={entryBarDelay}, streakLong={streakLong}, streakShort={streakShort}");
            res.DetailLines.Add($"- LongEligible={(res.BlockersLong.Count==0 ? "YES" : "NO")} | Blockers: {(res.BlockersLong.Count==0?"None":string.Join("; ", res.BlockersLong))}");
            res.DetailLines.Add($"- ShortEligible={(res.BlockersShort.Count==0 ? "YES" : "NO")} | Blockers: {(res.BlockersShort.Count==0?"None":string.Join("; ", res.BlockersShort))}");

            BuildSuggestions(res);
            foreach (var s in res.SuggestedFixLines) res.DetailLines.Add(s);
            return res;
        }

        private void BuildSuggestions(EntryDiagnosisResult res)
        {
            const double eps = 1e-4;
            var sugg = res.SuggestedFixLines;
            int longFail = res.BlockersLong.Count;
            int shortFail = res.BlockersShort.Count;
            bool forceLong = suggestionTarget == "LONG";
            bool forceShort = suggestionTarget == "SHORT";
            bool doExit     = suggestionTarget == "EXIT";

            if (doExit)
            {
                BuildExitSuggestions(res);
                return;
            }

            bool targetLong = forceLong ? true : (forceShort ? false : (longFail <= shortFail));
            sugg.Add($"SUGGESTIONS targeting {(targetLong?"LONG":"SHORT")}: minimal parameter changes to permit entry if rerun");
            int streak = targetLong ? res.SignalStreakLong : res.SignalStreakShort;
            if (streak < entryBarDelay)
                sugg.Add($"- Set EntryBarDelay from {entryBarDelay} -> {Math.Max(1, streak)} (match available streak)");
            if (targetLong)
            {
                if (!(res.FastGrad > 0 && res.SlowGrad > 0))
                    sugg.Add("- Direction: Gradients must both be >0; cannot auto-fix via params");
                if (!(res.Close > res.FastEMA && res.Close > res.SlowEMA))
                    sugg.Add("- Position: Price must be above both EMAs; cannot auto-fix via params");
                if (Math.Abs(res.FastGrad) < minEntryFastGradientAbs)
                    sugg.Add($"- Lower MinEntryFastGradientAbs {minEntryFastGradientAbs:F4} -> {(Math.Abs(res.FastGrad)+eps):F4}");
            }
            else
            {
                if (!(res.FastGrad < 0 && res.SlowGrad < 0))
                    sugg.Add("- Direction: Gradients must both be <0; cannot auto-fix via params");
                if (!(res.Close < res.FastEMA && res.Close < res.SlowEMA))
                    sugg.Add("- Position: Price must be below both EMAs; cannot auto-fix via params");
                if (Math.Abs(res.FastGrad) < minEntryFastGradientAbs)
                    sugg.Add($"- Lower MinEntryFastGradientAbs {minEntryFastGradientAbs:F4} -> {(Math.Abs(res.FastGrad)+eps):F4}");
            }
            if (Math.Abs(res.FastGrad) > maxEntryFastGradientAbs)
                sugg.Add($"- Raise MaxEntryFastGradientAbs {maxEntryFastGradientAbs:F4} -> {(Math.Abs(res.FastGrad)+eps):F4}");
            if (!disableEntryFilters)
            {
                if (res.ADX < minAdxForEntry) sugg.Add($"- Lower MinAdxForEntry {minAdxForEntry:F2} -> {(res.ADX+eps):F2} OR set DisableEntryFilters=true");
                if (res.GradStab > maxGradientStabilityForEntry) sugg.Add($"- Raise MaxGradientStabilityForEntry {maxGradientStabilityForEntry:F4} -> {(res.GradStab+eps):F4} OR set DisableEntryFilters=true");
                if (res.Bandwidth < minBandwidthForEntry) sugg.Add($"- Lower MinBandwidthForEntry {minBandwidthForEntry:F3} -> {(res.Bandwidth+eps):F3} OR set DisableEntryFilters=true");
                if (res.Bandwidth > maxBandwidthForEntry) sugg.Add($"- Raise MaxBandwidthForEntry {maxBandwidthForEntry:F3} -> {(res.Bandwidth+eps):F3} OR set DisableEntryFilters=true");
                if (requireAccelAlignment && ((targetLong && res.FastGrad > 0 && res.Accel < 0) || (!targetLong && res.FastGrad < 0 && res.Accel > 0)))
                    sugg.Add("- Set RequireAccelAlignment=false");
                if (enableATRCap && res.ATR > maxATRForEntry)
                    sugg.Add($"- Raise MaxATRForEntry {maxATRForEntry:F2} -> {(res.ATR+0.01):F2} OR set EnableATRCap=false");
                if (enableRSIFloor && res.RSI < minRSIForEntry)
                    sugg.Add($"- Lower MinRSIForEntry {minRSIForEntry:F1} -> {(res.RSI+0.1):F1} OR set EnableRSIFloor=false");
            }
            sugg.Add("- Note: ExitCooldown/WeakReversalDelay not reconstructed; if active then set ExitCooldownSeconds=0 or WeakReversalDelay=0 for testing");
        }

        private void BuildExitSuggestions(EntryDiagnosisResult res)
        {
            const double eps = 1e-4;
            var sugg = res.SuggestedFixLines;
            sugg.Add("SUGGESTIONS targeting EXIT: parameter nudges to confirm/trigger exits");

            // LONG exit rule components
            bool longGradCond = res.FastGrad < 0 && res.FastGrad <= fastEMAGradientExitThreshold;
            bool longPriceCond = res.Close < res.FastEMA;
            bool longExitReady = longGradCond && longPriceCond;
            if (!longExitReady)
            {
                sugg.Add("- LONG exit:");
                if (!exitOnFastEMAGradient) sugg.Add("  • Enable ExitOnFastEMAGradient=true");
                if (!(res.FastGrad < 0)) sugg.Add("  • FastGrad must be < 0 (cannot force via params)");
                if (res.FastGrad < 0 && res.FastGrad > fastEMAGradientExitThreshold)
                    sugg.Add($"  • Soften FastEMAGradientExitThreshold {fastEMAGradientExitThreshold:F2} -> {(res.FastGrad+eps):F2}");
                if (!exitOnCloseBelowFastEMA) sugg.Add("  • Enable ExitOnCloseBelowFastEMA=true");
                if (!longPriceCond) sugg.Add("  • Close must be < FastEMA (cannot force via params)");
            }

            // SHORT exit rule components
            bool shortGradCond = res.FastGrad > 0 && res.FastGrad >= fastEMAGradientExitThresholdShort;
            bool shortPriceCond = res.Close > res.FastEMA;
            bool shortExitReady = shortGradCond && shortPriceCond;
            if (!shortExitReady)
            {
                sugg.Add("- SHORT exit:");
                if (!exitOnFastEMAGradient) sugg.Add("  • Enable ExitOnFastEMAGradient=true");
                if (!(res.FastGrad > 0)) sugg.Add("  • FastGrad must be > 0 (cannot force via params)");
                if (res.FastGrad > 0 && res.FastGrad < fastEMAGradientExitThresholdShort)
                    sugg.Add($"  • Soften FastEMAGradientExitThresholdShort {fastEMAGradientExitThresholdShort:F2} -> {(res.FastGrad+eps):F2}");
                if (!exitOnCloseBelowFastEMA) sugg.Add("  • Enable ExitOnCloseBelowFastEMA=true (SHORT uses Close>FastEMA)");
                if (!shortPriceCond) sugg.Add("  • Close must be > FastEMA (cannot force via params)");
            }
        }

        private void ApplyDiagnosisSuggestions(EntryDiagnosisResult res)
        {
            const double eps = 1e-4;
            int targetStreak = res.SignalStreakLong >= res.SignalStreakShort ? res.SignalStreakLong : res.SignalStreakShort;
            if (targetStreak > 0 && targetStreak < entryBarDelay)
                entryBarDelay = Math.Max(1, targetStreak);
            double fgAbs = Math.Abs(res.FastGrad);
            if (fgAbs < minEntryFastGradientAbs)
                minEntryFastGradientAbs = Math.Max(0.0, fgAbs + eps);
            if (fgAbs > maxEntryFastGradientAbs)
                maxEntryFastGradientAbs = fgAbs + eps;
            if (adx != null && res.ADX < minAdxForEntry)
                minAdxForEntry = res.ADX + eps;
            if (res.GradStab > maxGradientStabilityForEntry)
                maxGradientStabilityForEntry = res.GradStab + eps;
            if (res.Bandwidth < minBandwidthForEntry)
                minBandwidthForEntry = Math.Max(0.0, res.Bandwidth + eps);
            if (res.Bandwidth > maxBandwidthForEntry)
                maxBandwidthForEntry = res.Bandwidth + eps;
            if (requireAccelAlignment && ((res.FastGrad > 0 && res.Accel < 0) || (res.FastGrad < 0 && res.Accel > 0)))
                requireAccelAlignment = false;
            if (enableATRCap && res.ATR > maxATRForEntry)
                maxATRForEntry = res.ATR + 0.01;
            if (enableRSIFloor && res.RSI < minRSIForEntry)
                minRSIForEntry = res.RSI + 0.1;
        }

        private double SafeGet(ISeries<double> series, int offset)
        {
            try { return series[offset]; } catch { return 0.0; }
        }
        private DateTime SafeTimeAt(int offset)
        {
            try { return Time[offset]; } catch { return DateTime.MinValue; }
        }

        private double ComputeFastGradientStdDev(int offset, int period)
        {
            int available = 0;
            double[] vals = new double[Math.Max(0, period)];
            for (int k = 0; k < period; k++)
            {
                int idx = offset + k;
                if (idx + 1 > CurrentBar) break;
                double v0 = SafeGet(fastEMA, idx);
                double v1 = SafeGet(fastEMA, idx + 1);
                vals[available++] = v0 - v1;
            }
            if (available <= 1) return 0.0;
            double mean = 0.0;
            for (int i = 0; i < available; i++) mean += vals[i];
            mean /= available;
            double var = 0.0;
            for (int i = 0; i < available; i++)
            {
                double d = vals[i] - mean;
                var += d * d;
            }
            var /= (available - 1);
            return Math.Sqrt(var);
        }

        private int CountConsecutiveDirectionalBars(int offset, bool longDirection)
        {
            int count = 0;
            for (int k = 0; ; k++)
            {
                int idx = offset + k;
                if (idx + 1 > CurrentBar) break;
                double f0 = SafeGet(fastEMA, idx);
                double f1 = SafeGet(fastEMA, idx + 1);
                double s0 = SafeGet(slowEMA, idx);
                double s1 = SafeGet(slowEMA, idx + 1);
                double c0 = SafeGet(Close, idx);
                double fg = f0 - f1;
                double sg = s0 - s1;
                bool dirOK = longDirection ? (fg > 0 && sg > 0) : (fg < 0 && sg < 0);
                bool posOK = longDirection ? (c0 > f0 && c0 > s0) : (c0 < f0 && c0 < s0);
                if (dirOK && posOK) count++; else break;
            }
            return count;
        }

        #endregion

        #region Properties
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Quantity", Description = "Number of contracts to trade", Order = 1, GroupName = "Parameters")]
        public int Quantity
        {
            get { return quantity; }
            set { quantity = Math.Max(1, value); }
        }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Fast EMA Period", Description = "Period for fast EMA (default: 10)", Order = 2, GroupName = "Parameters")]
        public int FastEMAPeriod
        {
            get { return fastEMAPeriod; }
            set { fastEMAPeriod = Math.Max(1, value); }
        }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Slow EMA Period", Description = "Period for slow EMA (default: 20)", Order = 3, GroupName = "Parameters")]
        public int SlowEMAPeriod
        {
            get { return slowEMAPeriod; }
            set { slowEMAPeriod = Math.Max(1, value); }
        }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Entry Bar Delay", Description = "Which bar to enter: 1=first bar (immediate), 2=second bar, 3=third bar, etc.", Order = 4, GroupName = "Parameters")]
        public int EntryBarDelay
        {
            get { return entryBarDelay; }
            set { entryBarDelay = Math.Max(1, value); }
        }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Initial Bars Wait", Description = "Number of bars to wait after BarsRequiredToTrade before allowing any trades (default: 5)", Order = 5, GroupName = "Parameters")]
        public int InitialBarsWait
        {
            get { return initialBarsWait; }
            set { initialBarsWait = Math.Max(0, value); }
        }
        
        
        
        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "Weak Gradient Threshold", Description = "Threshold for weak gradient (default: 0.5). Lower = more sensitive to weakness", Order = 6, GroupName = "Parameters")]
        public double WeakGradientThreshold
        {
            get { return weakGradientThreshold; }
            set { weakGradientThreshold = Math.Max(0.1, value); }
        }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Entry Fast Gradient Abs", Description = "Minimum absolute fast EMA gradient required for ENTRY only (default: 0.50)", Order = 7, GroupName = "Parameters")]
        public double MinEntryFastGradientAbs
        {
            get { return minEntryFastGradientAbs; }
            set { minEntryFastGradientAbs = Math.Max(0.0, value); }
        }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Validation Min Fast Gradient Abs", Description = "Minimum absolute fast EMA gradient required to STAY in position (validation during position). Default: 0.15", Order = 8, GroupName = "Parameters")]
        public double ValidationMinFastGradientAbs
        {
            get { return validationMinFastGradientAbs; }
            set { validationMinFastGradientAbs = Math.Max(0.0, value); }
        }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Max Entry Fast Gradient Abs", Description = "Upper cap on absolute fast EMA gradient to allow at ENTRY (filters overextended moves). Default: 0.60", Order = 9, GroupName = "Parameters")]
        public double MaxEntryFastGradientAbs
        {
            get { return maxEntryFastGradientAbs; }
            set { maxEntryFastGradientAbs = Math.Max(0.0, value); }
        }
        
        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Weak Reversal Delay", Description = "Bars to wait after weak reversal before allowing re-entry (default: 3)", Order = 7, GroupName = "Parameters")]
        public int WeakReversalDelayPeriod
        {
            get { return weakReversalDelayPeriod; }
            set { weakReversalDelayPeriod = Math.Max(1, value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Enable Adaptive Entry Gradient", Description = "Adapt MinEntryFastGradientAbs near zero-cross and after deep opposite legs.", Order = 10, GroupName = "Parameters")]
        public bool EnableAdaptiveEntryGradient
        {
            get { return enableAdaptiveEntryGradient; }
            set { enableAdaptiveEntryGradient = value; }
        }

        [NinjaScriptProperty]
        [Range(0.1, 2.0)]
        [Display(Name = "Adaptive Near-Zero Mult", Description = "Multiplier applied to entry threshold when recent fast gradient sign flip detected.", Order = 11, GroupName = "Parameters")]
        public double AdaptiveNearZeroMultiplier
        {
            get { return adaptiveNearZeroMultiplier; }
            set { adaptiveNearZeroMultiplier = Math.Max(0.1, value); }
        }

        [NinjaScriptProperty]
        [Range(0.1, 2.0)]
        [Display(Name = "Adaptive Deep-Leg Mult", Description = "Multiplier applied to entry threshold when prior opposite leg depth exceeds points threshold.", Order = 12, GroupName = "Parameters")]
        public double AdaptiveDeepLegMultiplier
        {
            get { return adaptiveDeepLegMultiplier; }
            set { adaptiveDeepLegMultiplier = Math.Max(0.1, value); }
        }

        [NinjaScriptProperty]
        [Range(0.0, 1000.0)]
        [Display(Name = "Adaptive Deep-Leg Points", Description = "Minimum points of opposite leg depth to trigger deep-leg multiplier.", Order = 13, GroupName = "Parameters")]
        public double AdaptiveDeepLegPoints
        {
            get { return adaptiveDeepLegPoints; }
            set { adaptiveDeepLegPoints = Math.Max(0.0, value); }
        }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Adaptive Lookback Bars", Description = "Bars to scan for zero-flip and deep opposite leg context.", Order = 14, GroupName = "Parameters")]
        public int AdaptiveLookbackBars
        {
            get { return adaptiveLookbackBars; }
            set { adaptiveLookbackBars = Math.Max(1, value); }
        }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Adaptive Threshold Floor", Description = "Absolute floor; adaptive entry threshold will not drop below this value.", Order = 15, GroupName = "Parameters")]
        public double AdaptiveMinFloor
        {
            get { return adaptiveMinFloor; }
            set { adaptiveMinFloor = Math.Max(0.0, value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Stream Bar Diagnostics", Description = "When true, send a compact diagnosis JSON for every completed bar to the local dashboard.", Order = 16, GroupName = "Debug")]
        public bool StreamBarDiagnostics
        {
            get { return streamBarDiagnostics; }
            set { streamBarDiagnostics = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Enable Logging", Description = "Enable detailed logging to Output window and log files (default: true)", Order = 1, GroupName = "Debug")]
        public bool EnableLogging
        {
            get { return enableLogging; }
            set { enableLogging = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Show Bar Index Labels", Description = "Display bar index numbers on chart for debugging (default: true)", Order = 2, GroupName = "Debug")]
        public bool ShowBarIndexLabels
        {
            get { return showBarIndexLabels; }
            set { showBarIndexLabels = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Show HUD", Description = "Show top-left HUD with gradients/position info (default: false)", Order = 3, GroupName = "Debug")]
        public bool ShowHud
        {
            get { return showHud; }
            set { showHud = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Auto Add Gradient Panel", Description = "Automatically add EMAGradientPair to chart (default: true)", Order = 4, GroupName = "Debug")]
        public bool AutoAddGradientPanel
        {
            get { return autoAddGradientPanel; }
            set { autoAddGradientPanel = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "HUD Position", Description = "Fixed position of HUD text on chart", Order = 5, GroupName = "Debug")]
        public TextPosition HudPosition
        {
            get { return hudPosition; }
            set { hudPosition = value; }
        }

        [NinjaScriptProperty]
        [Range(6, 32)]
        [Display(Name = "HUD Font Size", Description = "Font size for HUD text (points)", Order = 6, GroupName = "Debug")]
        public int HudFontSize
        {
            get { return hudFontSize; }
            set { hudFontSize = Math.Max(6, Math.Min(32, value)); hudSimpleFont = null; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Save Properties On Start", Description = "When true, writes all public strategy properties to a file at startup", Order = 7, GroupName = "Debug")]
        public bool SavePropertiesOnStart
        {
            get { return savePropertiesOnStart; }
            set { savePropertiesOnStart = value; }
        }


        [NinjaScriptProperty]
        [Display(Name = "Enable Sign Flip Exit", Description = "If true, use near-zero fast EMA gradient sign flip with dual EMA guard instead of magnitude thresholds.", Order = 7, GroupName = "Exit")]
        public bool EnableSignFlipExit
        {
            get { return useSignFlipExit; }
            set { useSignFlipExit = value; }
        }

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Sign Flip Tolerance", Description = "Tolerance around zero for gradient sign flip exit (e.g. 0.02).", Order = 8, GroupName = "Exit")]
        public double SignFlipTolerance
        {
            get { return signFlipTolerance; }
            set { signFlipTolerance = value; }
        }
        [NinjaScriptProperty]
        [Display(Name = "Dump Properties Now", Description = "Set true to immediately dump current properties to file (auto-resets).", Order = 8, GroupName = "Debug")]
        public bool DumpPropertiesNow
        {
            get { return dumpPropertiesNow; }
            set { dumpPropertiesNow = value; }
        }
        
        [Browsable(false)]
        [XmlIgnore]
        public bool HoverDebug
        {
            get { return false; }
            set { /* deprecated: no-op */ }
        }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "HUD Vertical Offset", Description = "Blank lines to insert before HUD when positioned TopLeft (pushes it lower).", Order = 10, GroupName = "Debug")]
        public int HudVerticalOffset
        {
            get { return hudVerticalOffset; }
            set { hudVerticalOffset = Math.Max(0, Math.Min(20, value)); }
        }
        
        // Removed diagnosis properties (replaced by interactive chart buttons)
        
        [NinjaScriptProperty]
        [Display(Name = "Exit on Fast EMA Gradient", Description = "Exit when fast EMA gradient reverses (LONG: grad<0, SHORT: grad>0)", Order = 1, GroupName = "Exit")]
        public bool ExitOnFastEMAGradient
        {
            get { return exitOnFastEMAGradient; }
            set { exitOnFastEMAGradient = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Exit on Slow EMA Gradient", Description = "Exit when slow EMA gradient reverses (LONG: grad<0, SHORT: grad>0)", Order = 2, GroupName = "Exit")]
        public bool ExitOnSlowEMAGradient
        {
            get { return exitOnSlowEMAGradient; }
            set { exitOnSlowEMAGradient = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Exit on Close vs Fast EMA", Description = "Exit when close crosses fast EMA (LONG: close<FastEMA, SHORT: close>FastEMA)", Order = 3, GroupName = "Exit")]
        public bool ExitOnCloseBelowFastEMA
        {
            get { return exitOnCloseBelowFastEMA; }
            set { exitOnCloseBelowFastEMA = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Exit on Close vs Slow EMA", Description = "Exit when close crosses slow EMA (LONG: close<SlowEMA, SHORT: close>SlowEMA)", Order = 4, GroupName = "Exit")]
        public bool ExitOnCloseBelowSlowEMA
        {
            get { return exitOnCloseBelowSlowEMA; }
            set { exitOnCloseBelowSlowEMA = value; }
        }
        
        [NinjaScriptProperty]
        [Range(-10.0, 0.0)]
        [Display(Name = "Fast EMA Gradient Exit Threshold (LONG)", Description = "LONG exit: Fast EMA gradient must be <= this value (default: -0.25). More negative = stronger reversal required", Order = 5, GroupName = "Exit")]
        public double FastEMAGradientExitThreshold
        {
            get { return fastEMAGradientExitThreshold; }
            set { fastEMAGradientExitThreshold = value; }
        }
        
        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Fast EMA Gradient Exit Threshold (SHORT)", Description = "SHORT exit: Fast EMA gradient must be >= this value (default: 0.25). More positive = stronger reversal required", Order = 6, GroupName = "Exit")]
        public double FastEMAGradientExitThresholdShort
        {
            get { return fastEMAGradientExitThresholdShort; }
            set { fastEMAGradientExitThresholdShort = value; }
        }

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Min Hold Bars", Description = "Minimum bars to hold a position before allowing exits (0 = allow same-bar exits). Default: 1", Order = 7, GroupName = "Exit")]
        public int MinHoldBars
        {
            get { return minHoldBars; }
            set { minHoldBars = Math.Max(0, value); }
        }

        

        [NinjaScriptProperty]
        [Range(0.0, 10000.0)]
        [Display(Name = "Exit Confirm Fast EMA Delta", Description = "Additional Fast EMA move (points) required to confirm a pending exit. LONG exits require EMA to drop by this amount; SHORT exits require EMA to rise by this amount. Default: 0.5", Order = 10, GroupName = "Exit")]
        public double ExitConfirmFastEMADelta
        {
            get { return exitConfirmFastEMADelta; }
            set { exitConfirmFastEMADelta = Math.Max(0.0, value); }
        }

        // --- Reversal Early Entry Properties ---
        [NinjaScriptProperty]
        [Display(Name = "Enable Reversal Early Entry", Description = "Allow early LONG entries before slow EMA confirmation.", Order = 1, GroupName = "Reversal")]
        public bool EnableReversalEarlyEntry
        {
            get { return enableReversalEarlyEntry; }
            set { enableReversalEarlyEntry = value; }
        }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Reversal Lookback Bars", Description = "Bars to scan for prior bear/bull context.", Order = 2, GroupName = "Reversal")]
        public int ReversalLookbackBars
        {
            get { return reversalLookbackBars; }
            set { reversalLookbackBars = Math.Max(1, value); }
        }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Min Bear Bars For Reversal", Description = "Consecutive bear bars required before early LONG.", Order = 3, GroupName = "Reversal")]
        public int MinBearBarsForReversal
        {
            get { return minBearBarsForReversal; }
            set { minBearBarsForReversal = Math.Max(1, value); }
        }

        [NinjaScriptProperty]
        [Range(0.0, 100000.0)]
        [Display(Name = "Min Reversal Depth Points", Description = "Minimum depth below fast EMA during prior bear leg.", Order = 4, GroupName = "Reversal")]
        public double MinReversalDepthPoints
        {
            get { return minReversalDepthPoints; }
            set { minReversalDepthPoints = Math.Max(0.0, value); }
        }

        [NinjaScriptProperty]
        [Range(0.0, 1000.0)]
        [Display(Name = "Min Accel For Reversal", Description = "Minimum acceleration (fastGrad - prevFastGrad).", Order = 5, GroupName = "Reversal")]
        public double MinAccelForReversal
        {
            get { return minAccelForReversal; }
            set { minAccelForReversal = Math.Max(0.0, value); }
        }

        [NinjaScriptProperty]
        [Range(0.0, 1000.0)]
        [Display(Name = "Max Reversal Stability", Description = "Maximum gradient std dev for reversal entry.", Order = 6, GroupName = "Reversal")]
        public double MaxReversalStability
        {
            get { return maxReversalStability; }
            set { maxReversalStability = Math.Max(0.0, value); }
        }

        [NinjaScriptProperty]
        [Range(0.0, 1000.0)]
        [Display(Name = "Min Reversal ADX", Description = "Minimum ADX value for early reversal entry.", Order = 7, GroupName = "Reversal")]
        public double MinReversalAdx
        {
            get { return minReversalAdx; }
            set { minReversalAdx = Math.Max(0.0, value); }
        }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Reversal Hold Guard Bars", Description = "Minimum bars to hold reversal trade before validation exit.", Order = 8, GroupName = "Reversal")]
        public int ReversalHoldGuardBars
        {
            get { return reversalHoldGuardBars; }
            set { reversalHoldGuardBars = Math.Max(0, value); }
        }

        [NinjaScriptProperty]
        [Range(0.0, 100000.0)]
        [Display(Name = "Reversal Stop Buffer", Description = "Logged hypothetical protective stop distance beyond trough.", Order = 9, GroupName = "Reversal")]
        public double ReversalStopBuffer
        {
            get { return reversalStopBuffer; }
            set { reversalStopBuffer = Math.Max(0.0, value); }
        }

        // --- Entry Filter Properties ---
        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min ADX For Entry", Description = "Minimum ADX value required to allow entry (trend strength). Default: 15", Order = 10, GroupName = "Filters")]
        public double MinAdxForEntry
        {
            get { return minAdxForEntry; }
            set { minAdxForEntry = Math.Max(0.0, value); }
        }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max Gradient Stability", Description = "Maximum rolling std dev of fast gradient allowed at entry (smoothness). Default: 1.85", Order = 11, GroupName = "Filters")]
        public double MaxGradientStabilityForEntry
        {
            get { return maxGradientStabilityForEntry; }
            set { maxGradientStabilityForEntry = Math.Max(0.0, value); }
        }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Min Bandwidth", Description = "Minimum EMA separation ratio (fast vs slow) required at entry. Default: 0.000", Order = 12, GroupName = "Filters")]
        public double MinBandwidthForEntry
        {
            get { return minBandwidthForEntry; }
            set { minBandwidthForEntry = Math.Max(0.0, value); }
        }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max Bandwidth", Description = "Maximum EMA separation ratio allowed at entry (avoid overextension). Default: 0.100", Order = 13, GroupName = "Filters")]
        public double MaxBandwidthForEntry
        {
            get { return maxBandwidthForEntry; }
            set { maxBandwidthForEntry = Math.Max(0.0, value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Require Accel Alignment", Description = "Require fast gradient acceleration to align with gradient direction (reinforcing move). Default: true", Order = 14, GroupName = "Filters")]
        public bool RequireAccelAlignment
        {
            get { return requireAccelAlignment; }
            set { requireAccelAlignment = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Disable Entry Filters", Description = "Bypass ADX / GradientStability / Bandwidth / Accel filters for data gathering (default: false)", Order = 15, GroupName = "Filters")]
        public bool DisableEntryFilters
        {
            get { return disableEntryFilters; }
            set { disableEntryFilters = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Enable ATR Cap", Description = "If true, require ATR <= MaxATRForEntry at entry.", Order = 16, GroupName = "Filters")]
        public bool EnableATRCap
        {
            get { return enableATRCap; }
            set { enableATRCap = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Max ATR For Entry", Description = "Maximum ATR(14) allowed at entry when ATR Cap enabled.", Order = 17, GroupName = "Filters")]
        public double MaxATRForEntry
        {
            get { return maxATRForEntry; }
            set { maxATRForEntry = Math.Max(0.0, value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Enable RSI Floor", Description = "If true, require RSI >= MinRSIForEntry at entry.", Order = 18, GroupName = "Filters")]
        public bool EnableRSIFloor
        {
            get { return enableRSIFloor; }
            set { enableRSIFloor = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Min RSI For Entry", Description = "Minimum RSI(14) required at entry when RSI Floor enabled.", Order = 19, GroupName = "Filters")]
        public double MinRSIForEntry
        {
            get { return minRSIForEntry; }
            set { minRSIForEntry = Math.Max(0.0, value); }
        }
        
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Signal
        {
            get { return Values[0]; }
        }
        
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

#endregion
