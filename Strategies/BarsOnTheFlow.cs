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
using System.Diagnostics; // For Process to run Python
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
        [Display(Name = "Trade Real-Time Only", Order = 3, GroupName = "Trading/Entry", Description = "When enabled, the strategy will only trade and log in real-time. Historical processing will be skipped.")]
        public bool TradeRealtimeOnly { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "ExitOnTrendBreak", Order = 3, GroupName = "Trading/Entry")]
        public bool ExitOnTrendBreak { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Min Bars Before Trend Break Exit", Order = 9, GroupName = "Trading/Entry", Description = "Minimum bars to hold a position before allowing exit on trend break. Prevents exiting one bar after entry when the entry bar closes opposite and breaks the trend count. Default: 2.")]
        public int MinBarsBeforeTrendBreakExit { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name = "ExitOnRetrace", Order = 1, GroupName = "Retrace")]
        public bool ExitOnRetrace { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "TrendRetraceFraction", Order = 2, GroupName = "Retrace")]
        public double TrendRetraceFraction { get; set; } = 0.66; // end trend visuals when profit gives back 66% of MFE

        [NinjaScriptProperty]
        [Display(Name = "Delay Retrace Exit On Bullish EMA", Order = 3, GroupName = "Retrace", Description = "When enabled, if a retrace exit is triggered but Fast EMA gradient is positive and price is above Fast EMA, postpone exit by one bar and re-check.")]
        public bool DelayRetraceExitOnBullishEma { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Delay Retrace Exit On Favorable Gradient", Order = 4, GroupName = "Retrace", Description = "When enabled, if a retrace exit is triggered but EMA gradient favors the current trade, postpone exit by one bar and re-check.")]
        public bool DelayRetraceExitOnFavorableGradient { get; set; } = false;

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
        [Display(Name = "Show Trading/Traffic Status", Order = 6, GroupName = "Display", Description = "Shows a fixed label (top-left) indicating whether Traffic Log is enabled and whether trading is currently allowed (Trading Hours / ATR filters).")]
        public bool ShowTradingTrafficStatus { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 12)]
        [Display(Name = "Status Label Top Padding (lines)", Order = 7, GroupName = "Display", Description = "Moves the Trading/Traffic status label down by adding blank lines above it. Increase if it overlaps chart text.")]
        public int TradingTrafficStatusTopPaddingLines { get; set; } = 2;

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
        [Display(Name = "AvoidShortsOnGoodCandle (Current Bar)", Order = 4, GroupName = "Trading/Entry", Description = "When enabled, block SHORT entries if the current bar is up (Close[0] > Open[0]) at the moment of entry.")]
        public bool AvoidShortsOnGoodCandleCurrentBar { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "AvoidLongsOnBadCandle (Current Bar)", Order = 5, GroupName = "Trading/Entry", Description = "When enabled, block LONG entries if the current bar is down (Close[0] < Open[0]) at the moment of entry.")]
        public bool AvoidLongsOnBadCandleCurrentBar { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "Min Ticks: Current Bar Direction", Order = 6, GroupName = "Trading/Entry", Description = "Minimum ticks the current bar must move against the entry to block it. 0 = any move against blocks, 1+ requires that many ticks against before blocking.")]
        public int MinTicksCurrentBarDirection { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name = "Use Trading Hours Filter", Order = 7, GroupName = "Trading/Entry", Description = "When enabled, restricts entries to a time window.")]
        public bool UseTradingHoursFilter { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Trading Start Hour", Order = 8, GroupName = "Trading/Entry", Description = "Start hour for trading window (0-23).")]
        public int TradingStartHour { get; set; } = 11;

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Trading Start Minute", Order = 9, GroupName = "Trading/Entry", Description = "Start minute for trading window (0-59).")]
        public int TradingStartMinute { get; set; } = 0;

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Trading End Hour", Order = 10, GroupName = "Trading/Entry", Description = "End hour for trading window (0-23).")]
        public int TradingEndHour { get; set; } = 12;

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Trading End Minute", Order = 11, GroupName = "Trading/Entry", Description = "End minute for trading window (0-59).")]
        public int TradingEndMinute { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Use ATR Filter", Order = 12, GroupName = "Trading/Entry", Description = "When enabled, restricts entries by ATR range.")]
        public bool UseAtrFilter { get; set; } = false;

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ATR Period", Order = 13, GroupName = "Trading/Entry")]
        public int AtrPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ATR Min (Points)", Order = 14, GroupName = "Trading/Entry", Description = "Minimum ATR in points required to allow entries. 0 = no minimum.")]
        public double AtrMin { get; set; } = 0;

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ATR Max (Points)", Order = 15, GroupName = "Trading/Entry", Description = "Maximum ATR in points allowed for entries. 0 = no maximum.")]
        public double AtrMax { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Use Traffic Filter (Ticks/Min)", Order = 16, GroupName = "Trading/Entry", Description = "When enabled, blocks entries when recent ticks-per-minute (from the traffic counter) is below a threshold. This lets you trade Extended Hours but avoid very low-liquidity periods.")]
        public bool UseTrafficFilter { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0, 100000)]
        [Display(Name = "Min Ticks/Min To Trade", Order = 17, GroupName = "Trading/Entry", Description = "Minimum recent ticks-per-minute required to allow entries. 0 disables the minimum (no blocking).")]
        public double MinTicksPerMinuteToTrade { get; set; } = 0;

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Traffic Lookback Bars", Order = 18, GroupName = "Trading/Entry", Description = "Average ticks-per-minute across the last N closed bars for the Traffic Filter. 1 = use only the last closed bar.")]
        public int TrafficFilterLookbackBars { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name = "ExitIfEntryBarOpposite", Order = 6, GroupName = "Trading/Entry", Description = "Exit immediately if the entry bar closes opposite to trade direction (e.g., long entry bar closes down). Helps avoid positions that show immediate weakness.")]
        public bool ExitIfEntryBarOpposite { get; set; } = false; // exit if the entry bar closes opposite to trade direction

        [NinjaScriptProperty]
        [Display(Name = "UseDeferredEntry", Order = 7, GroupName = "Trading/Entry", Description = "When enabled, defers entry to the next bar for validation using the decision bar's completed data. When disabled, entries execute immediately. Deferred entry prevents entries based on stale data but may delay execution by one bar.")]
        public bool UseDeferredEntry { get; set; } = true; // use deferred entry validation to ensure correct bar data

        [NinjaScriptProperty]
        [Display(Name = "ReverseOnTrendBreak", Order = 8, GroupName = "Trading/Entry")]
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
        public bool UseEmaCrossoverFilter { get; set; } = true; // enable EMA crossover confirmation filter

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "EMA Slow Period", Order = 2, GroupName = "EMA Crossover Filter", Description = "Slow EMA period for crossover confirmation (e.g., 20). Fast EMA period is shared with Fast EMA/Gradient settings.")]
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
        public int EmaCrossoverMinTicksCloseToFast { get; set; } = 4; // Minimum ticks between Close and Fast EMA

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Min Ticks: Fast EMA to Slow EMA", Order = 6, GroupName = "EMA Crossover Filter", Description = "Minimum number of ticks between Fast EMA and Slow EMA. Entry requires Fast EMA >= Slow EMA + (this value * TickSize) for longs, or Fast EMA <= Slow EMA - (this value * TickSize) for shorts. 0 = no minimum gap required.")]
        public int EmaCrossoverMinTicksFastToSlow { get; set; } = 4; // Minimum ticks between Fast EMA and Slow EMA

        [NinjaScriptProperty]
        [Range(0, 5)]
        [Display(Name = "Cooldown Bars After Crossover", Order = 7, GroupName = "EMA Crossover Filter", Description = "Number of candles to wait after a crossover before allowing entry. 0 = no cooldown (enter immediately), 1-5 = wait N candles after crossover before entry.")]
        public int EmaCrossoverCooldownBars { get; set; } = 0; // Cooldown bars after crossover

        [NinjaScriptProperty]
        [Display(Name = "Require Body Below/Above Fast EMA", Order = 8, GroupName = "EMA Crossover Filter", Description = "When enabled, requires the entire candle body (Open to Close) to be below Fast EMA for shorts or above Fast EMA for longs. When disabled, only the Close price needs to meet the condition. This filters out weak signals where the close meets the condition but the body extends past the EMA.")]
        public bool EmaCrossoverRequireBodyBelow { get; set; } = false; // require whole body below/above Fast EMA

        [NinjaScriptProperty]
        [Display(Name = "Allow Long When Body Above Fast But Fast Below Slow", Order = 9, GroupName = "EMA Crossover Filter", Description = "When enabled, allows LONG entry if body is completely above FastEMA (with gap) even if FastEMA < SlowEMA (no crossover yet). When disabled, requires FastEMA >= SlowEMA for long entries. This allows earlier entries when price is strong but EMAs haven't crossed yet.")]
        public bool AllowLongWhenBodyAboveFastButFastBelowSlow { get; set; } = false; // allow long entry when body above FastEMA even if FastEMA < SlowEMA

        [NinjaScriptProperty]
        [Display(Name = "Allow Short When Body Below Fast But Fast Above Slow", Order = 10, GroupName = "EMA Crossover Filter", Description = "When enabled, allows SHORT entry if body is completely below FastEMA (with gap) even if FastEMA > SlowEMA (no crossover yet). When disabled, requires FastEMA <= SlowEMA for short entries. This allows earlier entries when price is weak but EMAs haven't crossed yet.")]
        public bool AllowShortWhenBodyBelowFastButFastAboveSlow { get; set; } = false; // allow short entry when body below FastEMA even if FastEMA > SlowEMA

        [NinjaScriptProperty]
        [Display(Name = "GradientFilterEnabled", Order = 1, GroupName = "Fast EMA/Gradient")]
        public bool GradientFilterEnabled { get; set; } = true; // enable gradient-based entry filtering

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Fast EMA Period", Order = 2, GroupName = "Fast EMA/Gradient")]
        public int FastEmaPeriod { get; set; } = 10;

        // Back-compat: if a template references EmaFastPeriod, keep it in sync.
        [Browsable(false)]
        public int EmaFastPeriod
        {
            get { return FastEmaPeriod; }
            set { FastEmaPeriod = value; }
        }

        [NinjaScriptProperty]
        [Range(2, int.MaxValue)]
        [Display(Name = "FastGradLookbackBars", Order = 3, GroupName = "Fast EMA/Gradient", Description = "Legacy setting. Bar-close gradient always uses the 1-bar slope (EMA on the bar that just closed vs the prior bar).")]
        public int FastGradLookbackBars { get; set; } = 2;

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
        [Display(Name = "RecordBarSamplesInHistorical", Order = 4, GroupName = "Database/Dashboard", Description = "If true, record bar samples to database during historical playback (for debugging). WARNING: May cause timeouts with large datasets. Use only when debugging specific bars.")]
        public bool RecordBarSamplesInHistorical { get; set; } = false; // enable bar sample recording during historical playback

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "BarSampleDelayMs", Order = 5, GroupName = "Database/Dashboard", Description = "Milliseconds delay between bar sample requests during historical playback to prevent server overload (0 = no delay, 50 = recommended).")]
        public int BarSampleDelayMs { get; set; } = 50; // delay in milliseconds between requests during historical

        [NinjaScriptProperty]
        [Range(0, 60)]
        [Display(Name = "StateUpdateIntervalSeconds", Order = 4, GroupName = "Database/Dashboard", Description = "How often to post state updates (0 = bar close only, 1-60 = update every N seconds on each tick). Lower values = more responsive but more server load.")]
        public int StateUpdateIntervalSeconds { get; set; } = 0; // 0 = bar close only (default)

        [NinjaScriptProperty]
        [Display(Name = "StateUpdateOnPriceChange", Order = 5, GroupName = "Database/Dashboard", Description = "If true, post state update whenever the price changes (efficient, only updates when needed).")]
        public bool StateUpdateOnPriceChange { get; set; } = false; // update on price change

        [NinjaScriptProperty]
        [Display(Name = "SkipShortsAboveGradient", Order = 5, GroupName = "Fast EMA/Gradient")]
        public double SkipShortsAboveGradient { get; set; } = -10.0; // skip shorts when EMA gradient > this degrees

        [NinjaScriptProperty]
        [Display(Name = "SkipLongsBelowGradient", Order = 6, GroupName = "Fast EMA/Gradient")]
        public double SkipLongsBelowGradient { get; set; } = 10.0; // skip longs when EMA gradient < this degrees

        [NinjaScriptProperty]
        [Display(Name = "AllowMidBarGradientEntry", Order = 7, GroupName = "Fast EMA/Gradient")]
        public bool AllowMidBarGradientEntry { get; set; } = false; // allow entry mid-bar when gradient crosses threshold

        [NinjaScriptProperty]
        [Display(Name = "AllowMidBarGradientExit", Order = 8, GroupName = "Fast EMA/Gradient")]
        public bool AllowMidBarGradientExit { get; set; } = false;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA 21 Period", Order = 9, GroupName = "Fast EMA/Gradient", Description = "Period for the third EMA line plotted on chart (default 21).")]
        public int Ema21Period { get; set; } = 21;

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
        [Range(0, 50)]
        [Display(Name = "MfeProtectTrigger", Order = 85, GroupName = "Stop Loss", Description = "Peak MFE (points) to arm profit floor and skip gradient give-back exits. Uses bar high/low. 0 = use BreakEvenTrigger.")]
        public int MfeProtectTrigger { get; set; } = 6;

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "BreakEvenOffset", Order = 9, GroupName = "Stop Loss", Description = "Points above entry to place break-even stop (e.g., 2 = stop at entry+2 points for small profit lock).")]
        public int BreakEvenOffset { get; set; } = 2; // points above entry for break-even stop

        [NinjaScriptProperty]
        [Display(Name = "Use Profit Target", Order = 90, GroupName = "Stop Loss", Description = "When enabled, place a profit target (take-profit) order in points. Use this if you expect the strategy to exit after a fixed profit like +40 points.")]
        public bool UseProfitTarget { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Profit Target (Points)", Order = 91, GroupName = "Stop Loss", Description = "Profit target distance in points. 0 disables the profit target even if Use Profit Target is enabled.")]
        public double ProfitTargetPoints { get; set; } = 40;

        [NinjaScriptProperty]
        [Range(0.5, 3.0)]
        [Display(Name = "Stop Loss Multiplier", Order = 10, GroupName = "Stop Loss", Description = "Multiply the computed stop loss by this value. 1.0 = default, 1.5 = 50% wider (gives trades more room). Use if stops are hit too often.")]
        public double StopLossMultiplier { get; set; } = 1.0;

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Min Stop Loss Points", Order = 11, GroupName = "Stop Loss", Description = "Minimum stop distance in points. Stops will never be set below this (0 = use 1 point floor). Use to avoid overly tight dynamic/volume-aware stops.")]
        public double MinStopLossPoints { get; set; } = 0; // 0 = use default 4-tick minimum

        [NinjaScriptProperty]
        [Display(Name = "UseEmaTrailingStop", Order = 12, GroupName = "Stop Loss", Description = "When enabled, stop loss follows Fast EMA value. For longs: stop = Fast EMA (only moves up). For shorts: stop = Fast EMA (only moves down). When enabled, this OVERRIDES UseTrailingStop, UseDynamicStopLoss, StopLossPoints, and UseBreakEven.")]
        public bool UseEmaTrailingStop { get; set; } = false; // use Fast EMA as trailing stop loss

        /// <summary>
        /// Enum for EMA stop loss trigger mode
        /// </summary>
        public enum EmaStopTriggerModeType
        {
            FullCandle,  // Entire candle (High to Low) must be below/above stop
            BodyOnly,    // Only the candle body (Open to Close, excluding wicks) must be below/above stop
            CloseOnly    // Only the close price needs to be below/above stop
        }

        [NinjaScriptProperty]
        [Display(Name = "EmaStopTriggerMode", Order = 11, GroupName = "Stop Loss", Description = "How the EMA trailing stop triggers: FullCandle = entire candle range (High to Low) must be below/above stop, BodyOnly = only the candle body (Open to Close, excluding wicks) must be below/above stop, CloseOnly = only the close price needs to be below/above stop. Checked on bar close. NOTE: This parameter only applies when UseEmaTrailingStop is enabled. If UseEmaTrailingStop is disabled, this setting is ignored.")]
        public EmaStopTriggerModeType EmaStopTriggerMode { get; set; } = EmaStopTriggerModeType.BodyOnly; // default to body-only for less noisy exits. Only used when UseEmaTrailingStop is enabled.

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Profit Protection Points", Order = 12, GroupName = "Stop Loss", Description = "When in profit by this many points, allow early exit on close crossing EMA (even if FullCandle/BodyOnly hasn't triggered). 0 = disabled. Helps preserve profits by exiting earlier when trade is profitable. Only applies when EmaStopTriggerMode is FullCandle or BodyOnly.")]
        public double EmaStopProfitProtectionPoints { get; set; } = 0; // Profit threshold to enable early exit on close crossing EMA

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "EMA Stop Min Distance From Entry", Order = 13, GroupName = "Stop Loss", Description = "Minimum distance (in points) the EMA trailing stop must maintain from entry price. Prevents premature exits when EMA is very close to entry. 0 = no minimum (stop can be at entry). Only applies when UseEmaTrailingStop is enabled.")]
        public double EmaStopMinDistanceFromEntry { get; set; } = 0; // Minimum distance from entry for EMA stop

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "EMA Stop Activation Delay Bars", Order = 14, GroupName = "Stop Loss", Description = "Number of bars to wait after entry before EMA trailing stop becomes active. Prevents immediate exits right after entry. 0 = active immediately. Only applies when UseEmaTrailingStop is enabled.")]
        public int EmaStopActivationDelayBars { get; set; } = 0; // Bars to wait before EMA stop activates

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "EMA Stop Min Profit Before Activation", Order = 15, GroupName = "Stop Loss", Description = "Minimum profit (in points) required before EMA trailing stop becomes active. Prevents exits while trade is near breakeven. 0 = no minimum profit required. Only applies when UseEmaTrailingStop is enabled.")]
        public double EmaStopMinProfitBeforeActivation { get; set; } = 0; // Minimum profit before EMA stop activates

        [NinjaScriptProperty]
        [Display(Name = "Reverse On EMA Stop", Order = 16, GroupName = "Stop Loss", Description = "When enabled, reverse position when EMA stop triggers (long->short, short->long). Requires EnableShorts for short entries.")]
        public bool ReverseOnEmaStop { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "UseGradientStopLoss", Order = 17, GroupName = "Stop Loss", Description = "When enabled, exit position if gradient crosses into unfavorable territory. For longs: exit when gradient drops below ExitLongBelowGradient. For shorts: exit when gradient rises above ExitShortAboveGradient.")]
        public bool UseGradientStopLoss { get; set; } = false; // exit based on gradient crossing threshold

        [NinjaScriptProperty]
        [Range(-90, 90)]
        [Display(Name = "ExitLongBelowGradient", Order = 18, GroupName = "Stop Loss", Description = "Exit LONG position when gradient drops below this value (degrees). E.g., 0 = exit when gradient becomes negative, -10 = exit when gradient < -10°.")]
        public double ExitLongBelowGradient { get; set; } = 0; // exit long when gradient < this value

        [NinjaScriptProperty]
        [Range(-90, 90)]
        [Display(Name = "ExitShortAboveGradient", Order = 19, GroupName = "Stop Loss", Description = "Exit SHORT position when gradient rises above this value (degrees). E.g., 0 = exit when gradient becomes positive, 10 = exit when gradient > 10°.")]
        public double ExitShortAboveGradient { get; set; } = 0; // exit short when gradient > this value

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "GradientStopMinBarsDelay", Order = 20, GroupName = "Stop Loss", Description = "Minimum bars a trade must be held before the gradient stop can trigger. Prevents premature exits during initial volatility after entry. 0 = no delay (legacy behavior). Recommended: 5.")]
        public int GradientStopMinBarsDelay { get; set; } = 5;

        [NinjaScriptProperty]
        [Display(Name = "Enable Stop Loss Research", Order = 21, GroupName = "Stop Loss", Description = "When a trade closes with negative PnL, log a research row (avg bar size, ATR, MFE/MAE) to strategy_logs and suggest stop improvements to prevent mid-trade stops.")]
        public bool EnableStopLossResearch { get; set; } = true;

        [NinjaScriptProperty]
        [Range(5, 50)]
        [Display(Name = "Stop Loss Research Avg Bar Lookback", Order = 21, GroupName = "Stop Loss", Description = "Bars used to compute average bar size at entry for stop loss research. Default 20.")]
        public int StopLossResearchAvgBarLookback { get; set; } = 20;

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss Research Live Adjust", Order = 22, GroupName = "Stop Loss", Description = "When a trade closes with negative PnL, automatically adjust MinStopLossPoints and/or StopLossMultiplier (within caps) so next trades get wider stops if research suggests it. Works in addition to CSV logging.")]
        public bool EnableStopLossResearchLiveAdjust { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Live Adjust Max MinStopLossPoints", Order = 23, GroupName = "Stop Loss", Description = "Cap for auto-adjusted MinStopLossPoints (prevents runaway increases). Default 50.")]
        public int StopLossResearchLiveAdjustMaxMinPoints { get; set; } = 50;

        [NinjaScriptProperty]
        [Range(1.0, 3.0)]
        [Display(Name = "Live Adjust Max StopLossMultiplier", Order = 24, GroupName = "Stop Loss", Description = "Cap for auto-adjusted StopLossMultiplier. Default 2.0.")]
        public double StopLossResearchLiveAdjustMaxMultiplier { get; set; } = 2.0;

        [NinjaScriptProperty]
        [Display(Name = "EnableOpportunityLog", Order = 1, GroupName = "Logging/JSON")]
        public bool EnableOpportunityLog { get; set; } = true; // log every bar with opportunity analysis

        [NinjaScriptProperty]
        [Display(Name = "Enable Traffic Log", Order = 2, GroupName = "Logging/JSON", Description = "Log one row per bar: Date, Hour, DayOfWeek, Instrument, Volume, BarMinutes, VolumePerMinute, TicksInBar, TicksPerMinute. Use the CSV to build tables and predict traffic (high vs low activity) by hour and day of week. Efficient: one row per bar close, no tick spam.")]
        public bool EnableTrafficLog { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Enable Order Audit Log", Order = 2, GroupName = "Logging/JSON", Description = "When enabled, logs every order update (including order names, signals, IDs) to help trace unexpected orders.")]
        public bool EnableOrderAuditLog { get; set; } = false;

        private readonly Queue<bool> recentGood = new Queue<bool>(10);
        private readonly Queue<double> recentPnl = new Queue<double>(10);

        private double lastFastEmaSlope = double.NaN;
        private DateTime lastStateUpdateTime = DateTime.MinValue; // for throttling state updates
        private double lastStateUpdatePrice = double.NaN; // for price-change updates
        private DateTime strategyRunStartTime = DateTime.MinValue; // when this run started (DataLoaded); used for "running since" in state/dashboard

        // Optional bar index labels (copied from BareOhlcLogger)
        private bool showBarIndexLabels = true;
        private bool showFastGradLabels = true;
        private int lastTradingTrafficStatusLabelBar = -1;
        private const string tradingTrafficStatusLabelTag = "BOTF_TradingTrafficStatus";

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

        // Stop loss research (on losing trades)
        private StreamWriter stopLossResearchWriter;
        private string stopLossResearchPath;
        private bool stopLossResearchInitialized;

        // Traffic log (volume per minute by hour/day for predicting activity)
        private StreamWriter trafficLogWriter;
        private string trafficLogPath;
        private bool trafficLogInitialized;
        
        // Tick-traffic tracking (per bar). Used for traffic log + bar_samples payload.
        private int ticksInCurrentBar;
        private int lastClosedBarIndex = -1;
        private int lastClosedBarTicks;
        private double lastClosedBarMinutes = double.NaN;
        private double lastClosedBarTicksPerMinute = double.NaN;
        private readonly Queue<double> recentTicksPerMinute = new Queue<double>(64);

        private void UpdateTickTrafficCounters()
        {
            // Called on every OnBarUpdate tick (BIP 0). Maintains per-bar tick counts.
            if (CurrentBar < 0)
                return;

            if (IsFirstTickOfBar)
            {
                // We are at the first tick of a NEW bar; finalize the bar that just closed.
                lastClosedBarIndex = CurrentBar - 1;
                lastClosedBarTicks = ticksInCurrentBar <= 0 ? 1 : ticksInCurrentBar;

                if (CurrentBar >= 2)
                {
                    double mins = (Time[1] - Time[2]).TotalMinutes;
                    lastClosedBarMinutes = mins > 0 ? mins : double.NaN;
                }
                else
                {
                    lastClosedBarMinutes = double.NaN;
                }

                lastClosedBarTicksPerMinute =
                    (!double.IsNaN(lastClosedBarMinutes) && lastClosedBarMinutes > 0)
                        ? (lastClosedBarTicks / lastClosedBarMinutes)
                        : double.NaN;

                if (!double.IsNaN(lastClosedBarTicksPerMinute) && lastClosedBarTicksPerMinute > 0)
                {
                    recentTicksPerMinute.Enqueue(lastClosedBarTicksPerMinute);
                    while (recentTicksPerMinute.Count > 50)
                        recentTicksPerMinute.Dequeue();
                }

                // Start counting ticks for the new bar (include this tick).
                ticksInCurrentBar = 1;
            }
            else
            {
                // Mid-bar: increment tick count.
                if (ticksInCurrentBar <= 0)
                    ticksInCurrentBar = 1;
                else
                    ticksInCurrentBar++;
            }
        }

        // Deferred execution logs waiting for bar-close OHLC
        private readonly List<PendingLogEntry> pendingLogs = new List<PendingLogEntry>();

        // Fast EMA for context
        private EMA fastEma;
        
        // EMA crossover filter indicators
        private EMA emaFast;
        private EMA emaSlow;
        private EMA ema21;

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
        private bool profitFloorModeActive = false; // once armed: never SetTrailStop for this entry signal until flat
        private double profitFloorStopPrice = double.NaN;
        private string profitFloorEntrySignal = null;
        private int lastProfitFloorReinforceLogBar = -1;
        private double breakEvenEntryPrice = double.NaN;

        // Trade tracking for dashboard logging
        private int currentTradeEntryBar = -1;
        private double currentTradeEntryPrice = double.NaN;
        private DateTime currentTradeEntryTime = DateTime.MinValue;
        private MarketPosition currentTradeDirection = MarketPosition.Flat;
        private int currentTradeContracts = 0;
        private double currentTradeMFE = 0.0;  // Maximum Favorable Excursion
        private double currentTradeMfePoints = 0.0; // Peak favorable excursion in points (bar high/low)
        private double currentTradeMAE = 0.0;   // Maximum Adverse Excursion
        private double currentTradeStopLossPoints = 0.0; // Track the stop loss distance that was set
        private string currentTradeEntryReason = ""; // Track all reasons/filters that applied when entering the trade
        private string currentTradeExitReason = ""; // Track exit reason when trade exits
        private int lastExitBarIndex = -1; // Track which bar the exit happened on
        private MarketPosition previousPosition = MarketPosition.Flat;
        private int lastPositionQuantity = int.MinValue; // Track last position size for state refresh
        private DateTime lastMismatchLogTime = DateTime.MinValue; // Throttle mismatch diagnostics
        private int exitTraceBatchId = 0;
        private int exitTraceBatchBar = -1;
        private int exitTraceSeq = 0;
        private int exitOverlapBar = -1;
        private readonly HashSet<string> exitOverlapReasons = new HashSet<string>();
        private bool exitOverlapLogged;
        private int lastExitSubmitBar = -1;
        private MarketPosition lastExitSubmitSide = MarketPosition.Flat;
        private int entrySignalSeq = 0;
        private string activeEntrySignal;

        private string AllocateEntrySignal(MarketPosition direction)
        {
            entrySignalSeq++;
            activeEntrySignal = direction == MarketPosition.Long
                ? $"BarsOnTheFlowLong_{entrySignalSeq}"
                : $"BarsOnTheFlowShort_{entrySignalSeq}";
            return activeEntrySignal;
        }

        private string GetActiveEntrySignal()
        {
            if (!string.IsNullOrEmpty(activeEntrySignal))
                return activeEntrySignal;
            return Position.MarketPosition == MarketPosition.Long ? "BarsOnTheFlowLong" : "BarsOnTheFlowShort";
        }

        private static bool IsBarsOnTheFlowEntrySignalName(string name)
        {
            return !string.IsNullOrEmpty(name)
                && (name.StartsWith("BarsOnTheFlowLong", StringComparison.Ordinal)
                    || name.StartsWith("BarsOnTheFlowShort", StringComparison.Ordinal));
        }

        private void SubmitLongEntry()
        {
            string signal = AllocateEntrySignal(MarketPosition.Long);
            SetInitialStopLoss(signal, MarketPosition.Long);
            EnterLong(Math.Max(1, Contracts), signal);
        }

        private void SubmitShortEntry()
        {
            string signal = AllocateEntrySignal(MarketPosition.Short);
            SetInitialStopLoss(signal, MarketPosition.Short);
            EnterShort(Math.Max(1, Contracts), signal);
        }

        // Reset exit reason when a new entry occurs
        private void ResetMfeProtectionState(bool clearActiveEntrySignal = false)
        {
            breakEvenActivated = false;
            profitFloorModeActive = false;
            breakEvenEntryPrice = double.NaN;
            currentTradeMfePoints = 0.0;
            profitFloorStopPrice = double.NaN;
            profitFloorEntrySignal = null;
            lastProfitFloorReinforceLogBar = -1;
            if (clearActiveEntrySignal)
                activeEntrySignal = null;
        }

        /// <summary>
        /// Arm fixed profit floor for this entry signal.
        /// Trail logic may continue, but effective stop is always clamped to this floor.
        /// </summary>
        private void ApplyProfitFloorStop(string orderName, double floorPrice)
        {
            profitFloorStopPrice = Instrument.MasterInstrument.RoundToTickSize(floorPrice);
            profitFloorEntrySignal = orderName;
            profitFloorModeActive = true;

            SetProfitFloorClampedStop(orderName, profitFloorStopPrice, "floor-arm");

            Print($"[MFE_FLOOR_LOCK] Bar {CurrentBar}: Profit floor armed for {orderName} @ {profitFloorStopPrice:F2} (hybrid clamp mode)");
            Print($"[MFE_FLOOR_STOP] Bar {CurrentBar}: Fixed minimum floor @ {profitFloorStopPrice:F2} on {orderName} (mode=FloorClamp)");
        }

        private bool IsProfitFloorActive()
        {
            return (breakEvenActivated || profitFloorModeActive)
                && !double.IsNaN(profitFloorStopPrice)
                && !UseEmaTrailingStop;
        }

        private bool IsProfitFloorActiveForOrder(string orderName)
        {
            if (!IsProfitFloorActive())
                return false;
            if (string.IsNullOrEmpty(profitFloorEntrySignal))
                return true;
            return string.Equals(orderName, profitFloorEntrySignal, StringComparison.Ordinal);
        }

        private bool ShouldAllowTrailStop(string orderName)
        {
            return UseTrailingStop;
        }

        private static bool IsTrailStopOrder(Order order)
        {
            if (order == null)
                return false;
            string name = order.Name ?? string.Empty;
            return name.IndexOf("Trail", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsWorkingStopOrder(Order order)
        {
            if (order == null)
                return false;
            if (order.OrderState != OrderState.Working
                && order.OrderState != OrderState.Accepted
                && order.OrderState != OrderState.PartFilled)
                return false;
            return order.OrderType == OrderType.StopMarket || order.OrderType == OrderType.StopLimit;
        }

        private List<Order> FindAllWorkingStopOrders(string orderName)
        {
            var stops = new List<Order>();
            if (Account == null || string.IsNullOrEmpty(orderName))
                return stops;

            foreach (Order order in Account.Orders)
            {
                if (order == null || order.Instrument != Instrument)
                    continue;
                if (!string.Equals(order.FromEntrySignal, orderName, StringComparison.Ordinal))
                    continue;
                if (!IsWorkingStopOrder(order))
                    continue;
                stops.Add(order);
            }

            return stops;
        }

        private int CancelWorkingTrailStopsForSignal(string orderName)
        {
            int cancelled = 0;
            foreach (Order order in FindAllWorkingStopOrders(orderName))
            {
                if (!IsTrailStopOrder(order))
                    continue;
                try
                {
                    CancelOrder(order);
                    cancelled++;
                }
                catch (Exception ex)
                {
                    Print($"[MFE_FLOOR_LOCK] Bar {CurrentBar}: Cancel trail failed on {order.Name}: {ex.Message}");
                }
            }
            return cancelled;
        }

        private int CancelNonCompliantOrTrailStopsForSignal(string orderName, double floorPrice)
        {
            int cancelled = 0;
            foreach (Order order in FindAllWorkingStopOrders(orderName))
            {
                bool isTrail = IsTrailStopOrder(order);
                bool isCompliant = IsProfitFloorStopCompliant(order.StopPrice, floorPrice);
                if (!isTrail && isCompliant)
                    continue;
                try
                {
                    CancelOrder(order);
                    cancelled++;
                }
                catch (Exception ex)
                {
                    Print($"[MFE_FLOOR_ENFORCE] Bar {CurrentBar}: Cancel stop failed on {order.Name}: {ex.Message}");
                }
            }
            return cancelled;
        }

        private void ApplyFixedProfitFloorStop(string orderName, double floorPrice)
        {
            SetStopLoss(orderName, CalculationMode.Price, floorPrice, false);
            ReapplyProfitTargetIfConfigured(orderName);
        }

        private double ClampStopToFloor(double candidateStopPrice, double floorPrice)
        {
            if (Position.MarketPosition == MarketPosition.Long)
                return Math.Max(candidateStopPrice, floorPrice);
            if (Position.MarketPosition == MarketPosition.Short)
                return Math.Min(candidateStopPrice, floorPrice);
            return floorPrice;
        }

        private double ComputeTrailCandidateStopPriceFromTicks(int stopLossTicks)
        {
            double tickSize = Instrument.MasterInstrument.TickSize;
            if (tickSize <= 0)
                tickSize = 0.25;

            int safeTicks = Math.Max(1, stopLossTicks);
            double distance = safeTicks * tickSize;
            double fallback = Close[0];

            if (Position.MarketPosition == MarketPosition.Long)
            {
                double bid = 0;
                try { bid = GetCurrentBid(); } catch { }
                double marketRef = bid > 0 ? bid : fallback;
                return Instrument.MasterInstrument.RoundToTickSize(marketRef - distance);
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                double ask = 0;
                try { ask = GetCurrentAsk(); } catch { }
                double marketRef = ask > 0 ? ask : fallback;
                return Instrument.MasterInstrument.RoundToTickSize(marketRef + distance);
            }

            return double.NaN;
        }

        private void SetProfitFloorClampedStop(string orderName, double floorPrice, string reason)
        {
            double floor = Instrument.MasterInstrument.RoundToTickSize(floorPrice);
            int stopLossTicks = CalculateStopLossTicks();
            int ticksPerPoint = GetTicksPerPoint();
            if (stopLossTicks <= 0)
                stopLossTicks = Math.Max(4, ticksPerPoint > 0 ? ticksPerPoint : 4);

            double trailCandidate = ComputeTrailCandidateStopPriceFromTicks(stopLossTicks);
            if (double.IsNaN(trailCandidate))
                trailCandidate = floor;

            double effectiveStop = Instrument.MasterInstrument.RoundToTickSize(ClampStopToFloor(trailCandidate, floor));
            WorkingStopSnapshot before = CaptureWorkingStopSnapshot(orderName, floor);

            bool hasWorking = before != null && before.Found;
            bool workingCompliant = hasWorking && before.IsCompliant;
            bool atEffective = hasWorking && !double.IsNaN(before.StopPrice)
                && Math.Abs(before.StopPrice - effectiveStop) <= (Instrument.MasterInstrument.TickSize > 0 ? Instrument.MasterInstrument.TickSize * 0.5 : 0.125);

            if (!hasWorking || !workingCompliant || !atEffective)
                ApplyFixedProfitFloorStop(orderName, effectiveStop);

            Print($"[MFE_FLOOR_TRAIL_CLAMP] Bar {CurrentBar}: signal={orderName}, reason={reason}, floor={floor:F2}, trailCandidate={trailCandidate:F2}, effectiveStop={effectiveStop:F2}, hadWorking={hasWorking}, compliant={workingCompliant}, atEffective={atEffective}");
        }

        private sealed class WorkingStopSnapshot
        {
            public bool Found;
            public string Name;
            public string Type;
            public double StopPrice;
            public OrderState State;
            public bool IsTrailing;
            public bool IsCompliant;
        }

        private Order FindWorkingStopOrder(string orderName)
        {
            if (Account == null || string.IsNullOrEmpty(orderName))
                return null;

            Order best = null;
            foreach (Order order in Account.Orders)
            {
                if (order == null || order.Instrument != Instrument)
                    continue;
                if (!string.Equals(order.FromEntrySignal, orderName, StringComparison.Ordinal))
                    continue;
                if (!IsWorkingStopOrder(order))
                    continue;

                if (best == null)
                {
                    best = order;
                    continue;
                }

                bool candidateIsTrail = IsTrailStopOrder(order);
                bool currentIsTrail = IsTrailStopOrder(best);
                if (currentIsTrail && !candidateIsTrail)
                {
                    best = order;
                    continue;
                }

                bool candidateIsWorking = order.OrderState == OrderState.Working;
                bool currentIsWorking = best.OrderState == OrderState.Working;
                if (candidateIsWorking && !currentIsWorking)
                    best = order;
            }

            return best;
        }

        private bool IsProfitFloorStopCompliant(double stopPrice, double floorPrice)
        {
            if (stopPrice <= 0 || double.IsNaN(floorPrice))
                return false;

            double tickSize = Instrument.MasterInstrument.TickSize;
            if (tickSize <= 0)
                tickSize = 0.25;
            double tolerance = tickSize * 0.5;
            bool isLong = Position.MarketPosition == MarketPosition.Long;
            bool isShort = Position.MarketPosition == MarketPosition.Short;
            return (isLong && stopPrice >= floorPrice - tolerance)
                || (isShort && stopPrice <= floorPrice + tolerance);
        }

        private WorkingStopSnapshot CaptureWorkingStopSnapshot(string orderName, double floorPrice)
        {
            var snapshot = new WorkingStopSnapshot
            {
                Found = false,
                Name = "none",
                Type = "none",
                StopPrice = double.NaN,
                State = default(OrderState),
                IsTrailing = false,
                IsCompliant = false
            };

            Order best = FindWorkingStopOrder(orderName);
            if (best == null)
                return snapshot;

            snapshot.Found = true;
            snapshot.Name = best.Name ?? "null";
            snapshot.Type = best.OrderType.ToString();
            snapshot.StopPrice = best.StopPrice;
            snapshot.State = best.OrderState;
            snapshot.IsTrailing = IsTrailStopOrder(best);
            snapshot.IsCompliant = IsProfitFloorStopCompliant(best.StopPrice, floorPrice);
            return snapshot;
        }

        private bool HasWorkingProfitTarget(string orderName)
        {
            if (Account == null || string.IsNullOrEmpty(orderName))
                return false;

            foreach (Order order in Account.Orders)
            {
                if (order == null || order.Instrument != Instrument)
                    continue;
                if (!string.Equals(order.FromEntrySignal, orderName, StringComparison.Ordinal))
                    continue;
                if (order.OrderType != OrderType.Limit)
                    continue;
                if (order.OrderState == OrderState.Working
                    || order.OrderState == OrderState.Accepted
                    || order.OrderState == OrderState.PartFilled)
                    return true;
            }

            return false;
        }

        private void ReapplyProfitTargetIfConfigured(string orderName)
        {
            if (!UseProfitTarget || ProfitTargetPoints <= 0 || HasWorkingProfitTarget(orderName))
                return;

            int ticksPerPoint = GetTicksPerPoint();
            int tpp = ticksPerPoint > 0 ? ticksPerPoint : 4;
            double targetTicks = ProfitTargetPoints * tpp;
            SetProfitTarget(orderName, CalculationMode.Ticks, targetTicks);
        }

        private string FormatWorkingStopSnapshot(WorkingStopSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Found)
                return "workingStop=none";
            return $"workingStop={snapshot.Name}/{snapshot.Type} state={snapshot.State} price={snapshot.StopPrice:F2} trailing={snapshot.IsTrailing} compliant={snapshot.IsCompliant}";
        }

        /// <summary>
        /// While profit floor is active, keep stop aligned to trailing candidate but never worse than floor.
        /// </summary>
        private void ReinforceProfitFloorStopIfNeeded()
        {
            if (!IsProfitFloorActive() || Position.MarketPosition == MarketPosition.Flat)
                return;

            string orderName = GetActiveEntrySignal();
            if (!IsProfitFloorActiveForOrder(orderName))
                return;

            double floor = Instrument.MasterInstrument.RoundToTickSize(profitFloorStopPrice);
            SetProfitFloorClampedStop(orderName, floor, "reinforce-calc");
            WorkingStopSnapshot before = CaptureWorkingStopSnapshot(orderName, floor);
            bool needEnforce = !before.Found || !before.IsCompliant;
            if (!needEnforce)
                return;

            int cancelled = CancelNonCompliantOrTrailStopsForSignal(orderName, floor);
            SetProfitFloorClampedStop(orderName, floor, "reinforce-enforce");
            WorkingStopSnapshot after = CaptureWorkingStopSnapshot(orderName, floor);

            bool enforceFailed = !after.Found || !after.IsCompliant;
            string action = cancelled > 0 ? $"cancel({cancelled})+clampedStop" : "clampedStop";

            if (enforceFailed)
            {
                cancelled = CancelNonCompliantOrTrailStopsForSignal(orderName, floor);
                SetProfitFloorClampedStop(orderName, floor, "reinforce-retry");
                after = CaptureWorkingStopSnapshot(orderName, floor);
                enforceFailed = !after.Found || !after.IsCompliant;
                action = cancelled > 0 ? $"retryCancel({cancelled})+clampedStop" : "retryClampedStop";
            }

            bool shouldLog = enforceFailed || lastProfitFloorReinforceLogBar != CurrentBar;
            if (shouldLog)
            {
                if (!enforceFailed)
                    lastProfitFloorReinforceLogBar = CurrentBar;
                Print($"[MFE_FLOOR_ENFORCE] Bar {CurrentBar}: signal={orderName}, floor={floor:F2}, action={action}, failed={enforceFailed}, Before={FormatWorkingStopSnapshot(before)}; After={FormatWorkingStopSnapshot(after)}");
            }
        }

        /// <summary>
        /// Overwrite NinjaTrader's remembered stop for an entry signal (ticks/trail only — while in position).
        /// Required after MFE floor used SetStopLoss(Price) on a prior trade with the same signal name.
        /// </summary>
        private void RefreshManagedStopOnly(string orderName)
        {
            if (IsProfitFloorActiveForOrder(orderName))
            {
                ReinforceProfitFloorStopIfNeeded();
                return;
            }

            int stopLossTicks = CalculateStopLossTicks();
            int ticksPerPoint = GetTicksPerPoint();
            double stopLossPoints = (ticksPerPoint > 0) ? (stopLossTicks / (double)ticksPerPoint) : (stopLossTicks / 4.0);

            if (stopLossTicks <= 0)
            {
                stopLossTicks = Math.Max(4, ticksPerPoint);
                stopLossPoints = (ticksPerPoint > 0) ? (stopLossTicks / (double)ticksPerPoint) : 1.0;
            }

            currentTradeStopLossPoints = stopLossPoints;

            if (IsProfitFloorActiveForOrder(orderName))
                SetProfitFloorClampedStop(orderName, profitFloorStopPrice, "refresh");
            else if (ShouldAllowTrailStop(orderName))
                SetTrailStop(orderName, CalculationMode.Ticks, stopLossTicks, false);
            else
                SetStopLoss(orderName, CalculationMode.Ticks, stopLossTicks, false);
        }


        private void ResetExitReason()
        {
            currentTradeExitReason = "";
            lastExitBarIndex = -1;
        }

        private int GetMfeProtectTriggerPoints()
        {
            return MfeProtectTrigger > 0 ? MfeProtectTrigger : BreakEvenTrigger;
        }

        /// <summary>Update peak favorable excursion using current bar high/low (points).</summary>
        private void UpdateTradeMfePointsFromBar()
        {
            if (Position.MarketPosition == MarketPosition.Flat || double.IsNaN(Position.AveragePrice))
                return;

            double entry = Position.AveragePrice;
            double barFavorable = Position.MarketPosition == MarketPosition.Long
                ? High[0] - entry
                : entry - Low[0];
            if (barFavorable > currentTradeMfePoints)
                currentTradeMfePoints = barFavorable;
        }

        /// <summary>When peak MFE reached the protect trigger, gradient stop must not give back open profit.</summary>
        private bool ShouldSkipGradientStopForMfeProtection()
        {
            if (!UseBreakEven || !UseGradientStopLoss || Position.MarketPosition == MarketPosition.Flat)
                return false;

            UpdateTradeMfePointsFromBar();
            int trigger = GetMfeProtectTriggerPoints();
            if (currentTradeMfePoints < trigger)
                return false;

            double currentProfit = Position.GetUnrealizedProfitLoss(PerformanceUnit.Points);
            Print($"[GRADIENT_STOP_MFE_SKIP] Bar {CurrentBar}: Skipping gradient exit - peak MFE={currentTradeMfePoints:F2} >= {trigger}, currentProfit={currentProfit:F2}, profitFloorActive={breakEvenActivated}");
            return true;
        }

        private string NextExitTraceId()
        {
            if (CurrentBar != exitTraceBatchBar)
            {
                exitTraceBatchBar = CurrentBar;
                exitTraceBatchId++;
                exitTraceSeq = 0;
            }
            exitTraceSeq++;
            return $"{exitTraceBatchId}.{exitTraceSeq}";
        }

        private void LogExitTrace(string exitName, string fromEntrySignal, string reason, MarketPosition side)
        {
            TrackExitOverlap($"{side}:{exitName}");
            string traceId = NextExitTraceId();
            Print($"[EXIT_TRACE] Bar {CurrentBar}: Trace={traceId}, Side={side}, ExitName={exitName}, FromEntry={fromEntrySignal}, Reason={reason}");
        }

        private void TrackExitOverlap(string reason)
        {
            if (CurrentBar != exitOverlapBar)
            {
                exitOverlapBar = CurrentBar;
                exitOverlapReasons.Clear();
                exitOverlapLogged = false;
            }

            if (exitOverlapReasons.Add(reason) && exitOverlapReasons.Count > 1 && !exitOverlapLogged)
            {
                exitOverlapLogged = true;
                Print($"[EXIT_OVERLAP] Bar {CurrentBar}: Multiple exits triggered. Reasons={string.Join("; ", exitOverlapReasons)}");
            }
        }

        private bool TrySubmitExit(MarketPosition side, string exitName, string fromEntrySignal, string reason)
        {
            if (lastExitSubmitBar == CurrentBar && lastExitSubmitSide == side)
            {
                Print($"[EXIT_SKIP] Bar {CurrentBar}: Skipping additional {side} exit. ExitName={exitName}, Reason={reason}, AlreadySubmittedSide={lastExitSubmitSide}");
                return false;
            }

            lastExitSubmitBar = CurrentBar;
            lastExitSubmitSide = side;
            LogExitTrace(exitName, fromEntrySignal, reason, side);
            if (side == MarketPosition.Long)
                ExitLong(exitName, fromEntrySignal);
            else if (side == MarketPosition.Short)
                ExitShort(exitName, fromEntrySignal);
            return true;
        }

        private void ExitLongTrace(string exitName, string fromEntrySignal, string reason)
        {
            TrySubmitExit(MarketPosition.Long, exitName, fromEntrySignal, reason);
        }

        private void ExitShortTrace(string exitName, string fromEntrySignal, string reason)
        {
            TrySubmitExit(MarketPosition.Short, exitName, fromEntrySignal, reason);
        }

        private double lastFastEmaGradDeg = double.NaN;
        private double lastChartScaledGradDeg = double.NaN; // Visual ("eye") angle for the last completed bar
        private double lastRegressionGradDeg = double.NaN;  // atan(slope) angle for the last completed bar
        private Dictionary<int, double> gradientByBar = new Dictionary<int, double>(); // Store gradient per bar for accurate logging
        private Dictionary<int, double> chartGradientByBar = new Dictionary<int, double>(); // Visual chart-scaled angle per bar (for label comparison)
        private static readonly System.Threading.SemaphoreSlim dashboardPostSemaphore = new System.Threading.SemaphoreSlim(4, 4);
        private ATR atr;

        // Bar navigation panel
        private System.Windows.Controls.Grid barNavPanel;
        private System.Windows.Controls.TextBox barNavTextBox;
        private System.Windows.Controls.Button barNavButton;
        private bool barNavPanelDragging;
        private System.Windows.Point barNavPanelDragStart;
        private System.Windows.Thickness barNavPanelMarginAtDragStart;
        private static System.Windows.Thickness? barNavPanelSavedMargin;

        // EMA chart legend overlay
        private System.Windows.Controls.Border emaLegendPanel;
        private System.Windows.Controls.TextBlock emaLegendFastLabel;
        private System.Windows.Controls.TextBlock emaLegendSlowLabel;
        private System.Windows.Controls.TextBlock emaLegend21Label;
        private bool emaLegendPanelDragging;
        private System.Windows.Point emaLegendPanelDragStart;
        private System.Windows.Thickness emaLegendPanelMarginAtDragStart;
        private static System.Windows.Thickness? emaLegendPanelSavedMargin;
        
        // Stop loss controls
        private System.Windows.Controls.TextBox stopLossTextBox;
        private System.Windows.Controls.Button stopLossPlusButton;
        private System.Windows.Controls.Button stopLossMinusButton;
        
        // Panel controls
        private System.Windows.Controls.Button pauseTradingButton;
        private System.Windows.Controls.Button flattenButton;
        private bool pauseTradingFromPanel = true; // start paused; user resumes from the nav panel
        private System.Windows.Controls.TextBlock positionEntryLine;
        private System.Windows.Controls.TextBlock positionStopLine;
        private System.Windows.Controls.TextBlock positionTargetLine;
        private double lastPanelEntryPrice = double.NaN;
        private double lastPanelStopPrice = double.NaN;
        private double lastPanelTargetPrice = double.NaN;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                // Initialize cancellation token source
                cancellationTokenSource = new System.Threading.CancellationTokenSource();
                Name = "BarsOnTheFlow";
                Calculate = Calculate.OnEachTick; // evaluate at first tick of new bar using the completed bar
                IsOverlay = true; // draw labels and trend visuals on the price panel
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = false;
                ExitOnSessionCloseSeconds = 30;
                BarsRequiredToTrade = 4;
                lastPositionQuantity = int.MinValue;
                // Prevent NinjaTrader from automatically disabling the strategy on order errors
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;

                // Default to Extended Trading Hours behavior (CME index futures style: 18:00 -> 17:00 with 17:00-18:00 break)
                UseTradingHoursFilter = true;
                TradingStartHour = 18;
                TradingStartMinute = 0;
                TradingEndHour = 17;
                TradingEndMinute = 0;

                // Keep trades off during very low activity periods even in ETH
                UseTrafficFilter = true;
                MinTicksPerMinuteToTrade = 150;
                TrafficFilterLookbackBars = 3;

                // Take-profit so big moves don't give back everything
                UseProfitTarget = true;
                ProfitTargetPoints = 40;

                // Helpful by default so you can see what's blocking trading
                ShowTradingTrafficStatus = true;
                TradingTrafficStatusTopPaddingLines = 3;

                // Use chart-scaled (visual) gradient degrees for filters, exits, and labels
                UseChartScaledFastGradDeg = true;

                // Entry gating defaults: require EMA crossover + directional gradient + minimum EMA gaps
                UseEmaCrossoverFilter = true;
                EmaCrossoverRequireCrossover = true;
                EmaCrossoverMinTicksCloseToFast = 4;
                EmaCrossoverMinTicksFastToSlow = 4;
                GradientFilterEnabled = true;
                SkipLongsBelowGradient = 10;
                SkipShortsAboveGradient = -10;
            }
            else if (State == State.DataLoaded)
            {
                strategyRunStartTime = DateTime.Now; // Record run start so state/dashboard and tools can show "running since"
                emaFast = EMA(FastEmaPeriod);
                fastEma = emaFast; // Use the same Fast EMA instance for gradient/trailing/crossover
                
                // Initialize EMA crossover filter indicators (always initialize, filter is checked at runtime)
                emaSlow = EMA(EmaSlowPeriod);
                ema21 = EMA(Ema21Period);
                atr = ATR(AtrPeriod);

                // Draw EMAs on chart
                emaFast.Plots[0].Brush = Brushes.Blue;
                emaSlow.Plots[0].Brush = Brushes.DarkOrange;
                ema21.Plots[0].Brush = Brushes.MediumPurple;
                AddChartIndicator(emaFast);
                AddChartIndicator(emaSlow);
                AddChartIndicator(ema21);
                
                InitializeLog();
                
                // Clear tables on fresh run (called every time strategy starts/restarts)
                // This ensures clean data for each run - old data from previous runs is cleared
                // NOTE: Only clear on DataLoaded, NOT when transitioning to Realtime
                ClearTradesTable();
                ClearBarsTable();
                ClearBarSamples(); // Clear bar_samples table - ensures debugging fields start fresh
                
                // Export strategy state for API queries
                ExportStrategyState();
                
                // Initialize chart overlays on UI thread
                if (ChartControl != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() =>
                    {
                        CreateBarNavPanel();
                        CreateEmaLegendPanel();
                    });
                }
            }
            else if (State == State.Realtime)
            {
                // Send ALL accumulated historical bar data in ONE batch request
                // This avoids the problem of HTTP requests timing out during fast historical playback
                int historicalBarCount = 0;
                lock (historicalBarDataLock)
                {
                    historicalBarCount = historicalBarDataList.Count;
                }
                
                Print($"[REALTIME_TRANSITION] Transitioned to real-time mode");
                Print($"[REALTIME_TRANSITION] Accumulated {historicalBarCount} historical bars in memory");
                
                if (historicalBarCount > 0)
                {
                    Print($"[REALTIME_TRANSITION] Sending historical bar batch to server...");
                    SendHistoricalBarBatch();
                }
                else
                {
                    Print($"[REALTIME_TRANSITION] No historical bars to send");
                }
                
                Print($"[REALTIME_TRANSITION] Real-time bar recording will continue from here");
                
                ExportStrategyState();
                
                UpdateTradingTrafficStatusLabel(force: true);
                
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
                try { RemoveDrawObject(tradingTrafficStatusLabelTag); } catch { }

                // Clear any accumulated historical bar data
                lock (historicalBarDataLock)
                {
                    historicalBarDataList.Clear();
                }
                
                // Cancel all pending HTTP requests
                if (cancellationTokenSource != null)
                {
                    try
                    {
                        cancellationTokenSource.Cancel();
                        cancellationTokenSource.Dispose();
                    }
                    catch { }
                    cancellationTokenSource = null;
                }
                
                // Dispose HttpClient to cancel pending requests
                lock (clientLock)
                {
                    if (sharedClient != null)
                {
                    try
                    {
                            sharedClient.Dispose();
                    }
                    catch { }
                        sharedClient = null;
                    }
                }
                
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
                if (stopLossResearchWriter != null)
                {
                    try
                    {
                        stopLossResearchWriter.Flush();
                        stopLossResearchWriter.Dispose();
                    }
                    catch { }
                    stopLossResearchWriter = null;
                }
                if (trafficLogWriter != null)
                {
                    try
                    {
                        trafficLogWriter.Flush();
                        trafficLogWriter.Dispose();
                    }
                    catch { }
                    trafficLogWriter = null;
                }
                logInitialized = false;
                opportunityLogInitialized = false;
                outputLogInitialized = false;
                trafficLogInitialized = false;
                stopLossResearchInitialized = false;
                
                // Clean up chart overlays
                if (ChartControl != null && (barNavPanel != null || emaLegendPanel != null))
                {
                    ChartControl.Dispatcher.InvokeAsync(() =>
                    {
                        if (ChartControl != null && ChartControl.Parent is System.Windows.Controls.Grid)
                        {
                            var parent = ChartControl.Parent as System.Windows.Controls.Grid;
                            if (barNavPanel != null)
                                parent.Children.Remove(barNavPanel);
                            if (emaLegendPanel != null)
                                parent.Children.Remove(emaLegendPanel);
                        }
                        barNavPanel = null;
                        emaLegendPanel = null;
                        emaLegendFastLabel = null;
                        emaLegendSlowLabel = null;
                        emaLegend21Label = null;
                    });
                }
            }
        }

        private bool pendingShortFromGood;
        private bool pendingLongFromBad;
        private bool pendingExitLongOnGood;  // postpone long exit if next bar is good
        private bool pendingExitShortOnBad;  // postpone short exit if next bar is bad
        private bool pendingRetraceExitLong; // postpone long retrace exit by one bar
        private int pendingRetraceExitLongBar = -1;
        private bool pendingRetraceExitShort; // postpone short retrace exit by one bar
        private int pendingRetraceExitShortBar = -1;
        private bool pendingEmaStopReverseLong;
        private bool pendingEmaStopReverseShort;
        private int pendingEmaStopReverseBar = -1;
        private bool pendingEmaStopReverseAllowed;
        
        // Deferred entry tracking - waits one bar to validate against the bar where entry will appear
        private bool deferredLongEntry;       // long entry deferred to next bar for validation
        private bool deferredShortEntry;      // short entry deferred to next bar for validation
        private string deferredEntryReason;   // entry reason to use when executing deferred entry
        
        // Mid-bar gradient entry tracking
        private bool waitingForLongGradient;  // all conditions met except gradient for long
        private bool waitingForShortGradient; // all conditions met except gradient for short
        
        // Mid-bar gradient exit tracking
        private bool waitingToExitLongOnGradient;  // all exit conditions met except gradient for long exit
        private bool waitingToExitShortOnGradient; // all exit conditions met except gradient for short exit

        #region Main Bar Update Handler

        protected override void OnBarUpdate()
        {
            try
            {
            // If configured to trade real-time only, skip all historical processing (no trades/logs)
            if (TradeRealtimeOnly && State != State.Realtime)
            {
                Print($"[REALTIME_ONLY] Bar {CurrentBar}: Skipping historical processing (State={State})");
                return;
            }

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
            
            // Tick traffic counters (used by TrafficLog + bar_samples payload)
            UpdateTickTrafficCounters();

            if (CurrentBar < 1)
                return; // need a completed bar to score

            // ====================================================================
            // SECTION 2: MID-BAR PROCESSING (for gradient entry/exit)
            // ====================================================================
            if (!IsFirstTickOfBar)
            {
                CheckMaintenanceFlatten();
                ProcessMidBarOperations();
                
                // Update retrace/progress on live prices so exits can fire intrabar
                UpdateTrendLifecycle(Position.MarketPosition, updateOverlay: false);
                if (Position.MarketPosition != MarketPosition.Flat)
                    UpdateBarNavPositionLabels();
                return; // Exit early for mid-bar ticks - bar close logic below
            }

            // ====================================================================
            // SECTION 3: BAR CLOSE PROCESSING
            // ====================================================================
            
            // 3.0: Flatten before maintenance window (broker rejects orders during CME halt)
            CheckMaintenanceFlatten();

            // 3.1: Calculate market state (trends, gradients, etc.) - DO THIS FIRST so RecordBarSample can use the values
            double prevOpen = Open[1];
            double prevClose = Close[1];
            RecordCompletedBar(prevOpen, prevClose);

            // Traffic log: one row per bar (VolumePerMinute by Hour, DayOfWeek) for predicting activity
            if (EnableTrafficLog)
                LogTrafficSample();

            UpdateTradingTrafficStatusLabel();
            UpdateBarNavPositionLabels();

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
            
            // Per-bar gradient of the bar that just closed [1]: EMA[1] vs EMA[2].
            // On bar 7028, this is the slope of the Fast EMA line on bar 7027.
            double regDeg, chartDeg;
            ComputeCompletedBarGradDeg(out regDeg, out chartDeg);
            lastRegressionGradDeg = regDeg;
            lastChartScaledGradDeg = chartDeg;
            lastFastEmaSlope = (fastEma != null && CurrentBar >= 2 && !double.IsNaN(fastEma[1]) && !double.IsNaN(fastEma[2]))
                ? fastEma[1] - fastEma[2]
                : double.NaN;

            // Per-bar slope of the bar that just closed [1]: chart-scaled when enabled, else atan(price delta).
            lastFastEmaGradDeg = (UseChartScaledFastGradDeg && !double.IsNaN(chartDeg)) ? chartDeg : regDeg;
            if (!double.IsNaN(lastFastEmaGradDeg))
                gradientByBar[CurrentBar] = lastFastEmaGradDeg;
            
            // Store gradient for the bar that just closed (for logging/debugging)
            // On bar 2408, this stores the gradient for bar 2407
            if (CurrentBar >= 1 && !double.IsNaN(lastFastEmaGradDeg))
            {
                gradientByBar[CurrentBar - 1] = lastFastEmaGradDeg;
            }

            // Track the visual angle per-bar too (used by the on-chart label's "eye:" reading)
            if (!double.IsNaN(chartDeg))
            {
                chartGradientByBar[CurrentBar] = chartDeg;
                if (CurrentBar >= 1)
                    chartGradientByBar[CurrentBar - 1] = chartDeg;
            }

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

            // Store the final decision state for RecordBarSample (after ProcessTradingDecisions has updated pending flags)
            lastTrendUp = trendUp;
            lastTrendDown = trendDown;
            lastAllowLongThisBar = allowLongThisBar;
            lastAllowShortThisBar = allowShortThisBar;
            lastPendingLongFromBad = pendingLongFromBad;
            lastPendingShortFromGood = pendingShortFromGood;

            // 3.3.1: Database Operations (state updates, bar samples) - DO THIS AFTER ProcessTradingDecisions
            // so RecordBarSample captures the final decision state including entry reasons and updated pending flags
            ProcessDatabaseOperations();

            // 3.4: Visualization (trend overlays, labels)
            UpdateVisualizations(trendUp, trendDown);

            // 3.5: CSV Logging (bar snapshots, opportunity analysis)
            ProcessCSVLogging(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown, placedEntry);
            }
            catch (Exception ex)
            {
                Print($"[OnBarUpdate_ERROR] {ex.GetType().Name}: {ex.Message}");
            }
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

            if (pauseTradingFromPanel && Position.Quantity == 0)
            {
                Print($"[PANEL] Bar {CurrentBar}: Trading paused - skipping new entries");
                return false;
            }
            
            // Reset intendedPosition if we're flat but intendedPosition is stuck from a previous unfilled entry
            // This prevents blocking valid entries when a previous entry order didn't fill
            if (Position.Quantity == 0 && intendedPosition != MarketPosition.Flat)
            {
                Print($"[INTENDED_POSITION_RESET] Bar {CurrentBar}: Resetting intendedPosition from {intendedPosition} to Flat (position is Flat, previous entry didn't fill)");
                intendedPosition = MarketPosition.Flat;
            }

            // ====================================================================
            // DEFERRED ENTRY VALIDATION
            // Entries are deferred by one bar so we can validate using the bar where the entry will appear
            // Now [1] is the bar that was forming when we made the decision - check if its close validates entry
            // ====================================================================
            if (deferredLongEntry && Position.Quantity == 0)
            {
                double closeOfDecisionBar = Close[1];
                double openOfDecisionBar = Open[1];
                double fastEmaOfDecisionBar = emaFast != null && CurrentBar >= EmaFastPeriod ? emaFast[1] : double.NaN;
                bool currentBarIsBad = IsCurrentBarDownByMinTicks();
                string deferredFilterReason;
                bool deferredFiltersPass = EntryFiltersPass(out deferredFilterReason);
                
                Print($"[DEFERRED_ENTRY] Bar {CurrentBar}: Validating deferred LONG - Close[1]={closeOfDecisionBar:F4}, Open[1]={openOfDecisionBar:F4}, FastEMA[1]={fastEmaOfDecisionBar:F4}");
                
                bool closeBelowFastEma = !double.IsNaN(fastEmaOfDecisionBar) && closeOfDecisionBar < fastEmaOfDecisionBar;
                bool decisionBarWasBad = closeOfDecisionBar < openOfDecisionBar;
                bool blockedByBadCandle = AvoidLongsOnBadCandle && decisionBarWasBad;
                double deferredGradDeg;
                GetCurrentGradient(out deferredGradDeg);
                bool blockedByGradient = ShouldSkipLongDueToGradient(deferredGradDeg);
                
                if (closeBelowFastEma)
                {
                    Print($"[DEFERRED_ENTRY_BLOCKED] Bar {CurrentBar}: LONG entry BLOCKED - Close ({closeOfDecisionBar:F4}) < Fast EMA ({fastEmaOfDecisionBar:F4}) on the bar where entry would appear");
                    deferredLongEntry = false;
                    deferredEntryReason = "";
                    intendedPosition = MarketPosition.Flat; // Reset since entry was blocked
                }
                else if (blockedByBadCandle)
                {
                    Print($"[DEFERRED_ENTRY_BLOCKED] Bar {CurrentBar}: LONG entry BLOCKED - Decision bar closed BAD (O:{openOfDecisionBar:F4}, C:{closeOfDecisionBar:F4}) and AvoidLongsOnBadCandle=True");
                    deferredLongEntry = false;
                    deferredEntryReason = "";
                    intendedPosition = MarketPosition.Flat; // Reset since entry was blocked
                    pendingLongFromBad = true;
                }
                else if (!deferredFiltersPass)
                {
                    Print($"[DEFERRED_ENTRY_BLOCKED] Bar {CurrentBar}: LONG entry BLOCKED - {deferredFilterReason}");
                    deferredLongEntry = false;
                    deferredEntryReason = "";
                    intendedPosition = MarketPosition.Flat; // Reset since entry was blocked
                }
                else if (AvoidLongsOnBadCandleCurrentBar && currentBarIsBad)
                {
                    Print($"[DEFERRED_ENTRY_BLOCKED] Bar {CurrentBar}: LONG entry BLOCKED - Current bar down by >= {MinTicksCurrentBarDirection} ticks (Close[0]={Close[0]:F4} < Open[0]={Open[0]:F4}) and AvoidLongsOnBadCandleCurrentBar=True");
                    deferredLongEntry = false;
                    deferredEntryReason = "";
                    intendedPosition = MarketPosition.Flat; // Reset since entry was blocked
                }
                else if (blockedByGradient)
                {
                    Print($"[DEFERRED_ENTRY_BLOCKED] Bar {CurrentBar}: LONG entry BLOCKED - Current bar gradient {deferredGradDeg:F2}° < {SkipLongsBelowGradient:F2}° (GradientFilterEnabled={GradientFilterEnabled})");
                    deferredLongEntry = false;
                    deferredEntryReason = "";
                    intendedPosition = MarketPosition.Flat; // Reset since entry was blocked
                }
                else
                {
                    Print($"[DEFERRED_ENTRY_EXECUTE] Bar {CurrentBar}: LONG entry VALIDATED - Close ({closeOfDecisionBar:F4}) >= Fast EMA ({fastEmaOfDecisionBar:F4}), executing entry");
                    CaptureDecisionContext(Open[1], Close[1], allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                    currentTradeEntryReason = deferredEntryReason;
                    ResetExitReason();
                    PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering LONG (deferred, validated), CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}");
                    SubmitLongEntry();
                    lastEntryBarIndex = CurrentBar;
                    lastEntryDirection = MarketPosition.Long;
                    intendedPosition = MarketPosition.Long;
                    placedEntry = true;
                    deferredLongEntry = false;
                    deferredEntryReason = "";
                    pendingLongFromBad = false;
                    pendingShortFromGood = false;
                }
            }
            
            if (deferredShortEntry && Position.Quantity == 0)
            {
                double closeOfDecisionBar = Close[1];
                double openOfDecisionBar = Open[1];
                double fastEmaOfDecisionBar = emaFast != null && CurrentBar >= EmaFastPeriod ? emaFast[1] : double.NaN;
                bool currentBarIsGood = IsCurrentBarUpByMinTicks();
                string deferredFilterReason;
                bool deferredFiltersPass = EntryFiltersPass(out deferredFilterReason);
                
                Print($"[DEFERRED_ENTRY] Bar {CurrentBar}: Validating deferred SHORT - Close[1]={closeOfDecisionBar:F4}, Open[1]={openOfDecisionBar:F4}, FastEMA[1]={fastEmaOfDecisionBar:F4}");
                
                bool closeAboveFastEma = !double.IsNaN(fastEmaOfDecisionBar) && closeOfDecisionBar > fastEmaOfDecisionBar;
                bool decisionBarWasGood = closeOfDecisionBar > openOfDecisionBar;
                bool blockedByGoodCandle = AvoidShortsOnGoodCandle && decisionBarWasGood;
                double deferredGradDeg;
                GetCurrentGradient(out deferredGradDeg);
                bool blockedByGradient = ShouldSkipShortDueToGradient(deferredGradDeg);
                
                if (closeAboveFastEma)
                {
                    Print($"[DEFERRED_ENTRY_BLOCKED] Bar {CurrentBar}: SHORT entry BLOCKED - Close ({closeOfDecisionBar:F4}) > Fast EMA ({fastEmaOfDecisionBar:F4}) on the bar where entry would appear");
                    deferredShortEntry = false;
                    deferredEntryReason = "";
                    intendedPosition = MarketPosition.Flat; // Reset since entry was blocked
                }
                else if (blockedByGoodCandle)
                {
                    Print($"[DEFERRED_ENTRY_BLOCKED] Bar {CurrentBar}: SHORT entry BLOCKED - Decision bar closed GOOD (O:{openOfDecisionBar:F4}, C:{closeOfDecisionBar:F4}) and AvoidShortsOnGoodCandle=True");
                    deferredShortEntry = false;
                    deferredEntryReason = "";
                    intendedPosition = MarketPosition.Flat; // Reset since entry was blocked
                    pendingShortFromGood = true;
                }
                else if (!deferredFiltersPass)
                {
                    Print($"[DEFERRED_ENTRY_BLOCKED] Bar {CurrentBar}: SHORT entry BLOCKED - {deferredFilterReason}");
                    deferredShortEntry = false;
                    deferredEntryReason = "";
                    intendedPosition = MarketPosition.Flat; // Reset since entry was blocked
                }
                else if (AvoidShortsOnGoodCandleCurrentBar && currentBarIsGood)
                {
                    Print($"[DEFERRED_ENTRY_BLOCKED] Bar {CurrentBar}: SHORT entry BLOCKED - Current bar up by >= {MinTicksCurrentBarDirection} ticks (Close[0]={Close[0]:F4} > Open[0]={Open[0]:F4}) and AvoidShortsOnGoodCandleCurrentBar=True");
                    deferredShortEntry = false;
                    deferredEntryReason = "";
                    intendedPosition = MarketPosition.Flat; // Reset since entry was blocked
                }
                else if (blockedByGradient)
                {
                    Print($"[DEFERRED_ENTRY_BLOCKED] Bar {CurrentBar}: SHORT entry BLOCKED - Current bar gradient {deferredGradDeg:F2}° > {SkipShortsAboveGradient:F2}° (GradientFilterEnabled={GradientFilterEnabled})");
                    deferredShortEntry = false;
                    deferredEntryReason = "";
                    intendedPosition = MarketPosition.Flat; // Reset since entry was blocked
                }
                else
                {
                    Print($"[DEFERRED_ENTRY_EXECUTE] Bar {CurrentBar}: SHORT entry VALIDATED - Close ({closeOfDecisionBar:F4}) <= Fast EMA ({fastEmaOfDecisionBar:F4}), executing entry");
                    CaptureDecisionContext(Open[1], Close[1], allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                    currentTradeEntryReason = deferredEntryReason;
                    ResetExitReason();
                    PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering SHORT (deferred, validated), CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}");
                    SubmitShortEntry();
                    lastEntryBarIndex = CurrentBar;
                    lastEntryDirection = MarketPosition.Short;
                    intendedPosition = MarketPosition.Short;
                    placedEntry = true;
                    deferredShortEntry = false;
                    deferredEntryReason = "";
                    pendingLongFromBad = false;
                    pendingShortFromGood = false;
                }
            }

            // ====================================================================
            // DEFERRED EMA STOP REVERSAL
            // Queue reversal to next bar to avoid same-bar exit/entry collisions
            // ====================================================================
            if ((pendingEmaStopReverseLong || pendingEmaStopReverseShort) && Position.Quantity == 0 && CurrentBar >= pendingEmaStopReverseBar + 1)
            {
                bool isLong = pendingEmaStopReverseLong;
                bool allowSide = pendingEmaStopReverseAllowed && (isLong ? allowLongThisBar : allowShortThisBar);
                string sideText = isLong ? "LONG" : "SHORT";

                if (!allowSide)
                {
                    Print($"[EMA_STOP_REVERSE_DEFERRED] Bar {CurrentBar}: {sideText} reverse BLOCKED on next bar. allowSide={allowSide}, allowLongThisBar={allowLongThisBar}, allowShortThisBar={allowShortThisBar}");
                    pendingEmaStopReverseLong = false;
                    pendingEmaStopReverseShort = false;
                    pendingEmaStopReverseBar = -1;
                    pendingEmaStopReverseAllowed = false;
                }
                else
                {
                    currentTradeEntryReason = $"ReverseFromEmaStopDeferred";
                    ResetExitReason();
                    PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering {sideText} (deferred EMA stop reversal), CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}");
                    if (isLong)
                    {
                        SubmitLongEntry();
                        lastEntryBarIndex = CurrentBar;
                        lastEntryDirection = MarketPosition.Long;
                        intendedPosition = MarketPosition.Long;
                    }
                    else
                    {
                        SubmitShortEntry();
                        lastEntryBarIndex = CurrentBar;
                        lastEntryDirection = MarketPosition.Short;
                        intendedPosition = MarketPosition.Short;
                    }

                    placedEntry = true;
                    pendingEmaStopReverseLong = false;
                    pendingEmaStopReverseShort = false;
                    pendingEmaStopReverseBar = -1;
                    pendingEmaStopReverseAllowed = false;
                    return placedEntry;
                }
            }

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

                Print($"[EXIT_DEBUG] Bar {CurrentBar}: ExitIfEntryBarOpposite check - lastEntryBarIndex={lastEntryBarIndex}, CurrentBar-1={CurrentBar - 1}, currentPos={currentPos}, entryBarWasGood={entryBarWasGood}, entryBarWasBad={entryBarWasBad}, longExits={longExits}, shortExits={shortExits}");

                if (longExits)
                {
                    Print($"[EXIT_DEBUG] Bar {CurrentBar}: ExitIfEntryBarOpposite triggering LONG exit - entry bar was BAD (O:{Open[1]:F2}, C:{Close[1]:F2})");
                    CaptureDecisionContext(Open[1], Close[1], true, true, trendUp, trendDown);
                    currentTradeExitReason = "BarsOnTheFlowEntryBarOpp";
                    lastExitBarIndex = CurrentBar;
                    ExitLongTrace("BarsOnTheFlowEntryBarOpp", GetActiveEntrySignal(), "BarsOnTheFlowEntryBarOpp");
                    intendedPosition = MarketPosition.Flat;
                    RecordExitForCooldown(MarketPosition.Long);
                    return false; // Exit early, no entry placed
                }

                if (shortExits)
                {
                    Print($"[EXIT_DEBUG] Bar {CurrentBar}: ExitIfEntryBarOpposite triggering SHORT exit - entry bar was GOOD (O:{Open[1]:F2}, C:{Close[1]:F2})");
                    CaptureDecisionContext(Open[1], Close[1], true, true, trendUp, trendDown);
                    currentTradeExitReason = "BarsOnTheFlowEntryBarOppS";
                    lastExitBarIndex = CurrentBar;
                    ExitShortTrace("BarsOnTheFlowEntryBarOppS", GetActiveEntrySignal(), "BarsOnTheFlowEntryBarOppS");
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
                UpdateTradeMfePointsFromBar();
                double currentProfit = Position.GetUnrealizedProfitLoss(PerformanceUnit.Points);
                int mfeTrigger = GetMfeProtectTriggerPoints();
                bool mfeThresholdReached = currentTradeMfePoints >= mfeTrigger || currentProfit >= BreakEvenTrigger;

                if (mfeThresholdReached)
                {
                    // Apply profit floor even when trailing stop is on (replaces wide trail with locked profit).
                    // EMA trailing stop has its own floor/ceiling logic.
                    bool canApplyProfitFloor = !UseEmaTrailingStop;

                    double entryPrice = Position.AveragePrice;
                    double breakEvenStopPrice;
                    string orderName;

                    double currentPrice = Close[0];
                    double tickSize = Instrument.MasterInstrument.TickSize;
                    double minMove = tickSize > 0 ? tickSize : 0.25;
                    int ticksPerPoint = GetTicksPerPoint();
                    double safetyTicks = Math.Max(2, ticksPerPoint);
                    double safetyMargin = safetyTicks * minMove;

                    if (currentPos == MarketPosition.Long)
                    {
                        breakEvenStopPrice = entryPrice + BreakEvenOffset;
                        orderName = GetActiveEntrySignal();

                        if (canApplyProfitFloor)
                        {
                            double bid = 0;
                            try { bid = GetCurrentBid(); } catch { }
                            double marketRef = bid > 0 ? bid : currentPrice;

                            if (marketRef <= breakEvenStopPrice + minMove)
                            {
                                Print($"[MFE_PROTECT_MARKET_EXIT] Bar {CurrentBar}: LONG price already <= profit floor. MarketRef={marketRef:F4}, Floor={breakEvenStopPrice:F4}, PeakMFE={currentTradeMfePoints:F2}");
                                currentTradeExitReason = "BarsOnTheFlowBreakEvenMkt";
                                lastExitBarIndex = CurrentBar;
                                ExitLongTrace("BarsOnTheFlowBreakEvenMkt", GetActiveEntrySignal(), "BarsOnTheFlowBreakEvenMkt");
                                intendedPosition = MarketPosition.Flat;
                                RecordExitForCooldown(MarketPosition.Long);
                                breakEvenActivated = true;
                                breakEvenEntryPrice = entryPrice;
                                return false;
                            }

                            double maxAllowedStop = marketRef - safetyMargin;
                            if (breakEvenStopPrice > maxAllowedStop)
                            {
                                double original = breakEvenStopPrice;
                                breakEvenStopPrice = Instrument.MasterInstrument.RoundToTickSize(maxAllowedStop);
                                Print($"[MFE_PROTECT_CLAMP] LONG: adjusted floor from {original:F4} to {breakEvenStopPrice:F4} (market={currentPrice:F4})");
                            }

                            ApplyProfitFloorStop(orderName, breakEvenStopPrice);
                            currentTradeStopLossPoints = BreakEvenOffset;
                        }

                        Print($"[MFE_PROTECT] Bar {CurrentBar}: LONG profit floor armed. Entry={entryPrice:F2}, PeakMFE={currentTradeMfePoints:F2}, CurrentProfit={currentProfit:F2}, Floor={breakEvenStopPrice:F2} (Entry+{BreakEvenOffset}), UseTrailingStop={UseTrailingStop}, AppliedFloor={canApplyProfitFloor}");
                    }
                    else
                    {
                        breakEvenStopPrice = entryPrice - BreakEvenOffset;
                        orderName = GetActiveEntrySignal();

                        if (canApplyProfitFloor)
                        {
                            double ask = 0;
                            try { ask = GetCurrentAsk(); } catch { }
                            double marketRef = ask > 0 ? ask : currentPrice;

                            if (marketRef >= breakEvenStopPrice - minMove)
                            {
                                Print($"[MFE_PROTECT_MARKET_EXIT] Bar {CurrentBar}: SHORT price already >= profit floor. MarketRef={marketRef:F4}, Floor={breakEvenStopPrice:F4}, PeakMFE={currentTradeMfePoints:F2}");
                                currentTradeExitReason = "BarsOnTheFlowBreakEvenMktS";
                                lastExitBarIndex = CurrentBar;
                                ExitShortTrace("BarsOnTheFlowBreakEvenMktS", GetActiveEntrySignal(), "BarsOnTheFlowBreakEvenMktS");
                                intendedPosition = MarketPosition.Flat;
                                RecordExitForCooldown(MarketPosition.Short);
                                breakEvenActivated = true;
                                breakEvenEntryPrice = entryPrice;
                                return false;
                            }

                            double minAllowedStop = marketRef + safetyMargin;
                            if (breakEvenStopPrice < minAllowedStop)
                            {
                                double original = breakEvenStopPrice;
                                breakEvenStopPrice = Instrument.MasterInstrument.RoundToTickSize(minAllowedStop);
                                Print($"[MFE_PROTECT_CLAMP] SHORT: adjusted floor from {original:F4} to {breakEvenStopPrice:F4} (market={currentPrice:F4})");
                            }

                            ApplyProfitFloorStop(orderName, breakEvenStopPrice);
                            currentTradeStopLossPoints = BreakEvenOffset;
                        }

                        Print($"[MFE_PROTECT] Bar {CurrentBar}: SHORT profit floor armed. Entry={entryPrice:F2}, PeakMFE={currentTradeMfePoints:F2}, CurrentProfit={currentProfit:F2}, Floor={breakEvenStopPrice:F2} (Entry-{BreakEvenOffset}), UseTrailingStop={UseTrailingStop}, AppliedFloor={canApplyProfitFloor}");
                    }

                    breakEvenActivated = true;
                    breakEvenEntryPrice = entryPrice;
                }
            }

            ReinforceProfitFloorStopIfNeeded();

            // ====================================================================
            // GUARD 3: EMA Trailing Stop Loss Management
            // Uses CURRENT OHLC [0] - Real-time position management during bar formation
            // Stop loss follows Fast EMA but only moves in favorable direction
            // When break-even is activated, it acts as a floor (longs) or ceiling (shorts)
            // ====================================================================
            if (UseEmaTrailingStop && currentPos != MarketPosition.Flat && fastEma != null && CurrentBar >= FastEmaPeriod && !double.IsNaN(entryFastEmaValue))
            {
                // Check if EMA stop should be active based on delay and profit requirements
                int barsSinceEntry = lastEntryBarIndex > 0 ? (CurrentBar - lastEntryBarIndex) : int.MaxValue;
                double activationProfitCheck = currentPos == MarketPosition.Long ? (Close[0] - Position.AveragePrice) : (Position.AveragePrice - Close[0]);
                
                bool delayPassed = barsSinceEntry >= EmaStopActivationDelayBars;
                bool profitRequirementMet = EmaStopMinProfitBeforeActivation == 0 || activationProfitCheck >= EmaStopMinProfitBeforeActivation;
                bool shouldActivateStop = delayPassed && profitRequirementMet;
                
                if (!shouldActivateStop)
                {
                    // Skip EMA trailing stop management if not activated yet,
                    // but continue other exit logic this bar.
                }
                else
                {
                
                double currentFastEma = fastEma[0]; // Current Fast EMA value
                double newStopLossPrice = double.NaN;
                string orderName;
                
                // EMA Trailing Stop OVERRIDES break-even (as per description)
                // When UseEmaTrailingStop is enabled, we ignore break-even completely
                // and use only the EMA value for the stop loss
                
                if (currentPos == MarketPosition.Long)
                {
                    orderName = GetActiveEntrySignal();
                    double entryPrice = Position.AveragePrice;
                    // For longs: stop loss = Fast EMA, but only moves UP (never down)
                    double emaBasedStop = Instrument.MasterInstrument.RoundToTickSize(currentFastEma);
                    
                    // Calculate proposed stop based on EMA and minimum distance
                    double proposedStop = emaBasedStop;
                    if (EmaStopMinDistanceFromEntry > 0)
                    {
                        // For LONG: protective stop must be BELOW entry by at least EmaStopMinDistanceFromEntry.
                        // If EMA-based stop is too tight (too high), clamp it DOWN.
                        double maxAllowedStop = entryPrice - EmaStopMinDistanceFromEntry;
                        proposedStop = Instrument.MasterInstrument.RoundToTickSize(Math.Min(emaBasedStop, maxAllowedStop));
                    }
                    
                    // Only update if the proposed stop is higher than current (or if current is NaN)
                    // This allows the stop to trail the EMA upward, but ensures it never gets tighter than the min distance from entry.
                    if (double.IsNaN(currentEmaStopLoss) || proposedStop > currentEmaStopLoss)
                    {
                        newStopLossPrice = proposedStop;
                        currentEmaStopLoss = newStopLossPrice;
                        
                        // If requiring full candle below, don't set automatic stop loss (we'll check manually on bar close)
                        // Otherwise, set the stop loss normally
                        if (EmaStopTriggerMode == EmaStopTriggerModeType.CloseOnly)
                        {
                            SetStopLoss(orderName, CalculationMode.Price, newStopLossPrice, false);
                        }
                        
                        currentTradeStopLossPoints = Math.Abs(entryPrice - newStopLossPrice);
                        if (EmaStopMinDistanceFromEntry > 0 && newStopLossPrice < emaBasedStop)
                        {
                            Print($"[EMA_TRAILING_STOP] Bar {CurrentBar}: LONG - Fast EMA={currentFastEma:F4}, EMA-based stop={emaBasedStop:F4}, Min distance={EmaStopMinDistanceFromEntry:F4}, Final stop loss={newStopLossPrice:F4} (enforced minimum distance from entry {entryPrice:F4}), TriggerMode={EmaStopTriggerMode}");
                        }
                        else
                        {
                            Print($"[EMA_TRAILING_STOP] Bar {CurrentBar}: LONG - Fast EMA={currentFastEma:F4}, Stop loss={newStopLossPrice:F4}, TriggerMode={EmaStopTriggerMode} (EMA trailing stop overrides break-even)");
                        }
                    }
                }
                else if (currentPos == MarketPosition.Short)
                {
                    orderName = GetActiveEntrySignal();
                    double entryPrice = Position.AveragePrice;
                    // For shorts: stop loss = Fast EMA, but only moves DOWN (never up)
                    double emaBasedStop = Instrument.MasterInstrument.RoundToTickSize(currentFastEma);
                    
                    // Calculate proposed stop based on EMA and minimum distance
                    double proposedStop = emaBasedStop;
                    if (EmaStopMinDistanceFromEntry > 0)
                    {
                        // For SHORT: protective stop must be ABOVE entry by at least EmaStopMinDistanceFromEntry.
                        // If EMA-based stop is too tight (too low), clamp it UP.
                        double minAllowedStop = entryPrice + EmaStopMinDistanceFromEntry;
                        proposedStop = Instrument.MasterInstrument.RoundToTickSize(Math.Max(emaBasedStop, minAllowedStop));
                    }
                    
                    // Only update if the proposed stop is lower than current (or if current is NaN)
                    // This allows the stop to trail the EMA downward, but ensures it never gets tighter than the min distance from entry.
                    if (double.IsNaN(currentEmaStopLoss) || proposedStop < currentEmaStopLoss)
                    {
                        newStopLossPrice = proposedStop;
                        currentEmaStopLoss = newStopLossPrice;
                        
                        // If requiring full candle below, don't set automatic stop loss (we'll check manually on bar close)
                        // Otherwise, set the stop loss normally
                        if (EmaStopTriggerMode == EmaStopTriggerModeType.CloseOnly)
                        {
                            SetStopLoss(orderName, CalculationMode.Price, newStopLossPrice, false);
                        }
                        
                        currentTradeStopLossPoints = Math.Abs(entryPrice - newStopLossPrice);
                        if (EmaStopMinDistanceFromEntry > 0 && newStopLossPrice > emaBasedStop)
                        {
                            Print($"[EMA_TRAILING_STOP] Bar {CurrentBar}: SHORT - Fast EMA={currentFastEma:F4}, EMA-based stop={emaBasedStop:F4}, Min distance={EmaStopMinDistanceFromEntry:F4}, Final stop loss={newStopLossPrice:F4} (enforced minimum distance from entry {entryPrice:F4}), TriggerMode={EmaStopTriggerMode}");
                        }
                        else
                        {
                            Print($"[EMA_TRAILING_STOP] Bar {CurrentBar}: SHORT - Fast EMA={currentFastEma:F4}, Stop loss={newStopLossPrice:F4}, TriggerMode={EmaStopTriggerMode} (EMA trailing stop overrides break-even)");
                        }
                    }
                }
                }
            }

            // ====================================================================
            // GUARD 4: EMA Stop Loss - Bar Close Check (Both FullCandle and CloseOnly modes)
            // Uses FINAL OHLC [1] - Checks if entire completed candle (FullCandle) or just close (CloseOnly) is below/above stop loss
            // Executes for both modes: FullCandle checks entire candle, CloseOnly checks just close (as backup to SetStopLoss)
            // ====================================================================
            if (UseEmaTrailingStop && currentPos != MarketPosition.Flat && CurrentBar >= 1 && fastEma != null && CurrentBar >= FastEmaPeriod)
            {
                // Check if EMA stop should be active based on delay and profit requirements
                int barsSinceEntry = lastEntryBarIndex > 0 ? (CurrentBar - lastEntryBarIndex) : int.MaxValue;
                double activationProfitCheck = currentPos == MarketPosition.Long ? (Close[1] - Position.AveragePrice) : (Position.AveragePrice - Close[1]);
                
                bool delayPassed = barsSinceEntry >= EmaStopActivationDelayBars;
                bool profitRequirementMet = EmaStopMinProfitBeforeActivation == 0 || activationProfitCheck >= EmaStopMinProfitBeforeActivation;
                bool shouldActivateStop = delayPassed && profitRequirementMet;
                
                if (!shouldActivateStop)
                {
                    if (barsSinceEntry < EmaStopActivationDelayBars)
                    {
                        Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: EMA trailing stop INACTIVE - Bars since entry ({barsSinceEntry}) < Activation delay ({EmaStopActivationDelayBars})");
                    }
                    else if (activationProfitCheck < EmaStopMinProfitBeforeActivation)
                    {
                        Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: EMA trailing stop INACTIVE - Current profit ({activationProfitCheck:F4}) < Min profit required ({EmaStopMinProfitBeforeActivation:F4})");
                    }
                }
                else
                {
                // Use completed bar [1] for the check (matches the bar we're checking)
                double completedBarHigh = High[1];
                double completedBarLow = Low[1];
                double completedBarClose = Close[1];
                double completedBarOpen = Open[1];
                double completedBarFastEma = fastEma[1]; // Use completed bar's EMA to match the bar we're checking
                bool shouldExit = false;
                bool allowShortOnEmaStop = !(AvoidShortsOnGoodCandle && completedBarClose > completedBarOpen);
                bool allowLongOnEmaStop = !(AvoidLongsOnBadCandle && completedBarClose < completedBarOpen);
                
                // Determine the effective stop loss level (considering break-even floor/ceiling)
                double effectiveStopLoss = double.NaN;
                
                // EMA Trailing Stop OVERRIDES break-even (as per description)
                // When UseEmaTrailingStop is enabled, we ignore break-even completely
                // and use only the EMA value for the stop loss
                
                // For FullCandle mode: Use completed bar's EMA only
                // This ensures we're checking the bar against the EMA value at the time the bar closed
                if (EmaStopTriggerMode == EmaStopTriggerModeType.FullCandle)
                {
                    if (!double.IsNaN(completedBarFastEma))
                    {
                        effectiveStopLoss = completedBarFastEma;
                        Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: FullCandle mode - Using completed bar's EMA={completedBarFastEma:F4} (EMA trailing stop overrides break-even)");
                    }
                }
                // For CloseOnly/BodyOnly modes: Use EMA only (break-even is overridden)
                else
                {
                    // Use currentEmaStopLoss (which is the trailing stop that only moves in favorable direction)
                    // Or fall back to completedBarFastEma if currentEmaStopLoss is not set
                    double emaStopToUse = !double.IsNaN(currentEmaStopLoss) ? currentEmaStopLoss : completedBarFastEma;
                    
                    // Apply minimum distance from entry if specified and we're using the fallback EMA
                    if (!double.IsNaN(emaStopToUse) && EmaStopMinDistanceFromEntry > 0 && double.IsNaN(currentEmaStopLoss))
                    {
                        double entryPrice = Position.AveragePrice;
                        if (currentPos == MarketPosition.Long)
                        {
                            double minStopPrice = entryPrice + EmaStopMinDistanceFromEntry;
                            emaStopToUse = Instrument.MasterInstrument.RoundToTickSize(Math.Max(emaStopToUse, minStopPrice));
                        }
                        else if (currentPos == MarketPosition.Short)
                        {
                            double maxStopPrice = entryPrice - EmaStopMinDistanceFromEntry;
                            emaStopToUse = Instrument.MasterInstrument.RoundToTickSize(Math.Min(emaStopToUse, maxStopPrice));
                        }
                    }
                    
                    if (!double.IsNaN(emaStopToUse))
                    {
                        effectiveStopLoss = emaStopToUse;
                        Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: Using EMA stop={emaStopToUse:F4} (EMA trailing stop overrides break-even, breakEvenActivated={breakEvenActivated})");
                    }
                }
                
                if (double.IsNaN(effectiveStopLoss))
                {
                    Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: WARNING - No valid EMA stop loss to check (currentEmaStopLoss={currentEmaStopLoss}, completedBarFastEma={completedBarFastEma})");
                    // Try to use EMA if available (EMA trailing stop overrides break-even, so we don't use break-even here)
                    if (!double.IsNaN(currentEmaStopLoss))
                    {
                        effectiveStopLoss = currentEmaStopLoss;
                        Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: Using currentEmaStopLoss: {effectiveStopLoss:F4}");
                    }
                    else if (!double.IsNaN(completedBarFastEma))
                    {
                        // Apply minimum distance from entry if specified
                        double entryPrice = Position.AveragePrice;
                        if (EmaStopMinDistanceFromEntry > 0)
                        {
                            if (currentPos == MarketPosition.Long)
                            {
                                double minStopPrice = entryPrice + EmaStopMinDistanceFromEntry;
                                effectiveStopLoss = Instrument.MasterInstrument.RoundToTickSize(Math.Max(completedBarFastEma, minStopPrice));
                            }
                            else if (currentPos == MarketPosition.Short)
                            {
                                double maxStopPrice = entryPrice - EmaStopMinDistanceFromEntry;
                                effectiveStopLoss = Instrument.MasterInstrument.RoundToTickSize(Math.Min(completedBarFastEma, maxStopPrice));
                            }
                            Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: Using completedBarFastEma with min distance: {effectiveStopLoss:F4} (EMA={completedBarFastEma:F4}, Entry={entryPrice:F4}, MinDist={EmaStopMinDistanceFromEntry:F4})");
                        }
                        else
                        {
                            effectiveStopLoss = completedBarFastEma;
                            Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: Using completedBarFastEma: {effectiveStopLoss:F4}");
                        }
                    }
                    else
                    {
                        Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: ERROR - Cannot determine EMA stop loss, skipping check");
                        return false; // No valid stop loss to check
                    }
                }
                
                if (currentPos == MarketPosition.Long)
                {
                    // For longs: check based on trigger mode
                    bool shouldTrigger = false;
                    bool earlyExitDueToProfit = false;
                    
                    if (EmaStopTriggerMode == EmaStopTriggerModeType.FullCandle)
                    {
                        // Full candle: both High and Low must be below the stop loss
                        shouldTrigger = completedBarHigh < effectiveStopLoss && completedBarLow < effectiveStopLoss;
                        Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: LONG - FullCandle mode - High={completedBarHigh:F4}, Low={completedBarLow:F4}, StopLoss={effectiveStopLoss:F4}, Trigger={shouldTrigger}");
                        
                        // Profit protection: if in profit and close crossed below EMA, exit early
                        if (!shouldTrigger && EmaStopProfitProtectionPoints > 0)
                        {
                            double entryPrice = Position.AveragePrice;
                            double currentProfit = completedBarClose - entryPrice;
                            if (currentProfit >= EmaStopProfitProtectionPoints)
                            {
                                // Check if close crossed below EMA (CloseOnly check)
                                bool closeCrossedBelow = completedBarClose < effectiveStopLoss;
                                if (closeCrossedBelow)
                                {
                                    earlyExitDueToProfit = true;
                                    Print($"[EMA_STOP_PROFIT_PROTECTION] Bar {CurrentBar}: LONG - Early exit triggered! Profit={currentProfit:F4} >= {EmaStopProfitProtectionPoints:F4}, Close={completedBarClose:F4} < EMA={effectiveStopLoss:F4}");
                                }
                            }
                        }
                    }
                    else if (EmaStopTriggerMode == EmaStopTriggerModeType.BodyOnly)
                    {
                        // Body only: the FULL body (both bodyTop and bodyBottom) must be below the stop loss
                        // This ensures the entire body range is below the stop, not just part of it
                        double bodyTop = Math.Max(completedBarOpen, completedBarClose);
                        double bodyBottom = Math.Min(completedBarOpen, completedBarClose);
                        // For LONG exits: FULL body must be below stop loss (both top and bottom must be below)
                        shouldTrigger = bodyTop < effectiveStopLoss && bodyBottom < effectiveStopLoss;
                        
                        // Get current bar conditions for comparison
                        double currentBarClose = Close[0];
                        double currentBarFastEma = fastEma != null && CurrentBar >= FastEmaPeriod ? fastEma[0] : double.NaN;
                        bool currentBarAboveEma = !double.IsNaN(currentBarFastEma) && currentBarClose > currentBarFastEma;
                        double currentGradDeg;
                        GetCurrentGradient(out currentGradDeg);
                        
                        Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: LONG - BodyOnly mode - Open={completedBarOpen:F4}, Close={completedBarClose:F4}, BodyTop={bodyTop:F4}, BodyBottom={bodyBottom:F4}, StopLoss={effectiveStopLoss:F4}, FullBodyBelow={(bodyTop < effectiveStopLoss && bodyBottom < effectiveStopLoss)}, Trigger={shouldTrigger}");
                        if (shouldTrigger)
                        {
                            Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: NOTE - Exit triggered based on COMPLETED bar [1] conditions, not current bar [0]");
                            Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: Completed bar [1]: BodyTop={bodyTop:F4}, BodyBottom={bodyBottom:F4}, StopLoss={effectiveStopLoss:F4}, FullBodyBelow={(bodyTop < effectiveStopLoss && bodyBottom < effectiveStopLoss)}");
                            Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: Current bar [0]: Close={currentBarClose:F4}, FastEMA={currentBarFastEma:F4}, AboveEMA={currentBarAboveEma}, Gradient={currentGradDeg:F2}°");
                            Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: REMINDER - Exit fill will occur on next bar (bar {CurrentBar + 1}) in historical backtesting. Bar {CurrentBar + 1} conditions will not be checked before fill.");
                            if (currentBarAboveEma && currentGradDeg > 0)
                            {
                                Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: WARNING - Current bar looks good (above EMA, positive gradient), but exit triggered by completed bar's full body below stop loss");
                            }
                        }
                        
                        // Profit protection: if in profit and close crossed below EMA, exit early
                        if (!shouldTrigger && EmaStopProfitProtectionPoints > 0)
                        {
                            double entryPrice = Position.AveragePrice;
                            double currentProfit = completedBarClose - entryPrice;
                            if (currentProfit >= EmaStopProfitProtectionPoints)
                            {
                                // Check if close crossed below EMA (CloseOnly check)
                                bool closeCrossedBelow = completedBarClose < effectiveStopLoss;
                                if (closeCrossedBelow)
                                {
                                    earlyExitDueToProfit = true;
                                    Print($"[EMA_STOP_PROFIT_PROTECTION] Bar {CurrentBar}: LONG - Early exit triggered! Profit={currentProfit:F4} >= {EmaStopProfitProtectionPoints:F4}, Close={completedBarClose:F4} < EMA={effectiveStopLoss:F4}");
                                }
                            }
                        }
                    }
                    else if (EmaStopTriggerMode == EmaStopTriggerModeType.CloseOnly)
                    {
                        // Close only: just the close needs to be below the stop loss
                        shouldTrigger = completedBarClose < effectiveStopLoss;
                        Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: LONG - CloseOnly mode - Close={completedBarClose:F4}, StopLoss={effectiveStopLoss:F4}, Trigger={shouldTrigger}");
                    }
                    
                    if (shouldTrigger || earlyExitDueToProfit)
                    {
                        shouldExit = true;
                        string exitReason = earlyExitDueToProfit ? "ProfitProtection" : EmaStopTriggerMode.ToString();
                        Print($"[EMA_STOP_EXIT] Bar {CurrentBar}: LONG exit triggered - Mode={exitReason}, Effective stop loss={effectiveStopLoss:F4} (EMA={completedBarFastEma:F4}, Entry={Position.AveragePrice:F4})");
                    }
                }
                else if (currentPos == MarketPosition.Short)
                {
                    // For shorts: check based on trigger mode
                    bool shouldTrigger = false;
                    bool earlyExitDueToProfit = false;
                    
                    if (EmaStopTriggerMode == EmaStopTriggerModeType.FullCandle)
                    {
                        // Full candle: both High and Low must be above the stop loss
                        shouldTrigger = completedBarHigh > effectiveStopLoss && completedBarLow > effectiveStopLoss;
                        Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: SHORT - FullCandle mode - High={completedBarHigh:F4}, Low={completedBarLow:F4}, StopLoss={effectiveStopLoss:F4}, Trigger={shouldTrigger}");
                        
                        // Profit protection: if in profit and close crossed above EMA, exit early
                        if (!shouldTrigger && EmaStopProfitProtectionPoints > 0)
                        {
                            double entryPrice = Position.AveragePrice;
                            double currentProfit = entryPrice - completedBarClose;
                            if (currentProfit >= EmaStopProfitProtectionPoints)
                            {
                                // Check if close crossed above EMA (CloseOnly check)
                                bool closeCrossedAbove = completedBarClose > effectiveStopLoss;
                                if (closeCrossedAbove)
                                {
                                    earlyExitDueToProfit = true;
                                    Print($"[EMA_STOP_PROFIT_PROTECTION] Bar {CurrentBar}: SHORT - Early exit triggered! Profit={currentProfit:F4} >= {EmaStopProfitProtectionPoints:F4}, Close={completedBarClose:F4} > EMA={effectiveStopLoss:F4}");
                                }
                            }
                        }
                    }
                    else if (EmaStopTriggerMode == EmaStopTriggerModeType.BodyOnly)
                    {
                        // Body only: the FULL body (both bodyTop and bodyBottom) must be above the stop loss
                        // This ensures the entire body range is above the stop, not just part of it
                        double bodyTop = Math.Max(completedBarOpen, completedBarClose);
                        double bodyBottom = Math.Min(completedBarOpen, completedBarClose);
                        // For SHORT exits: FULL body must be above stop loss (both top and bottom must be above)
                        shouldTrigger = bodyTop > effectiveStopLoss && bodyBottom > effectiveStopLoss;
                        
                        // Get current bar conditions for comparison
                        double currentBarClose = Close[0];
                        double currentBarFastEma = fastEma != null && CurrentBar >= FastEmaPeriod ? fastEma[0] : double.NaN;
                        bool currentBarBelowEma = !double.IsNaN(currentBarFastEma) && currentBarClose < currentBarFastEma;
                        double currentGradDeg;
                        GetCurrentGradient(out currentGradDeg);
                        
                        Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: SHORT - BodyOnly mode - Open={completedBarOpen:F4}, Close={completedBarClose:F4}, BodyTop={bodyTop:F4}, BodyBottom={bodyBottom:F4}, StopLoss={effectiveStopLoss:F4}, FullBodyAbove={(bodyTop > effectiveStopLoss && bodyBottom > effectiveStopLoss)}, Trigger={shouldTrigger}");
                        if (shouldTrigger)
                        {
                            Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: NOTE - Exit triggered based on COMPLETED bar [1] conditions, not current bar [0]");
                            Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: Completed bar [1]: BodyTop={bodyTop:F4}, BodyBottom={bodyBottom:F4}, StopLoss={effectiveStopLoss:F4}, FullBodyAbove={(bodyTop > effectiveStopLoss && bodyBottom > effectiveStopLoss)}");
                            Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: Current bar [0]: Close={currentBarClose:F4}, FastEMA={currentBarFastEma:F4}, BelowEMA={currentBarBelowEma}, Gradient={currentGradDeg:F2}°");
                            Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: REMINDER - Exit fill will occur on next bar (bar {CurrentBar + 1}) in historical backtesting. Bar {CurrentBar + 1} conditions will not be checked before fill.");
                            if (currentBarBelowEma && currentGradDeg < 0)
                            {
                                Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: WARNING - Current bar looks good (below EMA, negative gradient), but exit triggered by completed bar's full body above stop loss");
                            }
                        }
                        
                        // Profit protection: if in profit and close crossed above EMA, exit early
                        if (!shouldTrigger && EmaStopProfitProtectionPoints > 0)
                        {
                            double entryPrice = Position.AveragePrice;
                            double currentProfit = entryPrice - completedBarClose;
                            if (currentProfit >= EmaStopProfitProtectionPoints)
                            {
                                // Check if close crossed above EMA (CloseOnly check)
                                bool closeCrossedAbove = completedBarClose > effectiveStopLoss;
                                if (closeCrossedAbove)
                                {
                                    earlyExitDueToProfit = true;
                                    Print($"[EMA_STOP_PROFIT_PROTECTION] Bar {CurrentBar}: SHORT - Early exit triggered! Profit={currentProfit:F4} >= {EmaStopProfitProtectionPoints:F4}, Close={completedBarClose:F4} > EMA={effectiveStopLoss:F4}");
                                }
                            }
                        }
                    }
                    else if (EmaStopTriggerMode == EmaStopTriggerModeType.CloseOnly)
                    {
                        // Close only: just the close needs to be above the stop loss
                        shouldTrigger = completedBarClose > effectiveStopLoss;
                        Print($"[EMA_STOP_CHECK] Bar {CurrentBar}: SHORT - CloseOnly mode - Close={completedBarClose:F4}, StopLoss={effectiveStopLoss:F4}, Trigger={shouldTrigger}");
                    }
                    
                    if (shouldTrigger || earlyExitDueToProfit)
                    {
                        shouldExit = true;
                        string exitReason = earlyExitDueToProfit ? "ProfitProtection" : EmaStopTriggerMode.ToString();
                        Print($"[EMA_STOP_EXIT] Bar {CurrentBar}: SHORT exit triggered - Mode={exitReason}, Effective stop loss={effectiveStopLoss:F4} (EMA={completedBarFastEma:F4}, Entry={Position.AveragePrice:F4})");
                    }
                }
                
                if (shouldExit)
                {
                    if (currentPos == MarketPosition.Long)
                    {
                        currentTradeExitReason = "BarsOnTheFlowEmaStop";
                        lastExitBarIndex = CurrentBar;
                        ExitLongTrace("BarsOnTheFlowEmaStop", GetActiveEntrySignal(), "BarsOnTheFlowEmaStop");
                        intendedPosition = MarketPosition.Flat;
                        RecordExitForCooldown(MarketPosition.Long);

                        if (ReverseOnEmaStop && EnableShorts)
                        {
                            pendingEmaStopReverseShort = true;
                            pendingEmaStopReverseLong = false;
                            pendingEmaStopReverseBar = CurrentBar;
                            pendingEmaStopReverseAllowed = allowShortOnEmaStop;
                            Print($"[EMA_STOP_REVERSE_DEFER] Bar {CurrentBar}: Queued SHORT reversal after EMA stop. allowShortOnEmaStop={allowShortOnEmaStop}");
                        }
                    }
                    else if (currentPos == MarketPosition.Short)
                    {
                        currentTradeExitReason = "BarsOnTheFlowEmaStopS";
                        lastExitBarIndex = CurrentBar;
                        ExitShortTrace("BarsOnTheFlowEmaStopS", GetActiveEntrySignal(), "BarsOnTheFlowEmaStopS");
                        intendedPosition = MarketPosition.Flat;
                        RecordExitForCooldown(MarketPosition.Short);

                        if (ReverseOnEmaStop)
                        {
                            pendingEmaStopReverseLong = true;
                            pendingEmaStopReverseShort = false;
                            pendingEmaStopReverseBar = CurrentBar;
                            pendingEmaStopReverseAllowed = allowLongOnEmaStop;
                            Print($"[EMA_STOP_REVERSE_DEFER] Bar {CurrentBar}: Queued LONG reversal after EMA stop. allowLongOnEmaStop={allowLongOnEmaStop}");
                        }
                    }
                }
                }
            }

            // ====================================================================
            // GRADIENT-BASED STOP LOSS
            // Exit when gradient crosses into unfavorable territory
            // For longs: exit when gradient drops below ExitLongBelowGradient
            // For shorts: exit when gradient rises above ExitShortAboveGradient
            // ====================================================================
            if (UseGradientStopLoss && currentPos != MarketPosition.Flat)
            {
                int barsSinceEntry = lastEntryBarIndex >= 0 ? (CurrentBar - lastEntryBarIndex) : int.MaxValue;

                if (barsSinceEntry < GradientStopMinBarsDelay)
                {
                    Print($"[GRADIENT_STOP_CHECK] Bar {CurrentBar}: Skipped - only {barsSinceEntry} bars since entry (min={GradientStopMinBarsDelay})");
                }
                else
                {
                    double currentGradDeg;
                    GetCurrentGradient(out currentGradDeg);
                    double currentGradient = currentGradDeg;
                    
                    if (currentPos == MarketPosition.Long)
                    {
                        Print($"[GRADIENT_STOP_CHECK] Bar {CurrentBar}: LONG - Gradient={currentGradient:F2}° (recalculated), ExitThreshold={ExitLongBelowGradient:F2}°, BarsInTrade={barsSinceEntry}");
                        
                        if (!double.IsNaN(currentGradient) && currentGradient < ExitLongBelowGradient)
                        {
                            if (ShouldSkipGradientStopForMfeProtection())
                            {
                                // profit floor stop manages exit
                            }
                            else
                            {
                            Print($"[GRADIENT_STOP_EXIT] Bar {CurrentBar}: LONG exit triggered - Gradient ({currentGradient:F2}°) < ExitThreshold ({ExitLongBelowGradient:F2}°) after {barsSinceEntry} bars");
                            currentTradeExitReason = "GradientStopLong";
                            lastExitBarIndex = CurrentBar;
                            ExitLongTrace("GradientStopLong", GetActiveEntrySignal(), "GradientStopLong");
                            intendedPosition = MarketPosition.Flat;
                            RecordExitForCooldown(MarketPosition.Long);
                            deferredLongEntry = false;
                            deferredShortEntry = false;
                            }
                        }
                    }
                    else if (currentPos == MarketPosition.Short)
                    {
                        Print($"[GRADIENT_STOP_CHECK] Bar {CurrentBar}: SHORT - Gradient={currentGradient:F2}° (recalculated), ExitThreshold={ExitShortAboveGradient:F2}°, BarsInTrade={barsSinceEntry}");
                        
                        if (!double.IsNaN(currentGradient) && currentGradient > ExitShortAboveGradient)
                        {
                            if (ShouldSkipGradientStopForMfeProtection())
                            {
                                // profit floor stop manages exit
                            }
                            else
                            {
                            Print($"[GRADIENT_STOP_EXIT] Bar {CurrentBar}: SHORT exit triggered - Gradient ({currentGradient:F2}°) > ExitThreshold ({ExitShortAboveGradient:F2}°) after {barsSinceEntry} bars");
                            currentTradeExitReason = "GradientStopShort";
                            lastExitBarIndex = CurrentBar;
                            ExitShortTrace("GradientStopShort", GetActiveEntrySignal(), "GradientStopShort");
                            intendedPosition = MarketPosition.Flat;
                            RecordExitForCooldown(MarketPosition.Short);
                            deferredLongEntry = false;
                            deferredShortEntry = false;
                            }
                        }
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
                    currentTradeExitReason = "BarsOnTheFlowExitS";
                    lastExitBarIndex = CurrentBar;
                    ExitShortTrace("BarsOnTheFlowExitS", GetActiveEntrySignal(), "BarsOnTheFlowExitS");
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
                    // Get current bar conditions for logging
                    double currentClose = Close[0];
                    double currentFastEma = fastEma != null && CurrentBar >= FastEmaPeriod ? fastEma[0] : double.NaN;
                    double currentGradDeg;
                    GetCurrentGradient(out currentGradDeg);
                    bool barAboveEma = !double.IsNaN(currentFastEma) && currentClose > currentFastEma;
                    
                    Print($"[EXIT_DEBUG] Bar {CurrentBar}: pendingExitLongOnGood resolving - prevBad, exiting LONG");
                    Print($"[EXIT_DEBUG]   Previous bar: O:{prevOpen:F2}, C:{prevClose:F2}, Bad={prevBad}");
                    Print($"[EXIT_DEBUG]   Current bar: Close={currentClose:F2}, FastEMA={currentFastEma:F2}, AboveEMA={barAboveEma}, Gradient={currentGradDeg:F2}°");
                    Print($"[EXIT_DEBUG]   NOTE: This is a deferred exit from a previous bar's marginal trend condition");
                    
                    // Previous bar was bad, confirms reversal - exit now
                    CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                    currentTradeExitReason = "BarsOnTheFlowExit";
                    lastExitBarIndex = CurrentBar;
                    ExitLongTrace("BarsOnTheFlowExit", GetActiveEntrySignal(), "BarsOnTheFlowExit");
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
                    // Recalculate gradient using the bar that just closed ([1]) to ensure we have the most current gradient
                    double currentGradDeg;
                    double currentGradSlope = GetCurrentGradient(out currentGradDeg);
                    double gradDegToUse = currentGradDeg;
                    
                    // CRITICAL: If Fast EMA is below Slow EMA on the bar that just closed, block LONG entry
                    bool emaCrossedBelow = false;
                    if (emaFast != null && emaSlow != null && CurrentBar >= Math.Max(EmaFastPeriod, EmaSlowPeriod))
                    {
                        double fastEmaAtBarClose = emaFast[1];
                        double slowEmaAtBarClose = emaSlow[1];
                        double openAtBarClose = Open[1];
                        double closeAtBarClose = Close[1];
                        double minGapCloseToFast = EmaCrossoverMinTicksCloseToFast * TickSize;
                        double bodyBottom = Math.Min(openAtBarClose, closeAtBarClose);
                        bool bodyAboveFast = bodyBottom >= fastEmaAtBarClose + minGapCloseToFast;

                        emaCrossedBelow = fastEmaAtBarClose < slowEmaAtBarClose;
                        if (emaCrossedBelow && AllowLongWhenBodyAboveFastButFastBelowSlow && bodyAboveFast)
                        {
                            // Allow long if body is above Fast EMA even though Fast EMA is below Slow EMA
                            emaCrossedBelow = false;
                            Print($"[GRADIENT_BLOCK] Bar {CurrentBar} (pendingShortFromGood->LONG): Fast EMA below Slow EMA, but body above Fast EMA - allowing LONG");
                        }
                        else if (emaCrossedBelow)
                        {
                            Print($"[GRADIENT_BLOCK] Bar {CurrentBar} (pendingShortFromGood->LONG): Fast EMA ({fastEmaAtBarClose:F4}) < Slow EMA ({slowEmaAtBarClose:F4}) - BLOCKING LONG entry");
                        }
                    }
                    
                    bool skipDueToGradient = emaCrossedBelow || ShouldSkipLongDueToGradient(gradDegToUse);
                    bool skipDueToEmaCrossover = UseEmaCrossoverFilter && !EmaCrossoverFilterPasses(true);
                    PrintAndLog($"[GRADIENT_CHECK] Bar {CurrentBar}: LONG entry (pendingShortFromGood) | gradient={gradDegToUse:F2}° (recalculated), threshold={SkipLongsBelowGradient:F2}°, GradientFilterEnabled={GradientFilterEnabled}, skipDueToGradient={skipDueToGradient}", "DEBUG");
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
                            currentTradeEntryReason = BuildEntryReason(true, trendUp, trendDown, prevGood, prevBad, skipDueToGradient, skipDueToEmaCrossover, "PendingShortFromGood", gradDegToUse);
                            ResetExitReason(); // Reset exit reason when entering new trade
                            PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering LONG from pendingShortFromGood, CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}", "ENTRY");
                            // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement)
                            SubmitLongEntry();
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
                    // Recalculate gradient using the bar that just closed ([1]) to ensure we have the most current gradient
                    double currentGradDeg;
                    double currentGradSlope = GetCurrentGradient(out currentGradDeg);
                    double gradDegToUse = currentGradDeg;
                    
                    // CRITICAL: Block SHORT entry if close is above Fast EMA OR if Fast EMA is above Slow EMA
                    // This ensures shorts only enter when price is below Fast EMA (bearish condition)
                    bool emaCrossedAbove = false;
                    bool closeAboveFastEma = false;
                    if (emaFast != null && emaSlow != null && CurrentBar >= Math.Max(EmaFastPeriod, EmaSlowPeriod))
                    {
                        double fastEmaAtBarClose = emaFast[1];
                        double slowEmaAtBarClose = emaSlow[1];
                        double closeAtBarClose = Close[1];
                        double openAtBarClose = Open[1];
                        double minGapCloseToFast = EmaCrossoverMinTicksCloseToFast * TickSize;
                        double bodyTop = Math.Max(openAtBarClose, closeAtBarClose);
                        bool bodyBelowFast = bodyTop <= fastEmaAtBarClose - minGapCloseToFast;
                        
                        // Block if Fast EMA > Slow EMA (bearish crossover condition)
                        emaCrossedAbove = fastEmaAtBarClose > slowEmaAtBarClose;
                        if (emaCrossedAbove && AllowShortWhenBodyBelowFastButFastAboveSlow && bodyBelowFast)
                        {
                            // Allow short if body is below Fast EMA even though Fast EMA is above Slow EMA
                            emaCrossedAbove = false;
                            Print($"[GRADIENT_BLOCK] Bar {CurrentBar} (pendingShortFromGood->SHORT): Fast EMA above Slow EMA, but body below Fast EMA - allowing SHORT");
                        }
                        else if (emaCrossedAbove)
                        {
                            Print($"[GRADIENT_BLOCK] Bar {CurrentBar} (pendingShortFromGood->SHORT): Fast EMA ({fastEmaAtBarClose:F4}) > Slow EMA ({slowEmaAtBarClose:F4}) - BLOCKING SHORT entry");
                        }
                        
                        // Block if close is above Fast EMA (price is bullish, not suitable for short entry)
                        closeAboveFastEma = closeAtBarClose > fastEmaAtBarClose;
                        if (closeAboveFastEma)
                        {
                            Print($"[PRICE_BLOCK] Bar {CurrentBar} (pendingShortFromGood->SHORT): Close ({closeAtBarClose:F4}) > Fast EMA ({fastEmaAtBarClose:F4}) - BLOCKING SHORT entry (price is above Fast EMA)");
                        }
                    }
                    
                    bool skipDueToGradient = emaCrossedAbove || closeAboveFastEma || ShouldSkipShortDueToGradient(gradDegToUse);
                    bool skipDueToEmaCrossover = UseEmaCrossoverFilter && !EmaCrossoverFilterPasses(false);
                    PrintAndLog($"[PENDING_SHORT_DEBUG] Bar {CurrentBar}: TrendDown + prevBad, executing pending SHORT | gradient={gradDegToUse:F2}° (recalculated), skipDueToGradient={skipDueToGradient}, skipDueToEmaCrossover={skipDueToEmaCrossover}", "DEBUG");
                    if (!skipDueToGradient && !skipDueToEmaCrossover)
                    {
                        if (intendedPosition == MarketPosition.Short)
                        {
                            PrintAndLog($"[Entry Skip] Bar {CurrentBar}: Already intendedPosition=Short, skipping pendingShortFromGood short entry", "DEBUG");
                        }
                        else
                        {
                            CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                            currentTradeEntryReason = BuildEntryReason(false, trendUp, trendDown, prevGood, prevBad, skipDueToGradient, skipDueToEmaCrossover, "PendingShortFromGood", gradDegToUse);
                            ResetExitReason(); // Reset exit reason when entering new trade
                            PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering SHORT from pendingShortFromGood, CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}", "ENTRY");
                            // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement)
                            SubmitShortEntry();
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
                // If UseDeferredEntry is false, pendingLongFromBad should never be used - clear it
                if (!UseDeferredEntry)
                {
                    Print($"[PENDING_LONG_DEBUG] Bar {CurrentBar}: Clearing pendingLongFromBad - UseDeferredEntry=False, entry blocked");
                    pendingLongFromBad = false;
                    pendingShortFromGood = false;
                }
                else
                {
                    PrintAndLog($"[PENDING_LONG_DEBUG] Bar {CurrentBar}: Resolving pendingLongFromBad | trendUp={trendUp}, trendDown={trendDown}, prevGood={prevGood}, prevBad={prevBad}, gradient={lastFastEmaGradDeg:F2}", "DEBUG");
                
                if (trendDown)
                {
                    // Recalculate gradient using the bar that just closed ([1]) to ensure we have the most current gradient
                    double gradDegToUse;
                    GetCurrentGradient(out gradDegToUse);
                    
                    // Check gradient and EMA crossover filters using helper method
                    bool skipDueToGradient, skipDueToEmaCrossover, emaCrossedAbove, closeAboveFastEma;
                    CheckGradientAndEmaFiltersForShort(gradDegToUse, out skipDueToGradient, out skipDueToEmaCrossover, out emaCrossedAbove, out closeAboveFastEma);
                    PrintAndLog($"[PENDING_LONG_DEBUG] Bar {CurrentBar}: TrendDown detected, reversing to SHORT | gradient={gradDegToUse:F2}° (recalculated), skipDueToGradient={skipDueToGradient}, skipDueToEmaCrossover={skipDueToEmaCrossover}", "DEBUG");
                    if (!skipDueToGradient && !skipDueToEmaCrossover)
                    {
                        if (intendedPosition == MarketPosition.Short)
                        {
                            PrintAndLog($"[Entry Skip] Bar {CurrentBar}: Already intendedPosition=Short, skipping pendingLongFromBad short entry", "DEBUG");
                        }
                        else
                        {
                            CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                            currentTradeEntryReason = BuildEntryReason(false, trendUp, trendDown, prevGood, prevBad, skipDueToGradient, skipDueToEmaCrossover, "PendingLongFromBad", gradDegToUse);
                            ResetExitReason(); // Reset exit reason when entering new trade
                            PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering SHORT from pendingLongFromBad, CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}");
                            // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement)
                            SubmitShortEntry();
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
                    
                    // Recalculate gradient using the bar that just closed ([1]) to ensure we have the most current gradient
                    double gradDegToUse;
                    GetCurrentGradient(out gradDegToUse);
                    
                    Print($"[GRADIENT_RECALC] Bar {CurrentBar} (pendingLongFromBad): Recalculated gradient: {gradDegToUse:F2}° (was {lastFastEmaGradDeg:F2}°)");
                    
                    // Check gradient and EMA crossover filters using helper method
                    bool skipDueToGradient, skipDueToEmaCrossover, emaCrossedBelow, closeBelowFastEma;
                    CheckGradientAndEmaFiltersForLong(gradDegToUse, out skipDueToGradient, out skipDueToEmaCrossover, out emaCrossedBelow, out closeBelowFastEma);
                    Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: LONG entry (pendingLongFromBad)");
                    Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: gradDegToUse={gradDegToUse:F2}° (recalculated from bar that just closed)");
                    Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: SkipLongsBelowGradient={SkipLongsBelowGradient:F2}°");
                    Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: GradientFilterEnabled={GradientFilterEnabled}");
                    Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: double.IsNaN(gradDegToUse)={double.IsNaN(gradDegToUse)}");
                    Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: gradDeg < SkipLongsBelowGradient = {gradDegToUse:F2} < {SkipLongsBelowGradient:F2} = {gradDegToUse < SkipLongsBelowGradient}");
                    PrintAndLog($"[GRADIENT_CHECK] Bar {CurrentBar}: LONG entry (pendingLongFromBad) | gradient={gradDegToUse:F2}° (recalculated), threshold={SkipLongsBelowGradient:F2}°, GradientFilterEnabled={GradientFilterEnabled}, skipDueToGradient={skipDueToGradient}", "DEBUG");
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
                            currentTradeEntryReason = BuildEntryReason(true, trendUp, trendDown, prevGood, prevBad, skipDueToGradient, skipDueToEmaCrossover, "PendingLongFromBad", gradDegToUse);
                            ResetExitReason(); // Reset exit reason when entering new trade
                            PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering LONG from pendingLongFromBad, CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}");
                            // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement)
                            SubmitLongEntry();
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
                } // Close else block for UseDeferredEntry check
            }

            // Fresh signals with deferral logic.
            Print($"[ENTRY_DEBUG] Bar {CurrentBar}: Checking trend signals - trendUp={trendUp}, trendDown={trendDown}, currentPos={currentPos}, placedEntry={placedEntry}");
            
            if (trendUp)
            {
                Print($"[ENTRY_DEBUG] Bar {CurrentBar}: trendUp=True - checking entry conditions");
                Print($"[ENTRY_DEBUG] Bar {CurrentBar}: currentPos={currentPos}, Position.MarketPosition={Position.MarketPosition}, Position.Quantity={Position.Quantity}");
                Print($"[ENTRY_DEBUG] Bar {CurrentBar}: placedEntry={placedEntry}, allowLongThisBar={allowLongThisBar}");
                
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
                            currentTradeExitReason = "BarsOnTheFlowExitS";
                            lastExitBarIndex = CurrentBar;
                            ExitShortTrace("BarsOnTheFlowExitS", GetActiveEntrySignal(), "BarsOnTheFlowExitS");
                            RecordExitForCooldown(MarketPosition.Short);
                        }
                        else
                        {
                            Print($"[EXIT_DEBUG] Bar {CurrentBar}: TrendUp break - exiting SHORT and REVERSING to LONG");
                            
                            // Recalculate gradient for entry reason (even though gradient filter is bypassed for reversals)
                            double currentGradDeg;
                            double currentGradSlope = GetCurrentGradient(out currentGradDeg);
                            double gradDegToUse = currentGradDeg;
                            
                            // ReverseOnTrendBreak overrides gradient filter, but EMA crossover filter still applies
                            PrintAndLog($"[GRADIENT_CHECK] Bar {CurrentBar}: LONG entry (reverse from short) | gradient={gradDegToUse:F2}° (recalculated), threshold={SkipLongsBelowGradient:F2}°, GradientFilterEnabled={GradientFilterEnabled}, BYPASSED (ReverseOnTrendBreak={ReverseOnTrendBreak})", "DEBUG");
                            
                            // Check EMA crossover filter (still applies even during reversals)
                            bool skipDueToEmaCrossover = UseEmaCrossoverFilter && !EmaCrossoverFilterPasses(true);
                            PrintAndLog($"[EMA_CROSSOVER_CHECK] Bar {CurrentBar}: LONG entry (reverse from short) | UseEmaCrossoverFilter={UseEmaCrossoverFilter}, EnableBarsOnTheFlowTrendDetection={EnableBarsOnTheFlowTrendDetection}, skipDueToEmaCrossover={skipDueToEmaCrossover}", "DEBUG");
                            
                            currentTradeExitReason = "BarsOnTheFlowExitS";
                            lastExitBarIndex = CurrentBar;
                            ExitShortTrace("BarsOnTheFlowExitS", GetActiveEntrySignal(), "BarsOnTheFlowExitS");
                            RecordExitForCooldown(MarketPosition.Short);
                            if (intendedPosition != MarketPosition.Long && !skipDueToEmaCrossover)
                            {
                                currentTradeEntryReason = BuildEntryReason(true, trendUp, trendDown, prevGood, prevBad, false, skipDueToEmaCrossover, "ReverseFromShort", gradDegToUse);
                                ResetExitReason(); // Reset exit reason when entering new trade
                                PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering LONG (reverse from short), CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}");
                                // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement) - even during reversals
                                SubmitLongEntry();
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
                        currentTradeExitReason = "BarsOnTheFlowExitS";
                        lastExitBarIndex = CurrentBar;
                        ExitShortTrace("BarsOnTheFlowExitS", GetActiveEntrySignal(), "BarsOnTheFlowExitS");
                        RecordExitForCooldown(MarketPosition.Short);
                    }
                }

                // Check entry conditions for LONG when flat
                Print($"[ENTRY_DEBUG] Bar {CurrentBar}: Checking LONG entry block - placedEntry={placedEntry}, currentPos={currentPos}, Position.MarketPosition={Position.MarketPosition}, Position.Quantity={Position.Quantity}");
                Print($"[ENTRY_DEBUG] Bar {CurrentBar}: Entry block condition: !placedEntry={!placedEntry}, currentPos==Flat={currentPos == MarketPosition.Flat}, Position.Quantity==0={Position.Quantity == 0}");
                
                // Use Position.Quantity for more reliable flat check (handles historical playback issues)
                bool isActuallyFlat = Position.Quantity == 0;
                
                if (!placedEntry && isActuallyFlat)
                {
                    Print($"[ENTRY_DEBUG] Bar {CurrentBar}: ENTRY BLOCK REACHED - checking LONG entry conditions");
                    if (!EntryFiltersPass(out string entryFilterReasonLong))
                    {
                        Print($"[ENTRY_DEBUG] Bar {CurrentBar}: LONG entry blocked - {entryFilterReasonLong}");
                    }
                    else
                    if (AvoidLongsOnBadCandle && prevBad)
                    {
                        if (UseDeferredEntry)
                        {
                            Print($"[Entry Block] Bar {CurrentBar}: Deferring LONG - bar closed BAD (O:{prevOpen:F2}, C:{prevClose:F2}) - will wait for good candle");
                            pendingLongFromBad = true;
                            pendingShortFromGood = false;
                        }
                        else
                        {
                            Print($"[Entry Block] Bar {CurrentBar}: BLOCKING LONG - bar closed BAD (O:{prevOpen:F2}, C:{prevClose:F2}) and UseDeferredEntry=False (no deferral, entry blocked)");
                            pendingLongFromBad = false;
                            pendingShortFromGood = false;
                        }
                    }
                    else if (allowLongThisBar)
                    {
                        bool currentBarIsBad = IsCurrentBarDownByMinTicks();
                        if (AvoidLongsOnBadCandleCurrentBar && currentBarIsBad)
                        {
                            Print($"[ENTRY_DEBUG] Bar {CurrentBar}: LONG entry blocked - current bar down by >= {MinTicksCurrentBarDirection} ticks (Close[0]={Close[0]:F4} < Open[0]={Open[0]:F4}) and AvoidLongsOnBadCandleCurrentBar=True");
                        }
                        else
                        {
                        Print($"[ENTRY_DEBUG] Bar {CurrentBar}: allowLongThisBar=True - proceeding with LONG entry checks");
                        Print($"[ENTRY_DEBUG] Bar {CurrentBar}: No bad candle block - checking gradient and EMA filters");
                        
                        // Recalculate gradient using the bar that just closed ([1]) to ensure we have the most current gradient
                        // This is important because the gradient calculated at the start of the bar may not reflect the bar's final movement
                        double gradDegToUse;
                        GetCurrentGradient(out gradDegToUse);
                        
                        Print($"[GRADIENT_RECALC] Bar {CurrentBar}: Recalculated gradient for entry decision: {gradDegToUse:F2}° (was {lastFastEmaGradDeg:F2}°)");
                        Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: LONG entry check");
                        Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: gradDegToUse={gradDegToUse:F2}° (recalculated from bar that just closed)");
                        Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: SkipLongsBelowGradient={SkipLongsBelowGradient:F2}°");
                        Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: GradientFilterEnabled={GradientFilterEnabled}");
                        Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: double.IsNaN(gradDegToUse)={double.IsNaN(gradDegToUse)}");
                        Print($"[GRADIENT_CHECK_DETAIL] Bar {CurrentBar}: gradDeg < SkipLongsBelowGradient = {gradDegToUse:F2} < {SkipLongsBelowGradient:F2} = {gradDegToUse < SkipLongsBelowGradient}");
                        
                        // Check gradient and EMA crossover filters using helper method
                        bool skipDueToGradient, skipDueToEmaCrossover, emaCrossedBelow, closeBelowFastEma;
                        CheckGradientAndEmaFiltersForLong(gradDegToUse, out skipDueToGradient, out skipDueToEmaCrossover, out emaCrossedBelow, out closeBelowFastEma);
                        
                        PrintAndLog($"[GRADIENT_CHECK] Bar {CurrentBar}: LONG entry check | gradient={gradDegToUse:F2}° (recalculated), threshold={SkipLongsBelowGradient:F2}°, GradientFilterEnabled={GradientFilterEnabled}, skipDueToGradient={skipDueToGradient}", "DEBUG");
                        PrintAndLog($"[EMA_CROSSOVER_CHECK] Bar {CurrentBar}: LONG entry check | UseEmaCrossoverFilter={UseEmaCrossoverFilter}, EnableBarsOnTheFlowTrendDetection={EnableBarsOnTheFlowTrendDetection}, skipDueToEmaCrossover={skipDueToEmaCrossover}", "DEBUG");
                        
                        Print($"[ENTRY_DECISION] Bar {CurrentBar}: skipDueToGradient={skipDueToGradient}, skipDueToEmaCrossover={skipDueToEmaCrossover}, willEnter={!skipDueToGradient && !skipDueToEmaCrossover}");
                        
                        if (!skipDueToGradient && !skipDueToEmaCrossover)
                        {
                            Print($"[ENTRY_DEBUG] Bar {CurrentBar}: All filters passed! skipDueToGradient={skipDueToGradient}, skipDueToEmaCrossover={skipDueToEmaCrossover}");
                            Print($"[ENTRY_DEBUG] Bar {CurrentBar}: Position check - Position.MarketPosition={Position.MarketPosition}, Position.Quantity={Position.Quantity}, intendedPosition={intendedPosition}");
                            // Don't re-enter if already long - check both actual and intended position
                            if (Position.Quantity == 0 && intendedPosition != MarketPosition.Long)
                            {
                                if (UseDeferredEntry)
                                {
                                    // DEFER ENTRY: Instead of entering immediately, defer to next bar for validation
                                    // This ensures we check the bar where the entry will actually appear
                                    string entryReason = BuildEntryReason(true, trendUp, trendDown, prevGood, prevBad, skipDueToGradient, skipDueToEmaCrossover, "FreshSignal", gradDegToUse);
                                    Print($"[DEFERRED_ENTRY_SET] Bar {CurrentBar}: Deferring LONG entry to next bar for validation. Reason: {entryReason}");
                                    deferredLongEntry = true;
                                    deferredShortEntry = false;
                                    deferredEntryReason = entryReason;
                                    intendedPosition = MarketPosition.Long; // Mark intent to prevent duplicate signals
                                    pendingLongFromBad = false;
                                    pendingShortFromGood = false;
                                }
                                else
                                {
                                    // IMMEDIATE ENTRY: Enter right away without deferring
                                    string entryReason = BuildEntryReason(true, trendUp, trendDown, prevGood, prevBad, skipDueToGradient, skipDueToEmaCrossover, "FreshSignal", gradDegToUse);
                                    Print($"[IMMEDIATE_ENTRY] Bar {CurrentBar}: Entering LONG immediately (UseDeferredEntry=False). Reason: {entryReason}");
                                    CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                                    currentTradeEntryReason = entryReason;
                                    ResetExitReason();
                                    PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering LONG, CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={entryReason}");
                                    SubmitLongEntry();
                                    lastEntryBarIndex = CurrentBar;
                                    lastEntryDirection = MarketPosition.Long;
                                    intendedPosition = MarketPosition.Long;
                                    placedEntry = true;
                                    pendingLongFromBad = false;
                                    pendingShortFromGood = false;
                                }
                            }
                            else
                            {
                                Print($"[ENTRY_DEBUG] Bar {CurrentBar}: Entry blocked - Position.Quantity={Position.Quantity}, intendedPosition={intendedPosition}");
                                Print($"[Entry Skip] Bar {CurrentBar}: Already LONG (actual={Position.MarketPosition}, intended={intendedPosition}), not re-entering");
                            }
                        }
                        else if (AllowMidBarGradientEntry && skipDueToGradient)
                        {
                            Print($"[ENTRY_DEBUG] Bar {CurrentBar}: Gradient blocked but AllowMidBarGradientEntry=True - waiting for mid-bar cross");
                            // All conditions met except gradient - wait for mid-bar cross
                            waitingForLongGradient = true;
                        }
                        else
                        {
                            Print($"[ENTRY_DEBUG] Bar {CurrentBar}: Entry blocked by filters - skipDueToGradient={skipDueToGradient}, skipDueToEmaCrossover={skipDueToEmaCrossover}, AllowMidBarGradientEntry={AllowMidBarGradientEntry}");
                        }
                        }
                    }
                    else
                    {
                        Print($"[ENTRY_DEBUG] Bar {CurrentBar}: Entry blocked - allowLongThisBar={allowLongThisBar}");
                    }
                }
                else
                {
                    Print($"[ENTRY_DEBUG] Bar {CurrentBar}: Entry block SKIPPED - placedEntry={placedEntry}, isActuallyFlat={isActuallyFlat}, Position.Quantity={Position.Quantity}");
                }
            }
            else if (trendDown)
            {
                Print($"[ENTRY_DEBUG] Bar {CurrentBar}: trendDown=True - checking entry conditions");
                Print($"[ENTRY_DEBUG] Bar {CurrentBar}: currentPos={currentPos}, Position.MarketPosition={Position.MarketPosition}, Position.Quantity={Position.Quantity}");
                Print($"[ENTRY_DEBUG] Bar {CurrentBar}: placedEntry={placedEntry}, allowShortThisBar={allowShortThisBar}");
                Print($"[ENTRY_DEBUG] Bar {CurrentBar}: EnableBarsOnTheFlowTrendDetection={EnableBarsOnTheFlowTrendDetection}, ExitOnTrendBreak={ExitOnTrendBreak}");
                
                if (currentPos == MarketPosition.Long)
                {
                    Print($"[ENTRY_DEBUG] Bar {CurrentBar}: In LONG position with trendDown=True - checking exit conditions");
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
                            currentTradeExitReason = "BarsOnTheFlowExit";
                            lastExitBarIndex = CurrentBar;
                            ExitLongTrace("BarsOnTheFlowExit", GetActiveEntrySignal(), "BarsOnTheFlowExit");
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
                            
                            // Recalculate gradient for entry reason (even though gradient filter is bypassed for reversals)
                            double currentGradDeg;
                            double currentGradSlope = GetCurrentGradient(out currentGradDeg);
                            double gradDegToUse = currentGradDeg;
                            
                            currentTradeExitReason = "BarsOnTheFlowExit";
                            lastExitBarIndex = CurrentBar;
                            ExitLongTrace("BarsOnTheFlowExit", GetActiveEntrySignal(), "BarsOnTheFlowExit");
                            if (intendedPosition != MarketPosition.Short && !skipDueToEmaCrossover)
                            {
                                currentTradeEntryReason = BuildEntryReason(false, trendUp, trendDown, prevGood, prevBad, false, skipDueToEmaCrossover, "ReverseFromLong", gradDegToUse);
                                ResetExitReason(); // Reset exit reason when entering new trade
                                PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering SHORT (reverse from long), CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}");
                                // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement) - even during reversals
                                SubmitShortEntry();
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
                        currentTradeExitReason = "BarsOnTheFlowExit";
                        lastExitBarIndex = CurrentBar;
                        ExitLongTrace("BarsOnTheFlowExit", GetActiveEntrySignal(), "BarsOnTheFlowExit");
                        RecordExitForCooldown(MarketPosition.Long);
                    }
                }

                // Check entry conditions for SHORT when flat
                Print($"[ENTRY_DEBUG] Bar {CurrentBar}: Checking SHORT entry block - placedEntry={placedEntry}, currentPos={currentPos}, Position.MarketPosition={Position.MarketPosition}, Position.Quantity={Position.Quantity}");
                Print($"[ENTRY_DEBUG] Bar {CurrentBar}: Entry block condition: !placedEntry={!placedEntry}, currentPos==Flat={currentPos == MarketPosition.Flat}, Position.Quantity==0={Position.Quantity == 0}");
                
                // Use Position.Quantity for more reliable flat check (handles historical playback issues)
                bool isActuallyFlat = Position.Quantity == 0;
                
                if (!placedEntry && isActuallyFlat)
                {
                    Print($"[ENTRY_DEBUG] Bar {CurrentBar}: ENTRY BLOCK REACHED - checking SHORT entry conditions");
                    Print($"[SHORT_ENTRY_DEBUG] Bar {CurrentBar}: Checking SHORT entry - trendDown={trendDown}, prevBad={prevBad}, prevGood={prevGood}, allowShortThisBar={allowShortThisBar}, AvoidShortsOnGoodCandle={AvoidShortsOnGoodCandle}");
                    
                    if (!EntryFiltersPass(out string entryFilterReasonShort))
                    {
                        Print($"[ENTRY_DEBUG] Bar {CurrentBar}: SHORT entry blocked - {entryFilterReasonShort}");
                    }
                    else if (AvoidShortsOnGoodCandle && prevGood)
                    {
                        if (UseDeferredEntry)
                        {
                            Print($"[Entry Block] Bar {CurrentBar}: Deferring SHORT - bar closed GOOD (O:{prevOpen:F2}, C:{prevClose:F2}) - will wait for bad candle");
                            pendingShortFromGood = true;
                            pendingLongFromBad = false;
                        }
                        else
                        {
                            Print($"[Entry Block] Bar {CurrentBar}: BLOCKING SHORT - bar closed GOOD (O:{prevOpen:F2}, C:{prevClose:F2}) and UseDeferredEntry=False (no deferral, entry blocked)");
                            pendingShortFromGood = false;
                            pendingLongFromBad = false;
                        }
                    }
                    else if (allowShortThisBar)
                        {
                            bool currentBarIsGood = IsCurrentBarUpByMinTicks();
                            if (AvoidShortsOnGoodCandleCurrentBar && currentBarIsGood)
                            {
                                Print($"[ENTRY_DEBUG] Bar {CurrentBar}: SHORT entry blocked - current bar up by >= {MinTicksCurrentBarDirection} ticks (Close[0]={Close[0]:F4} > Open[0]={Open[0]:F4}) and AvoidShortsOnGoodCandleCurrentBar=True");
                            }
                            else
                            {
                            // Recalculate gradient using the bar that just closed ([1]) to ensure we have the most current gradient
                            double gradDegToUse;
                            GetCurrentGradient(out gradDegToUse);
                            
                            Print($"[GRADIENT_RECALC] Bar {CurrentBar}: Recalculated gradient for SHORT entry decision: {gradDegToUse:F2}° (was {lastFastEmaGradDeg:F2}°)");
                            
                            // Check gradient and EMA crossover filters using helper method
                            bool skipDueToGradient, skipDueToEmaCrossover, emaCrossedAbove, closeAboveFastEma;
                            CheckGradientAndEmaFiltersForShort(gradDegToUse, out skipDueToGradient, out skipDueToEmaCrossover, out emaCrossedAbove, out closeAboveFastEma);
                            
                            PrintAndLog($"[GRADIENT_CHECK] Bar {CurrentBar}: SHORT entry check | gradient={gradDegToUse:F2}° (recalculated), threshold={SkipShortsAboveGradient:F2}°, GradientFilterEnabled={GradientFilterEnabled}, skipDueToGradient={skipDueToGradient}", "DEBUG");
                            PrintAndLog($"[EMA_CROSSOVER_CHECK] Bar {CurrentBar}: SHORT entry check | UseEmaCrossoverFilter={UseEmaCrossoverFilter}, EnableBarsOnTheFlowTrendDetection={EnableBarsOnTheFlowTrendDetection}, skipDueToEmaCrossover={skipDueToEmaCrossover}", "DEBUG");
                            
                            Print($"[SHORT_ENTRY_DEBUG] Bar {CurrentBar}: trendDown={trendDown}, prevBad={prevBad}, allowShortThisBar={allowShortThisBar}, skipDueToGradient={skipDueToGradient}, skipDueToEmaCrossover={skipDueToEmaCrossover}, Position={Position.MarketPosition}, intendedPosition={intendedPosition}");
                            
                            if (!skipDueToGradient && !skipDueToEmaCrossover)
                            {
                                Print($"[ENTRY_DEBUG] Bar {CurrentBar}: All filters passed! skipDueToGradient={skipDueToGradient}, skipDueToEmaCrossover={skipDueToEmaCrossover}");
                                Print($"[ENTRY_DEBUG] Bar {CurrentBar}: Position check - Position.MarketPosition={Position.MarketPosition}, Position.Quantity={Position.Quantity}, intendedPosition={intendedPosition}");
                                // Don't re-enter if already short - check both actual and intended position
                                if (Position.Quantity == 0 && intendedPosition != MarketPosition.Short)
                                {
                                    if (UseDeferredEntry)
                                    {
                                        // DEFER ENTRY: Instead of entering immediately, defer to next bar for validation
                                        // This ensures we check the bar where the entry will actually appear
                                        string entryReason = BuildEntryReason(false, trendUp, trendDown, prevGood, prevBad, skipDueToGradient, skipDueToEmaCrossover, "FreshSignal", gradDegToUse);
                                        Print($"[DEFERRED_ENTRY_SET] Bar {CurrentBar}: Deferring SHORT entry to next bar for validation. Reason: {entryReason}");
                                        deferredShortEntry = true;
                                        deferredLongEntry = false;
                                        deferredEntryReason = entryReason;
                                        intendedPosition = MarketPosition.Short; // Mark intent to prevent duplicate signals
                                        pendingShortFromGood = false;
                                        pendingLongFromBad = false;
                                    }
                                    else
                                    {
                                        // IMMEDIATE ENTRY: Enter right away without deferring
                                        string entryReason = BuildEntryReason(false, trendUp, trendDown, prevGood, prevBad, skipDueToGradient, skipDueToEmaCrossover, "FreshSignal", gradDegToUse);
                                        Print($"[IMMEDIATE_ENTRY] Bar {CurrentBar}: Entering SHORT immediately (UseDeferredEntry=False). Reason: {entryReason}");
                                        CaptureDecisionContext(prevOpen, prevClose, allowLongThisBar, allowShortThisBar, trendUp, trendDown);
                                        currentTradeEntryReason = entryReason;
                                        ResetExitReason();
                                        PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering SHORT, CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={entryReason}");
                                        SubmitShortEntry();
                                        lastEntryBarIndex = CurrentBar;
                                        lastEntryDirection = MarketPosition.Short;
                                        intendedPosition = MarketPosition.Short;
                                        placedEntry = true;
                                        pendingShortFromGood = false;
                                        pendingLongFromBad = false;
                                    }
                                }
                                else
                                {
                                    Print($"[ENTRY_DEBUG] Bar {CurrentBar}: Entry blocked - Position.Quantity={Position.Quantity}, intendedPosition={intendedPosition}");
                                    Print($"[Entry Skip] Bar {CurrentBar}: Already SHORT (actual={Position.MarketPosition}, intended={intendedPosition}), not re-entering");
                                }
                            }
                            else if (AllowMidBarGradientEntry && skipDueToGradient)
                            {
                                Print($"[ENTRY_DEBUG] Bar {CurrentBar}: Gradient blocked but AllowMidBarGradientEntry=True - waiting for mid-bar cross");
                                // All conditions met except gradient - wait for mid-bar cross
                                waitingForShortGradient = true;
                            }
                            else
                            {
                                Print($"[ENTRY_DEBUG] Bar {CurrentBar}: Entry blocked by filters - skipDueToGradient={skipDueToGradient}, skipDueToEmaCrossover={skipDueToEmaCrossover}, AllowMidBarGradientEntry={AllowMidBarGradientEntry}");
                            }
                            }
                        }
                        else
                        {
                            Print($"[ENTRY_DEBUG] Bar {CurrentBar}: Entry blocked - allowShortThisBar={allowShortThisBar} or conditions not met");
                        }
                }
                else
                {
                    Print($"[ENTRY_DEBUG] Bar {CurrentBar}: SHORT entry block SKIPPED - placedEntry={placedEntry}, isActuallyFlat={isActuallyFlat}, Position.Quantity={Position.Quantity}");
                }
            }
            // ExitOnTrendBreak only works when BarsOnTheFlow trend detection is enabled
            else if (EnableBarsOnTheFlowTrendDetection && ExitOnTrendBreak && currentPos == MarketPosition.Long)
            {
                int barsSinceEntryLong = lastEntryBarIndex >= 0 ? (CurrentBar - lastEntryBarIndex) : 0;
                if (barsSinceEntryLong >= MinBarsBeforeTrendBreakExit)
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
                            ExitLongTrace("BarsOnTheFlowExit", GetActiveEntrySignal(), "BarsOnTheFlowExit");
                            RecordExitForCooldown(MarketPosition.Long);
                        }
                        else
                        {
                            // Recalculate gradient for entry reason (even though gradient filter is bypassed for reversals)
                            double currentGradDeg;
                            double currentGradSlope = GetCurrentGradient(out currentGradDeg);
                            double gradDegToUse = currentGradDeg;
                            
                            // ReverseOnTrendBreak overrides gradient filter, but EMA crossover filter still applies
                            Print($"[EXIT_DEBUG] Bar {CurrentBar}: ExitOnTrendBreak - exiting LONG and REVERSING to SHORT");
                            Print($"[Reverse Debug] Bar {CurrentBar}: REVERSING to short! (gradient filter overridden, gradient={gradDegToUse:F2}° recalculated)");
                            
                            // Check EMA crossover filter (still applies even during reversals)
                            bool skipDueToEmaCrossover = UseEmaCrossoverFilter && !EmaCrossoverFilterPasses(false);
                            PrintAndLog($"[EMA_CROSSOVER_CHECK] Bar {CurrentBar}: SHORT entry (ExitOnTrendBreak reversal) | UseEmaCrossoverFilter={UseEmaCrossoverFilter}, EnableBarsOnTheFlowTrendDetection={EnableBarsOnTheFlowTrendDetection}, skipDueToEmaCrossover={skipDueToEmaCrossover}", "DEBUG");
                            
                            currentTradeExitReason = "BarsOnTheFlowExit";
                            lastExitBarIndex = CurrentBar;
                            ExitLongTrace("BarsOnTheFlowExit", GetActiveEntrySignal(), "BarsOnTheFlowExit");
                            RecordExitForCooldown(MarketPosition.Long);
                            if (intendedPosition != MarketPosition.Short && !skipDueToEmaCrossover)
                            {
                                currentTradeEntryReason = BuildEntryReason(false, trendUp, trendDown, prevGood, prevBad, false, skipDueToEmaCrossover, "ExitOnTrendBreakReversal", gradDegToUse);
                                ResetExitReason(); // Reset exit reason when entering new trade
                                PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering SHORT (ExitOnTrendBreak reversal), CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}");
                                // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement) - even during reversals
                                SubmitShortEntry();
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
                        ExitLongTrace("BarsOnTheFlowExit", GetActiveEntrySignal(), "BarsOnTheFlowExit");
                        RecordExitForCooldown(MarketPosition.Long);
                    }
                }
                }
            }
            // ExitOnTrendBreak only works when BarsOnTheFlow trend detection is enabled
            else if (EnableBarsOnTheFlowTrendDetection && ExitOnTrendBreak && currentPos == MarketPosition.Short)
            {
                int barsSinceEntryShort = lastEntryBarIndex >= 0 ? (CurrentBar - lastEntryBarIndex) : 0;
                if (barsSinceEntryShort >= MinBarsBeforeTrendBreakExit)
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
                            ExitShortTrace("BarsOnTheFlowExitS", GetActiveEntrySignal(), "BarsOnTheFlowExitS");
                            RecordExitForCooldown(MarketPosition.Short);
                        }
                        else
                        {
                            // Recalculate gradient for entry reason (even though gradient filter is bypassed for reversals)
                            double currentGradDeg;
                            double currentGradSlope = GetCurrentGradient(out currentGradDeg);
                            double gradDegToUse = currentGradDeg;
                            
                            // ReverseOnTrendBreak overrides gradient filter, but EMA crossover filter still applies
                            Print($"[EXIT_DEBUG] Bar {CurrentBar}: ExitOnTrendBreak - exiting SHORT and REVERSING to LONG");
                            Print($"[Reverse Debug] Bar {CurrentBar}: REVERSING to long! (gradient filter overridden, gradient={gradDegToUse:F2}° recalculated)");
                            
                            // Check EMA crossover filter (still applies even during reversals)
                            bool skipDueToEmaCrossover = UseEmaCrossoverFilter && !EmaCrossoverFilterPasses(true);
                            PrintAndLog($"[EMA_CROSSOVER_CHECK] Bar {CurrentBar}: LONG entry (ExitOnTrendBreak reversal) | UseEmaCrossoverFilter={UseEmaCrossoverFilter}, EnableBarsOnTheFlowTrendDetection={EnableBarsOnTheFlowTrendDetection}, skipDueToEmaCrossover={skipDueToEmaCrossover}", "DEBUG");
                            
                            currentTradeExitReason = "BarsOnTheFlowExitS";
                            lastExitBarIndex = CurrentBar;
                            ExitShortTrace("BarsOnTheFlowExitS", GetActiveEntrySignal(), "BarsOnTheFlowExitS");
                            RecordExitForCooldown(MarketPosition.Short);
                            if (intendedPosition != MarketPosition.Long && !skipDueToEmaCrossover)
                            {
                                currentTradeEntryReason = BuildEntryReason(true, trendUp, trendDown, prevGood, prevBad, false, skipDueToEmaCrossover, "ExitOnTrendBreakReversal", gradDegToUse);
                                ResetExitReason(); // Reset exit reason when entering new trade
                                PrintAndLog($"[ENTRY] Bar {CurrentBar}: Entering LONG (ExitOnTrendBreak reversal), CurrentPos={Position.Quantity}, Contracts={Contracts}, EntryReason={currentTradeEntryReason}");
                                // CRITICAL: Set stop loss BEFORE entering (NinjaTrader requirement) - even during reversals
                                SubmitLongEntry();
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
                        ExitShortTrace("BarsOnTheFlowExitS", GetActiveEntrySignal(), "BarsOnTheFlowExitS");
                        RecordExitForCooldown(MarketPosition.Short);
                    }
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
            try
            {
                UpdateStrategyState();
                lastStateUpdateTime = DateTime.Now;
                lastStateUpdatePrice = Close[0];
            }
            catch (Exception ex)
            {
                Print($"[ProcessDatabaseOperations] Bar {CurrentBar}: ✗ Error in UpdateStrategyState: {ex.GetType().Name}: {ex.Message}");
            }

            // Optionally stream diagnostics (incl. gradient) to dashboard
            try
            {
                if (EnableDashboardDiagnostics)
                {
                    bool allowShortThisBar = !(AvoidShortsOnGoodCandle && Close[1] > Open[1]);
                    bool allowLongThisBar = !(AvoidLongsOnBadCandle && Close[1] < Open[1]);
                    SendDashboardDiag(allowLongThisBar, allowShortThisBar);
                }
            }
            catch (Exception ex)
            {
                Print($"[ProcessDatabaseOperations] Bar {CurrentBar}: ✗ Error in SendDashboardDiag: {ex.GetType().Name}: {ex.Message}");
            }

            // Record bar sample for volatility database
            // - Historical mode: Stores in memory (no HTTP), batch sent when transitioning to real-time
            // - Real-time mode: Sends via HTTP (bars come slowly, server can handle it)
            if (CurrentBar >= 1)
            {
                // Log progress periodically
                if (CurrentBar <= 10 || CurrentBar % 500 == 0)
                {
                    int memCount = 0;
                    lock (historicalBarDataLock)
                    {
                        memCount = historicalBarDataList.Count;
                    }
                    Print($"[BAR_RECORD] Bar {CurrentBar}: Calling RecordBarSample() (State={State}, memory={memCount} bars)");
                }
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
            if (TradeRealtimeOnly && State != State.Realtime)
                return;

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
            ReinforceProfitFloorStopIfNeeded();

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
            if (TradeRealtimeOnly && State != State.Realtime)
                return;

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
                    { "TradeRealtimeOnly", TradeRealtimeOnly },
                    
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
                    { "EmaCrossoverRequireBodyBelow", EmaCrossoverRequireBodyBelow },
                    { "AllowLongWhenBodyAboveFastButFastBelowSlow", AllowLongWhenBodyAboveFastButFastBelowSlow },
                    { "AllowShortWhenBodyBelowFastButFastAboveSlow", AllowShortWhenBodyBelowFastButFastAboveSlow },
                    
                    // Entry/Exit parameters
                    { "DisableRealTimeTrading", DisableRealTimeTrading },
                    { "EnableBarsOnTheFlowTrendDetection", EnableBarsOnTheFlowTrendDetection },
                    { "TrendLookbackBars", TrendLookbackBars },
                    { "MinMatchingBars", MinMatchingBars },
                    { "UsePnLTiebreaker", UsePnLTiebreaker },
                    { "ReverseOnTrendBreak", ReverseOnTrendBreak },
                    { "ExitIfEntryBarOpposite", ExitIfEntryBarOpposite },
                    { "UseDeferredEntry", UseDeferredEntry },
                    { "StopLossPoints", StopLossPoints },
                    { "UseTrailingStop", UseTrailingStop },
                    { "UseDynamicStopLoss", UseDynamicStopLoss },
                    { "DynamicStopLookback", DynamicStopLookback },
                    { "DynamicStopMultiplier", DynamicStopMultiplier },
                    { "UseEmaTrailingStop", UseEmaTrailingStop },
                    { "EmaStopTriggerMode", EmaStopTriggerMode.ToString() },
                    { "EmaStopProfitProtectionPoints", EmaStopProfitProtectionPoints },
                    { "EmaStopMinDistanceFromEntry", EmaStopMinDistanceFromEntry },
                    { "EmaStopActivationDelayBars", EmaStopActivationDelayBars },
                    { "EmaStopMinProfitBeforeActivation", EmaStopMinProfitBeforeActivation },
                    { "ReverseOnEmaStop", ReverseOnEmaStop },
                    { "UseGradientStopLoss", UseGradientStopLoss },
                    { "ExitLongBelowGradient", ExitLongBelowGradient },
                    { "ExitShortAboveGradient", ExitShortAboveGradient },
                    { "ExitOnRetrace", ExitOnRetrace },
                    { "TrendRetraceFraction", TrendRetraceFraction },
                    { "DelayRetraceExitOnBullishEma", DelayRetraceExitOnBullishEma },
                    { "DelayRetraceExitOnFavorableGradient", DelayRetraceExitOnFavorableGradient },
                    
                    // Mid-bar parameters
                    { "AllowMidBarGradientEntry", AllowMidBarGradientEntry },
                    { "AllowMidBarGradientExit", AllowMidBarGradientExit },
                    
                    // Debug/Display parameters
                    { "ShowBarIndexLabels", ShowBarIndexLabels },
                    { "ShowFastGradLabels", ShowFastGradLabels },
                    { "EnableDashboardDiagnostics", EnableDashboardDiagnostics },
                    { "DashboardBaseUrl", DashboardBaseUrl },
                    { "DashboardAsyncHistorical", DashboardAsyncHistorical },
                    { "RecordBarSamplesInHistorical", RecordBarSamplesInHistorical },
                    { "BarSampleDelayMs", BarSampleDelayMs },
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
            // Calculate label positions to stack vertically on the completed bar (barsAgo=1)
            // All labels should be on the same bar to avoid overlap and match the logged bar index
            double labelBaseY = High[1] + (8 * TickSize); // Start just above the completed bar
            int labelStackOffset = 0; // Track vertical offset for stacking labels
            
            // Draw bar index label on the completed bar (barsAgo=1) to match logged bar index
            if (showBarIndexLabels && CurrentBar >= 1)
            {
                int completedBarIndex = CurrentBar - 1; // Bar that just closed
                string tag = "BarLabel_" + completedBarIndex;
                double barIndexY = labelBaseY + (labelStackOffset * 12 * TickSize);
                labelStackOffset++; // Move offset for next label
                Draw.Text(this, tag, completedBarIndex.ToString(), 1, barIndexY, Brushes.Black); // barsAgo = 1
            }

            // EMA Crossover Labels - shows visual indicators for EMA crossover conditions
            // Positioned right below bar label, above the bar
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
                        // Position right below bar label, above the bar
                        double circleY = labelBaseY + (labelStackOffset * 12 * TickSize);
                        labelStackOffset++; // Move offset for next label (gradient label)
                        // Use a filled circle character (●) for visibility
                        Draw.Text(this, emaLabelTag, "●", 1, circleY, Brushes.Green);
                    }
                    // Red circle below bar: Close < Fast EMA < Slow EMA (bearish condition)
                    else if (close < fastEma && fastEma < slowEma)
                    {
                        // Position well below the bar to avoid overlap with any labels
                        double circleY = Low[1] - (14 * TickSize); // 20 ticks below the bar (further than before)
                        // Use a filled circle character (●) for visibility
                        Draw.Text(this, emaLabelTag, "●", 1, circleY, Brushes.Red);
                    }
                }
            }
            
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
            
            // Draw gradient label on the same completed bar (barsAgo=1)
            if (willShowGradient)
            {
                int prevBarIndex = CurrentBar - 1;
                string gradTag = "FastGradLabel_" + prevBarIndex;
                double gradY = labelBaseY + (labelStackOffset * 12 * TickSize); // 12 ticks spacing between labels
                labelStackOffset++; // Move offset for next label

                // "F:" shows the exact angle used by the strategy for this bar. When the used
                // value is NOT already the chart-scaled visual angle, also show "eye:" so you
                // can compare the number against what the slope looks like on the chart.
                string gradText = "F:" + gradForLabel.ToString("F1") + "\u00B0";
                double eyeDeg = double.NaN;
                if (chartGradientByBar.ContainsKey(prevBarIndex))
                    eyeDeg = chartGradientByBar[prevBarIndex];
                else if (!double.IsNaN(lastChartScaledGradDeg))
                    eyeDeg = lastChartScaledGradDeg;
                if (!double.IsNaN(eyeDeg) && Math.Abs(eyeDeg - gradForLabel) > 0.05)
                    gradText += "  eye:" + eyeDeg.ToString("F1") + "\u00B0";

                Draw.Text(this, gradTag, gradText, 1, gradY, Brushes.Black); // barsAgo = 1
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

            if (TradeRealtimeOnly && State != State.Realtime)
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
                int qty = Math.Abs(execution.Quantity);
                double profitLocked = 0;
                    
                    if (order.OrderAction == OrderAction.Sell) // Long exit
                    {
                    profitLocked = (exitPrice - breakEvenEntryPrice) * qty;
                    }
                    else if (order.OrderAction == OrderAction.BuyToCover) // Short exit
                    {
                    profitLocked = (breakEvenEntryPrice - exitPrice) * qty;
                    }
                    
                    Print($"[BREAKEVEN_EXIT] Bar {CurrentBar}: Break-even stop hit! Entry={breakEvenEntryPrice:F2}, Exit={exitPrice:F2}, Locked Profit={profitLocked:F2} points");
                }
                
                Print($"[EXIT_FILL_DEBUG] Bar {CurrentBar}: Exit filled - {action}, resetting intendedPosition from {intendedPosition} to Flat");
                intendedPosition = MarketPosition.Flat;
                ResetMfeProtectionState(clearActiveEntrySignal: true);
                MarketPosition exitedPosition = action == OrderAction.Sell ? MarketPosition.Long : MarketPosition.Short;
                RecordExitForCooldown(exitedPosition);
                UpdateBarNavPositionLabels(force: true);
            }
            // Update intendedPosition when entries fill
            else if (isEntry)
            {
                int fillQty = Math.Abs(execution.Quantity);
                int orderQty = order.Quantity;
                int posQty = Math.Abs(Position.Quantity);
                if (fillQty > Math.Max(1, Contracts) || orderQty > Math.Max(1, Contracts) || posQty > Math.Max(1, Contracts))
                {
                    Print($"[ENTRY_FILL_QTY] Bar {CurrentBar}: Entry fill qty mismatch. FillQty={fillQty}, OrderQty={orderQty}, PositionQty={posQty}, Contracts={Contracts}, OrderName={order.Name}");
                }

                ResetMfeProtectionState();
                string entrySignal = !string.IsNullOrEmpty(order.FromEntrySignal)
                    ? order.FromEntrySignal
                    : GetActiveEntrySignal();
                try
                {
                    RefreshManagedStopOnly(entrySignal);
                    Print($"[ENTRY_STOP_REFRESH] Bar {CurrentBar}: Fresh stop applied for {entrySignal} after entry fill @ {execution.Price:F2}");
                }
                catch (Exception ex)
                {
                    Print($"[ENTRY_STOP_REFRESH] Bar {CurrentBar}: Failed to refresh stop for {entrySignal}: {ex.Message}");
                }

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

                UpdateBarNavPositionLabels(force: true);
            }

            string reason = GetOrderReason(order, isEntry, isExit);
            
            // Capture exit reason for RecordBarSample (fallback if not already set)
            if (isExit && string.IsNullOrEmpty(currentTradeExitReason))
            {
                currentTradeExitReason = order.Name ?? reason;
                lastExitBarIndex = CurrentBar;
            }

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
                // Use CurrentBar for entry/exit logging - this will be adjusted when the bar closes
                // The actual bar index logged will be the completed bar (justClosedBar) when ProcessPendingCSVLogs runs
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

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
            double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string comment)
        {
            base.OnOrderUpdate(order, limitPrice, stopPrice, quantity, filled, averageFillPrice, orderState, time, error, comment);

            if (order == null)
                return;

            if (EnableOrderAuditLog)
            {
                string orderName = order.Name ?? "null";
                string fromEntrySignal = order.FromEntrySignal ?? "null";
                string orderId = order.OrderId ?? "null";
                string oco = order.Oco ?? "null";
                string action = order.OrderAction.ToString();
                string type = order.OrderType.ToString();
                Print($"[ORDER_AUDIT] Bar {CurrentBar}: Name={orderName}, Action={action}, Type={type}, State={orderState}, Qty={quantity}, Filled={filled}, AvgFill={averageFillPrice:F2}, Limit={limitPrice:F2}, Stop={stopPrice:F2}, FromEntrySignal={fromEntrySignal}, OCO={oco}, OrderId={orderId}, Time={time:HH:mm:ss.fff}, Comment={comment}");
            }

            string activeSignal = GetActiveEntrySignal();
            if (profitFloorModeActive
                && IsProfitFloorActiveForOrder(order.FromEntrySignal)
                && !string.IsNullOrEmpty(activeSignal)
                && string.Equals(order.FromEntrySignal, activeSignal, StringComparison.Ordinal)
                && (IsTrailStopOrder(order)
                    || order.OrderType == OrderType.StopMarket
                    || order.OrderType == OrderType.StopLimit)
                && (orderState == OrderState.Working
                    || orderState == OrderState.Accepted
                    || orderState == OrderState.PartFilled
                    || orderState == OrderState.Submitted))
            {
                ReinforceProfitFloorStopIfNeeded();
            }

            // Log only when something goes wrong or the order is cancelled/rejected
            if (error != ErrorCode.NoError || orderState == OrderState.Rejected || orderState == OrderState.Cancelled)
            {
                Print($"[ORDER_UPDATE] Bar {CurrentBar}: {order.Name} {order.OrderAction} {order.OrderType} " +
                      $"State={orderState}, Error={error}, Comment={comment}, Qty={quantity}, Filled={filled}, " +
                      $"AvgFill={averageFillPrice:F2}, Limit={limitPrice:F2}, Stop={stopPrice:F2}");
            }

            // Handle rejected stop orders: re-submit at valid price or exit at market.
            // NinjaTrader's built-in trailing stop can generate "stop above/below market"
            // rejections during volatile moves. Rather than freezing, recover gracefully.
            if (orderState == OrderState.Rejected && order.OrderType == OrderType.StopMarket
                && Position.MarketPosition != MarketPosition.Flat)
            {
                Print($"[REJECTED_STOP_RECOVERY] Bar {CurrentBar}: Stop order rejected ({order.Name} {order.OrderAction} @ {stopPrice:F2}). Re-applying stop at current distance.");
                try
                {
                    int stopLossTicks = CalculateStopLossTicks();
                    RefreshManagedStopOnly(GetActiveEntrySignal());
                    Print($"[REJECTED_STOP_RECOVERY] Re-applied {stopLossTicks}-tick stop via CalculationMode.Ticks");
                }
                catch (Exception ex)
                {
                    Print($"[REJECTED_STOP_RECOVERY] Failed to re-apply stop: {ex.Message}. Exiting at market.");
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        currentTradeExitReason = "StopRejectionExit";
                        ExitLong("StopRejectionExit", GetActiveEntrySignal());
                    }
                    else if (Position.MarketPosition == MarketPosition.Short)
                    {
                        currentTradeExitReason = "StopRejectionExit";
                        ExitShort("StopRejectionExit", GetActiveEntrySignal());
                    }
                }
            }

            if (orderState == OrderState.Rejected && order.OrderType == OrderType.Limit
                && order.Name == "Profit target" && Position.MarketPosition != MarketPosition.Flat)
            {
                Print($"[REJECTED_TARGET] Bar {CurrentBar}: Profit target rejected ({order.OrderAction} @ {limitPrice:F2}, OCO={order.Oco}). Bracket was configured pre-entry on signal {GetActiveEntrySignal()}.");
            }

            if (order.Instrument == Instrument)
                UpdateBarNavPositionLabels();
        }

        protected override void OnPositionUpdate(Cbi.Position position, double averagePrice, int quantity, Cbi.MarketPosition marketPosition)
        {
            base.OnPositionUpdate(position, averagePrice, quantity, marketPosition);

            if (quantity != lastPositionQuantity)
            {
                lastPositionQuantity = quantity;
                try
                {
                    ExportStrategyState();
                }
                catch (Exception ex)
                {
                    Print($"[STATE_UPDATE] Bar {CurrentBar}: Failed to export state on size change: {ex.Message}");
                }
            }

            int expectedQty = Math.Max(1, Contracts);
            if (marketPosition != MarketPosition.Flat && Math.Abs(quantity) > expectedQty)
            {
                if ((DateTime.Now - lastMismatchLogTime).TotalSeconds >= 5)
                {
                    lastMismatchLogTime = DateTime.Now;
                    LogPositionSizeMismatch(quantity, marketPosition, expectedQty);
                }
            }

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
                currentTradeMfePoints = 0.0;
                currentTradeMAE = 0.0;
                
                Print($"[TRADE_ENTRY] Bar {currentTradeEntryBar} (baseBar={baseBar}, lastEntryBarIndex={lastEntryBarIndex}, CurrentBar={CurrentBar}): {marketPosition} entry at {averagePrice:F2}, Contracts={currentTradeContracts}");
            }
            // Detect when position is closed (Long/Short -> Flat)
            else if (previousPosition != MarketPosition.Flat && marketPosition == MarketPosition.Flat)
            {
                // Trade exit - send trade data to dashboard
                // IMPORTANT: Reset entry bar BEFORE sending to prevent duplicate sends if OnPositionUpdate is called multiple times
                int entryBarToSend = currentTradeEntryBar;
                double entryPriceToSend = currentTradeEntryPrice;
                DateTime entryTimeToSend = currentTradeEntryTime;
                MarketPosition directionToSend = currentTradeDirection;
                int contractsToSend = currentTradeContracts;
                string entryReasonToSend = currentTradeEntryReason; // BUG FIX: Save entry reason before resetting
                double mfeToSend = currentTradeMFE;
                double maeToSend = currentTradeMAE;
                double stopLossPointsToSend = currentTradeStopLossPoints;
                
                // Reset tracking IMMEDIATELY to prevent duplicate sends
                currentTradeEntryBar = -1;
                currentTradeEntryReason = "";
                currentTradeEntryPrice = double.NaN;
                currentTradeEntryTime = DateTime.MinValue;
                currentTradeDirection = MarketPosition.Flat;
                currentTradeContracts = 0;
                currentTradeMFE = 0.0;
                currentTradeMfePoints = 0.0;
                currentTradeMAE = 0.0;
                currentTradeStopLossPoints = 0.0;
                
                // Now send with the saved values (only if we had a valid entry)
                if (entryBarToSend >= 0 && !double.IsNaN(entryPriceToSend))
                {
                    // Temporarily restore values for SendTradeCompletedToDashboard
                    currentTradeEntryBar = entryBarToSend;
                    currentTradeEntryPrice = entryPriceToSend;
                    currentTradeEntryTime = entryTimeToSend;
                    currentTradeDirection = directionToSend;
                    currentTradeContracts = contractsToSend;
                    currentTradeEntryReason = entryReasonToSend; // BUG FIX: Restore entry reason
                    currentTradeMFE = mfeToSend;
                    currentTradeMAE = maeToSend;
                    currentTradeStopLossPoints = stopLossPointsToSend;
                    
                    SendTradeCompletedToDashboard();
                    
                    // Reset again after sending
                    currentTradeEntryBar = -1;
                    currentTradeEntryPrice = double.NaN;
                    currentTradeEntryTime = DateTime.MinValue;
                    currentTradeDirection = MarketPosition.Flat;
                    currentTradeContracts = 0;
                    currentTradeEntryReason = "";
                    currentTradeMFE = 0.0;
                    currentTradeMAE = 0.0;
                    currentTradeStopLossPoints = 0.0;
                }
            }
            // Update MFE/MAE while position is open
            else if (marketPosition != MarketPosition.Flat && currentTradeEntryBar >= 0)
            {
                UpdateTradeMFEandMAE();
            }

            previousPosition = marketPosition;
            UpdateBarNavPositionLabels();
        }

        private void LogPositionSizeMismatch(int quantity, MarketPosition marketPosition, int expectedQty)
        {
            try
            {
                string accountName = Account != null ? Account.Name : "null";
                Print($"[POSITION_MISMATCH] Bar {CurrentBar}: PositionQty={quantity}, Expected={expectedQty}, MarketPosition={marketPosition}, Contracts={Contracts}, Account={accountName}");

                // Log account-level position for this instrument
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
                Print($"[POSITION_MISMATCH] AccountQuantity={accountQuantity} for {Instrument.FullName}");

                // Log recent executions for this instrument (last 5 by time)
                if (Account != null && Account.Executions != null)
                {
                    var recentExecs = Account.Executions
                        .Where(e => e != null && e.Instrument == Instrument)
                        .OrderByDescending(e => e.Time)
                        .Take(5)
                        .ToList();

                    if (recentExecs.Count == 0)
                    {
                        Print("[POSITION_MISMATCH] No recent executions found for this instrument.");
                    }
                    else
                    {
                        foreach (var exec in recentExecs)
                        {
                            string orderName = exec.Order != null ? exec.Order.Name : "null";
                            string orderAction = exec.Order != null ? exec.Order.OrderAction.ToString() : "null";
                            string orderId = exec.Order != null ? exec.Order.OrderId : "null";
                            string fromEntrySignal = exec.Order != null ? exec.Order.FromEntrySignal : "null";
                            string oco = exec.Order != null ? exec.Order.Oco : "null";
                            string orderState = exec.Order != null ? exec.Order.OrderState.ToString() : "null";
                            string execId = exec.ExecutionId ?? "null";
                            bool strategyNamed = !string.IsNullOrEmpty(orderName) && orderName.StartsWith("BarsOnTheFlow", StringComparison.Ordinal);
                            bool strategySignal = !string.IsNullOrEmpty(fromEntrySignal) && fromEntrySignal.StartsWith("BarsOnTheFlow", StringComparison.Ordinal);
                            string origin = (strategyNamed || strategySignal)
                                ? "Strategy"
                                : "External";
                            Print($"[POSITION_MISMATCH] Exec Time={exec.Time:HH:mm:ss.fff}, Qty={exec.Quantity}, Price={exec.Price:F2}, Order={orderName}, Action={orderAction}, OrderId={orderId}, ExecId={execId}, Origin={origin}, FromEntrySignal={fromEntrySignal}, OCO={oco}, OrderState={orderState}");
                        }
                    }
                }
                else
                {
                    Print("[POSITION_MISMATCH] Account.Executions not available.");
                }
            }
            catch (Exception ex)
            {
                Print($"[POSITION_MISMATCH] Error logging mismatch details: {ex.Message}");
            }
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

                double favorablePoints = currentTradeDirection == MarketPosition.Long
                    ? currentPrice - currentTradeEntryPrice
                    : currentTradeEntryPrice - currentPrice;
                if (favorablePoints > currentTradeMfePoints)
                    currentTradeMfePoints = favorablePoints;

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

                // Calculate bars held
                int barsHeld = CurrentBar - currentTradeEntryBar;
                if (barsHeld < 0) barsHeld = 0;

                // Get realized P&L in points
                double realizedPoints = 0.0;
                if (lastTrade.ProfitCurrency != 0 && Instrument.MasterInstrument.PointValue > 0)
                {
                    realizedPoints = lastTrade.ProfitCurrency / Instrument.MasterInstrument.PointValue;
                }

                double stopLossPointsUsed = currentTradeStopLossPoints; // For stop loss research when trade loses

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
                            stopLossPointsUsed = stopLossPoints;
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

                // When trade closed with negative PnL, run stop loss research (avg bar size, ATR, MFE/MAE, suggestions)
                if (realizedPoints < 0 && EnableStopLossResearch)
                {
                    RunStopLossResearchOnLosingTrade(currentTradeEntryBar, currentTradeEntryPrice, lastTrade.Exit.Price, currentTradeDirection, currentTradeMFE, currentTradeMAE, stopLossPointsUsed, barsHeld, exitReason, realizedPoints);
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
                
                // Send to both endpoints:
                // 1. Original dashboard.db endpoint (for backward compatibility)
                string url1 = DashboardBaseUrl.TrimEnd('/') + "/trade_completed";
                // 2. New volatility.db endpoint (same database as bar_samples)
                string url2 = DashboardBaseUrl.TrimEnd('/') + "/api/volatility/record-trade";

                // Send asynchronously to both endpoints
                Task.Run(() =>
                {
                    try
                    {
                        // Send to original endpoint
                        SendJsonToServer(json, url1);
                        Print($"[TRADE_EXIT] Bar {CurrentBar}: Trade data sent to dashboard.db");
                    }
                    catch (ObjectDisposedException ex)
                    {
                        Print($"[TRADE_EXIT] Bar {CurrentBar}: HttpClient disposed for dashboard.db, skipping: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Print($"[TRADE_EXIT] Bar {CurrentBar}: Error sending to dashboard.db: {ex.Message}");
                    }
                });
                
                Task.Run(() =>
                {
                    try
                    {
                        // Send to volatility.db endpoint
                        SendJsonToServer(json, url2);
                        Print($"[TRADE_EXIT] Bar {CurrentBar}: Trade data sent to volatility.db");
                    }
                    catch (ObjectDisposedException ex)
                    {
                        Print($"[TRADE_EXIT] Bar {CurrentBar}: HttpClient disposed for volatility.db, skipping: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Print($"[TRADE_EXIT] Bar {CurrentBar}: Error sending to volatility.db: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Print($"[TRADE_EXIT] Bar {CurrentBar}: Error preparing trade data: {ex.Message}");
            }
        }

        /// <summary>
        /// Compute average bar size (High-Low) in points over the N bars ending at the entry bar.
        /// Used by stop loss research to see if stop was tighter than typical bar range.
        /// </summary>
        private double GetAverageBarSizePointsAtEntryBar(int entryBar1Based, int lookback)
        {
            int entryBarIndex = entryBar1Based - 1; // 0-based bar index
            if (entryBarIndex < 0 || CurrentBar <= entryBarIndex)
                return 0;
            int start = Math.Max(0, entryBarIndex - lookback + 1);
            double sum = 0;
            int count = 0;
            for (int i = start; i <= entryBarIndex; i++)
            {
                int barsAgo = CurrentBar - i;
                if (barsAgo >= 0 && barsAgo < CurrentBar)
                {
                    sum += High[barsAgo] - Low[barsAgo];
                    count++;
                }
            }
            return count > 0 ? sum / count : 0;
        }

        /// <summary>
        /// When a trade closes with negative PnL, log research data (avg bar size, ATR, MFE/MAE) and suggestions
        /// to help tune stop loss and avoid being stopped out by normal bar noise.
        /// </summary>
        private void RunStopLossResearchOnLosingTrade(int entryBar1Based, double entryPrice, double exitPrice, MarketPosition direction, double mfe, double mae, double stopUsedPoints, int barsHeld, string exitReason, double realizedPoints)
        {
            if (!EnableStopLossResearch)
                return;
            InitializeLog(); // Ensure log dir and research log exist (may not have been written yet)
            if (!stopLossResearchInitialized || stopLossResearchWriter == null)
                return;
            try
            {
                double avgBarSizePrice = GetAverageBarSizePointsAtEntryBar(entryBar1Based, StopLossResearchAvgBarLookback);
                double atrAtEntryPrice = 0;
                if (atr != null && CurrentBar >= AtrPeriod)
                {
                    int entryBarIndex = entryBar1Based - 1;
                    int barsAgo = CurrentBar - entryBarIndex;
                    if (barsAgo >= 0 && barsAgo < CurrentBar)
                        atrAtEntryPrice = atr[barsAgo];
                }
                // Convert price range to points (same units as StopLossPoints) for comparison
                int tpp = GetTicksPerPoint();
                double pointSize = (tpp > 0 && Instrument != null && Instrument.MasterInstrument != null) ? (Instrument.MasterInstrument.TickSize * tpp) : 1.0;
                double avgBarSize = pointSize > 0 ? avgBarSizePrice / pointSize : avgBarSizePrice;
                double atrAtEntry = pointSize > 0 ? atrAtEntryPrice / pointSize : atrAtEntryPrice;

                string dirStr = direction == MarketPosition.Long ? "Long" : "Short";
                var analysis = new List<string>();
                var suggestion = new List<string>();

                if (stopUsedPoints > 0 && avgBarSize > 0 && stopUsedPoints < avgBarSize)
                {
                    analysis.Add($"Stop ({stopUsedPoints:F2} pts) was tighter than avg bar size ({avgBarSize:F2} pts)");
                    suggestion.Add($"Consider MinStopLossPoints >= {Math.Ceiling(avgBarSize)} or StopLossMultiplier > 1 so stop is not narrower than typical bar range.");
                }
                if (stopUsedPoints > 0 && atrAtEntry > 0 && stopUsedPoints < atrAtEntry * 0.5)
                {
                    analysis.Add($"Stop ({stopUsedPoints:F2} pts) was much smaller than ATR at entry ({atrAtEntry:F2} pts)");
                    suggestion.Add($"Volatility at entry exceeded stop; consider stop >= 0.5*ATR (e.g. {Math.Max(1, (int)(atrAtEntry * 0.5))} pts) or use dynamic/ATR-based stop.");
                }
                if (mae > 0)
                {
                    analysis.Add($"MAE was {mae:F2} pts (max adverse excursion)");
                    if (stopUsedPoints > 0 && mae > stopUsedPoints)
                        suggestion.Add($"Price went {mae - stopUsedPoints:F2} pts past your stop before exit; stop was hit. A wider stop (e.g. {Math.Ceiling(mae * 1.1)} pts) might have held.");
                }
                if (mfe > 0)
                {
                    analysis.Add($"MFE was {mfe:F2} pts (trade went in favor then reversed)");
                    suggestion.Add("Consider trailing stop or profit target to lock in gains when MFE is positive.");
                }
                if (barsHeld <= 2)
                {
                    analysis.Add($"Very short hold ({barsHeld} bars); possible noise exit.");
                    suggestion.Add("Review MinBarsBeforeTrendBreakExit or avoid exiting on first bar after entry.");
                }

                string analysisStr = analysis.Count > 0 ? string.Join("; ", analysis) : "Losing trade";
                string suggestionStr = suggestion.Count > 0 ? string.Join(" ", suggestion) : "Review stop and entry timing.";

                string timeStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string exitReasonEsc = (exitReason ?? "").Replace(",", ";").Replace("\"", "'");
                string line = $"{timeStr},{entryBar1Based},{CurrentBar + 1},{dirStr},{entryPrice:F2},{exitPrice:F2},{realizedPoints:F2},{mfe:F2},{mae:F2},{stopUsedPoints:F2},{barsHeld},{avgBarSize:F2},{atrAtEntry:F2},\"{exitReasonEsc}\",\"{analysisStr}\",\"{suggestionStr}\"";
                stopLossResearchWriter.WriteLine(line);
                stopLossResearchWriter.Flush();
                Print($"[STOP_LOSS_RESEARCH] Losing trade logged: Realized={realizedPoints:F2} pts, MFE={mfe:F2}, MAE={mae:F2}, AvgBar={avgBarSize:F2}, ATR={atrAtEntry:F2}. Suggestion: {suggestionStr}");

                // Live adjustment: nudge MinStopLossPoints and/or StopLossMultiplier so next trades get wider stops (within caps)
                if (EnableStopLossResearchLiveAdjust)
                {
                    double suggestedMinPoints = MinStopLossPoints;
                    if (stopUsedPoints > 0 && avgBarSize > 0 && stopUsedPoints < avgBarSize)
                        suggestedMinPoints = Math.Max(suggestedMinPoints, Math.Ceiling(avgBarSize));
                    if (stopUsedPoints > 0 && atrAtEntry > 0 && stopUsedPoints < atrAtEntry * 0.5)
                        suggestedMinPoints = Math.Max(suggestedMinPoints, Math.Max(1, (int)(atrAtEntry * 0.5)));
                    if (mae > stopUsedPoints && stopUsedPoints > 0 && mae > 0)
                        suggestedMinPoints = Math.Max(suggestedMinPoints, Math.Ceiling(mae * 1.1));

                    int cappedMin = Math.Min((int)Math.Round(suggestedMinPoints), Math.Max(1, StopLossResearchLiveAdjustMaxMinPoints));
                    if (cappedMin > MinStopLossPoints)
                    {
                        MinStopLossPoints = cappedMin;
                        Print($"[STOP_LOSS_RESEARCH] Live adjust: MinStopLossPoints -> {MinStopLossPoints} (from research: avgBar={avgBarSize:F1}, ATR={atrAtEntry:F1}, MAE={mae:F1})");
                    }

                    bool stopWasTighterThanVolatility = (stopUsedPoints > 0 && avgBarSize > 0 && stopUsedPoints < avgBarSize) || (stopUsedPoints > 0 && atrAtEntry > 0 && stopUsedPoints < atrAtEntry * 0.5);
                    if (stopWasTighterThanVolatility && StopLossMultiplier < StopLossResearchLiveAdjustMaxMultiplier)
                    {
                        double newMult = Math.Min(StopLossMultiplier + 0.05, StopLossResearchLiveAdjustMaxMultiplier);
                        if (newMult > StopLossMultiplier)
                        {
                            StopLossMultiplier = Math.Round(newMult, 2);
                            Print($"[STOP_LOSS_RESEARCH] Live adjust: StopLossMultiplier -> {StopLossMultiplier:F2}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"[STOP_LOSS_RESEARCH] Error: {ex.Message}");
            }
        }

        private void ClearTradesTable()
        {
            // Clear trades tables on fresh run (both dashboard.db and volatility.db)
            // MUST BE SYNCHRONOUS to complete before historical playback starts!
            try
            {
                // Clear dashboard.db trades table
                string url1 = DashboardBaseUrl.TrimEnd('/') + "/api/trades/clear";
                Print($"[TRADES_CLEAR] Clearing dashboard.db trades table (SYNC)...");
                
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response1 = client.PostAsync(url1, null).Result; // BLOCKING
                    
                    if (response1.IsSuccessStatusCode)
                    {
                        var responseText1 = response1.Content.ReadAsStringAsync().Result;
                        Print($"[TRADES_CLEAR] ✓ Cleared dashboard.db. Response: {responseText1}");
                    }
                    else
                    {
                        var errorText1 = response1.Content.ReadAsStringAsync().Result;
                        Print($"[TRADES_CLEAR] ✗ HTTP {response1.StatusCode}: {errorText1}");
                    }
                    response1.Dispose();
                }
                
                // Clear volatility.db trades table
                string url2 = DashboardBaseUrl.TrimEnd('/') + "/api/volatility/clear-trades";
                Print($"[TRADES_CLEAR] Clearing volatility.db trades table (SYNC)...");
                
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response2 = client.PostAsync(url2, null).Result; // BLOCKING
                    
                    if (response2.IsSuccessStatusCode)
                    {
                        var responseText2 = response2.Content.ReadAsStringAsync().Result;
                        Print($"[TRADES_CLEAR] ✓ Cleared volatility.db. Response: {responseText2}");
                    }
                    else
                    {
                        var errorText2 = response2.Content.ReadAsStringAsync().Result;
                        Print($"[TRADES_CLEAR] ✗ HTTP {response2.StatusCode}: {errorText2}");
                    }
                    response2.Dispose();
                }
            }
            catch (Exception ex)
            {
                Print($"[TRADES_CLEAR] Error clearing trades: {ex.Message}");
            }
        }

        private void ClearBarsTable()
        {
            // Clear BarsOnTheFlowStateAndBar table on fresh run
            // MUST BE SYNCHRONOUS to complete before historical playback starts!
            try
            {
                string url = DashboardBaseUrl.TrimEnd('/') + "/api/databases/clear-bars-table";
                Print($"[BARS_CLEAR] Clearing BarsOnTheFlowStateAndBar table (SYNC)...");
                
                // Use dedicated HttpClient to avoid issues
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response = client.PostAsync(url, null).Result; // BLOCKING
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var responseText = response.Content.ReadAsStringAsync().Result;
                        Print($"[BARS_CLEAR] ✓ Cleared. Response: {responseText}");
                    }
                    else
                    {
                        var errorText = response.Content.ReadAsStringAsync().Result;
                        Print($"[BARS_CLEAR] ✗ HTTP {response.StatusCode}: {errorText}");
                    }
                    response.Dispose();
                }
            }
            catch (Exception ex)
            {
                Print($"[BARS_CLEAR] Error clearing bars table: {ex.Message}");
            }
        }

        private void ImportHistoricalCsvToDatabase(string csvPath)
        {
            // Import CSV to database by calling Python script directly
            // This is more reliable than HTTP as it doesn't require the server to be running
            // and doesn't suffer from timeout issues
            try
            {
                Print($"[CSV_IMPORT] Starting Python-based CSV import: {Path.GetFileName(csvPath)}");
                
                // Get the Python import script path
                // logFilePath is like: C:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\strategy_logs\BarsOnTheFlow_...
                // We need: C:\Mac\Home\Documents\NinjaTrader 8\bin\Custom\web\dashboard
                string logDir = Path.GetDirectoryName(logFilePath);
                string customDir = Path.GetDirectoryName(logDir); // Go up from strategy_logs to Custom
                string dashboardDir = Path.Combine(customDir, "web", "dashboard");
                string importScript = Path.Combine(dashboardDir, "import_csv_to_database.py");
                
                Print($"[CSV_IMPORT] ========================================");
                Print($"[CSV_IMPORT] CSV Import Starting");
                Print($"[CSV_IMPORT] Log file: {Path.GetFileName(csvPath)}");
                Print($"[CSV_IMPORT] Log dir: {logDir}");
                Print($"[CSV_IMPORT] Custom dir: {customDir}");
                Print($"[CSV_IMPORT] Dashboard dir: {dashboardDir}");
                Print($"[CSV_IMPORT] Import script: {importScript}");
                Print($"[CSV_IMPORT] Script exists: {File.Exists(importScript)}");
                Print($"[CSV_IMPORT] CSV exists: {File.Exists(csvPath)}");
                Print($"[CSV_IMPORT] ========================================");
                
                if (!File.Exists(importScript))
                {
                    Print($"[CSV_IMPORT] Import script not found, falling back to HTTP");
                    ImportHistoricalCsvViaHttp(csvPath);
                    return;
                }
                
                // Run import in background (don't block strategy startup)
                Task.Run(() =>
                {
                    try
                    {
                        bool success = ImportCsvViaPython(csvPath, importScript, dashboardDir);
                        if (!success)
                        {
                            Print($"[CSV_IMPORT] Python import failed, trying HTTP fallback...");
                            ImportHistoricalCsvViaHttp(csvPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Print($"[CSV_IMPORT] Error during Python import: {ex.Message}");
                        Print($"[CSV_IMPORT] Trying HTTP fallback...");
                        try
                        {
                            ImportHistoricalCsvViaHttp(csvPath);
                        }
                        catch (Exception ex2)
                        {
                            Print($"[CSV_IMPORT] HTTP fallback also failed: {ex2.Message}");
                        }
                    }
                    finally
                    {
                        historicalCsvImportComplete = true;
                        Print($"[CSV_IMPORT] Import process complete - real-time recording enabled");
                    }
                });
            }
            catch (Exception ex)
            {
                Print($"[CSV_IMPORT] Error preparing CSV import: {ex.Message}");
                historicalCsvImportComplete = true;
            }
        }
        
        private bool ImportCsvViaPython(string csvPath, string scriptPath, string workingDir)
        {
            // Run Python script to import CSV directly to SQLite database
            try
            {
                // Try python3 first, then python
                string pythonExe = "python";
                try
                {
                    var testProcess = new ProcessStartInfo { FileName = "python3", Arguments = "--version", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                    using (var test = Process.Start(testProcess))
                    {
                        if (test != null)
                        {
                            test.WaitForExit(1000);
                            pythonExe = "python3";
                        }
                    }
                }
                catch { }
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{scriptPath}\" \"{csvPath}\"",
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                Print($"[CSV_IMPORT] Running: {pythonExe} \"{Path.GetFileName(scriptPath)}\" \"{Path.GetFileName(csvPath)}\"");
                Print($"[CSV_IMPORT] Working directory: {workingDir}");
                
                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    
                    // Read output
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    
                    // Wait for completion (max 120 seconds for large files)
                    bool completed = process.WaitForExit(120000);
                    
                    if (!completed)
                    {
                        Print($"[CSV_IMPORT] Python process timed out after 120 seconds");
                        try { process.Kill(); } catch { }
                        return false; // Timeout
                    }
                    
                    int exitCode = process.ExitCode;
                    Print($"[CSV_IMPORT] Python process exited with code: {exitCode}");
                    
                    // Print ALL output lines (not just first 10) to see what happened
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        string[] outputLines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in outputLines)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                                Print($"[CSV_IMPORT_OUT] {line.Trim()}");
                        }
                    }
                    
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        string[] errorLines = error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in errorLines)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                                Print($"[CSV_IMPORT_ERR] {line.Trim()}");
                        }
                    }
                    
                    if (exitCode != 0)
                    {
                        Print($"[CSV_IMPORT] WARNING: Python script exited with error code {exitCode}");
                        return false;
                    }
                    else
                    {
                        Print($"[CSV_IMPORT] SUCCESS: CSV import completed successfully");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"[CSV_IMPORT] Python execution error: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    Print($"[CSV_IMPORT] Inner exception: {ex.InnerException.Message}");
                return false;
            }
        }
        
        private void ImportHistoricalCsvViaHttp(string csvPath)
        {
            // Fallback: Import CSV via HTTP request to server
            try
            {
                string url = DashboardBaseUrl.TrimEnd('/') + "/api/databases/import-csv";
                string jsonPayload = $"{{\"csv_path\":\"{csvPath.Replace("\\", "\\\\")}\"}}";
                
                Print($"[CSV_IMPORT_HTTP] Triggering HTTP import for: {Path.GetFileName(csvPath)}");
                
                EnsureHttpClient();
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                
                var cts = cancellationTokenSource;
                Task.Run(async () =>
                {
                    try
                    {
                        if (cts == null || cts.IsCancellationRequested)
                            return;
                        
                        using (var quickTimeoutCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5)))
                        {
                            var response = await sharedClient.PostAsync(url, content, quickTimeoutCts.Token).ConfigureAwait(false);
                            Print($"[CSV_IMPORT_HTTP] Request accepted (status: {response.StatusCode})");
                            response.Dispose();
                        }
                        
                        historicalCsvImportComplete = true;
                    }
                    catch (Exception ex)
                    {
                        Print($"[CSV_IMPORT_HTTP] Error: {ex.Message}");
                        historicalCsvImportComplete = true;
                    }
                });
            }
            catch (Exception ex)
            {
                Print($"[CSV_IMPORT_HTTP] Error preparing request: {ex.Message}");
                historicalCsvImportComplete = true;
            }
        }
        
        private void ClearBarSamples()
        {
            // Clear bar_samples table on fresh run
            // Called automatically when strategy starts (State.DataLoaded)
            // This ensures each run starts with a clean bar_samples table
            // MUST BE SYNCHRONOUS to complete before historical playback starts!
            try
            {
                string url = DashboardBaseUrl.TrimEnd('/') + "/api/databases/clear-bar-samples";
                Print($"[BAR_SAMPLES_CLEAR] Clearing bar_samples table (SYNC)...");
                
                // Use dedicated HttpClient to avoid issues
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response = client.PostAsync(url, null).Result; // BLOCKING
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var responseText = response.Content.ReadAsStringAsync().Result;
                        Print($"[BAR_SAMPLES_CLEAR] ✓ Cleared. Response: {responseText}");
                    }
                    else
                    {
                        var errorText = response.Content.ReadAsStringAsync().Result;
                        Print($"[BAR_SAMPLES_CLEAR] ✗ HTTP {response.StatusCode}: {errorText}");
                    }
                    response.Dispose();
                }
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
                logWriter.WriteLine($"# DelayRetraceExitOnBullishEma={DelayRetraceExitOnBullishEma}");
                logWriter.WriteLine($"# DelayRetraceExitOnFavorableGradient={DelayRetraceExitOnFavorableGradient}");
                logWriter.WriteLine($"# ReverseOnEmaStop={ReverseOnEmaStop}");
                logWriter.WriteLine($"# EnableTrendOverlay={EnableTrendOverlay}");
                logWriter.WriteLine($"# EnableShorts={EnableShorts}");
                logWriter.WriteLine($"# AvoidShortsOnGoodCandle={AvoidShortsOnGoodCandle}");
                logWriter.WriteLine($"# AvoidLongsOnBadCandle={AvoidLongsOnBadCandle}");
                logWriter.WriteLine($"# TradeRealtimeOnly={TradeRealtimeOnly}");
                
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
                logWriter.WriteLine($"# RecordBarSamplesInHistorical={RecordBarSamplesInHistorical}");
                logWriter.WriteLine($"# BarSampleDelayMs={BarSampleDelayMs}");
                
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
                state.AppendLine($"  \"runningSince\": \"{(strategyRunStartTime != DateTime.MinValue ? strategyRunStartTime.ToString("yyyy-MM-dd HH:mm:ss") : "")}\",");
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
                state.AppendLine($"  \"mfeProtectTrigger\": {MfeProtectTrigger},");
                state.AppendLine($"  \"breakEvenOffset\": {BreakEvenOffset},");
                state.AppendLine($"  \"breakEvenActivated\": {breakEvenActivated.ToString().ToLower()},");
                state.AppendLine($"  \"minStopLossPoints\": {MinStopLossPoints},");
                state.AppendLine($"  \"emaStopMinDistanceFromEntry\": {EmaStopMinDistanceFromEntry},");
                
                state.AppendLine($"  \"enableShorts\": {EnableShorts.ToString().ToLower()},");
                state.AppendLine($"  \"avoidLongsOnBadCandle\": {AvoidLongsOnBadCandle.ToString().ToLower()},");
                state.AppendLine($"  \"avoidShortsOnGoodCandle\": {AvoidShortsOnGoodCandle.ToString().ToLower()},");
                state.AppendLine($"  \"avoidLongsOnBadCandleCurrentBar\": {AvoidLongsOnBadCandleCurrentBar.ToString().ToLower()},");
                state.AppendLine($"  \"avoidShortsOnGoodCandleCurrentBar\": {AvoidShortsOnGoodCandleCurrentBar.ToString().ToLower()},");
                state.AppendLine($"  \"minTicksCurrentBarDirection\": {MinTicksCurrentBarDirection},");
                state.AppendLine($"  \"useTradingHoursFilter\": {UseTradingHoursFilter.ToString().ToLower()},");
                state.AppendLine($"  \"tradingStartHour\": {TradingStartHour},");
                state.AppendLine($"  \"tradingStartMinute\": {TradingStartMinute},");
                state.AppendLine($"  \"tradingEndHour\": {TradingEndHour},");
                state.AppendLine($"  \"tradingEndMinute\": {TradingEndMinute},");
                state.AppendLine($"  \"barTime\": \"{Time[0]:yyyy-MM-dd HH:mm:ss}\",");
                state.AppendLine($"  \"localNow\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",");
                state.AppendLine($"  \"tradingHoursNow\": \"{lastTradingHoursNow:yyyy-MM-dd HH:mm:ss}\",");
                state.AppendLine($"  \"tradingHoursNowBasis\": \"{lastTradingHoursNowBasis}\",");
                state.AppendLine($"  \"useAtrFilter\": {UseAtrFilter.ToString().ToLower()},");
                state.AppendLine($"  \"atrPeriod\": {AtrPeriod},");
                state.AppendLine($"  \"atrMin\": {AtrMin},");
                state.AppendLine($"  \"atrMax\": {AtrMax},");
                state.AppendLine($"  \"useTrafficFilter\": {UseTrafficFilter.ToString().ToLower()},");
                state.AppendLine($"  \"minTicksPerMinuteToTrade\": {MinTicksPerMinuteToTrade},");
                state.AppendLine($"  \"trafficFilterLookbackBars\": {TrafficFilterLookbackBars},");
                state.AppendLine($"  \"lastClosedBarTicksPerMinute\": {(double.IsNaN(lastClosedBarTicksPerMinute) ? -1 : lastClosedBarTicksPerMinute)},");
                state.AppendLine($"  \"useProfitTarget\": {UseProfitTarget.ToString().ToLower()},");
                state.AppendLine($"  \"profitTargetPoints\": {ProfitTargetPoints},");
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
                state.AppendLine($"  \"emaCrossoverRequireBodyBelow\": {EmaCrossoverRequireBodyBelow.ToString().ToLower()},");
                state.AppendLine($"  \"useDeferredEntry\": {UseDeferredEntry.ToString().ToLower()},");
                state.AppendLine($"  \"useChartScaledFastGradDeg\": {UseChartScaledFastGradDeg.ToString().ToLower()},");
                state.AppendLine($"  \"enableBarsOnTheFlowTrendDetection\": {EnableBarsOnTheFlowTrendDetection.ToString().ToLower()},");
                state.AppendLine($"  \"trendLookbackBars\": {TrendLookbackBars},");
                state.AppendLine($"  \"minMatchingBars\": {MinMatchingBars},");
                state.AppendLine($"  \"usePnLTiebreaker\": {UsePnLTiebreaker.ToString().ToLower()},");
                state.AppendLine($"  \"pendingLongFromBad\": {pendingLongFromBad.ToString().ToLower()},");
                state.AppendLine($"  \"pendingShortFromGood\": {pendingShortFromGood.ToString().ToLower()},");
                state.AppendLine($"  \"recordBarSamplesInHistorical\": {RecordBarSamplesInHistorical.ToString().ToLower()},");
                state.AppendLine($"  \"barSampleDelayMs\": {BarSampleDelayMs},");
                state.AppendLine($"  \"useEmaTrailingStop\": {UseEmaTrailingStop.ToString().ToLower()},");
                state.AppendLine($"  \"emaStopTriggerMode\": \"{EmaStopTriggerMode}\",");
                state.AppendLine($"  \"reverseOnEmaStop\": {ReverseOnEmaStop.ToString().ToLower()},");
                state.AppendLine($"  \"useGradientStopLoss\": {UseGradientStopLoss.ToString().ToLower()},");
                state.AppendLine($"  \"gradientStopMinBarsDelay\": {GradientStopMinBarsDelay},");
                state.AppendLine($"  \"exitLongBelowGradient\": {ExitLongBelowGradient},");
                state.AppendLine($"  \"exitShortAboveGradient\": {ExitShortAboveGradient}");
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
                    
                    // ALWAYS use async to prevent blocking the strategy
                    // Synchronous calls can cause hangs/deadlocks
                    var cts = cancellationTokenSource; // Capture cancellation token
                    Task.Run(async () => {
                        try
                        {
                            // Check if strategy is still running or cancelled
                            if (State == State.Terminated || State == State.SetDefaults || cts == null || cts.IsCancellationRequested)
                            {
                                return; // Strategy stopped, don't send request
                            }
                            
                            EnsureHttpClient();
                            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                            var response = await sharedClient.PostAsync(url, content, cts.Token).ConfigureAwait(false);
                            
                            // Check again after request completes
                            if (State == State.Terminated || State == State.SetDefaults || cts.IsCancellationRequested)
                            {
                                response.Dispose();
                                return; // Strategy stopped, don't log
                            }
                            
                            if (!response.IsSuccessStatusCode)
                            {
                                Print($"[Dashboard] POST failed for bar {barIndexForLog}: {(int)response.StatusCode} {response.StatusCode}");
                            }
                            else if (!dashboardPostLoggedSuccess)
                            {
                                dashboardPostLoggedSuccess = true;
                                Print($"[Dashboard] POST succeeded: {(int)response.StatusCode} {response.StatusCode} (bar {barIndexForLog})");
                            }
                            response.Dispose();
                        }
                        catch (TaskCanceledException)
                        {
                            // Request was cancelled - don't log (strategy was stopped)
                        }
                        catch (OperationCanceledException)
                        {
                            // Request was cancelled - don't log (strategy was stopped)
                        }
                        catch (Exception ex)
                        {
                            // Only log if strategy is still running and not cancelled
                            if (State != State.Terminated && State != State.SetDefaults && (cts == null || !cts.IsCancellationRequested))
                            {
                                Print($"[Dashboard] POST failed for bar {barIndexForLog}: {ex.Message}");
                            }
                        }
                    });
                    
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
            
            ExportStrategyState();
        }

        /// <summary>
        /// Sends a pre-built JSON string to the server.
        /// Called on background thread via Task.Run.
        /// </summary>
        private void SendJsonToServer(string json, string url)
        {
            try
            {
                if (!dashboardPostSemaphore.Wait(2000))
                {
                    Print($"[Dashboard] POST skipped (throttled) for url={url}");
                    return;
                }

                EnsureHttpClient();
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                // Use ConfigureAwait(false) to avoid sync-context deadlocks
                using var response = sharedClient
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
                    string responseBody = "";
                    try
                    {
                        responseBody = response.Content.ReadAsStringAsync().Result;
                    }
                    catch { }
                    Print($"[Dashboard] POST failed for {url}: {(int)response.StatusCode} {response.StatusCode}, Response: {responseBody}");
                }
                else
                {
                    string responseBody = "";
                    try
                    {
                        responseBody = response.Content.ReadAsStringAsync().Result;
                    }
                    catch { }
                    Print($"[Dashboard] POST succeeded to {url}: {(int)response.StatusCode} {response.StatusCode}, Response: {responseBody}");
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
            finally
            {
                if (dashboardPostSemaphore.CurrentCount < 4)
                    dashboardPostSemaphore.Release();
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
            json.Append("\"runningSince\":\"").Append(strategyRunStartTime != DateTime.MinValue ? strategyRunStartTime.ToString("yyyy-MM-dd HH:mm:ss") : "").Append("\",");
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
            json.Append("\"minStopLossPoints\":").Append(MinStopLossPoints).Append(',');
            json.Append("\"emaStopMinDistanceFromEntry\":").Append(EmaStopMinDistanceFromEntry).Append(',');
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
            
            // EMA trailing stop configuration
            json.Append("\"useEmaTrailingStop\":").Append(UseEmaTrailingStop ? "true" : "false").Append(',');
            json.Append("\"emaStopTriggerMode\":\"").Append(EmaStopTriggerMode.ToString()).Append("\",");
            json.Append("\"emaStopProfitProtectionPoints\":").Append(EmaStopProfitProtectionPoints.ToString(ci)).Append(',');
            
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
            json.Append("\"emaCrossoverRequireBodyBelow\":").Append(EmaCrossoverRequireBodyBelow ? "true" : "false").Append(',');
            
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

                // Initialize stop loss research log (used when a trade closes with negative PnL)
                InitializeStopLossResearchLog(logDir, ts);

                // Traffic log: volume per minute by hour/day for predicting activity
                if (EnableTrafficLog)
                    InitializeTrafficLog(logDir, ts);
            }
            catch (Exception ex)
            {
                Print($"[BarsOnTheFlow] Failed to initialize log: {ex.Message}");
                logWriter = null;
                logInitialized = false;
            }
        }

        private void InitializeStopLossResearchLog(string logDir, string ts)
        {
            if (stopLossResearchInitialized)
                return;
            try
            {
                // Delete old stop loss research CSV files from this folder so they don't accumulate on the drive
                try
                {
                    string pattern = "BarsOnTheFlow_StopLossResearch_*.csv";
                    string[] oldFiles = Directory.GetFiles(logDir, pattern);
                    foreach (string f in oldFiles)
                    {
                        try
                        {
                            File.Delete(f);
                            Print($"[BarsOnTheFlow] Deleted old stop loss research file: {Path.GetFileName(f)}");
                        }
                        catch (Exception exDel)
                        {
                            Print($"[BarsOnTheFlow] Could not delete old research file {Path.GetFileName(f)}: {exDel.Message}");
                        }
                    }
                }
                catch (Exception exCleanup)
                {
                    Print($"[BarsOnTheFlow] Cleanup of old research files failed: {exCleanup.Message}");
                }

                stopLossResearchPath = Path.Combine(logDir, $"BarsOnTheFlow_StopLossResearch_{Instrument.FullName}_{ts}.csv");
                stopLossResearchWriter = new StreamWriter(stopLossResearchPath, false) { AutoFlush = true };
                stopLossResearchInitialized = true;
                string header = "Time,EntryBar,ExitBar,Direction,EntryPrice,ExitPrice,RealizedPoints,MFE,MAE,StopUsedPoints,BarsHeld,AvgBarSizeAtEntry,ATRAtEntry,ExitReason,Analysis,Suggestion";
                stopLossResearchWriter.WriteLine(header);
            }
            catch (Exception ex)
            {
                Print($"[BarsOnTheFlow] Failed to initialize stop loss research log: {ex.Message}");
                stopLossResearchWriter = null;
                stopLossResearchInitialized = false;
            }
        }

        private void InitializeTrafficLog(string logDir, string ts)
        {
            if (trafficLogInitialized)
                return;
            try
            {
                string safeInstrument = Instrument?.FullName ?? "Instrument";
                foreach (char c in Path.GetInvalidFileNameChars())
                    safeInstrument = safeInstrument.Replace(c, '_');

                // Persistent file across runs/days (append-only). This is the "reference log".
                trafficLogPath = Path.Combine(logDir, $"BarsOnTheFlow_Traffic_{safeInstrument}.csv");

                bool fileExists = File.Exists(trafficLogPath);
                trafficLogWriter = new StreamWriter(trafficLogPath, append: true) { AutoFlush = true };
                trafficLogInitialized = true;
                if (!fileExists)
                {
                    string header = "Date,Time,DayOfWeek,Hour,Minute,Instrument,Volume,BarMinutes,VolumePerMinute,TicksInBar,TicksPerMinute";
                    trafficLogWriter.WriteLine(header);
                }
            }
            catch (Exception ex)
            {
                Print($"[BarsOnTheFlow] Failed to initialize traffic log: {ex.Message}");
                trafficLogWriter = null;
                trafficLogInitialized = false;
            }
        }

        private void LogTrafficSample()
        {
            if (!trafficLogInitialized || trafficLogWriter == null || CurrentBar < 2)
                return;
            try
            {
                DateTime barTime = Time[1];
                double volume = Volume[1];
                double barMinutes = (Time[1] - Time[2]).TotalMinutes;
                if (barMinutes <= 0)
                    barMinutes = 1.0;
                double volumePerMinute = volume / barMinutes;
                
                int ticksInBar = (lastClosedBarIndex == CurrentBar - 1) ? lastClosedBarTicks : 0;
                double ticksPerMinute = barMinutes > 0 ? (ticksInBar / barMinutes) : 0.0;

                string line = $"{barTime:yyyy-MM-dd},{barTime:HH:mm},{(int)barTime.DayOfWeek},{barTime.Hour},{barTime.Minute},{Instrument.FullName},{volume:F0},{barMinutes:F2},{volumePerMinute:F2},{ticksInBar},{ticksPerMinute:F2}";
                trafficLogWriter.WriteLine(line);
            }
            catch (Exception ex)
            {
                Print($"[BarsOnTheFlow] Traffic log write failed: {ex.Message}");
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
            // Note: CurrentBar is 0-based in NinjaTrader. When CurrentBar=152, the bar that just closed is bar 151 (CurrentBar-1).
            // This barIndex matches the chart's bar index display.
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

                // Get EMA values for logging
                double emaFastValForLog = (emaFast != null && CurrentBar >= EmaFastPeriod) ? emaFast[barsAgo] : double.NaN;
                double emaSlowValForLog = (emaSlow != null && CurrentBar >= EmaSlowPeriod) ? emaSlow[barsAgo] : double.NaN;
                string emaFastValueStr = double.IsNaN(emaFastValForLog) ? string.Empty : emaFastValForLog.ToString("F4");
                string emaSlowValueStr = double.IsNaN(emaSlowValForLog) ? string.Empty : emaSlowValForLog.ToString("F4");

                // Reuse the existing CSV schema; mark action as BAR snapshot
                // CSV columns: timestamp,bar,direction,open,high,low,close,openFinal,highFinal,lowFinal,closeFinal,candleType,fastEma,fastEmaGradDeg,volume,bodyPct,upperWick,lowerWick,action,orderName,quantity,price,pnl,reason,prevOpen,prevClose,prevCandleType,allowLongThisBar,allowShortThisBar,trendUpAtDecision,trendDownAtDecision,decisionBarIndex,pendingShortFromGood,pendingLongFromBad,barPattern,useEmaCrossoverFilter,emaFastPeriod,emaSlowPeriod,emaCrossoverWindowBars,emaCrossoverRequireCrossover,emaCrossoverCooldownBars,emaFastValue,emaSlowValue
                string line = string.Format(
                    "{0},{1},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11},{12},{13},{14},{15:F6},{16:F4},{17:F4},{18},{19},{20},{21},{22},{23},{24},{25},{26},{27},{28},{29},{30},{31},{32},{33},{34},{35},{36},{37},{38},{39},{40},{41},{42}",
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
                    barPattern, // 34 barPattern
                    UseEmaCrossoverFilter, // 35 useEmaCrossoverFilter
                    EmaFastPeriod, // 36 emaFastPeriod
                    EmaSlowPeriod, // 37 emaSlowPeriod
                    EmaCrossoverWindowBars, // 38 emaCrossoverWindowBars
                    EmaCrossoverRequireCrossover, // 39 emaCrossoverRequireCrossover
                    EmaCrossoverCooldownBars, // 40 emaCrossoverCooldownBars
                    emaFastValueStr, // 41 emaFastValue
                    emaSlowValueStr // 42 emaSlowValue
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
                if (IsBarsOnTheFlowEntrySignalName(order.Name))
                    return order.Name.StartsWith("BarsOnTheFlowLong", StringComparison.Ordinal) ? "TrendUp" : "TrendDown";
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
            int maxLookback = Math.Max(TrendLookbackBars, MinMatchingBars);
            if (maxLookback < 1)
                maxLookback = 1;

            recentGood.Enqueue(isGood);
            if (recentGood.Count > maxLookback)
                recentGood.Dequeue();

            recentPnl.Enqueue(pnl);
            if (recentPnl.Count > maxLookback)
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

        private void UpdateTrendLifecycle(MarketPosition currentPos, bool updateOverlay = true)
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
                if (updateOverlay)
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

            // Clear pending retrace defer if conditions no longer apply or position changed
            if (!shouldEndTrend || Position.MarketPosition != MarketPosition.Long)
            {
                pendingRetraceExitLong = false;
                pendingRetraceExitLongBar = -1;
            }
            if (!shouldEndTrend || Position.MarketPosition != MarketPosition.Short)
            {
                pendingRetraceExitShort = false;
                pendingRetraceExitShortBar = -1;
            }

            if (shouldEndTrend)
            {
                if (ExitOnRetrace)
                {
                    // Get current conditions for logging
                    double currentClose = Close[0];
                    double currentFastEma = fastEma != null && CurrentBar >= FastEmaPeriod ? fastEma[0] : double.NaN;
                    double currentGradDeg;
                    GetCurrentGradient(out currentGradDeg);
                    bool barAboveEma = !double.IsNaN(currentFastEma) && currentClose > currentFastEma;
                    bool deferLongDueToBullishEma = DelayRetraceExitOnBullishEma && currentGradDeg > 0 && barAboveEma;
                    bool deferShortDueToBullishEma = DelayRetraceExitOnBullishEma && currentGradDeg < 0 && !barAboveEma;
                    bool deferLongDueToFavorableGradient = DelayRetraceExitOnFavorableGradient && currentGradDeg > 0;
                    bool deferShortDueToFavorableGradient = DelayRetraceExitOnFavorableGradient && currentGradDeg < 0;
                    
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        // If favorable gradient is enabled, keep deferring as long as gradient favors the trade
                        if (deferLongDueToFavorableGradient)
                        {
                            Print($"[RETRACE_EXIT] Bar {CurrentBar}: LONG retrace exit DEFERRED (favorable gradient). Will re-check next bar.");
                            Print($"[RETRACE_EXIT]   Close={currentClose:F2}, FastEMA={currentFastEma:F2}, AboveEMA={barAboveEma}, Gradient={currentGradDeg:F2}°");
                            return;
                        }

                        // Defer one bar if bullish EMA conditions are present
                        if (deferLongDueToBullishEma && !pendingRetraceExitLong)
                        {
                            pendingRetraceExitLong = true;
                            pendingRetraceExitLongBar = CurrentBar;
                            Print($"[RETRACE_EXIT] Bar {CurrentBar}: LONG retrace exit DEFERRED (bullish EMA). Will re-check next bar.");
                            Print($"[RETRACE_EXIT]   Close={currentClose:F2}, FastEMA={currentFastEma:F2}, AboveEMA={barAboveEma}, Gradient={currentGradDeg:F2}°");
                            return;
                        }

                        // If we deferred on the current bar, wait until next bar to re-check
                        if (pendingRetraceExitLong && CurrentBar == pendingRetraceExitLongBar)
                        {
                            return;
                        }

                        // Clear defer flag after the next bar re-check
                        if (pendingRetraceExitLong && CurrentBar >= pendingRetraceExitLongBar + 1)
                        {
                            pendingRetraceExitLong = false;
                            pendingRetraceExitLongBar = -1;
                        }

                        Print($"[RETRACE_EXIT] Bar {CurrentBar}: LONG retrace exit triggered");
                        Print($"[RETRACE_EXIT]   MFE={trendMaxProfit:F2}, Current profit={currentProfit:F2}, Retrace fraction={TrendRetraceFraction:F2}");
                        Print($"[RETRACE_EXIT]   Current bar: Close={currentClose:F2}, FastEMA={currentFastEma:F2}, AboveEMA={barAboveEma}, Gradient={currentGradDeg:F2}°");
                        Print($"[RETRACE_EXIT]   NOTE: Retrace exit is based on profit retracement, not current bar conditions");
                        ExitLongTrace("BarsOnTheFlowRetrace", GetActiveEntrySignal(), "BarsOnTheFlowRetrace");
                        RecordExitForCooldown(MarketPosition.Long);
                    }
                    else if (Position.MarketPosition == MarketPosition.Short)
                    {
                        // If favorable gradient is enabled, keep deferring as long as gradient favors the trade
                        if (deferShortDueToFavorableGradient)
                        {
                            Print($"[RETRACE_EXIT] Bar {CurrentBar}: SHORT retrace exit DEFERRED (favorable gradient). Will re-check next bar.");
                            Print($"[RETRACE_EXIT]   Close={currentClose:F2}, FastEMA={currentFastEma:F2}, BelowEMA={!barAboveEma}, Gradient={currentGradDeg:F2}°");
                            return;
                        }

                        // Defer one bar if bearish EMA conditions are present
                        if (deferShortDueToBullishEma && !pendingRetraceExitShort)
                        {
                            pendingRetraceExitShort = true;
                            pendingRetraceExitShortBar = CurrentBar;
                            Print($"[RETRACE_EXIT] Bar {CurrentBar}: SHORT retrace exit DEFERRED (bearish EMA). Will re-check next bar.");
                            Print($"[RETRACE_EXIT]   Close={currentClose:F2}, FastEMA={currentFastEma:F2}, BelowEMA={!barAboveEma}, Gradient={currentGradDeg:F2}°");
                            return;
                        }

                        // If we deferred on the current bar, wait until next bar to re-check
                        if (pendingRetraceExitShort && CurrentBar == pendingRetraceExitShortBar)
                        {
                            return;
                        }

                        // Clear defer flag after the next bar re-check
                        if (pendingRetraceExitShort && CurrentBar >= pendingRetraceExitShortBar + 1)
                        {
                            pendingRetraceExitShort = false;
                            pendingRetraceExitShortBar = -1;
                        }

                        Print($"[RETRACE_EXIT] Bar {CurrentBar}: SHORT retrace exit triggered");
                        Print($"[RETRACE_EXIT]   MFE={trendMaxProfit:F2}, Current profit={currentProfit:F2}, Retrace fraction={TrendRetraceFraction:F2}");
                        Print($"[RETRACE_EXIT]   Current bar: Close={currentClose:F2}, FastEMA={currentFastEma:F2}, BelowEMA={!barAboveEma}, Gradient={currentGradDeg:F2}°");
                        Print($"[RETRACE_EXIT]   NOTE: Retrace exit is based on profit retracement, not current bar conditions");
                        ExitShortTrace("BarsOnTheFlowRetraceS", GetActiveEntrySignal(), "BarsOnTheFlowRetraceS");
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
            pendingRetraceExitLong = false;
            pendingRetraceExitLongBar = -1;
            pendingRetraceExitShort = false;
            pendingRetraceExitShortBar = -1;
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
        private static DateTime lastBarSampleRequestTime = DateTime.MinValue; // track last request time for rate limiting
        private static System.Threading.SemaphoreSlim barSampleSemaphore = new System.Threading.SemaphoreSlim(3, 3); // limit to 3 concurrent requests (very low to prevent blocking)
        private System.Threading.CancellationTokenSource cancellationTokenSource; // cancellation token for HTTP requests
        
        // In-memory storage for historical bar data - avoids HTTP during fast historical playback
        private System.Collections.Generic.List<string> historicalBarDataList = new System.Collections.Generic.List<string>();
        private readonly object historicalBarDataLock = new object();

        private void CreateBarNavPanel()
        {
            if (ChartControl == null || ChartControl.Parent == null)
                return;

            try
            {
                // Create main container grid (position via Margin; drag handle at top)
                barNavPanel = new System.Windows.Controls.Grid
                {
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top,
                    Margin = barNavPanelSavedMargin ?? new System.Windows.Thickness(0, 10, 0, 0),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 30, 30, 30)),
                    Width = 260
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

                // Create row definitions (row 0 = drag handle)
                barNavPanel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                barNavPanel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                barNavPanel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                barNavPanel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                barNavPanel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                barNavPanel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

                // Create column definitions for first row (bar navigation)
                barNavPanel.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                barNavPanel.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

                // ===== ROW 0: DRAG HANDLE =====
                var barNavDragHandle = new System.Windows.Controls.Border
                {
                    Height = 14,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 45)),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 70, 70)),
                    BorderThickness = new System.Windows.Thickness(0, 0, 0, 1),
                    Cursor = System.Windows.Input.Cursors.SizeAll,
                    ToolTip = "Drag to move panel"
                };
                System.Windows.Controls.Grid.SetColumn(barNavDragHandle, 0);
                System.Windows.Controls.Grid.SetRow(barNavDragHandle, 0);
                System.Windows.Controls.Grid.SetColumnSpan(barNavDragHandle, 2);

                barNavDragHandle.Child = new System.Windows.Controls.TextBlock
                {
                    Text = "⋮⋮  BarsOnTheFlow",
                    FontSize = 9,
                    FontWeight = System.Windows.FontWeights.SemiBold,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    IsHitTestVisible = false
                };

                barNavDragHandle.MouseLeftButtonDown += BarNavPanelDragHandle_MouseLeftButtonDown;
                barNavDragHandle.MouseMove += BarNavPanelDragHandle_MouseMove;
                barNavDragHandle.MouseLeftButtonUp += BarNavPanelDragHandle_MouseLeftButtonUp;
                barNavDragHandle.LostMouseCapture += BarNavPanelDragHandle_LostMouseCapture;

                // ===== ROW 1: BAR NAVIGATION =====
                
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
                System.Windows.Controls.Grid.SetRow(barNavTextBox, 1);
                
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
                System.Windows.Controls.Grid.SetRow(barNavButton, 1);

                // Handle button click
                barNavButton.Click += (sender, e) => NavigateToBar();

                // ===== ROW 2: STOP LOSS CONTROLS =====
                
                // Create a horizontal StackPanel for stop loss controls
                var stopLossPanel = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new System.Windows.Thickness(5, 0, 5, 5)
                };
                System.Windows.Controls.Grid.SetColumn(stopLossPanel, 0);
                System.Windows.Controls.Grid.SetRow(stopLossPanel, 2);
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
                double initSlPoints = CalculateStopLossTicks() / 4.0;
                stopLossTextBox = new System.Windows.Controls.TextBox
                {
                    Text = initSlPoints.ToString("F1"),
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
                    ToolTip = "Calculated stop loss in points (updates each bar)",
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

                // ===== ROW 3: LIVE POSITION / BRACKET PRICES =====
                var positionInfoPanel = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Vertical,
                    Margin = new System.Windows.Thickness(8, 0, 8, 4)
                };
                System.Windows.Controls.Grid.SetColumn(positionInfoPanel, 0);
                System.Windows.Controls.Grid.SetRow(positionInfoPanel, 3);
                System.Windows.Controls.Grid.SetColumnSpan(positionInfoPanel, 2);

                positionEntryLine = CreatePositionInfoLine("Entry: —", System.Windows.Media.Brushes.White);
                positionStopLine = CreatePositionInfoLine("Stop: —", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 113, 113)));
                positionTargetLine = CreatePositionInfoLine("Target: —", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 222, 128)));
                positionInfoPanel.Children.Add(positionEntryLine);
                positionInfoPanel.Children.Add(positionStopLine);
                positionInfoPanel.Children.Add(positionTargetLine);

                // ===== ROW 4: TRADING CONTROLS =====
                var tradingControlPanel = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new System.Windows.Thickness(5, 0, 5, 6)
                };
                System.Windows.Controls.Grid.SetColumn(tradingControlPanel, 0);
                System.Windows.Controls.Grid.SetRow(tradingControlPanel, 4);
                System.Windows.Controls.Grid.SetColumnSpan(tradingControlPanel, 2);

                pauseTradingButton = new System.Windows.Controls.Button
                {
                    Content = "Pause",
                    FontSize = 11,
                    FontWeight = System.Windows.FontWeights.Bold,
                    Width = 48,
                    Height = 28,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 179, 8)),
                    Foreground = System.Windows.Media.Brushes.Black,
                    BorderThickness = new System.Windows.Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Pause new entries"
                };
                pauseTradingButton.Click += (sender, e) =>
                {
                    TriggerCustomEvent(_ =>
                    {
                        pauseTradingFromPanel = !pauseTradingFromPanel;
                        Print($"[PANEL] PauseTradingFromPanel={pauseTradingFromPanel}");
                    }, null);
                    UpdatePauseTradingButton();
                };

                flattenButton = new System.Windows.Controls.Button
                {
                    Content = "Flatten",
                    FontSize = 11,
                    FontWeight = System.Windows.FontWeights.Bold,
                    Width = 36,
                    Height = 28,
                    Margin = new System.Windows.Thickness(1, 0, 0, 0),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new System.Windows.Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Flatten position and cancel working orders"
                };
                flattenButton.Click += (sender, e) =>
                {
                    TriggerCustomEvent(_ => FlattenFromPanel(), null);
                };

                tradingControlPanel.Children.Add(pauseTradingButton);
                tradingControlPanel.Children.Add(flattenButton);

                // Live parameter reload: Load Default.xml without restarting strategy
                var loadDefaultsButton = new System.Windows.Controls.Button
                {
                    Content = "Load Default.xml",
                    FontSize = 10,
                    FontWeight = System.Windows.FontWeights.Bold,
                    Width = 76,
                    Height = 28,
                    Margin = new System.Windows.Thickness(2, 0, 0, 0),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new System.Windows.Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Load BarsOnTheFlow Default.xml parameters live (no restart)"
                };
                loadDefaultsButton.Click += (sender, e) =>
                {
                    TriggerCustomEvent(_ => LoadDefaultsFromTemplate(), null);
                };
                tradingControlPanel.Children.Add(loadDefaultsButton);

                // ===== ROW 5: MANUAL ENTRY (BUY / SELL) =====
                var manualEntryPanel = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new System.Windows.Thickness(5, 0, 5, 8)
                };
                System.Windows.Controls.Grid.SetColumn(manualEntryPanel, 0);
                System.Windows.Controls.Grid.SetRow(manualEntryPanel, 5);
                System.Windows.Controls.Grid.SetColumnSpan(manualEntryPanel, 2);

                var buyButton = new System.Windows.Controls.Button
                {
                    Content = "Buy",
                    FontSize = 11,
                    FontWeight = System.Windows.FontWeights.Bold,
                    Width = 110,
                    Height = 28,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new System.Windows.Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Manually enter LONG at market (uses current Contracts and stop settings)"
                };
                buyButton.Click += (sender, e) =>
                {
                    TriggerCustomEvent(_ => ManualEntryFromPanel(MarketPosition.Long), null);
                };

                var sellButton = new System.Windows.Controls.Button
                {
                    Content = "Sell",
                    FontSize = 11,
                    FontWeight = System.Windows.FontWeights.Bold,
                    Width = 110,
                    Height = 28,
                    Margin = new System.Windows.Thickness(6, 0, 0, 0),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new System.Windows.Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Manually enter SHORT at market (uses current Contracts and stop settings)"
                };
                sellButton.Click += (sender, e) =>
                {
                    TriggerCustomEvent(_ => ManualEntryFromPanel(MarketPosition.Short), null);
                };

                manualEntryPanel.Children.Add(buyButton);
                manualEntryPanel.Children.Add(sellButton);

                // Add all controls to main panel
                barNavPanel.Children.Add(barNavDragHandle);
                barNavPanel.Children.Add(barNavTextBox);
                barNavPanel.Children.Add(barNavButton);
                barNavPanel.Children.Add(stopLossPanel);
                barNavPanel.Children.Add(positionInfoPanel);
                barNavPanel.Children.Add(tradingControlPanel);
                barNavPanel.Children.Add(manualEntryPanel);

                // Add panel to chart
                if (ChartControl.Parent is System.Windows.Controls.Grid)
                {
                    var parent = ChartControl.Parent as System.Windows.Controls.Grid;
                    parent.Children.Add(barNavPanel);

                    if (!barNavPanelSavedMargin.HasValue)
                    {
                        barNavPanel.Loaded += BarNavPanel_InitialPositionLoaded;
                    }
                }

                UpdatePauseTradingButton();
                UpdateBarNavPositionLabels(force: true);
            }
            catch (Exception ex)
            {
                Print($"[BarsOnTheFlow] Failed to create bar navigation panel: {ex.Message}");
            }
        }

        private void CreateEmaLegendPanel()
        {
            if (ChartControl == null || ChartControl.Parent == null || emaLegendPanel != null)
                return;

            try
            {
                emaLegendFastLabel = CreateEmaLegendLabel();
                emaLegendSlowLabel = CreateEmaLegendLabel();
                emaLegend21Label = CreateEmaLegendLabel();

                var legendContent = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Vertical,
                    IsHitTestVisible = false
                };
                legendContent.Children.Add(CreateEmaLegendRow(System.Windows.Media.Brushes.Blue, emaLegendFastLabel));
                legendContent.Children.Add(CreateEmaLegendRow(System.Windows.Media.Brushes.DarkOrange, emaLegendSlowLabel));
                legendContent.Children.Add(CreateEmaLegendRow(System.Windows.Media.Brushes.MediumPurple, emaLegend21Label));

                var emaLegendDragHandle = new System.Windows.Controls.Border
                {
                    Height = 12,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 45)),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 70, 70)),
                    BorderThickness = new System.Windows.Thickness(0, 0, 0, 1),
                    Cursor = System.Windows.Input.Cursors.SizeAll,
                    ToolTip = "Drag to move EMA legend"
                };
                emaLegendDragHandle.Child = new System.Windows.Controls.TextBlock
                {
                    Text = "⋮⋮  EMA Legend",
                    FontSize = 8,
                    FontWeight = System.Windows.FontWeights.SemiBold,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    IsHitTestVisible = false
                };
                emaLegendDragHandle.MouseLeftButtonDown += EmaLegendPanelDragHandle_MouseLeftButtonDown;
                emaLegendDragHandle.MouseMove += EmaLegendPanelDragHandle_MouseMove;
                emaLegendDragHandle.MouseLeftButtonUp += EmaLegendPanelDragHandle_MouseLeftButtonUp;
                emaLegendDragHandle.LostMouseCapture += EmaLegendPanelDragHandle_LostMouseCapture;

                var legendGrid = new System.Windows.Controls.Grid();
                legendGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                legendGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                System.Windows.Controls.Grid.SetRow(emaLegendDragHandle, 0);
                System.Windows.Controls.Grid.SetRow(legendContent, 1);
                legendGrid.Children.Add(emaLegendDragHandle);
                legendGrid.Children.Add(legendContent);

                emaLegendPanel = new System.Windows.Controls.Border
                {
                    Child = legendGrid,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top,
                    Margin = emaLegendPanelSavedMargin ?? new System.Windows.Thickness(8, 10, 0, 0),
                    Padding = new System.Windows.Thickness(8, 0, 10, 6),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(210, 30, 30, 30)),
                    CornerRadius = new System.Windows.CornerRadius(4)
                };

                emaLegendPanel.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = System.Windows.Media.Colors.Black,
                    Direction = 320,
                    ShadowDepth = 2,
                    Opacity = 0.4,
                    BlurRadius = 4
                };

                UpdateEmaLegendLabels();

                if (ChartControl.Parent is System.Windows.Controls.Grid parent)
                    parent.Children.Add(emaLegendPanel);
            }
            catch (Exception ex)
            {
                Print($"[BarsOnTheFlow] Failed to create EMA legend: {ex.Message}");
            }
        }

        private static System.Windows.Controls.TextBlock CreateEmaLegendLabel()
        {
            return new System.Windows.Controls.TextBlock
            {
                FontSize = 10,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(226, 232, 240)),
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
        }

        private static System.Windows.Controls.StackPanel CreateEmaLegendRow(System.Windows.Media.Brush swatchBrush, System.Windows.Controls.TextBlock label)
        {
            var row = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new System.Windows.Thickness(0, 1, 0, 1)
            };

            row.Children.Add(new System.Windows.Controls.Border
            {
                Width = 18,
                Height = 3,
                Background = swatchBrush,
                Margin = new System.Windows.Thickness(0, 0, 6, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                CornerRadius = new System.Windows.CornerRadius(1)
            });
            row.Children.Add(label);
            return row;
        }

        private void UpdateEmaLegendLabels()
        {
            if (emaLegendFastLabel != null)
                emaLegendFastLabel.Text = $"Fast EMA {FastEmaPeriod}";
            if (emaLegendSlowLabel != null)
                emaLegendSlowLabel.Text = $"Slow EMA {EmaSlowPeriod}";
            if (emaLegend21Label != null)
                emaLegend21Label.Text = $"EMA {Ema21Period}";
        }

        private void EmaLegendPanelDragHandle_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (emaLegendPanel == null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
                return;

            var handle = sender as System.Windows.UIElement;
            if (handle == null)
                return;

            var parent = emaLegendPanel.Parent as System.Windows.FrameworkElement;
            if (parent == null)
                return;

            emaLegendPanelDragging = true;
            emaLegendPanelDragStart = e.GetPosition(parent);
            emaLegendPanelMarginAtDragStart = emaLegendPanel.Margin;
            handle.CaptureMouse();
            e.Handled = true;
        }

        private void EmaLegendPanelDragHandle_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!emaLegendPanelDragging || emaLegendPanel == null)
                return;

            var handle = sender as System.Windows.UIElement;
            if (handle == null || !handle.IsMouseCaptured)
                return;

            var parent = emaLegendPanel.Parent as System.Windows.FrameworkElement;
            if (parent == null)
                return;

            var current = e.GetPosition(parent);
            double deltaX = current.X - emaLegendPanelDragStart.X;
            double deltaY = current.Y - emaLegendPanelDragStart.Y;
            double panelWidth = emaLegendPanel.ActualWidth > 0 ? emaLegendPanel.ActualWidth : 120;
            double panelHeight = emaLegendPanel.ActualHeight > 0 ? emaLegendPanel.ActualHeight : 80;
            double newLeft = emaLegendPanelMarginAtDragStart.Left + deltaX;
            double newTop = emaLegendPanelMarginAtDragStart.Top + deltaY;

            if (parent.ActualWidth > 0)
                newLeft = Math.Max(0, Math.Min(newLeft, parent.ActualWidth - panelWidth));
            if (parent.ActualHeight > 0)
                newTop = Math.Max(0, Math.Min(newTop, parent.ActualHeight - panelHeight));

            emaLegendPanel.Margin = new System.Windows.Thickness(newLeft, newTop, 0, 0);
        }

        private void EmaLegendPanelDragHandle_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            EndEmaLegendPanelDrag(sender as System.Windows.UIElement);
            e.Handled = true;
        }

        private void EmaLegendPanelDragHandle_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        {
            EndEmaLegendPanelDrag(sender as System.Windows.UIElement);
        }

        private void EndEmaLegendPanelDrag(System.Windows.UIElement handle)
        {
            if (!emaLegendPanelDragging)
                return;

            emaLegendPanelDragging = false;
            handle?.ReleaseMouseCapture();
            if (emaLegendPanel != null)
                emaLegendPanelSavedMargin = emaLegendPanel.Margin;
        }

        private void BarNavPanel_InitialPositionLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (barNavPanel == null || barNavPanelSavedMargin.HasValue)
                return;

            barNavPanel.Loaded -= BarNavPanel_InitialPositionLoaded;

            var parent = barNavPanel.Parent as System.Windows.FrameworkElement;
            if (parent == null || parent.ActualWidth <= 0)
                return;

            double panelWidth = barNavPanel.ActualWidth > 0 ? barNavPanel.ActualWidth : barNavPanel.Width;
            double left = Math.Max(0, parent.ActualWidth - panelWidth - 100);
            var margin = new System.Windows.Thickness(left, 10, 0, 0);
            barNavPanel.Margin = margin;
            barNavPanelSavedMargin = margin;
        }

        private void BarNavPanelDragHandle_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (barNavPanel == null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
                return;

            var handle = sender as System.Windows.UIElement;
            if (handle == null)
                return;

            var parent = barNavPanel.Parent as System.Windows.FrameworkElement;
            if (parent == null)
                return;

            barNavPanelDragging = true;
            barNavPanelDragStart = e.GetPosition(parent);
            barNavPanelMarginAtDragStart = barNavPanel.Margin;
            handle.CaptureMouse();
            e.Handled = true;
        }

        private void BarNavPanelDragHandle_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!barNavPanelDragging || barNavPanel == null)
                return;

            var handle = sender as System.Windows.UIElement;
            if (handle == null || !handle.IsMouseCaptured)
                return;

            var parent = barNavPanel.Parent as System.Windows.FrameworkElement;
            if (parent == null)
                return;

            var current = e.GetPosition(parent);
            double deltaX = current.X - barNavPanelDragStart.X;
            double deltaY = current.Y - barNavPanelDragStart.Y;
            double panelWidth = barNavPanel.ActualWidth > 0 ? barNavPanel.ActualWidth : barNavPanel.Width;
            double panelHeight = barNavPanel.ActualHeight > 0 ? barNavPanel.ActualHeight : 200;
            double newLeft = barNavPanelMarginAtDragStart.Left + deltaX;
            double newTop = barNavPanelMarginAtDragStart.Top + deltaY;

            if (parent.ActualWidth > 0)
                newLeft = Math.Max(0, Math.Min(newLeft, parent.ActualWidth - panelWidth));
            if (parent.ActualHeight > 0)
                newTop = Math.Max(0, Math.Min(newTop, parent.ActualHeight - panelHeight));

            barNavPanel.Margin = new System.Windows.Thickness(newLeft, newTop, 0, 0);
        }

        private void BarNavPanelDragHandle_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            EndBarNavPanelDrag(sender as System.Windows.UIElement);
            e.Handled = true;
        }

        private void BarNavPanelDragHandle_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        {
            EndBarNavPanelDrag(sender as System.Windows.UIElement);
        }

        private void EndBarNavPanelDrag(System.Windows.UIElement handle)
        {
            if (!barNavPanelDragging)
                return;

            barNavPanelDragging = false;
            handle?.ReleaseMouseCapture();
            if (barNavPanel != null)
                barNavPanelSavedMargin = barNavPanel.Margin;
        }

        private static System.Windows.Controls.TextBlock CreatePositionInfoLine(string text, System.Windows.Media.Brush foreground)
        {
            return new System.Windows.Controls.TextBlock
            {
                Text = text,
                FontSize = 11,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                Foreground = foreground,
                Margin = new System.Windows.Thickness(0, 1, 0, 1)
            };
        }

        private static string FormatPanelPrice(double price)
        {
            return double.IsNaN(price) || price <= 0 ? "—" : price.ToString("F2");
        }

        private void TryGetWorkingBracketOrderPrices(MarketPosition pos, string entrySignal,
            out double stopPrice, out bool hasStop, out double targetPrice, out bool hasTarget)
        {
            stopPrice = targetPrice = double.NaN;
            hasStop = hasTarget = false;

            if (Account == null || pos == MarketPosition.Flat)
                return;

            foreach (Order order in Account.Orders)
            {
                if (order == null || order.Instrument != Instrument)
                    continue;

                if (order.OrderState != OrderState.Working
                    && order.OrderState != OrderState.Accepted
                    && order.OrderState != OrderState.PartFilled)
                    continue;

                string from = order.FromEntrySignal ?? string.Empty;
                if (!string.IsNullOrEmpty(entrySignal))
                {
                    if (from != entrySignal)
                        continue;
                }
                else if (!IsBarsOnTheFlowEntrySignalName(from))
                {
                    continue;
                }

                if (order.OrderType == OrderType.StopMarket || order.OrderType == OrderType.StopLimit)
                {
                    double px = order.StopPrice > 0 ? order.StopPrice : order.LimitPrice;
                    if (px > 0)
                    {
                        stopPrice = px;
                        hasStop = true;
                    }
                    continue;
                }

                if (order.OrderType != OrderType.Limit || order.LimitPrice <= 0)
                    continue;

                bool isExitLimit = (pos == MarketPosition.Long && order.OrderAction == OrderAction.Sell)
                    || (pos == MarketPosition.Short && order.OrderAction == OrderAction.BuyToCover);
                if (!isExitLimit)
                    continue;

                if (order.Name == "Profit target" || !hasTarget)
                {
                    targetPrice = order.LimitPrice;
                    hasTarget = true;
                }
            }
        }

        private void UpdateBarNavPositionLabels(bool force = false)
        {
            if (positionEntryLine == null || ChartControl == null)
                return;

            double entry = double.NaN;
            double stop = double.NaN;
            double target = double.NaN;
            bool hasStop = false;
            bool hasTarget = false;
            string stopLabel = "Stop";
            string targetLabel = "Target";

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                entry = Position.AveragePrice;
                TryGetWorkingBracketOrderPrices(Position.MarketPosition, GetActiveEntrySignal(),
                    out stop, out hasStop, out target, out hasTarget);

                if (Position.MarketPosition == MarketPosition.Long)
                {
                    stopLabel = "Sell stop";
                    targetLabel = "Sell limit";
                }
                else
                {
                    stopLabel = "Buy stop";
                    targetLabel = "Buy limit";
                }
            }

            double entryKey = entry;
            double stopKey = hasStop ? stop : double.NaN;
            double targetKey = hasTarget ? target : double.NaN;
            if (!force
                && entryKey == lastPanelEntryPrice
                && stopKey == lastPanelStopPrice
                && targetKey == lastPanelTargetPrice)
                return;

            lastPanelEntryPrice = entryKey;
            lastPanelStopPrice = stopKey;
            lastPanelTargetPrice = targetKey;

            string entryText = $"Entry: {FormatPanelPrice(entry)}";
            string stopText = $"{stopLabel}: {FormatPanelPrice(hasStop ? stop : double.NaN)}";
            string targetText = $"{targetLabel}: {FormatPanelPrice(hasTarget ? target : double.NaN)}";

            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                if (positionEntryLine == null)
                    return;
                positionEntryLine.Text = entryText;
                positionStopLine.Text = stopText;
                positionTargetLine.Text = targetText;
            });
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
                    string orderName = GetActiveEntrySignal();
                    if (newStopLoss > 0)
                    {
                        if (IsProfitFloorActiveForOrder(orderName))
                        {
                            SetProfitFloorClampedStop(orderName, profitFloorStopPrice, "panel-adjust");
                            Print($"[Stop Loss] Profit floor active @ {profitFloorStopPrice:F2} — applied floor-clamped trail update");
                        }
                        else if (ShouldAllowTrailStop(orderName))
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

        /// <summary>
        /// Load key parameters from the BarsOnTheFlow Default.xml template live, without restarting the strategy.
        /// Applies mainly risk/stop-related settings so you can iterate quickly during a run.
        /// </summary>
        private void LoadDefaultsFromTemplate()
        {
            try
            {
                string userDir = NinjaTrader.Core.Globals.UserDataDir;
                string templateDir = System.IO.Path.Combine(userDir, "templates", "Strategy", "BarsOnTheFlow");
                string templatePath = System.IO.Path.Combine(templateDir, "Default.xml");
                
                if (!System.IO.File.Exists(templatePath))
                {
                    Print($"[LoadDefaults] Default.xml not found at {templatePath}");
                    return;
                }

                var doc = System.Xml.Linq.XDocument.Load(templatePath);
                var botf = doc.Root;
                if (botf == null)
                {
                    Print("[LoadDefaults] Default.xml missing root element");
                    return;
                }

                // Navigate to <BarsOnTheFlow> state section (StrategyTemplate/Strategy/BarsOnTheFlow)
                var barsNode = doc.Descendants("BarsOnTheFlow").FirstOrDefault();
                if (barsNode == null)
                {
                    Print("[LoadDefaults] Default.xml missing <BarsOnTheFlow> state section");
                    return;
                }

                var ci = System.Globalization.CultureInfo.InvariantCulture;

                double readDouble(string name, double current)
                {
                    var el = barsNode.Element(name);
                    if (el == null || string.IsNullOrWhiteSpace(el.Value))
                        return current;
                    if (double.TryParse(el.Value, System.Globalization.NumberStyles.Any, ci, out double v))
                        return v;
                    return current;
                }

                int readInt(string name, int current)
                {
                    var el = barsNode.Element(name);
                    if (el == null || string.IsNullOrWhiteSpace(el.Value))
                        return current;
                    if (int.TryParse(el.Value, System.Globalization.NumberStyles.Any, ci, out int v))
                        return v;
                    return current;
                }

                bool readBool(string name, bool current)
                {
                    var el = barsNode.Element(name);
                    if (el == null || string.IsNullOrWhiteSpace(el.Value))
                        return current;
                    if (bool.TryParse(el.Value, out bool v))
                        return v;
                    return current;
                }

                // Apply the most important risk / stop parameters live
                double newMinStopLossPoints = readDouble("MinStopLossPoints", MinStopLossPoints);
                double newEmaStopMinDist = readDouble("EmaStopMinDistanceFromEntry", EmaStopMinDistanceFromEntry);
                int newStopLossPoints = readInt("StopLossPoints", StopLossPoints);
                bool newUseEmaTrailingStop = readBool("UseEmaTrailingStop", UseEmaTrailingStop);
                bool newUseBreakEven = readBool("UseBreakEven", UseBreakEven);
                int newBreakEvenTrigger = readInt("BreakEvenTrigger", BreakEvenTrigger);
                int newBreakEvenOffset = readInt("BreakEvenOffset", BreakEvenOffset);
                bool newUseChartScaledFastGradDeg = readBool("UseChartScaledFastGradDeg", UseChartScaledFastGradDeg);

                MinStopLossPoints = newMinStopLossPoints;
                EmaStopMinDistanceFromEntry = newEmaStopMinDist;
                StopLossPoints = newStopLossPoints;
                UseEmaTrailingStop = newUseEmaTrailingStop;
                UseBreakEven = newUseBreakEven;
                BreakEvenTrigger = newBreakEvenTrigger;
                BreakEvenOffset = newBreakEvenOffset;
                UseChartScaledFastGradDeg = newUseChartScaledFastGradDeg;

                Print($"[LoadDefaults] Applied Default.xml: StopLossPoints={StopLossPoints}, MinStopLossPoints={MinStopLossPoints}, EmaStopMinDistanceFromEntry={EmaStopMinDistanceFromEntry}, UseEmaTrailingStop={UseEmaTrailingStop}, UseBreakEven={UseBreakEven}, BreakEvenTrigger={BreakEvenTrigger}, BreakEvenOffset={BreakEvenOffset}, UseChartScaledFastGradDeg={UseChartScaledFastGradDeg}");

                // Re-apply stop loss to any open position using the updated parameters
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    string orderName = GetActiveEntrySignal();
                    ApplyStopLoss(orderName);
                }

                // Refresh strategy state file so dashboard/state page sees the new parameters
                ExportStrategyState();
            }
            catch (Exception ex)
            {
                Print($"[LoadDefaults] Failed to load Default.xml: {ex.Message}");
            }
        }

        private void UpdatePauseTradingButton()
        {
            if (ChartControl == null)
                return;

            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                if (pauseTradingButton == null)
                    return;

                if (pauseTradingFromPanel)
                {
                    pauseTradingButton.Content = "Resume";
                    pauseTradingButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
                    pauseTradingButton.Foreground = System.Windows.Media.Brushes.White;
                    pauseTradingButton.ToolTip = "Resume new entries";
                }
                else
                {
                    pauseTradingButton.Content = "Pause";
                    pauseTradingButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 179, 8));
                    pauseTradingButton.Foreground = System.Windows.Media.Brushes.Black;
                    pauseTradingButton.ToolTip = "Pause new entries";
                }
            });
        }

        private void ManualEntryFromPanel(MarketPosition direction)
        {
            try
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    Print($"[PANEL] Manual {direction} entry ignored - already in {Position.MarketPosition} position (flatten first)");
                    return;
                }

                if (direction == MarketPosition.Long)
                {
                    Print($"[PANEL] Manual entry requested - LONG {Math.Max(1, Contracts)} @ market");
                    currentTradeEntryReason = "ManualPanelEntry";
                    SubmitLongEntry();
                    lastEntryBarIndex = CurrentBar;
                    lastEntryDirection = MarketPosition.Long;
                    intendedPosition = MarketPosition.Long;
                }
                else
                {
                    Print($"[PANEL] Manual entry requested - SHORT {Math.Max(1, Contracts)} @ market");
                    currentTradeEntryReason = "ManualPanelEntry";
                    SubmitShortEntry();
                    lastEntryBarIndex = CurrentBar;
                    lastEntryDirection = MarketPosition.Short;
                    intendedPosition = MarketPosition.Short;
                }
            }
            catch (Exception ex)
            {
                Print($"[PANEL] Manual entry error: {ex.Message}");
            }
        }

        private void FlattenFromPanel()
        {
            try
            {
                if (Account != null)
                {
                    foreach (Order order in Account.Orders)
                    {
                        if (order != null && order.OrderState == OrderState.Working && order.Instrument == Instrument)
                        {
                            CancelOrder(order);
                        }
                    }
                }

                if (Position.MarketPosition == MarketPosition.Long)
                {
                    Print($"[PANEL] Flatten requested - exiting LONG");
                    ExitLongTrace("BarsOnTheFlowManualFlatten", GetActiveEntrySignal(), "BarsOnTheFlowManualFlatten");
                }
                else if (Position.MarketPosition == MarketPosition.Short)
                {
                    Print($"[PANEL] Flatten requested - exiting SHORT");
                    ExitShortTrace("BarsOnTheFlowManualFlattenS", GetActiveEntrySignal(), "BarsOnTheFlowManualFlattenS");
                }

                intendedPosition = MarketPosition.Flat;
            }
            catch (Exception ex)
            {
                Print($"[PANEL] Flatten error: {ex.Message}");
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
                double entryPrice = Close[0]; // Approximate entry price (we're about to enter)
                double stopLossPrice = Instrument.MasterInstrument.RoundToTickSize(entryFastEmaValue);

                // Enforce minimum distance from entry so the initial stop isn't tighter than EmaStopMinDistanceFromEntry
                // (otherwise the stop order can be hit by noise before activation delay/min profit even apply)
                if (EmaStopMinDistanceFromEntry > 0)
                {
                    if (direction == MarketPosition.Long)
                    {
                        // For LONG: protective stop must be BELOW entry. If EMA stop is too tight (too high),
                        // clamp it DOWN so it stays at least EmaStopMinDistanceFromEntry below entry.
                        double maxAllowedStop = entryPrice - EmaStopMinDistanceFromEntry;
                        if (stopLossPrice > maxAllowedStop)
                            stopLossPrice = Instrument.MasterInstrument.RoundToTickSize(maxAllowedStop);
                    }
                    else
                    {
                        // For SHORT: protective stop must be ABOVE entry. If EMA stop is too tight (too low),
                        // clamp it UP so it stays at least EmaStopMinDistanceFromEntry above entry.
                        double minAllowedStop = entryPrice + EmaStopMinDistanceFromEntry;
                        if (stopLossPrice < minAllowedStop)
                            stopLossPrice = Instrument.MasterInstrument.RoundToTickSize(minAllowedStop);
                    }
                }

                currentEmaStopLoss = stopLossPrice;

                if (EmaStopTriggerMode == EmaStopTriggerModeType.CloseOnly)
                {
                    SetStopLoss(orderName, CalculationMode.Price, stopLossPrice, false);
                }

                currentTradeStopLossPoints = Math.Abs(entryPrice - stopLossPrice);
                Print($"[SetInitialStopLoss] EMA Trailing Stop: stop at {stopLossPrice:F4} (EMA={entryFastEmaValue:F4}, min dist={EmaStopMinDistanceFromEntry}), Direction={direction}, ~{currentTradeStopLossPoints:F2} pts from entry, TriggerMode={EmaStopTriggerMode}");
                return;
            }
            
            int stopLossTicks = CalculateStopLossTicks();
            int ticksPerPoint = GetTicksPerPoint();
            double stopLossPoints = (ticksPerPoint > 0) ? (stopLossTicks / (double)ticksPerPoint) : (stopLossTicks / 4.0);
            
            if (stopLossTicks <= 0)
            {
                stopLossTicks = Math.Max(4, ticksPerPoint);
                stopLossPoints = (ticksPerPoint > 0) ? (stopLossTicks / (double)ticksPerPoint) : 1.0;
                Print($"[SetInitialStopLoss] ERROR: Calculated stop was <=0 - enforcing minimum {stopLossTicks} ticks ({stopLossPoints:F2} pts)");
            }
            
            currentTradeStopLossPoints = stopLossPoints;
            Print($"[SetInitialStopLoss] Setting stop loss: {orderName}, Direction={direction}, StopLossTicks={stopLossTicks}, StopLossPoints={stopLossPoints:F2}, UseTrailingStop={UseTrailingStop}");

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

            if (UseProfitTarget && ProfitTargetPoints > 0)
            {
                int tpp = ticksPerPoint > 0 ? ticksPerPoint : 4;
                double targetTicks = ProfitTargetPoints * tpp;
                SetProfitTarget(orderName, CalculationMode.Ticks, targetTicks);
                Print($"[SetInitialStopLoss] Set PROFIT TARGET: {ProfitTargetPoints:F2} points ({targetTicks:F0} ticks, signal={orderName})");
            }
        }
        
        /// <summary>
        /// Apply stop loss when position is already open (e.g., for break-even adjustments).
        /// Use SetInitialStopLoss() when entering a new trade.
        /// </summary>
        private void ApplyStopLoss(string orderName)
        {
            int stopLossTicks = CalculateStopLossTicks();
            int ticksPerPoint = GetTicksPerPoint();
            double stopLossPoints = (ticksPerPoint > 0) ? (stopLossTicks / (double)ticksPerPoint) : (stopLossTicks / 4.0);
            Print($"[ApplyStopLoss] {orderName}, StopLossTicks={stopLossTicks}, StopLossPoints={stopLossPoints:F2}, Position={Position.MarketPosition}, UseTrailingStop={UseTrailingStop}, UseDynamicStopLoss={UseDynamicStopLoss}");
            
            if (stopLossTicks <= 0)
            {
                stopLossTicks = Math.Max(4, ticksPerPoint);
                stopLossPoints = (ticksPerPoint > 0) ? (stopLossTicks / (double)ticksPerPoint) : 1.0;
                Print($"[ApplyStopLoss] ERROR: Enforcing minimum {stopLossTicks} ticks ({stopLossPoints:F2} pts)");
            }
            
            // This method is for adjusting stop loss when position is already open
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (IsProfitFloorActiveForOrder(orderName))
                {
                    ReinforceProfitFloorStopIfNeeded();
                    Print($"[ApplyStopLoss] Profit floor active @ {profitFloorStopPrice:F2} — maintaining floor-clamped stop");
                    return;
                }

                // Store the stop loss distance for later use in exit reason
                currentTradeStopLossPoints = stopLossPoints;
                
                if (ShouldAllowTrailStop(orderName))
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
        private int cachedVolumeAwareStopMinuteOfDay = -1;
        private DateTime cachedVolumeAwareStopTime = DateTime.MinValue;

        /// <summary>Ticks per point from instrument (e.g. 4 for ES/MNQ with 0.25 tick size).</summary>
        private int GetTicksPerPoint()
        {
            double tickSize = Instrument.MasterInstrument.TickSize;
            if (tickSize <= 0) return 4;
            return Math.Max(1, (int)Math.Round(1.0 / tickSize));
        }

        /// <summary>Enforce minimum stop: MinStopLossPoints (if set) or at least 1 point in ticks.</summary>
        private int EnforceMinStopTicks(int ticks)
        {
            int ticksPerPoint = GetTicksPerPoint();
            int minTicks = 1 * ticksPerPoint; // default 1 point minimum
            if (MinStopLossPoints > 0)
            {
                int minFromPoints = (int)Math.Round(MinStopLossPoints * ticksPerPoint);
                if (minFromPoints > minTicks) minTicks = minFromPoints;
            }
            return Math.Max(ticks, minTicks);
        }

        private int CalculateStopLossTicks()
        {
            int ticksPerPoint = GetTicksPerPoint();

            if (!UseDynamicStopLoss || CurrentBar < DynamicStopLookback)
            {
                // Fixed stop: StopLossPoints -> ticks using instrument tick size, then multiplier
                int fixedStopTicks = (int)Math.Round(StopLossPoints * (double)ticksPerPoint);
                if (StopLossMultiplier != 1.0)
                    fixedStopTicks = Math.Max(1, (int)Math.Round(fixedStopTicks * StopLossMultiplier));
                return EnforceMinStopTicks(Math.Max(fixedStopTicks, 1));
            }

            // Try volume-aware stop from API first
            // NOTE: API already applies its own 1.2x scaling to avg_range, so we only
            // apply StopLossMultiplier (user headroom knob), NOT DynamicStopMultiplier.
            if (UseVolumeAwareStop)
            {
                int volumeAwareStop = GetVolumeAwareStopTicks();
                if (volumeAwareStop > 0)
                {
                    int adjustedStop = volumeAwareStop;
                    if (StopLossMultiplier != 1.0)
                        adjustedStop = Math.Max(1, (int)Math.Round(adjustedStop * StopLossMultiplier));
                    adjustedStop = EnforceMinStopTicks(adjustedStop);
                    Print($"[DynamicStopLoss] Volume-aware: {volumeAwareStop} ticks * SLMult={StopLossMultiplier} -> {adjustedStop} ticks (min={MinStopLossPoints}pts)");
                    return adjustedStop;
                }
            }

            // Fallback: average range of recent candles
            double totalRange = 0;
            int barsToCheck = Math.Min(DynamicStopLookback, CurrentBar);
            for (int i = 1; i <= barsToCheck; i++)
                totalRange += High[i] - Low[i];
            double averageRange = totalRange / barsToCheck;
            double stopLossPrice = averageRange * DynamicStopMultiplier;
            int stopLossTicks = (int)Math.Round(stopLossPrice * ticksPerPoint);
            if (StopLossMultiplier != 1.0)
                stopLossTicks = Math.Max(1, (int)Math.Round(stopLossTicks * StopLossMultiplier));
            stopLossTicks = EnforceMinStopTicks(stopLossTicks);
            Print($"[DynamicStopLoss] Fallback: Avg range {averageRange:F2} -> {stopLossTicks} ticks");
            return stopLossTicks;
        }

        private int GetVolumeAwareStopTicks()
        {
            try
            {
                int currentHour = Time[0].Hour;
                int currentMinute = Time[0].Minute;
                int minuteOfDay = currentHour * 60 + currentMinute;
                // .NET DayOfWeek: Sunday=0 … Saturday=6 → Python weekday: Monday=0 … Sunday=6
                int dow = ((int)Time[0].DayOfWeek + 6) % 7;

                if (cachedVolumeAwareStopTicks > 0 &&
                    cachedVolumeAwareStopMinuteOfDay == minuteOfDay &&
                    (DateTime.Now - cachedVolumeAwareStopTime).TotalSeconds < 30)
                {
                    return cachedVolumeAwareStopTicks;
                }

                EnsureHttpClient();

                long currentVolume = (long)Volume[1];

                string url = $"{DashboardBaseUrl.TrimEnd('/')}/api/volatility/recommended-stop?hour={currentHour}&minute={currentMinute}&dow={dow}&volume={currentVolume}&symbol=MNQ";

                var task = sharedClient.GetStringAsync(url);
                if (!task.Wait(200))
                {
                    Print("[VolumeAwareStop] API timeout");
                    return 0;
                }

                string json = task.Result;

                int startIdx = json.IndexOf("\"recommended_stop_ticks\":");
                if (startIdx < 0) return 0;

                startIdx += "\"recommended_stop_ticks\":".Length;
                int endIdx = json.IndexOfAny(new[] { ',', '}' }, startIdx);
                if (endIdx < 0) return 0;

                string ticksStr = json.Substring(startIdx, endIdx - startIdx).Trim();
                if (int.TryParse(ticksStr, out int ticks))
                {
                    string volumeCondition = "NORMAL";
                    int vcStart = json.IndexOf("\"volume_condition\":\"");
                    if (vcStart >= 0)
                    {
                        vcStart += "\"volume_condition\":\"".Length;
                        int vcEnd = json.IndexOf("\"", vcStart);
                        if (vcEnd > vcStart)
                            volumeCondition = json.Substring(vcStart, vcEnd - vcStart);
                    }

                    string confidence = "UNKNOWN";
                    int confStart = json.IndexOf("\"confidence\":\"");
                    if (confStart >= 0)
                    {
                        confStart += "\"confidence\":\"".Length;
                        int confEnd = json.IndexOf("\"", confStart);
                        if (confEnd > confStart)
                            confidence = json.Substring(confStart, confEnd - confStart);
                    }

                    string source = "?";
                    int srcStart = json.IndexOf("\"source\":\"");
                    if (srcStart >= 0)
                    {
                        srcStart += "\"source\":\"".Length;
                        int srcEnd = json.IndexOf("\"", srcStart);
                        if (srcEnd > srcStart)
                            source = json.Substring(srcStart, srcEnd - srcStart);
                    }

                    Print($"[VolumeAwareStop] {Time[0]:HH:mm} dow={dow} vol={currentVolume} cond={volumeCondition} conf={confidence} src={source} -> {ticks} ticks");

                    cachedVolumeAwareStopTicks = ticks;
                    cachedVolumeAwareStopMinuteOfDay = minuteOfDay;
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

        private bool IsCurrentBarDownByMinTicks()
        {
            if (MinTicksCurrentBarDirection <= 0)
                return Close[0] < Open[0];
            return Close[0] <= Open[0] - (MinTicksCurrentBarDirection * TickSize);
        }

        private bool IsCurrentBarUpByMinTicks()
        {
            if (MinTicksCurrentBarDirection <= 0)
                return Close[0] > Open[0];
            return Close[0] >= Open[0] + (MinTicksCurrentBarDirection * TickSize);
        }

        private DateTime lastTradingHoursNow = DateTime.MinValue;
        private string lastTradingHoursNowBasis = "";

        private DateTime GetTradingHoursNow()
        {
            // By default, use bar time (Time[0]) which is in the chart/data-series time zone.
            // Problem: users often set chart time zone to local time, but ETH windows like 18:00->17:00 are typically
            // specified in US Eastern (CME futures). If we treat those as local, we incorrectly block trades.
            //
            // Heuristic: if the window is the common CME ETH pattern (18:00->17:00), interpret "now" in Eastern Time
            // when bar time appears to match local machine time (within a few minutes).
            DateTime barNow = Time[0];
            lastTradingHoursNowBasis = "BarTime";
            lastTradingHoursNow = barNow;

            bool looksLikeCmeEth =
                TradingStartHour == 18 && TradingStartMinute == 0 &&
                TradingEndHour == 17 && TradingEndMinute == 0;

            if (!looksLikeCmeEth)
                return barNow;

            try
            {
                // If bar time is essentially local clock time, convert to Eastern.
                double deltaMin = Math.Abs((barNow - DateTime.Now).TotalMinutes);
                if (deltaMin > 5)
                    return barNow;

                var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                DateTime easternNow = TimeZoneInfo.ConvertTime(barNow, TimeZoneInfo.Local, eastern);
                lastTradingHoursNowBasis = "EasternTime";
                lastTradingHoursNow = easternNow;
                return easternNow;
            }
            catch
            {
                return barNow;
            }
        }

        private const int MaintenanceFlattenMinutesBefore = 2;

        private bool IsWithinTradingHours()
        {
            if (!UseTradingHoursFilter)
                return true;

            DateTime now = GetTradingHoursNow();
            int currentMinutes = now.Hour * 60 + now.Minute;
            int startMinutes = TradingStartHour * 60 + TradingStartMinute;
            int endMinutes = TradingEndHour * 60 + TradingEndMinute;

            if (startMinutes == endMinutes)
                return true;

            if (startMinutes < endMinutes)
                return currentMinutes >= startMinutes && currentMinutes <= endMinutes;

            return currentMinutes >= startMinutes || currentMinutes <= endMinutes;
        }

        private bool IsApproachingMaintenance()
        {
            if (!UseTradingHoursFilter)
                return false;

            DateTime now = GetTradingHoursNow();
            int currentMinutes = now.Hour * 60 + now.Minute;
            int endMinutes = TradingEndHour * 60 + TradingEndMinute;

            int minutesUntilClose = endMinutes - currentMinutes;
            if (minutesUntilClose < 0)
                minutesUntilClose += 24 * 60;

            return minutesUntilClose <= MaintenanceFlattenMinutesBefore && minutesUntilClose >= 0;
        }

        private bool maintenanceFlattenDone = false;

        private void CheckMaintenanceFlatten()
        {
            if (!UseTradingHoursFilter || Position.MarketPosition == MarketPosition.Flat)
            {
                if (!IsApproachingMaintenance())
                    maintenanceFlattenDone = false;
                return;
            }

            if (maintenanceFlattenDone)
                return;

            if (!IsApproachingMaintenance())
            {
                maintenanceFlattenDone = false;
                return;
            }

            maintenanceFlattenDone = true;
            DateTime now = GetTradingHoursNow();
            Print($"[MAINTENANCE_FLATTEN] Bar {CurrentBar}: Flattening position {MaintenanceFlattenMinutesBefore} min before session close ({TradingEndHour:D2}:{TradingEndMinute:D2} ET). Now={now:HH:mm}");

            if (Position.MarketPosition == MarketPosition.Long)
            {
                currentTradeExitReason = "MaintenanceFlatten";
                ExitLongTrace("MaintenanceFlatten", GetActiveEntrySignal(), "MaintenanceFlatten");
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                currentTradeExitReason = "MaintenanceFlatten";
                ExitShortTrace("MaintenanceFlattenS", GetActiveEntrySignal(), "MaintenanceFlattenS");
            }
        }

        private bool IsAtrWithinRange(out double atrValue)
        {
            atrValue = double.NaN;
            if (!UseAtrFilter || atr == null)
                return true;

            atrValue = atr[0];
            if (double.IsNaN(atrValue))
                return false;

            if (AtrMin > 0 && atrValue < AtrMin)
                return false;
            if (AtrMax > 0 && atrValue > AtrMax)
                return false;

            return true;
        }

        private bool IsTrafficWithinRange(out double ticksPerMinuteAvg, out double ticksPerMinuteLast)
        {
            ticksPerMinuteLast = lastClosedBarTicksPerMinute;
            ticksPerMinuteAvg = double.NaN;

            if (!UseTrafficFilter || MinTicksPerMinuteToTrade <= 0)
                return true;

            if (recentTicksPerMinute == null || recentTicksPerMinute.Count == 0)
                return true; // not enough data yet - don't block startup

            int n = Math.Max(1, TrafficFilterLookbackBars);
            // Average of last N values (queue is small; iterate)
            double sum = 0;
            int count = 0;
            int skip = Math.Max(0, recentTicksPerMinute.Count - n);
            int i = 0;
            foreach (var v in recentTicksPerMinute)
            {
                if (i++ < skip)
                    continue;
                if (double.IsNaN(v) || v <= 0)
                    continue;
                sum += v;
                count++;
            }

            if (count <= 0)
                return true;

            ticksPerMinuteAvg = sum / count;
            return ticksPerMinuteAvg >= MinTicksPerMinuteToTrade;
        }

        private bool EntryFiltersPass(out string blockReason)
        {
            blockReason = "";

            if (!IsWithinTradingHours())
            {
                blockReason = $"TradingHoursFilter blocked (window {TradingStartHour:D2}:{TradingStartMinute:D2}-{TradingEndHour:D2}:{TradingEndMinute:D2}, now {Time[0]:HH:mm}/{lastTradingHoursNow:HH:mm} {lastTradingHoursNowBasis})";
                return false;
            }

            if (UseAtrFilter)
            {
                if (!IsAtrWithinRange(out double atrValue))
                {
                    blockReason = $"AtrFilter blocked (ATR={atrValue:F2}, min={AtrMin:F2}, max={AtrMax:F2})";
                    return false;
                }
            }

            if (UseTrafficFilter && MinTicksPerMinuteToTrade > 0)
            {
                if (!IsTrafficWithinRange(out double tpmAvg, out double tpmLast))
                {
                    string avgStr = double.IsNaN(tpmAvg) ? "n/a" : tpmAvg.ToString("F0");
                    string lastStr = double.IsNaN(tpmLast) ? "n/a" : tpmLast.ToString("F0");
                    blockReason = $"TrafficFilter blocked (ticksPerMin avg{Math.Max(1, TrafficFilterLookbackBars)}={avgStr}, last={lastStr}, min={MinTicksPerMinuteToTrade:F0})";
                    return false;
                }
            }

            return true;
        }

        private void UpdateTradingTrafficStatusLabel(bool force = false)
        {
            if (BarsInProgress != 0)
                return;

            if (!ShowTradingTrafficStatus)
            {
                try { RemoveDrawObject(tradingTrafficStatusLabelTag); } catch { }
                return;
            }

            if (!force && lastTradingTrafficStatusLabelBar == CurrentBar)
                return;

            lastTradingTrafficStatusLabelBar = CurrentBar;

            bool withinHours = IsWithinTradingHours();
            bool atrOk = IsAtrWithinRange(out double atrValue);
            bool trafficOk = IsTrafficWithinRange(out double tpmAvgFilter, out double tpmLast);

            // Always compute traffic stats for display, even when the traffic filter is OFF.
            double tpmAvg = tpmAvgFilter;
            if (double.IsNaN(tpmAvg))
            {
                int n = Math.Max(1, TrafficFilterLookbackBars);
                if (recentTicksPerMinute != null && recentTicksPerMinute.Count > 0)
                {
                    double sum = 0;
                    int count = 0;
                    int skip = Math.Max(0, recentTicksPerMinute.Count - n);
                    int i = 0;
                    foreach (var v in recentTicksPerMinute)
                    {
                        if (i++ < skip)
                            continue;
                        if (double.IsNaN(v) || v <= 0)
                            continue;
                        sum += v;
                        count++;
                    }
                    if (count > 0)
                        tpmAvg = sum / count;
                }
            }

            bool tradingAllowed = withinHours && atrOk && trafficOk;

            var sb = new StringBuilder(256);
            sb.AppendLine($"TrafficLog: {(EnableTrafficLog ? "ON" : "OFF")}");
            sb.AppendLine($"Trading: {(tradingAllowed ? "ENABLED" : "DISABLED")}");

            // Show a single "BlockedBy" line; if nothing is blocking, say "No restrictions".
            var blocked = new List<string>(3);
            if (UseTradingHoursFilter && !withinHours) blocked.Add("TradingHours");
            if (UseAtrFilter && !atrOk) blocked.Add("ATR");
            if (UseTrafficFilter && MinTicksPerMinuteToTrade > 0 && !trafficOk) blocked.Add("Traffic");
            sb.AppendLine(blocked.Count == 0 ? "BlockedBy: No restrictions" : $"BlockedBy: {string.Join(", ", blocked.ToArray())}");

            if (UseTradingHoursFilter)
                sb.AppendLine($"Hours: {TradingStartHour:D2}:{TradingStartMinute:D2}-{TradingEndHour:D2}:{TradingEndMinute:D2}  Now: {Time[0]:HH:mm} / {lastTradingHoursNow:HH:mm} ({lastTradingHoursNowBasis}) ({(withinHours ? "IN" : "OUT")})");
            else
                sb.AppendLine("Hours: (no restriction)");

            if (UseAtrFilter)
                sb.AppendLine($"ATR({AtrPeriod}): {(double.IsNaN(atrValue) ? "n/a" : atrValue.ToString("F2"))}  Range: {AtrMin:F2}-{AtrMax:F2} ({(atrOk ? "OK" : "BLOCK")})");
            else
                sb.AppendLine("ATR: (no restriction)");

            int trafficN = Math.Max(1, TrafficFilterLookbackBars);
            string avgStr2 = double.IsNaN(tpmAvg) ? "n/a" : tpmAvg.ToString("F0");
            string lastStr2 = double.IsNaN(tpmLast) ? "n/a" : tpmLast.ToString("F0");
            if (UseTrafficFilter && MinTicksPerMinuteToTrade > 0)
                sb.AppendLine($"Traffic: avg{trafficN}={avgStr2}  last={lastStr2}  Min={MinTicksPerMinuteToTrade:F0} ({(trafficOk ? "OK" : "BLOCK")})");
            else
                sb.AppendLine($"Traffic: avg{trafficN}={avgStr2}  last={lastStr2} (no restriction)");

            int slTicks = CalculateStopLossTicks();
            double slPoints = slTicks / 4.0;
            string slSource = UseDynamicStopLoss ? (UseVolumeAwareStop ? "API" : "BarRange") : "Fixed";
            sb.AppendLine($"SL: {slPoints:F1} pts ({slTicks}t) [{slSource}]  Trail: {(UseTrailingStop ? "ON" : "OFF")}  BE: {(UseBreakEven ? $"+{BreakEvenTrigger}" : "OFF")}");

            if (UseProfitTarget && ProfitTargetPoints > 0)
                sb.AppendLine($"Target: +{ProfitTargetPoints:F2} pts");
            else
                sb.AppendLine("Target: (none)");

            if (stopLossTextBox != null && ChartControl != null)
            {
                ChartControl.Dispatcher.InvokeAsync(() =>
                {
                    if (stopLossTextBox != null)
                        stopLossTextBox.Text = slPoints.ToString("F1");
                });
            }

            Brush textBrush = tradingAllowed ? Brushes.LimeGreen : Brushes.OrangeRed;
            string labelText = sb.ToString().TrimEnd();
            if (TradingTrafficStatusTopPaddingLines > 0)
                labelText = new string('\n', TradingTrafficStatusTopPaddingLines) + labelText;
            Draw.TextFixed(
                this,
                tradingTrafficStatusLabelTag,
                labelText,
                TextPosition.TopLeft,
                textBrush,
                new NinjaTrader.Gui.Tools.SimpleFont("Consolas", 14),
                Brushes.Transparent,
                Brushes.Transparent,
                0);
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
                double open = Open[1];
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
                    
                    // If body requirement is enabled, check that entire body is above Fast EMA (with minimum gap)
                    if (EmaCrossoverRequireBodyBelow)
                    {
                        double bodyTop = Math.Max(open, close);
                        double bodyBottom = Math.Min(open, close);
                        // Body must be above Fast EMA with minimum gap (same as close requirement)
                        bool bodyAboveFast = bodyBottom >= fastEma + minGapCloseToFast && bodyTop >= fastEma + minGapCloseToFast;
                        
                        // Check if we should allow entry even when FastEMA < SlowEMA
                        if (AllowLongWhenBodyAboveFastButFastBelowSlow && bodyAboveFast && !fastAboveSlow)
                        {
                            // Allow entry: body is above FastEMA but FastEMA < SlowEMA (no crossover yet)
                            conditionsMet = true;
                            Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} LONG: BodyOnly mode - Open={open:F4}, Close={close:F4}, BodyTop={bodyTop:F4}, BodyBottom={bodyBottom:F4}, FastEMA={fastEma:F4}, SlowEMA={slowEma:F4}, minGap={minGapCloseToFast:F4}");
                            Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} LONG: bodyAboveFast={bodyAboveFast}, fastAboveSlow={fastAboveSlow} (FastEMA < SlowEMA)");
                            Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} LONG: AllowLongWhenBodyAboveFastButFastBelowSlow=True - ALLOWING entry despite FastEMA < SlowEMA");
                        }
                        else
                        {
                            // Standard logic: require both bodyAboveFast AND fastAboveSlow
                            conditionsMet = bodyAboveFast && fastAboveSlow;
                            Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} LONG: BodyOnly mode - Open={open:F4}, Close={close:F4}, BodyTop={bodyTop:F4}, BodyBottom={bodyBottom:F4}, FastEMA={fastEma:F4}, minGap={minGapCloseToFast:F4}, bodyAboveFast={bodyAboveFast} (bodyBottom >= fastEma+gap: {bodyBottom >= fastEma + minGapCloseToFast}, bodyTop >= fastEma+gap: {bodyTop >= fastEma + minGapCloseToFast})");
                            Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} LONG: conditionsMet={conditionsMet} (bodyAboveFast={bodyAboveFast}, fastAboveSlow={fastAboveSlow})");
                        }
                    }
                    else
                    {
                        // Check if we should allow entry even when FastEMA < SlowEMA
                        if (AllowLongWhenBodyAboveFastButFastBelowSlow && closeAboveFast && !fastAboveSlow)
                        {
                            // Allow entry: close is above FastEMA but FastEMA < SlowEMA (no crossover yet)
                            conditionsMet = true;
                            Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} LONG: Close={close:F4}, FastEMA={fastEma:F4}, SlowEMA={slowEma:F4}, closeAboveFast={closeAboveFast}, fastAboveSlow={fastAboveSlow} (FastEMA < SlowEMA)");
                            Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} LONG: AllowLongWhenBodyAboveFastButFastBelowSlow=True - ALLOWING entry despite FastEMA < SlowEMA");
                        }
                        else
                        {
                            // Standard logic: require both closeAboveFast AND fastAboveSlow
                            conditionsMet = closeAboveFast && fastAboveSlow;
                        }
                    }
                    
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
                    
                    // If body requirement is enabled, check that entire body is below Fast EMA (with minimum gap)
                    if (EmaCrossoverRequireBodyBelow)
                    {
                        double bodyTop = Math.Max(open, close);
                        double bodyBottom = Math.Min(open, close);
                        // Body must be below Fast EMA with minimum gap (same as close requirement)
                        bool bodyBelowFast = bodyTop <= fastEma - minGapCloseToFast && bodyBottom <= fastEma - minGapCloseToFast;
                        
                        // Check if we should allow entry even when FastEMA > SlowEMA
                        if (AllowShortWhenBodyBelowFastButFastAboveSlow && bodyBelowFast && !fastBelowSlow)
                        {
                            // Allow entry: body is below FastEMA but FastEMA > SlowEMA (no crossover yet)
                            conditionsMet = true;
                            Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} SHORT: BodyOnly mode - Open={open:F4}, Close={close:F4}, BodyTop={bodyTop:F4}, BodyBottom={bodyBottom:F4}, FastEMA={fastEma:F4}, SlowEMA={slowEma:F4}, minGap={minGapCloseToFast:F4}");
                            Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} SHORT: bodyBelowFast={bodyBelowFast}, fastBelowSlow={fastBelowSlow} (FastEMA > SlowEMA)");
                            Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} SHORT: AllowShortWhenBodyBelowFastButFastAboveSlow=True - ALLOWING entry despite FastEMA > SlowEMA");
                        }
                        else
                        {
                            // Standard logic: require both bodyBelowFast AND fastBelowSlow
                            conditionsMet = bodyBelowFast && fastBelowSlow;
                            Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} SHORT: BodyOnly mode - Open={open:F4}, Close={close:F4}, BodyTop={bodyTop:F4}, BodyBottom={bodyBottom:F4}, FastEMA={fastEma:F4}, minGap={minGapCloseToFast:F4}, bodyBelowFast={bodyBelowFast} (bodyTop <= fastEma-gap: {bodyTop <= fastEma - minGapCloseToFast}, bodyBottom <= fastEma-gap: {bodyBottom <= fastEma - minGapCloseToFast})");
                        }
                    }
                    else
                    {
                        // Check if we should allow entry even when FastEMA > SlowEMA
                        if (AllowShortWhenBodyBelowFastButFastAboveSlow && closeBelowFast && !fastBelowSlow)
                        {
                            // Allow entry: close is below FastEMA but FastEMA > SlowEMA (no crossover yet)
                            conditionsMet = true;
                            Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} SHORT: Close={close:F4}, FastEMA={fastEma:F4}, SlowEMA={slowEma:F4}, closeBelowFast={closeBelowFast}, fastBelowSlow={fastBelowSlow} (FastEMA > SlowEMA)");
                            Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} SHORT: AllowShortWhenBodyBelowFastButFastAboveSlow=True - ALLOWING entry despite FastEMA > SlowEMA");
                        }
                        else
                        {
                            // Standard logic: require both closeBelowFast AND fastBelowSlow
                            conditionsMet = closeBelowFast && fastBelowSlow;
                        }
                    }
                    
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
                Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} {(isLong ? "LONG" : "SHORT")}: EmaCrossoverFilterPasses returning {conditionsMet}");
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
            double openCurrent = Open[1];
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
                
                // If body requirement is enabled, check that entire body is above Fast EMA (with minimum gap)
                if (EmaCrossoverRequireBodyBelow)
                {
                    double bodyTop = Math.Max(openCurrent, closeCurrent);
                    double bodyBottom = Math.Min(openCurrent, closeCurrent);
                    // Body must be above Fast EMA with minimum gap (same as close requirement)
                    bool bodyAboveFast = bodyBottom >= fastEmaCurrent + minGapCloseToFastCurrent && bodyTop >= fastEmaCurrent + minGapCloseToFastCurrent;
                    
                    // Check if we should allow entry even when FastEMA < SlowEMA
                    if (AllowLongWhenBodyAboveFastButFastBelowSlow && bodyAboveFast && !fastAboveSlow)
                    {
                        Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} LONG: BodyOnly mode (RequireCrossover=true) - Open={openCurrent:F4}, Close={closeCurrent:F4}, BodyTop={bodyTop:F4}, BodyBottom={bodyBottom:F4}, FastEMA={fastEmaCurrent:F4}, SlowEMA={slowEmaCurrent:F4}, minGap={minGapCloseToFastCurrent:F4}");
                        Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} LONG: bodyAboveFast={bodyAboveFast}, fastAboveSlow={fastAboveSlow} (FastEMA < SlowEMA)");
                        Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} LONG: AllowLongWhenBodyAboveFastButFastBelowSlow=True - ALLOWING entry despite FastEMA < SlowEMA");
                        return true;
                    }
                    
                    Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} LONG: BodyOnly mode (RequireCrossover=true) - Open={openCurrent:F4}, Close={closeCurrent:F4}, BodyTop={bodyTop:F4}, BodyBottom={bodyBottom:F4}, FastEMA={fastEmaCurrent:F4}, minGap={minGapCloseToFastCurrent:F4}, bodyAboveFast={bodyAboveFast}");
                    return bodyAboveFast && fastAboveSlow;
                }
                else
                {
                    // Check if we should allow entry even when FastEMA < SlowEMA
                    if (AllowLongWhenBodyAboveFastButFastBelowSlow && closeAboveFast && !fastAboveSlow)
                    {
                        Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} LONG: Close={closeCurrent:F4}, FastEMA={fastEmaCurrent:F4}, SlowEMA={slowEmaCurrent:F4}, closeAboveFast={closeAboveFast}, fastAboveSlow={fastAboveSlow} (FastEMA < SlowEMA)");
                        Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} LONG: AllowLongWhenBodyAboveFastButFastBelowSlow=True - ALLOWING entry despite FastEMA < SlowEMA");
                        return true;
                    }
                    
                    return closeAboveFast && fastAboveSlow;
                }
            }
            else
            {
                // For shorts: Close <= Fast EMA - minGap AND Fast EMA <= Slow EMA - minGap
                bool closeBelowFast = closeCurrent <= fastEmaCurrent - minGapCloseToFastCurrent;
                bool fastBelowSlow = fastEmaCurrent <= slowEmaCurrent - minGapFastToSlowCurrent;
                
                // If body requirement is enabled, check that entire body is below Fast EMA (with minimum gap)
                if (EmaCrossoverRequireBodyBelow)
                {
                    double bodyTop = Math.Max(openCurrent, closeCurrent);
                    double bodyBottom = Math.Min(openCurrent, closeCurrent);
                    // Body must be below Fast EMA with minimum gap (same as close requirement)
                    bool bodyBelowFast = bodyTop <= fastEmaCurrent - minGapCloseToFastCurrent && bodyBottom <= fastEmaCurrent - minGapCloseToFastCurrent;
                    
                    // Check if we should allow entry even when FastEMA > SlowEMA
                    if (AllowShortWhenBodyBelowFastButFastAboveSlow && bodyBelowFast && !fastBelowSlow)
                    {
                        Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} SHORT: BodyOnly mode (RequireCrossover=true) - Open={openCurrent:F4}, Close={closeCurrent:F4}, BodyTop={bodyTop:F4}, BodyBottom={bodyBottom:F4}, FastEMA={fastEmaCurrent:F4}, SlowEMA={slowEmaCurrent:F4}, minGap={minGapCloseToFastCurrent:F4}");
                        Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} SHORT: bodyBelowFast={bodyBelowFast}, fastBelowSlow={fastBelowSlow} (FastEMA > SlowEMA)");
                        Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} SHORT: AllowShortWhenBodyBelowFastButFastAboveSlow=True - ALLOWING entry despite FastEMA > SlowEMA");
                        return true;
                    }
                    
                    Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} SHORT: BodyOnly mode (RequireCrossover=true) - Open={openCurrent:F4}, Close={closeCurrent:F4}, BodyTop={bodyTop:F4}, BodyBottom={bodyBottom:F4}, FastEMA={fastEmaCurrent:F4}, minGap={minGapCloseToFastCurrent:F4}, bodyBelowFast={bodyBelowFast}");
                    return bodyBelowFast && fastBelowSlow;
                }
                else
                {
                    // Check if we should allow entry even when FastEMA > SlowEMA
                    if (AllowShortWhenBodyBelowFastButFastAboveSlow && closeBelowFast && !fastBelowSlow)
                    {
                        Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} SHORT: Close={closeCurrent:F4}, FastEMA={fastEmaCurrent:F4}, SlowEMA={slowEmaCurrent:F4}, closeBelowFast={closeBelowFast}, fastBelowSlow={fastBelowSlow} (FastEMA > SlowEMA)");
                        Print($"[EMA_FILTER_DEBUG] Bar {CurrentBar} SHORT: AllowShortWhenBodyBelowFastButFastAboveSlow=True - ALLOWING entry despite FastEMA > SlowEMA");
                        return true;
                    }
                    
                    return closeBelowFast && fastBelowSlow;
                }
            }
        }

        // Rate limit bar sample recording
        private int lastRecordedBarSample = -1;
        private bool historicalCsvImportComplete = false; // Track when historical CSV import is done

        /// <summary>
        /// Sends all accumulated historical bar data to the server in one batch.
        /// Called when transitioning from Historical to Realtime mode.
        /// </summary>
        private void SendHistoricalBarBatch()
        {
            Print($"[BATCH_SEND] ===== SendHistoricalBarBatch() CALLED =====");
            
            string[] barsToSend;
            lock (historicalBarDataLock)
            {
                barsToSend = historicalBarDataList.ToArray();
                Print($"[BATCH_SEND] Lock acquired, found {historicalBarDataList.Count} bars in memory");
                historicalBarDataList.Clear(); // Clear the list after copying
            }
            
            if (barsToSend.Length == 0)
            {
                Print($"[BATCH_SEND] ✗ ERROR: No historical bars to send! (barsToSend.Length = 0)");
                Print($"[BATCH_SEND] This means RecordBarSample() was not called during historical playback!");
                return;
            }
            
            Print($"[BATCH_SEND] Preparing to send {barsToSend.Length} historical bars...");
            
            // Build batch JSON array
            string batchJson = "[" + string.Join(",", barsToSend) + "]";
            Print($"[BATCH_SEND] Batch JSON size: {batchJson.Length} bytes ({batchJson.Length / 1024.0:F1} KB)");
            
            // Send SYNCHRONOUSLY to ensure data is saved before strategy can terminate
            // This blocks for a few seconds but guarantees the data is saved
            try
            {
                EnsureHttpClient();
                
                string batchUrl = DashboardBaseUrl.TrimEnd('/') + "/api/volatility/batch-record-bars";
                var content = new StringContent(batchJson, System.Text.Encoding.UTF8, "application/json");
                
                Print($"[BATCH_SEND] Sending POST to {batchUrl} (SYNC - will block until complete)...");
                
                // Use a dedicated HttpClient for this request to avoid disposal issues
                using (var batchClient = new System.Net.Http.HttpClient())
                {
                    batchClient.Timeout = TimeSpan.FromMinutes(5);
                    
                    var response = batchClient.PostAsync(batchUrl, content).Result; // Blocking call
                    
                    if (response.IsSuccessStatusCode)
                    {
                        string responseBody = response.Content.ReadAsStringAsync().Result;
                        Print($"[BATCH_SEND] ✓ SUCCESS! Sent {barsToSend.Length} bars. Response: {responseBody.Substring(0, Math.Min(200, responseBody.Length))}");
                    }
                    else
                    {
                        string errorBody = response.Content.ReadAsStringAsync().Result;
                        Print($"[BATCH_SEND] ✗ HTTP {response.StatusCode}: {errorBody.Substring(0, Math.Min(200, errorBody.Length))}");
                    }
                    response.Dispose();
                }
                
                Print($"[BATCH_SEND] Batch upload complete - real-time recording can proceed");
            }
            catch (AggregateException ae)
            {
                var innerEx = ae.InnerException ?? ae;
                Print($"[BATCH_SEND] ✗ Error: {innerEx.GetType().Name}: {innerEx.Message}");
            }
            catch (Exception ex)
            {
                Print($"[BATCH_SEND] ✗ Error: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void RecordBarSample()
        {
            // IMPORTANT: RecordBarSample is called at the START of a new bar (CurrentBar=2407)
            // At this point, [1] refers to the bar that just closed (bar 2406)
            // So we record bar_index as CurrentBar - 1 (the bar that just closed)
            int barIndexToRecord = CurrentBar >= 1 ? CurrentBar - 1 : CurrentBar;
            
            // Only record once per bar (check using the bar index we're actually recording)
            if (barIndexToRecord == lastRecordedBarSample)
            {
                if (CurrentBar <= 5) // Only log for first few bars to avoid spam
                {
                    Print($"[BAR_SAMPLE] Bar {CurrentBar}: Already recorded bar {barIndexToRecord}, skipping");
                }
                return;
            }
            lastRecordedBarSample = barIndexToRecord;

            try
            {
                if (CurrentBar <= 5)
                {
                    Print($"[BAR_SAMPLE] Bar {CurrentBar}: Starting RecordBarSample()");
                }
                
                EnsureHttpClient();
                
                // Verify HttpClient was created
                if (sharedClient == null)
                {
                    Print($"[BAR_SAMPLE_ERROR] Bar {CurrentBar}: HttpClient is null after EnsureHttpClient() call!");
                    return;
                }
                
                if (CurrentBar <= 5)
                {
                    Print($"[BAR_SAMPLE] Bar {CurrentBar}: HttpClient is OK, preparing JSON payload");
                }
                
                // Fire and forget - don't wait for response
                // Get gradient degree for this bar (use the gradient calculated on this bar, which represents trend ending at previous bar)
                double gradDeg = double.NaN;
                double gradRecorded = double.NaN;
                double gradCurrent = double.NaN;
                string gradSource = "none";
                int gradBarIndex = -1;

                if (gradientByBar.TryGetValue(barIndexToRecord, out double gradForRecordedBar))
                {
                    gradRecorded = gradForRecordedBar;
                }

                if (gradientByBar.TryGetValue(CurrentBar, out double gradForCurrentBar))
                {
                    gradCurrent = gradForCurrentBar;
                }

                if (!double.IsNaN(gradRecorded))
                {
                    gradDeg = gradRecorded;
                    gradSource = "bar_index";
                    gradBarIndex = barIndexToRecord;
                }
                else if (!double.IsNaN(gradCurrent))
                {
                    gradDeg = gradCurrent;
                    gradSource = "current_bar";
                    gradBarIndex = CurrentBar;
                }
                else if (!double.IsNaN(lastFastEmaGradDeg))
                {
                    gradDeg = lastFastEmaGradDeg;
                    gradSource = "last_fast_ema";
                    gradBarIndex = barIndexToRecord;
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
                // IMPORTANT: RecordBarSample is called at the START of a new bar (CurrentBar=2407)
                // At this point, [1] refers to the bar that just closed (bar 2406)
                // So we should record bar_index as CurrentBar - 1 (the bar that just closed)
                // and use [1] for its OHLC data
                // barIndexToRecord is already defined at the start of the function
                double prevOpen = CurrentBar >= 1 ? Open[1] : double.NaN;
                double prevClose = CurrentBar >= 1 ? Close[1] : double.NaN;
                bool prevGood = !double.IsNaN(prevOpen) && !double.IsNaN(prevClose) && prevClose > prevOpen;
                bool prevBad = !double.IsNaN(prevOpen) && !double.IsNaN(prevClose) && prevClose < prevOpen;
                string candleType = "flat";
                if (prevGood) candleType = "good";
                else if (prevBad) candleType = "bad";
                
                // Use the stored values from OnBarUpdate (set after ProcessTradingDecisions)
                // These reflect the actual decision state after ProcessTradingDecisions ran
                bool trendUp = lastTrendUp;
                bool trendDown = lastTrendDown;
                bool allowShortThisBar = lastAllowShortThisBar;
                bool allowLongThisBar = lastAllowLongThisBar;
                bool pendingLongFromBad = lastPendingLongFromBad;
                bool pendingShortFromGood = lastPendingShortFromGood;
                
                // Capture entry reason if an entry was placed on the bar that just closed
                string entryReasonForBar = "";
                if (lastEntryBarIndex == barIndexToRecord && !string.IsNullOrEmpty(currentTradeEntryReason))
                {
                    entryReasonForBar = currentTradeEntryReason;
                }
                
                // Capture exit reason if an exit happened on the bar that just closed
                string exitReasonForBar = "";
                if (lastExitBarIndex == barIndexToRecord && !string.IsNullOrEmpty(currentTradeExitReason))
                {
                    exitReasonForBar = currentTradeExitReason;
                    // Reset exit reason after capturing it (so it doesn't persist to next bar)
                    if (lastExitBarIndex == barIndexToRecord)
                    {
                        currentTradeExitReason = "";
                        lastExitBarIndex = -1;
                    }
                }
                
                // Determine the reason to display: entry reason on entry bars, exit reason on exit bars
                string reasonForBar = "";
                if (!string.IsNullOrEmpty(entryReasonForBar))
                {
                    reasonForBar = entryReasonForBar;
                }
                else if (!string.IsNullOrEmpty(exitReasonForBar))
                {
                    reasonForBar = exitReasonForBar;
                }
                
                // Build JSON string for bar data
                // Use barIndexToRecord (CurrentBar - 1) to match the OHLC data from [1]
                double barMinutes = double.NaN;
                if (CurrentBar >= 2)
                {
                    barMinutes = (Time[1] - Time[2]).TotalMinutes;
                    if (barMinutes <= 0) barMinutes = double.NaN;
                }
                double volumePerMinute = (!double.IsNaN(barMinutes) && barMinutes > 0) ? ((long)Volume[1] / barMinutes) : double.NaN;
                int ticksInBar = (lastClosedBarIndex == barIndexToRecord) ? lastClosedBarTicks : 0;
                double ticksPerMinute = (!double.IsNaN(barMinutes) && barMinutes > 0 && ticksInBar > 0) ? (ticksInBar / barMinutes) : double.NaN;

                string barJson = $"{{\"timestamp\":\"{Time[1]:yyyy-MM-dd HH:mm:ss}\"," +
                    $"\"bar_index\":{barIndexToRecord}," +
                    $"\"symbol\":\"MNQ\"," +
                    $"\"open\":{Open[1]}," +
                    $"\"high\":{High[1]}," +
                    $"\"low\":{Low[1]}," +
                    $"\"close\":{Close[1]}," +
                    $"\"volume\":{(long)Volume[1]}," +
                    $"\"bar_minutes\":{(double.IsNaN(barMinutes) ? "null" : barMinutes.ToString("F4"))}," +
                    $"\"volume_per_minute\":{(double.IsNaN(volumePerMinute) ? "null" : volumePerMinute.ToString("F2"))}," +
                    $"\"ticks_in_bar\":{ticksInBar}," +
                    $"\"ticks_per_minute\":{(double.IsNaN(ticksPerMinute) ? "null" : ticksPerMinute.ToString("F2"))}," +
                    $"\"direction\":\"{Position.MarketPosition}\"," +
                    $"\"in_trade\":{(Position.MarketPosition != MarketPosition.Flat).ToString().ToLower()}," +
                    $"\"ema_fast_period\":{EmaFastPeriod}," +
                    $"\"ema_slow_period\":{EmaSlowPeriod}," +
                    $"\"ema_fast_value\":{(emaFast != null && CurrentBar >= EmaFastPeriod ? emaFast[1].ToString("F4") : "null")}," +
                    $"\"ema_slow_value\":{(emaSlow != null && CurrentBar >= EmaSlowPeriod ? emaSlow[1].ToString("F4") : "null")}," +
                    $"\"fast_ema_grad_deg\":{(double.IsNaN(gradDeg) ? "null" : gradDeg.ToString("F2"))}," +
                    $"\"grad_source\":\"{gradSource}\"," +
                    $"\"grad_bar_index\":{gradBarIndex}," +
                    $"\"grad_recorded\":{(double.IsNaN(gradRecorded) ? "null" : gradRecorded.ToString("F2"))}," +
                    $"\"grad_current\":{(double.IsNaN(gradCurrent) ? "null" : gradCurrent.ToString("F2"))}," +
                    $"\"grad_last\":{(double.IsNaN(lastFastEmaGradDeg) ? "null" : lastFastEmaGradDeg.ToString("F2"))}," +
                    $"\"stop_loss_points\":{stopLossPointsValue.ToString("F2")}," +
                    $"\"candle_type\":\"{candleType}\"," +
                    $"\"trend_up\":{trendUp.ToString().ToLower()}," +
                    $"\"trend_down\":{trendDown.ToString().ToLower()}," +
                    $"\"allow_long_this_bar\":{allowLongThisBar.ToString().ToLower()}," +
                    $"\"allow_short_this_bar\":{allowShortThisBar.ToString().ToLower()}," +
                    $"\"pending_long_from_bad\":{pendingLongFromBad.ToString().ToLower()}," +
                    $"\"pending_short_from_good\":{pendingShortFromGood.ToString().ToLower()}," +
                    $"\"avoid_longs_on_bad_candle\":{AvoidLongsOnBadCandle.ToString().ToLower()}," +
                    $"\"avoid_shorts_on_good_candle\":{AvoidShortsOnGoodCandle.ToString().ToLower()}," +
                    $"\"entry_reason\":\"{reasonForBar.Replace("\"", "\\\"")}\"}}";
                
                // ===== HISTORICAL MODE: Store in memory, NO HTTP =====
                // During fast historical playback, HTTP requests queue up faster than they complete.
                // Instead, we store all bar data in memory and send it in ONE batch when transitioning to real-time.
                if (State == State.Historical)
                {
                    lock (historicalBarDataLock)
                    {
                        historicalBarDataList.Add(barJson);
                    }
                    
                    // Log progress periodically (every 500 bars)
                    if (CurrentBar % 500 == 0 || CurrentBar <= 10)
                    {
                        Print($"[BAR_SAMPLE] Bar {CurrentBar}: Stored in memory (total={historicalBarDataList.Count} bars, no HTTP during historical)");
                    }
                    return; // Exit early - no HTTP during historical
                }
                
                // ===== REAL-TIME MODE: Send via HTTP =====
                var content = new StringContent(barJson, System.Text.Encoding.UTF8, "application/json");
                
                string recordBarUrl = DashboardBaseUrl.TrimEnd('/') + "/api/volatility/record-bar";
                int currentBarForLogging = CurrentBar; // Capture CurrentBar for logging in callback
                
                Print($"[BAR_SAMPLE_RT] Bar {CurrentBar}: Sending real-time bar via HTTP to {recordBarUrl}");
                
                // Use Task.Run to ensure async execution doesn't block
                var cts = cancellationTokenSource; // Capture cancellation token
                bool semaphoreAcquired = false;
                
                Task.Run(async () =>
                {
                    // Check if strategy is still running or cancelled
                    if (State == State.Terminated || State == State.SetDefaults || cts == null || cts.IsCancellationRequested)
                    {
                        Print($"[BAR_SAMPLE_RT] Bar {currentBarForLogging}: Task cancelled or strategy terminated, skipping");
                        return; // Strategy stopped, don't send request
                    }
                    
                    // Real-time: Use semaphore to limit concurrent requests
                    try
                    {
                        // Use timeout to prevent indefinite blocking (2 seconds max wait)
                        using (var semaphoreCts = new CancellationTokenSource(2000))
                        {
                            await barSampleSemaphore.WaitAsync(semaphoreCts.Token).ConfigureAwait(false);
                        }
                        semaphoreAcquired = true;
                    }
                    catch (OperationCanceledException)
                    {
                        Print($"[BAR_SAMPLE_RT_ERROR] Bar {currentBarForLogging}: Semaphore wait timeout");
                        return;
                    }
                    try
                    {
                        // Retry logic with exponential backoff to ensure no bars are lost
                        int maxRetries = 3;
                        int retryDelayMs = 100;
                        bool success = false;
                        
                        for (int attempt = 0; attempt < maxRetries && !success; attempt++)
                        {
                            // Check if strategy stopped before each retry
                            if (State == State.Terminated || State == State.SetDefaults)
                            {
                                break; // Strategy stopped, exit retry loop
                            }
                            
                            try
                            {
                                if (attempt == 0)
                                {
                                    Print($"[BAR_SAMPLE_RT] Bar {currentBarForLogging}: POST attempt {attempt + 1}/{maxRetries}");
                                }
                                
                                // Add exponential backoff delay for retries
                                if (attempt > 0)
                                {
                                    await Task.Delay(retryDelayMs * (int)Math.Pow(2, attempt - 1)).ConfigureAwait(false);
                                }
                                
                                // 10 second timeout for real-time requests
                                using (var timeoutCts = new CancellationTokenSource(10000))
                                using (var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, timeoutCts.Token))
                                {
                                    var response = await sharedClient.PostAsync(recordBarUrl, content, combinedCts.Token).ConfigureAwait(false);
                                    
                                    Print($"[BAR_SAMPLE_RT] Bar {currentBarForLogging}: HTTP {response.StatusCode}");
                                    
                                    if (response.IsSuccessStatusCode)
                                    {
                                        success = true;
                                        try
                                        {
                                            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                            Print($"[BAR_SAMPLE_RT] Bar {currentBarForLogging}: ✓ Recorded ({responseBody.Substring(0, Math.Min(80, responseBody.Length))})");
            }
            catch
            {
                                            Print($"[BAR_SAMPLE_RT] Bar {currentBarForLogging}: ✓ Recorded");
                                        }
                                        response.Dispose();
                                    }
                                    else
                                    {
                                        // Log non-success status codes
                                        var errorBody = "";
                                        try
                                        {
                                            errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                        }
                                        catch { }
                                        
                                        Print($"[BAR_SAMPLE_RT] Bar {currentBarForLogging}: HTTP {response.StatusCode} - {errorBody.Substring(0, Math.Min(100, errorBody.Length))}");
                                        
                                        response.Dispose();
                                        if (attempt < maxRetries - 1)
                                        {
                                            // Will retry
                                            continue;
                                        }
                                        else
                                        {
                                            // Only log if strategy is still running
                                            if (State != State.Terminated && State != State.SetDefaults)
                                            {
                                                Print($"[BAR_SAMPLE_ERROR] Bar {currentBarForLogging}: Server returned {response.StatusCode} after {maxRetries} attempts. Error: {errorBody.Substring(0, Math.Min(200, errorBody.Length))}");
                                            }
                                        }
                                    }
                                }
                            }
                            catch (TaskCanceledException)
                            {
                                // Check if cancelled or just timeout
                                if (cts != null && cts.IsCancellationRequested)
                                {
                                    break; // Strategy was cancelled, exit retry loop
                                }
                                
                                // Timeout - retry if we have attempts left
                                if (attempt < maxRetries - 1)
                                {
                                    continue; // Will retry with exponential backoff
                                }
                                else
                                {
                                    // Only log if strategy is still running and not cancelled
                                    if (State != State.Terminated && State != State.SetDefaults && (cts == null || !cts.IsCancellationRequested))
                                    {
                                        Print($"[BAR_SAMPLE_ERROR] Bar {currentBarForLogging}: Request timeout after {maxRetries} attempts");
                                    }
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                // Strategy was cancelled, exit retry loop
                                break;
                            }
                            catch (Exception ex)
                            {
                                if (attempt < maxRetries - 1)
                                {
                                    continue; // Will retry
                                }
                                else
                                {
                                    // Only log if strategy is still running and not cancelled
                                    if (State != State.Terminated && State != State.SetDefaults && (cts == null || !cts.IsCancellationRequested))
                                    {
                                        Print($"[BAR_SAMPLE_ERROR] Bar {currentBarForLogging}: {ex.GetType().Name}: {ex.Message} after {maxRetries} attempts");
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        // Release semaphore if we acquired it
                        if (semaphoreAcquired)
                        {
                            barSampleSemaphore.Release();
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                // Log errors so we can diagnose connection issues
                Print($"[BAR_SAMPLE_ERROR] Bar {CurrentBar}: Exception in RecordBarSample: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Print($"[BAR_SAMPLE_ERROR] Inner exception: {ex.InnerException.Message}");
                }
                Print($"[BAR_SAMPLE_ERROR] Stack trace: {ex.StackTrace}");
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
                    var handler = new HttpClientHandler();
                    handler.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;
                    sharedClient = new HttpClient(handler);
                        sharedClient.Timeout = TimeSpan.FromMinutes(10); // Increased timeout to handle CSV imports and queued requests
                        Print($"[EnsureHttpClient] ✓ HttpClient created successfully. BaseUrl: {DashboardBaseUrl}");
                        // Tune connection behavior for local dashboard posts
                        try
                        {
                            System.Net.ServicePointManager.Expect100Continue = false;
                            System.Net.ServicePointManager.DefaultConnectionLimit = 20;
                            System.Net.ServicePointManager.UseNagleAlgorithm = false;
                        }
                        catch
                        {
                            // Ignore if ServicePointManager is not available
                        }
                    }
                    catch (Exception ex)
                    {
                        Print($"[EnsureHttpClient] ✗ Error creating HttpClient: {ex.GetType().Name}: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            Print($"[EnsureHttpClient] Inner exception: {ex.InnerException.Message}");
                        }
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
                    SubmitLongEntry();
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
                    SubmitShortEntry();
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
                ExitLongTrace("BarsOnTheFlowGradExit", GetActiveEntrySignal(), "BarsOnTheFlowGradExit");
                RecordExitForCooldown(MarketPosition.Long);
                waitingToExitLongOnGradient = false;
            }
            // Exit short if gradient rises above threshold (and we were waiting for it)
            else if (waitingToExitShortOnGradient && Position.MarketPosition == MarketPosition.Short && currentGradDeg > SkipShortsAboveGradient)
            {
                Print($"[EXIT_DEBUG] Bar {CurrentBar}: waitingToExitShortOnGradient resolving - gradient rose {currentGradDeg:F2}° > {SkipShortsAboveGradient:F2}° - Exiting SHORT");
                Print($"[MidBar Exit] Bar {CurrentBar}: Short gradient rose! {currentGradDeg:F2}° > {SkipShortsAboveGradient:F2}° - Exiting");
                ExitShortTrace("BarsOnTheFlowGradExitS", GetActiveEntrySignal(), "BarsOnTheFlowGradExitS");
                RecordExitForCooldown(MarketPosition.Short);
                waitingToExitShortOnGradient = false;
            }
        }

        /// <summary>
        /// Per-bar gradient of the bar that just closed [1]: slope from EMA[2] → EMA[1].
        /// Matches the visual slope of the Fast EMA line on that single completed bar.
        /// </summary>
        private void ComputeCompletedBarGradDeg(out double regDeg, out double chartDeg)
        {
            regDeg = double.NaN;
            chartDeg = double.NaN;

            if (fastEma == null || CurrentBar < 2)
                return;

            double emaRecent = fastEma[1]; // bar that just closed
            double emaPrior = fastEma[2];  // prior bar
            if (double.IsNaN(emaRecent) || double.IsNaN(emaPrior))
                return;

            double slope = emaRecent - emaPrior;
            regDeg = Math.Atan(slope) * (180.0 / Math.PI);

            if (ChartControl == null || ChartBars == null || ChartPanel == null)
                return;

            int barRecent = CurrentBar - 1;
            int barPrior = CurrentBar - 2;

            double xRecent = ChartControl.GetXByBarIndex(ChartBars, barRecent);
            double xPrior = ChartControl.GetXByBarIndex(ChartBars, barPrior);

            double panelHeight = ChartPanel.H;
            double priceMax = ChartPanel.MaxValue;
            double priceMin = ChartPanel.MinValue;
            double priceRange = priceMax - priceMin;
            if (priceRange <= 0 || panelHeight <= 0)
                return;

            double dx = Math.Abs(xRecent - xPrior);
            if (dx < 1e-6)
                return;

            double yRecent = panelHeight * (priceMax - emaRecent) / priceRange;
            double yPrior = panelHeight * (priceMax - emaPrior) / priceRange;
            double dyPixels = yPrior - yRecent; // positive when price rose
            chartDeg = Math.Atan2(dyPixels, dx) * (180.0 / Math.PI);
        }

        /// <summary>
        /// Returns the 1-bar gradient of the bar that just closed [1] (same value shown on the F: label).
        /// </summary>
        private double GetCurrentGradient(out double gradDeg)
        {
            double regDeg, chartDeg;
            ComputeCompletedBarGradDeg(out regDeg, out chartDeg);

            double deg = regDeg;
            if (UseChartScaledFastGradDeg && !double.IsNaN(chartDeg))
                deg = chartDeg;

            gradDeg = !double.IsNaN(deg) ? deg : lastFastEmaGradDeg;

            double slope = double.NaN;
            if (fastEma != null && CurrentBar >= 2 && !double.IsNaN(fastEma[1]) && !double.IsNaN(fastEma[2]))
                slope = fastEma[1] - fastEma[2];
            return slope;
        }

        /// <summary>
        /// Helper method to get EMA and price values from the completed bar [1].
        /// Returns NaN values if EMAs are not ready.
        /// </summary>
        private void GetBarCloseValues(out double fastEmaValue, out double slowEmaValue, out double closeValue, out double openValue)
        {
            if (emaFast != null && emaSlow != null && CurrentBar >= Math.Max(EmaFastPeriod, EmaSlowPeriod))
            {
                fastEmaValue = emaFast[1];
                slowEmaValue = emaSlow[1];
                closeValue = Close[1];
                openValue = Open[1];
            }
            else
            {
                fastEmaValue = double.NaN;
                slowEmaValue = double.NaN;
                closeValue = Close[1];
                openValue = Open[1];
            }
        }

        /// <summary>
        /// Helper method to check gradient and EMA crossover filters for LONG entries.
        /// Returns skip flags and EMA crossover status.
        /// </summary>
        private void CheckGradientAndEmaFiltersForLong(double gradDeg, out bool skipDueToGradient, out bool skipDueToEmaCrossover, out bool emaCrossedBelow, out bool closeBelowFastEma)
        {
            emaCrossedBelow = false;
            closeBelowFastEma = false;
            
            double fastEmaValue, slowEmaValue, closeValue, openValue;
            GetBarCloseValues(out fastEmaValue, out slowEmaValue, out closeValue, out openValue);
            
            if (!double.IsNaN(fastEmaValue) && !double.IsNaN(slowEmaValue))
            {
                // Block if Fast EMA < Slow EMA (bearish crossover condition)
                double minGapCloseToFast = EmaCrossoverMinTicksCloseToFast * TickSize;
                double bodyBottom = Math.Min(openValue, closeValue);
                bool bodyAboveFast = bodyBottom >= fastEmaValue + minGapCloseToFast;

                emaCrossedBelow = fastEmaValue < slowEmaValue;
                if (emaCrossedBelow && AllowLongWhenBodyAboveFastButFastBelowSlow && bodyAboveFast)
                {
                    emaCrossedBelow = false;
                    Print($"[GRADIENT_BLOCK] Bar {CurrentBar}: Fast EMA below Slow EMA, but body above Fast EMA - allowing LONG");
                }
                else if (emaCrossedBelow)
                {
                    Print($"[GRADIENT_BLOCK] Bar {CurrentBar}: Fast EMA ({fastEmaValue:F4}) < Slow EMA ({slowEmaValue:F4}) - BLOCKING LONG entry regardless of gradient");
                }
                
                // Block if close is below Fast EMA (price is bearish, not suitable for long entry)
                closeBelowFastEma = closeValue < fastEmaValue;
                if (closeBelowFastEma)
                {
                    Print($"[PRICE_BLOCK] Bar {CurrentBar}: Close ({closeValue:F4}) < Fast EMA ({fastEmaValue:F4}) - BLOCKING LONG entry (price is below Fast EMA)");
                }
            }
            
            skipDueToGradient = emaCrossedBelow || closeBelowFastEma || ShouldSkipLongDueToGradient(gradDeg);
            skipDueToEmaCrossover = UseEmaCrossoverFilter && !EmaCrossoverFilterPasses(true);
        }

        /// <summary>
        /// Helper method to check gradient and EMA crossover filters for SHORT entries.
        /// Returns skip flags and EMA crossover status.
        /// </summary>
        private void CheckGradientAndEmaFiltersForShort(double gradDeg, out bool skipDueToGradient, out bool skipDueToEmaCrossover, out bool emaCrossedAbove, out bool closeAboveFastEma)
        {
            emaCrossedAbove = false;
            closeAboveFastEma = false;
            
            double fastEmaValue, slowEmaValue, closeValue, openValue;
            GetBarCloseValues(out fastEmaValue, out slowEmaValue, out closeValue, out openValue);
            
            if (!double.IsNaN(fastEmaValue) && !double.IsNaN(slowEmaValue))
            {
                // Block if Fast EMA > Slow EMA (bullish crossover - not suitable for short entry)
                double minGapCloseToFast = EmaCrossoverMinTicksCloseToFast * TickSize;
                double bodyTop = Math.Max(openValue, closeValue);
                bool bodyBelowFast = bodyTop <= fastEmaValue - minGapCloseToFast;

                emaCrossedAbove = fastEmaValue > slowEmaValue;
                if (emaCrossedAbove && AllowShortWhenBodyBelowFastButFastAboveSlow && bodyBelowFast)
                {
                    emaCrossedAbove = false;
                    Print($"[GRADIENT_BLOCK] Bar {CurrentBar}: Fast EMA above Slow EMA, but body below Fast EMA - allowing SHORT");
                }
                else if (emaCrossedAbove)
                {
                    Print($"[GRADIENT_BLOCK] Bar {CurrentBar}: Fast EMA ({fastEmaValue:F4}) > Slow EMA ({slowEmaValue:F4}) - BLOCKING SHORT entry regardless of gradient");
                }
                
                // Block if close is above Fast EMA (price is bullish, not suitable for short entry)
                closeAboveFastEma = closeValue > fastEmaValue;
                if (closeAboveFastEma)
                {
                    Print($"[PRICE_BLOCK] Bar {CurrentBar}: Close ({closeValue:F4}) > Fast EMA ({fastEmaValue:F4}) - BLOCKING SHORT entry (price is above Fast EMA)");
                }
            }
            
            skipDueToGradient = emaCrossedAbove || closeAboveFastEma || ShouldSkipShortDueToGradient(gradDeg);
            skipDueToEmaCrossover = UseEmaCrossoverFilter && !EmaCrossoverFilterPasses(false);
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
            // IMPORTANT: This is called at the START of a new bar (first tick), so [1] refers to the bar that just closed
            // We want the gradient to reflect the EMA movement INCLUDING the bar that just closed ([1])
            // So we use [1] as the most recent point, [2] as the second most recent, etc.
            double sumX = 0.0;
            double sumY = 0.0;
            double sumXY = 0.0;
            double sumXX = 0.0;
            int validPoints = 0;
            for (int i = 0; i < window; i++)
            {
                double x = i;
                // y uses only completed bars: i=0 -> oldest (barsAgo = window), i=window-1 -> most recent completed (barsAgo = 1)
                // This ensures we use the bar that just closed ([1]) as the most recent point
                int barsAgo = window - i;
                if (barsAgo > CurrentBar)
                    continue; // Skip if out of range
                double y = fastEma[barsAgo];
                if (double.IsNaN(y))
                    continue; // Skip NaN values
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumXX += x * x;
                validPoints++;
            }
            
            // Need at least 2 points for regression
            if (validPoints < 2)
                return double.NaN;

            double denom = (validPoints * sumXX) - (sumX * sumX);
            if (Math.Abs(denom) < 1e-8)
                return double.NaN;

            double slope = ((validPoints * sumXY) - (sumX * sumY)) / denom;

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

            int window = Math.Max(2, lookback);
            // Use completed bars only: most recent completed is barsAgo=1
            if (CurrentBar < window)
                return double.NaN;

            int barsAgoRecent = 1;
            int barsAgoOld = window;               // lookback bars ago
            int barRecent = CurrentBar - barsAgoRecent; // most recent completed bar
            int barOld = CurrentBar - barsAgoOld;       // older bar based on lookback

            double emaRecent = fastEma[barsAgoRecent]; // EMA at barRecent
            double emaOld = fastEma[barsAgoOld];       // EMA at barOld

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

            // Use signed dy so positive angles = upward slope
            double dyPixels = yOld - yRecent;
            double angleRad = Math.Atan2(dyPixels, dx);
            double angleDeg = angleRad * (180.0 / Math.PI);

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
