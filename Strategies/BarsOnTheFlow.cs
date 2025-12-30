#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        [Display(Name = "Contracts", Order = 1, GroupName = "Trading/Entry")]
        public int Contracts { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Disable Real-Time Trading", Order = 2, GroupName = "Trading/Entry", Description = "When enabled, the strategy will only trade on historical data and will not place any trades in real-time mode.")]
        public bool DisableRealTimeTrading { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "ExitOnTrendBreak", Order = 3, GroupName = "Trading/Entry")]
        public bool ExitOnTrendBreak { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "ExitOnRetrace", Order = 1, GroupName = "Retrace")]
        public bool ExitOnRetrace { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "TrendRetraceFraction", Order = 2, GroupName = "Retrace")]
        public double TrendRetraceFraction { get; set; } = 0.66; // end trend visuals when profit gives back 66% of MFE

        [NinjaScriptProperty]
        [Display(Name = "EnableTrendOverlay", Order = 1, GroupName = "Display/Visualization")]
        public bool EnableTrendOverlay { get; set; } = true; // draw semi-transparent overlay for active trend

        [NinjaScriptProperty]
        [Display(Name = "Show Bar Index Labels", Order = 2, GroupName = "Display/Visualization")]
        public bool ShowBarIndexLabels
        {
            get { return showBarIndexLabels; }
            set { showBarIndexLabels = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Show Fast Grad Labels", Order = 3, GroupName = "Display/Visualization")]
        public bool ShowFastGradLabels
        {
            get { return showFastGradLabels; }
            set { showFastGradLabels = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Show EMA Crossover Labels", Order = 4, GroupName = "Display/Visualization", Description = "Shows green circle above bar when Close > Fast EMA > Slow EMA, red circle below bar when Close < Fast EMA < Slow EMA.")]
        public bool ShowEmaCrossoverLabels { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Show Tick Gap Indicators", Order = 5, GroupName = "Display/Visualization", Description = "Show visual indicators (✓/✗) for minimum tick gap requirements: Close to Fast EMA and Fast EMA to Slow EMA.")]
        public bool ShowTickGapIndicators { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "EnableShorts", Order = 3, GroupName = "Trading/Entry")]
        public bool EnableShorts { get; set; } = true; // trade shorts symmetrically to longs

        [NinjaScriptProperty]
        [Display(Name = "AvoidShortsOnGoodCandle", Order = 4, GroupName = "Trading/Entry")]
        public bool AvoidShortsOnGoodCandle { get; set; } = true; // block shorts that trigger off an up-close bar and restart counting

        [NinjaScriptProperty]
        [Display(Name = "AvoidLongsOnBadCandle", Order = 5, GroupName = "Trading/Entry")]
        public bool AvoidLongsOnBadCandle { get; set; } = true; // block longs that trigger off a down-close bar and restart counting

        [NinjaScriptProperty]
        [Display(Name = "ExitIfEntryBarOpposite", Order = 6, GroupName = "Trading/Entry", Description = "Exit immediately if the entry bar closes opposite to trade direction (e.g., long entry bar closes down). Helps avoid positions that show immediate weakness.")]
        public bool ExitIfEntryBarOpposite { get; set; } = true; // exit if the entry bar closes opposite to trade direction

        [NinjaScriptProperty]
        [Display(Name = "ReverseOnTrendBreak", Order = 7, GroupName = "Trading/Entry")]
        public bool ReverseOnTrendBreak { get; set; } = false; // reverse position instead of just exiting on trend break

        [NinjaScriptProperty]
        [Display(Name = "Enable BarsOnTheFlow Trend Detection", Order = 0, GroupName = "BarsOnTheFlow", Description = "Master switch: When disabled, all BarsOnTheFlow trend detection parameters (TrendLookbackBars, MinMatchingBars, UsePnLTiebreaker) are ignored. No trend-based entries/exits will occur.")]
        public bool EnableBarsOnTheFlowTrendDetection { get; set; } = true; // master switch for trend detection

        [NinjaScriptProperty]
        [Range(3, 10)]
        [Display(Name = "TrendLookbackBars", Order = 1, GroupName = "BarsOnTheFlow", Description = "Number of recent bars to analyze for trend detection (3-10). Independent of minimum required matching bars. Only used when EnableBarsOnTheFlowTrendDetection is true.")]
        public int TrendLookbackBars { get; set; } = 5; // lookback window for trend analysis

        [NinjaScriptProperty]
        [Range(2, 10)]
        [Display(Name = "MinMatchingBars", Order = 2, GroupName = "BarsOnTheFlow", Description = "Minimum number of matching good/bad bars required within the lookback window to trigger a trend (2-10). Only used when EnableBarsOnTheFlowTrendDetection is true.")]
        public int MinMatchingBars { get; set; } = 3; // minimum matching bars required

        [NinjaScriptProperty]
        [Display(Name = "UsePnLTiebreaker", Order = 3, GroupName = "BarsOnTheFlow", Description = "When enabled, allows trend entry when minimum bars not met but net PnL supports the direction. When disabled, strictly requires minimum matching bars. Only used when EnableBarsOnTheFlowTrendDetection is true.")]
        public bool UsePnLTiebreaker { get; set; } = false; // PnL tiebreaker for marginal patterns

        [NinjaScriptProperty]
        [Display(Name = "Enable EMA Crossover Filter", Order = 0, GroupName = "EMA Crossover Filter", Description = "When enabled, requires EMA crossover confirmation for entries. Can be used alone or together with other filters.")]
        public bool UseEmaCrossoverFilter { get; set; } = false; // enable EMA crossover confirmation filter

        [NinjaScriptProperty]
        [Range(5, 50)]
        [Display(Name = "EMA Fast Period", Order = 1, GroupName = "EMA Crossover Filter", Description = "Fast EMA period for crossover confirmation (e.g., 10).")]
        public int EmaFastPeriod { get; set; } = 10;

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "EMA Slow Period", Order = 2, GroupName = "EMA Crossover Filter", Description = "Slow EMA period for crossover confirmation (e.g., 20).")]
        public int EmaSlowPeriod { get; set; } = 20;

        [NinjaScriptProperty]
        [Range(0, 5)]
        [Display(Name = "Crossover Window Bars", Order = 3, GroupName = "EMA Crossover Filter", Description = "How many bars apart the crossovers can occur (0 = same bar only, 1-5 = within N bars). Higher values = more signals but less strict.")]
        public int EmaCrossoverWindowBars { get; set; } = 0; // 0 = same bar only (strict), 1-5 = within N bars (relaxed)

        [NinjaScriptProperty]
        [Display(Name = "Require Crossover", Order = 4, GroupName = "EMA Crossover Filter", Description = "When true, requires actual crossovers. When false, allows entry if conditions are already met (price above/below EMAs, fast above/below slow).")]
        public bool EmaCrossoverRequireCrossover { get; set; } = true; // true = require crossover, false = allow if conditions already met

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Min Ticks: Close to Fast EMA", Order = 5, GroupName = "EMA Crossover Filter", Description = "Minimum number of ticks between Close price and Fast EMA. Entry requires Close >= Fast EMA + (this value * TickSize) for longs, or Close <= Fast EMA - (this value * TickSize) for shorts. 0 = no minimum gap required.")]
        public int EmaCrossoverMinTicksCloseToFast { get; set; } = 0; // Minimum ticks between Close and Fast EMA

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Min Ticks: Fast EMA to Slow EMA", Order = 6, GroupName = "EMA Crossover Filter", Description = "Minimum number of ticks between Fast EMA and Slow EMA. Entry requires Fast EMA >= Slow EMA + (this value * TickSize) for longs, or Fast EMA <= Slow EMA - (this value * TickSize) for shorts. 0 = no minimum gap required.")]
        public int EmaCrossoverMinTicksFastToSlow { get; set; } = 0; // Minimum ticks between Fast EMA and Slow EMA

        [NinjaScriptProperty]
        [Range(0, 5)]
        [Display(Name = "Cooldown Bars After Crossover", Order = 7, GroupName = "EMA Crossover Filter", Description = "Number of candles to wait after a crossover before allowing entry. 0 = no cooldown (enter immediately), 1-5 = wait N candles after crossover before entry.")]
        public int EmaCrossoverCooldownBars { get; set; } = 0; // Cooldown bars after crossover

        [NinjaScriptProperty]
        [Display(Name = "GradientFilterEnabled", Order = 1, GroupName = "Fast EMA/Gradient")]
        public bool GradientFilterEnabled { get; set; } = false; // enable gradient-based entry filtering

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Fast EMA Period", Order = 2, GroupName = "Fast EMA/Gradient")]
        public int FastEmaPeriod { get; set; } = 10;

        [NinjaScriptProperty]
        [Range(2, int.MaxValue)]
        [Display(Name = "FastGradLookbackBars", Order = 3, GroupName = "Fast EMA/Gradient")]
        public int FastGradLookbackBars { get; set; } = 2; // gradient lookback - use 2 for immediate visual slope

        [NinjaScriptProperty]
        [Display(Name = "Use Chart-Scaled FastGrad Deg", Order = 4, GroupName = "Fast EMA/Gradient")]
        public bool UseChartScaledFastGradDeg { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "EnableDashboardDiagnostics", Order = 1, GroupName = "Database/Dashboard")]
        public bool EnableDashboardDiagnostics { get; set; } = true; // when true, post compact diags (incl. gradient) to dashboard

        [NinjaScriptProperty]
        [Display(Name = "DashboardBaseUrl", Order = 2, GroupName = "Database/Dashboard")]
        public string DashboardBaseUrl { get; set; } = "http://localhost:51888";

        [NinjaScriptProperty]
        [Display(Name = "DashboardAsyncHistorical", Order = 3, GroupName = "Database/Dashboard", Description = "If true, dashboard posts run async even during historical playback (faster but may miss late bars). If false, historical runs sync (slower but complete).")]
        public bool DashboardAsyncHistorical { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0, 60)]
        [Display(Name = "StateUpdateIntervalSeconds", Order = 4, GroupName = "Database/Dashboard", Description = "How often to post state updates (0 = bar close only, 1-60 = update every N seconds on each tick). Lower values = more responsive but more server load.")]
        public int StateUpdateIntervalSeconds { get; set; } = 0; // 0 = bar close only (default)

        [NinjaScriptProperty]
        [Display(Name = "StateUpdateOnPriceChange", Order = 5, GroupName = "Database/Dashboard", Description = "If true, post state update whenever the price changes (efficient, only updates when needed).")]
        public bool StateUpdateOnPriceChange { get; set; } = false; // update on price change

        [NinjaScriptProperty]
        [Display(Name = "SkipShortsAboveGradient", Order = 5, GroupName = "Fast EMA/Gradient")]
        public double SkipShortsAboveGradient { get; set; } = -7.0; // skip shorts when EMA gradient > this degrees

        [NinjaScriptProperty]
        [Display(Name = "SkipLongsBelowGradient", Order = 6, GroupName = "Fast EMA/Gradient")]
        public double SkipLongsBelowGradient { get; set; } = 7.0; // skip longs when EMA gradient < this degrees

        [NinjaScriptProperty]
        [Display(Name = "AllowMidBarGradientEntry", Order = 7, GroupName = "Fast EMA/Gradient")]
        public bool AllowMidBarGradientEntry { get; set; } = false; // allow entry mid-bar when gradient crosses threshold

        [NinjaScriptProperty]
        [Display(Name = "AllowMidBarGradientExit", Order = 8, GroupName = "Fast EMA/Gradient")]
        public bool AllowMidBarGradientExit { get; set; } = false; // allow exit mid-bar when gradient crosses unfavorable threshold

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Stop Loss Points", Order = 1, GroupName = "Stop Loss")]
        public int StopLossPoints { get; set; } = 20; // stop loss in points (0 = disabled)

        [NinjaScriptProperty]
        [Display(Name = "UseTrailingStop", Order = 2, GroupName = "Stop Loss", Description = "When enabled, stop loss trails price dynamically. When disabled, stop loss is static at entry price minus StopLossPoints.")]
        public bool UseTrailingStop { get; set; } = false; // use trailing stop instead of static stop

        [NinjaScriptProperty]
        [Display(Name = "UseDynamicStopLoss", Order = 3, GroupName = "Stop Loss", Description = "When enabled, stop loss is calculated based on volatility data. When disabled, uses fixed StopLossPoints value.")]
        public bool UseDynamicStopLoss { get; set; } = false; // calculate stop loss from volatility data

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "DynamicStopLookback", Order = 4, GroupName = "Stop Loss", Description = "Number of recent candles to average for dynamic stop loss calculation (fallback when API unavailable).")]
        public int DynamicStopLookback { get; set; } = 5; // number of bars to average for dynamic stop

        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "DynamicStopMultiplier", Order = 5, GroupName = "Stop Loss", Description = "Multiplier for average candle range (e.g., 1.0 = 1x average range, 1.5 = 1.5x average range).")]
        public double DynamicStopMultiplier { get; set; } = 1.0; // multiplier for average range

        [NinjaScriptProperty]
        [Display(Name = "UseVolumeAwareStop", Order = 6, GroupName = "Stop Loss", Description = "Query volatility database for hour/volume adjusted stop loss. Requires server running.")]
        public bool UseVolumeAwareStop { get; set; } = true; // use API for volume-aware stops

        [NinjaScriptProperty]
        [Display(Name = "UseBreakEven", Order = 7, GroupName = "Stop Loss", Description = "Move stop to break-even (or better) after reaching profit target.")]
        public bool UseBreakEven { get; set; } = true; // enable break-even stop

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "BreakEvenTrigger", Order = 8, GroupName = "Stop Loss", Description = "Points of profit required to activate break-even stop (e.g., 5 = activate after 5 points profit).")]
        public int BreakEvenTrigger { get; set; } = 5; // profit points needed to trigger break-even

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "BreakEvenOffset", Order = 9, GroupName = "Stop Loss", Description = "Points above entry to place break-even stop (e.g., 2 = stop at entry+2 points for small profit lock).")]
        public int BreakEvenOffset { get; set; } = 2; // points above entry for break-even stop

        [NinjaScriptProperty]
        [Display(Name = "UseEmaTrailingStop", Order = 10, GroupName = "Stop Loss", Description = "When enabled, stop loss follows Fast EMA value. For longs: stop = Fast EMA (only moves up). For shorts: stop = Fast EMA (only moves down). When enabled, this OVERRIDES UseTrailingStop, UseDynamicStopLoss, StopLossPoints, and UseBreakEven.")]
        public bool UseEmaTrailingStop { get; set; } = false; // use Fast EMA as trailing stop loss

        [NinjaScriptProperty]
        [Display(Name = "EmaStopRequireFullCandleBelow", Order = 11, GroupName = "Stop Loss", Description = "When enabled with UseEmaTrailingStop, stop loss only triggers if the entire completed candle range (High to Low) is below (for longs) or above (for shorts) the Fast EMA stop loss. Checked on bar close.")]
        public bool EmaStopRequireFullCandleBelow { get; set; } = false; // require entire candle range to be below/above stop loss

        [NinjaScriptProperty]
        [Display(Name = "EnableOpportunityLog", Order = 1, GroupName = "Logging/JSON")]
        public bool EnableOpportunityLog { get; set; } = true; // log every bar with opportunity analysis

        private readonly Queue<bool> recentGood = new Queue<bool>(10);
        private readonly Queue<double> recentPnl = new Queue<double>(10);

        private double lastFastEmaSlope = double.NaN;
        private DateTime lastStateUpdateTime = DateTime.MinValue; // for throttling state updates
        private double lastStateUpdatePrice = double.NaN; // for price-change updates

        // Optional bar index labels (copied from BareOhlcLogger)
        private bool showBarIndexLabels = true;
        private bool showFastGradLabels = true;

        // Logging
        private StreamWriter logWriter;
        private string logFilePath;
        private bool logInitialized;

        // Dashboard post diagnostics (limit noise to first success/failure)
        private bool dashboardPostLoggedSuccess;
        private bool dashboardPostLoggedFailure;
        
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
        
        // EMA crossover filter indicators
        private EMA emaFast;
        private EMA emaSlow;

        // EMA trailing stop loss tracking
        private double entryFastEmaValue = double.NaN; // Fast EMA value at entry
        private double currentEmaStopLoss = double.NaN; // Current EMA-based stop loss price (only moves in favorable direction)
        
        // Reset EMA stop loss tracking when position is closed
        private void ResetEmaStopLoss()
        {
            entryFastEmaValue = double.NaN;
            currentEmaStopLoss = double.NaN;
        }

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
        
        // Track last crossover bars for cooldown period
        private int lastLongCrossoverBar = -1;
        private int lastShortCrossoverBar = -1;
        
        // Track last exit bars for cooldown period (cooldown counts from exit, not crossover)
        private int lastLongExitBar = -1;
        private int lastShortExitBar = -1;

        // Track break-even activation
        private bool breakEvenActivated = false;
        private double breakEvenEntryPrice = double.NaN;

        // Trade tracking for dashboard logging
        private int currentTradeEntryBar = -1;
        private double currentTradeEntryPrice = double.NaN;
        private DateTime currentTradeEntryTime = DateTime.MinValue;
        private MarketPosition currentTradeDirection = MarketPosition.Flat;
        private int currentTradeContracts = 0;
        private double currentTradeMFE = 0.0;  // Maximum Favorable Excursion
        private double currentTradeMAE = 0.0;   // Maximum Adverse Excursion
        private double currentTradeStopLossPoints = 0.0; // Track the stop loss distance that was set
        private string currentTradeEntryReason = ""; // Track all reasons/filters that applied when entering the trade
        private MarketPosition previousPosition = MarketPosition.Flat;

        private double lastFastEmaGradDeg = double.NaN;
        private Dictionary<int, double> gradientByBar = new Dictionary<int, double>(); // Store gradient per bar for accurate logging

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
                
                // Initialize EMA crossover filter indicators (always initialize, filter is checked at runtime)
                emaFast = EMA(EmaFastPeriod);
                emaSlow = EMA(EmaSlowPeriod);
                
                InitializeLog();
                
                // Clear tables on fresh run
                ClearTradesTable();
                ClearBarsTable();
                ClearBarSamples();
                
                // Export strategy state for API queries
                ExportStrategyState();
                
                // Initialize bar navigation panel on UI thread
                if (ChartControl != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() => CreateBarNavPanel());
                }
            }
            else if (State == State.Realtime)
            {
                // When transitioning to real-time, if trading is disabled, cancel any pending orders
                if (DisableRealTimeTrading)
                {
                    Print($"[REALTIME_BLOCK] Transitioning to real-time - DisableRealTimeTrading=True, canceling any pending orders");
                    // Cancel any pending entry orders
                    foreach (Order order in Account.Orders)
                    {
                        if (order != null && order.OrderState == OrderState.Working && order.Instrument == Instrument)
                        {
                            CancelOrder(order);
                        }
                    }
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

        #region Main Bar Update Handler

        protected override void OnBarUpdate()
        {
            // Simple check: if real-time trading is disabled and we're NOT in historical mode, skip all trading logic
            // This catches all real-time scenarios (State.Realtime, State.Transition, etc.)
            if (DisableRealTimeTrading && State != State.Historical)
            {
                Print($"[REALTIME_BLOCK] Bar {CurrentBar}: Real-time trading disabled - skipping OnBarUpdate. State={State}, DisableRealTimeTrading={DisableRealTimeTrading}");
                return;
            }
            
            // Also block orders on the last few bars of historical data to prevent orders from being submitted
            // right before transitioning to real-time (which would then try to execute in real-time)
            if (DisableRealTimeTrading && State == State.Historical && Bars != null && CurrentBar >= Bars.Count - 3)
            {
                Print($"[REALTIME_BLOCK] Bar {CurrentBar}: Near end of historical data ({Bars.Count} bars total) - blocking orders to prevent real-time execution. DisableRealTimeTrading={DisableRealTimeTrading}");
                // Don't return here - allow other logic to run, but block actual order submission
                // We'll check again in ProcessTradingDecisions
            }

            // ====================================================================
            // SECTION 1: CSV LOGGING - Process pending logs from previous bar
            // ====================================================================
            ProcessPendingCSVLogs();

            if (CurrentBar < 1)
                return; // need a completed bar to score

            // ====================================================================
            // SECTION 2: MID-BAR PROCESSING (for gradient entry/exit)
            // ====================================================================
            if (!IsFirstTickOfBar)
            {
                ProcessMidBarOperations();
                return; // Exit early for mid-bar ticks - bar close logic below
            }

            // ====================================================================
            // SECTION 3: BAR CLOSE PROCESSING
            // ====================================================================
            
            // 3.1: Database Operations (state updates, bar samples)
            ProcessDatabaseOperations();

            // 3.2: Calculate market state (trends, gradients, etc.)
            double prevOpen = Open[1];
            double prevClose = Close[1];
            RecordCompletedBar(prevOpen, prevClose);

            bool prevGood = prevClose > prevOpen;
            bool prevBad = prevClose < prevOpen;
            
            // BarsOnTheFlow trend detection - can be completely disabled via master switch
            bool trendUp = EnableBarsOnTheFlowTrendDetection ? IsTrendUp() : false;
            bool trendDown = EnableBarsOnTheFlowTrendDetection && EnableShorts ? IsTrendDown() : false;
            
            // If trend detection is disabled but EMA crossover filter is enabled, use EMA crossover as signal generator
            if (!EnableBarsOnTheFlowTrendDetection && UseEmaCrossoverFilter)
            {
                // EMA crossover becomes the signal generator when trend detection is off
                bool emaLongSignal = EmaCrossoverFilterPasses(true);
                bool emaShortSignal = EnableShorts && EmaCrossoverFilterPasses(false);
                
                Print($"[TREND_SIGNAL_DEBUG] Bar {CurrentBar}: EnableBarsOnTheFlowTrendDetection={EnableBarsOnTheFlowTrendDetection}, UseEmaCrossoverFilter={UseEmaCrossoverFilter}, EnableShorts={EnableShorts}");
                Print($"[TREND_SIGNAL_DEBUG] Bar {CurrentBar}: emaLongSignal={emaLongSignal}, emaShortSignal={emaShortSignal}");
                
                if (emaLongSignal)
                    trendUp = true;
                if (emaShortSignal)
                    trendDown = true;
                
                Print($"[TREND_SIGNAL_DEBUG] Bar {CurrentBar}: After EMA check - trendUp={trendUp}, trendDown={trendDown}");
            }
            
            bool allowShortThisBar = !(AvoidShortsOnGoodCandle && prevGood);
            bool allowLongThisBar = !(AvoidLongsOnBadCandle && prevBad);
            
            // Compute gradient
            int gradWindow = Math.Max(2, FastGradLookbackBars);
                        double regDeg;
                        lastFastEmaSlope = ComputeFastEmaGradient(gradWindow, out regDeg);

                        double chartDeg = double.NaN;
                        if (UseChartScaledFastGradDeg)
                        {
                            chartDeg = ComputeChartScaledFastEmaDeg(gradWindow);
                        }

                        lastFastEmaGradDeg = !double.IsNaN(chartDeg) ? chartDeg : regDeg;
                        if (!double.IsNaN(lastFastEmaGradDeg))
                            gradientByBar[CurrentBar] = lastFastEmaGradDeg;

            // Reset mid-bar gradient waiting flags at start of new bar
            waitingForLongGradient = false;
            waitingForShortGradient = false;
            waitingToExitLongOnGradient = false;
            waitingToExitShortOnGradient = false;

            // 3.3: TRADING DECISION LOGIC (all entry/exit filters and guards)
            // Double-check before processing trading decisions
            if (DisableRealTimeTrading && State != State.Historical)
            {
                Print($"[REALTIME_BLOCK] Bar {CurrentBar}: Blocking ProcessTradingDecisions - State={State}");
                return;
            }
            
            // Also block if we're on the last few bars of historical data (to prevent orders that would execute in real-time)
            if (DisableRealTimeTrading && State == State.Historical && Bars != null && CurrentBar >= Bars.Count - 3)
            {
                Print($"[REALTIME_BLOCK] Bar {CurrentBar}: Blocking ProcessTradingDecisions - last 3 bars of historical data ({Bars.Count} total) to prevent real-time execution");
                return;
            }
            
            bool placedEntry = ProcessTradingDecisions(prevOpen, prevClose, prevGood, prevBad, trendUp, trendDown, allowLongThisBar, allowShortThisBar);

            // 3.4: Visualization (trend overlays, labels)
            UpdateVisualizations(trendUp, trendDown);

            // 3.5: CSV Logging (bar snapshots, opportunity analysis)
            ProcessCSVLogging(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown, placedEntry);
        }
        
        #endregion

        #region Trading Decision Logic - All Entry/Exit Filters and Guards
        
        /// <summary>
        /// ALL TRADING DECISION LOGIC IS HERE
        /// This method contains all entry/exit filters, guards, and decision logic.
        /// To disable trading logic, comment out the call to this method in OnBarUpdate.
        /// To try alternative logic, create ProcessTradingDecisionsV2() and swap the call.
        /// 
        /// ====================================================================
        /// OHLC DATA USAGE GUIDELINES:
        /// ====================================================================
        /// 
        /// MUST USE FINAL OHLC [1] (Previous Completed Bar):
        /// - Trend detection (IsTrendUp/IsTrendDown) - analyzes completed bars
        /// - Gradient calculation for bar-close decisions - uses completed EMA values
        /// - Entry filters (AvoidShortsOnGoodCandle, AvoidLongsOnBadCandle) - based on completed bar
        /// - ExitIfEntryBarOpposite - checks if entry bar closed opposite (completed bar)
        /// - Bar snapshots/logging - records completed bar data
        /// 
        /// REASON: These decisions are made on the FIRST TICK of a new bar, when:
        /// - [1] = Previous bar (completed, final OHLC) ✓ Use this for decisions
        /// - [0] = New bar (just started, incomplete data) ✗ Don't use for decisions
        /// 
        /// CAN USE CURRENT OHLC [0] (Current Bar - Forming):
        /// - Mid-bar gradient entry/exit checks (CheckMidBarGradientEntry/Exit) - uses real-time EMA[0]
        /// - Break-even stop management - uses current price Close[0]
        /// - Trailing stop updates - uses current price
        /// - Real-time position management - uses current price
        /// - State updates during mid-bar ticks - uses current price
        /// 
        /// REASON: These decisions are made DURING the current bar formation, when:
        /// - [0] = Current bar (forming, real-time data) ✓ Use this for real-time decisions
        /// - [1] = Previous bar (already completed) ✓ Use for comparison/reference
        /// 
        /// ====================================================================
        /// </summary>
        private bool ProcessTradingDecisions(double prevOpen, double prevClose, bool prevGood, bool prevBad, 
            bool trendUp, bool trendDown, bool allowLongThisBar, bool allowShortThisBar)
        {
            Print($"[PROCESS_TRADING_DEBUG] Bar {CurrentBar}: Starting ProcessTradingDecisions");
            Print($"[PROCESS_TRADING_DEBUG] Bar {CurrentBar}: trendUp={trendUp}, trendDown={trendDown}, prevGood={prevGood}, prevBad={prevBad}");
            Print($"[PROCESS_TRADING_DEBUG] Bar {CurrentBar}: allowLongThisBar={allowLongThisBar}, allowShortThisBar={allowShortThisBar}");
            Print($"[PROCESS_TRADING_DEBUG] Bar {CurrentBar}: Position={Position.MarketPosition}, intendedPosition={intendedPosition}");
            Print($"[PROCESS_TRADING_DEBUG] Bar {CurrentBar}: pendingLongFromBad={pendingLongFromBad}, pendingShortFromGood={pendingShortFromGood}");
            Print($"[PROCESS_TRADING_DEBUG] Bar {CurrentBar}: GradientFilterEnabled={GradientFilterEnabled}, lastFastEmaGradDeg={lastFastEmaGradDeg:F2}°");
            
            bool placedEntry = false;
            MarketPosition currentPos = Position.MarketPosition;

            // ====================================================================
            // GUARD 1: Exit if entry bar closes opposite to position
            // Uses FINAL OHLC [1] - Checks if the entry bar (previous completed bar) closed opposite
            // ====================================================================
            if (ExitIfEntryBarOpposite && lastEntryBarIndex == CurrentBar - 1 && currentPos != MarketPosition.Flat)
            {
                // Uses [1] because we're checking the completed entry bar's final close
                bool entryBarWasGood = Close[1] > Open[1];
                bool entryBarWasBad = Close[1] < Open[1];

                bool longExits = currentPos == MarketPosition.Long && entryBarWasBad;
                bool shortExits = currentPos == MarketPosition.Short && entryBarWasGood;

                if (longExits)
                {
                    Print($"[EXIT_DEBUG] Bar {CurrentBar}: ExitIfEntryBarOpposite triggering LONG exit - entry bar was BAD (O:{Open[1]:F2}, C:{Close[1]:F2})");
                    CaptureDecisionContext(Open[1], Close[1], true, true, trendUp, trendDown);
                    ExitLong("BarsOnTheFlowEntryBarOpp", "BarsOnTheFlowLong");
                    intendedPosition = MarketPosition.Flat;
                    RecordExitForCooldown(MarketPosition.Long);
                    return false; // Exit early, no entry placed
                }

                if (shortExits)
                {
                    Print($"[EXIT_DEBUG] Bar {CurrentBar}: ExitIfEntryBarOpposite triggering SHORT exit - entry bar was GOOD (O:{Open[1]:F2}, C:{Close[1]:F2})");
                    CaptureDecisionContext(Open[1], Close[1], true, true, trendUp, trendDown);
                    ExitShort("BarsOnTheFlowEntryBarOppS", "BarsOnTheFlowShort");
                    intendedPosition = MarketPosition.Flat;
                    RecordExitForCooldown(MarketPosition.Short);
                    return false; // Exit early, no entry placed
                }
            }

            // ====================================================================
            // GUARD 2: Break-even stop management
            // Uses CURRENT OHLC [0] - Real-time position management during bar formation
            // When UseEmaTrailingStop is enabled, break-even acts as a floor/ceiling for the EMA stop
            // ====================================================================
            if (UseBreakEven && currentPos != MarketPosition.Flat && !breakEvenActivated)
            {
                // Uses current position and current price (real-time, not bar-close)
                double currentProfit = Position.GetUnrealizedProfitLoss(PerformanceUnit.Points);
                
                if (currentProfit >= BreakEvenTrigger)
                {
                    // Calculate break-even stop level
                    double entryPrice = Position.AveragePrice;
                    double breakEvenStopPrice;
                    string orderName;
                    
                    if (currentPos == MarketPosition.Long)
                    {
                        breakEvenStopPrice = entryPrice + BreakEvenOffset;
                        orderName = "BarsOnTheFlowLong";
                        
                        // If EMA trailing stop is enabled, break-even becomes a floor (minimum stop level)
                        // Otherwise, set it directly
                        if (!UseEmaTrailingStop)
                        {
                            SetStopLoss(orderName, CalculationMode.Price, breakEvenStopPrice, false);
                            currentTradeStopLossPoints = BreakEvenOffset;
                        }
                        
                        Print($"[BREAKEVEN] Bar {CurrentBar}: LONG break-even activated! Entry={entryPrice:F2}, Profit={currentProfit:F2}, BreakEvenStop={breakEvenStopPrice:F2} (Entry+{BreakEvenOffset}), UseEmaTrailingStop={UseEmaTrailingStop}");
                    }
                    else // Short
                    {
                        breakEvenStopPrice = entryPrice - BreakEvenOffset;
                        orderName = "BarsOnTheFlowShort";
                        
                        // If EMA trailing stop is enabled, break-even becomes a ceiling (maximum stop level)
                        // Otherwise, set it directly
                        if (!UseEmaTrailingStop)
                        {
                            SetStopLoss(orderName, CalculationMode.Price, breakEvenStopPrice, false);
                            currentTradeStopLossPoints = BreakEvenOffset;
                        }
                        
                        Print($"[BREAKEVEN] Bar {CurrentBar}: SHORT break-even activated! Entry={entryPrice:F2}, Profit={currentProfit:F2}, BreakEvenStop={breakEvenStopPrice:F2} (Entry-{BreakEvenOffset}), UseEmaTrailingStop={UseEmaTrailingStop}");
                    }
                    
                    breakEvenActivated = true;
                    breakEvenEntryPrice = entryPrice;
                }
            }

            // ====================================================================
            // GUARD 3: EMA Trailing Stop Loss Management
            // Uses CURRENT OHLC [0] - Real-time position management during bar formation
            // Stop loss follows Fast EMA but only moves in favorable direction
            // When break-even is activated, it acts as a floor (longs) or ceiling (shorts)
            // ====================================================================
            if (UseEmaTrailingStop && currentPos != MarketPosition.Flat && fastEma != null && CurrentBar >= FastEmaPeriod && !double.IsNaN(entryFastEmaValue))
            {
                double currentFastEma = fastEma[0]; // Current Fast EMA value
                double newStopLossPrice = double.NaN;
                string orderName;
                
                // Calculate break-even floor/ceiling if activated
                double breakEvenFloor = double.NaN; // For longs: minimum stop loss level
                double breakEvenCeiling = double.NaN; // For shorts: maximum stop loss level
                if (breakEvenActivated && UseBreakEven)
                {
                    double entryPrice = Position.AveragePrice;
                    if (currentPos == MarketPosition.Long)
                    {
                        breakEvenFloor = entryPrice + BreakEvenOffset; // Break-even is the floor for longs
                    }
                    else if (currentPos == MarketPosition.Short)
                    {
                        breakEvenCeiling = entryPrice - BreakEvenOffset; // Break-even is the ceiling for shorts
                    }
                }
                
                if (currentPos == MarketPosition.Long)
                {
                    orderName = "BarsOnTheFlowLong";
                    // For longs: stop loss = Fast EMA, but only moves UP (never down)
                    // Only update if Fast EMA is higher than current stop loss
                    if (double.IsNaN(currentEmaStopLoss) || currentFastEma > currentEmaStopLoss)
                    {
                        newStopLossPrice = Instrument.MasterInstrument.RoundToTickSize(currentFastEma);
                        
                        // Apply break-even floor: stop loss cannot go below break-even level
                        if (!double.IsNaN(breakEvenFloor) && newStopLossPrice < breakEvenFloor)
                        {
                            newStopLossPrice = breakEvenFloor;
                            Print($"[EMA_TRAILING_STOP] Bar {CurrentBar}: LONG - Fast EMA={currentFastEma:F4} is below break-even floor ({breakEvenFloor:F4}), using break-even as stop loss");
                        }
                        else
                        {
                            currentEmaStopLoss = newStopLossPrice; // Only update EMA stop if we're using it
                        }
                        
                        // If requiring full candle below, don't set automatic stop loss (we'll check manually on bar close)
                        // Otherwise, set the stop loss normally
                        if (!EmaStopRequireFullCandleBelow)
                        {
                            SetStopLoss(orderName, CalculationMode.Price, newStopLossPrice, false);
                        }
                        
                        currentTradeStopLossPoints = Math.Abs(Position.AveragePrice - newStopLossPrice);
                        Print($"[EMA_TRAILING_STOP] Bar {CurrentBar}: LONG - Fast EMA={currentFastEma:F4}, Stop loss={newStopLossPrice:F4}, BreakEvenFloor={breakEvenFloor:F4}, RequireFullCandleBelow={EmaStopRequireFullCandleBelow}");
                    }
                }
                else if (currentPos == MarketPosition.Short)
                {
                    orderName = "BarsOnTheFlowShort";
                    // For shorts: stop loss = Fast EMA, but only moves DOWN (never up)
                    // Only update if Fast EMA is lower than current stop loss
                    if (double.IsNaN(currentEmaStopLoss) || currentFastEma < currentEmaStopLoss)
                    {
                        newStopLossPrice = Instrument.MasterInstrument.RoundToTickSize(currentFastEma);
                        
                        // Apply break-even ceiling: stop loss cannot go above break-even level
                        if (!double.IsNaN(breakEvenCeiling) && newStopLossPrice > breakEvenCeiling)
                        {
                            newStopLossPrice = breakEvenCeiling;
                            Print($"[EMA_TRAILING_STOP] Bar {CurrentBar}: SHORT - Fast EMA={currentFastEma:F4} is above break-even ceiling ({breakEvenCeiling:F4}), using break-even as stop loss");
                        }
                        else
                        {
                            currentEmaStopLoss = newStopLossPrice; // Only update EMA stop if we're using it
                        }
                        
                        // If requiring full candle below, don't set automatic stop loss (we'll check manually on bar close)
                        // Otherwise, set the stop loss normally
                        if (!EmaStopRequireFullCandleBelow)
                        {
                            SetStopLoss(orderName, CalculationMode.Price, newStopLossPrice, false);
                        }
                        
                        currentTradeStopLossPoints = Math.Abs(Position.AveragePrice - newStopLossPrice);
                        Print($"[EMA_TRAILING_STOP] Bar {CurrentBar}: SHORT - Fast EMA={currentFastEma:F4}, Stop loss={newStopLossPrice:F4}, BreakEvenCeiling={breakEvenCeiling:F4}, RequireFullCandleBelow={EmaStopRequireFullCandleBelow}");
                    }
                }
            }

            // ====================================================================
            // GUARD 4: EMA Stop Loss - Full Candle Range Check (Bar Close)
            // Uses FINAL OHLC [1] - Checks if entire completed candle is below/above stop loss
            // Only executes if EmaStopRequireFullCandleBelow is enabled
            // ====================================================================
            if (UseEmaTrailingStop && EmaStopRequireFullCandleBelow && currentPos != MarketPosition.Flat && CurrentBar >= 1)
            {
                // Determine the effective stop loss level (considering break-even floor/ceiling)
                double effectiveStopLoss = double.NaN;
                
                // Calculate break-even floor/ceiling if activated
                double breakEvenFloor = double.NaN;
                double breakEvenCeiling = double.NaN;
                if (breakEvenActivated && UseBreakEven)
                {
                    double entryPrice = Position.AveragePrice;
                    if (currentPos == MarketPosition.Long)
                    {
                        breakEvenFloor = entryPrice + BreakEvenOffset;
                    }
                    else if (currentPos == MarketPosition.Short)
                    {
                        breakEvenCeiling = entryPrice - BreakEvenOffset;
                    }
                }
                
                // Use the higher of EMA stop or break-even floor (longs), or lower (shorts)
                if (currentPos == MarketPosition.Long)
                {
                    if (!double.IsNaN(currentEmaStopLoss) && !double.IsNaN(breakEvenFloor))
                    {
                        effectiveStopLoss = Math.Max(currentEmaStopLoss, breakEvenFloor);
                    }
                    else if (!double.IsNaN(currentEmaStopLoss))
                    {
                        effectiveStopLoss = currentEmaStopLoss;
                    }
                    else if (!double.IsNaN(breakEvenFloor))
                    {
                        effectiveStopLoss = breakEvenFloor;
                    }
                }
                else if (currentPos == MarketPosition.Short)
                {
                    if (!double.IsNaN(currentEmaStopLoss) && !double.IsNaN(breakEvenCeiling))
                    {
                        effectiveStopLoss = Math.Min(currentEmaStopLoss, breakEvenCeiling);
                    }
                    else if (!double.IsNaN(currentEmaStopLoss))
                    {
                        effectiveStopLoss = currentEmaStopLoss;
                    }
                    else if (!double.IsNaN(breakEvenCeiling))
                    {
                        effectiveStopLoss = breakEvenCeiling;
                    }
                }
                
                if (double.IsNaN(effectiveStopLoss))
                    return false; // No valid stop loss to check
                
                // Use completed bar [1] for the check
                double completedBarHigh = High[1];
                double completedBarLow = Low[1];
                bool shouldExit = false;
                
                if (currentPos == MarketPosition.Long)
                {
                    // For longs: exit only if entire candle (High to Low) is below the effective stop loss
                    // This means both High and Low must be below the stop loss
                    if (completedBarHigh < effectiveStopLoss && completedBarLow < effectiveStopLoss)
                    {
                        shouldExit = true;
                        Print($"[EMA_STOP_FULL_CANDLE] Bar {CurrentBar}: LONG exit triggered - Entire candle range (High={completedBarHigh:F4}, Low={completedBarLow:F4}) is below effective stop loss ({effectiveStopLoss:F4}, EMA={currentEmaStopLoss:F4}, BreakEvenFloor={breakEvenFloor:F4})");
                    }
                }
                else if (currentPos == MarketPosition.Short)
                {
                    // For shorts: exit only if entire candle (High to Low) is above the effective stop loss
                    // This means both High and Low must be above the stop loss
                    if (completedBarHigh > effectiveStopLoss && completedBarLow > effectiveStopLoss)
                    {
                        shouldExit = true;
                        Print($"[EMA_STOP_FULL_CANDLE] Bar {CurrentBar}: SHORT exit triggered - Entire candle range (High={completedBarHigh:F4}, Low={completedBarLow:F4}) is above effective stop loss ({effectiveStopLoss:F4}, EMA={currentEmaStopLoss:F4}, BreakEvenCeiling={breakEvenCeiling:F4})");
                    }
                }
                
                if (shouldExit)
                {
                    if (currentPos == MarketPosition.Long)
                    {
                        ExitLong("BarsOnTheFlowEmaStop", "BarsOnTheFlowLong");
                        intendedPosition = MarketPosition.Flat;
                        RecordExitForCooldown(MarketPosition.Long);
                    }
                    else if (currentPos == MarketPosition.Short)
                    {
                        ExitShort("BarsOnTheFlowEmaStopS", "BarsOnTheFlowShort");
                        intendedPosition = MarketPosition.Flat;
                        RecordExitForCooldown(MarketPosition.Short);
                    }
                }
            }

            // Calculate 4-bar PnL for marginal trend detection
            double fourBarPnl = recentPnl.Count == 4 ? recentPnl.Sum() : 0;
            int goodCount = recentGood.Count >= 4 ? recentGood.Count(g => g) : 0;
            int badCount = recentGood.Count >= 4 ? recentGood.Count(g => !g) : 0;
            bool isMarginalTrend = (goodCount == 2 && badCount == 2);

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
                    RecordExitForCooldown(MarketPosition.Short);
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
                    RecordExitForCooldown(MarketPosition.Long);
                    // Don't return here - allow reversal logic to potentially enter short
                }
                else if (prevGood)
                {
                    Print($"[EXIT_DEBUG] Bar {CurrentBar}: pendingExitLongOnGood cancelling - prevGood, trend continues (O:{prevOpen:F2}, C:{prevClose:F2})");
                    // Previous bar was good, we now have 3 good bars - trend continues, cancel exit
                    pendingExitLongOnGood = false;
                }
            }

            // Resolve pending deferred shorts that were blocked by a prior good bar.
            if (pendingShortFromGood && currentPos == MarketPosition.Flat)
            {
                PrintAndLog($"[PENDING_SHORT_DEBUG] Bar {CurrentBar}: Resolving pendingShortFromGood | trendUp={trendUp}, trendDown={trendDown}, prevGood={prevGood}, prevBad={prevBad}, gradient={lastFastEmaGradDeg:F2}", "DEBUG");
                
                if (trendUp)
                {
                    bool skipDueToGradient = ShouldSkipLongDueToGradient(lastFastEmaGradDeg);
                    bool skipDueToEmaCrossover = UseEmaCrossoverFilter && !EmaCrossoverFilterPasses(true);
                    PrintAndLog($"[GRADIENT_CHECK] Bar {CurrentBar}: LONG entry (pendingShortFromGood) | gradient={lastFastEmaGradDeg:F2}°, threshold={SkipLongsBelowGradient:F2}°, GradientFilterEnabled={GradientFilterEnabled}, skipDueToGradient={skipDueToGradient}", "DEBUG");
                    PrintAndLog($"[EMA_CROSSOVER_CHECK] Bar {CurrentBar}: LONG entry (pendingShortFromGood) | UseEmaCrossoverFilter={UseEmaCrossoverFilter}, EnableBarsOnTheFlowTrendDetection={EnableBarsOnTheFlowTrendDetection}, skipDueToEmaCrossover={skipDueToEmaCrossover}", "DEBUG");
                    PrintAndLog($"[PENDING_SHORT_DEBUG] Bar {CurrentBar}: TrendUp detected, reversing to LONG | skipDueToGradient={skipDueToGradient}, skipDueToEmaCrossover={skipDueToEmaCrossover}", "DEBUG");
                    if (!skipDueToGradient && !skipDueToEmaCrossover)
                    {
                        if (intendedPosition == MarketPosition.Long)
                        {
                            PrintAndLog($"[Entry Skip] Bar {CurrentBar}: Already intendedPosition=Long, skipping pendingShortFromGood entry", "DEBUG");
                        }
                        else
                        {
                            CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                            currentTradeEntryReason = BuildEntryReason(true, trendUp, trendDown, prevGood, prevBad, skipDueToGradient, skipDueToEmaCrossover, "PendingShortFromGood", lastFastEmaGradDeg);
                            PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering LONG from pendingShortFromGood, CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}", "ENTRY");
                            // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement)
                            SetInitialStopLoss("BarsOnTheFlowLong", MarketPosition.Long);
                            EnterLong(Math.Max(1, Contracts), "BarsOnTheFlowLong");
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
                        PrintAndLog($"[PENDING_SHORT_DEBUG] Bar {CurrentBar}: Filter blocked LONG (gradient={skipDueToGradient}, emaCrossover={skipDueToEmaCrossover}), clearing pending flag", "DEBUG");
                        pendingShortFromGood = false;
                    }
                }
                else if (trendDown && prevBad)
                {
                    bool skipDueToGradient = ShouldSkipShortDueToGradient(lastFastEmaGradDeg);
                    bool skipDueToEmaCrossover = UseEmaCrossoverFilter && !EmaCrossoverFilterPasses(false);
                    PrintAndLog($"[PENDING_SHORT_DEBUG] Bar {CurrentBar}: TrendDown + prevBad, executing pending SHORT | skipDueToGradient={skipDueToGradient}, skipDueToEmaCrossover={skipDueToEmaCrossover}", "DEBUG");
                    if (!skipDueToGradient && !skipDueToEmaCrossover)
                    {
                        if (intendedPosition == MarketPosition.Short)
                        {
                            PrintAndLog($"[Entry Skip] Bar {CurrentBar}: Already intendedPosition=Short, skipping pendingShortFromGood short entry", "DEBUG");
                        }
                        else
                        {
                            CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                            currentTradeEntryReason = BuildEntryReason(false, trendUp, trendDown, prevGood, prevBad, skipDueToGradient, skipDueToEmaCrossover, "PendingShortFromGood", lastFastEmaGradDeg);
                            PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering SHORT from pendingShortFromGood, CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}", "ENTRY");
                            // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement)
                            SetInitialStopLoss("BarsOnTheFlowShort", MarketPosition.Short);
                            EnterShort(Math.Max(1, Contracts), "BarsOnTheFlowShort");
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
                    bool skipDueToEmaCrossover = UseEmaCrossoverFilter && !EmaCrossoverFilterPasses(false);
                    PrintAndLog($"[PENDING_LONG_DEBUG] Bar {CurrentBar}: TrendDown detected, reversing to SHORT | skipDueToGradient={skipDueToGradient}, skipDueToEmaCrossover={skipDueToEmaCrossover}", "DEBUG");
                    if (!skipDueToGradient && !skipDueToEmaCrossover)
                    {
                        if (intendedPosition == MarketPosition.Short)
                        {
                            PrintAndLog($"[Entry Skip] Bar {CurrentBar}: Already intendedPosition=Short, skipping pendingLongFromBad short entry", "DEBUG");
                        }
                        else
                        {
                            CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                            currentTradeEntryReason = BuildEntryReason(false, trendUp, trendDown, prevGood, prevBad, skipDueToGradient, skipDueToEmaCrossover, "PendingLongFromBad", lastFastEmaGradDeg);
                            PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering SHORT from pendingLongFromBad, CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}");
                            // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement)
                            SetInitialStopLoss("BarsOnTheFlowShort", MarketPosition.Short);
                            EnterShort(Math.Max(1, Contracts), "BarsOnTheFlowShort");
                            lastEntryBarIndex = CurrentBar;
                            lastEntryDirection = MarketPosition.Short;
                            intendedPosition = MarketPosition.Short;
                            placedEntry = true;
                        }
                        pendingLongFromBad = false;
                        pendingShortFromGood = false;
                    }
                    else if (AllowMidBarGradientEntry && skipDueToGradient)
                    {
                        PrintAndLog($"[PENDING_LONG_DEBUG] Bar {CurrentBar}: Gradient blocked SHORT, waiting for mid-bar gradient", "DEBUG");
                        waitingForShortGradient = true;
                    }
                    else
                    {
                        PrintAndLog($"[PENDING_LONG_DEBUG] Bar {CurrentBar}: Filter blocked SHORT (gradient={skipDueToGradient}, emaCrossover={skipDueToEmaCrossover}), clearing pending flag", "DEBUG");
                        pendingLongFromBad = false;
                    }
                }
                else if (trendUp && prevGood)
                {
                    Print($"[PENDING_LONG_ENTRY_PATH] Bar {CurrentBar}: Entering via pendingLongFromBad path");
                    bool skipDueToGradient = ShouldSkipLongDueToGradient(lastFastEmaGradDeg);
                    bool skipDueToEmaCrossover = UseEmaCrossoverFilter && !EmaCrossoverFilterPasses(true);
                    Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: LONG entry (pendingLongFromBad)");
                    Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: lastFastEmaGradDeg={lastFastEmaGradDeg:F2}°");
                    Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: SkipLongsBelowGradient={SkipLongsBelowGradient:F2}°");
                    Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: GradientFilterEnabled={GradientFilterEnabled}");
                    Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: double.IsNaN(lastFastEmaGradDeg)={double.IsNaN(lastFastEmaGradDeg)}");
                    Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: gradDeg < SkipLongsBelowGradient = {lastFastEmaGradDeg:F2} < {SkipLongsBelowGradient:F2} = {lastFastEmaGradDeg < SkipLongsBelowGradient}");
                    PrintAndLog($"[GRADIENT_CHECK] Bar {CurrentBar}: LONG entry (pendingLongFromBad) | gradient={lastFastEmaGradDeg:F2}°, threshold={SkipLongsBelowGradient:F2}°, GradientFilterEnabled={GradientFilterEnabled}, skipDueToGradient={skipDueToGradient}", "DEBUG");
                    PrintAndLog($"[EMA_CROSSOVER_CHECK] Bar {CurrentBar}: LONG entry (pendingLongFromBad) | UseEmaCrossoverFilter={UseEmaCrossoverFilter}, EnableBarsOnTheFlowTrendDetection={EnableBarsOnTheFlowTrendDetection}, skipDueToEmaCrossover={skipDueToEmaCrossover}", "DEBUG");
                    PrintAndLog($"[PENDING_LONG_DEBUG] Bar {CurrentBar}: TrendUp + prevGood, executing pending LONG | skipDueToGradient={skipDueToGradient}, skipDueToEmaCrossover={skipDueToEmaCrossover}", "DEBUG");
                    Print($"[ENTRY_DECISION] Bar {CurrentBar} (pendingLongFromBad): skipDueToGradient={skipDueToGradient}, skipDueToEmaCrossover={skipDueToEmaCrossover}, willEnter={!skipDueToGradient && !skipDueToEmaCrossover}");
                    if (!skipDueToGradient && !skipDueToEmaCrossover)
                    {
                        if (intendedPosition == MarketPosition.Long)
                        {
                            PrintAndLog($"[Entry Skip] Bar {CurrentBar}: Already intendedPosition=Long, skipping pendingLongFromBad entry", "DEBUG");
                        }
                        else
                        {
                            CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                            currentTradeEntryReason = BuildEntryReason(true, trendUp, trendDown, prevGood, prevBad, skipDueToGradient, skipDueToEmaCrossover, "PendingLongFromBad", lastFastEmaGradDeg);
                            PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering LONG from pendingLongFromBad, CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}");
                            // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement)
                            SetInitialStopLoss("BarsOnTheFlowLong", MarketPosition.Long);
                            EnterLong(Math.Max(1, Contracts), "BarsOnTheFlowLong");
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
                            RecordExitForCooldown(MarketPosition.Short);
                        }
                        else
                        {
                            Print($"[EXIT_DEBUG] Bar {CurrentBar}: TrendUp break - exiting SHORT and REVERSING to LONG");
                            // ReverseOnTrendBreak overrides gradient filter, but EMA crossover filter still applies
                            PrintAndLog($"[GRADIENT_CHECK] Bar {CurrentBar}: LONG entry (reverse from short) | gradient={lastFastEmaGradDeg:F2}°, threshold={SkipLongsBelowGradient:F2}°, GradientFilterEnabled={GradientFilterEnabled}, BYPASSED (ReverseOnTrendBreak={ReverseOnTrendBreak})", "DEBUG");
                            
                            // Check EMA crossover filter (still applies even during reversals)
                            bool skipDueToEmaCrossover = UseEmaCrossoverFilter && !EmaCrossoverFilterPasses(true);
                            PrintAndLog($"[EMA_CROSSOVER_CHECK] Bar {CurrentBar}: LONG entry (reverse from short) | UseEmaCrossoverFilter={UseEmaCrossoverFilter}, EnableBarsOnTheFlowTrendDetection={EnableBarsOnTheFlowTrendDetection}, skipDueToEmaCrossover={skipDueToEmaCrossover}", "DEBUG");
                            
                            ExitShort();
                            RecordExitForCooldown(MarketPosition.Short);
                            if (intendedPosition != MarketPosition.Long && !skipDueToEmaCrossover)
                            {
                                currentTradeEntryReason = BuildEntryReason(true, trendUp, trendDown, prevGood, prevBad, false, skipDueToEmaCrossover, "ReverseFromShort", lastFastEmaGradDeg);
                                PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering LONG (reverse from short), CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}");
                                // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement) - even during reversals
                                SetInitialStopLoss("BarsOnTheFlowLong", MarketPosition.Long);
                                EnterLong(Math.Max(1, Contracts), "BarsOnTheFlowLong");
                                lastEntryBarIndex = CurrentBar;
                                lastEntryDirection = MarketPosition.Long;
                                intendedPosition = MarketPosition.Long;
                                placedEntry = true;
                            }
                            else if (skipDueToEmaCrossover)
                            {
                                PrintAndLog($"[Entry Skip] Bar {CurrentBar}: EMA crossover filter blocked reversal to LONG", "DEBUG");
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
                        RecordExitForCooldown(MarketPosition.Short);
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
                            Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: LONG entry check");
                            Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: lastFastEmaGradDeg={lastFastEmaGradDeg:F2}°");
                            Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: SkipLongsBelowGradient={SkipLongsBelowGradient:F2}°");
                            Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: GradientFilterEnabled={GradientFilterEnabled}");
                            Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: double.IsNaN(lastFastEmaGradDeg)={double.IsNaN(lastFastEmaGradDeg)}");
                            Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: gradDeg < SkipLongsBelowGradient = {lastFastEmaGradDeg:F2} < {SkipLongsBelowGradient:F2} = {lastFastEmaGradDeg < SkipLongsBelowGradient}");
                            PrintAndLog($"[GRADIENT_CHECK] Bar {CurrentBar}: LONG entry check | gradient={lastFastEmaGradDeg:F2}°, threshold={SkipLongsBelowGradient:F2}°, GradientFilterEnabled={GradientFilterEnabled}, skipDueToGradient={skipDueToGradient}", "DEBUG");
                            
                            // Check EMA crossover filter (only when trend detection is enabled - when disabled, EMA crossover generates signals directly)
                            bool skipDueToEmaCrossover = UseEmaCrossoverFilter && !EmaCrossoverFilterPasses(true);
                            PrintAndLog($"[EMA_CROSSOVER_CHECK] Bar {CurrentBar}: LONG entry check | UseEmaCrossoverFilter={UseEmaCrossoverFilter}, EnableBarsOnTheFlowTrendDetection={EnableBarsOnTheFlowTrendDetection}, skipDueToEmaCrossover={skipDueToEmaCrossover}", "DEBUG");
                            
                            Print($"[ENTRY_DECISION] Bar {CurrentBar}: skipDueToGradient={skipDueToGradient}, skipDueToEmaCrossover={skipDueToEmaCrossover}, willEnter={!skipDueToGradient && !skipDueToEmaCrossover}");
                            
                            if (!skipDueToGradient && !skipDueToEmaCrossover)
                            {
                                // Don't re-enter if already long - check both actual and intended position
                                if (Position.MarketPosition != MarketPosition.Long && intendedPosition != MarketPosition.Long)
                                {
                                    CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                                    currentTradeEntryReason = BuildEntryReason(true, trendUp, trendDown, prevGood, prevBad, skipDueToGradient, skipDueToEmaCrossover, "FreshSignal", lastFastEmaGradDeg);
                                    PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering LONG (fresh signal), CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}");
                                    // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement)
                                    SetInitialStopLoss("BarsOnTheFlowLong", MarketPosition.Long);
                                    EnterLong(Math.Max(1, Contracts), "BarsOnTheFlowLong");
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
                            else if (AllowMidBarGradientEntry && skipDueToGradient)
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
                            RecordExitForCooldown(MarketPosition.Long);
                        }
                        else
                        {
                            // ReverseOnTrendBreak overrides gradient filter, but EMA crossover filter still applies
                            Print($"[EXIT_DEBUG] Bar {CurrentBar}: TrendDown break - exiting LONG and REVERSING to SHORT");
                            Print($"[Reverse Debug] Bar {CurrentBar}: REVERSING to short! (gradient filter overridden)");
                            
                            // Check EMA crossover filter (still applies even during reversals)
                            bool skipDueToEmaCrossover = UseEmaCrossoverFilter && !EmaCrossoverFilterPasses(false);
                            PrintAndLog($"[EMA_CROSSOVER_CHECK] Bar {CurrentBar}: SHORT entry (reverse from long) | UseEmaCrossoverFilter={UseEmaCrossoverFilter}, EnableBarsOnTheFlowTrendDetection={EnableBarsOnTheFlowTrendDetection}, skipDueToEmaCrossover={skipDueToEmaCrossover}", "DEBUG");
                            
                            ExitLong("BarsOnTheFlowExit", "BarsOnTheFlowLong");
                            if (intendedPosition != MarketPosition.Short && !skipDueToEmaCrossover)
                            {
                                currentTradeEntryReason = BuildEntryReason(false, trendUp, trendDown, prevGood, prevBad, false, skipDueToEmaCrossover, "ReverseFromLong", lastFastEmaGradDeg);
                                PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering SHORT (reverse from long), CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}");
                                // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement) - even during reversals
                                SetInitialStopLoss("BarsOnTheFlowShort", MarketPosition.Short);
                                EnterShort(Math.Max(1, Contracts), "BarsOnTheFlowShort");
                                lastEntryBarIndex = CurrentBar;
                                lastEntryDirection = MarketPosition.Short;
                                intendedPosition = MarketPosition.Short;
                                placedEntry = true;
                            }
                            else if (skipDueToEmaCrossover)
                            {
                                PrintAndLog($"[Entry Skip] Bar {CurrentBar}: EMA crossover filter blocked reversal to SHORT", "DEBUG");
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
                        RecordExitForCooldown(MarketPosition.Long);
                    }
                }

                if (!placedEntry && currentPos == MarketPosition.Flat)
                {
                    Print($"[SHORT_ENTRY_DEBUG] Bar {CurrentBar}: Checking SHORT entry - trendDown={trendDown}, prevBad={prevBad}, prevGood={prevGood}, allowShortThisBar={allowShortThisBar}, AvoidShortsOnGoodCandle={AvoidShortsOnGoodCandle}");
                    
                    if (AvoidShortsOnGoodCandle && prevGood)
                    {
                        Print($"[Entry Block] Bar {CurrentBar}: Deferring SHORT - bar closed GOOD (O:{prevOpen:F2}, C:{prevClose:F2})");
                        pendingShortFromGood = true;
                        pendingLongFromBad = false;
                    }
                    else if (trendDown && prevBad && allowShortThisBar)
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
                            PrintAndLog($"[GRADIENT_CHECK] Bar {CurrentBar}: SHORT entry check | gradient={lastFastEmaGradDeg:F2}°, threshold={SkipShortsAboveGradient:F2}°, GradientFilterEnabled={GradientFilterEnabled}, skipDueToGradient={skipDueToGradient}", "DEBUG");
                            
                            // Check EMA crossover filter
                            bool skipDueToEmaCrossover = UseEmaCrossoverFilter && !EmaCrossoverFilterPasses(false);
                            PrintAndLog($"[EMA_CROSSOVER_CHECK] Bar {CurrentBar}: SHORT entry check | UseEmaCrossoverFilter={UseEmaCrossoverFilter}, EnableBarsOnTheFlowTrendDetection={EnableBarsOnTheFlowTrendDetection}, skipDueToEmaCrossover={skipDueToEmaCrossover}", "DEBUG");
                            
                            Print($"[SHORT_ENTRY_DEBUG] Bar {CurrentBar}: trendDown={trendDown}, prevBad={prevBad}, allowShortThisBar={allowShortThisBar}, skipDueToGradient={skipDueToGradient}, skipDueToEmaCrossover={skipDueToEmaCrossover}, Position={Position.MarketPosition}, intendedPosition={intendedPosition}");
                            
                            if (!skipDueToGradient && !skipDueToEmaCrossover)
                            {
                                // Don't re-enter if already short - check both actual and intended position
                                if (Position.MarketPosition != MarketPosition.Short && intendedPosition != MarketPosition.Short)
                                {
                                    CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                                    currentTradeEntryReason = BuildEntryReason(false, trendUp, trendDown, prevGood, prevBad, skipDueToGradient, skipDueToEmaCrossover, "FreshSignal", lastFastEmaGradDeg);
                                    PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering SHORT (fresh signal), CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}");
                                    // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement)
                                    SetInitialStopLoss("BarsOnTheFlowShort", MarketPosition.Short);
                                    EnterShort(Math.Max(1, Contracts), "BarsOnTheFlowShort");
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
                            else if (AllowMidBarGradientEntry && skipDueToGradient)
                            {
                                // All conditions met except gradient - wait for mid-bar cross
                                waitingForShortGradient = true;
                            }
                        }
                    }
                }
            }
            // ExitOnTrendBreak only works when BarsOnTheFlow trend detection is enabled
            else if (EnableBarsOnTheFlowTrendDetection && ExitOnTrendBreak && currentPos == MarketPosition.Long)
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
                            RecordExitForCooldown(MarketPosition.Long);
                        }
                        else
                        {
                            // ReverseOnTrendBreak overrides gradient filter, but EMA crossover filter still applies
                            Print($"[EXIT_DEBUG] Bar {CurrentBar}: ExitOnTrendBreak - exiting LONG and REVERSING to SHORT");
                            Print($"[Reverse Debug] Bar {CurrentBar}: REVERSING to short! (gradient filter overridden)");
                            
                            // Check EMA crossover filter (still applies even during reversals)
                            bool skipDueToEmaCrossover = UseEmaCrossoverFilter && !EmaCrossoverFilterPasses(false);
                            PrintAndLog($"[EMA_CROSSOVER_CHECK] Bar {CurrentBar}: SHORT entry (ExitOnTrendBreak reversal) | UseEmaCrossoverFilter={UseEmaCrossoverFilter}, EnableBarsOnTheFlowTrendDetection={EnableBarsOnTheFlowTrendDetection}, skipDueToEmaCrossover={skipDueToEmaCrossover}", "DEBUG");
                            
                            ExitLong("BarsOnTheFlowExit", "BarsOnTheFlowLong");
                            RecordExitForCooldown(MarketPosition.Long);
                            if (intendedPosition != MarketPosition.Short && !skipDueToEmaCrossover)
                            {
                                currentTradeEntryReason = BuildEntryReason(false, trendUp, trendDown, prevGood, prevBad, false, skipDueToEmaCrossover, "ExitOnTrendBreakReversal", lastFastEmaGradDeg);
                                PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering SHORT (ExitOnTrendBreak reversal), CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}");
                                // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement) - even during reversals
                                SetInitialStopLoss("BarsOnTheFlowShort", MarketPosition.Short);
                                EnterShort(Math.Max(1, Contracts), "BarsOnTheFlowShort");
                                lastEntryBarIndex = CurrentBar;
                                lastEntryDirection = MarketPosition.Short;
                                intendedPosition = MarketPosition.Short;
                                placedEntry = true;
                            }
                            else if (skipDueToEmaCrossover)
                            {
                                PrintAndLog($"[Entry Skip] Bar {CurrentBar}: EMA crossover filter blocked ExitOnTrendBreak reversal to SHORT", "DEBUG");
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
                        RecordExitForCooldown(MarketPosition.Long);
                    }
                }
            }
            // ExitOnTrendBreak only works when BarsOnTheFlow trend detection is enabled
            else if (EnableBarsOnTheFlowTrendDetection && ExitOnTrendBreak && currentPos == MarketPosition.Short)
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
                            RecordExitForCooldown(MarketPosition.Short);
                        }
                        else
                        {
                            // ReverseOnTrendBreak overrides gradient filter, but EMA crossover filter still applies
                            Print($"[EXIT_DEBUG] Bar {CurrentBar}: ExitOnTrendBreak - exiting SHORT and REVERSING to LONG");
                            Print($"[Reverse Debug] Bar {CurrentBar}: REVERSING to long! (gradient filter overridden)");
                            
                            // Check EMA crossover filter (still applies even during reversals)
                            bool skipDueToEmaCrossover = UseEmaCrossoverFilter && !EmaCrossoverFilterPasses(true);
                            PrintAndLog($"[EMA_CROSSOVER_CHECK] Bar {CurrentBar}: LONG entry (ExitOnTrendBreak reversal) | UseEmaCrossoverFilter={UseEmaCrossoverFilter}, EnableBarsOnTheFlowTrendDetection={EnableBarsOnTheFlowTrendDetection}, skipDueToEmaCrossover={skipDueToEmaCrossover}", "DEBUG");
                            
                            ExitShort("BarsOnTheFlowExitS", "BarsOnTheFlowShort");
                            RecordExitForCooldown(MarketPosition.Short);
                            if (intendedPosition != MarketPosition.Long && !skipDueToEmaCrossover)
                            {
                                currentTradeEntryReason = BuildEntryReason(true, trendUp, trendDown, prevGood, prevBad, false, skipDueToEmaCrossover, "ExitOnTrendBreakReversal", lastFastEmaGradDeg);
                                PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering LONG (ExitOnTrendBreak reversal), CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}");
                                // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement) - even during reversals
                                SetInitialStopLoss("BarsOnTheFlowLong", MarketPosition.Long);
                                EnterLong(Math.Max(1, Contracts), "BarsOnTheFlowLong");
                                lastEntryBarIndex = CurrentBar;
                                lastEntryDirection = MarketPosition.Long;
                                intendedPosition = MarketPosition.Long;
                                placedEntry = true;
                            }
                            else if (skipDueToEmaCrossover)
                            {
                                PrintAndLog($"[Entry Skip] Bar {CurrentBar}: EMA crossover filter blocked ExitOnTrendBreak reversal to LONG", "DEBUG");
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
                        RecordExitForCooldown(MarketPosition.Short);
                    }
                }
            }

            UpdateTrendLifecycle(currentPos);

            return placedEntry;
        }
        
        /// <summary>
        /// Builds a comprehensive entry reason string that captures all filters and conditions that applied when entering a trade.
        /// </summary>
        private string BuildEntryReason(bool isLong, bool trendUp, bool trendDown, bool prevGood, bool prevBad,
            bool skipDueToGradient, bool skipDueToEmaCrossover, string entryPath, double gradientDeg)
        {
            List<string> reasons = new List<string>();
            
            // Entry path (how the entry was triggered)
            if (!string.IsNullOrEmpty(entryPath))
            {
                reasons.Add(entryPath);
            }
            
            // Trend detection
            if (EnableBarsOnTheFlowTrendDetection)
            {
                if (isLong && trendUp)
                    reasons.Add("TrendUp");
                else if (!isLong && trendDown)
                    reasons.Add("TrendDown");
            }
            else
            {
                reasons.Add("TrendDetectionDisabled");
            }
            
            // Candle type
            if (isLong && prevGood)
                reasons.Add("GoodCandle");
            else if (isLong && prevBad)
                reasons.Add("BadCandle(Deferred)");
            else if (!isLong && prevBad)
                reasons.Add("BadCandle");
            else if (!isLong && prevGood)
                reasons.Add("GoodCandle(Deferred)");
            
            // Gradient filter
            if (GradientFilterEnabled)
            {
                if (!skipDueToGradient)
                {
                    reasons.Add($"GradientOK({gradientDeg:F1}°)");
                }
                else
                {
                    reasons.Add($"GradientBlocked({gradientDeg:F1}°)");
                }
            }
            else
            {
                reasons.Add("GradientDisabled");
            }
            
            // EMA crossover filter
            if (UseEmaCrossoverFilter)
            {
                if (!skipDueToEmaCrossover)
                {
                    reasons.Add("EMACrossoverOK");
                }
                else
                {
                    reasons.Add("EMACrossoverBlocked");
                }
            }
            else
            {
                reasons.Add("EMACrossoverDisabled");
            }
            
            // Cooldown check
            if (isLong)
            {
                int barsSinceExit = lastLongExitBar >= 0 ? CurrentBar - lastLongExitBar : int.MaxValue;
                if (barsSinceExit < EmaCrossoverCooldownBars)
                {
                    reasons.Add($"Cooldown({barsSinceExit}/{EmaCrossoverCooldownBars})");
                }
                else
                {
                    reasons.Add("CooldownOK");
                }
            }
            else
            {
                int barsSinceExit = lastShortExitBar >= 0 ? CurrentBar - lastShortExitBar : int.MaxValue;
                if (barsSinceExit < EmaCrossoverCooldownBars)
                {
                    reasons.Add($"Cooldown({barsSinceExit}/{EmaCrossoverCooldownBars})");
                }
                else
                {
                    reasons.Add("CooldownOK");
                }
            }
            
            return string.Join("; ", reasons);
        }
        
        #endregion

        #region Database Operations - All API Calls and State Updates
        
        /// <summary>
        /// All database operations (API calls, state updates, bar samples)
        /// To disable database operations, comment out the call to this method in OnBarUpdate.
        /// </summary>
        private void ProcessDatabaseOperations()
        {
            // CRITICAL: Always update strategy state on bar close
            // This ensures every bar is logged to the database
            UpdateStrategyState();
            lastStateUpdateTime = DateTime.Now;
            lastStateUpdatePrice = Close[0];

            // Optionally stream diagnostics (incl. gradient) to dashboard
            if (EnableDashboardDiagnostics)
            {
                bool allowShortThisBar = !(AvoidShortsOnGoodCandle && Close[1] > Open[1]);
                bool allowLongThisBar = !(AvoidLongsOnBadCandle && Close[1] < Open[1]);
                SendDashboardDiag(allowLongThisBar, allowShortThisBar);
            }

            // Record bar sample for volatility database (non-blocking)
            // Always record bar samples for debugging, not just when UseVolumeAwareStop is enabled
            if (CurrentBar > 10)
            {
                RecordBarSample();
            }
        }
        
        #endregion

        #region CSV/Logging Operations - All CSV Writing and Logging
        
        /// <summary>
        /// Process pending CSV logs from previous bar
        /// </summary>
        private void ProcessPendingCSVLogs()
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
                                "{0},{1},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11},{12},{13},{14},{15:F6},{16:F4},{17:F4},{18},{19},{20},{21},{22},{23},{24:F4},{25:F4},{26},{27},{28},{29},{30},{31},{32},{33},{34},{35},{36},{37},{38},{39},{40},{41:F4},{42:F4}",
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
                                p.BarPattern,
                                p.UseEmaCrossoverFilter,
                                p.EmaFastPeriod,
                                p.EmaSlowPeriod,
                                p.EmaCrossoverWindowBars,
                                p.EmaCrossoverRequireCrossover,
                                p.EmaCrossoverCooldownBars,
                                double.IsNaN(p.EmaFastValue) ? "" : p.EmaFastValue.ToString("F4"),
                                double.IsNaN(p.EmaSlowValue) ? "" : p.EmaSlowValue.ToString("F4"));
                            LogLine(line);
                            pendingLogs.RemoveAt(i);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Process mid-bar operations (gradient entry/exit checks, state updates)
        /// </summary>
        private void ProcessMidBarOperations()
        {
            // Handle mid-bar gradient entry and exit checks
            if (GradientFilterEnabled)
            {
                if (AllowMidBarGradientEntry)
                    CheckMidBarGradientEntry();
                if (AllowMidBarGradientExit)
                    CheckMidBarGradientExit();
            }

            // Tick-based state update (throttled by StateUpdateIntervalSeconds or on price change)
            // NOTE: This only applies to mid-bar ticks, NOT bar close
            if (EnableDashboardDiagnostics)
            {
                bool shouldUpdate = false;
                
                // Time-based update
                if (StateUpdateIntervalSeconds > 0 && (DateTime.Now - lastStateUpdateTime).TotalSeconds >= StateUpdateIntervalSeconds)
                    shouldUpdate = true;
                
                // Price-change update (only if price actually changed)
                if (StateUpdateOnPriceChange && Close[0] != lastStateUpdatePrice)
                    shouldUpdate = true;
                
                if (shouldUpdate)
                {
                    UpdateStrategyState();
                    lastStateUpdateTime = DateTime.Now;
                    lastStateUpdatePrice = Close[0];
                }
            }
        }

        /// <summary>
        /// All CSV logging operations (bar snapshots, opportunity analysis)
        /// To disable CSV logging, comment out the call to this method in OnBarUpdate.
        /// </summary>
        private void ProcessCSVLogging(double prevOpen, double prevClose, bool allowLongThisBar, bool allowShortThisBar, 
            bool trendUp, bool trendDown, bool placedEntry)
        {
            // Write a per-bar snapshot even when no orders fire
            LogBarSnapshot(1, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
            
            // Log opportunity analysis
            if (EnableOpportunityLog && opportunityLogInitialized)
            {
                LogOpportunityAnalysis(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown, placedEntry);
            }
        }

        /// <summary>
        /// Writes all strategy parameters to a JSON file for the strategy_state.html dashboard page.
        /// This file is used by http://localhost:51888/strategy_state.html to display current parameters.
        /// Called during log initialization to create the parameters file.
        /// </summary>
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
                    
                    // EMA crossover filter parameters
                    { "UseEmaCrossoverFilter", UseEmaCrossoverFilter },
                    { "EmaFastPeriod", EmaFastPeriod },
                    { "EmaSlowPeriod", EmaSlowPeriod },
                    { "EmaCrossoverWindowBars", EmaCrossoverWindowBars },
                    { "EmaCrossoverRequireCrossover", EmaCrossoverRequireCrossover },
                    { "EmaCrossoverCooldownBars", EmaCrossoverCooldownBars },
                    { "EmaCrossoverMinTicksCloseToFast", EmaCrossoverMinTicksCloseToFast },
                    { "EmaCrossoverMinTicksFastToSlow", EmaCrossoverMinTicksFastToSlow },
                    
                    // Entry/Exit parameters
                    { "Contracts", Contracts },
                    { "DisableRealTimeTrading", DisableRealTimeTrading },
                    { "ExitOnTrendBreak", ExitOnTrendBreak },
                    { "EnableBarsOnTheFlowTrendDetection", EnableBarsOnTheFlowTrendDetection },
                    { "TrendLookbackBars", TrendLookbackBars },
                    { "MinMatchingBars", MinMatchingBars },
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
                    { "EnableDashboardDiagnostics", EnableDashboardDiagnostics },
                    { "DashboardBaseUrl", DashboardBaseUrl },
                    { "DashboardAsyncHistorical", DashboardAsyncHistorical },
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
        
        #endregion

        #region Visualization Operations - All Drawing and Display
        
        /// <summary>
        /// All visualization operations (trend overlays, labels, gradient displays)
        /// To disable visualization, comment out the call to this method in OnBarUpdate.
        /// </summary>
        private void UpdateVisualizations(bool trendUp, bool trendDown)
        {
            if (showBarIndexLabels && CurrentBar >= 0)
            {
                string tag = "BarLabel_" + CurrentBar;
                double yPosition = High[0] + (6 * TickSize);
                Draw.Text(this, tag, CurrentBar.ToString(), 0, yPosition, Brushes.Black);
            }

            // Calculate label positions to stack vertically - determine what labels will be shown first
            double labelBaseY = High[1] + (8 * TickSize); // Start just above the bar
            int labelStackOffset = 0; // Track vertical offset for stacking labels
            
            // Check if gradient label will be shown
            bool willShowGradient = false;
            double gradForLabel = double.NaN;
            if (showFastGradLabels && CurrentBar >= 1)
            {
                int prevBarIndex = CurrentBar - 1;
                if (gradientByBar.ContainsKey(prevBarIndex))
                {
                    gradForLabel = gradientByBar[prevBarIndex];
                    willShowGradient = !double.IsNaN(gradForLabel);
                }
                else if (!double.IsNaN(lastFastEmaGradDeg))
                {
                    gradForLabel = lastFastEmaGradDeg;
                    willShowGradient = true;
                }
            }
            
            // Draw gradient label first (top of stack)
            if (willShowGradient)
            {
                int prevBarIndex = CurrentBar - 1;
                string gradTag = "FastGradLabel_" + prevBarIndex;
                double gradY = labelBaseY + (labelStackOffset * 12 * TickSize); // 12 ticks spacing between labels
                labelStackOffset++; // Move offset for next label
                string gradText = "F:" + gradForLabel.ToString("F1");
                Draw.Text(this, gradTag, gradText, 1, gradY, Brushes.Black); // barsAgo = 1
            }

            // EMA Crossover Labels - shows visual indicators for EMA crossover conditions
            if (ShowEmaCrossoverLabels && CurrentBar >= 1 && emaFast != null && emaSlow != null && 
                CurrentBar >= Math.Max(EmaFastPeriod, EmaSlowPeriod))
            {
                // Use completed bar data [1] for bar-close decisions
                double close = Close[1];
                double fastEma = emaFast[1];
                double slowEma = emaSlow[1];
                
                // Check for valid values
                if (!double.IsNaN(close) && !double.IsNaN(fastEma) && !double.IsNaN(slowEma))
                {
                    string emaLabelTag = "EmaCrossoverLabel_" + (CurrentBar - 1);
                    
                    // Green circle above bar: Close > Fast EMA > Slow EMA (bullish condition)
                    if (close > fastEma && fastEma > slowEma)
                    {
                        double circleY = High[1] + (8 * TickSize); // Above the bar
                        // Use a filled circle character (●) for visibility
                        Draw.Text(this, emaLabelTag, "●", 1, circleY, Brushes.Green);
                    }
                    // Red circle below bar: Close < Fast EMA < Slow EMA (bearish condition)
                    else if (close < fastEma && fastEma < slowEma)
                    {
                        double circleY = Low[1] - (8 * TickSize); // Below the bar
                        // Use a filled circle character (●) for visibility
                        Draw.Text(this, emaLabelTag, "●", 1, circleY, Brushes.Red);
                    }
                }
            }

            // Tick Gap Indicators - shows visual confirmation/cancellation for minimum tick gap requirements
            if (ShowTickGapIndicators && CurrentBar >= 1 && emaFast != null && emaSlow != null && 
                CurrentBar >= Math.Max(EmaFastPeriod, EmaSlowPeriod) && UseEmaCrossoverFilter)
            {
                // Use completed bar data [1] for bar-close decisions
                double close = Close[1];
                double fastEma = emaFast[1];
                double slowEma = emaSlow[1];
                
                // Check for valid values
                if (!double.IsNaN(close) && !double.IsNaN(fastEma) && !double.IsNaN(slowEma))
                {
                    // Calculate minimum gaps in price (ticks * tick size)
                    double minGapCloseToFast = EmaCrossoverMinTicksCloseToFast * TickSize;
                    double minGapFastToSlow = EmaCrossoverMinTicksFastToSlow * TickSize;
                    
                    // Check if gaps meet requirements for both long and short conditions
                    bool closeToFastGapMet = false;
                    bool fastToSlowGapMet = false;
                    
                    // For longs: Close >= Fast EMA + minGap AND Fast EMA >= Slow EMA + minGap
                    bool longCloseToFast = close >= fastEma + minGapCloseToFast;
                    bool longFastToSlow = fastEma >= slowEma + minGapFastToSlow;
                    
                    // For shorts: Close <= Fast EMA - minGap AND Fast EMA <= Slow EMA - minGap
                    bool shortCloseToFast = close <= fastEma - minGapCloseToFast;
                    bool shortFastToSlow = fastEma <= slowEma - minGapFastToSlow;
                    
                    // Determine which direction we're checking (based on EMA alignment)
                    if (close > fastEma && fastEma > slowEma)
                    {
                        // Bullish alignment - check long requirements
                        closeToFastGapMet = longCloseToFast;
                        fastToSlowGapMet = longFastToSlow;
                    }
                    else if (close < fastEma && fastEma < slowEma)
                    {
                        // Bearish alignment - check short requirements
                        closeToFastGapMet = shortCloseToFast;
                        fastToSlowGapMet = shortFastToSlow;
                    }
                    // If EMAs are not aligned (e.g., Close between EMAs), show indicators based on long requirements
                    else
                    {
                        closeToFastGapMet = longCloseToFast;
                        fastToSlowGapMet = longFastToSlow;
                    }
                    
                    // Draw indicators above the bar, stacking vertically below gradient label if present
                    // Use the same labelBaseY and labelStackOffset system for consistent stacking
                    double tickGapBaseY = labelBaseY + (labelStackOffset * 12 * TickSize); // Stack below gradient label if present
                    
                    // Close to Fast EMA gap indicator
                    string closeToFastTag = "TickGapCloseToFast_" + (CurrentBar - 1);
                    string closeToFastSymbol = closeToFastGapMet ? "✓" : "✗";
                    Brush closeToFastColor = closeToFastGapMet ? Brushes.Green : Brushes.Red;
                    Draw.Text(this, closeToFastTag, $"C→F:{closeToFastSymbol}", 1, tickGapBaseY, closeToFastColor);
                    
                    // Fast EMA to Slow EMA gap indicator - stack below C→F
                    string fastToSlowTag = "TickGapFastToSlow_" + (CurrentBar - 1);
                    string fastToSlowSymbol = fastToSlowGapMet ? "✓" : "✗";
                    Brush fastToSlowColor = fastToSlowGapMet ? Brushes.Green : Brushes.Red;
                    Draw.Text(this, fastToSlowTag, $"F→S:{fastToSlowSymbol}", 1, tickGapBaseY + (12 * TickSize), fastToSlowColor);
                }
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
        
        #endregion

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
                // Check if this was a break-even stop exit
                if (breakEvenActivated && (order.OrderType == OrderType.StopMarket || order.OrderType == OrderType.StopLimit))
                {
                    double exitPrice = execution.Price;
                    double profitLocked = 0;
                    
                    if (order.OrderAction == OrderAction.Sell) // Long exit
                    {
                        profitLocked = (exitPrice - breakEvenEntryPrice) * Position.Quantity;
                    }
                    else if (order.OrderAction == OrderAction.BuyToCover) // Short exit
                    {
                        profitLocked = (breakEvenEntryPrice - exitPrice) * Position.Quantity;
                    }
                    
                    Print($"[BREAKEVEN_EXIT] Bar {CurrentBar}: Break-even stop hit! Entry={breakEvenEntryPrice:F2}, Exit={exitPrice:F2}, Locked Profit={profitLocked:F2} points");
                }
                
                Print($"[EXIT_FILL_DEBUG] Bar {CurrentBar}: Exit filled - {action}, resetting intendedPosition from {intendedPosition} to Flat");
                intendedPosition = MarketPosition.Flat;
                // Reset break-even tracking
                breakEvenActivated = false;
                breakEvenEntryPrice = double.NaN;
                // Reset cooldown tracking when position exits (for stop loss, manual exit, etc.)
                MarketPosition exitedPosition = action == OrderAction.Sell ? MarketPosition.Long : MarketPosition.Short;
                RecordExitForCooldown(exitedPosition);
            }
            // Update intendedPosition when entries fill
            else if (isEntry)
            {
                if (action == OrderAction.Buy)
                {
                    Print($"[ENTRY_FILL_DEBUG] Bar {CurrentBar}: Long entry filled, setting intendedPosition=Long");
                    intendedPosition = MarketPosition.Long;
                    // Track entry for trade logging (if not already tracked by OnPositionUpdate)
                    if (currentTradeEntryBar < 0)
                    {
                        currentTradeEntryBar = CurrentBar;
                        currentTradeEntryPrice = execution.Price;
                        currentTradeEntryTime = time;
                        currentTradeDirection = MarketPosition.Long;
                        currentTradeContracts = execution.Quantity;
                    }
                }
                else if (action == OrderAction.SellShort)
                {
                    Print($"[ENTRY_FILL_DEBUG] Bar {CurrentBar}: Short entry filled, setting intendedPosition=Short");
                    intendedPosition = MarketPosition.Short;
                    // Track entry for trade logging (if not already tracked by OnPositionUpdate)
                    if (currentTradeEntryBar < 0)
                    {
                        currentTradeEntryBar = CurrentBar;
                        currentTradeEntryPrice = execution.Price;
                        currentTradeEntryTime = time;
                        currentTradeDirection = MarketPosition.Short;
                        currentTradeContracts = execution.Quantity;
                    }
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
                BarPattern = isEntry ? GetBarSequencePattern() : string.Empty,
                // EMA Crossover Filter parameters (for trade entry analysis)
                UseEmaCrossoverFilter = UseEmaCrossoverFilter,
                EmaFastPeriod = EmaFastPeriod,
                EmaSlowPeriod = EmaSlowPeriod,
                EmaCrossoverWindowBars = EmaCrossoverWindowBars,
                EmaCrossoverRequireCrossover = EmaCrossoverRequireCrossover,
                EmaCrossoverCooldownBars = EmaCrossoverCooldownBars,
                EmaCrossoverMinTicksCloseToFast = EmaCrossoverMinTicksCloseToFast,
                EmaCrossoverMinTicksFastToSlow = EmaCrossoverMinTicksFastToSlow,
                // Capture actual EMA values at execution time
                EmaFastValue = (emaFast != null && CurrentBar >= EmaFastPeriod) ? emaFast[0] : double.NaN,
                EmaSlowValue = (emaSlow != null && CurrentBar >= EmaSlowPeriod) ? emaSlow[0] : double.NaN
            });
        }

        protected override void OnPositionUpdate(Cbi.Position position, double averagePrice, int quantity, Cbi.MarketPosition marketPosition)
        {
            base.OnPositionUpdate(position, averagePrice, quantity, marketPosition);

            // Detect when a new position is opened (Flat -> Long/Short)
            if (previousPosition == MarketPosition.Flat && marketPosition != MarketPosition.Flat)
            {
                // Trade entry - record entry information
                // Use lastEntryBarIndex (captured when entry was submitted) instead of CurrentBar
                // because CurrentBar may have advanced by the time OnPositionUpdate is called
                // Add 1 to match chart display (chart is 1-indexed, CurrentBar is 0-indexed)
                int baseBar = (lastEntryBarIndex >= 0) ? lastEntryBarIndex : CurrentBar;
                currentTradeEntryBar = baseBar + 1;
                currentTradeEntryPrice = averagePrice;
                currentTradeEntryTime = Time[0];
                currentTradeDirection = marketPosition;
                currentTradeContracts = Math.Abs(quantity);
                currentTradeMFE = 0.0;
                currentTradeMAE = 0.0;
                
                Print($"[TRADE_ENTRY] Bar {currentTradeEntryBar} (baseBar={baseBar}, lastEntryBarIndex={lastEntryBarIndex}, CurrentBar={CurrentBar}): {marketPosition} entry at {averagePrice:F2}, Contracts={currentTradeContracts}");
            }
            // Detect when position is closed (Long/Short -> Flat)
            else if (previousPosition != MarketPosition.Flat && marketPosition == MarketPosition.Flat)
            {
                // Trade exit - send trade data to dashboard
                SendTradeCompletedToDashboard();
                
                // Reset tracking
                currentTradeEntryBar = -1;
                currentTradeEntryReason = "";
                currentTradeEntryPrice = double.NaN;
                currentTradeEntryTime = DateTime.MinValue;
                currentTradeDirection = MarketPosition.Flat;
                currentTradeContracts = 0;
                currentTradeMFE = 0.0;
                currentTradeMAE = 0.0;
                currentTradeStopLossPoints = 0.0;
            }
            // Update MFE/MAE while position is open
            else if (marketPosition != MarketPosition.Flat && currentTradeEntryBar >= 0)
            {
                UpdateTradeMFEandMAE();
            }

            previousPosition = marketPosition;
        }

        private void UpdateTradeMFEandMAE()
        {
            if (currentTradeDirection == MarketPosition.Flat || double.IsNaN(currentTradeEntryPrice))
                return;

            try
            {
                double currentPrice = Close[0];
                double unrealizedPnL = 0.0;

                if (currentTradeDirection == MarketPosition.Long)
                {
                    unrealizedPnL = (currentPrice - currentTradeEntryPrice) * Instrument.MasterInstrument.PointValue;
                }
                else if (currentTradeDirection == MarketPosition.Short)
                {
                    unrealizedPnL = (currentTradeEntryPrice - currentPrice) * Instrument.MasterInstrument.PointValue;
                }

                // Update MFE (best profit)
                if (unrealizedPnL > currentTradeMFE)
                    currentTradeMFE = unrealizedPnL;

                // Update MAE (worst loss)
                if (unrealizedPnL < currentTradeMAE)
                    currentTradeMAE = unrealizedPnL;
            }
            catch { }
        }

        private void SendTradeCompletedToDashboard()
        {
            if (currentTradeEntryBar < 0 || double.IsNaN(currentTradeEntryPrice))
                return;

            try
            {
                // Get the actual completed trade from NinjaTrader
                Trade lastTrade = null;
                if (SystemPerformance != null && SystemPerformance.AllTrades != null && SystemPerformance.AllTrades.Count > 0)
                {
                    lastTrade = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
                }

                if (lastTrade == null || lastTrade.Exit == null)
                {
                    Print($"[TRADE_EXIT] Bar {CurrentBar}: Position closed but no trade found in SystemPerformance");
                    return;
                }

                // Calculate bars heldmmmmmmmm
                int barsHeld = CurrentBar - currentTradeEntryBar;
                if (barsHeld < 0) barsHeld = 0;

                // Get realized P&L in points
                double realizedPoints = 0.0;
                if (lastTrade.ProfitCurrency != 0 && Instrument.MasterInstrument.PointValue > 0)
                {
                    realizedPoints = lastTrade.ProfitCurrency / Instrument.MasterInstrument.PointValue;
                }

                // Get exit reason from order name or default, and enhance with stop loss info if applicable
                string exitReason = "Unknown";
                if (lastTrade.Exit != null && lastTrade.Exit.Order != null)
                {
                    Order exitOrder = lastTrade.Exit.Order;
                    exitReason = exitOrder.Name ?? "Unknown";
                    
                    // Check if this is a stop loss exit and add the stop loss price
                    if (exitOrder.OrderType == OrderType.StopMarket || exitOrder.OrderType == OrderType.StopLimit)
                    {
                        double stopLossPrice = lastTrade.Exit.Price; // The actual price where stop was triggered
                        string stopType = exitOrder.OrderType == OrderType.StopMarket ? "StopMarket" : "StopLimit";
                        
                        // Determine stop type from order name
                        bool isTrailStop = exitOrder.Name != null && (exitOrder.Name.Contains("Trail") || exitOrder.Name.Contains("trail"));
                        bool isBreakEven = breakEvenActivated;
                        
                        // Calculate stop loss distance in points
                        // Use the stored stop loss distance if available, otherwise calculate from entry/exit prices
                        double stopLossPoints = currentTradeStopLossPoints;
                        
                        // If we don't have the stored value, calculate from prices
                        if (stopLossPoints <= 0 || double.IsNaN(stopLossPoints))
                        {
                            if (currentTradeDirection == MarketPosition.Long)
                            {
                                stopLossPoints = currentTradeEntryPrice - stopLossPrice;
                            }
                            else if (currentTradeDirection == MarketPosition.Short)
                            {
                                stopLossPoints = stopLossPrice - currentTradeEntryPrice;
                            }
                            
                            // CRITICAL: If calculated stop loss is 0 or negative, this indicates a problem
                            // (stop loss should always be set and should never equal entry price)
                            if (stopLossPoints <= 0)
                            {
                                Print($"[SendTradeCompletedToDashboard] WARNING: Calculated stop loss points is {stopLossPoints:F2} - this should never happen! Entry={currentTradeEntryPrice:F2}, Exit={stopLossPrice:F2}");
                                // Use the configured stop loss as fallback to avoid showing 0.00
                                stopLossPoints = Math.Max(StopLossPoints, 1.0); // Minimum 1 point
                                Print($"[SendTradeCompletedToDashboard] Using fallback stop loss: {stopLossPoints:F2} pts");
                            }
                        }
                        
                        // For break-even stops, show the offset that was used
                        if (isBreakEven)
                        {
                            exitReason = $"{exitReason} (BreakEven {stopType} @ {stopLossPrice:F2}, {stopLossPoints:F2} pts)";
                        }
                        // For trail stops, show it's a trail stop
                        else if (isTrailStop)
                        {
                            exitReason = $"{exitReason} (Trail {stopType} @ {stopLossPrice:F2}, {stopLossPoints:F2} pts)";
                        }
                        else
                        {
                            exitReason = $"{exitReason} ({stopType} @ {stopLossPrice:F2}, {stopLossPoints:F2} pts)";
                        }
                    }
                }

                // Capture exit bar data (using [1] since we're on first tick of next bar)
                // Exit bar is the previous completed bar
                double exitBarOpen = CurrentBar >= 1 ? Open[1] : double.NaN;
                double exitBarHigh = CurrentBar >= 1 ? High[1] : double.NaN;
                double exitBarLow = CurrentBar >= 1 ? Low[1] : double.NaN;
                double exitBarClose = CurrentBar >= 1 ? Close[1] : double.NaN;
                
                // Determine candle type for exit bar
                string exitBarCandleType = "";
                if (CurrentBar >= 1 && !double.IsNaN(exitBarOpen) && !double.IsNaN(exitBarClose))
                {
                    exitBarCandleType = exitBarClose > exitBarOpen ? "good" : (exitBarClose < exitBarOpen ? "bad" : "");
                }
                
                // Get Fast EMA value at exit bar (using [1] for completed bar)
                double exitBarFastEma = (fastEma != null && CurrentBar >= 1 && CurrentBar >= FastEmaPeriod) ? fastEma[1] : double.NaN;
                
                // Get Fast EMA gradient degree at ENTRY bar (the gradient that was used for the entry decision)
                // The gradient is calculated at the START of a bar using completed bars (ending at barsAgo=1)
                // The gradient calculated on bar N represents the trend ending at bar N-1, and is displayed on bar N-1 (chart)
                // When entry happens on bar N (chart display, 1-indexed), the gradient used was from bar N-1 (chart)
                // currentTradeEntryBar is 1-indexed (for display), but gradientByBar uses 0-indexed CurrentBar
                // To match the chart label (which shows gradient on the bar it represents), use: entryBarIndex = currentTradeEntryBar - 2
                double entryBarFastEmaGradDeg = double.NaN;
                int entryBarIndex = currentTradeEntryBar - 2; // Convert and go back one more bar to match chart label position
                if (entryBarIndex >= 0 && gradientByBar.ContainsKey(entryBarIndex))
                {
                    entryBarFastEmaGradDeg = gradientByBar[entryBarIndex];
                }
                // Fallback: try the bar where gradient was calculated (entryBarIndex + 1)
                else if (entryBarIndex >= -1)
                {
                    int calcBarIndex = currentTradeEntryBar - 1; // Bar where gradient was calculated
                    if (gradientByBar.ContainsKey(calcBarIndex))
                    {
                        entryBarFastEmaGradDeg = gradientByBar[calcBarIndex];
                    }
                }
                if (double.IsNaN(entryBarFastEmaGradDeg) && !double.IsNaN(lastFastEmaGradDeg))
                {
                    // Final fallback to current gradient if entry bar gradient not found
                    entryBarFastEmaGradDeg = lastFastEmaGradDeg;
                }
                
                // Get Fast EMA gradient degree at exit bar (for reference)
                double exitBarFastEmaGradDeg = !double.IsNaN(lastFastEmaGradDeg) ? lastFastEmaGradDeg : double.NaN;
                
                // Get bar pattern at exit time
                string exitBarPattern = GetBarSequencePattern();
                
                // Build trade data JSON manually (no external JSON library needed)
                var sb = new StringBuilder();
                sb.Append("{");
                sb.Append($"\"EntryTime\":{currentTradeEntryTime.ToUniversalTime().Subtract(new DateTime(1970, 1, 1)).TotalSeconds},");
                sb.Append($"\"EntryBar\":{currentTradeEntryBar},");
                sb.Append($"\"Direction\":\"{(currentTradeDirection == MarketPosition.Long ? "Long" : "Short")}\",");
                sb.Append($"\"EntryPrice\":{currentTradeEntryPrice},");
                sb.Append($"\"ExitTime\":{lastTrade.Exit.Time.ToUniversalTime().Subtract(new DateTime(1970, 1, 1)).TotalSeconds},");
                sb.Append($"\"ExitBar\":{CurrentBar + 1},");  // Add 1 to match chart display (chart is 1-indexed, CurrentBar is 0-indexed)
                sb.Append($"\"ExitPrice\":{lastTrade.Exit.Price},");
                sb.Append($"\"BarsHeld\":{barsHeld},");
                sb.Append($"\"RealizedPoints\":{realizedPoints},");
                sb.Append($"\"MFE\":{currentTradeMFE},");
                sb.Append($"\"MAE\":{currentTradeMAE},");
                sb.Append($"\"ExitReason\":\"{exitReason.Replace("\"", "\\\"")}\",");
                sb.Append($"\"EntryReason\":\"{(string.IsNullOrEmpty(currentTradeEntryReason) ? "Unknown" : currentTradeEntryReason.Replace("\"", "\\\""))}\",");
                sb.Append($"\"Contracts\":{currentTradeContracts},");
                sb.Append($"\"EmaFastPeriod\":{EmaFastPeriod},");
                sb.Append($"\"EmaSlowPeriod\":{EmaSlowPeriod},");
                // Add actual EMA values at exit time
                sb.Append($"\"EmaFastValue\":{(emaFast != null && CurrentBar >= EmaFastPeriod ? emaFast[0].ToString("F4") : "null")},");
                sb.Append($"\"EmaSlowValue\":{(emaSlow != null && CurrentBar >= EmaSlowPeriod ? emaSlow[0].ToString("F4") : "null")},");
                // Add exit bar OHLC and other data
                sb.Append($"\"OpenFinal\":{(double.IsNaN(exitBarOpen) ? "null" : exitBarOpen.ToString("F4"))},");
                sb.Append($"\"HighFinal\":{(double.IsNaN(exitBarHigh) ? "null" : exitBarHigh.ToString("F4"))},");
                sb.Append($"\"LowFinal\":{(double.IsNaN(exitBarLow) ? "null" : exitBarLow.ToString("F4"))},");
                sb.Append($"\"CloseFinal\":{(double.IsNaN(exitBarClose) ? "null" : exitBarClose.ToString("F4"))},");
                sb.Append($"\"FastEma\":{(double.IsNaN(exitBarFastEma) ? "null" : exitBarFastEma.ToString("F4"))},");
                sb.Append($"\"FastEmaGradDeg\":{(double.IsNaN(entryBarFastEmaGradDeg) ? "null" : entryBarFastEmaGradDeg.ToString("F4"))},");
                sb.Append($"\"CandleType\":\"{exitBarCandleType}\",");
                sb.Append($"\"BarPattern\":\"{exitBarPattern.Replace("\"", "\\\"")}\"");
                sb.Append("}");

                string json = sb.ToString();
                string url = DashboardBaseUrl.TrimEnd('/') + "/trade_completed";

                // Send asynchronously
                Task.Run(() =>
                {
                    try
                    {
                        SendJsonToServer(json, url);
                        Print($"[TRADE_EXIT] Bar {CurrentBar}: Trade data sent to dashboard");
                    }
                    catch (ObjectDisposedException ex)
                    {
                        Print($"[TRADE_EXIT] Bar {CurrentBar}: HttpClient disposed, skipping send: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Print($"[TRADE_EXIT] Bar {CurrentBar}: Error sending trade data: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Print($"[TRADE_EXIT] Bar {CurrentBar}: Error preparing trade data: {ex.Message}");
            }
        }

        private void ClearTradesTable()
        {
            // Clear trades table on fresh run
            try
            {
                string url = DashboardBaseUrl.TrimEnd('/') + "/api/trades/clear";
                
                // Send asynchronously
                Task.Run(async () =>
                {
                    try
                    {
                        EnsureHttpClient();
                        var response = await sharedClient.PostAsync(url, null);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            Print($"[TRADES_CLEAR] Trades table cleared for fresh run");
                        }
                        else
                        {
                            Print($"[TRADES_CLEAR] Failed to clear trades - HTTP {response.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Print($"[TRADES_CLEAR] Error clearing trades: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Print($"[TRADES_CLEAR] Error preparing clear request: {ex.Message}");
            }
        }

        private void ClearBarsTable()
        {
            // Clear BarsOnTheFlowStateAndBar table on fresh run
            try
            {
                string url = DashboardBaseUrl.TrimEnd('/') + "/api/databases/clear-bars-table";
                
                // Send asynchronously
                Task.Run(async () =>
                {
                    try
                    {
                        EnsureHttpClient();
                        var response = await sharedClient.PostAsync(url, null);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            Print($"[BARS_CLEAR] BarsOnTheFlowStateAndBar table cleared for fresh run");
                        }
                        else
                        {
                            Print($"[BARS_CLEAR] Failed to clear bars table - HTTP {response.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Print($"[BARS_CLEAR] Error clearing bars table: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Print($"[BARS_CLEAR] Error preparing clear request: {ex.Message}");
            }
        }

        private void ClearBarSamples()
        {
            // Clear bar_samples table on fresh run
            try
            {
                string url = DashboardBaseUrl.TrimEnd('/') + "/api/databases/clear-bar-samples";
                
                // Send asynchronously
                Task.Run(async () =>
                {
                    try
                    {
                        EnsureHttpClient();
                        var response = await sharedClient.PostAsync(url, null);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            Print($"[BAR_SAMPLES_CLEAR] bar_samples table cleared for fresh run");
                        }
                        else
                        {
                            Print($"[BAR_SAMPLES_CLEAR] Failed to clear bar_samples - HTTP {response.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Print($"[BAR_SAMPLES_CLEAR] Error clearing bar_samples: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Print($"[BAR_SAMPLES_CLEAR] Error preparing clear request: {ex.Message}");
            }
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
                logWriter.WriteLine($"# MinMatchingBars={MinMatchingBars}");
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
                state.AppendLine($"  \"contractsTrading\": {Position.Quantity},");
                
                // Also report account-level position
                int accountQuantity = 0;
                if (Account != null && Account.Positions != null)
                {
                    foreach (var pos in Account.Positions)
                    {
                        if (pos.Instrument == Instrument)
                        {
                            accountQuantity = pos.Quantity;
                            break;
                        }
                    }
                }
                state.AppendLine($"  \"accountQuantity\": {accountQuantity},");
                
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
                
                // Break-even settings
                state.AppendLine($"  \"useBreakEven\": {UseBreakEven.ToString().ToLower()},");
                state.AppendLine($"  \"breakEvenTrigger\": {BreakEvenTrigger},");
                state.AppendLine($"  \"breakEvenOffset\": {BreakEvenOffset},");
                state.AppendLine($"  \"breakEvenActivated\": {breakEvenActivated.ToString().ToLower()},");
                
                state.AppendLine($"  \"enableShorts\": {EnableShorts.ToString().ToLower()},");
                state.AppendLine($"  \"avoidLongsOnBadCandle\": {AvoidLongsOnBadCandle.ToString().ToLower()},");
                state.AppendLine($"  \"avoidShortsOnGoodCandle\": {AvoidShortsOnGoodCandle.ToString().ToLower()},");
                state.AppendLine($"  \"exitOnTrendBreak\": {ExitOnTrendBreak.ToString().ToLower()},");
                state.AppendLine($"  \"reverseOnTrendBreak\": {ReverseOnTrendBreak.ToString().ToLower()},");
                state.AppendLine($"  \"fastEmaPeriod\": {FastEmaPeriod},");
                state.AppendLine($"  \"gradientFilterEnabled\": {GradientFilterEnabled.ToString().ToLower()},");
                state.AppendLine($"  \"skipLongsBelowGradient\": {SkipLongsBelowGradient},");
                state.AppendLine($"  \"skipShortsAboveGradient\": {SkipShortsAboveGradient},");
                state.AppendLine($"  \"useEmaCrossoverFilter\": {UseEmaCrossoverFilter.ToString().ToLower()},");
                state.AppendLine($"  \"emaFastPeriod\": {EmaFastPeriod},");
                state.AppendLine($"  \"emaSlowPeriod\": {EmaSlowPeriod},");
                state.AppendLine($"  \"emaCrossoverWindowBars\": {EmaCrossoverWindowBars},");
                state.AppendLine($"  \"emaCrossoverRequireCrossover\": {EmaCrossoverRequireCrossover.ToString().ToLower()},");
                state.AppendLine($"  \"emaCrossoverCooldownBars\": {EmaCrossoverCooldownBars},");
                state.AppendLine($"  \"emaCrossoverMinTicksCloseToFast\": {EmaCrossoverMinTicksCloseToFast},");
                state.AppendLine($"  \"emaCrossoverMinTicksFastToSlow\": {EmaCrossoverMinTicksFastToSlow},");
                state.AppendLine($"  \"enableBarsOnTheFlowTrendDetection\": {EnableBarsOnTheFlowTrendDetection.ToString().ToLower()},");
                state.AppendLine($"  \"trendLookbackBars\": {TrendLookbackBars},");
                state.AppendLine($"  \"minMatchingBars\": {MinMatchingBars},");
                state.AppendLine($"  \"usePnLTiebreaker\": {UsePnLTiebreaker.ToString().ToLower()},");
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
            // Send state to server on each bar for real-time monitoring
            // This method is called on every bar close to ensure all bars are logged
            if (EnableDashboardDiagnostics)
            {
                try
            {
                // Build JSON on main thread (safe access to NinjaTrader objects)
                string jsonPayload = BuildStrategyStateJson();
                string url = DashboardBaseUrl.TrimEnd('/') + "/state";
                
                    // Extract barIndex for logging
                    int barIndexForLog = CurrentBar;
                    try
                    {
                        int barIndexPos = jsonPayload.IndexOf("\"barIndex\":");
                        if (barIndexPos >= 0)
                        {
                            int startPos = barIndexPos + 11;
                            int endPos = jsonPayload.IndexOf(',', startPos);
                            if (endPos < 0) endPos = jsonPayload.IndexOf('}', startPos);
                            if (endPos > startPos)
                            {
                                string barIndexStr = jsonPayload.Substring(startPos, endPos - startPos).Trim();
                                if (int.TryParse(barIndexStr, out int parsed))
                                    barIndexForLog = parsed;
                            }
                        }
                    }
                    catch { }
                    
                    // For historical data, use synchronous to ensure all bars are logged
                    // For realtime, use async to avoid blocking
                    bool useAsync = (State == State.Realtime) && (State != State.Historical || DashboardAsyncHistorical);

                if (useAsync)
                {
                    // Async fire-and-forget to avoid blocking (fast, may miss late bars in historical)
                        Task.Run(() => {
                            try
                            {
                                SendJsonToServer(jsonPayload, url);
                            }
                            catch (Exception ex)
                            {
                                Print($"[Dashboard] Background send failed for bar {barIndexForLog}: {ex.Message}");
                            }
                        });
                }
                else
                {
                    // Synchronous during historical when completeness is required
                        // This ensures all bars are sent even if some requests are slow
                        try
                        {
                    SendJsonToServer(jsonPayload, url);
                        }
                        catch (Exception ex)
                        {
                            Print($"[Dashboard] Sync send exception for bar {barIndexForLog}: {ex.Message}");
                        }
                    }
                    
                    // Log every 100 bars to track progress
                    if (barIndexForLog % 100 == 0)
                    {
                        Print($"[Dashboard] State update sent for bar {barIndexForLog}");
                    }
                }
                catch (Exception ex)
                {
                    Print($"[Dashboard] UpdateStrategyState failed for bar {CurrentBar}: {ex.Message}");
                }
            }
            
            // Keep file backup every 10 bars to avoid excessive I/O
            if (CurrentBar % 10 == 0)
            {
                ExportStrategyState();
            }
        }

        /// <summary>
        /// Sends a pre-built JSON string to the server.
        /// Called on background thread via Task.Run.
        /// </summary>
        private void SendJsonToServer(string json, string url)
        {
            try
            {
                EnsureHttpClient();
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                // Use ConfigureAwait(false) to avoid sync-context deadlocks
                var response = sharedClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                
                // Extract barIndex from JSON string for logging
                int sentBarIndex = -1;
                try
                {
                    int barIndexPos = json.IndexOf("\"barIndex\":");
                    if (barIndexPos >= 0)
                    {
                        int startPos = barIndexPos + 11; // length of "barIndex":
                        int endPos = json.IndexOf(',', startPos);
                        if (endPos < 0) endPos = json.IndexOf('}', startPos);
                        if (endPos > startPos)
                        {
                            string barIndexStr = json.Substring(startPos, endPos - startPos).Trim();
                            if (int.TryParse(barIndexStr, out int parsed))
                                sentBarIndex = parsed;
                        }
                    }
                }
                catch { }
                
                if (!response.IsSuccessStatusCode)
                {
                    Print($"[Dashboard] POST failed for bar {sentBarIndex}: {(int)response.StatusCode} {response.StatusCode}");
                }
                else if (!dashboardPostLoggedSuccess)
                {
                    dashboardPostLoggedSuccess = true;
                    Print($"[Dashboard] POST succeeded: {(int)response.StatusCode} {response.StatusCode} (bar {sentBarIndex})");
                }
            }
            catch (ObjectDisposedException ex)
            {
                Print($"[SendJsonToServer] HttpClient disposed: {ex.Message}");
                // Reset client so it can be recreated on next call
                lock (clientLock)
                {
                    sharedClient = null;
                }
            }
            catch (Exception ex)
            {
                // Extract barIndex from JSON string for logging
                int failedBarIndex = -1;
                try
                {
                    int barIndexPos = json.IndexOf("\"barIndex\":");
                    if (barIndexPos >= 0)
                    {
                        int startPos = barIndexPos + 11; // length of "barIndex":
                        int endPos = json.IndexOf(',', startPos);
                        if (endPos < 0) endPos = json.IndexOf('}', startPos);
                        if (endPos > startPos)
                        {
                            string barIndexStr = json.Substring(startPos, endPos - startPos).Trim();
                            if (int.TryParse(barIndexStr, out int parsed))
                                failedBarIndex = parsed;
                        }
                    }
                }
                catch { }
                
                if (!dashboardPostLoggedFailure)
                {
                    dashboardPostLoggedFailure = true;
                    var baseEx = ex.GetBaseException();
                    Print($"[Dashboard] POST failed for bar {failedBarIndex}: {baseEx.Message} (url={url})");
                    Print($"[Dashboard] Detail: {baseEx}");
                }
                else
                {
                    // Log every failure after the first one (but limit frequency)
                    Print($"[Dashboard] POST failed for bar {failedBarIndex}: {ex.GetBaseException().Message}");
                }
            }
        }

        /// <summary>
        /// Builds the strategy state JSON string on the main thread.
        /// This ensures thread-safe access to all NinjaTrader objects.
        /// </summary>
        private string BuildStrategyStateJson()
        {
            // Use previous bar's final data (index [1]) for OHLC
            // But store CurrentBar as barIndex to match what user sees on chart
            DateTime barTime = Time[1];
            double barOpen = Open[1];
            double barHigh = High[1];
            double barLow = Low[1];
            double barClose = Close[1];
            double barVolume = Volume[1];

            // Calculate dynamic stop loss
            int calcStopTicks = CalculateStopLossTicks();
            double calcStopPoints = calcStopTicks / 4.0;

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var json = new StringBuilder();
            json.Append("{");
            json.Append("\"timestamp\":\"").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("\",");
            json.Append("\"strategyName\":\"BarsOnTheFlow\",");
            json.Append("\"isRunning\":true,");
            json.Append("\"enableDashboardDiagnostics\":").Append(EnableDashboardDiagnostics ? "true" : "false").Append(',');
            
            // Store CurrentBar as barIndex to match chart display (OHLC is from previous bar [1])
            json.Append("\"barIndex\":").Append(CurrentBar).Append(',');
            json.Append("\"barTime\":\"").Append(barTime.ToString("o")).Append("\",");
            json.Append("\"open\":").Append(barOpen.ToString(ci)).Append(',');
            json.Append("\"high\":").Append(barHigh.ToString(ci)).Append(',');
            json.Append("\"low\":").Append(barLow.ToString(ci)).Append(',');
            json.Append("\"close\":").Append(barClose.ToString(ci)).Append(',');
            json.Append("\"volume\":").Append(barVolume.ToString(ci)).Append(',');
            
            // Current bar index (same as barIndex now, kept for backward compatibility)
            json.Append("\"currentBar\":").Append(CurrentBar).Append(',');
            
            // Position state
            json.Append("\"contracts\":").Append(Contracts).Append(',');
            json.Append("\"contractsTrading\":").Append(Position.Quantity).Append(',');
            
            // Also report account-level position for this instrument
            int accountQuantity = 0;
            if (Account != null && Account.Positions != null)
            {
                foreach (var pos in Account.Positions)
                {
                    if (pos.Instrument == Instrument)
                    {
                        accountQuantity = pos.Quantity;
                        break;
                    }
                }
            }
            json.Append("\"accountQuantity\":").Append(accountQuantity).Append(',');
            
            json.Append("\"positionMarketPosition\":\"").Append(Position.MarketPosition).Append("\",");
            json.Append("\"positionQuantity\":").Append(Position.Quantity).Append(',');
            json.Append("\"positionAveragePrice\":").Append(Position.AveragePrice.ToString(ci)).Append(',');
            json.Append("\"intendedPosition\":\"").Append(intendedPosition).Append("\",");
            
            // Stop loss configuration
            json.Append("\"stopLossPoints\":").Append(StopLossPoints).Append(',');
            json.Append("\"calculatedStopTicks\":").Append(calcStopTicks).Append(',');
            json.Append("\"calculatedStopPoints\":").Append(calcStopPoints.ToString("F2", ci)).Append(',');
            json.Append("\"useTrailingStop\":").Append(UseTrailingStop ? "true" : "false").Append(',');
            json.Append("\"useDynamicStopLoss\":").Append(UseDynamicStopLoss ? "true" : "false").Append(',');
            json.Append("\"lookback\":").Append(DynamicStopLookback).Append(',');
            json.Append("\"multiplier\":").Append(DynamicStopMultiplier.ToString(ci)).Append(',');
            
            // Break-even configuration
            json.Append("\"useBreakEven\":").Append(UseBreakEven ? "true" : "false").Append(',');
            json.Append("\"breakEvenTrigger\":").Append(BreakEvenTrigger).Append(',');
            json.Append("\"breakEvenOffset\":").Append(BreakEvenOffset).Append(',');
            json.Append("\"breakEvenActivated\":").Append(breakEvenActivated ? "true" : "false").Append(',');
            
            // Strategy parameters
            json.Append("\"enableShorts\":").Append(EnableShorts ? "true" : "false").Append(',');
            json.Append("\"avoidLongsOnBadCandle\":").Append(AvoidLongsOnBadCandle ? "true" : "false").Append(',');
            json.Append("\"avoidShortsOnGoodCandle\":").Append(AvoidShortsOnGoodCandle ? "true" : "false").Append(',');
            json.Append("\"exitOnTrendBreak\":").Append(ExitOnTrendBreak ? "true" : "false").Append(',');
            json.Append("\"reverseOnTrendBreak\":").Append(ReverseOnTrendBreak ? "true" : "false").Append(',');
            json.Append("\"fastEmaPeriod\":").Append(FastEmaPeriod).Append(',');
            json.Append("\"gradientThresholdSkipLongs\":").Append(SkipLongsBelowGradient.ToString(ci)).Append(',');
            json.Append("\"gradientThresholdSkipShorts\":").Append(SkipShortsAboveGradient.ToString(ci)).Append(',');
            json.Append("\"gradientFilterEnabled\":").Append(GradientFilterEnabled ? "true" : "false").Append(',');
            
            // EMA crossover filter parameters
            json.Append("\"useEmaCrossoverFilter\":").Append(UseEmaCrossoverFilter ? "true" : "false").Append(',');
            json.Append("\"emaFastPeriod\":").Append(EmaFastPeriod).Append(',');
            json.Append("\"emaSlowPeriod\":").Append(EmaSlowPeriod).Append(',');
            json.Append("\"emaCrossoverWindowBars\":").Append(EmaCrossoverWindowBars).Append(',');
            json.Append("\"emaCrossoverRequireCrossover\":").Append(EmaCrossoverRequireCrossover ? "true" : "false").Append(',');
            json.Append("\"emaCrossoverCooldownBars\":").Append(EmaCrossoverCooldownBars).Append(',');
            
            // Gradient values (degrees)
            if (!double.IsNaN(lastFastEmaGradDeg))
                json.Append("\"fastGradDeg\":").Append(lastFastEmaGradDeg.ToString(ci)).Append(',');
            // Note: slow gradient not currently calculated, can be added later if needed
            // if (!double.IsNaN(lastSlowEmaGradDeg))
            //     json.Append("\"slowGradDeg\":").Append(lastSlowEmaGradDeg.ToString(ci)).Append(',');
            
            // Trend parameters
            json.Append("\"enableBarsOnTheFlowTrendDetection\":").Append(EnableBarsOnTheFlowTrendDetection ? "true" : "false").Append(',');
            json.Append("\"trendLookbackBars\":").Append(TrendLookbackBars).Append(',');
            json.Append("\"minMatchingBars\":").Append(MinMatchingBars).Append(',');
            json.Append("\"usePnLTiebreaker\":").Append(UsePnLTiebreaker ? "true" : "false").Append(',');
            
            // Pending signals
            json.Append("\"pendingLongFromBad\":").Append(pendingLongFromBad ? "true" : "false").Append(',');
            json.Append("\"pendingShortFromGood\":").Append(pendingShortFromGood ? "true" : "false").Append(',');
            
            // Entry tracking
            json.Append("\"lastEntryBarIndex\":").Append(lastEntryBarIndex).Append(',');
            json.Append("\"lastEntryDirection\":\"").Append(lastEntryDirection).Append("\",");
            
            // Performance metrics (real-time)
            double unrealizedPnL = 0;
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                unrealizedPnL = Position.GetUnrealizedProfitLoss(PerformanceUnit.Points);
            }
            
            double realizedPnL = 0;
            int totalTrades = 0;
            int winningTrades = 0;
            int losingTrades = 0;
            double winRate = 0;
            
            if (SystemPerformance != null && SystemPerformance.AllTrades.Count > 0)
            {
                realizedPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                totalTrades = SystemPerformance.AllTrades.Count;
                winningTrades = SystemPerformance.AllTrades.WinningTrades.Count;
                losingTrades = SystemPerformance.AllTrades.LosingTrades.Count;
                winRate = totalTrades > 0 ? (winningTrades * 100.0 / totalTrades) : 0;
            }
            
            json.Append("\"unrealizedPnL\":").Append(unrealizedPnL.ToString(ci)).Append(',');
            json.Append("\"realizedPnL\":").Append(realizedPnL.ToString(ci)).Append(',');
            json.Append("\"totalTradesCount\":").Append(totalTrades).Append(',');
            json.Append("\"winningTradesCount\":").Append(winningTrades).Append(',');
            json.Append("\"losingTradesCount\":").Append(losingTrades).Append(',');
            json.Append("\"winRate\":").Append(winRate.ToString("F2", ci));
            
            json.Append("}");

            return json.ToString();
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
                logWriter.WriteLine("timestamp,bar,direction,open,high,low,close,openFinal,highFinal,lowFinal,closeFinal,candleType,fastEma,fastEmaGradDeg,volume,bodyPct,upperWick,lowerWick,action,orderName,quantity,price,pnl,reason,prevOpen,prevClose,prevCandleType,allowLongThisBar,allowShortThisBar,trendUpAtDecision,trendDownAtDecision,decisionBarIndex,pendingShortFromGood,pendingLongFromBad,barPattern,useEmaCrossoverFilter,emaFastPeriod,emaSlowPeriod,emaCrossoverWindowBars,emaCrossoverRequireCrossover,emaCrossoverCooldownBars,emaFastValue,emaSlowValue");
                
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
                    "emaCrossoverFilterLong,emaCrossoverFilterShort,emaFastPeriod,emaSlowPeriod,emaCrossoverWindowBars," +
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
        protected void Print(string message)
        {
            base.Print(message);
            LogToOutput("INFO", message);
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

                // Use the gradient that was stored at the start of the bar being logged
                // This ensures we log the gradient that was used for entry decisions, not the gradient for the next bar
                double gradForBar = gradientByBar.ContainsKey(barIndex) ? gradientByBar[barIndex] : lastFastEmaGradDeg;
                string gradStr = double.IsNaN(gradForBar) ? string.Empty : gradForBar.ToString("F4");
                
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
                // Check if this is a break-even stop exit
                if (breakEvenActivated && (order.OrderType == OrderType.StopMarket || order.OrderType == OrderType.StopLimit))
                {
                    return "BreakEvenStop";
                }
                
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

        /// <summary>
        /// RECORD COMPLETED BAR - Uses FINAL OHLC [1]
        /// Records the completed bar's final OHLC data for trend analysis.
        /// Called on bar close (first tick of new bar) with [1] values (completed bar).
        /// </summary>
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

        /// <summary>
        /// TREND DETECTION - Uses FINAL OHLC [1]
        /// Analyzes completed bars from recentGood queue (populated by RecordCompletedBar using [1]).
        /// Called on bar close (first tick of new bar) when [1] contains the completed bar's final OHLC.
        /// NOTE: This method is only effective when EnableBarsOnTheFlowTrendDetection is true.
        /// When disabled, returns false regardless of trend analysis.
        /// </summary>
        private bool IsTrendUp()
        {
            // Early exit if master switch is disabled (defensive check, though caller should handle this)
            if (!EnableBarsOnTheFlowTrendDetection)
                return false;
                
            // Ensure we have enough data in the lookback window
            int lookback = Math.Min(TrendLookbackBars, recentGood.Count);
            if (lookback < MinMatchingBars)
                return false;

            // Get the last 'lookback' bars
            var lastBars = recentGood.ToArray().Skip(Math.Max(0, recentGood.Count - lookback)).ToArray();
            var lastPnls = recentPnl.ToArray().Skip(Math.Max(0, recentPnl.Count - lookback)).ToArray();
            
            int goodCount = lastBars.Count(g => g);
            double netPnl = lastPnls.Sum();
            
            // Primary condition: minimum matching bars met
            if (goodCount >= MinMatchingBars)
                return true;
            
            // Secondary condition: PnL tiebreaker (if enabled and we're close to minimum)
            if (UsePnLTiebreaker && goodCount >= (MinMatchingBars - 1) && netPnl > 0)
                return true;
            
            return false;
        }

        /// <summary>
        /// TREND DETECTION - Uses FINAL OHLC [1]
        /// Analyzes completed bars from recentGood queue (populated by RecordCompletedBar using [1]).
        /// Called on bar close (first tick of new bar) when [1] contains the completed bar's final OHLC.
        /// NOTE: This method is only effective when EnableBarsOnTheFlowTrendDetection is true.
        /// When disabled, the caller will return false regardless of this method's result.
        /// </summary>
        private bool IsTrendDown()
        {
            // Early exit if master switch is disabled (defensive check, though caller should handle this)
            if (!EnableBarsOnTheFlowTrendDetection)
                return false;
                
            // Ensure we have enough data in the lookback window
            int lookback = Math.Min(TrendLookbackBars, recentGood.Count);
            if (lookback < MinMatchingBars)
                return false;

            // Get the last 'lookback' bars
            var lastBars = recentGood.ToArray().Skip(Math.Max(0, recentGood.Count - lookback)).ToArray();
            var lastPnls = recentPnl.ToArray().Skip(Math.Max(0, recentPnl.Count - lookback)).ToArray();
            
            int badCount = lastBars.Count(g => !g);
            double netPnl = lastPnls.Sum();
            
            // Primary condition: minimum matching bars met
            if (badCount >= MinMatchingBars)
                return true;
            
            // Secondary condition: PnL tiebreaker (if enabled and we're close to minimum)
            if (UsePnLTiebreaker && badCount >= (MinMatchingBars - 1) && netPnl < 0)
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
                    {
                        ExitLong("BarsOnTheFlowRetrace", "BarsOnTheFlowLong");
                        RecordExitForCooldown(MarketPosition.Long);
                    }
                    else if (Position.MarketPosition == MarketPosition.Short)
                    {
                        ExitShort("BarsOnTheFlowRetraceS", "BarsOnTheFlowShort");
                        RecordExitForCooldown(MarketPosition.Short);
                    }
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
            // EMA Crossover Filter parameters (for trade entry analysis)
            public bool UseEmaCrossoverFilter { get; set; }
            public int EmaFastPeriod { get; set; }
            public int EmaSlowPeriod { get; set; }
            public int EmaCrossoverWindowBars { get; set; }
            public bool EmaCrossoverRequireCrossover { get; set; }
            public int EmaCrossoverCooldownBars { get; set; }
            // Actual EMA values at execution time
            public double EmaFastValue { get; set; }
            public double EmaSlowValue { get; set; }
            public int EmaCrossoverMinTicksCloseToFast { get; set; }
            public int EmaCrossoverMinTicksFastToSlow { get; set; }
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

        /// <summary>
        /// Set stop loss BEFORE entering a trade (when position is still Flat).
        /// This is the correct NinjaTrader pattern - SetStopLoss must be called BEFORE EnterLong/EnterShort.
        /// </summary>
        private void SetInitialStopLoss(string orderName, MarketPosition direction)
        {
            // If using EMA trailing stop, capture Fast EMA value at entry
            if (UseEmaTrailingStop && fastEma != null && CurrentBar >= FastEmaPeriod)
            {
                entryFastEmaValue = fastEma[0]; // Use current bar's EMA value
                currentEmaStopLoss = entryFastEmaValue; // Initialize stop loss to entry EMA value
                
                // Set stop loss to Fast EMA price
                double stopLossPrice = Instrument.MasterInstrument.RoundToTickSize(entryFastEmaValue);
                
                // If requiring full candle below, don't set automatic stop loss (we'll check manually on bar close)
                // Otherwise, set the stop loss normally
                if (!EmaStopRequireFullCandleBelow)
                {
                    SetStopLoss(orderName, CalculationMode.Price, stopLossPrice, false);
                }
                
                Print($"[SetInitialStopLoss] EMA Trailing Stop: Setting stop loss to Fast EMA value at entry: {entryFastEmaValue:F4} (price: {stopLossPrice:F4}), Direction={direction}, RequireFullCandleBelow={EmaStopRequireFullCandleBelow}");
                
                // Store stop loss points for reporting (calculate distance from entry)
                double entryPrice = Close[0]; // Approximate entry price
                currentTradeStopLossPoints = Math.Abs(entryPrice - stopLossPrice);
                
                return; // EMA trailing stop is set, no need for other stop loss logic
            }
            
            int stopLossTicks = CalculateStopLossTicks();
            double stopLossPoints = stopLossTicks / 4.0; // Convert ticks to points (4 ticks per point for MNQ)
            
            // CRITICAL: Stop loss should NEVER be zero - enforce minimum
            if (stopLossTicks <= 0)
            {
                Print($"[SetInitialStopLoss] ERROR: Calculated stop loss is {stopLossTicks} ticks - enforcing minimum of 4 ticks (1 point)");
                stopLossTicks = 4; // Minimum 1 point
                stopLossPoints = 1.0;
            }
            
            // Store the stop loss distance for later use in exit reason
            currentTradeStopLossPoints = stopLossPoints;
            
            Print($"[SetInitialStopLoss] Setting stop loss BEFORE entry: {orderName}, Direction={direction}, StopLossTicks={stopLossTicks}, StopLossPoints={stopLossPoints:F2}, UseTrailingStop={UseTrailingStop}");
            
            // Set stop loss BEFORE entry (position is still Flat at this point)
            if (UseTrailingStop)
            {
                SetTrailStop(orderName, CalculationMode.Ticks, stopLossTicks, false);
                Print($"[SetInitialStopLoss] Set TRAILING stop loss: {stopLossPoints:F2} points ({stopLossTicks} ticks)");
            }
            else
            {
                SetStopLoss(orderName, CalculationMode.Ticks, stopLossTicks, false);
                Print($"[SetInitialStopLoss] Set STATIC stop loss: {stopLossPoints:F2} points ({stopLossTicks} ticks)");
            }
        }
        
        /// <summary>
        /// Apply stop loss when position is already open (e.g., for break-even adjustments).
        /// Use SetInitialStopLoss() when entering a new trade.
        /// </summary>
        private void ApplyStopLoss(string orderName)
        {
            int stopLossTicks = CalculateStopLossTicks();
            double stopLossPoints = stopLossTicks / 4.0; // Convert ticks to points (4 ticks per point for MNQ)
            Print($"[ApplyStopLoss] Called for {orderName}, StopLossTicks={stopLossTicks}, StopLossPoints={stopLossPoints:F2}, Position={Position.MarketPosition}, UseTrailingStop={UseTrailingStop}, UseDynamicStopLoss={UseDynamicStopLoss}");
            
            // CRITICAL: Stop loss should NEVER be zero - enforce minimum
            if (stopLossTicks <= 0)
            {
                Print($"[ApplyStopLoss] ERROR: Calculated stop loss is {stopLossTicks} ticks - enforcing minimum of 4 ticks (1 point)");
                stopLossTicks = 4; // Minimum 1 point
                stopLossPoints = 1.0;
            }
            
            // This method is for adjusting stop loss when position is already open
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                // Store the stop loss distance for later use in exit reason
                currentTradeStopLossPoints = stopLossPoints;
                
                if (UseTrailingStop)
                {
                    SetTrailStop(orderName, CalculationMode.Ticks, stopLossTicks, false);
                    Print($"[ApplyStopLoss] Set TRAILING stop loss: {stopLossPoints:F2} points ({stopLossTicks} ticks)");
                }
                else
                {
                    SetStopLoss(orderName, CalculationMode.Ticks, stopLossTicks, false);
                    Print($"[ApplyStopLoss] Set STATIC stop loss: {stopLossPoints:F2} points ({stopLossTicks} ticks)");
                }
            }
            else
            {
                Print($"[ApplyStopLoss] WARNING: Position is Flat - cannot adjust stop loss. Use SetInitialStopLoss() before entering.");
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
                int fixedStopTicks = StopLossPoints * 4; // Convert points to ticks
                // Enforce minimum: Never allow 0 stop loss (minimum 4 ticks = 1 point)
                // Even if user sets StopLossPoints to 0, we enforce a minimum for safety
                return Math.Max(fixedStopTicks, 4); // Minimum 1 point (4 ticks)
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
        
        /// <summary>
        /// Record exit bar when a position exits - cooldown counts from exit, not crossover
        /// </summary>
        private void RecordExitForCooldown(MarketPosition exitedPosition)
        {
            // Reset EMA stop loss tracking when position is closed
            ResetEmaStopLoss();
            
            if (exitedPosition == MarketPosition.Long)
            {
                lastLongExitBar = CurrentBar;
                Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar}: Recording LONG exit for cooldown (lastLongExitBar={lastLongExitBar})");
            }
            else if (exitedPosition == MarketPosition.Short)
            {
                lastShortExitBar = CurrentBar;
                Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar}: Recording SHORT exit for cooldown (lastShortExitBar={lastShortExitBar})");
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private bool ShouldSkipShortDueToGradient(double gradDeg)
        {
            return GradientFilterEnabled && !double.IsNaN(gradDeg) && gradDeg > SkipShortsAboveGradient;
        }

        /// <summary>
        /// EMA CROSSOVER FILTER - Uses FINAL OHLC [1]
        /// Checks if EMA crossover confirmation passes for entry.
        /// 
        /// If RequireCrossover = true:
        ///   For longs: Close crosses above EMA(Fast) AND EMA(Fast) crosses above EMA(Slow).
        ///   For shorts: Close crosses below EMA(Fast) AND EMA(Fast) crosses below EMA(Slow).
        ///   Crossovers can occur on the same bar (strict) or within EmaCrossoverWindowBars (relaxed).
        /// 
        /// If RequireCrossover = false:
        ///   For longs: Close greater than EMA Fast AND EMA Fast greater than EMA Slow (current state check).
        ///   For shorts: Close less than EMA Fast AND EMA Fast less than EMA Slow (current state check).
        /// 
        /// Uses completed bar data [1] for bar-close decisions.
        /// </summary>
        private bool EmaCrossoverFilterPasses(bool isLong)
        {
            // If filter is disabled, always pass
            if (!UseEmaCrossoverFilter)
            {
                Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} {(isLong ? "LONG" : "SHORT")}: Filter disabled, returning true");
                return true;
            }

            // Need indicators initialized
            if (emaFast == null || emaSlow == null || CurrentBar < Math.Max(EmaFastPeriod, EmaSlowPeriod))
            {
                Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} {(isLong ? "LONG" : "SHORT")}: Indicators not ready (emaFast={emaFast != null}, emaSlow={emaSlow != null}, CurrentBar={CurrentBar}, required={Math.Max(EmaFastPeriod, EmaSlowPeriod)})");
                return false;
            }

            // If not requiring crossover, just check current state
            if (!EmaCrossoverRequireCrossover)
            {
                // FIRST: Check cooldown period - this applies regardless of whether conditions are currently met
                // Cooldown counts ALL bars since last EXIT, not since crossover
                if (EmaCrossoverCooldownBars > 0)
                {
                    int lastExitBar = isLong ? lastLongExitBar : lastShortExitBar;
                    string direction = isLong ? "LONG" : "SHORT";
                    
                    if (lastExitBar >= 0)
                    {
                        int barsSinceExit = CurrentBar - lastExitBar;
                        Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} {direction}: Checking cooldown BEFORE conditions check - lastExitBar={lastExitBar}, barsSinceExit={barsSinceExit}, EmaCrossoverCooldownBars={EmaCrossoverCooldownBars}");
                        
                        if (barsSinceExit < EmaCrossoverCooldownBars)
                        {
                            // Still in cooldown period - block entry regardless of current conditions
                            Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} {direction}: COOLDOWN BLOCKING (counts all bars since exit) - {barsSinceExit} < {EmaCrossoverCooldownBars}");
                            return false;
                        }
                        else
                        {
                            Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} {direction}: COOLDOWN PASSED - {barsSinceExit} >= {EmaCrossoverCooldownBars}");
                        }
                    }
                }
                
                // Use [1] for completed bar data
                double close = Close[1];
                double fastEma = emaFast[1];
                double slowEma = emaSlow[1];
                
                // Calculate minimum gaps in price (ticks * tick size)
                double minGapCloseToFast = EmaCrossoverMinTicksCloseToFast * TickSize;
                double minGapFastToSlow = EmaCrossoverMinTicksFastToSlow * TickSize;
                
                bool conditionsMet = false;
                if (isLong)
                {
                    // For longs: Close >= Fast EMA + minGap AND Fast EMA >= Slow EMA + minGap
                    bool closeAboveFast = close >= fastEma + minGapCloseToFast;
                    bool fastAboveSlow = fastEma >= slowEma + minGapFastToSlow;
                    conditionsMet = closeAboveFast && fastAboveSlow;
                    
                    // Track when conditions are met (for cooldown calculation)
                    // Record the most recent bar where conditions became true (for cooldown)
                    if (conditionsMet && CurrentBar > 1)
                    {
                        double prevClose = Close[2];
                        double prevFastEma = emaFast[2];
                        double prevSlowEma = emaSlow[2];
                        bool prevCloseAboveFast = prevClose >= prevFastEma + minGapCloseToFast;
                        bool prevFastAboveSlow = prevFastEma >= prevSlowEma + minGapFastToSlow;
                        bool prevConditionsMet = prevCloseAboveFast && prevFastAboveSlow;
                        
                        Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} LONG: conditionsMet={conditionsMet}, prevConditionsMet={prevConditionsMet}, lastLongCrossoverBar={lastLongCrossoverBar}");
                        Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} LONG: Close[1]={close:F4}, FastEMA[1]={fastEma:F4}, SlowEMA[1]={slowEma:F4}");
                        Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} LONG: Close[2]={prevClose:F4}, FastEMA[2]={prevFastEma:F4}, SlowEMA[2]={prevSlowEma:F4}");
                        Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} LONG: minGapCloseToFast={minGapCloseToFast:F4}, minGapFastToSlow={minGapFastToSlow:F4}");
                        
                        // If conditions just became true (crossover detected), record it
                        if (!prevConditionsMet)
                        {
                            // New crossover: conditions just became true on previous bar [1]
                            lastLongCrossoverBar = CurrentBar - 1;
                            Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} LONG: NEW CROSSOVER detected! Set lastLongCrossoverBar={lastLongCrossoverBar}");
                        }
                        // If conditions are already met but cooldown was reset (e.g., after exit),
                        // record the previous bar [1] as the crossover bar to enforce cooldown
                        else if (prevConditionsMet && lastLongCrossoverBar < 0)
                        {
                            // Conditions were already true, but cooldown was reset - record previous bar
                            lastLongCrossoverBar = CurrentBar - 1;
                            Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} LONG: Conditions already met but cooldown reset! Set lastLongCrossoverBar={lastLongCrossoverBar}");
                        }
                        else
                        {
                            Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} LONG: No crossover recorded (prevConditionsMet={prevConditionsMet}, lastLongCrossoverBar={lastLongCrossoverBar})");
                        }
                    }
                }
                else
                {
                    // For shorts: Close <= Fast EMA - minGap AND Fast EMA <= Slow EMA - minGap
                    bool closeBelowFast = close <= fastEma - minGapCloseToFast;
                    bool fastBelowSlow = fastEma <= slowEma - minGapFastToSlow;
                    conditionsMet = closeBelowFast && fastBelowSlow;
                    
                    Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} SHORT: close={close:F4}, fastEma={fastEma:F4}, slowEma={slowEma:F4}");
                    Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} SHORT: closeBelowFast={closeBelowFast} (close={close:F4} <= fastEma-minGap={fastEma - minGapCloseToFast:F4})");
                    Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} SHORT: fastBelowSlow={fastBelowSlow} (fastEma={fastEma:F4} <= slowEma-minGap={slowEma - minGapFastToSlow:F4})");
                    Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} SHORT: conditionsMet={conditionsMet}");
                    
                    // Track when conditions are met (for cooldown calculation)
                    // Record the most recent bar where conditions became true (for cooldown)
                    if (conditionsMet && CurrentBar > 1)
                    {
                        double prevClose = Close[2];
                        double prevFastEma = emaFast[2];
                        double prevSlowEma = emaSlow[2];
                        bool prevCloseBelowFast = prevClose <= prevFastEma - minGapCloseToFast;
                        bool prevFastBelowSlow = prevFastEma <= prevSlowEma - minGapFastToSlow;
                        bool prevConditionsMet = prevCloseBelowFast && prevFastBelowSlow;
                        
                        Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} SHORT: conditionsMet={conditionsMet}, prevConditionsMet={prevConditionsMet}, lastShortCrossoverBar={lastShortCrossoverBar}");
                        Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} SHORT: Close[1]={close:F4}, FastEMA[1]={fastEma:F4}, SlowEMA[1]={slowEma:F4}");
                        Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} SHORT: Close[2]={prevClose:F4}, FastEMA[2]={prevFastEma:F4}, SlowEMA[2]={prevSlowEma:F4}");
                        Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} SHORT: minGapCloseToFast={minGapCloseToFast:F4}, minGapFastToSlow={minGapFastToSlow:F4}");
                        
                        // If conditions just became true (crossover detected), record it
                        if (!prevConditionsMet)
                        {
                            // New crossover: conditions just became true on previous bar [1]
                            lastShortCrossoverBar = CurrentBar - 1;
                            Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} SHORT: NEW CROSSOVER detected! Set lastShortCrossoverBar={lastShortCrossoverBar}");
                        }
                        // If conditions are already met but cooldown was reset (e.g., after exit),
                        // record the previous bar [1] as the crossover bar to enforce cooldown
                        else if (prevConditionsMet && lastShortCrossoverBar < 0)
                        {
                            // Conditions were already true, but cooldown was reset - record previous bar
                            lastShortCrossoverBar = CurrentBar - 1;
                            Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} SHORT: Conditions already met but cooldown reset! Set lastShortCrossoverBar={lastShortCrossoverBar}");
                        }
                        else
                        {
                            Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} SHORT: No crossover recorded (prevConditionsMet={prevConditionsMet}, lastShortCrossoverBar={lastShortCrossoverBar})");
                        }
                    }
                }
                
                // Cooldown was already checked above (before conditions check)
                // Now just return whether conditions are met
                return conditionsMet;
            }

            // Require crossover logic (original behavior)
            // FIRST: Check cooldown period - this applies regardless of whether crossovers are found
            // Cooldown counts ALL bars since last EXIT, not since crossover
            if (EmaCrossoverCooldownBars > 0)
            {
                int lastExitBar = isLong ? lastLongExitBar : lastShortExitBar;
                string direction = isLong ? "LONG" : "SHORT";
                
                if (lastExitBar >= 0)
                {
                    int barsSinceExit = CurrentBar - lastExitBar;
                    Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} {direction}: Checking cooldown BEFORE crossover check - lastExitBar={lastExitBar}, barsSinceExit={barsSinceExit}, EmaCrossoverCooldownBars={EmaCrossoverCooldownBars}");
                    
                    if (barsSinceExit < EmaCrossoverCooldownBars)
                    {
                        // Still in cooldown period - block entry regardless of current crossovers
                        Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} {direction}: COOLDOWN BLOCKING (counts all bars since exit) - {barsSinceExit} < {EmaCrossoverCooldownBars}");
                        return false;
                    }
                    else
                    {
                        Print($"[EMA_COOLDOWN_DEBUG] Bar {CurrentBar} {direction}: COOLDOWN PASSED - {barsSinceExit} >= {EmaCrossoverCooldownBars}");
                    }
                }
            }
            
            // Check if crossovers occurred within the window (looking back from current bar [1])
            int window = Math.Max(0, EmaCrossoverWindowBars);
            bool closeCrossEmaFastFound = false;
            bool emaFastCrossEmaSlowFound = false;
            int closeCrossBar = -1;
            int emaCrossBar = -1;

            // Look back through the window to find crossovers
            for (int i = 1; i <= window + 1; i++)
            {
                if (CurrentBar < i)
                    break;

                // Check for Close crossing EMA Fast
                if (!closeCrossEmaFastFound)
                {
                    bool closeCross = isLong
                        ? CrossAbove(Close, emaFast, i)
                        : CrossBelow(Close, emaFast, i);
                    if (closeCross)
                    {
                        closeCrossEmaFastFound = true;
                        closeCrossBar = i;
                    }
                }

                // Check for EMA Fast crossing EMA Slow
                if (!emaFastCrossEmaSlowFound)
                {
                    bool emaCross = isLong
                        ? CrossAbove(emaFast, emaSlow, i)
                        : CrossBelow(emaFast, emaSlow, i);
                    if (emaCross)
                    {
                        emaFastCrossEmaSlowFound = true;
                        emaCrossBar = i;
                    }
                }
            }

            // Both crossovers must be found
            if (!closeCrossEmaFastFound || !emaFastCrossEmaSlowFound)
                return false;

            // If window is 0, both must be on the same bar (strict mode)
            if (window == 0)
            {
                if (closeCrossBar != 1 || emaCrossBar != 1)
                    return false;
            }
            else
            {
                // If window > 0, both must be within the window (relaxed mode)
                // Check if the crossovers are within the window of each other
                int barsApart = Math.Abs(closeCrossBar - emaCrossBar);
                if (barsApart > window)
                    return false;
            }

            // Record the crossover bar (use the most recent crossover bar)
            int crossoverBar = Math.Max(closeCrossBar, emaCrossBar);
            if (isLong)
            {
                // Crossover happened at CurrentBar - crossoverBar bars ago
                lastLongCrossoverBar = CurrentBar - crossoverBar;
            }
            else
            {
                lastShortCrossoverBar = CurrentBar - crossoverBar;
            }

            // Cooldown was already checked above (before crossover detection)

            // After crossovers are found, check minimum gap requirements on current bar [1]
            double closeCurrent = Close[1];
            double fastEmaCurrent = emaFast[1];
            double slowEmaCurrent = emaSlow[1];
            
            // Calculate minimum gaps in price (ticks * tick size)
            double minGapCloseToFastCurrent = EmaCrossoverMinTicksCloseToFast * TickSize;
            double minGapFastToSlowCurrent = EmaCrossoverMinTicksFastToSlow * TickSize;
            
            if (isLong)
            {
                // For longs: Close >= Fast EMA + minGap AND Fast EMA >= Slow EMA + minGap
                bool closeAboveFast = closeCurrent >= fastEmaCurrent + minGapCloseToFastCurrent;
                bool fastAboveSlow = fastEmaCurrent >= slowEmaCurrent + minGapFastToSlowCurrent;
                return closeAboveFast && fastAboveSlow;
            }
            else
            {
                // For shorts: Close <= Fast EMA - minGap AND Fast EMA <= Slow EMA - minGap
                bool closeBelowFast = closeCurrent <= fastEmaCurrent - minGapCloseToFastCurrent;
                bool fastBelowSlow = fastEmaCurrent <= slowEmaCurrent - minGapFastToSlowCurrent;
                return closeBelowFast && fastBelowSlow;
            }
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
                // Get gradient degree for this bar (use the gradient calculated on this bar, which represents trend ending at previous bar)
                double gradDeg = double.NaN;
                if (gradientByBar.ContainsKey(CurrentBar))
                {
                    gradDeg = gradientByBar[CurrentBar];
                }
                else if (!double.IsNaN(lastFastEmaGradDeg))
                {
                    gradDeg = lastFastEmaGradDeg;
                }
                
                // Calculate stop loss value - always show the configured/calculated stop loss, not just when in trade
                double stopLossPointsValue = currentTradeStopLossPoints;
                if (stopLossPointsValue <= 0)
                {
                    // If not in a trade, calculate what the stop loss would be based on current configuration
                    int stopLossTicks = CalculateStopLossTicks();
                    stopLossPointsValue = stopLossTicks / 4.0; // Convert ticks to points (4 ticks per point for MNQ)
                }
                
                // Ensure we always have a valid stop loss value (CalculateStopLossTicks should return at least 4 ticks = 1 point)
                if (stopLossPointsValue <= 0)
                {
                    stopLossPointsValue = StopLossPoints > 0 ? StopLossPoints : 20.0; // Fallback to configured or default 20 points
                }
                
                // Calculate debugging fields for strategy decision analysis
                double prevOpen = CurrentBar >= 1 ? Open[1] : double.NaN;
                double prevClose = CurrentBar >= 1 ? Close[1] : double.NaN;
                bool prevGood = !double.IsNaN(prevOpen) && !double.IsNaN(prevClose) && prevClose > prevOpen;
                bool prevBad = !double.IsNaN(prevOpen) && !double.IsNaN(prevClose) && prevClose < prevOpen;
                string candleType = "flat";
                if (prevGood) candleType = "good";
                else if (prevBad) candleType = "bad";
                
                // Calculate trend and allow flags (same logic as in OnBarUpdate)
                bool trendUp = EnableBarsOnTheFlowTrendDetection ? IsTrendUp() : false;
                bool trendDown = EnableBarsOnTheFlowTrendDetection && EnableShorts ? IsTrendDown() : false;
                
                // If trend detection is disabled but EMA crossover filter is enabled, use EMA crossover as signal generator
                if (!EnableBarsOnTheFlowTrendDetection && UseEmaCrossoverFilter)
                {
                    bool emaLongSignal = EmaCrossoverFilterPasses(true);
                    bool emaShortSignal = EnableShorts && EmaCrossoverFilterPasses(false);
                    if (emaLongSignal) trendUp = true;
                    if (emaShortSignal) trendDown = true;
                }
                
                bool allowShortThisBar = !(AvoidShortsOnGoodCandle && prevGood);
                bool allowLongThisBar = !(AvoidLongsOnBadCandle && prevBad);
                
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
                    $"\"in_trade\":{(Position.MarketPosition != MarketPosition.Flat).ToString().ToLower()}," +
                    $"\"ema_fast_period\":{EmaFastPeriod}," +
                    $"\"ema_slow_period\":{EmaSlowPeriod}," +
                    $"\"ema_fast_value\":{(emaFast != null && CurrentBar >= EmaFastPeriod ? emaFast[1].ToString("F4") : "null")}," +
                    $"\"ema_slow_value\":{(emaSlow != null && CurrentBar >= EmaSlowPeriod ? emaSlow[1].ToString("F4") : "null")}," +
                    $"\"fast_ema_grad_deg\":{(double.IsNaN(gradDeg) ? "null" : gradDeg.ToString("F2"))}," +
                    $"\"stop_loss_points\":{stopLossPointsValue.ToString("F2")}," +
                    $"\"candle_type\":\"{candleType}\"," +
                    $"\"trend_up\":{trendUp.ToString().ToLower()}," +
                    $"\"trend_down\":{trendDown.ToString().ToLower()}," +
                    $"\"allow_long_this_bar\":{allowLongThisBar.ToString().ToLower()}," +
                    $"\"allow_short_this_bar\":{allowShortThisBar.ToString().ToLower()}," +
                    $"\"pending_long_from_bad\":{pendingLongFromBad.ToString().ToLower()}," +
                    $"\"pending_short_from_good\":{pendingShortFromGood.ToString().ToLower()}," +
                    $"\"avoid_longs_on_bad_candle\":{AvoidLongsOnBadCandle.ToString().ToLower()}," +
                    $"\"avoid_shorts_on_good_candle\":{AvoidShortsOnGoodCandle.ToString().ToLower()}}}",
                    System.Text.Encoding.UTF8, "application/json");
                
                // Fire and forget with proper error handling to avoid connection leaks
                sharedClient.PostAsync("http://localhost:51888/api/volatility/record-bar", content)
                    .ContinueWith(t =>
                    {
                        try
                        {
                            if (t.IsFaulted || t.IsCanceled)
                            {
                                // Silently ignore errors - recording bar samples is optional
                            }
                            else if (t.IsCompleted)
                            {
                                // Dispose response to free connection
                                t.Result?.Dispose();
                            }
                        }
                        catch
                        {
                            // Silently ignore - recording bar samples is optional
                        }
                    }, TaskContinuationOptions.ExecuteSynchronously);
            }
            catch
            {
                // Silently ignore - recording bar samples is optional
            }
        }

        private void EnsureHttpClient()
        {
            // Double-check locking pattern
            if (sharedClient != null)
            {
                // Verify client hasn't been disposed
                try
                {
                    // Quick check - accessing a property should throw if disposed
                    var _ = sharedClient.Timeout;
                    return;
                }
                catch (ObjectDisposedException)
                {
                    // Client was disposed, reset to null so we can recreate
                    lock (clientLock)
                    {
                        sharedClient = null;
                    }
                }
            }

            lock (clientLock)
            {
                if (sharedClient == null)
                {
                    try
                    {
                        sharedClient = new HttpClient();
                        sharedClient.Timeout = TimeSpan.FromSeconds(10); // Reduced timeout to avoid hanging connections
                        sharedClient.DefaultRequestHeaders.ConnectionClose = true; // Close connections to avoid pool exhaustion
                        // Limit connection pool size (set once globally)
                        try
                        {
                            System.Net.ServicePointManager.DefaultConnectionLimit = 4; // Limit concurrent connections
                        }
                        catch
                        {
                            // Ignore if ServicePointManager is not available
                        }
                    }
                    catch (Exception ex)
                    {
                        Print($"[EnsureHttpClient] Error creating HttpClient: {ex.Message}");
                        sharedClient = null;
                    }
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
                sharedClient.PostAsync(url, content, cts.Token).ContinueWith(t =>
                {
                    try
                    {
                        if (t.IsCompleted && !t.IsFaulted && !t.IsCanceled)
                        {
                            // Dispose response to free connection
                            t.Result?.Dispose();
                        }
                    }
                    catch
                    {
                        // Silently ignore - dashboard send is best-effort
                    }
                }, TaskContinuationOptions.ExecuteSynchronously);
            }
            catch
            {
                // Dashboard diagnostics are optional; swallow exceptions to avoid impacting the strategy.
            }
        }

        /// <summary>
        /// MID-BAR GRADIENT ENTRY CHECK
        /// Uses CURRENT OHLC [0] - This runs during bar formation, not on bar close.
        /// 
        /// OHLC Usage:
        /// - EMA[0] = Current tick's EMA value (real-time, forming bar) ✓
        /// - EMA[1] = Previous bar's final EMA value (completed) ✓
        /// - Close[0] = Current tick price (real-time) ✓
        /// 
        /// This allows entry when gradient crosses threshold mid-bar, before bar close.
        /// </summary>
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
                    currentTradeEntryReason = $"MidBarGradient; GradientOK({currentGradDeg:F1}°)";
                    PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering LONG (mid-bar gradient), CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}");
                    // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement)
                    SetInitialStopLoss("BarsOnTheFlowLong", MarketPosition.Long);
                    EnterLong(Math.Max(1, Contracts), "BarsOnTheFlowLong");
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
                    currentTradeEntryReason = $"MidBarGradient; GradientOK({currentGradDeg:F1}°)";
                    PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering SHORT (mid-bar gradient), CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}");
                    // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement)
                    SetInitialStopLoss("BarsOnTheFlowShort", MarketPosition.Short);
                    EnterShort(Math.Max(1, Contracts), "BarsOnTheFlowShort");
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

        /// <summary>
        /// MID-BAR GRADIENT EXIT CHECK
        /// Uses CURRENT OHLC [0] - This runs during bar formation, not on bar close.
        /// 
        /// OHLC Usage:
        /// - EMA[0] = Current tick's EMA value (real-time, forming bar) ✓
        /// - Close[0] = Current tick price (real-time) ✓
        /// 
        /// This allows exit when gradient crosses unfavorable threshold mid-bar, before bar close.
        /// </summary>
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
                RecordExitForCooldown(MarketPosition.Long);
                waitingToExitLongOnGradient = false;
            }
            // Exit short if gradient rises above threshold (and we were waiting for it)
            else if (waitingToExitShortOnGradient && Position.MarketPosition == MarketPosition.Short && currentGradDeg > SkipShortsAboveGradient)
            {
                Print($"[EXIT_DEBUG] Bar {CurrentBar}: waitingToExitShortOnGradient resolving - gradient rose {currentGradDeg:F2}° > {SkipShortsAboveGradient:F2}° - Exiting SHORT");
                Print($"[MidBar Exit] Bar {CurrentBar}: Short gradient rose! {currentGradDeg:F2}° > {SkipShortsAboveGradient:F2}° - Exiting");
                ExitShort("BarsOnTheFlowGradExitS", "BarsOnTheFlowShort");
                RecordExitForCooldown(MarketPosition.Short);
                waitingToExitShortOnGradient = false;
            }
        }

        /// <summary>
        /// GRADIENT CALCULATION - Uses FINAL OHLC [1]
        /// Calculates EMA gradient using completed bars (barsAgo 1, 2, etc.).
        /// Called on bar close (first tick of new bar) when [1] contains the completed bar's final EMA value.
        /// </summary>
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

        /// <summary>
        /// CHART-SCALED GRADIENT CALCULATION - Uses FINAL OHLC [1]
        /// Calculates gradient using completed bars (barsAgo 1, 2, etc.) for bar-close decisions.
        /// Called on bar close (first tick of new bar) when [1] contains the completed bar's final EMA value.
        /// </summary>
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
                int goodCount = recentGood.Count >= MinMatchingBars ? recentGood.Count(g => g) : 0;
                int badCount = recentGood.Count >= MinMatchingBars ? recentGood.Count(g => !g) : 0;
                double netPnl = recentPnl.Count >= MinMatchingBars ? recentPnl.Sum() : 0;
                string barPattern = GetBarSequencePattern();

                // Position state
                string currentPos = Position.MarketPosition.ToString();
                int entryBarNum = lastEntryBarIndex;
                double entryPx = Position.MarketPosition != MarketPosition.Flat ? Position.AveragePrice : double.NaN;
                double unrealizedPnl = Position.MarketPosition != MarketPosition.Flat ? Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, close) : 0;

                // Filter results
                bool gradFilterBlocksLong = GradientFilterEnabled && ShouldSkipLongDueToGradient(gradDeg);
                bool gradFilterBlocksShort = GradientFilterEnabled && ShouldSkipShortDueToGradient(gradDeg);
                bool emaCrossoverFilterBlocksLong = UseEmaCrossoverFilter && !EmaCrossoverFilterPasses(true);
                bool emaCrossoverFilterBlocksShort = UseEmaCrossoverFilter && !EmaCrossoverFilterPasses(false);

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
                    else if (emaCrossoverFilterBlocksLong)
                    {
                        actionTaken = "SKIPPED_LONG";
                        blockReason = $"EMACrossoverFilter (Close x EMA{EmaFastPeriod} and EMA{EmaFastPeriod} x EMA{EmaSlowPeriod} not crossed)";
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
                    else if (emaCrossoverFilterBlocksShort)
                    {
                        actionTaken = "SKIPPED_SHORT";
                        blockReason = $"EMACrossoverFilter (Close x EMA{EmaFastPeriod} and EMA{EmaFastPeriod} x EMA{EmaSlowPeriod} not crossed)";
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
                    "{27},{28},{29},{30}," +
                    "{31},{32}," +
                    "{33},{34},{35}",
                    timestamp, bar, open, high, low, close, candleType, volume, 
                    double.IsNaN(emaVal) ? "" : emaVal.ToString("F4"), gradDeg,
                    trendUp, trendDown, goodCount, badCount, netPnl, barPattern,
                    currentPos, entryBarNum, double.IsNaN(entryPx) ? "" : entryPx.ToString("F4"), unrealizedPnl,
                    allowLongThisBar, allowShortThisBar,
                    gradFilterBlocksLong, gradFilterBlocksShort, gradDeg, SkipLongsBelowGradient, SkipShortsAboveGradient,
                    emaCrossoverFilterBlocksLong, emaCrossoverFilterBlocksShort, EmaFastPeriod, EmaSlowPeriod, EmaCrossoverWindowBars,
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
