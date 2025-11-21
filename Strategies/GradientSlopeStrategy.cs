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
        private int minHoldBars = 1; // Minimum bars to hold a position before allowing exits (0 = allow same-bar exits)

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
        private bool showHud = false;                // Show top-left status HUD
        private int cooldownStartBar = -1;          // Track bar when cooldown started
        
        // Parameters
        private int quantity = 1;
        private int fastEMAPeriod = 10;
        private int slowEMAPeriod = 20;
        private int entryBarDelay = 1;
        private int initialBarsWait = 5;  // Bars to wait after strategy starts before allowing any trades
        private bool enableLogging = true;
        private double weakGradientThreshold = 0.5;  // Threshold for "weak" gradient
        private int weakReversalDelayPeriod = 3;  // Bars to wait after weak reversal
        private double minEntryFastGradientAbs = 0.45; // Minimum absolute fast gradient required for entry only (tunable)
        private double validationMinFastGradientAbs = 0.25; // Minimum absolute fast gradient required to stay in position
        
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
        
        #endregion
        
        #region Fast EMA Exit Logic
        
        /// <summary>
        /// Check if LONG position should exit based on Fast EMA conditions
        /// Core rule: Exit when fast EMA gradient is negative AND below threshold AND price is below fast EMA
        /// </summary>
        private bool ShouldExitLongOnFastEMA(double fastGradient, double currentClose, double fastEMAValue, out string exitReason)
        {
            exitReason = "";
            
            // Core Fast EMA exit rule for LONG:
            // 1. Fast gradient must be negative
            // 2. Fast gradient must be <= threshold (e.g., -0.25 or more negative)
            // 3. Close must be below fast EMA
            bool gradientNegative = fastGradient < 0;
            bool gradientBelowThreshold = fastGradient <= fastEMAGradientExitThreshold;
            bool closeBelowFastEMA = currentClose < fastEMAValue;
            
            bool shouldExit = gradientNegative && gradientBelowThreshold && closeBelowFastEMA;
            
            if (shouldExit)
            {
                exitReason = $"FastEMAGrad={fastGradient:F4}(<={fastEMAGradientExitThreshold}),Close<FastEMA";
            }
            
            return shouldExit;
        }
        
        /// <summary>
        /// Check if SHORT position should exit based on Fast EMA conditions
        /// Core rule: Exit when fast EMA gradient is positive AND above threshold AND price is above fast EMA
        /// </summary>
        private bool ShouldExitShortOnFastEMA(double fastGradient, double currentClose, double fastEMAValue, out string exitReason)
        {
            exitReason = "";
            
            // Core Fast EMA exit rule for SHORT:
            // 1. Fast gradient must be positive
            // 2. Fast gradient must be >= threshold (e.g., 0.25 or more positive)
            // 3. Close must be above fast EMA
            bool gradientPositive = fastGradient > 0;
            bool gradientAboveThreshold = fastGradient >= fastEMAGradientExitThresholdShort;
            bool closeAboveFastEMA = currentClose > fastEMAValue;
            
            bool shouldExit = gradientPositive && gradientAboveThreshold && closeAboveFastEMA;
            
            if (shouldExit)
            {
                exitReason = $"FastEMAGrad={fastGradient:F4}(>={fastEMAGradientExitThresholdShort}),Close>FastEMA";
            }
            
            return shouldExit;
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
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
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
                LogOutput("=".PadRight(80, '='));
                Print(string.Format("[GRAD] Log File: {0}", logFilePath));

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
            }
        }
        
        #endregion
        
        #region OnBarUpdate
        
        protected override void OnBarUpdate()
        {
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
                        Draw.TextFixed(this, "HUD_Cooldown", $"COOLDOWN: {secsLeft}s left", TextPosition.TopLeft);
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
                    LogTrade(Time[0], CurrentBar, "EXIT", "LONG", currentClose, quantity, $"VALIDATION_FAILED:{validationReason}");
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
                            LogTrade(Time[0], CurrentBar, "EXIT", "LONG", currentClose, quantity, $"CONFIRMED:{exitPendingReason};FastEMAΔ={emaDrop:F2}>= {exitConfirmFastEMADelta:F2}");
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
                    LogTrade(Time[0], CurrentBar, "EXIT", "SHORT", currentClose, quantity, $"VALIDATION_FAILED:{validationReason}");
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
                            LogTrade(Time[0], CurrentBar, "EXIT", "SHORT", currentClose, quantity, $"CONFIRMED:{exitPendingReason};FastEMAΔ={emaRise:F2}>= {exitConfirmFastEMADelta:F2}");
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
                        // LONG validation: Both gradients must be positive, fast gradient strong, price above both EMAs
                        bool gradientsPositive = (fastGradient > 0 && slowGradient > 0);
                        bool fastGradientStrong = (fastGradient > minEntryFastGradientAbs);
                        bool priceAboveEMAs = (currentClose > fastEMAValue && currentClose > slowEMAValue);

                        conditionsStillValid = gradientsPositive && fastGradientStrong && priceAboveEMAs;

                        // Reversal logic remains disabled; compute value but not used
                        bool oppositeGradientsNegative = (fastGradient < 0 && slowGradient < 0);
                        bool oppositeFastGradientStrong = (fastGradient < -minEntryFastGradientAbs);
                        bool oppositePriceBelowEMAs = (currentClose < fastEMAValue && currentClose < slowEMAValue);
                        shouldReverse = oppositeGradientsNegative && oppositeFastGradientStrong && oppositePriceBelowEMAs;

                        if (!conditionsStillValid)
                        {
                            if (!gradientsPositive)
                                invalidationReason = "Gradients turned negative";
                            else if (!fastGradientStrong)
                                invalidationReason = $"Fast gradient too weak ({fastGradient:F4} <= {minEntryFastGradientAbs:F2})";
                            else if (!priceAboveEMAs)
                                invalidationReason = "Price dropped below EMAs";
                        }
                    }
                    else if (currentSignal == "SHORT")
                    {
                        // SHORT validation: Both gradients negative, fast gradient strong enough, price below both EMAs
                        bool gradientsNegative = (fastGradient < 0 && slowGradient < 0);
                        bool fastGradientStrong = (fastGradient < -minEntryFastGradientAbs);
                        bool priceBelowEMAs = (currentClose < fastEMAValue && currentClose < slowEMAValue);

                        conditionsStillValid = gradientsNegative && fastGradientStrong && priceBelowEMAs;

                        // Reversal logic remains disabled; compute value but not used
                        bool oppositeGradientsPositive = (fastGradient > 0 && slowGradient > 0);
                        bool oppositeFastGradientStrong = (fastGradient > minEntryFastGradientAbs);
                        bool oppositePriceAboveEMAs = (currentClose > fastEMAValue && currentClose > slowEMAValue);
                        shouldReverse = oppositeGradientsPositive && oppositeFastGradientStrong && oppositePriceAboveEMAs;

                        if (!conditionsStillValid)
                        {
                            if (!gradientsNegative)
                                invalidationReason = "Gradients turned positive";
                            else if (!fastGradientStrong)
                                invalidationReason = $"Fast gradient too weak ({fastGradient:F4} >= -{minEntryFastGradientAbs:F2})";
                            else if (!priceBelowEMAs)
                                invalidationReason = "Price rose above EMAs";
                        }
                    }
                    
                    // Log detailed entry decision to CSV
                    bool fastGradPositive = fastGradient > 0;
                    bool slowGradPositive = slowGradient > 0;
                    bool closeAboveFastEMA = currentClose > fastEMAValue;
                    bool closeAboveSlowEMA = currentClose > slowEMAValue;
                    string entryConditions = $"ENTRY_DECISION: Signal={currentSignal} Bar={barsInSignal}/{entryBarDelay} Valid={conditionsStillValid} ShouldReverse={shouldReverse} PriceRising={priceRising} FastGrad={fastGradient:F4}({(fastGradPositive?"POS":"NEG")}) SlowGrad={slowGradient:F4}({(slowGradPositive?"POS":"NEG")}) Close={currentClose:F2} FastEMA={fastEMAValue:F2}({(closeAboveFastEMA?"ABOVE":"BELOW")}) SlowEMA={slowEMAValue:F2}({(closeAboveSlowEMA?"ABOVE":"BELOW")})";
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
                            LogTrade(Time[0], CurrentBar, "EXIT", "LONG", currentClose, quantity, $"ENTRY_PHASE_VALIDATION_FAILED:{invalidationReason}");
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
                            LogTrade(Time[0], CurrentBar, "EXIT", "SHORT", currentClose, quantity, $"ENTRY_PHASE_VALIDATION_FAILED:{invalidationReason}");
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
                            Print(string.Format("[GRAD] {0} | ENTRY CANCELLED: {1} - Restarting delay counter", Time[0], invalidationReason));
                            LogOutput($"    ⚠ ENTRY CANCELLED: {invalidationReason} - Restarting delay counter");
                            LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                                currentSignal, currentSignal, myPosition, "ENTRY_CANCELLED", invalidationReason);
                            signalStartBar = CurrentBar; // Restart the delay from current bar
                        }
                    }
                    else if (currentSignal == "LONG" && myPosition == "FLAT" && Position.MarketPosition == MarketPosition.Flat && !inExitCooldown)
                    {
                        // Reset any pending exit from prior state
                        exitPending = false; exitPendingSide = ""; exitPendingStartBar = -1; exitPendingAnchorFastEMA = 0; exitPendingReason = "";
                        EnterLong(quantity, "EnterLong");
                        lastTradeBar = CurrentBar;
                        myPosition = "LONG";
                        entryBar = CurrentBar;
                        entryPrice = currentClose; entryTime = Time[0]; tradeMFE = 0.0; tradeMAE = 0.0;
                        Print(string.Format("[GRAD] {0} | ENTER LONG at {1:F2} on bar #{2} of signal | FastGrad: {3:+0.000;-0.000} | SlowGrad: {4:+0.000;-0.000}",
                            Time[0], currentClose, barsInSignal, fastGradient, slowGradient));
                        LogOutput($">>> ENTER LONG at {currentClose:F2} (Bar {barsInSignal}/{entryBarDelay}) | FastGrad: {fastGradient:+0.000;-0.000} | SlowGrad: {slowGradient:+0.000;-0.000}");
                        LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                            currentSignal, currentSignal, "LONG", "ENTRY", $"EnterLong_Bar{barsInSignal}");
                        LogTrade(Time[0], CurrentBar, "ENTRY", "LONG", currentClose, quantity, $"EnterLong_Bar{barsInSignal}");
                        if (showChartAnnotations)
                        {
                            Draw.ArrowUp(this, $"EnterLong_{CurrentBar}", false, 0, Low[0] - 2*TickSize, Brushes.LimeGreen);
                            Draw.Text(this, $"EnterLong_T_{CurrentBar}", "LONG", 0, Low[0] - 6*TickSize, Brushes.LimeGreen);
                        }
                    }
                    else if (currentSignal == "SHORT" && myPosition == "FLAT" && Position.MarketPosition == MarketPosition.Flat && !inExitCooldown)
                    {
                        // Reset any pending exit from prior state
                        exitPending = false; exitPendingSide = ""; exitPendingStartBar = -1; exitPendingAnchorFastEMA = 0; exitPendingReason = "";
                        EnterShort(quantity, "EnterShort");
                        lastTradeBar = CurrentBar;
                        myPosition = "SHORT";
                        entryBar = CurrentBar;
                        entryPrice = currentClose; entryTime = Time[0]; tradeMFE = 0.0; tradeMAE = 0.0;
                        Print(string.Format("[GRAD] {0} | ENTER SHORT at {1:F2} on bar #{2} of signal | FastGrad: {3:+0.000;-0.000} | SlowGrad: {4:+0.000;-0.000}",
                            Time[0], currentClose, barsInSignal, fastGradient, slowGradient));
                        LogOutput($">>> ENTER SHORT at {currentClose:F2} (Bar {barsInSignal}/{entryBarDelay}) | FastGrad: {fastGradient:+0.000;-0.000} | SlowGrad: {slowGradient:+0.000;-0.000}");
                        LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                            currentSignal, currentSignal, "SHORT", "ENTRY", $"EnterShort_Bar{barsInSignal}");
                        LogTrade(Time[0], CurrentBar, "ENTRY", "SHORT", currentClose, quantity, $"EnterShort_Bar{barsInSignal}");
                        if (showChartAnnotations)
                        {
                            Draw.ArrowDown(this, $"EnterShort_{CurrentBar}", false, 0, High[0] + 2*TickSize, Brushes.IndianRed);
                            Draw.Text(this, $"EnterShort_T_{CurrentBar}", "SHORT", 0, High[0] + 6*TickSize, Brushes.IndianRed);
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
                    Draw.TextFixed(this, "HUD_Main", hud, TextPosition.TopLeft);
                }
            }
            
            // Debug: Draw bar index labels on chart
            if (showBarIndexLabels && IsFirstTickOfBar)
            {
                Draw.Text(this, "BarLabel_" + CurrentBar, CurrentBar.ToString(), 0, Low[0] - (5 * TickSize), Brushes.Gray);
            }
        }
        
        #endregion
        
        

        #region Helper Methods
        
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
                    csvWriter.WriteLine("Timestamp,Bar,Close,FastEMA,SlowEMA,FastGradient,SlowGradient,PrevSignal,NewSignal,MyPosition,ActualPosition,Action,Notes,InWeakDelay,SignalStartBar,LastTradeBar,PriceGradient,BarsSinceEntry,EntryBar,EntryPrice,ExitPending,ExitPendingSide,ExitPendingAnchorFastEMA,ExitPendingEMADelta,MinHoldBars,InExitCooldown,CooldownSecsLeft,TradeMFE,TradeMAE,UnrealizedPoints");
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
                csvWriter.WriteLine(string.Format("{0:yyyy-MM-dd HH:mm:ss},{1},{2:F2},{3:F2},{4:F2},{5:F4},{6:F4},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16:F4},{17},{18},{19:F2},{20},{21},{22:F2},{23:F2},{24},{25},{26},{27:F2},{28:F2},{29:F2}",
                    time, bar, close, fastEma, slowEma, fastGrad, slowGrad, prevSignal, newSignal, position, actualPos, action, notes,
                    inWeakReversalDelay ? "1" : "0", signalStartBar, lastTradeBar,
                    priceGradient, barsSinceEntry, entryBar, entryPrice, exitPending ? 1 : 0, exitPendingSide, exitPendingAnchorFastEMA, exitPendingDelta,
                    minHoldBars, inExitCooldown ? 1 : 0, cooldownSecsLeft, tradeMFE, Math.Abs(tradeMAE), unrealizedPts));
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
            }
            catch (Exception ex)
            {
                Print(string.Format("[GRAD] Trades CSV Error: {0}", ex.Message));
            }
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
        [Display(Name = "Min Entry Fast Gradient Abs", Description = "Minimum absolute fast EMA gradient required for ENTRY only (default: 0.45)", Order = 7, GroupName = "Parameters")]
        public double MinEntryFastGradientAbs
        {
            get { return minEntryFastGradientAbs; }
            set { minEntryFastGradientAbs = Math.Max(0.0, value); }
        }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Validation Min Fast Gradient Abs", Description = "Minimum absolute fast EMA gradient required to STAY in position (validation during position). Default: 0.25", Order = 8, GroupName = "Parameters")]
        public double ValidationMinFastGradientAbs
        {
            get { return validationMinFastGradientAbs; }
            set { validationMinFastGradientAbs = Math.Max(0.0, value); }
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
        [Display(Name = "Exit Confirm Fast EMA Delta", Description = "Additional Fast EMA move (points) required to confirm a pending exit. LONG exits require EMA to drop by this amount; SHORT exits require EMA to rise by this amount. Default: 0.5", Order = 8, GroupName = "Exit")]
        public double ExitConfirmFastEMADelta
        {
            get { return exitConfirmFastEMADelta; }
            set { exitConfirmFastEMADelta = Math.Max(0.0, value); }
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
