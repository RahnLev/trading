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
        
        // Log file output
        private StreamWriter logWriter;
        private string logFilePath;
        
        // Weak reversal tracking
        private bool inWeakReversalDelay = false;
        private int weakReversalDelayBars = 0;
        
        // Parameters
        private int quantity = 1;
        private int fastEMAPeriod = 10;
        private int slowEMAPeriod = 20;
        private int entryBarDelay = 1;
        private bool enableLogging = true;
        private double weakGradientThreshold = 0.5;  // Threshold for "weak" gradient
        private int weakReversalDelayPeriod = 3;  // Bars to wait after weak reversal
        
        // Exit conditions
        private bool exitOnFastEMAGradient = true;  // Exit when fast EMA gradient reverses
        private bool exitOnSlowEMAGradient = false;  // Exit when slow EMA gradient reverses
        private bool exitOnCloseBelowFastEMA = true;  // Exit when close below fast EMA
        private bool exitOnCloseBelowSlowEMA = false;  // Exit when close below slow EMA (BOTH EMAs required)
        
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
            }
        }
        
        #endregion
        
        #region OnBarUpdate
        
        protected override void OnBarUpdate()
        {
            // Ensure we have enough bars
            if (CurrentBar < BarsRequiredToTrade)
                return;
            
            if (fastEMA == null || slowEMA == null)
                return;
            
            // Get current EMA values
            double fastEMAValue = fastEMA[0];
            double slowEMAValue = slowEMA[0];
            double currentClose = Close[0];

            // CORE DEBUG SNAPSHOT AT TOP OF BAR
            if (enableLogging && IsFirstTickOfBar)
            {
                LogOutput($"BAR SNAPSHOT -> Time:{Time[0]} Bar:{CurrentBar} Close:{currentClose:F2} FastEMA:{fastEMAValue:F2} SlowEMA:{slowEMAValue:F2} MyPos:{myPosition} NTPos:{Position.MarketPosition} CurrSignal:{currentSignal} InWeakDelay:{inWeakReversalDelay} LastTradeBar:{lastTradeBar}");
            }

            // Keep internal myPosition aligned with actual NinjaTrader position when flat
            if (Position.MarketPosition == MarketPosition.Flat && myPosition != "FLAT")
                myPosition = "FLAT";

            // Guard: ensure only one trade (entry or exit) per bar
            bool tradeAlreadyPlacedThisBar = (CurrentBar == lastTradeBar);
            
            // Calculate gradients (current - previous)
            double fastGradient = 0;
            double slowGradient = 0;
            
            if (prevFastEMA != 0 && prevSlowEMA != 0)
            {
                fastGradient = fastEMAValue - prevFastEMA;
                slowGradient = slowEMAValue - prevSlowEMA;
            }

            if (enableLogging && IsFirstTickOfBar)
            {
                LogOutput($"GRADIENTS -> FastGrad:{fastGradient:+0.000;-0.000} SlowGrad:{slowGradient:+0.000;-0.000} PrevFast:{prevFastEMA:F4} PrevSlow:{prevSlowEMA:F4}");
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
            // IMMEDIATE EXIT CONDITIONS - Check BEFORE signal logic
            // Core rule (always enforced):
            //  LONG  -> exit when fastGradient < 0 AND Close < fastEMA
            //  SHORT -> exit when fastGradient > 0 AND Close > fastEMA
            // This ignores slow EMA and always triggers when conditions are met.
            
            // Exit LONG position when fast EMA turns down and price is below fast EMA
            if (myPosition == "LONG" && !tradeAlreadyPlacedThisBar)
            {
                bool coreExit = (fastGradient < 0 && currentClose < fastEMAValue);
                bool shouldExit = coreExit;
                string exitReason = "";

                if (coreExit)
                {
                    exitReason = "FastEMAGrad<0,Close<FastEMA";
                }
                else
                {
                    // Fallback to optional configured conditions only if core rule not met
                    bool gradientCondition = true;
                    bool closeCondition = true;

                    if (exitOnFastEMAGradient)
                    {
                        gradientCondition = gradientCondition && (fastGradient < 0);
                        if (fastGradient < 0)
                            exitReason = "FastEMAGrad<0";
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
                
                if (shouldExit)
                {
                    LogOutput($"IMMEDIATE EXIT CHECK (LONG) -> CoreExit:{coreExit} ShouldExit:{shouldExit} Reason:{exitReason} FastGrad:{fastGradient:+0.000;-0.000} SlowGrad:{slowGradient:+0.000;-0.000} Close:{currentClose:F2} FastEMA:{fastEMAValue:F2} SlowEMA:{slowEMAValue:F2} TradeAlreadyThisBar:{tradeAlreadyPlacedThisBar}");
                    ExitLong("ExitLong");
                    lastTradeBar = CurrentBar;
                    myPosition = "FLAT";
                    // Force strategy flat and idle for a few bars after an immediate exit
                    currentSignal = "FLAT";
                    signalStartBar = -1;
                    inWeakReversalDelay = true;
                    weakReversalDelayBars = CurrentBar + weakReversalDelayPeriod;
                    LogOutput($"    ⏸ IMMEDIATE EXIT (LONG) -> Forcing FLAT and idle until bar {weakReversalDelayBars}");
                    Print(string.Format("[GRAD] {0} | IMMEDIATE EXIT LONG at {1:F2} | Reason: {2}", 
                        Time[0], currentClose, exitReason));
                    LogOutput($"<<< IMMEDIATE EXIT LONG at {currentClose:F2} | {exitReason}");
                    LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                        currentSignal, newSignal, "LONG", "EXIT", $"IMMEDIATE_{exitReason}");
                    
                    // Set weak reversal delay if conditions are weak
                    double priceGradient3Bar = 0;
                    if (CurrentBar >= 3) priceGradient3Bar = (Close[0] - Close[3]) / 3.0;
                    if (Math.Abs(priceGradient3Bar) < weakGradientThreshold && Math.Abs(fastGradient) < weakGradientThreshold)
                    {
                        inWeakReversalDelay = true;
                        weakReversalDelayBars = CurrentBar + weakReversalDelayPeriod;
                        LogOutput($"    ⏸ Setting weak reversal delay");
                    }
                }
            }
            
            // Exit SHORT position when fast EMA turns up and price is above fast EMA
            if (myPosition == "SHORT" && !tradeAlreadyPlacedThisBar)
            {
                bool coreExit = (fastGradient > 0 && currentClose > fastEMAValue);
                bool shouldExit = coreExit;
                string exitReason = "";

                if (coreExit)
                {
                    exitReason = "FastEMAGrad>0,Close>FastEMA";
                }
                else
                {
                    bool gradientCondition = true;
                    bool closeCondition = true;

                    if (exitOnFastEMAGradient)
                    {
                        gradientCondition = gradientCondition && (fastGradient > 0);
                        if (fastGradient > 0)
                            exitReason = "FastEMAGrad>0";
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
                    LogOutput($"IMMEDIATE EXIT CHECK (SHORT) -> CoreExit:{coreExit} ShouldExit:{shouldExit} Reason:{exitReason} FastGrad:{fastGradient:+0.000;-0.000} SlowGrad:{slowGradient:+0.000;-0.000} Close:{currentClose:F2} FastEMA:{fastEMAValue:F2} SlowEMA:{slowEMAValue:F2} TradeAlreadyThisBar:{tradeAlreadyPlacedThisBar}");
                    ExitShort("ExitShort");
                    lastTradeBar = CurrentBar;
                    myPosition = "FLAT";
                    // Force strategy flat and idle for a few bars after an immediate exit
                    currentSignal = "FLAT";
                    signalStartBar = -1;
                    inWeakReversalDelay = true;
                    weakReversalDelayBars = CurrentBar + weakReversalDelayPeriod;
                    LogOutput($"    ⏸ IMMEDIATE EXIT (SHORT) -> Forcing FLAT and idle until bar {weakReversalDelayBars}");
                    Print(string.Format("[GRAD] {0} | IMMEDIATE EXIT SHORT at {1:F2} | Reason: {2}", 
                        Time[0], currentClose, exitReason));
                    LogOutput($"<<< IMMEDIATE EXIT SHORT at {currentClose:F2} | {exitReason}");
                    LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                        currentSignal, newSignal, "SHORT", "EXIT", $"IMMEDIATE_{exitReason}");
                    
                    // Set weak reversal delay if conditions are weak
                    double priceGradient3Bar = 0;
                    if (CurrentBar >= 3) priceGradient3Bar = (Close[0] - Close[3]) / 3.0;
                    if (Math.Abs(priceGradient3Bar) < weakGradientThreshold && Math.Abs(fastGradient) < weakGradientThreshold)
                    {
                        inWeakReversalDelay = true;
                        weakReversalDelayBars = CurrentBar + weakReversalDelayPeriod;
                        LogOutput($"    ⏸ Setting weak reversal delay");
                    }
                }
            }
            
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
                
                // Exit existing position when signal changes
                if (myPosition != "FLAT" && !tradeAlreadyPlacedThisBar)
                {
                    LogOutput($"SIGNAL CHANGE EXIT CHECK -> MyPos:{myPosition} CurrSignal:{currentSignal} NewSignal:{newSignal} TradeAlreadyThisBar:{tradeAlreadyPlacedThisBar} WeakRev:{isWeakReversal} PriceGrad:{priceGradient:+0.000;-0.000} FastGrad:{fastGradient:+0.000;-0.000}");
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
            if (currentSignal != "FLAT" && signalStartBar == -1 && myPosition == "FLAT" && !inWeakReversalDelay)
            {
                signalStartBar = CurrentBar;
                LogOutput($"SIGNAL START -> CurrSignal:{currentSignal} StartBar:{signalStartBar} MyPos:{myPosition} InWeakDelay:{inWeakReversalDelay}");
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
                    if (currentSignal == "LONG")
                    {
                        EnterLong(quantity, "EnterLong");
                        lastTradeBar = CurrentBar;
                        myPosition = "LONG";
                        entryBar = CurrentBar;
                        Print(string.Format("[GRAD] {0} | ENTER LONG at {1:F2} on bar #{2} of signal | FastGrad: {3:+0.000;-0.000} | SlowGrad: {4:+0.000;-0.000}",
                            Time[0], currentClose, barsInSignal, fastGradient, slowGradient));
                        LogOutput($">>> ENTER LONG at {currentClose:F2} (Bar {barsInSignal}/{entryBarDelay}) | FastGrad: {fastGradient:+0.000;-0.000} | SlowGrad: {slowGradient:+0.000;-0.000}");
                        LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                            currentSignal, currentSignal, "LONG", "ENTRY", $"EnterLong_Bar{barsInSignal}");
                    }
                    else if (currentSignal == "SHORT")
                    {
                        EnterShort(quantity, "EnterShort");
                        lastTradeBar = CurrentBar;
                        myPosition = "SHORT";
                        entryBar = CurrentBar;
                        Print(string.Format("[GRAD] {0} | ENTER SHORT at {1:F2} on bar #{2} of signal | FastGrad: {3:+0.000;-0.000} | SlowGrad: {4:+0.000;-0.000}",
                            Time[0], currentClose, barsInSignal, fastGradient, slowGradient));
                        LogOutput($">>> ENTER SHORT at {currentClose:F2} (Bar {barsInSignal}/{entryBarDelay}) | FastGrad: {fastGradient:+0.000;-0.000} | SlowGrad: {slowGradient:+0.000;-0.000}");
                        LogToCSV(Time[0], CurrentBar, currentClose, fastEMAValue, slowEMAValue, fastGradient, slowGradient,
                            currentSignal, currentSignal, "SHORT", "ENTRY", $"EnterShort_Bar{barsInSignal}");
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
            
            // Store current EMA values for next bar's gradient calculation
            prevFastEMA = fastEMAValue;
            prevSlowEMA = slowEMAValue;
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
                    csvWriter.WriteLine("Timestamp,Bar,Close,FastEMA,SlowEMA,FastGradient,SlowGradient,PrevSignal,NewSignal,MyPosition,ActualPosition,Action,Notes");
                    csvHeaderWritten = true;
                }
                
                // Capture actual NinjaTrader position for logging (brokerage-level position)
                string actualPos = Position.MarketPosition.ToString();
                
                // Write data row
                csvWriter.WriteLine(string.Format("{0:yyyy-MM-dd HH:mm:ss},{1},{2:F2},{3:F2},{4:F2},{5:F4},{6:F4},{7},{8},{9},{10},{11},{12}",
                    time, bar, close, fastEma, slowEma, fastGrad, slowGrad, prevSignal, newSignal, position, actualPos, action, notes));
                csvWriter.Flush();
            }
            catch (Exception ex)
            {
                Print(string.Format("[GRAD] CSV Error: {0}", ex.Message));
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
        [Display(Name = "Enable Logging", Description = "Enable detailed logging to Output window", Order = 5, GroupName = "Parameters")]
        public bool EnableLogging
        {
            get { return enableLogging; }
            set { enableLogging = value; }
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
        [Range(1, 20)]
        [Display(Name = "Weak Reversal Delay", Description = "Bars to wait after weak reversal before allowing re-entry (default: 3)", Order = 7, GroupName = "Parameters")]
        public int WeakReversalDelayPeriod
        {
            get { return weakReversalDelayPeriod; }
            set { weakReversalDelayPeriod = Math.Max(1, value); }
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
